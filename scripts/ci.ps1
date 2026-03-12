. (Join-Path $PSScriptRoot 'common.ps1')

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
Set-Location $repoRoot

Invoke-Step -Description 'bootstrap step' -Command {
    & (Join-Path $PSScriptRoot 'bootstrap.ps1')
}
Invoke-Step -Description 'build step' -Command {
    & (Join-Path $PSScriptRoot 'build.ps1')
}
Invoke-Step -Description 'test step' -Command {
    & (Join-Path $PSScriptRoot 'test.ps1')
}
Invoke-Step -Description 'smoke step' -Command {
    & (Join-Path $PSScriptRoot 'smoke.ps1')
}
Invoke-Step -Description 'refresh generated docs step' -Command {
    & (Join-Path $PSScriptRoot 'refresh-generated-docs.ps1')
}
