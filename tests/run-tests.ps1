# Compiles the PTZVC sources together with the test harness and runs it.
# Usage:  powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1 [-ShotsDir <folder for rendered frames>]
param([string]$ShotsDir = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$out = Join-Path $env:TEMP 'PTZVC.Tests.exe'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | Where-Object { $_.Name -ne 'Program.cs' } | ForEach-Object { $_.FullName }
$sources += Join-Path $PSScriptRoot 'Tests.cs'
$resources = Get-ChildItem (Join-Path $root 'src\assets') -Filter *.png | Where-Object { $_.Name -notlike '*-color*' } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
& $csc /nologo /target:exe /optimize+ "/out:$out" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll $resources $sources
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
if ($ShotsDir) { New-Item -ItemType Directory -Force $ShotsDir | Out-Null; & $out $ShotsDir }
else { & $out }
exit $LASTEXITCODE
