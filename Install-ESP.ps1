param([Parameter(Mandatory = $true)][string]$GameRoot)
$ErrorActionPreference = 'Stop'
if (Get-Process ReadyOrNotSteam-Win64-Shipping -ErrorAction SilentlyContinue) { throw 'Close Ready Or Not before installing.' }
$bin = Join-Path (Resolve-Path -LiteralPath $GameRoot).Path 'ReadyOrNot\Binaries\Win64'
if (!(Test-Path -LiteralPath (Join-Path $bin 'ReadyOrNotSteam-Win64-Shipping.exe'))) { throw 'The Steam game executable was not found.' }
$runtime = Join-Path $bin 'ue4ss'
$proxy = Join-Path $bin 'dwmapi.dll'
$vendor = Join-Path $PSScriptRoot 'vendor\UE4SS'
if (!(Test-Path -LiteralPath (Join-Path $vendor 'dwmapi.dll'))) { throw 'Run Download-UE4SS.ps1 first, or use the complete release archive.' }
if ((Test-Path -LiteralPath $runtime) -and ((Get-FileHash -LiteralPath (Join-Path $runtime 'UE4SS.dll') -ErrorAction Stop).Hash -ne (Get-FileHash -LiteralPath (Join-Path $vendor 'ue4ss\UE4SS.dll')).Hash)) { throw 'Existing UE4SS version differs. No files were overwritten.' }
if ((Test-Path -LiteralPath $proxy) -and ((Get-FileHash -LiteralPath $proxy).Hash -ne (Get-FileHash -LiteralPath (Join-Path $vendor 'dwmapi.dll')).Hash)) { throw 'Another proxy DLL is present. No files were overwritten.' }
if (!(Test-Path -LiteralPath $runtime)) {
 if (Test-Path -LiteralPath $proxy) { throw 'Another proxy DLL is present; installation stopped without replacing it.' }
 Copy-Item -LiteralPath (Join-Path $vendor 'ue4ss') -Destination $bin -Recurse
 Copy-Item -LiteralPath (Join-Path $vendor 'dwmapi.dll') -Destination $bin
 Set-Content -LiteralPath (Join-Path $runtime 'Mods\mods.txt') -Value 'RoNESP : 1' -Encoding ASCII
} elseif (!(Test-Path -LiteralPath (Join-Path $runtime 'UE4SS.dll'))) { throw 'Existing UE4SS directory is incomplete; no files overwritten.' }
$modDir = Join-Path $runtime 'Mods\RoNESP\Scripts'
$destination = Join-Path $modDir 'main.lua'
$backupDir = Join-Path $PSScriptRoot ('backups\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
if (Test-Path -LiteralPath $destination) {
 New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
 Copy-Item -LiteralPath $destination -Destination (Join-Path $backupDir 'main.lua')
}
New-Item -ItemType Directory -Path $modDir -Force | Out-Null
$lua = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'mod\Scripts\main.lua') -Raw
$outputPath = (Join-Path $PSScriptRoot 'telemetry.json').Replace('\','/').Replace("'", "\'")
$lua = $lua.Replace('__RON_ESP_TELEMETRY_PATH__', $outputPath)
Set-Content -LiteralPath $destination -Value $lua -Encoding UTF8
$modsFile = Join-Path $runtime 'Mods\mods.txt'
$mods = if (Test-Path -LiteralPath $modsFile) { Get-Content -LiteralPath $modsFile -Raw } else { '' }
if ($mods -match '(?m)^\s*RoNESP\s*:') {
 $mods = [regex]::Replace($mods, '(?m)^\s*RoNESP\s*:\s*\d+', 'RoNESP : 1')
} else { $mods += "`r`nRoNESP : 1`r`n" }
Set-Content -LiteralPath $modsFile -Value $mods -Encoding UTF8
Write-Output 'ESP installed. Launch the game normally and run Start-ESP.cmd.'
