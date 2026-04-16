param(
    [string]$RepoRoot = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)),
    [string]$RunId,
    [string]$RunRoot,
    [string]$ArtifactsRoot,
    [ValidateSet('artifacts_root', 'fallback_build_cache')]
    [string]$PreferredSourceContextName
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..\common.ps1')

$requestSemantics = Resolve-WinBridgeBundleRequestSemantics `
    -HasRunIdParameter $PSBoundParameters.ContainsKey('RunId') `
    -HasRunRootParameter $PSBoundParameters.ContainsKey('RunRoot') `
    -HasArtifactsRootParameter $PSBoundParameters.ContainsKey('ArtifactsRoot') `
    -HasPreferredSourceContextParameter $PSBoundParameters.ContainsKey('PreferredSourceContextName')

$allowAmbientState = -not $requestSemantics.HasExplicitExecutionContextInput
$effectiveExecutionContextResolveArgs = New-WinBridgeEffectiveExecutionContextResolveArgs `
    -RepoRoot $RepoRoot `
    -BoundParameters $PSBoundParameters `
    -RunId $RunId `
    -RunRoot $RunRoot `
    -ArtifactsRoot $ArtifactsRoot `
    -AllowAmbientState:$allowAmbientState
$effectiveExecutionContext = Resolve-WinBridgeEffectiveExecutionContext @effectiveExecutionContextResolveArgs

$initializeArgs = New-WinBridgeExecutionContextInitializationArgs `
    -EffectiveExecutionContext $effectiveExecutionContext `
    -HasExplicitArtifactsRoot $PSBoundParameters.ContainsKey('ArtifactsRoot') `
    -AllowAmbientState $allowAmbientState `
    -UseArtifactsRootWhenMissing:(-not $PSBoundParameters.ContainsKey('RunId') -and -not $PSBoundParameters.ContainsKey('RunRoot'))

$context = Initialize-WinBridgeExecutionContext @initializeArgs
$repoRoot = $context.RepoRoot

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,
        [Parameter(Mandatory)]
        [string] $DestinationDirectory
    )

    if (Test-Path $DestinationDirectory) {
        Remove-Item -LiteralPath $DestinationDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    Copy-Item -Path (Join-Path $SourceDirectory '*') -Destination $DestinationDirectory -Recurse -Force
}

$sourceContext = Resolve-WinBridgeTestBundleSourceContext -RepoRoot $repoRoot -ArtifactsRoot $context.ArtifactsRoot -PreferredContextName $PreferredSourceContextName
$serverDll = $sourceContext.ServerFile
$helperExe = $sourceContext.HelperFile

$bundleRoot = Join-Path $context.RunRoot 'test-bundle'
$bundleInstanceRoot = Join-Path $bundleRoot ("bundle-" + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfff'))
$serverBundleDirectory = Join-Path $bundleInstanceRoot 'server'
$helperBundleDirectory = Join-Path $bundleInstanceRoot 'helper'

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null

Copy-DirectoryContents -SourceDirectory $serverDll.Directory.FullName -DestinationDirectory $serverBundleDirectory
Copy-DirectoryContents -SourceDirectory $helperExe.Directory.FullName -DestinationDirectory $helperBundleDirectory

$manifestPath = Join-Path $bundleRoot 'okno-test-bundle.json'
$manifest = [ordered]@{
    runId                   = $context.RunId
    repoRoot                = $repoRoot
    artifactsRoot           = $context.ArtifactsRoot
    bundleRoot              = $bundleInstanceRoot
    manifestPath            = $manifestPath
    sourceContextName       = $sourceContext.Name
    sourceContextPriority   = $sourceContext.Priority
    sourceOldestWriteUtc    = if ($null -ne $sourceContext.OldestTimestampUtc) { ([DateTimeOffset]$sourceContext.OldestTimestampUtc).ToUniversalTime().ToString('o') } else { $null }
    sourceNewestWriteUtc    = if ($null -ne $sourceContext.NewestTimestampUtc) { ([DateTimeOffset]$sourceContext.NewestTimestampUtc).ToUniversalTime().ToString('o') } else { $null }
    serverSourceDirectory   = $serverDll.Directory.FullName
    helperSourceDirectory   = $helperExe.Directory.FullName
    serverDll               = (Join-Path $serverBundleDirectory 'Okno.Server.dll')
    helperExe               = (Join-Path $helperBundleDirectory 'WinBridge.SmokeWindowHost.exe')
    preparedAtUtc           = [DateTimeOffset]::UtcNow.ToString('o')
    targetFramework         = $script:WinBridgeTargetFramework
    configuration           = $script:WinBridgeBuildConfiguration
}

$manifestJson = $manifest | ConvertTo-Json -Depth 4 -Compress
Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
$manifestJson
