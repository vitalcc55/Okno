$ErrorActionPreference = 'Stop'
if (Get-Variable -Name PSStyle -ErrorAction Ignore) {
    $PSStyle.OutputRendering = 'PlainText'
}

& (Join-Path $PSScriptRoot 'publish-computer-use-win-plugin-core.ps1')
