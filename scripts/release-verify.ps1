. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
${null} = Initialize-WinBridgeExecutionContext -RepoRoot $repoRoot -DefaultRunId ("release-verify-" + (Get-Date -Format 'yyyyMMddTHHmmssfff')) -UseArtifactsRoot
Set-Location $repoRoot

& (Join-Path $PSScriptRoot 'ci.ps1')
& (Join-Path $PSScriptRoot 'test-install-surface-acceptance.ps1')
& (Join-Path $PSScriptRoot 'codex\publish-computer-use-win-plugin.ps1')
& (Join-Path $PSScriptRoot 'codex\materialize-computer-use-win-cache-copy.ps1')
& (Join-Path $PSScriptRoot 'codex\prove-computer-use-win-cache-install.ps1')
