param(
    [string] $CachePluginRoot = (Join-Path $env:USERPROFILE '.codex\plugins\cache\computer-use-win-local\computer-use-win\0.1.0'),
    [string] $OutputPath,
    [int] $TimeoutMs = 10000
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $repoRoot ".tmp\.codex\computer-use-win-cache-proof\proof-$timestamp.json"
}

function Convert-ToNormalizedPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return [System.IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Convert-ToPluginRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Path
    )

    $root = Convert-ToNormalizedPath -Path $RootPath
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    return $fullPath.Substring($root.Length).TrimStart('\').Replace('\', '/')
}

function Get-PluginFileHashMap {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    if (-not (Test-Path $RootPath -PathType Container)) {
        throw "Plugin root not found: $RootPath"
    }

    $map = @{}
    Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object {
            $relativePath = Convert-ToPluginRelativePath -RootPath $RootPath -Path $_.FullName
            -not $relativePath.StartsWith('artifacts/', [System.StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = Convert-ToPluginRelativePath -RootPath $RootPath -Path $_.FullName
            $map[$relativePath] = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
        }

    return $map
}

function Get-MapDigest {
    param(
        [Parameter(Mandatory)]
        [hashtable] $Map
    )

    $builder = [System.Text.StringBuilder]::new()
    foreach ($key in @($Map.Keys | Sort-Object)) {
        [void]$builder.Append($key)
        [void]$builder.Append('=')
        [void]$builder.Append($Map[$key])
        [void]$builder.Append("`n")
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha256.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function Assert-RuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    if (-not (Test-Path $ManifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest is missing: $ManifestPath"
    }

    $manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    if ($manifest.formatVersion -ne 1) {
        throw "Unsupported runtime bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in @($manifest.files)) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.FullName, [System.IO.Path]::GetFullPath($ManifestPath), [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName

    foreach ($file in $actualFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
        if (-not $expectedMap.ContainsKey($relativePath)) {
            throw "Published runtime bundle contains unexpected file '$relativePath'."
        }

        if ([int64]$file.Length -ne $expectedMap[$relativePath]) {
            throw "Published runtime bundle contains size drift for '$relativePath'."
        }

        $null = $expectedMap.Remove($relativePath)
    }

    if ($expectedMap.Count -gt 0) {
        throw "Published runtime bundle is incomplete. Missing: $($expectedMap.Keys -join ', ')."
    }
}

function Assert-PluginCopiesMatch {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPluginRoot,
        [Parameter(Mandatory)]
        [string] $CacheRoot
    )

    $repoMap = Get-PluginFileHashMap -RootPath $RepoPluginRoot
    $cacheMap = Get-PluginFileHashMap -RootPath $CacheRoot
    $repoKeys = @($repoMap.Keys | Sort-Object)
    $cacheKeys = @($cacheMap.Keys | Sort-Object)

    $missing = @($repoKeys | Where-Object { -not $cacheMap.ContainsKey($_) })
    $extra = @($cacheKeys | Where-Object { -not $repoMap.ContainsKey($_) })
    $changed = @($repoKeys | Where-Object { $cacheMap.ContainsKey($_) -and $cacheMap[$_] -ne $repoMap[$_] })
    if ($missing.Count -gt 0 -or $extra.Count -gt 0 -or $changed.Count -gt 0) {
        throw "Cache-installed computer-use-win plugin is stale or drifted. Missing=[$($missing -join ', ')], Extra=[$($extra -join ', ')], Changed=[$($changed -join ', ')]."
    }

    return [PSCustomObject]@{
        fileCount = $repoKeys.Count
        digest = Get-MapDigest -Map $repoMap
    }
}

function Convert-GitStatusPath {
    param(
        [Parameter(Mandatory)]
        [string] $StatusLine
    )

    if ($StatusLine.Length -le 3) {
        return ''
    }

    $path = $StatusLine.Substring(3).Trim()
    $renameSeparator = ' -> '
    $renameIndex = $path.IndexOf($renameSeparator, [System.StringComparison]::Ordinal)
    if ($renameIndex -ge 0) {
        $path = $path.Substring($renameIndex + $renameSeparator.Length)
    }

    return $path.Trim('"').Replace('\', '/')
}

function Test-RuntimeAffectingPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $normalized = $Path.Replace('\', '/')
    if ($normalized.StartsWith('src/WinBridge.Server/', [System.StringComparison]::OrdinalIgnoreCase) `
        -or $normalized.StartsWith('src/WinBridge.Runtime', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    if ($normalized.Contains('/')) {
        return $false
    }

    $rootRuntimeInputs = @(
        '.editorconfig',
        '.globalconfig',
        'Directory.Build.rsp',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'global.json',
        'WinBridge.sln',
        'NuGet.Config',
        'nuget.config'
    )
    if ($rootRuntimeInputs -contains $normalized) {
        return $true
    }

    return $normalized.EndsWith('.globalconfig', [System.StringComparison]::OrdinalIgnoreCase) `
        -or $normalized.EndsWith('.props', [System.StringComparison]::OrdinalIgnoreCase) `
        -or $normalized.EndsWith('.targets', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RuntimePublicationInputFiles {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $files = [System.Collections.Generic.List[string]]::new()
    $rootRuntimeInputs = @(
        '.editorconfig',
        '.globalconfig',
        'Directory.Build.rsp',
        'Directory.Build.props',
        'Directory.Build.targets',
        'Directory.Packages.props',
        'global.json',
        'WinBridge.sln',
        'NuGet.Config',
        'nuget.config'
    )
    foreach ($relativePath in $rootRuntimeInputs) {
        $candidate = Join-Path $RepoRoot $relativePath
        if (Test-Path $candidate -PathType Leaf) {
            $files.Add([System.IO.Path]::GetFullPath($candidate))
        }
    }

    foreach ($pattern in @('*.globalconfig', '*.props', '*.targets')) {
        foreach ($path in @(Get-ChildItem -LiteralPath $RepoRoot -File -Filter $pattern -ErrorAction SilentlyContinue)) {
            $fullPath = [System.IO.Path]::GetFullPath($path.FullName)
            if (-not $files.Contains($fullPath)) {
                $files.Add($fullPath)
            }
        }
    }

    $sourceRoot = Join-Path $RepoRoot 'src'
    if (Test-Path $sourceRoot -PathType Container) {
        Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -ErrorAction Stop |
            Where-Object {
                $_.FullName -notmatch '\\bin\\' `
                    -and $_.FullName -notmatch '\\obj\\' `
                    -and ($_.Extension -in @('.cs', '.csproj', '.props', '.targets'))
            } |
            ForEach-Object {
                $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
                if (-not $files.Contains($fullPath)) {
                    $files.Add($fullPath)
                }
            }
    }

    return @($files | Sort-Object -Unique)
}

function Get-LatestRuntimePublicationInputWriteTimeUtc {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $inputFiles = @(Get-RuntimePublicationInputFiles -RepoRoot $RepoRoot)
    if ($inputFiles.Count -eq 0) {
        throw "No runtime publication input files found under $RepoRoot."
    }

    return ($inputFiles | ForEach-Object { Get-Item -LiteralPath $_ } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
}

function Assert-RuntimeBundleFreshForPublicationInputs {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $RepoPluginRoot
    )

    $runtimeRoot = Join-Path $RepoPluginRoot 'runtime\win-x64'
    $serverExe = Join-Path $runtimeRoot 'Okno.Server.exe'
    $manifestPath = Join-Path $runtimeRoot 'okno-runtime-bundle-manifest.json'
    if (-not (Test-Path $serverExe -PathType Leaf)) {
        throw "Published computer-use-win runtime is missing: $serverExe"
    }

    Assert-RuntimeBundleMatchesManifest -RootPath $runtimeRoot -ManifestPath $manifestPath

    $latestInputWriteTimeUtc = Get-LatestRuntimePublicationInputWriteTimeUtc -RepoRoot $RepoRoot
    $serverExeWriteTimeUtc = (Get-Item -LiteralPath $serverExe).LastWriteTimeUtc
    $manifestWriteTimeUtc = (Get-Item -LiteralPath $manifestPath).LastWriteTimeUtc
    $fresh = $manifestWriteTimeUtc.AddSeconds(2) -ge $latestInputWriteTimeUtc
    if (-not $fresh) {
        throw "Published computer-use-win runtime manifest is older than runtime publication inputs. Run scripts/codex/publish-computer-use-win-plugin.ps1 and sync the cache copy before accepting install proof."
    }

    return [PSCustomObject]@{
        serverExe = [System.IO.Path]::GetFullPath($serverExe)
        serverExeWriteTimeUtc = $serverExeWriteTimeUtc.ToString('O')
        manifestPath = [System.IO.Path]::GetFullPath($manifestPath)
        manifestWriteTimeUtc = $manifestWriteTimeUtc.ToString('O')
        latestRuntimePublicationInputWriteTimeUtc = $latestInputWriteTimeUtc.ToString('O')
        runtimeBundleFreshForSource = $fresh
    }
}

function Test-Property {
    param(
        [Parameter(Mandatory)]
        [object] $Object,
        [Parameter(Mandatory)]
        [string] $Name
    )

    return @($Object.PSObject.Properties.Name) -contains $Name
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,
        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-McpRequest {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Method,
        [Parameter(Mandatory)]
        [object] $Params,
        [Parameter(Mandatory)]
        [object] $Id,
        [Parameter(Mandatory)]
        [int] $TimeoutMs
    )

    $payload = @{
        jsonrpc = '2.0'
        id = $Id
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 32 -Compress

    $Process.StandardInput.WriteLine($payload)
    $Process.StandardInput.Flush()
    $readTask = $Process.StandardOutput.ReadLineAsync()
    if (-not $readTask.Wait($TimeoutMs)) {
        throw "Timed out waiting for MCP response to '$Method'."
    }

    $line = $readTask.Result
    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "MCP host returned empty response to '$Method'."
    }

    $json = $line | ConvertFrom-Json
    if ($null -ne $json.error) {
        throw "MCP request '$Method' failed: $($json.error | ConvertTo-Json -Depth 16 -Compress)"
    }

    return [PSCustomObject]@{
        Raw = $line
        Json = $json
    }
}

function Send-McpNotification {
    param(
        [Parameter(Mandatory)]
        [System.Diagnostics.Process] $Process,
        [Parameter(Mandatory)]
        [string] $Method,
        [Parameter(Mandatory)]
        [object] $Params
    )

    $payload = @{
        jsonrpc = '2.0'
        method = $Method
        params = $Params
    } | ConvertTo-Json -Depth 32 -Compress
    $Process.StandardInput.WriteLine($payload)
    $Process.StandardInput.Flush()
}

function Convert-ToProcessArgument {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Start-CacheMcpHost {
    param(
        [Parameter(Mandatory)]
        [string] $CacheRoot
    )

    $launcherPath = Join-Path $CacheRoot 'run-computer-use-win-mcp.ps1'
    if (-not (Test-Path $launcherPath -PathType Leaf)) {
        throw "Cache-installed launcher not found: $launcherPath"
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'powershell.exe'
    $startInfo.Arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (Convert-ToProcessArgument -Value $launcherPath)
    $startInfo.WorkingDirectory = $CacheRoot
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $startInfo.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $startInfo.UseShellExecute = $false

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw 'Failed to start cache-installed computer-use-win MCP host.'
    }

    return $process
}

$repoPluginRoot = Join-Path $repoRoot 'plugins\computer-use-win'
$cacheRoot = Convert-ToNormalizedPath -Path $CachePluginRoot
$copyProof = Assert-PluginCopiesMatch -RepoPluginRoot $repoPluginRoot -CacheRoot $cacheRoot
$runtimeFreshness = Assert-RuntimeBundleFreshForPublicationInputs -RepoRoot $repoRoot -RepoPluginRoot $repoPluginRoot

$process = Start-CacheMcpHost -CacheRoot $cacheRoot
try {
    $initializeCall = Invoke-McpRequest -Process $process -Method 'initialize' -Id 1 -TimeoutMs $TimeoutMs -Params @{
        protocolVersion = '2025-11-25'
        capabilities = @{}
        clientInfo = @{
            name = 'Okno.CacheInstallProof'
            version = '0.1.0'
        }
    }
    Send-McpNotification -Process $process -Method 'notifications/initialized' -Params @{}
    $toolsCall = Invoke-McpRequest -Process $process -Method 'tools/list' -Id 2 -TimeoutMs $TimeoutMs -Params @{}
    $listAppsCall = Invoke-McpRequest -Process $process -Method 'tools/call' -Id 3 -TimeoutMs $TimeoutMs -Params @{
        name = 'list_apps'
        arguments = @{}
    }

    $expectedTools = @(
        'click',
        'drag',
        'get_app_state',
        'list_apps',
        'perform_secondary_action',
        'press_key',
        'scroll',
        'set_value',
        'type_text'
    )
    $tools = @($toolsCall.Json.result.tools)
    $toolNames = @($tools | ForEach-Object { [string]$_.name } | Sort-Object)
    Assert-Condition -Condition (@($toolNames).Count -eq $expectedTools.Count) -Message "Expected $($expectedTools.Count) public tools, got $(@($toolNames).Count): $($toolNames -join ', ')."
    foreach ($toolName in $expectedTools) {
        Assert-Condition -Condition ($toolNames -contains $toolName) -Message "Missing public tool '$toolName' in cache-installed tools/list."
    }

    $typeTextTool = @($tools | Where-Object { $_.name -eq 'type_text' })[0]
    Assert-Condition -Condition (Test-Property -Object $typeTextTool.inputSchema.properties -Name 'allowFocusedFallback') -Message 'type_text schema is missing allowFocusedFallback.'
    Assert-Condition -Condition (Test-Property -Object $typeTextTool.inputSchema.properties -Name 'observeAfter') -Message 'type_text schema is missing observeAfter.'
    Assert-Condition -Condition (Test-Property -Object $typeTextTool.inputSchema.properties -Name 'point') -Message 'type_text schema is missing point.'
    Assert-Condition -Condition (Test-Property -Object $typeTextTool.inputSchema.properties -Name 'coordinateSpace') -Message 'type_text schema is missing coordinateSpace.'
    $typeTextCoordinateSpaces = @(
        $typeTextTool.inputSchema.properties.coordinateSpace.enum |
            ForEach-Object { [string]$_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    Assert-Condition -Condition (
        $typeTextCoordinateSpaces.Count -eq 1 -and
        $typeTextCoordinateSpaces[0] -eq 'capture_pixels'
    ) -Message "type_text coordinateSpace enum must be capture_pixels-only, got: $($typeTextCoordinateSpaces -join ', ')."

    foreach ($toolName in @('click', 'press_key', 'scroll', 'drag')) {
        $tool = @($tools | Where-Object { $_.name -eq $toolName })[0]
        Assert-Condition -Condition (Test-Property -Object $tool.inputSchema.properties -Name 'observeAfter') -Message "$toolName schema is missing observeAfter."
    }

    foreach ($toolName in @('set_value', 'perform_secondary_action')) {
        $tool = @($tools | Where-Object { $_.name -eq $toolName })[0]
        Assert-Condition -Condition (-not (Test-Property -Object $tool.inputSchema.properties -Name 'observeAfter')) -Message "$toolName unexpectedly exposes observeAfter."
    }

    $listAppsStatus = [string]$listAppsCall.Json.result.structuredContent.status
    Assert-Condition -Condition ($listAppsStatus -eq 'ok') -Message "list_apps returned status '$listAppsStatus'."

    $repoHead = (& git -C $repoRoot rev-parse HEAD).Trim()
    $repoStatusShort = @(& git -C $repoRoot status --short)
    $runtimeAffectingDirtyPaths = @(
        $repoStatusShort |
            ForEach-Object { Convert-GitStatusPath -StatusLine $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-RuntimeAffectingPath -Path $_) } |
            Sort-Object -Unique
    )
    $repoWorkingTreeClean = $repoStatusShort.Count -eq 0
    $publicationAcceptanceEligible = $repoWorkingTreeClean `
        -and $runtimeAffectingDirtyPaths.Count -eq 0 `
        -and [bool]$runtimeFreshness.runtimeBundleFreshForSource
    $proof = [PSCustomObject]@{
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        repoRoot = $repoRoot
        repoHead = $repoHead
        repoWorkingTreeClean = $repoWorkingTreeClean
        repoStatusShort = $repoStatusShort
        runtimeAffectingDirtyPaths = $runtimeAffectingDirtyPaths
        runtimeBundleServerExe = $runtimeFreshness.serverExe
        runtimeBundleServerExeWriteTimeUtc = $runtimeFreshness.serverExeWriteTimeUtc
        runtimeBundleManifestPath = $runtimeFreshness.manifestPath
        runtimeBundleManifestWriteTimeUtc = $runtimeFreshness.manifestWriteTimeUtc
        runtimeBundleWriteTimeUtc = $runtimeFreshness.manifestWriteTimeUtc
        latestRuntimeSourceWriteTimeUtc = $runtimeFreshness.latestRuntimePublicationInputWriteTimeUtc
        latestRuntimePublicationInputWriteTimeUtc = $runtimeFreshness.latestRuntimePublicationInputWriteTimeUtc
        runtimeBundleFreshForSource = $runtimeFreshness.runtimeBundleFreshForSource
        runtimeBundleFreshForPublicationInputs = $runtimeFreshness.runtimeBundleFreshForSource
        publicationAcceptanceEligible = $publicationAcceptanceEligible
        repoPluginRoot = Convert-ToNormalizedPath -Path $repoPluginRoot
        cacheRoot = $cacheRoot
        shippedFileCount = $copyProof.fileCount
        shippedDigest = $copyProof.digest
        repoPluginDigest = $copyProof.digest
        cachePluginDigest = $copyProof.digest
        protocol = [string]$initializeCall.Json.result.protocolVersion
        server = [string]$initializeCall.Json.result.serverInfo.name
        tools = $toolNames
        typeTextHasAllowFocusedFallback = $true
        typeTextHasPoint = $true
        typeTextHasCoordinateSpace = $true
        typeTextCoordinateSpaceValues = $typeTextCoordinateSpaces
        selectedActionsHaveObserveAfter = $true
        semanticOnlyActionsLackObserveAfter = $true
        listAppsStatus = $listAppsStatus
        appCount = @($listAppsCall.Json.result.structuredContent.apps).Count
    }

    $outputDirectory = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
    }

    $proof | ConvertTo-Json -Depth 32 | Set-Content -Path $OutputPath -Encoding utf8
    $proof | ConvertTo-Json -Depth 32
}
finally {
    try {
        if (-not $process.HasExited) {
            $process.StandardInput.Close()
            if (-not $process.WaitForExit(2000)) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
    }
    finally {
        $process.Dispose()
    }
}
