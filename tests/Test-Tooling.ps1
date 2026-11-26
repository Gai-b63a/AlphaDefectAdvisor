#Requires -Version 7.0
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
foreach ($file in (Get-ChildItem "$root/tools","$root/tests" -Filter *.ps1 -File -Recurse)) {
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($file.FullName,[ref]$null,[ref]$errors)
    if ($errors) { throw "$($file.Name): $errors" }
}
$fixture = [IO.Directory]::CreateTempSubdirectory('advisor-ci-').FullName
try {
    $playtest = [IO.Directory]::CreateDirectory("$fixture/tools/playtest").FullName
    Copy-Item "$root/tools/playtest/Initialize-LocalProgress.ps1","$root/tools/playtest/unlocks-v0.114.0.json" $playtest
    & "$playtest/Initialize-LocalProgress.ps1"
    $save = "$fixture/runtime/local-playtest/profile/SlayTheSpire2/default/1/modded/profile1/saves/progress.save"
    $hash = (Get-FileHash $save).Hash
    & "$playtest/Initialize-LocalProgress.ps1"
    $progress = Get-Content -Raw $save | ConvertFrom-Json
    if ((Get-FileHash $save).Hash -ne $hash -or $progress.enable_ftues -or
        $progress.epochs.id -notcontains 'NEOW_EPOCH' -or $progress.character_stats.Count -ne 6) {
        throw 'Local profile preparation failed.'
    }

    . "$root/tools/workshop/Steam.ps1"
    $id = (Get-Content -Raw "$root/workshop/mod_id.txt").Trim()
    $logs = [IO.Directory]::CreateDirectory("$fixture/steamcmd/logs").FullName
    $preview = [IO.Path]::GetFullPath("$fixture/image.png")
    $client = "$fixture/steamcmd/steamcmd.exe"
    @("[AppID 2868840] Upload starting for workshop item $id by AppID 2868840",
      "[AppID 2868840] Uploaded new workshop preview file $preview",
      "[AppID 2868840] Upload finished for workshop item $id : OK") | Set-Content "$logs/workshop_log.txt"
    if (!(Test-UploadReceipt $client $id $preview) -or (Test-UploadReceipt $client $id "$preview.other")) {
        throw 'Workshop receipt matched the wrong submission.'
    }
} finally { Remove-Item -LiteralPath $fixture -Recurse -Force }
