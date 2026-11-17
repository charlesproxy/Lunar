$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePath = Join-Path $PSScriptRoot 'Lunar.cs'
$outputPath = Join-Path $PSScriptRoot 'Lunar-1.58.exe'
$iconPath = Join-Path $PSScriptRoot 'lunar.ico'
$resources = Get-ChildItem (Join-Path $PSScriptRoot 'assets/*') | Where-Object { $_.Extension -in @('.png','.json') } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
& $compilerPath /nologo /target:winexe /optimize+ "/out:$outputPath" "/win32icon:$iconPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll $sourcePath (Join-Path $PSScriptRoot 'History.cs') (Join-Path $PSScriptRoot 'Profile.cs') (Join-Path $PSScriptRoot 'Tracker.cs') (Join-Path $PSScriptRoot 'Henrik.cs') (Join-Path $PSScriptRoot 'Store.cs') (Join-Path $PSScriptRoot 'Friends.cs') (Join-Path $PSScriptRoot 'Desktop.cs') (Join-Path $PSScriptRoot 'Crosshair.cs') (Join-Path $PSScriptRoot 'SavedCrosshairs.cs') @resources
if ($LASTEXITCODE -ne 0) { throw 'Falha na compilação.' }
Write-Output "Compilado: $outputPath"
