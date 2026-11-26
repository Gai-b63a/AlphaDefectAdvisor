#Requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$SteamApps,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][ValidateSet('win-x64','osx-arm64')][string]$Platform
)
$ErrorActionPreference = 'Stop'
$item = '3845626411'
$report = [ordered]@{platform=$Platform; expected_version=$Version; item=$item; errors=@(); source=$SteamApps}
$manifest = Join-Path $SteamApps 'appmanifest_2868840.acf'
if (!(Test-Path -LiteralPath $manifest)) { $report.errors += 'Game manifest missing' }
elseif ((Get-Content -LiteralPath $manifest -Raw) -match '"buildid"\s+"(\d+)"') { $report.steam_build_id = $Matches[1] }
else { $report.errors += 'Steam build ID missing' }
try {
    $folder = Join-Path $SteamApps "workshop/content/2868840/$item"
    $mod = Get-Content -LiteralPath "$folder/AlphaDefectAdvisor.json" -Raw | ConvertFrom-Json
    $report.installed_version = $mod.version
    if ($mod.id -cne 'AlphaDefectAdvisor' -or $mod.version -cne $Version) { $report.errors += 'Mod ID or version mismatch' }
    foreach ($name in 'AlphaDefectAdvisor.dll','runtime.zip') {
        $report["${name}_sha256"] = (Get-FileHash -LiteralPath "$folder/$name").Hash.ToLowerInvariant()
    }
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "$folder/runtime.zip").Path)
    try {
        if (!$zip.GetEntry("$Platform.zip") -and !($Platform -eq 'win-x64' -and $zip.GetEntry('python/python.exe'))) {
            $report.errors += 'Runtime for the selected guest is missing'
        }
    } finally { $zip.Dispose() }
} catch { $report.errors += $_.Exception.Message }
$report | ConvertTo-Json
if ($report.errors.Count) { exit 1 }

