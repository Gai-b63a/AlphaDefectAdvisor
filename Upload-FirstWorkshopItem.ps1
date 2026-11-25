#Requires -Version 7.0
<#
.SYNOPSIS
    Upload the prepared dist/workshop workspace as a new Steam Workshop item.
.EXAMPLE
    .\Upload-FirstWorkshopItem.ps1
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

$temporary = $null
try {
    if (-not $Uploader) {
        $temporary = Join-Path ([IO.Path]::GetTempPath()) ("AlphaDefectUploader-" + [guid]::NewGuid())
        [IO.Directory]::CreateDirectory($temporary) | Out-Null
        $archive = Join-Path $temporary 'ModUploader-win-x64.zip'
        $uri = 'https://github.com/megacrit/sts2-mod-uploader/releases/download/v0.2.0/ModUploader-win-x64.zip'
        Invoke-WebRequest -Uri $uri -OutFile $archive -TimeoutSec 120
        $expected = '2b55c19cc5932235ca9dbd663ca07d04e5d5a0018402303199f9b4ec8ca06578'
        if ((Get-FileHash -LiteralPath $archive).Hash -ne $expected) {
            throw 'Official ModUploader archive checksum mismatch.'
        }
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $temporary 'app')
        $matches = @(Get-ChildItem -LiteralPath (Join-Path $temporary 'app') -File -Recurse -Filter 'ModUploader.exe')
        if ($matches.Count -ne 1) { throw 'Expected one ModUploader.exe in the official archive.' }
        $Uploader = $matches[0].FullName
    }
    & (Join-Path $PSScriptRoot 'Publish-Workshop.ps1') -Mode Create -Uploader $Uploader -Python $Python
} finally {
    if ($temporary) {
        $log = Get-ChildItem -LiteralPath $temporary -Recurse -File -Filter 'mod-uploader.log' | Select-Object -First 1
        if ($log) { Copy-Item -LiteralPath $log.FullName -Destination (Join-Path $PSScriptRoot 'dist/mod-uploader.log') -Force }
        $resolved = [IO.Path]::GetFullPath($temporary)
        if ((Split-Path -Parent $resolved) -eq [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) -and
            (Split-Path -Leaf $resolved) -match '^AlphaDefectUploader-[0-9a-f-]{36}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
}
