function Test-UploadReceipt($client, $item, $preview, [long]$offset = 0) {
    $path = Join-Path (Split-Path $client) 'logs/workshop_log.txt'
    if (!(Test-Path -LiteralPath $path)) { return $false }
    $stream = [IO.File]::Open($path,'Open','Read','ReadWrite')
    try {
        $stream.Position = $offset
        $reader = [IO.StreamReader]::new($stream)
        $started = $matched = $finished = $false
        while ($null -ne ($line = $reader.ReadLine())) {
            if ($line -notmatch '\[AppID 2868840\] (.+)$') { continue }
            $message = $Matches[1].TrimEnd()
            if ($message.StartsWith('Upload starting for workshop item ')) {
                $started = $message -ceq "Upload starting for workshop item $item by AppID 2868840"
                $matched = $finished = $false
            } elseif ($started -and $message -cmatch '^Uploaded new workshop preview file (.+)$') {
                $matched = [IO.Path]::GetFullPath($Matches[1]) -eq $preview
            } elseif ($started -and $message.StartsWith('Upload finished for workshop item ')) {
                $finished = $matched -and $message -ceq "Upload finished for workshop item $item : OK"
                $started = $false
            }
        }
        return $finished
    } finally { $stream.Dispose() }
}
function Test-PreviousUpload($store, $client, $hold, $version, $item) {
    $vdfPath = Join-Path (Split-Path $client) 'advisor.vdf'
    if (!(Test-Path -LiteralPath $vdfPath)) { return $false }
    $vdf = [IO.File]::ReadAllText($vdfPath)
    if ($vdf -notmatch "(?m)^\s*\x22publishedfileid\x22\s+\x22$item\x22\s*$" -or $vdf -notmatch '(?m)^\s*"previewfile"\s+"([^"]+)"\s*$') { return $false }
    $preview = [IO.Path]::GetFullPath($Matches[1])
    if ([IO.Path]::GetRelativePath($store,$preview).Replace('\','/') -notmatch '^build-[0-9a-f-]{36}/source/dist/workshop/image\.png$') { return $false }
    $record = [IO.File]::ReadAllText($hold).Trim()
    if ($record.StartsWith('{')) {
        $data = $record | ConvertFrom-Json
        if ($data.item -cne $item -or $data.version -cne $version -or $data.preview -ne $preview) { return $false }
    } else {
        $delta = ([IO.File]::GetLastWriteTimeUtc($vdfPath) - [IO.File]::GetLastWriteTimeUtc($hold)).TotalSeconds
        if ($record -cne "Check Steam item $item and SteamCMD login before retrying version $version." -or $delta -lt -2 -or $delta -gt 120 -or
            $vdf -notmatch ('(?m)^\s*"changenote"\s+"v' + [regex]::Escape($version) + '(?![.0-9])')) { return $false }
    }
    return Test-UploadReceipt $client $item $preview
}
function Send-WorkshopUpdate($client, $account, $workspace, $item) {
    $config = Get-Content -LiteralPath "$workspace/workshop.json" -Raw | ConvertFrom-Json
    $preview = [IO.Path]::GetFullPath("$workspace/image.png")
    $vdf = Join-Path (Split-Path $client) 'advisor.vdf'
    $fields = [ordered]@{ appid='2868840'; publishedfileid=$item; contentfolder=[IO.Path]::GetFullPath("$workspace/content").Replace('\','/'); previewfile=$preview.Replace('\','/'); visibility='0'; title=$config.title; description=$config.description; changenote=$config.changeNote }
    $lines = foreach ($entry in $fields.GetEnumerator()) {
        '  "{0}" "{1}"' -f $entry.Key,$entry.Value.Replace('\','\\').Replace('"','\"')
    }
    [IO.File]::WriteAllLines($vdf, (@('"workshopitem"','{') + $lines + '}'))
    $log = Join-Path (Split-Path $client) 'logs/workshop_log.txt'
    $offset = if (Test-Path -LiteralPath $log) { (Get-Item -LiteralPath $log).Length } else { 0 }
    $start = [Diagnostics.ProcessStartInfo]::new($client)
    $start.WorkingDirectory = Split-Path $client
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $start.RedirectStandardError = $start.RedirectStandardInput = $true
    foreach ($arg in '+@ShutdownOnFailedCommand','1','+login',$account,'+workshop_build_item',$vdf,'+quit') { $start.ArgumentList.Add($arg) }
    Write-Host 'Connecting to Steam. Approve the sign-in in Steam Mobile if prompted.'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(1200000)) { $process.Kill($true); throw 'SteamCMD timed out.' }
        $output = $stdout.GetAwaiter().GetResult() + [Environment]::NewLine + $stderr.GetAwaiter().GetResult()
        if ($process.ExitCode -or $output -match '(?im)^\s*(ERROR!|FAILED\s*\()') { throw "SteamCMD failed (exit $($process.ExitCode)). Check the Workshop change history." }
        if ([IO.File]::ReadAllText($vdf) -notmatch "(?m)^\s*\x22publishedfileid\x22\s+\x22$item\x22\s*$" -or
            (!(Test-UploadReceipt $client $item $preview $offset) -and $output -notmatch '(?im)^\s*Success[.!]')) { throw 'Steam upload could not be confirmed.' }
    } finally { $process.Dispose() }
}
