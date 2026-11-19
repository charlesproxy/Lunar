# Publishes a Lunar update that installed copies download and apply by themselves.
# Usage:  powershell -ExecutionPolicy Bypass -File .\release.ps1 -Version 1.80 [-Notes "what changed"]
# Uploads ONLY Lunar-<version>.zip and lunar-update.json to GitHub Releases (no raw .exe, no .msi).
param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Notes = '',
    [string]$Repo = 'charlesproxy/Lunar'
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$v = [version]$Version
$release = $v.ToString($(if ($v.Build -le 0) { 2 } else { 3 }))
$fileVersion = "$($v.Major).$($v.Minor).$([Math]::Max($v.Build,0)).0"

if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'GitHub CLI (gh) is required.' }
gh auth status -h github.com --active | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Run "gh auth login" first.' }

# 1. Stamp the version into the source.
$lunarCs = Join-Path $root 'Lunar.cs'
$source = [IO.File]::ReadAllText($lunarCs)
$stamped = [regex]::Replace($source, 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(""$fileVersion"")")
if ($stamped -ne $source) { [IO.File]::WriteAllText($lunarCs, $stamped, (New-Object Text.UTF8Encoding $false)) }

# 2. Build and run the offline checks.
& (Join-Path $root 'build.ps1')
$exe = Join-Path $root "Lunar-$release.exe"
if (!(Test-Path -LiteralPath $exe)) { throw "Build did not produce $exe." }
$test = Start-Process -FilePath $exe -ArgumentList '--self-test' -Wait -PassThru -NoNewWindow
if ($test.ExitCode -ne 0) { throw 'Self-test failed; nothing was published.' }

# 3. Sign the manifest and pack the executable in a zip.
$output = Join-Path $root 'Output'
[IO.Directory]::CreateDirectory($output) | Out-Null
$manifest = Join-Path $output 'lunar-update.json'
& (Join-Path $root 'sign-update.ps1') -Portable $exe -Destination $manifest
$zip = Join-Path $output "Lunar-$release.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -LiteralPath $exe -DestinationPath $zip -CompressionLevel Optimal

# 4. Commit the version bump and publish the release (marked latest so the app finds it).
Push-Location $root
try {
    git add -A
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) { git commit -m "Release $release" | Out-Null }
    git push origin HEAD
    if ($LASTEXITCODE -ne 0) { throw 'git push failed.' }
    if (!$Notes) { $Notes = "Lunar $release" }
    gh release create "v$release" $zip $manifest --repo $Repo --title "Lunar $release" --notes $Notes --latest
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed.' }
} finally { Pop-Location }
Write-Output "Published v$release. Installed copies (1.85+) will offer the update within 5 minutes, or as soon as Lunar is opened."
