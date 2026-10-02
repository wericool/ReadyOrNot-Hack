param([string]$OutputName='ReadyOrNot-ESP.exe')
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$target=Join-Path $PSScriptRoot $OutputName
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x compiler was not found.'}
foreach($p in @(Get-Process ReadyOrNot-ESP -ErrorAction SilentlyContinue)){if($p.Path -eq $target){throw 'Close this copy of ESP before rebuilding.'}}
$zip=Join-Path $PSScriptRoot 'vendor\UE4SS.zip'
if(!(Test-Path -LiteralPath $zip)){& (Join-Path $PSScriptRoot 'Download-UE4SS.ps1')}
if((Get-FileHash -LiteralPath $zip).Hash -ne 'AF8EA9D8975E8EFF7967423F43B8B50875E66A29A0F434CFFCE6E0867EA17252'){throw 'UE4SS archive checksum mismatch.'}
$generated=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $generated -Force | Out-Null
$lua=Get-Content (Join-Path $PSScriptRoot 'mod\Scripts\main.lua') -Raw
$lua=[regex]::Replace($lua,"(?m)^local output = '[^']*'","local output = '__RON_ESP_TELEMETRY_PATH__'")
$resource=Join-Path $generated 'main.lua'
[IO.File]::WriteAllText($resource,$lua,[Text.UTF8Encoding]::new($false))
& $compiler /nologo /target:winexe /platform:x64 "/out:$target" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/resource:$zip,RoN.UE4SS.zip" "/resource:$resource,RoN.main.lua" "/resource:$PSScriptRoot\LICENSES.txt,RoN.LICENSES.txt" "$PSScriptRoot\src\Overlay.cs" "$PSScriptRoot\src\Launcher.cs"
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
Write-Output "Built $OutputName"
