#Requires -Version 7.0
<#
.SYNOPSIS
    Upload the prepared dist/workshop workspace as a new Steam Workshop item.
.EXAMPLE
    .\Upload-FirstWorkshopItem.ps1 -Uploader 'C:\ModUploader\ModUploader.exe'
#>
[CmdletBinding()]
param(
    [string]$Uploader,
    [string]$Python = 'python'
)

$ErrorActionPreference = 'Stop'
if (-not $Uploader) {
    $besideScript = Join-Path $PSScriptRoot 'ModUploader.exe'
    if (Test-Path -LiteralPath $besideScript -PathType Leaf) {
        $Uploader = $besideScript
    } else {
        $Uploader = (Get-Command ModUploader.exe -CommandType Application -ErrorAction SilentlyContinue |
            Select-Object -First 1).Source
    }
}

if (-not $Uploader) {
    Add-Type -AssemblyName System.Windows.Forms
    $picker = [System.Windows.Forms.OpenFileDialog]::new()
    try {
        $picker.Title = 'Select the official Slay the Spire 2 ModUploader.exe'
        $picker.Filter = 'ModUploader.exe|ModUploader.exe'
        if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $Uploader = $picker.FileName
    } finally {
        $picker.Dispose()
    }
}

& (Join-Path $PSScriptRoot 'Publish-Workshop.ps1') -Mode Create -Uploader $Uploader -Python $Python
