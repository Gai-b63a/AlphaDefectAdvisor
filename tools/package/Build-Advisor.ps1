param(
    [string]$GameAssemblyDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64',
    [Parameter(Mandatory)][string]$RuntimeArchive,
    [string]$UpstreamDir
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath("$PSScriptRoot/../..")
$upstream = if ($UpstreamDir) { $UpstreamDir } else { Join-Path $root 'upstream' }
$project = Join-Path $root 'advisor'
$source = Join-Path $upstream 'engine/Sts2Headless/Engine/RunSession.cs'
$gameDll = Join-Path $GameAssemblyDir 'sts2.dll'
if ((Get-FileHash $gameDll).Hash -ne 'DDCEB87905B6096DEB7BE975922E24A53992453675DF26DF545B1C46B1826C5D') {
    throw 'Expected Slay the Spire 2 v0.114.0.'
}
# Compile the live adapter with a partial declaration, leaving the upstream checkout untouched.
$original = [IO.File]::ReadAllText($source)
$adapted = $original.Replace('public sealed class RunSession : IDisposable', 'public sealed partial class RunSession : IDisposable')
if (!$adapted.Contains('public sealed partial class RunSession : IDisposable')) { throw 'Unsupported upstream RunSession.' }
New-Item -Type Directory -Force "$project/obj" | Out-Null
[IO.File]::WriteAllText("$project/obj/LiveRunSession.cs",$adapted)
# Adapt the pinned observation encoder to the retail v0.114.0 API.
$obs = Join-Path $upstream 'engine/Sts2Headless/Engine/Obs'
$fool = [IO.File]::ReadAllText("$obs/Characters/Fool.cs")
$fool = $fool.Replace('arrows.Count(a => a.Status == ArrowStatus.Destroyed)', '0')
$fool = $fool.Replace('MegaCrit.Sts2.Core.Commands.MelodyCmd.MelodyCardsPlayedThisTurn(me)',
    'CombatManager.Instance.History.CardPlaysStarted.Count(e => e.HappenedThisTurn(cs) && e.CardPlay.Player == me && IsMelody(e.CardPlay.Card))')
[IO.File]::WriteAllText("$project/obj/LiveFool.cs", $fool)
$gains = [IO.File]::ReadAllText("$obs/Gains26.cs").Replace('FreeCardPower', 'EnsemblePower')
[IO.File]::WriteAllText("$project/obj/LiveGains26.cs", $gains)
$hash = (Get-FileHash -LiteralPath $RuntimeArchive).Hash
[IO.File]::WriteAllText("$project/obj/PayloadInfo.cs", "namespace AlphaDefectAdvisor; internal static class PayloadInfo { internal const string Hash = `"$hash`"; }")
dotnet build "$project/AlphaDefectAdvisor.csproj" -c Release "-p:GameDir=$((Resolve-Path $GameAssemblyDir).Path)" "-p:UpstreamDir=$upstream" -p:NuGetAudit=false
if ($LASTEXITCODE) { throw 'Mod build failed.' }

$out = Join-Path $root 'dist/AlphaDefectAdvisor'
$files = [ordered]@{
    'AlphaDefectAdvisor.dll' = "$project/bin/Release/net9.0/AlphaDefectAdvisor.dll"
    'AlphaDefectAdvisor.json' = "$project/AlphaDefectAdvisor.json"
    'runtime.zip' = $RuntimeArchive
    'README.md' = "$root/README.md"
    'LICENSE' = "$root/LICENSE"
    'AlphaDefect-LICENSE.txt' = "$root/third-party/AlphaDefect/LICENSE"
    'AlphaDefect-NOTICE.md' = "$root/third-party/AlphaDefect/NOTICE.md"
}
New-Item -Type Directory -Force $out | Out-Null
foreach ($item in (Get-ChildItem -LiteralPath $out -Force)) {
    if (!$item.PSIsContainer -and $item.Name -in @('Setup-Advisor.ps1', 'companion.py')) {
        Remove-Item -LiteralPath $item.FullName
        continue
    }
    if ($item.PSIsContainer -or !$files.Contains($item.Name)) { throw "Unexpected item in distribution directory: $($item.Name)" }
}
foreach ($name in $files.Keys) { Copy-Item -LiteralPath $files[$name] -Destination "$out/$name" -Force }
Write-Host "Mod files: $out"

$workshop = Join-Path $root 'dist/workshop'
$content = New-Item -Type Directory -Force (Join-Path $workshop 'content')
foreach ($item in (Get-ChildItem -LiteralPath $content.FullName -Force)) {
    if (!$item.PSIsContainer -and $item.Name -in @('Setup-Advisor.ps1', 'companion.py')) {
        Remove-Item -LiteralPath $item.FullName
        continue
    }
    if ($item.PSIsContainer -or !$files.Contains($item.Name)) { throw "Unexpected Workshop content: $($item.Name)" }
}
foreach ($name in $files.Keys) { Copy-Item -LiteralPath "$out/$name" -Destination $content.FullName -Force }
foreach ($name in @('workshop.json','image.png')) { Copy-Item -LiteralPath "$root/workshop/$name" -Destination $workshop -Force }
. "$PSScriptRoot/ChangeLog.ps1"
$version = (Get-Content -LiteralPath "$project/AlphaDefectAdvisor.json" -Raw | ConvertFrom-Json).version
Set-WorkshopNotes $workshop $version (Read-ChangeLog "$root/CHANGELOG.md")
Write-Host "Workshop workspace: $workshop"
