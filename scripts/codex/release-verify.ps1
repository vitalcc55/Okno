param(
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

& (Join-Path $PSScriptRoot '..\release-verify.ps1') -Version $Version
