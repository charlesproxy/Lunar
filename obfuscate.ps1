# Obfuscates a built Lunar executable in place with ConfuserEx2, so the shipped .exe cannot be
# trivially decompiled back into readable source. Called by release.ps1 and build-installer.ps1.
param(
    [Parameter(Mandatory=$true)][string]$Exe,
    [string]$ToolDir = (Join-Path $env:LOCALAPPDATA 'Lunar\tools\confuserex')
)
$ErrorActionPreference = 'Stop'
$cli = Join-Path $ToolDir 'Confuser.CLI.exe'

# Portable ConfuserEx2 CLI, fetched once and cached (no admin, no install).
if (!(Test-Path -LiteralPath $cli)) {
    New-Item -ItemType Directory -Force $ToolDir | Out-Null
    $zip = Join-Path $env:TEMP 'ConfuserEx-CLI.zip'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://github.com/mkaring/ConfuserEx/releases/download/v1.6.0/ConfuserEx-CLI.zip' -OutFile $zip -UseBasicParsing
    Expand-Archive -LiteralPath $zip -DestinationPath $ToolDir -Force
    if (!(Test-Path -LiteralPath $cli)) { throw 'Could not obtain ConfuserEx (Confuser.CLI.exe).' }
}

$exeFull = (Resolve-Path -LiteralPath $Exe).Path
$baseDir = Split-Path -Parent $exeFull
$leaf    = Split-Path -Leaf $exeFull
$work    = Join-Path $env:TEMP ('lunar-obf-' + [Guid]::NewGuid().ToString('N'))
$outDir  = Join-Path $work 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$proj = Join-Path $work 'lunar.crproj'

# Rename, control-flow, and constant protection. Embedded resource names are NOT renamed, because
# Lunar loads resources (icon, translations, WebView2) by their literal names at runtime.
@"
<project outputDir="$outDir" baseDir="$baseDir" xmlns="http://confuser.codeplex.com">
  <probePath>lib\webview2</probePath>
  <rule pattern="true" inherit="false">
    <protection id="anti ildasm" />
    <protection id="constants" />
    <protection id="ctrl flow" />
    <protection id="rename">
      <argument name="renResources" value="false" />
    </protection>
  </rule>
  <module path="$leaf" />
</project>
"@ | Set-Content -LiteralPath $proj -Encoding UTF8

& $cli -n $proj
$protected = Join-Path $outDir $leaf
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $protected)) {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    throw 'Obfuscation failed; nothing was changed.'
}
Copy-Item -LiteralPath $protected -Destination $exeFull -Force
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
Write-Output "Obfuscated: $exeFull"
