$ErrorActionPreference = 'Stop'
$url = 'https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental-latest/UE4SS_v3.0.1-1152-ge3ba1016.zip'
$sha = 'AF8EA9D8975E8EFF7967423F43B8B50875E66A29A0F434CFFCE6E0867EA17252'
$vendor = Join-Path $PSScriptRoot 'vendor'
$target = Join-Path $vendor 'UE4SS'
if (Test-Path -LiteralPath $target) { throw 'vendor/UE4SS already exists; use the existing runtime or move it aside manually.' }
New-Item -ItemType Directory -Path $vendor -Force | Out-Null
$zip = Join-Path $vendor 'UE4SS.zip'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $sha) { throw 'UE4SS archive checksum mismatch. Archive was not extracted.' }
Expand-Archive -LiteralPath $zip -DestinationPath $target
Write-Output 'Pinned UE4SS downloaded and verified.'
