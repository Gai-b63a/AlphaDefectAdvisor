#Requires -Version 7.0
param([switch]$Install, [string]$GameAssemblyDir, [string]$Account)
$ErrorActionPreference = 'Stop'
$taskName = 'AlphaDefect Advisor Workshop Update'
$item = '3845626411'
$store = Join-Path $env:LOCALAPPDATA 'AlphaDefectAdvisor/publisher'
$client = Join-Path $store 'steamcmd/steamcmd.exe'
$config = Join-Path $store 'updater.json'
$state = Join-Path $store 'published-version.txt'
$hold = Join-Path $store 'update-needs-attention.txt'
$log = Join-Path $store 'update.log'
$releaseApi = 'https://api.github.com/repos/Gai-b63a/AlphaDefectAdvisor/releases/latest'
function Save-Text($file, $value) {
    [IO.File]::WriteAllText("$file.tmp", $value)
    [IO.File]::Move("$file.tmp", $file, $true)
}
if ($Install) {
    if (!(Test-Path -LiteralPath $client)) { throw 'Run under the Windows user who performed the first SteamCMD upload.' }
    if (!$GameAssemblyDir) { $GameAssemblyDir = 'E:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64' }
    if ((Get-FileHash (Join-Path $GameAssemblyDir 'sts2.dll')).Hash -ne '62ECBE91093E1FA862DE4329AAC865209DCB297804CAF0F0520D92D8C0B4A9C3') { throw 'Expected game v0.113.1. Specify -GameAssemblyDir for a different Steam library.' }
    if (!$Account) {
        $history = Join-Path $store 'steamcmd/logs/console_log.txt'
        if (Test-Path $history) {
            $last = Select-String -LiteralPath $history -Pattern "Logging in user '([a-zA-Z0-9_]+)'" | Select-Object -Last 1
            if ($last) { $Account = $last.Matches[0].Groups[1].Value }
        }
        if (!$Account) { $Account = Read-Host 'Steam account login name used for the first upload' }
    }
    if ($Account -notmatch '^[a-zA-Z0-9_]+$') { throw 'Invalid Steam account login name.' }
    $python = (Get-Command python -CommandType Application | Select-Object -First 1).Source
    & $python -I -c 'import sys;sys.exit(sys.version_info < (3,11))'
    if ($LASTEXITCODE) { throw 'Python 3.11+ is required on the publishing PC.' }
    $installedScript = Join-Path $store 'Local-WorkshopUpdate.ps1'
    if ($PSCommandPath -ne $installedScript) { Copy-Item -LiteralPath $PSCommandPath -Destination $installedScript -Force }
    Save-Text $config (@{account=$Account;game=(Resolve-Path $GameAssemblyDir).Path;python=$python} | ConvertTo-Json)
    if (!(Test-Path $state)) { Save-Text $state '0.2.0' } # Version first uploaded to this item.
    $pwsh = (Get-Process -Id $PID).Path
    $action = New-ScheduledTaskAction -Execute $pwsh -Argument "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$installedScript`""
    $who = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $principal = New-ScheduledTaskPrincipal -UserId $who -LogonType Interactive -RunLevel Limited
    $poll = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(30) -RepetitionInterval (New-TimeSpan -Minutes 30)
    $signin = New-ScheduledTaskTrigger -AtLogOn -User $who
    $settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 1)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($poll,$signin) -Principal $principal -Settings $settings -Force | Out-Null
    if (Test-Path $hold) { Remove-Item -LiteralPath $hold }
    Start-ScheduledTask -TaskName $taskName
    Write-Host "Installed. Status: $log"
    return
}

if (Test-Path $hold) { return }
$cfg = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json
$work = $null
try {
    $release = Invoke-RestMethod $releaseApi -Headers @{'User-Agent'='AlphaDefectAdvisor'} -TimeoutSec 30
    if ($release.tag_name -notmatch '^advisor-v(\d+\.\d+\.\d+)$') { throw 'Unexpected release tag.' }
    $version = $Matches[1]
    if ([version]$version -le [version]([IO.File]::ReadAllText($state).Trim())) { return }
    $work = [IO.Directory]::CreateDirectory((Join-Path $store ('build-' + [guid]::NewGuid()))).FullName
    foreach ($name in 'build-source.zip','runtime.zip') {
        $asset = @($release.assets | Where-Object name -CEQ $name)
        if ($asset.Count -ne 1 -or $asset[0].digest -notmatch '^sha256:([0-9a-f]{64})$') { throw "Missing release asset or checksum: $name" }
        $expected = $Matches[1]
        $file = Join-Path $work $name
        Invoke-WebRequest $asset[0].browser_download_url -OutFile $file -TimeoutSec 600
        if ((Get-FileHash $file).Hash -ne $expected) { throw "Incorrect checksum: $name" }
    }
    $src = Join-Path $work 'source'
    [IO.Compression.ZipFile]::ExtractToDirectory("$work/build-source.zip", $src)
    $manifest = Get-Content -Raw "$src/advisor/AlphaDefectAdvisor.json" | ConvertFrom-Json
    if ($manifest.version -cne $version -or ([IO.File]::ReadAllText("$src/workshop/mod_id.txt")).Trim() -cne $item) { throw 'Release version or target item mismatch.' }
    Push-Location -LiteralPath $src
    try {
        & ./Build-Advisor.ps1 -GameAssemblyDir $cfg.game -RuntimeArchive "$work/runtime.zip" *> "$store/last-build.log"
        & ./Publish-Workshop.ps1 -Mode Check -Python $cfg.python
        $env:STEAM_USERNAME = $cfg.account
        # An interrupted submission must be reconciled before another automatic attempt.
        Save-Text $hold "Check Steam item $item and SteamCMD login before retrying version $version."
        & $cfg.python -I tools/steam_release.py update $client
        if ($LASTEXITCODE) { throw 'SteamCMD did not confirm the update; automatic submissions paused.' }
        Save-Text $state $version
        Remove-Item -LiteralPath $hold
        Save-Text $log "$(Get-Date -Format o) Updated item $item to $version."
    } finally { Pop-Location }
} catch {
    Save-Text $log "$(Get-Date -Format o) $($_.Exception.Message)"
    throw
} finally {
    if ($work -and [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($work)) -eq [IO.Path]::GetFullPath($store) -and
        [IO.Path]::GetFileName($work) -match '^build-[0-9a-f-]{36}$') { Remove-Item -LiteralPath $work -Recurse -Force }
}
