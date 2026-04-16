$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$script:WinBridgeDefaultRunId = 'local'
$script:WinBridgeTargetFramework = 'net8.0-windows10.0.19041.0'
$script:WinBridgeBuildConfiguration = 'Debug'

function Get-RepoRoot {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptRoot
    )

    return Split-Path -Parent $ScriptRoot
}

function Resolve-WinBridgePathArgument {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetFullPath($RepoRoot)) $Path))
}

function Get-WinBridgeRunRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $RunId
    )

    return Join-Path ([System.IO.Path]::GetFullPath($RepoRoot)) ".tmp\\.codex\\runs\\$RunId"
}

function Try-Resolve-WinBridgeCanonicalRunIdFromRunRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $RunRoot
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedRunRoot = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $RunRoot
    if ([string]::IsNullOrWhiteSpace($resolvedRunRoot)) {
        return $null
    }

    $canonicalRunsBase = Join-Path $resolvedRepoRoot '.tmp\.codex\runs'
    $canonicalRunsPrefix = [System.IO.Path]::GetFullPath($canonicalRunsBase + [System.IO.Path]::DirectorySeparatorChar)
    if ($resolvedRunRoot.StartsWith($canonicalRunsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        $relative = $resolvedRunRoot.Substring($canonicalRunsPrefix.Length)
        $segments = @($relative -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($segments.Count -gt 0) {
            return $segments[0]
        }
    }

    return $null
}

function Initialize-WinBridgeExecutionContext {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $DefaultRunId = $script:WinBridgeDefaultRunId,
        [string] $RunId,
        [string] $RunRoot,
        [string] $ArtifactsRoot,
        [switch] $UseArtifactsRoot
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $env:WINBRIDGE_REPO_ROOT = $resolvedRepoRoot

    if ($PSBoundParameters.ContainsKey('RunId')) {
        if ([string]::IsNullOrWhiteSpace($RunId)) {
            Remove-Item Env:WINBRIDGE_RUN_ID -ErrorAction SilentlyContinue
        }
        else {
            $env:WINBRIDGE_RUN_ID = $RunId
        }
    }
    elseif ([string]::IsNullOrWhiteSpace($env:WINBRIDGE_RUN_ID)) {
        $env:WINBRIDGE_RUN_ID = $DefaultRunId
    }

    if ($PSBoundParameters.ContainsKey('RunRoot')) {
        if ([string]::IsNullOrWhiteSpace($RunRoot)) {
            Remove-Item Env:WINBRIDGE_RUN_ROOT -ErrorAction SilentlyContinue
        }
        else {
            $env:WINBRIDGE_RUN_ROOT = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $RunRoot
        }
    }
    elseif ([string]::IsNullOrWhiteSpace($env:WINBRIDGE_RUN_ROOT)) {
        $env:WINBRIDGE_RUN_ROOT = Get-WinBridgeRunRoot -RepoRoot $resolvedRepoRoot -RunId $env:WINBRIDGE_RUN_ID
    }

    if ($PSBoundParameters.ContainsKey('ArtifactsRoot')) {
        if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
            Remove-Item Env:WINBRIDGE_ARTIFACTS_ROOT -ErrorAction SilentlyContinue
        }
        else {
            $env:WINBRIDGE_ARTIFACTS_ROOT = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $ArtifactsRoot
        }
    }
    elseif ($UseArtifactsRoot -and [string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
        $env:WINBRIDGE_ARTIFACTS_ROOT = Join-Path $resolvedRepoRoot ".tmp\\.codex\\artifacts\\$($env:WINBRIDGE_RUN_ID)"
    }

    New-Item -ItemType Directory -Force -Path $env:WINBRIDGE_RUN_ROOT | Out-Null
    if (-not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
        New-Item -ItemType Directory -Force -Path $env:WINBRIDGE_ARTIFACTS_ROOT | Out-Null
    }

    return [PSCustomObject]@{
        RepoRoot      = $resolvedRepoRoot
        RunId         = $env:WINBRIDGE_RUN_ID
        RunRoot       = $env:WINBRIDGE_RUN_ROOT
        ArtifactsRoot = $env:WINBRIDGE_ARTIFACTS_ROOT
    }
}

function Get-WinBridgeBundleManifestPathForRunRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RunRoot
    )

    return Join-Path $RunRoot 'test-bundle\\okno-test-bundle.json'
}

function Get-WinBridgeBundleManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    $runRoot = if (-not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_RUN_ROOT)) {
        $env:WINBRIDGE_RUN_ROOT
    }
    else {
        Get-WinBridgeRunRoot -RepoRoot $RepoRoot -RunId $script:WinBridgeDefaultRunId
    }

    return Get-WinBridgeBundleManifestPathForRunRoot -RunRoot $runRoot
}

