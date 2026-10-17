# Builds PTZVC.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (.NET Framework 4.x) not found' }

$icon = Join-Path $root 'src\ptzvc.ico'
$mark = Join-Path $root 'src\assets\logo-mark-color.png'
if (-not (Test-Path $icon) -or (Get-Item $mark).LastWriteTime -gt (Get-Item $icon).LastWriteTime) {
    & (Join-Path $root 'tools\make-icon.ps1') -Mark $mark -Out $icon
}

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$refs = '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll'
# the one-color logos are embedded under their file names (read by Brand.cs); the -color ones only feed the icon
$resources = Get-ChildItem (Join-Path $root 'src\assets') -Filter *.png | Where-Object { $_.Name -notlike '*-color*' } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }

& $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$dist\PTZVC.exe" "/win32icon:$icon" $refs $resources $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built $dist\PTZVC.exe"
