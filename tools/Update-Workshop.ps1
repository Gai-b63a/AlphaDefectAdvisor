#Requires -Version 7.0
param([switch]$VerifyOnly, [string]$GameAssemblyDir, [string]$Account)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/package/Dependencies.ps1"
. "$PSScriptRoot/workshop/Steam.ps1"
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
if ($VerifyOnly) {
    $store = [IO.Path]::GetFullPath("$PSScriptRoot/../runtime/workshop-test")
    $config = Join-Path $store 'updater.json'
    $log = Join-Path $store 'update.log'
}
[IO.Directory]::CreateDirectory($store) | Out-Null
$lock = [IO.File]::Open((Join-Path $store 'manual-update.lock'), 'OpenOrCreate', 'Write', 'None')
try {
    if (!$VerifyOnly -and (Test-Path $hold)) {
        $pending = [IO.File]::ReadAllText($hold).Trim()
        $version = if ($pending.StartsWith('{')) { ($pending | ConvertFrom-Json).version }
            elseif ($pending -match 'retrying version (\d+\.\d+\.\d+)\.$') { $Matches[1] }
        if (!$version -or $version -notmatch '^\d+\.\d+\.\d+$') { throw "Check the pending submission in $hold." }
        if (!(Test-PreviousUpload $store $client $hold $version $item)) { throw "Previous submission unconfirmed. Check the Workshop change history and $hold before retrying." }
        if (!(Test-Path $state) -or [version]$version -gt [version]([IO.File]::ReadAllText($state).Trim())) { Save-Text $state $version }
        Save-Text $log "$(Get-Date -Format o) Confirmed previous upload of $version to item $item from SteamCMD's Workshop log."
        Remove-Item -LiteralPath $hold
        Write-Host "Confirmed previous upload of $version. No files sent."
        return
    }
    if (!(Test-Path $config) -or $Account -or $GameAssemblyDir) {
        if (!$GameAssemblyDir) { $GameAssemblyDir = Join-Path (Find-GameDirectory) 'data_sts2_windows_x86_64' }
        if ((Get-FileHash (Join-Path $GameAssemblyDir 'sts2.dll')).Hash -ne 'DDCEB87905B6096DEB7BE975922E24A53992453675DF26DF545B1C46B1826C5D') { throw 'Expected game v0.114.0. Specify -GameAssemblyDir for a different Steam library.' }
        if (!$Account -and !$VerifyOnly) {
            $history = Join-Path $store 'steamcmd/logs/console_log.txt'
            if (Test-Path $history) {
                $last = Select-String -LiteralPath $history -Pattern "Logging in user '([a-zA-Z0-9_]+)'" | Select-Object -Last 1
                if ($last) { $Account = $last.Matches[0].Groups[1].Value }
            }
            if (!$Account) { $Account = Read-Host 'Steam account login name used for the first upload' }
        }
        if (!$VerifyOnly -and $Account -notmatch '^[a-zA-Z0-9_]+$') { throw 'Invalid Steam account login name.' }
        Save-Text $config (@{account=$Account;game=(Resolve-Path $GameAssemblyDir).Path} | ConvertTo-Json)
    }
    if (!$VerifyOnly -and !(Test-Path $state)) { Save-Text $state '0.2.0' }
    $cfg = Get-Content -Raw -LiteralPath $config | ConvertFrom-Json
    $work = $null
    try {
        $release = Invoke-RestMethod $releaseApi -Headers @{'User-Agent'='AlphaDefectAdvisor'} -TimeoutSec 30
        if ($release.tag_name -notmatch '^advisor-v(\d+\.\d+\.\d+)$') { throw 'Unexpected release tag.' }
        $version = $Matches[1]
        if (!$VerifyOnly -and [version]$version -le [version]([IO.File]::ReadAllText($state).Trim())) { Write-Host 'Already published.'; return }
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
            $build = @('tools/package/Build-Advisor.ps1','tools/Build-Advisor.ps1','Build-Advisor.ps1') |
                ForEach-Object { Join-Path $src $_ } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
            if (!$build) { throw 'Release contains no supported build script.' }
            Invoke-PrivateBuild $build @('-GameAssemblyDir',$cfg.game,'-RuntimeArchive',"$work/runtime.zip") "$store/last-build.log"
            & "$PSScriptRoot/package/Test-WorkshopPackage.ps1" -Workspace "$src/dist/workshop"
            if ($VerifyOnly) { Write-Host "Verified release $version for item $item."; return }
            if (!(Test-Path -LiteralPath $client)) {
                $zip = Join-Path $work 'steamcmd.zip'
                Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile $zip
                [IO.Compression.ZipFile]::ExtractToDirectory($zip,(Split-Path $client),$true)
            }
            # An interrupted submission must be reconciled before retrying.
            Save-Text $hold (@{version=$version; item=$item; preview=[IO.Path]::GetFullPath("$src/dist/workshop/image.png")} | ConvertTo-Json)
            Send-WorkshopUpdate $client $cfg.account "$src/dist/workshop" $item
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
} finally { $lock.Dispose() }
