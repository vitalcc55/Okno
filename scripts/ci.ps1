. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

Invoke-ScriptProcessStep -Description 'bootstrap step' -ScriptPath (Join-Path $PSScriptRoot 'bootstrap.ps1')
Invoke-ScriptProcessStep -Description 'build step' -ScriptPath (Join-Path $PSScriptRoot 'build.ps1')
Invoke-ScriptProcessStep -Description 'test step' -ScriptPath (Join-Path $PSScriptRoot 'test.ps1')
Invoke-ScriptProcessStep -Description 'smoke step' -ScriptPath (Join-Path $PSScriptRoot 'smoke.ps1')
Invoke-ScriptProcessStep -Description 'refresh generated docs step' -ScriptPath (Join-Path $PSScriptRoot 'refresh-generated-docs.ps1')
