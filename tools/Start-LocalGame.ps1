#Requires -Version 7.0
param(
    [string]$GameDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2',
    [switch]$RebuildRuntime,
    [switch]$SmokeTest
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root 'runtime/local-playtest'
$game = Join-Path $testRoot 'game'
$profile = Join-Path $testRoot 'profile'
$exe = Join-Path $game 'SlayTheSpire2.exe'
if (Get-Process -Name SlayTheSpire2 -ErrorAction SilentlyContinue | Where-Object Path -EQ $exe) {
    throw 'Close the local test game before rebuilding it.'
}

$buildArgs = @{ GameAssemblyDir = Join-Path $GameDir 'data_sts2_windows_x86_64' }
$archive = Join-Path $root 'runtime/package/runtime.zip'
if (!$RebuildRuntime -and (Test-Path -LiteralPath $archive)) { $buildArgs.RuntimeArchive = $archive }
& "$PSScriptRoot/Build-Advisor.ps1" @buildArgs

# Use a separate executable directory: the retail loader discovers its local mods here.
& robocopy.exe $GameDir $game /E /XD mods /XF steam_appid.txt /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
if ($LASTEXITCODE -ge 8) { throw "Copying the game failed (robocopy $LASTEXITCODE)." }
& "$PSScriptRoot/Install-Advisor.ps1" -GameDir $game

# Seed a new offline profile with only the local advisor enabled. Keep later player settings.
$settingsDir = Join-Path $profile 'SlayTheSpire2/default/1'
$settings = Join-Path $settingsDir 'settings.save'
if (!(Test-Path -LiteralPath $settings)) {
    New-Item -ItemType Directory -Force $settingsDir | Out-Null
    @{
        schema_version = 8
        mod_settings = @{
            mods_enabled = $true
            mod_list = @(@{ id = 'AlphaDefectAdvisor'; source = 'mods_directory'; is_enabled = $true })
        }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settings -Encoding utf8NoBOM
}

$log = Join-Path $testRoot ("game-{0:yyyyMMdd-HHmmssfff}.log" -f (Get-Date))
$start = [Diagnostics.ProcessStartInfo]::new($exe)
$start.UseShellExecute = $false
$start.WorkingDirectory = $game
$start.Environment['APPDATA'] = $profile
foreach ($arg in @('--force-steam=off', '--log-file', $log)) { $start.ArgumentList.Add($arg) }
if ($SmokeTest) {
    foreach ($arg in @('--headless', '--quit-after', '900')) { $start.ArgumentList.Add($arg) }
}
$process = [Diagnostics.Process]::Start($start)
Write-Host "Test game PID $($process.Id). Log: $log"
if ($SmokeTest) {
    if (!$process.WaitForExit(120000)) { $process.Kill(); throw "Test game timed out. See $log" }
    $messages = [IO.File]::ReadAllText($log)
    $dll = Join-Path $game 'mods/AlphaDefectAdvisor/AlphaDefectAdvisor.dll'
    if ($process.ExitCode -ne 0 -or !$messages.Contains('Steam initialization skipped') -or
        !$messages.Replace('/', '\').Contains("Loading assembly DLL $dll") -or
        !$messages.Contains("Finished mod initialization for 'AlphaDefect Advisor'")) {
        throw "The test game did not confirm local advisor initialization. See $log"
    }
    Write-Host 'Verified startup with Steam disabled and the local advisor DLL initialized.'
}
