$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

& (Join-Path $PSScriptRoot '..\test-install-surface-acceptance.ps1')
