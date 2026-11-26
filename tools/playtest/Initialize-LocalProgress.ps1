#Requires -Version 7.0
# Seed only the offline modded profile used by Play-Local.ps1.
param()
$ErrorActionPreference = 'Stop'
$account = Join-Path $PSScriptRoot '../../runtime/local-playtest/profile/SlayTheSpire2/default/1/modded'
$selector = Join-Path $account 'profile.save'
$id = if (Test-Path -LiteralPath $selector) { (Get-Content -LiteralPath $selector -Raw | ConvertFrom-Json).last_profile_id } else { 1 }
if ("$id" -notmatch '^[1-3]$') { throw "Unexpected local playtest profile slot: $id" }
$folder = Join-Path $account "profile$id/saves"
$file = Join-Path $folder 'progress.save'

$data = Get-Content -LiteralPath "$PSScriptRoot/unlocks-v0.114.0.json" -Raw | ConvertFrom-Json -AsHashtable
if (Test-Path -LiteralPath $file) {
    $progress = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    if ($progress.schema_version -ne 25) { throw 'Unexpected playtest progress schema for v0.114.0.' }
} else {
    # Unspecified fields take SerializableProgress defaults on load, including its generated profile ID.
    $progress = @{ schema_version=25; epochs=@(); ftue_completed=@(); character_stats=@(); enemy_stats=@() }
    foreach ($key in 'discovered_cards','discovered_relics','discovered_potions','discovered_events','discovered_acts') { $progress[$key] = @() }
}

$epochs = $data.Epochs
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
foreach ($epoch in $epochs) {
    $row = $progress.epochs | Where-Object id -CEQ $epoch | Select-Object -First 1
    if ($row) { $row.state = 'revealed' }
    else { $progress.epochs += @{ id = $epoch; state = 'revealed'; obtain_date = $now } }
}
$progress.pending_character_unlock = 'NONE.NONE'
$progress.enable_ftues = $false
$progress.ftue_completed = @(($progress.ftue_completed + @($data.Ftues)) | Sort-Object -Unique)
foreach ($entry in @{ discovered_cards='AllCards'; discovered_relics='AllRelics'; discovered_potions='AllPotions'; discovered_events='AllEvents'; discovered_acts='Acts' }.GetEnumerator()) {
    $ids = @($data[$entry.Value])
    $progress[$entry.Key] = @(($progress[$entry.Key] + $ids) | Sort-Object -Unique)
}
foreach ($monsterId in $data.Monsters) {
    $row = $progress.enemy_stats | Where-Object enemy_id -CEQ $monsterId | Select-Object -First 1
    # The game's unlock command marks each monster seen via one fight record.
    $seen = @(@{ character='CHARACTER.IRONCLAD'; wins=1; losses=0 })
    if (!$row) { $progress.enemy_stats += @{ enemy_id=$monsterId; fight_stats=$seen } }
    elseif (!$row.fight_stats.Count) { $row.fight_stats = $seen }
}
foreach ($characterId in $data.Characters) {
    $row = $progress.character_stats | Where-Object id -CEQ $characterId | Select-Object -First 1
    if (!$row) {
        $row = @{ id=$characterId; total_wins=0; total_losses=0; fastest_win_time=-1 }
        $progress.character_stats += $row
    }
    $row.max_ascension = 10
}
$progress.max_multiplayer_ascension = 10
$progress.max_blitz_ascension = 5
# A profile with zero completed runs still gets the scripted opening, even with FTUEs disabled.
if (!($progress.character_stats | Where-Object { $_.total_wins -gt 0 -or $_.total_losses -gt 0 })) {
    ($progress.character_stats | Where-Object id -CEQ 'CHARACTER.IRONCLAD').total_losses = 1
}
New-Item -ItemType Directory -Force $folder | Out-Null
$staged = Join-Path $folder "progress.$([guid]::NewGuid()).tmp"
try {
    $progress | ConvertTo-Json -Depth 64 | Set-Content -LiteralPath $staged -Encoding utf8NoBOM
    [IO.File]::Move($staged, $file, $true)
} finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged }
}
Write-Host "Prepared offline playtest profile $id with all $($epochs.Count) epochs revealed and tutorials completed."
