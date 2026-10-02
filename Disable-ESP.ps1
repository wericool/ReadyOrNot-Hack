param([Parameter(Mandatory = $true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
if (Get-Process ReadyOrNotSteam-Win64-Shipping -ErrorAction SilentlyContinue) { throw 'Close Ready Or Not before disabling UE4SS.' }
$bin = Join-Path (Resolve-Path -LiteralPath $GameRoot).Path 'ReadyOrNot\Binaries\Win64'
if (!(Test-Path -LiteralPath (Join-Path $bin 'ReadyOrNotSteam-Win64-Shipping.exe'))) { throw 'The Steam game executable was not found.' }
$proxy = Join-Path $bin 'dwmapi.dll'
$original = Join-Path $PSScriptRoot 'vendor\UE4SS\dwmapi.dll'
$disabled = Join-Path $bin 'dwmapi.dll.RoNESP-disabled'
if (!(Test-Path -LiteralPath $proxy)) { Write-Output 'UE4SS loader is already disabled.'; exit }
if ((Get-FileHash -LiteralPath $proxy).Hash -ne 'CF440B9EB8643BB7C434ACFDA696AEE57FD981D185DCA5E57FB8DBB18F8FC1CD') { throw 'The proxy DLL differs from this package. It was left untouched.' }
if (Test-Path -LiteralPath $disabled) { throw 'A disabled copy already exists. It was left untouched.' }
Move-Item -LiteralPath $proxy -Destination $disabled
Write-Output 'UE4SS loader disabled. Mod files and game saves are preserved.'
