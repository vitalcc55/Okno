$script:ComputerUseWinRuntimeBundleManifestFileName = 'okno-runtime-bundle-manifest.json'

function Get-ComputerUseWinRuntimeBundleManifestPath {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Join-Path $RootPath $script:ComputerUseWinRuntimeBundleManifestFileName
}

function Remove-DirectoryIfExists {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Write-StderrDiagnostic {
    param(
        [Parameter(Mandatory)]
        [string] $Message
    )

    [Console]::Error.WriteLine($Message)
}

function Invoke-NativeCommandToStderr {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Command,
        [Parameter(Mandatory)]
        [string] $FailureMessage
    )

    $output = & $Command 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        foreach ($line in $output) {
            if ($null -ne $line) {
                Write-StderrDiagnostic -Message ([string]$line)
            }
        }

        throw "$FailureMessage ExitCode=$exitCode."
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)]
        [string] $SourceRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot
    )

    $normalizedSourceRoot = [System.IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File | ForEach-Object {
        $sourcePath = [System.IO.Path]::GetFullPath($_.FullName)
        $relativePath = $sourcePath.Substring($normalizedSourceRoot.Length).TrimStart('\')
        $destinationPath = Join-Path $DestinationRoot $relativePath
        $destinationDirectory = Split-Path -Parent $destinationPath
        if (-not (Test-Path $destinationDirectory -PathType Container)) {
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        }

        Copy-Item -LiteralPath $_.FullName -Destination $destinationPath -Force
    }
}

function New-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $files = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinRuntimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName |
        ForEach-Object {
            $fullPath = [System.IO.Path]::GetFullPath($_.FullName)
            $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
            [pscustomobject]@{
                path = $relativePath
                size = [int64]$_.Length
            }
        }

    [pscustomobject]@{
        formatVersion = 1
        files = @($files)
    }
}

function Write-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifest = New-ComputerUseWinRuntimeBundleManifest -RootPath $RootPath
    $manifestPath = Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath
    $manifest | ConvertTo-Json -Depth 6 -Compress | Set-Content -Path $manifestPath -Encoding UTF8
}

function Read-ComputerUseWinRuntimeBundleManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    $manifestPath = Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath
    if (-not (Test-Path $manifestPath -PathType Leaf)) {
        throw "Runtime bundle manifest '$manifestPath' is missing."
    }

    Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
}

function Test-ComputerUseWinRuntimeBundleManifestExists {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    Test-Path (Get-ComputerUseWinRuntimeBundleManifestPath -RootPath $RootPath) -PathType Leaf
}

function Assert-ComputerUseWinRuntimeBundleMatchesManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    $manifest = Read-ComputerUseWinRuntimeBundleManifest -RootPath $RootPath
    if ($manifest.formatVersion -ne 1) {
        throw "$Description uses unsupported runtime bundle manifest version '$($manifest.formatVersion)'."
    }

    $expectedEntries = @($manifest.files)
    $expectedMap = New-Object 'System.Collections.Generic.Dictionary[string,long]' ([System.StringComparer]::Ordinal)
    foreach ($entry in $expectedEntries) {
        $expectedMap[[string]$entry.path] = [int64]$entry.size
    }

    $normalizedRootPath = [System.IO.Path]::GetFullPath($RootPath).TrimEnd('\')
    $actualFiles = Get-ChildItem -LiteralPath $RootPath -Recurse -File |
        Where-Object { -not [string]::Equals($_.Name, $script:ComputerUseWinRuntimeBundleManifestFileName, [System.StringComparison]::OrdinalIgnoreCase) } |
        Sort-Object FullName

    foreach ($file in $actualFiles) {
        $fullPath = [System.IO.Path]::GetFullPath($file.FullName)
        $relativePath = $fullPath.Substring($normalizedRootPath.Length).TrimStart('\')
        if (-not $expectedMap.ContainsKey($relativePath)) {
            throw "$Description contains unexpected file '$relativePath'."
        }

        if ([int64]$file.Length -ne $expectedMap[$relativePath]) {
            throw "$Description contains size drift for '$relativePath'."
        }

        $null = $expectedMap.Remove($relativePath)
    }

    if ($expectedMap.Count -gt 0) {
        throw "$Description is incomplete. Missing: $($expectedMap.Keys -join ', ')."
    }
}

function Assert-ComputerUseWinRuntimeBundleHasExistingManifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $Description
    )

    if (Test-ComputerUseWinRuntimeBundleManifestExists -RootPath $RootPath) {
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $RootPath -Description $Description
        return
    }

    throw "$Description cannot be accepted as a runtime bundle because '$script:ComputerUseWinRuntimeBundleManifestFileName' is missing."
}

function Publish-ComputerUseWinRuntimeBundleToDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $DestinationRoot,
        [string] $Rid = 'win-x64',
        [string] $PublishSourceRoot = ''
    )

    $serverProjectPath = Join-Path $RepoRoot 'src\WinBridge.Server\WinBridge.Server.csproj'
    Remove-DirectoryIfExists -Path $DestinationRoot
    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null

    if (-not [string]::IsNullOrWhiteSpace($PublishSourceRoot)) {
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $PublishSourceRoot -Description "Test publish source runtime bundle '$PublishSourceRoot'"
        Copy-DirectoryContents -SourceRoot $PublishSourceRoot -DestinationRoot $DestinationRoot
        Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description "Published computer-use-win runtime bundle '$DestinationRoot'"
        return
    }

    Invoke-NativeCommandToStderr -FailureMessage "dotnet publish failed for computer-use-win runtime bundle." -Command {
        & dotnet publish $serverProjectPath `
            --configuration Release `
            --runtime $Rid `
            --self-contained true `
            -p:UseAppHost=true `
            -p:UiaWorkerPublishSelfContained=true `
            -p:PublishSingleFile=false `
            -p:PublishTrimmed=false `
            --output $DestinationRoot
    }

    Write-ComputerUseWinRuntimeBundleManifest -RootPath $DestinationRoot
    Assert-ComputerUseWinRuntimeBundleMatchesManifest -RootPath $DestinationRoot -Description "Published computer-use-win runtime bundle '$DestinationRoot'"
}
