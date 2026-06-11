param(
    [string] $Version = '',
    [switch] $SkipInteractiveDesktopProof
)

$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

$arguments = @('-Version', $Version)
if ($SkipInteractiveDesktopProof) {
    $arguments += '-SkipInteractiveDesktopProof'
}

& (Join-Path $PSScriptRoot '..\release-verify.ps1') @arguments
