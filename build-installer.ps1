param([string]$WixPath,[string]$ObfuscarPath)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!$WixPath){
  $candle=Get-Command candle.exe -ErrorAction SilentlyContinue
  if($candle){$WixPath=Split-Path -Parent $candle.Source}
  else{
    $candidates=@("${env:ProgramFiles(x86)}\WiX Toolset v3.14\bin","${env:ProgramFiles(x86)}\WiX Toolset v3.11\bin","$env:ProgramFiles\WiX Toolset v3.14\bin","$env:ProgramFiles\WiX Toolset v3.11\bin")
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
  if($ObfuscarPath){
    $inputDirectory=[Security.SecurityElement]::Escape($root)
    $protected=Join-Path $output 'protected'
    $protectedDirectory=[Security.SecurityElement]::Escape($protected)
    $module=[Security.SecurityElement]::Escape($app.FullName)
    $config=Join-Path $output 'obfuscar.xml'
    @"
<Obfuscator>
  <Var name="InPath" value="$inputDirectory" />
  <Var name="OutPath" value="$protectedDirectory" />
  <Var name="KeepPublicApi" value="true" />
  <Var name="HidePrivateApi" value="true" />
  <Var name="RenameProperties" value="false" />
  <Var name="RenameEvents" value="false" />
  <Var name="HideStrings" value="true" />
  <Module file="$module" />
</Obfuscator>
"@ | Set-Content -LiteralPath $config -Encoding UTF8
    & $ObfuscarPath $config
    if($LASTEXITCODE -ne 0){throw 'Release obfuscation failed.'}
    $protectedApp=Join-Path $protected $app.Name
    if(!(Test-Path -LiteralPath $protectedApp)){throw 'Obfuscar did not produce the executable.'}
    Copy-Item -LiteralPath $protectedApp -Destination $app.FullName -Force
  }
  $obj=Join-Path $output 'LunarSetup.wixobj'
  & (Join-Path $WixPath 'candle.exe') -nologo "-dLunarVersion=$versionString" "-dLunarExecutable=$($app.FullName)" -out $obj $source
  if($LASTEXITCODE -ne 0){throw 'WiX compilation failed.'}
  $msi=Join-Path $output "Lunar-Setup-$($version.Major).$($version.Minor).msi"
  & (Join-Path $WixPath 'light.exe') -nologo -ext (Join-Path $WixPath 'WixUIExtension.dll') -ext (Join-Path $WixPath 'WixNetFxExtension.dll') -out $msi $obj
  if($LASTEXITCODE -ne 0){throw 'WiX linking failed.'}
}finally{Pop-Location}
