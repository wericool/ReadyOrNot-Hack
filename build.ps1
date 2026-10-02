$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler was not found.' }
$target = Join-Path $PSScriptRoot 'RoN-ESP.exe'
foreach ($process in @(Get-Process RoN-ESP -ErrorAction SilentlyContinue)) {
 if ($process.Path -eq $target) { throw 'Close this copy of the overlay with End before rebuilding.' }
}
& $compiler /nologo /target:winexe "/out:$target" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "$PSScriptRoot\src\Overlay.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output 'Built RoN-ESP.exe'
