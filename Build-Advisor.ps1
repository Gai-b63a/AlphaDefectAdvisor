param(
    [string]$GameAssemblyDir = (Join-Path $PSScriptRoot 'runtime/game/v0.113.1'),
    [string]$BuildPython = 'python',
    [string]$Wheelhouse,
    [string]$CrtDir,
    [string]$RuntimeArchive
)
$ErrorActionPreference = 'Stop'
$upstream = Join-Path $PSScriptRoot 'upstream'
$project = Join-Path $PSScriptRoot 'advisor'
$source = Join-Path $upstream 'engine/Sts2Headless/Engine/RunSession.cs'
$gameDll = Join-Path $GameAssemblyDir 'sts2.dll'
if ((Get-FileHash $gameDll).Hash -ne '62ECBE91093E1FA862DE4329AAC865209DCB297804CAF0F0520D92D8C0B4A9C3') {
    throw 'Expected Slay the Spire 2 v0.113.1.'
}
# Compile the live adapter with a partial declaration, leaving the upstream checkout untouched.
$original = [IO.File]::ReadAllText($source)
$adapted = $original.Replace('public sealed class RunSession : IDisposable', 'public sealed partial class RunSession : IDisposable')
if (!$adapted.Contains('public sealed partial class RunSession : IDisposable')) { throw 'Unsupported upstream RunSession.' }
New-Item -Type Directory -Force "$project/obj" | Out-Null
[IO.File]::WriteAllText("$project/obj/LiveRunSession.cs",$adapted)
if (!$RuntimeArchive) {
    $RuntimeArchive = Join-Path $PSScriptRoot 'runtime/package/runtime.zip'
    $arguments = @('-I', "$PSScriptRoot/tools/build_runtime.py", $RuntimeArchive)
    if ($Wheelhouse) { $arguments += @('--wheelhouse', $Wheelhouse) }
    if ($CrtDir) { $arguments += @('--crt', $CrtDir) }
    & $BuildPython @arguments
    if ($LASTEXITCODE) { throw 'Private runtime build failed.' }
}
$hash = (Get-FileHash -LiteralPath $RuntimeArchive).Hash
[IO.File]::WriteAllText("$project/obj/PayloadInfo.cs", "namespace AlphaDefectAdvisor; internal static class PayloadInfo { internal const string Hash = `"$hash`"; }")
dotnet build "$project/AlphaDefectAdvisor.csproj" -c Release "-p:GameDir=$((Resolve-Path $GameAssemblyDir).Path)" -p:NuGetAudit=false
if ($LASTEXITCODE) { throw 'Mod build failed.' }

$out = Join-Path $PSScriptRoot 'dist/AlphaDefectAdvisor'
$files = [ordered]@{
    'AlphaDefectAdvisor.dll' = "$project/bin/Release/net9.0/AlphaDefectAdvisor.dll"
    'AlphaDefectAdvisor.json' = "$project/AlphaDefectAdvisor.json"
    'runtime.zip' = $RuntimeArchive
    'README.md' = "$PSScriptRoot/README.md"
    'LICENSE' = "$PSScriptRoot/LICENSE"
    'AlphaDefect-LICENSE.txt' = "$PSScriptRoot/third-party/AlphaDefect/LICENSE"
    'AlphaDefect-NOTICE.md' = "$PSScriptRoot/third-party/AlphaDefect/NOTICE.md"
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

$workshop = Join-Path $PSScriptRoot 'dist/workshop'
$content = New-Item -Type Directory -Force (Join-Path $workshop 'content')
foreach ($item in (Get-ChildItem -LiteralPath $content.FullName -Force)) {
    if (!$item.PSIsContainer -and $item.Name -in @('Setup-Advisor.ps1', 'companion.py')) {
        Remove-Item -LiteralPath $item.FullName
        continue
    }
    if ($item.PSIsContainer -or !$files.Contains($item.Name)) { throw "Unexpected Workshop content: $($item.Name)" }
}
foreach ($name in $files.Keys) { Copy-Item -LiteralPath "$out/$name" -Destination $content.FullName -Force }
foreach ($name in @('workshop.json','image.png')) { Copy-Item -LiteralPath "$PSScriptRoot/workshop/$name" -Destination $workshop -Force }
Write-Host "Workshop workspace: $workshop"
