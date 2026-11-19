param([string]$WixPath,[string]$ObfuscarPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!$WixPath){
  $candle=Get-Command candle.exe -ErrorAction SilentlyContinue
  if($candle){$WixPath=Split-Path -Parent $candle.Source}
  else{
    $candidates=@("$env:LOCALAPPDATA\Lunar\tools\wix314","${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin","${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin","$env:ProgramFiles\WiX Toolset v3.14\bin","$env:ProgramFiles\WiX Toolset v3.11\bin")
    $WixPath=$candidates|Where-Object {Test-Path (Join-Path $_ 'candle.exe')}|Select-Object -First 1
  }
}
if(!$WixPath -or !(Test-Path (Join-Path $WixPath 'candle.exe')) -or !(Test-Path (Join-Path $WixPath 'light.exe'))){throw 'WiX Toolset 3.11 or 3.14 is required to build the MSI. Install WiX on the build machine, then rerun this script.'}
$source=Join-Path $root 'LunarSetup.wxs'
Push-Location $root
try{
  & (Join-Path $root 'build.ps1')
  $app=Get-ChildItem -LiteralPath $root -File -Filter 'Lunar-*.exe' | Sort-Object { [version]$_.VersionInfo.FileVersion } -Descending | Select-Object -First 1
  $version=[version]$app.VersionInfo.FileVersion
  $versionString="$($version.Major).$($version.Minor).$($version.Build)"
  $output=Join-Path $root 'Output'
  [IO.Directory]::CreateDirectory($output)|Out-Null
  & (Join-Path $root 'obfuscate.ps1') -Exe $app.FullName
  $obj=Join-Path $output 'LunarSetup.wixobj'
  & (Join-Path $WixPath 'candle.exe') -nologo "-dLunarVersion=$versionString" "-dLunarExecutable=$($app.FullName)" -out $obj $source
  if($LASTEXITCODE -ne 0){throw 'WiX compilation failed.'}
  $msi=Join-Path $output "Lunar-Setup-$($version.Major).$($version.Minor).msi"
  & (Join-Path $WixPath 'light.exe') -nologo -ext (Join-Path $WixPath 'WixUIExtension.dll') -ext (Join-Path $WixPath 'WixNetFxExtension.dll') -out $msi $obj
  if($LASTEXITCODE -ne 0){throw 'WiX linking failed.'}
}finally{Pop-Location}
