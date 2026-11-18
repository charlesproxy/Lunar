$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$versionMatch = Select-String -LiteralPath (Join-Path $PSScriptRoot 'Lunar.cs') -Pattern 'AssemblyFileVersion\("(\d+)\.(\d+)\.(\d+)\.\d+"\)' | Select-Object -First 1
if (!$versionMatch) { throw 'AssemblyFileVersion not found in Lunar.cs.' }
$g = $versionMatch.Matches[0].Groups
$releaseVersion = if ($g[3].Value -eq '0') { "$($g[1].Value).$($g[2].Value)" } else { "$($g[1].Value).$($g[2].Value).$($g[3].Value)" }
$outputPath = Join-Path $PSScriptRoot "Lunar-$releaseVersion.exe"
$resources = Get-ChildItem (Join-Path $PSScriptRoot 'assets/*') | Where-Object { $_.Extension -eq '.json' -or $_.Name -in @('lunar.png','discord-clyde.png') } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$webview = Get-ChildItem (Join-Path $PSScriptRoot 'lib/webview2/*.dll') | ForEach-Object { "/resource:$($_.FullName),webview2/$($_.Name)" }
$sources = @('Lunar.cs','ApiAssets.cs','History.cs','Profile.cs','Tracker.cs','RiotStats.cs','Store.cs','Settings.cs','Skeleton.cs','Video.cs','Friends.cs','Desktop.cs','Crosshair.cs','SavedCrosshairs.cs','Updates.cs','Language.cs','Notify.cs','Log.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
& $compilerPath /nologo /codepage:65001 /target:winexe /optimize+ "/out:$outputPath" "/win32icon:$(Join-Path $PSScriptRoot 'lunar.ico')" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll /reference:System.IO.Compression.dll "/reference:$(Join-Path $PSScriptRoot 'lib\webview2\Microsoft.Web.WebView2.Core.dll')" "/reference:$(Join-Path $PSScriptRoot 'lib\webview2\Microsoft.Web.WebView2.WinForms.dll')" @sources "/resource:$(Join-Path $PSScriptRoot 'UpdatePublicKey.xml'),lunar-update-public.xml" "/resource:$(Join-Path $PSScriptRoot 'lunar.ico'),lunar.ico" @resources @webview
if ($LASTEXITCODE -ne 0) { throw 'Lunar compilation failed.' }
Write-Output "Built: $outputPath"
