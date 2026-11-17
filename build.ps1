param([switch]$EmbedHenrikKey)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sourcePath = Join-Path $PSScriptRoot 'Lunar.cs'
$outputPath = Join-Path $PSScriptRoot 'Lunar-1.60.exe'
$iconPath = Join-Path $PSScriptRoot 'lunar.ico'
$resources = Get-ChildItem (Join-Path $PSScriptRoot 'assets/*') | Where-Object { $_.Extension -in @('.png','.json') } | ForEach-Object { "/resource:$($_.FullName),$($_.Name)" }
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char] '\') + '\'
$temporaryDirectory = [IO.Path]::GetFullPath((Join-Path $temporaryRoot ('Lunar-build-' + [guid]::NewGuid().ToString('N'))))
if (!$temporaryDirectory.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe temporary build path.' }
$bundleSource = Join-Path $temporaryDirectory 'HenrikKeyBundle.cs'
$keyBytes = $null
$key = ''
try {
    [IO.Directory]::CreateDirectory($temporaryDirectory) | Out-Null
    if ($EmbedHenrikKey) {
        $keyFile = Join-Path $env:APPDATA 'Lunar\henrik.key'
        if (Test-Path -LiteralPath $keyFile) {
            $encrypted = [IO.File]::ReadAllBytes($keyFile)
            try {
                $keyBytes = [Security.Cryptography.ProtectedData]::Unprotect(
                    $encrypted, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
                $key = [Text.Encoding]::UTF8.GetString($keyBytes).Trim()
            } catch {
                throw 'The saved HenrikDev key cannot be opened for this Windows account.'
            } finally {
                [Array]::Clear($encrypted, 0, $encrypted.Length)
            }
        }
        if ([string]::IsNullOrWhiteSpace($key) -and ![string]::IsNullOrWhiteSpace($env:LUNAR_HENRIK_KEY)) {
            $key = $env:LUNAR_HENRIK_KEY.Trim()
        }
        if ([string]::IsNullOrWhiteSpace($key)) {
            throw 'Release build requires a HenrikDev key saved in this Windows profile or LUNAR_HENRIK_KEY.'
        }
    }
    $encodedKey = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($key))
    $bundle = @"
namespace Lunar {
static class HenrikKeyBundle {
    internal static string Read() {
        const string encoded = "$encodedKey";
        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encoded));
    }
}
}
"@
    [IO.File]::WriteAllText($bundleSource, $bundle, (New-Object Text.UTF8Encoding $false))
    & $compilerPath /nologo /target:winexe /optimize+ "/out:$outputPath" "/win32icon:$iconPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll $sourcePath (Join-Path $PSScriptRoot 'History.cs') (Join-Path $PSScriptRoot 'Profile.cs') (Join-Path $PSScriptRoot 'Tracker.cs') (Join-Path $PSScriptRoot 'Henrik.cs') (Join-Path $PSScriptRoot 'Store.cs') (Join-Path $PSScriptRoot 'Friends.cs') (Join-Path $PSScriptRoot 'Desktop.cs') (Join-Path $PSScriptRoot 'Crosshair.cs') (Join-Path $PSScriptRoot 'SavedCrosshairs.cs') (Join-Path $PSScriptRoot 'Updates.cs') $bundleSource "/resource:$(Join-Path $PSScriptRoot 'UpdatePublicKey.xml'),lunar-update-public.xml" @resources
    if ($LASTEXITCODE -ne 0) { throw 'Lunar compilation failed.' }
    Write-Output "Built: $outputPath"
} finally {
    if ($keyBytes) { [Array]::Clear($keyBytes, 0, $keyBytes.Length) }
    $key = $null
    $encodedKey = $null
    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force }
}
