# Cache immutable inputs under this checkout, including when building a downloaded release.
$dependencyRoot = [IO.Path]::GetFullPath("$PSScriptRoot/../../runtime/toolchain")
function Get-VerifiedFile($uri, $file, $digest, $algorithm = 'SHA256') {
    if ((Test-Path -LiteralPath $file) -and (Get-FileHash -LiteralPath $file -Algorithm $algorithm).Hash -eq $digest) { return }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($file)) | Out-Null
    $temp = "$file.$([guid]::NewGuid()).tmp"
    try {
        Invoke-WebRequest -Uri $uri -OutFile $temp -TimeoutSec 900 -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $temp -Algorithm $algorithm).Hash -ne $digest) { throw "Checksum mismatch for $uri" }
        [IO.File]::Move($temp, $file, $true)
    } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
}
function Get-PrivateSdk {
    $version = '9.0.301'
    $destination = "$dependencyRoot/sdk-$version"
    if (!(Test-Path -LiteralPath "$destination/dotnet.exe")) {
        $zip = "$dependencyRoot/dotnet-sdk-$version.zip"
        $sha512 = 'f5b4a5e0c27d3e8cb6ce3524d99ea4e02c2d1f17a9b2a219f154bcd788455e25c45f3b305bb0b25468626b392f3b14e92dc7c628cc4299f7a7dd74c3ce1b05cd'
        Get-VerifiedFile "https://dotnetcli.blob.core.windows.net/dotnet/Sdk/$version/dotnet-sdk-$version-win-x64.zip" $zip $sha512 SHA512
        $temp = "$destination-$([guid]::NewGuid())"
        try {
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $temp)
            try { [IO.Directory]::Move($temp, $destination) } catch { if (!(Test-Path "$destination/dotnet.exe")) { throw } }
        } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
    }
    return "$destination/dotnet.exe"
}
function Invoke-PrivateBuild($script, $arguments, $logFile) {
    $sdk = Get-PrivateSdk
    $start = [Diagnostics.ProcessStartInfo]::new("$PSHOME/pwsh.exe")
    $start.UseShellExecute = $false
    $start.Environment['PATH'] = "$(Split-Path $sdk);$env:PATH"
    $start.Environment['DOTNET_ROOT'] = Split-Path $sdk
    $start.Environment['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $start.Environment['DOTNET_CLI_HOME'] = "$dependencyRoot/home"
    $start.Environment['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
    $start.Environment['DOTNET_SKIP_FIRST_TIME_EXPERIENCE'] = '1'
    $start.Environment['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    $start.Environment['NUGET_PACKAGES'] = "$dependencyRoot/nuget"
    $start.Environment['DOTNET_NOLOGO'] = '1'
    $start.Environment['DOTNET_ADD_GLOBAL_TOOLS_TO_PATH'] = '0'
    if ($logFile) { $start.RedirectStandardOutput = $start.RedirectStandardError = $true }
    foreach ($arg in (@('-NoProfile','-File',$script) + $arguments)) { $start.ArgumentList.Add($arg) }
    $process = [Diagnostics.Process]::Start($start)
    try {
        if ($logFile) {
            $stdout = $process.StandardOutput.ReadToEndAsync()
            $stderr = $process.StandardError.ReadToEndAsync()
        }
        $process.WaitForExit()
        if ($logFile) { [IO.File]::WriteAllText($logFile, $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()) }
        if ($process.ExitCode) { throw "Mod build failed ($($process.ExitCode)). $logFile" }
    }
    finally { $process.Dispose() }
}
function Get-PlaytestInputs($root, [bool]$refresh) {
    $base = 'https://github.com/Gai-b63a/AlphaDefectAdvisor/releases/download/advisor-v0.2.7'
    $cache = "$dependencyRoot/advisor-v0.2.7"
    $sources = "$cache/build-source.zip"
    Get-VerifiedFile "$base/build-source.zip" $sources '80a6c8d0a67329c5240ff05a05b4116d33fd38499e976f17eab03e9527c936b3'
    $sourceDir = "$cache/source"
    if (!(Test-Path -LiteralPath $sourceDir)) {
        $temp = "$sourceDir-$([guid]::NewGuid())"
        try {
            [IO.Compression.ZipFile]::ExtractToDirectory($sources, $temp)
            try { [IO.Directory]::Move($temp,$sourceDir) } catch { if (!(Test-Path $sourceDir)) { throw } }
        } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force } }
    }
    $archive = "$cache/runtime.zip"
    Get-VerifiedFile "$base/runtime.zip" $archive '8f158604f9726df82bf915fbf1218955ef7379352257edbf31989536ad8f6a03'
    $worker = "$root/advisor/companion.py"
    $local = "$cache/windows-$((Get-FileHash -LiteralPath $worker).Hash).zip"
    if ($refresh -or !(Test-Path -LiteralPath $local)) {
        $temp = "$local.$([guid]::NewGuid()).tmp"
        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $win = $zip.GetEntry('win-x64.zip')
                if ($win) { [IO.Compression.ZipFileExtensions]::ExtractToFile($win,$temp) }
                else { [IO.File]::Copy($archive,$temp) }
            } finally { $zip.Dispose() }
            $zip = [IO.Compression.ZipFile]::Open($temp,'Update')
            try {
                $zip.GetEntry('advisor/companion.py').Delete()
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$worker,'advisor/companion.py') | Out-Null
            } finally { $zip.Dispose() }
            [IO.File]::Move($temp,$local,$true)
        } finally { if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp } }
    }
    return @{ Archive=$local; Upstream="$sourceDir/upstream" }
}

function Find-GameDirectory {
    $steam = (Get-ItemProperty -LiteralPath 'HKCU:/Software/Valve/Steam' -ErrorAction SilentlyContinue).SteamPath
    $libraries = @()
    if ($steam) {
        $libraries += $steam
        $vdf = Join-Path $steam 'steamapps/libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            $libraries += @(Select-String -LiteralPath $vdf -Pattern '^\s*"path"\s+"([^"]+)"' |
                ForEach-Object { $_.Matches[0].Groups[1].Value.Replace('\\','\') })
        }
    }
    foreach ($library in ($libraries + 'E:\SteamLibrary')) {
        $game = Join-Path $library 'steamapps/common/Slay the Spire 2'
        if (Test-Path -LiteralPath "$game/data_sts2_windows_x86_64/sts2.dll") { return $game }
    }
    throw 'Game not found. Specify -GameDir for playtesting or -GameAssemblyDir for publishing.'
}
