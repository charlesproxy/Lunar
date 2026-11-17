param([Parameter(Mandatory=$true)][string]$WixPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$source=Join-Path $root 'LunarSetup.wxs'
Push-Location $root
try{
  & (Join-Path $root 'build.ps1')
  $app=Get-ChildItem -LiteralPath $root -File -Filter 'Lunar-*.exe' | Sort-Object { [version]$_.VersionInfo.FileVersion } -Descending | Select-Object -First 1
  $version=[version]$app.VersionInfo.FileVersion
  $versionString="$($version.Major).$($version.Minor).$($version.Build)"
  $output=Join-Path $root 'Output'
  [IO.Directory]::CreateDirectory($output)|Out-Null
  $obj=Join-Path $output 'LunarSetup.wixobj'
  & (Join-Path $WixPath 'candle.exe') -nologo "-dLunarVersion=$versionString" "-dLunarExecutable=$($app.FullName)" -out $obj $source
  if($LASTEXITCODE -ne 0){throw 'WiX compilation failed.'}
  $msi=Join-Path $output "Lunar-Setup-$($version.Major).$($version.Minor).msi"
  & (Join-Path $WixPath 'light.exe') -nologo -ext (Join-Path $WixPath 'WixUIExtension.dll') -ext (Join-Path $WixPath 'WixNetFxExtension.dll') -out $msi $obj
  if($LASTEXITCODE -ne 0){throw 'WiX linking failed.'}
}finally{Pop-Location}
