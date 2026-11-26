[CmdletBinding(DefaultParameterSetName='Workspace')]
param(
    [Parameter(Mandatory,ParameterSetName='Workspace')][string]$Workspace,
    [Parameter(Mandatory,ParameterSetName='Runtime')][string]$Runtime
)
$ErrorActionPreference = 'Stop'
function Test-Payload($path, $platform) {
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            if (!$names.Add($name) -or $name -match '(^/|\\|:|(^|/)(\.\.|catalog)(/|$)|(^|/)(sts2\.dll|godotsharp\.dll|enchant_effects\.json)$|\.pck$)') {
                throw "Unexpected runtime entry: $name"
            }
        }
        if (!$platform -and $names.Contains('win-x64.zip')) {
            if (!$names.SetEquals([string[]]@('win-x64.zip','osx-arm64.zip'))) { throw 'Invalid platform archives.' }
            foreach ($rid in 'win-x64','osx-arm64') {
                $temp = [IO.Path]::GetTempFileName()
                try {
                    [IO.Compression.ZipFileExtensions]::ExtractToFile($zip.GetEntry("$rid.zip"),$temp,$true)
                    Test-Payload $temp $rid
                } finally { Remove-Item -LiteralPath $temp }
            }
            return
        }
        $required = @('advisor/companion.py','ironclad_ema_v24782.pt','upstream/tools/ability_test.py')
        if ($platform -eq 'osx-arm64') { $required += 'python/bin/python3.12','python/lib/libpython3.12.dylib','python/lib/python3.12/site-packages/torch/lib/libtorch_cpu.dylib' }
        else { $required += 'python/python.exe','python/python312._pth','python/msvcp140.dll','python/LICENSE.txt','python/Lib/site-packages/torch/lib/torch_cpu.dll' }
        foreach ($name in $required) { if (!$names.Contains($name)) { throw "Missing $name" } }
        foreach ($entry in $zip.Entries) {
            $stream = $entry.Open()
            try {
                if ($entry.FullName -ceq 'ironclad_ema_v24782.pt') {
                    if ((Get-FileHash -InputStream $stream).Hash -ne '78ba58312335b1a49823fdaf6aa263ee46f9dae46b5d4753bc609eb671588f20') { throw 'Unexpected model checksum.' }
                } else { $stream.CopyTo([IO.Stream]::Null) }
            } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
}
if ($Runtime) { Test-Payload (Resolve-Path $Runtime).Path; return }
$content = Join-Path $Workspace 'content'
$expected = 'AlphaDefectAdvisor.dll','AlphaDefectAdvisor.json','LICENSE','README.md','AlphaDefect-LICENSE.txt','AlphaDefect-NOTICE.md','runtime.zip'
$files = @(Get-ChildItem -Force -LiteralPath $content)
if (($files | Where-Object PSIsContainer) -or (Compare-Object $expected $files.Name -CaseSensitive)) { throw 'Unexpected Workshop content.' }
$manifest = Get-Content -Raw -LiteralPath "$content/AlphaDefectAdvisor.json" | ConvertFrom-Json
$config = Get-Content -Raw -LiteralPath "$Workspace/workshop.json" | ConvertFrom-Json
if ($manifest.id -cne 'AlphaDefectAdvisor' -or !$manifest.version -or $config.visibility -cne 'public' -or !$config.title -or !$config.description -or !$config.changeNote) { throw 'Invalid Workshop metadata.' }
$image = [IO.File]::ReadAllBytes((Resolve-Path "$Workspace/image.png").Path)
if ($image.Length -lt 8 -or $image.Length -ge 1000000 -or [BitConverter]::ToString($image,0,8) -ne '89-50-4E-47-0D-0A-1A-0A') { throw 'Invalid Workshop preview.' }
# Read the embedded checksum without loading a net9.0 assembly into the PowerShell host.
$stream = [IO.File]::OpenRead((Resolve-Path "$content/AlphaDefectAdvisor.dll").Path)
$pe = [Reflection.PortableExecutable.PEReader]::new($stream)
try {
    $metadata = [Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
    $hash = $null
    foreach ($handle in $metadata.TypeDefinitions) {
        $type = $metadata.GetTypeDefinition($handle)
        if ($metadata.GetString($type.Name) -cne 'PayloadInfo' -or $metadata.GetString($type.Namespace) -cne 'AlphaDefectAdvisor') { continue }
        foreach ($fieldHandle in $type.GetFields()) {
            $field = $metadata.GetFieldDefinition($fieldHandle)
            if ($metadata.GetString($field.Name) -ceq 'Hash') {
                $constant = $metadata.GetConstant($field.GetDefaultValue())
                $hash = [Text.Encoding]::Unicode.GetString($metadata.GetBlobBytes($constant.Value))
            }
        }
    }
} finally { $pe.Dispose(); $stream.Dispose() }
if ((Get-FileHash -LiteralPath "$content/runtime.zip").Hash -ne $hash) { throw 'Runtime does not match the mod DLL.' }
Test-Payload (Resolve-Path "$content/runtime.zip").Path
Write-Host 'Workshop package and DLL verified.'

