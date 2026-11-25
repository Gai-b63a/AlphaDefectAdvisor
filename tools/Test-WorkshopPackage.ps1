param(
    [Parameter(Mandatory)][string]$Workspace,
    [string]$Python = 'python'
)
$ErrorActionPreference = 'Stop'
& $Python -I "$PSScriptRoot/check_workshop.py" --workspace $Workspace
if ($LASTEXITCODE) { throw 'Workshop package validation failed.' }
$content = Join-Path $Workspace 'content'
$dll = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes("$content/AlphaDefectAdvisor.dll"))
$hash = $dll.GetType('AlphaDefectAdvisor.PayloadInfo', $true).GetField('Hash','Static,NonPublic').GetRawConstantValue()
if ((Get-FileHash -LiteralPath "$content/runtime.zip").Hash -ne $hash) { throw 'Runtime does not match the mod DLL.' }
Write-Host 'Package and DLL verified.'