function Try-Resolve-WinBridgeRunIdFromArtifactsRoot {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $ArtifactsRoot
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $resolvedArtifactsRoot = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $ArtifactsRoot
    if ([string]::IsNullOrWhiteSpace($resolvedArtifactsRoot)) {
        return $null
    }

    $canonicalArtifactsBase = Join-Path $resolvedRepoRoot '.tmp\.codex\artifacts'
    $canonicalArtifactsPrefix = [System.IO.Path]::GetFullPath($canonicalArtifactsBase + [System.IO.Path]::DirectorySeparatorChar)
    if (-not $resolvedArtifactsRoot.StartsWith($canonicalArtifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $relative = $resolvedArtifactsRoot.Substring($canonicalArtifactsPrefix.Length)
    $segments = @($relative -split '[\\/]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($segments.Count -eq 0) {
        return $null
    }

    return $segments[0]
}

function Try-Resolve-WinBridgeArtifactsContextFromAssemblyBaseDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $AssemblyBaseDirectory,
        [string] $ExpectedProjectName = 'WinBridge.Server.IntegrationTests'
    )

    $resolvedBaseDirectory = Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path $AssemblyBaseDirectory
    if ([string]::IsNullOrWhiteSpace($resolvedBaseDirectory)) {
        return $null
    }

    $current = [System.IO.DirectoryInfo]::new($resolvedBaseDirectory)
    while ($current -ne $null -and -not [string]::Equals($current.Name, 'bin', [System.StringComparison]::OrdinalIgnoreCase)) {
        $current = $current.Parent
    }

    if ($current -eq $null -or $current.Parent -eq $null) {
        return $null
    }

    $segments = New-Object System.Collections.Generic.List[string]
    $segmentCursor = [System.IO.DirectoryInfo]::new($resolvedBaseDirectory)
    while ($segmentCursor -ne $null -and -not [string]::Equals($segmentCursor.FullName, $current.FullName, [System.StringComparison]::OrdinalIgnoreCase)) {
        $segments.Add($segmentCursor.Name)
        $segmentCursor = $segmentCursor.Parent
    }

    $segments = @($segments)
    [array]::Reverse($segments)
    if ($segments.Count -eq 0 -or -not [string]::Equals($segments[0], $ExpectedProjectName, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $artifactsRoot = $current.Parent.FullName
    $runId = Try-Resolve-WinBridgeRunIdFromArtifactsRoot -RepoRoot $RepoRoot -ArtifactsRoot $artifactsRoot
    $runRoot = if ([string]::IsNullOrWhiteSpace($runId)) {
        $null
    }
    else {
        Get-WinBridgeRunRoot -RepoRoot $RepoRoot -RunId $runId
    }

    return [PSCustomObject]@{
        ProvenanceMode = 'artifacts_root'
        ArtifactsRoot  = $artifactsRoot
        RunId          = $runId
        RunRoot        = $runRoot
    }
}

function Resolve-WinBridgeEffectiveExecutionContext {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $RunId,
        [string] $RunRoot,
        [string] $ArtifactsRoot,
        [string] $AssemblyBaseDirectory,
        [string] $DefaultRunId = $script:WinBridgeDefaultRunId,
        [bool] $AllowAmbientState = $true
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)

    $assemblyArtifactsContext = $null
    if ($PSBoundParameters.ContainsKey('AssemblyBaseDirectory') -and -not [string]::IsNullOrWhiteSpace($AssemblyBaseDirectory)) {
        $assemblyArtifactsContext = Try-Resolve-WinBridgeArtifactsContextFromAssemblyBaseDirectory -RepoRoot $resolvedRepoRoot -AssemblyBaseDirectory $AssemblyBaseDirectory
    }

    $effectiveArtifactsRoot = $null
    if ($PSBoundParameters.ContainsKey('ArtifactsRoot')) {
        $effectiveArtifactsRoot = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $ArtifactsRoot
    }
    elseif ($null -ne $assemblyArtifactsContext) {
        $effectiveArtifactsRoot = $assemblyArtifactsContext.ArtifactsRoot
    }
    elseif ($AllowAmbientState -and -not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
        $effectiveArtifactsRoot = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $env:WINBRIDGE_ARTIFACTS_ROOT
    }

    $artifactsRootContext = $null
    if (-not [string]::IsNullOrWhiteSpace($effectiveArtifactsRoot)) {
        $artifactsDerivedRunId = Try-Resolve-WinBridgeRunIdFromArtifactsRoot -RepoRoot $resolvedRepoRoot -ArtifactsRoot $effectiveArtifactsRoot
        $artifactsRootContext = [PSCustomObject]@{
            ProvenanceMode = 'artifacts_root'
            ArtifactsRoot  = $effectiveArtifactsRoot
            RunId          = $artifactsDerivedRunId
            RunRoot        = if ([string]::IsNullOrWhiteSpace($artifactsDerivedRunId)) { $null } else { Get-WinBridgeRunRoot -RepoRoot $resolvedRepoRoot -RunId $artifactsDerivedRunId }
        }
    }

    $explicitRunRoot = $null
    $explicitRunRootContext = $null
    if ($PSBoundParameters.ContainsKey('RunRoot') -and -not [string]::IsNullOrWhiteSpace($RunRoot)) {
        $explicitRunRoot = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $RunRoot
        $explicitRunRootContext = [PSCustomObject]@{
            ProvenanceMode = 'run_root'
            RunRoot        = $explicitRunRoot
            RunId          = Try-Resolve-WinBridgeCanonicalRunIdFromRunRoot -RepoRoot $resolvedRepoRoot -RunRoot $explicitRunRoot
        }
    }

    Validate-WinBridgeExplicitExecutionContext `
        -RepoRoot $resolvedRepoRoot `
        -BoundParameters $PSBoundParameters `
        -RunId $RunId `
        -RunRootContext $explicitRunRootContext `
        -ArtifactsRootContext $artifactsRootContext `
        -AssemblyArtifactsContext $assemblyArtifactsContext `
        -AssemblyBaseDirectory $AssemblyBaseDirectory

    $effectiveRunId = if ($PSBoundParameters.ContainsKey('RunId') -and -not [string]::IsNullOrWhiteSpace($RunId)) {
        $RunId
    }
    elseif ($null -ne $assemblyArtifactsContext -and -not [string]::IsNullOrWhiteSpace($assemblyArtifactsContext.RunId)) {
        [string]$assemblyArtifactsContext.RunId
    }
    elseif ($null -ne $artifactsRootContext -and -not [string]::IsNullOrWhiteSpace($artifactsRootContext.RunId)) {
        [string]$artifactsRootContext.RunId
    }
    elseif ($null -ne $explicitRunRootContext -and -not [string]::IsNullOrWhiteSpace($explicitRunRootContext.RunId)) {
        [string]$explicitRunRootContext.RunId
    }
    elseif ($AllowAmbientState -and -not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_RUN_ID)) {
        $env:WINBRIDGE_RUN_ID
    }
    else {
        $DefaultRunId
    }

    $effectiveRunRoot = if ($PSBoundParameters.ContainsKey('RunRoot') -and -not [string]::IsNullOrWhiteSpace($RunRoot)) {
        $explicitRunRoot
    }
    elseif ($null -ne $assemblyArtifactsContext -and -not [string]::IsNullOrWhiteSpace($assemblyArtifactsContext.RunRoot)) {
        Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path ([string]$assemblyArtifactsContext.RunRoot)
    }
    elseif ($null -ne $artifactsRootContext -and -not [string]::IsNullOrWhiteSpace($artifactsRootContext.RunRoot)) {
        Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path ([string]$artifactsRootContext.RunRoot)
    }
    elseif ($AllowAmbientState -and -not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_RUN_ROOT)) {
        Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $env:WINBRIDGE_RUN_ROOT
    }
    else {
        Get-WinBridgeRunRoot -RepoRoot $resolvedRepoRoot -RunId $effectiveRunId
    }

    return [PSCustomObject]@{
        RepoRoot               = $resolvedRepoRoot
        RunId                  = $effectiveRunId
        RunRoot                = $effectiveRunRoot
        ArtifactsRoot          = $effectiveArtifactsRoot
        AssemblyArtifactsContext = $assemblyArtifactsContext
        ArtifactsRootContext   = $artifactsRootContext
    }
}

function Resolve-WinBridgeBundleRequestSemantics {
    param(
        [bool] $HasManifestPathParameter = $false,
        [bool] $HasRunIdParameter = $false,
        [bool] $HasRunRootParameter = $false,
        [bool] $HasArtifactsRootParameter = $false,
        [bool] $HasAssemblyBaseDirectoryParameter = $false,
        [bool] $HasPreferredSourceContextParameter = $false
    )

    $hasExplicitContextIdentityInput =
        $HasRunIdParameter -or
        $HasRunRootParameter -or
        $HasArtifactsRootParameter

    $hasExplicitContextProvenanceInput = $HasAssemblyBaseDirectoryParameter
    $hasExplicitExecutionContextInput = $hasExplicitContextIdentityInput -or $hasExplicitContextProvenanceInput
    $hasContextLocalSelectionInput = $HasPreferredSourceContextParameter
    $hasExplicitBundleResolutionRequest = $hasExplicitExecutionContextInput -or $hasContextLocalSelectionInput

    return [PSCustomObject]@{
        HasManifestPathParameter            = $HasManifestPathParameter
        HasExplicitContextIdentityInput     = $hasExplicitContextIdentityInput
        HasExplicitContextProvenanceInput   = $hasExplicitContextProvenanceInput
        HasExplicitExecutionContextInput    = $hasExplicitExecutionContextInput
        HasContextLocalSelectionInput       = $hasContextLocalSelectionInput
        HasExplicitBundleResolutionRequest  = $hasExplicitBundleResolutionRequest
    }
}

function Validate-WinBridgeExplicitBundleRequest {
    param(
        [Parameter(Mandatory)]
        [psobject] $RequestSemantics,
        [Parameter(Mandatory)]
        [hashtable] $BoundParameters
    )

    if (-not $RequestSemantics.HasManifestPathParameter) {
        return
    }

    $conflictingFields = [System.Collections.Generic.List[string]]::new()
    foreach ($fieldName in @('RunId', 'RunRoot', 'ArtifactsRoot', 'AssemblyBaseDirectory', 'PreferredSourceContextName')) {
        if ($BoundParameters.ContainsKey($fieldName)) {
            $conflictingFields.Add($fieldName)
        }
    }

    if ($conflictingFields.Count -eq 0) {
        return
    }

    throw "Explicit manifest request is incompatible with additional explicit request fields: $([string]::Join(', ', @($conflictingFields)))."
}

function New-WinBridgeEffectiveExecutionContextResolveArgs {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [hashtable] $BoundParameters,
        [string] $RunId,
        [string] $RunRoot,
        [string] $ArtifactsRoot,
        [string] $AssemblyBaseDirectory,
        [bool] $AllowAmbientState = $true
    )

    $resolveArgs = @{
        RepoRoot          = $RepoRoot
        AllowAmbientState = $AllowAmbientState
    }

    if ($null -ne $BoundParameters -and $BoundParameters.ContainsKey('RunId')) {
        $resolveArgs.RunId = $RunId
    }

    if ($null -ne $BoundParameters -and $BoundParameters.ContainsKey('RunRoot')) {
        $resolveArgs.RunRoot = $RunRoot
    }

    if ($null -ne $BoundParameters -and $BoundParameters.ContainsKey('ArtifactsRoot')) {
        $resolveArgs.ArtifactsRoot = $ArtifactsRoot
    }

    if ($null -ne $BoundParameters -and $BoundParameters.ContainsKey('AssemblyBaseDirectory')) {
        $resolveArgs.AssemblyBaseDirectory = $AssemblyBaseDirectory
    }

    return $resolveArgs
}

function Validate-WinBridgeExplicitExecutionContext {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [hashtable] $BoundParameters,
        [string] $RunId,
        [psobject] $RunRootContext,
        [psobject] $ArtifactsRootContext,
        [psobject] $AssemblyArtifactsContext,
        [string] $AssemblyBaseDirectory
    )

    $derivedSources = [System.Collections.Generic.List[object]]::new()

    if ($BoundParameters.ContainsKey('RunId') -and -not [string]::IsNullOrWhiteSpace($RunId)) {
        $derivedSources.Add([PSCustomObject]@{
                Name         = 'RunId'
                Requested    = $RunId
                DerivedRunId = $RunId
            })
    }

    if ($BoundParameters.ContainsKey('RunRoot') -and $null -ne $RunRootContext -and -not [string]::IsNullOrWhiteSpace([string]$RunRootContext.RunId)) {
        $derivedSources.Add([PSCustomObject]@{
                Name         = 'RunRoot'
                Requested    = [string]$RunRootContext.RunRoot
                DerivedRunId = [string]$RunRootContext.RunId
            })
    }

    if ($BoundParameters.ContainsKey('ArtifactsRoot') -and $null -ne $ArtifactsRootContext -and -not [string]::IsNullOrWhiteSpace([string]$ArtifactsRootContext.RunId)) {
        $derivedSources.Add([PSCustomObject]@{
                Name         = 'ArtifactsRoot'
                Requested    = [string]$ArtifactsRootContext.ArtifactsRoot
                DerivedRunId = [string]$ArtifactsRootContext.RunId
            })
    }

    if ($BoundParameters.ContainsKey('AssemblyBaseDirectory') -and $null -ne $AssemblyArtifactsContext -and -not [string]::IsNullOrWhiteSpace([string]$AssemblyArtifactsContext.RunId)) {
        $resolvedAssemblyBaseDirectory = Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path $AssemblyBaseDirectory
        $derivedSources.Add([PSCustomObject]@{
                Name         = 'AssemblyBaseDirectory'
                Requested    = [string]$resolvedAssemblyBaseDirectory
                DerivedRunId = [string]$AssemblyArtifactsContext.RunId
            })
    }

    $derivedSources = @($derivedSources)
    if ($derivedSources.Count -le 1) {
        return
    }

    $uniqueRunIds = @(
        $derivedSources |
            Group-Object -Property DerivedRunId |
            Select-Object -ExpandProperty Name)
    if ($uniqueRunIds.Count -le 1) {
        return
    }

    $details = @(
        $derivedSources |
            ForEach-Object {
                "{0}='{1}' -> runId '{2}'" -f $_.Name, $_.Requested, $_.DerivedRunId
            })
    throw "Explicit execution context is internally inconsistent. $($details -join '; ')."
}

function New-WinBridgeExecutionContextInitializationArgs {
    param(
        [Parameter(Mandatory)]
        [psobject] $EffectiveExecutionContext,
        [bool] $HasExplicitArtifactsRoot = $false,
        [bool] $AllowAmbientState = $true,
        [bool] $UseArtifactsRootWhenMissing = $false
    )

    $initializeArgs = @{
        RepoRoot = $EffectiveExecutionContext.RepoRoot
        RunId    = $EffectiveExecutionContext.RunId
        RunRoot  = $EffectiveExecutionContext.RunRoot
    }

    if ($HasExplicitArtifactsRoot -or -not [string]::IsNullOrWhiteSpace($EffectiveExecutionContext.ArtifactsRoot) -or -not $AllowAmbientState) {
        $initializeArgs.ArtifactsRoot = $EffectiveExecutionContext.ArtifactsRoot
    }
    elseif ($UseArtifactsRootWhenMissing) {
        $initializeArgs.UseArtifactsRoot = $true
    }

    return $initializeArgs
}

function Get-WinBridgeRuntimeSourceContexts {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $ArtifactsRoot
    )

    $contexts = @()
    if (-not [string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
        $contexts += [PSCustomObject]@{
            Name       = 'artifacts_root'
            Priority   = 0
            ServerRoot = Join-Path $ArtifactsRoot 'bin\WinBridge.Server'
            HelperRoot = Join-Path $ArtifactsRoot 'bin\WinBridge.SmokeWindowHost'
        }
    }

    $contexts += [PSCustomObject]@{
        Name       = 'fallback_build_cache'
        Priority   = 1
        ServerRoot = Join-Path $RepoRoot 'src\WinBridge.Server\bin'
        HelperRoot = Join-Path $RepoRoot 'tests\WinBridge.SmokeWindowHost\bin'
    }

    return $contexts
}

function Resolve-WinBridgeTestBundleSourceContext {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $ArtifactsRoot,
        [string] $PreferredContextName
    )

    $candidateContexts = @(
        Get-WinBridgeRuntimeSourceContexts -RepoRoot $RepoRoot -ArtifactsRoot $ArtifactsRoot |
            ForEach-Object {
                $serverFile = Get-LatestFile -Path $_.ServerRoot -Filter 'Okno.Server.dll'
                $helperFile = Get-LatestFile -Path $_.HelperRoot -Filter 'WinBridge.SmokeWindowHost.exe'
                $timestamps = @()
                if ($null -ne $serverFile) {
                    $timestamps += $serverFile.LastWriteTimeUtc
                }

                if ($null -ne $helperFile) {
                    $timestamps += $helperFile.LastWriteTimeUtc
                }

                $oldestTimestampUtc = $null
                $newestTimestampUtc = $null
                if ($timestamps.Count -gt 0) {
                    $orderedTimestamps = @($timestamps | Sort-Object)
                    $oldestTimestampUtc = $orderedTimestamps[0]
                    $newestTimestampUtc = $orderedTimestamps[-1]
                }

                [PSCustomObject]@{
                    Name               = $_.Name
                    Priority           = $_.Priority
                    ServerRoot         = $_.ServerRoot
                    HelperRoot         = $_.HelperRoot
                    ServerFile         = $serverFile
                    HelperFile         = $helperFile
                    OldestTimestampUtc = $oldestTimestampUtc
                    NewestTimestampUtc = $newestTimestampUtc
                }
            })

    $completeContexts = @(
        $candidateContexts |
            Where-Object {
                $null -ne $_.ServerFile -and $null -ne $_.HelperFile
            } |
            Sort-Object `
                @{ Expression = 'OldestTimestampUtc'; Descending = $true }, `
                @{ Expression = 'NewestTimestampUtc'; Descending = $true }, `
                @{ Expression = 'Priority'; Descending = $false }, `
                @{ Expression = 'Name'; Descending = $false })

    if ($completeContexts.Count -eq 0) {
        $searchedRoots = @(
            $candidateContexts | ForEach-Object {
                "'$($_.ServerRoot)' + '$($_.HelperRoot)'"
            })
        throw "Не удалось подобрать согласованный source context для staged bundle. Проверены пары roots: $($searchedRoots -join ', '). Сначала выполни scripts/build.ps1 или подготовь актуальные fallback outputs."
    }

    if (-not [string]::IsNullOrWhiteSpace($PreferredContextName)) {
        $preferredContext = @(
            $completeContexts |
                Where-Object { [string]::Equals($_.Name, $PreferredContextName, [System.StringComparison]::OrdinalIgnoreCase) } |
                Select-Object -First 1)
        if ($preferredContext.Count -eq 0) {
            $availableContexts = @($completeContexts | ForEach-Object { $_.Name })
            $availableContextsJoined = [string]::Join("', '", @($availableContexts))
            throw ('Ne udalos podobrat requested source context ''{0}''. Dostupny: ''{1}''.' -f $PreferredContextName, $availableContextsJoined)
        }

        return $preferredContext[0]
    }

    return $completeContexts[0]
}

function ConvertTo-WinBridgeUnixMilliseconds {
    param(
        $Value
    )

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [DateTimeOffset]) {
        return [long]$Value.ToUniversalTime().ToUnixTimeMilliseconds()
    }

    if ($Value -is [DateTime]) {
        return [long]([DateTimeOffset]$Value.ToUniversalTime()).ToUnixTimeMilliseconds()
    }

    if ($Value -is [long] -or $Value -is [int]) {
        return [long]$Value
    }

    $stringValue = [string]$Value
    if ([string]::IsNullOrWhiteSpace($stringValue)) {
        return $null
    }

    $dateMatch = [System.Text.RegularExpressions.Regex]::Match($stringValue, '^/Date\((?<ms>-?\d+)(?:[-+]\d+)?\)/$')
    if ($dateMatch.Success) {
        return [long]$dateMatch.Groups['ms'].Value
    }

    try {
        return [long]([DateTimeOffset]::Parse($stringValue, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)).ToUniversalTime().ToUnixTimeMilliseconds()
    }
    catch {
        return $null
    }
}

function Read-WinBridgeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $ManifestPath,
        [switch] $ThrowOnInvalid
    )

    if ([string]::IsNullOrWhiteSpace($ManifestPath) -or -not (Test-Path $ManifestPath)) {
        if ($ThrowOnInvalid) {
            throw ('Manifest staged test bundle ''{0}'' ne naiden.' -f $ManifestPath)
        }

        return $null
    }

    try {
        $manifest = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        if ($ThrowOnInvalid) {
            throw ('Manifest staged test bundle ''{0}'' soderzhit nevalidnyi JSON.' -f $ManifestPath)
        }

        return $null
    }

    $serverDll = [string]$manifest.serverDll
    $helperExe = [string]$manifest.helperExe
    if ([string]::IsNullOrWhiteSpace($serverDll) -or [string]::IsNullOrWhiteSpace($helperExe)) {
        if ($ThrowOnInvalid) {
            throw ('Manifest staged test bundle ''{0}'' ne soderzhit serverDll i helperExe.' -f $ManifestPath)
        }

        return $null
    }

    if (-not (Test-Path $serverDll) -or -not (Test-Path $helperExe)) {
        if ($ThrowOnInvalid) {
            throw ('Staged bundle manifest ''{0}'' ukazyvaet na otsutstvuyushchie puti server/helper.' -f $ManifestPath)
        }

        return $null
    }

    return [PSCustomObject]@{
        ManifestPath           = [System.IO.Path]::GetFullPath($ManifestPath)
        ServerDll              = [System.IO.Path]::GetFullPath($serverDll)
        HelperExe              = [System.IO.Path]::GetFullPath($helperExe)
        RunId                  = [string]$manifest.runId
        ArtifactsRoot          = [string]$manifest.artifactsRoot
        SourceContextName      = [string]$manifest.sourceContextName
        ServerSourceDirectory  = [string]$manifest.serverSourceDirectory
        HelperSourceDirectory  = [string]$manifest.helperSourceDirectory
        SourceOldestWriteUtc   = $manifest.sourceOldestWriteUtc
        SourceNewestWriteUtc   = $manifest.sourceNewestWriteUtc
        SourceOldestWriteUnixMs = ConvertTo-WinBridgeUnixMilliseconds -Value $manifest.sourceOldestWriteUtc
        SourceNewestWriteUnixMs = ConvertTo-WinBridgeUnixMilliseconds -Value $manifest.sourceNewestWriteUtc
        RawManifest            = $manifest
    }
}

function Test-WinBridgeBundleManifestMatchesContext {
    param(
        [Parameter(Mandatory)]
        [psobject] $Manifest,
        [string] $RunId,
        [string] $ArtifactsRoot,
        [string] $PreferredSourceContextName,
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($RunId) -and -not [string]::Equals([string]$Manifest.RunId, $RunId, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    if (
        -not [string]::IsNullOrWhiteSpace($PreferredSourceContextName) -and
        -not [string]::Equals([string]$Manifest.SourceContextName, $PreferredSourceContextName, [System.StringComparison]::OrdinalIgnoreCase)
    ) {
        return $false
    }

    $expectedArtifactsRoot = Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path $ArtifactsRoot
    $manifestArtifactsRoot = Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path ([string]$Manifest.ArtifactsRoot)
    if ([string]::IsNullOrWhiteSpace($expectedArtifactsRoot)) {
        return [string]::IsNullOrWhiteSpace($manifestArtifactsRoot)
    }

    return [string]::Equals($manifestArtifactsRoot, $expectedArtifactsRoot, [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-WinBridgeBundleManifestReusableForCurrentSourceContext {
    param(
        [Parameter(Mandatory)]
        [psobject] $Manifest,
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $RunId,
        [string] $ArtifactsRoot,
        [string] $PreferredSourceContextName
    )

    if (-not (Test-WinBridgeBundleManifestMatchesContext -Manifest $Manifest -RunId $RunId -ArtifactsRoot $ArtifactsRoot -PreferredSourceContextName $PreferredSourceContextName -RepoRoot $RepoRoot)) {
        return $false
    }

    $currentSourceContext = $null
    try {
        $currentSourceContext = Resolve-WinBridgeTestBundleSourceContext -RepoRoot $RepoRoot -ArtifactsRoot $ArtifactsRoot -PreferredContextName $PreferredSourceContextName
    }
    catch {
        return $false
    }

    if ($null -eq $currentSourceContext -or $null -eq $currentSourceContext.ServerFile -or $null -eq $currentSourceContext.HelperFile) {
        return $false
    }

    $currentServerSourceDirectory = [System.IO.Path]::GetFullPath($currentSourceContext.ServerFile.Directory.FullName)
    $currentHelperSourceDirectory = [System.IO.Path]::GetFullPath($currentSourceContext.HelperFile.Directory.FullName)
    if (-not [string]::Equals((Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path ([string]$Manifest.ServerSourceDirectory)), $currentServerSourceDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    if (-not [string]::Equals((Resolve-WinBridgePathArgument -RepoRoot $RepoRoot -Path ([string]$Manifest.HelperSourceDirectory)), $currentHelperSourceDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $currentOldestWriteUnixMs = ConvertTo-WinBridgeUnixMilliseconds -Value $currentSourceContext.OldestTimestampUtc
    $currentNewestWriteUnixMs = ConvertTo-WinBridgeUnixMilliseconds -Value $currentSourceContext.NewestTimestampUtc
    if ($Manifest.SourceOldestWriteUnixMs -ne $currentOldestWriteUnixMs) {
        return $false
    }

    if ($Manifest.SourceNewestWriteUnixMs -ne $currentNewestWriteUnixMs) {
        return $false
    }

    return $true
}

function Resolve-WinBridgeVerificationContext {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $TestProjectName = 'WinBridge.Server.IntegrationTests'
    )

    $effectiveContext = Resolve-WinBridgeEffectiveExecutionContext -RepoRoot $RepoRoot -ArtifactsRoot $env:WINBRIDGE_ARTIFACTS_ROOT
    $resolvedRepoRoot = $effectiveContext.RepoRoot
    $runId = $effectiveContext.RunId
    $runRoot = $effectiveContext.RunRoot
    $artifactsRoot = $effectiveContext.ArtifactsRoot

    $artifactsTestAssembly = $null
    if (-not [string]::IsNullOrWhiteSpace($artifactsRoot)) {
        $artifactsTestAssembly = Get-LatestFile -Path (Join-Path $artifactsRoot "bin\\$TestProjectName") -Filter "$TestProjectName.dll"
    }

    if ($null -ne $artifactsTestAssembly) {
        return [PSCustomObject]@{
            RunId                     = $runId
            RunRoot                   = $runRoot
            RequestedArtifactsRoot    = $artifactsRoot
            EffectiveArtifactsRoot    = $artifactsRoot
            TestAssemblyProvenance    = 'artifacts_root'
            BundleSourceContextName   = 'artifacts_root'
            DotnetTestArguments       = @('test', 'WinBridge.sln', '--no-build', '--artifacts-path', $artifactsRoot)
            IntegrationTestAssembly   = $artifactsTestAssembly.FullName
            BundleManifestPath        = Get-WinBridgeBundleManifestPathForRunRoot -RunRoot $runRoot
        }
    }

    $defaultTestAssembly = Get-LatestFile -Path (Join-Path $resolvedRepoRoot "tests\\$TestProjectName\\bin") -Filter "$TestProjectName.dll"
    return [PSCustomObject]@{
        RunId                     = $runId
        RunRoot                   = $runRoot
        RequestedArtifactsRoot    = $artifactsRoot
        EffectiveArtifactsRoot    = $null
        TestAssemblyProvenance    = 'fallback_build_cache'
        BundleSourceContextName   = 'fallback_build_cache'
        DotnetTestArguments       = @('test', 'WinBridge.sln', '--no-build')
        IntegrationTestAssembly   = if ($null -ne $defaultTestAssembly) { $defaultTestAssembly.FullName } else { $null }
        BundleManifestPath        = Get-WinBridgeBundleManifestPathForRunRoot -RunRoot $runRoot
    }
}

function Resolve-WinBridgeBundleOverrideSource {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $ManifestPath,
        [bool] $HasManifestPathParameter = $false,
        [bool] $HasRunIdParameter = $false,
        [bool] $HasRunRootParameter = $false,
        [bool] $HasArtifactsRootParameter = $false,
        [bool] $HasAssemblyBaseDirectoryParameter = $false,
        [bool] $HasPreferredSourceContextParameter = $false,
        [psobject] $RequestSemantics
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $explicitManifestItem = Get-Item Env:WINBRIDGE_TEST_BUNDLE_MANIFEST -ErrorAction SilentlyContinue
    $explicitServerItem = Get-Item Env:WINBRIDGE_SERVER_DLL -ErrorAction SilentlyContinue
    $explicitHelperItem = Get-Item Env:WINBRIDGE_SMOKE_HELPER_EXE -ErrorAction SilentlyContinue
    $effectiveRequestSemantics = if ($null -ne $RequestSemantics) {
        $RequestSemantics
    }
    else {
        Resolve-WinBridgeBundleRequestSemantics `
            -HasManifestPathParameter $HasManifestPathParameter `
            -HasRunIdParameter $HasRunIdParameter `
            -HasRunRootParameter $HasRunRootParameter `
            -HasArtifactsRootParameter $HasArtifactsRootParameter `
            -HasAssemblyBaseDirectoryParameter $HasAssemblyBaseDirectoryParameter `
            -HasPreferredSourceContextParameter $HasPreferredSourceContextParameter
    }

    if ($HasManifestPathParameter) {
        return [PSCustomObject]@{
            Kind         = 'explicit_manifest_parameter'
            ManifestPath = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path $ManifestPath
        }
    }

    if ($effectiveRequestSemantics.HasExplicitBundleResolutionRequest) {
        return [PSCustomObject]@{
            Kind         = 'explicit_resolution_request'
            ManifestPath = $null
        }
    }

    if ($null -ne $explicitManifestItem) {
        return [PSCustomObject]@{
            Kind         = 'explicit_manifest_environment'
            ManifestPath = Resolve-WinBridgePathArgument -RepoRoot $resolvedRepoRoot -Path ([string]$explicitManifestItem.Value)
        }
    }

    $hasExplicitServerOverride = $null -ne $explicitServerItem
    $hasExplicitHelperOverride = $null -ne $explicitHelperItem
    if ($hasExplicitServerOverride -or $hasExplicitHelperOverride) {
        return [PSCustomObject]@{
            Kind       = if ($hasExplicitServerOverride -and $hasExplicitHelperOverride) { 'ambient_server_helper_environment' } else { 'partial_server_helper_environment' }
            ServerDll  = if ($hasExplicitServerOverride) { [string]$explicitServerItem.Value } else { $null }
            HelperExe  = if ($hasExplicitHelperOverride) { [string]$explicitHelperItem.Value } else { $null }
        }
    }

    return [PSCustomObject]@{
        Kind         = 'auto_resolution'
        ManifestPath = $null
    }
}

function Resolve-WinBridgeBundleResolution {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $ManifestPath,
        [string] $AssemblyBaseDirectory,
        [string] $RunId,
        [string] $RunRoot,
        [string] $ArtifactsRoot,
        [string] $PreferredSourceContextName,
        [switch] $AutoPrepare
    )

    $resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
    $requestSemantics = Resolve-WinBridgeBundleRequestSemantics `
        -HasManifestPathParameter $PSBoundParameters.ContainsKey('ManifestPath') `
        -HasRunIdParameter $PSBoundParameters.ContainsKey('RunId') `
        -HasRunRootParameter $PSBoundParameters.ContainsKey('RunRoot') `
        -HasArtifactsRootParameter $PSBoundParameters.ContainsKey('ArtifactsRoot') `
        -HasAssemblyBaseDirectoryParameter $PSBoundParameters.ContainsKey('AssemblyBaseDirectory') `
        -HasPreferredSourceContextParameter $PSBoundParameters.ContainsKey('PreferredSourceContextName')
    Validate-WinBridgeExplicitBundleRequest -RequestSemantics $requestSemantics -BoundParameters $PSBoundParameters

    $overrideSource = Resolve-WinBridgeBundleOverrideSource `
        -RepoRoot $resolvedRepoRoot `
        -ManifestPath $ManifestPath `
        -HasManifestPathParameter $PSBoundParameters.ContainsKey('ManifestPath') `
        -HasRunIdParameter $PSBoundParameters.ContainsKey('RunId') `
        -HasRunRootParameter $PSBoundParameters.ContainsKey('RunRoot') `
        -HasArtifactsRootParameter $PSBoundParameters.ContainsKey('ArtifactsRoot') `
        -HasAssemblyBaseDirectoryParameter $PSBoundParameters.ContainsKey('AssemblyBaseDirectory') `
        -HasPreferredSourceContextParameter $PSBoundParameters.ContainsKey('PreferredSourceContextName') `
        -RequestSemantics $requestSemantics

    if ($overrideSource.Kind -eq 'partial_server_helper_environment') {
        throw 'Explicit runtime override must provide both WINBRIDGE_SERVER_DLL and WINBRIDGE_SMOKE_HELPER_EXE.'
    }

    if ($overrideSource.Kind -eq 'ambient_server_helper_environment') {
        $explicitServerDll = [string]$overrideSource.ServerDll
        $explicitHelperExe = [string]$overrideSource.HelperExe
        if ([string]::IsNullOrWhiteSpace($explicitServerDll) -or -not (Test-Path $explicitServerDll)) {
            throw "Explicit runtime override 'WINBRIDGE_SERVER_DLL' points to missing path '$explicitServerDll'."
        }

        if ([string]::IsNullOrWhiteSpace($explicitHelperExe) -or -not (Test-Path $explicitHelperExe)) {
            throw "Explicit runtime override 'WINBRIDGE_SMOKE_HELPER_EXE' points to missing path '$explicitHelperExe'."
        }

        return [PSCustomObject]@{
            ResolutionMode           = 'explicit_server_helper'
            ManifestPath             = $null
            ServerDll                = [System.IO.Path]::GetFullPath($explicitServerDll)
            HelperExe                = [System.IO.Path]::GetFullPath($explicitHelperExe)
            RunId                    = $null
            RunRoot                  = $null
            ArtifactsRoot            = $null
            RequestedArtifactsRoot   = $null
            PreferredSourceContext   = $null
            AutoPrepared             = $false
        }
    }

    if ($overrideSource.Kind -eq 'explicit_manifest_parameter' -or $overrideSource.Kind -eq 'explicit_manifest_environment') {
        $manifest = Read-WinBridgeBundleManifest -ManifestPath $overrideSource.ManifestPath -ThrowOnInvalid
        return [PSCustomObject]@{
            ResolutionMode           = 'explicit_manifest'
            ManifestPath             = $manifest.ManifestPath
            ServerDll                = $manifest.ServerDll
            HelperExe                = $manifest.HelperExe
            RunId                    = $manifest.RunId
            RunRoot                  = $null
            ArtifactsRoot            = $manifest.ArtifactsRoot
            RequestedArtifactsRoot   = $null
            PreferredSourceContext   = $manifest.SourceContextName
            AutoPrepared             = $false
        }
    }

    $effectivePreferredSourceContextName = $PreferredSourceContextName
    if ([string]::IsNullOrWhiteSpace($effectivePreferredSourceContextName) -and $PSBoundParameters.ContainsKey('AssemblyBaseDirectory')) {
        $effectiveAssemblyContext = Try-Resolve-WinBridgeArtifactsContextFromAssemblyBaseDirectory -RepoRoot $resolvedRepoRoot -AssemblyBaseDirectory $AssemblyBaseDirectory
        $effectivePreferredSourceContextName = if ($null -ne $effectiveAssemblyContext) {
            'artifacts_root'
        }
        else {
            'fallback_build_cache'
        }
    }

    $allowAmbientExecutionContext = -not $requestSemantics.HasExplicitExecutionContextInput
    $effectiveExecutionContextResolveArgs = New-WinBridgeEffectiveExecutionContextResolveArgs `
        -RepoRoot $resolvedRepoRoot `
        -BoundParameters $PSBoundParameters `
        -RunId $RunId `
        -RunRoot $RunRoot `
        -ArtifactsRoot $ArtifactsRoot `
        -AssemblyBaseDirectory $AssemblyBaseDirectory `
        -AllowAmbientState:$allowAmbientExecutionContext
    $effectiveExecutionContext = Resolve-WinBridgeEffectiveExecutionContext @effectiveExecutionContextResolveArgs

    $initializeArgs = New-WinBridgeExecutionContextInitializationArgs -EffectiveExecutionContext $effectiveExecutionContext -HasExplicitArtifactsRoot $PSBoundParameters.ContainsKey('ArtifactsRoot') -AllowAmbientState $allowAmbientExecutionContext
    $context = Initialize-WinBridgeExecutionContext @initializeArgs
    $defaultManifestPath = Get-WinBridgeBundleManifestPathForRunRoot -RunRoot $context.RunRoot
    $existingManifest = Read-WinBridgeBundleManifest -ManifestPath $defaultManifestPath
    if (
        $null -ne $existingManifest -and
        (Test-WinBridgeBundleManifestReusableForCurrentSourceContext -Manifest $existingManifest -RunId $context.RunId -ArtifactsRoot $context.ArtifactsRoot -PreferredSourceContextName $effectivePreferredSourceContextName -RepoRoot $resolvedRepoRoot)
    ) {
        return [PSCustomObject]@{
            ResolutionMode           = 'default_manifest'
            ManifestPath             = $existingManifest.ManifestPath
            ServerDll                = $existingManifest.ServerDll
            HelperExe                = $existingManifest.HelperExe
            RunId                    = $context.RunId
            RunRoot                  = $context.RunRoot
            ArtifactsRoot            = $context.ArtifactsRoot
            RequestedArtifactsRoot   = $effectiveExecutionContext.ArtifactsRoot
            PreferredSourceContext   = if (-not [string]::IsNullOrWhiteSpace($effectivePreferredSourceContextName)) { $effectivePreferredSourceContextName } else { $existingManifest.SourceContextName }
            AutoPrepared             = $false
        }
    }

    if (-not $AutoPrepare) {
        throw "Manifest staged test bundle '$defaultManifestPath' not found."
    }

    $bundleArgs = @{
        RepoRoot = $resolvedRepoRoot
        RunId    = $context.RunId
        RunRoot  = $context.RunRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($context.ArtifactsRoot)) {
        $bundleArgs.ArtifactsRoot = $context.ArtifactsRoot
    }
    if (-not [string]::IsNullOrWhiteSpace($effectivePreferredSourceContextName)) {
        $bundleArgs.PreferredSourceContextName = $effectivePreferredSourceContextName
    }

    $preparedManifest = Use-OknoTestBundle @bundleArgs
    return [PSCustomObject]@{
        ResolutionMode           = 'auto_prepared_manifest'
        ManifestPath             = [string]$preparedManifest.manifestPath
        ServerDll                = [string]$preparedManifest.serverDll
        HelperExe                = [string]$preparedManifest.helperExe
        RunId                    = $context.RunId
        RunRoot                  = $context.RunRoot
        ArtifactsRoot            = $context.ArtifactsRoot
        RequestedArtifactsRoot   = $effectiveExecutionContext.ArtifactsRoot
        PreferredSourceContext   = if (-not [string]::IsNullOrWhiteSpace($effectivePreferredSourceContextName)) { $effectivePreferredSourceContextName } else { [string]$preparedManifest.sourceContextName }
        AutoPrepared             = $true
    }
}

function Invoke-WinBridgeSolutionBuild {
    param(
        [string] $Description = 'dotnet build'
    )

    $arguments = @('build', 'WinBridge.sln', '--no-restore')
    if (-not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
        $arguments += @('--artifacts-path', $env:WINBRIDGE_ARTIFACTS_ROOT)
    }

    Invoke-NativeCommand -Description $Description -Command {
        dotnet @arguments
    }
}

function Invoke-WinBridgeSolutionRestore {
    param(
        [string] $Description = 'dotnet restore'
    )

    $arguments = @('restore', 'WinBridge.sln')
    if (-not [string]::IsNullOrWhiteSpace($env:WINBRIDGE_ARTIFACTS_ROOT)) {
        $arguments += @('--artifacts-path', $env:WINBRIDGE_ARTIFACTS_ROOT)
    }

    Invoke-NativeCommand -Description $Description -Command {
        dotnet @arguments
    }
}

function Use-OknoTestBundle {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [string] $RunId,
        [string] $RunRoot,
        [string] $ArtifactsRoot,
        [string] $PreferredSourceContextName
    )

    $prepareScript = Join-Path $RepoRoot 'scripts\\codex\\prepare-okno-test-bundle.ps1'
    $prepareInvokeArgs = @{
        RepoRoot = $RepoRoot
    }
    if ($PSBoundParameters.ContainsKey('RunId') -and -not [string]::IsNullOrWhiteSpace($RunId)) {
        $prepareInvokeArgs.RunId = $RunId
    }

    if ($PSBoundParameters.ContainsKey('RunRoot') -and -not [string]::IsNullOrWhiteSpace($RunRoot)) {
        $prepareInvokeArgs.RunRoot = $RunRoot
    }

    if ($PSBoundParameters.ContainsKey('ArtifactsRoot') -and -not [string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
        $prepareInvokeArgs.ArtifactsRoot = $ArtifactsRoot
    }

    if ($PSBoundParameters.ContainsKey('PreferredSourceContextName') -and -not [string]::IsNullOrWhiteSpace($PreferredSourceContextName)) {
        $prepareInvokeArgs.PreferredSourceContextName = $PreferredSourceContextName
    }

    $manifest = & $prepareScript @prepareInvokeArgs | ConvertFrom-Json
    $env:WINBRIDGE_TEST_BUNDLE_MANIFEST = [string]$manifest.manifestPath
    $env:WINBRIDGE_SERVER_DLL = [string]$manifest.serverDll
    $env:WINBRIDGE_SMOKE_HELPER_EXE = [string]$manifest.helperExe
    return $manifest
}

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    $global:LASTEXITCODE = 0
    & $Command

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [scriptblock] $Command
    )

    $global:LASTEXITCODE = 0
    & $Command

    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "$Description failed with exit code $exitCode."
    }
}

function Invoke-ScriptProcessStep {
    param(
        [Parameter(Mandatory)]
        [string] $Description,
        [Parameter(Mandatory)]
        [string] $ScriptPath
    )

    Invoke-NativeCommand -Description $Description -Command {
        powershell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $ScriptPath
    }
}

function Get-LatestFile {
    param(
        [Parameter(Mandatory)]
        [string] $Path,
        [Parameter(Mandatory)]
        [string] $Filter
    )

    if (-not (Test-Path $Path)) {
        return $null
    }

    return Get-ChildItem -Path $Path -Filter $Filter -Recurse |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1
}

function Get-SmokeNarrativeSteps {
    return @(
        'init',
        'tools/list',
        'health',
        '`windows.launch_process` dry-run/live helper launch',
        'list monitors',
        'list windows',
        'attach',
        'session_state',
        'uia_snapshot',
        'capture',
        'helper minimize/activate/window capture',
        'wait active/exists/gone/text/focus/visual',
        'terminal `windows.open_target` dry-run/live folder proof',
        'open_target and launch artifact/event cross-check'
    )
}

function Get-SmokeCoverageNarrative {
    return [string]::Join(' -> ', @(Get-SmokeNarrativeSteps))
}

function Get-SmokeCommandPurpose {
    return 'stdio MCP smoke with staged run bundle, owned helper scenario, terminal `windows.open_target` folder proof and artifact report'
}

function Get-SmokeCommandLiteral {
    return '`powershell -ExecutionPolicy Bypass -File scripts/smoke.ps1`'
}

function Get-SmokeUnownedProbeBasePath {
    return Join-Path ([System.IO.Path]::GetTempPath()) 'WinBridge\SmokeUnowned'
}

function Remove-StaleSmokeUnownedProbeRoots {
    param(
        [Parameter(Mandatory)]
        [string] $CurrentRunId,
        [int] $MaxAgeDays = 1
    )

    $basePath = Get-SmokeUnownedProbeBasePath
    if (-not (Test-Path $basePath)) {
        return
    }

    $cutoff = (Get-Date).AddDays(-$MaxAgeDays)
    foreach ($directory in @(Get-ChildItem -Path $basePath -Directory -ErrorAction SilentlyContinue)) {
        if ($directory.Name -eq $CurrentRunId) {
            continue
        }

        if ($directory.LastWriteTime -ge $cutoff) {
            continue
        }

        try {
            Remove-Item -LiteralPath $directory.FullName -Force -Recurse -ErrorAction Stop
        }
        catch {
        }
    }
}

function New-SmokeUnownedProbeFolder {
    param(
        [Parameter(Mandatory)]
        [string] $RunId,
        [Parameter(Mandatory)]
        [string] $FolderName
    )

    $root = Join-Path (Get-SmokeUnownedProbeBasePath) $RunId
    $folderPath = Join-Path $root $FolderName
    New-Item -ItemType Directory -Force -Path $folderPath | Out-Null
    return $folderPath
}
