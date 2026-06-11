param(
    [string] $Version = '',
    [switch] $SkipInteractiveDesktopProof
)

. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Assert-WinBridgeComputerUseWinVersionState -RepoRoot $repoRoot -RequestedVersion $Version
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -DefaultRunId ("release-verify-" + (Get-Date -Format 'yyyyMMddTHHmmssfff')) -UseArtifactsRoot
Set-Location $repoRoot

if ($SkipInteractiveDesktopProof) {
    $env:WINBRIDGE_SKIP_INTERACTIVE_DESKTOP_PROOF = '1'
    Write-Host 'Release verify is running without interactive desktop proof; this mode is intended for hosted release packaging runners only.'
}

& (Join-Path $PSScriptRoot 'ci.ps1')
& (Join-Path $PSScriptRoot 'test-install-surface-acceptance.ps1')
& (Join-Path $PSScriptRoot 'codex\publish-computer-use-win-plugin.ps1')
& (Join-Path $PSScriptRoot 'codex\materialize-computer-use-win-cache-copy.ps1')
if ($SkipInteractiveDesktopProof) {
    & (Join-Path $PSScriptRoot 'codex\prove-computer-use-win-cache-install.ps1') -SkipInteractiveDesktopProof
}
else {
    & (Join-Path $PSScriptRoot 'codex\prove-computer-use-win-cache-install.ps1')
}
