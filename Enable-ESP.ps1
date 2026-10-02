param([Parameter(Mandatory = $true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
if (Get-Process ReadyOrNotSteam-Win64-Shipping -ErrorAction SilentlyContinue) { throw 'Close Ready Or Not before enabling UE4SS.' }
$bin = Join-Path (Resolve-Path -LiteralPath $GameRoot).Path 'ReadyOrNot\Binaries\Win64'
if (!(Test-Path -LiteralPath (Join-Path $bin 'ReadyOrNotSteam-Win64-Shipping.exe'))) { throw 'The Steam game executable was not found.' }
$proxy = Join-Path $bin 'dwmapi.dll'
$disabled = Join-Path $bin 'dwmapi.dll.RoNESP-disabled'
$original = Join-Path $PSScriptRoot 'vendor\UE4SS\dwmapi.dll'
if (Test-Path -LiteralPath $proxy) { Write-Output 'A loader is already enabled; no changes made.'; exit }
if (!(Test-Path -LiteralPath $disabled)) { throw 'No disabled loader was found.' }
if ((Get-FileHash -LiteralPath $disabled).Hash -ne 'CF440B9EB8643BB7C434ACFDA696AEE57FD981D185DCA5E57FB8DBB18F8FC1CD') { throw 'The disabled DLL differs from this package.' }
Move-Item -LiteralPath $disabled -Destination $proxy
Write-Output 'UE4SS loader enabled.'
