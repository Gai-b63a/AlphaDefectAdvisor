param(
    [ValidateSet('Check','Create','Update')][string]$Mode = 'Check',
    [string]$Workspace = (Join-Path $PSScriptRoot 'dist/workshop'),
    [string]$Uploader = 'ModUploader.exe',
    [string]$Python = 'python',
    [string]$ItemId,
    [string]$StateDir = (Join-Path $PSScriptRoot 'workshop')
)
$ErrorActionPreference = 'Stop'
$Workspace = (Resolve-Path -LiteralPath $Workspace).Path
$StateDir = (Resolve-Path -LiteralPath $StateDir).Path
& $Python -I "$PSScriptRoot/tools/check_workshop.py" --workspace $Workspace
if ($LASTEXITCODE) { throw 'Workshop package validation failed.' }
$content = Join-Path $Workspace 'content'
$dll = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes("$content/AlphaDefectAdvisor.dll"))
$hash = $dll.GetType('AlphaDefectAdvisor.PayloadInfo', $true).GetField('Hash','Static,NonPublic').GetRawConstantValue()
if ((Get-FileHash -LiteralPath "$content/runtime.zip").Hash -ne $hash) { throw 'Runtime does not match the mod DLL.' }
if ($Mode -eq 'Check') { Write-Host 'Package and DLL verified.'; return }

$exe = (Get-Command $Uploader -CommandType Application -ErrorAction Stop).Source
$saved = Join-Path $StateDir 'mod_id.txt'
$local = Join-Path $Workspace 'mod_id.txt'
$pending = Join-Path $StateDir '.create-pending'
$mutex = [Threading.Mutex]::new($false, 'Local\AlphaDefectAdvisorWorkshopUpload')
$locked = $false
try {
    try { $locked = $mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'Another Workshop upload is in progress.' }
    $ids = @(if ($PSBoundParameters.ContainsKey('ItemId')) { $ItemId }
        foreach ($path in @($saved,$local)) {
            if (Test-Path -LiteralPath $path) { ([IO.File]::ReadAllText($path)).Trim() }
        })
    foreach ($id in $ids) {
        if ($id -notmatch '^[1-9][0-9]*$' -or ![uint64]::TryParse($id, [ref]([uint64]0))) { throw 'Invalid Workshop ID.' }
    }
    $ids = @($ids | Select-Object -Unique)
    if ($ids.Count -gt 1) { throw 'Conflicting Workshop IDs. Verify the intended Steam item.' }
    if ($Mode -eq 'Create') {
        if ($ids.Count -or (Test-Path -LiteralPath $pending)) { throw 'Creation already recorded or pending. Check the existing Steam item.' }
        [IO.File]::WriteAllText($pending, [DateTime]::UtcNow.ToString('o'))
    } else {
        if ($ids.Count -ne 1) { throw 'Update requires a saved Workshop ID or -ItemId.' }
        [IO.File]::WriteAllText($local, $ids[0])
    }
    Push-Location -LiteralPath (Split-Path -Parent $exe)
    try {
        & $exe upload -w $Workspace
        if ($LASTEXITCODE) { throw "ModUploader exit code $LASTEXITCODE. Check mod-uploader.log." }
    } finally { Pop-Location }
    $published = ([IO.File]::ReadAllText($local)).Trim()
    if ($published -notmatch '^[1-9][0-9]*$' -or ![uint64]::TryParse($published, [ref]([uint64]0)) -or ($ids.Count -and $ids[0] -ne $published)) {
        throw 'Uploader returned an unexpected Workshop ID.'
    }
    [IO.File]::WriteAllText("$saved.new", "$published`n")
    [IO.File]::Move("$saved.new", $saved, $true)
    if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
    Write-Output "https://steamcommunity.com/sharedfiles/filedetails/?id=$published"
} finally {
    if ($locked) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
