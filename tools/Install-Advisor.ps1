param([string]$GameDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2')
$ErrorActionPreference = 'Stop'
$dll = Join-Path $GameDir 'data_sts2_windows_x86_64/sts2.dll'
if ((Get-FileHash $dll).Hash -ne 'DDCEB87905B6096DEB7BE975922E24A53992453675DF26DF545B1C46B1826C5D') {
    throw 'This advisor requires Slay the Spire 2 v0.114.0.'
}
$source = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist/AlphaDefectAdvisor'
$files = @('AlphaDefectAdvisor.dll','AlphaDefectAdvisor.json','runtime.zip','README.md','LICENSE','AlphaDefect-LICENSE.txt','AlphaDefect-NOTICE.md') |
    ForEach-Object { Get-Item -LiteralPath (Join-Path $source $_) }
$dest = Join-Path $GameDir 'mods/AlphaDefectAdvisor'
New-Item -Type Directory -Force $dest | Out-Null
foreach ($item in $files) { Copy-Item -LiteralPath $item.FullName -Destination $dest -Force }
Write-Host "Installed in $dest. Start the game from Steam and enable AlphaDefect Advisor."

