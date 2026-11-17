$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputPath = Join-Path $PSScriptRoot 'Lunar-1.78.exe'
$resources = Get-ChildItem (Join-Path $PSScriptRoot 'assets/*') | Where-Object { $_.Extension -in @('.png','.json') } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$sources = @('Lunar.cs','History.cs','Profile.cs','Tracker.cs','RiotStats.cs','Store.cs','Friends.cs','Desktop.cs','Crosshair.cs','SavedCrosshairs.cs','Updates.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compilerPath /nologo /target:winexe /optimize+ "/out:$outputPath" "/win32icon:$(Join-Path $PSScriptRoot 'lunar.ico')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll @sources "/resource:$(Join-Path $PSScriptRoot 'UpdatePublicKey.xml'),lunar-update-public.xml" @resources
if ($LASTEXITCODE -ne 0) { throw 'Lunar compilation failed.' }
Write-Output "Built: $outputPath"
