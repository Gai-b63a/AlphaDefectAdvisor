function Read-ChangeLog([string]$path) {
    $versions = [ordered]@{}
    $text = [IO.File]::ReadAllText($path).Replace("`r`n","`n")
    $sections = [regex]::Split($text, '(?m)^## ')
    foreach ($section in ($sections | Select-Object -Skip 1)) {
        $heading, $notes = $section -split "`n", 2
        $version = $heading.Trim()
        if ($version -notmatch '^\d+\.\d+\.\d+$' -or $versions.Contains($version) -or [string]::IsNullOrWhiteSpace($notes)) {
            throw "Invalid, empty or duplicate changelog section: $heading"
        }
        $versions[$version] = $notes.Trim()
    }
    return $versions
}

function Set-WorkshopNotes($workspace, $version, $notes) {
    if (!$notes[$version]) { throw "Missing CHANGELOG.md entry for $version." }
    $file = Join-Path $workspace 'workshop.json'
    $metadata = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json -AsHashtable
    $metadata.changeNote = "v$version`n`n" + $notes[$version]
    $link = 'https://github.com/Gai-b63a/AlphaDefectAdvisor/blob/main/CHANGELOG.md'
    if (!$metadata.description.Contains($link)) {
        $metadata.description += "`n`n[url=$link]Full version history[/url]"
    }
    $metadata | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $file -Encoding utf8NoBOM
}
