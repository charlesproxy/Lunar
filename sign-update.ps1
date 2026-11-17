param(
    [Parameter(Mandatory=$true)][string]$Portable,
    [Parameter(Mandatory=$true)][string]$Installer,
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$exe = Get-Item -LiteralPath $Portable
$msi = Get-Item -LiteralPath $Installer
$version = [version]$exe.VersionInfo.FileVersion
$releaseVersion = $version.ToString($(if ($version.Build -eq 0) { 2 } else { 3 }))
if ($exe.Name -ne "Lunar-$releaseVersion.exe" -or $msi.Name -ne "Lunar-Setup-$releaseVersion.msi") {
    throw 'Update package filenames must match the Lunar version.'
}
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$key = Join-Path $localAppData 'Lunar/update-signing.key'
$scope = [Security.Cryptography.DataProtectionScope]::CurrentUser
$privateKey = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($key), $null, $scope)
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new([Security.Cryptography.CspParameters]::new(24))
$rsa.PersistKeyInCsp = $false
try {
    $rsa.FromXmlString([Text.Encoding]::UTF8.GetString($privateKey))
    if ($rsa.ToXmlString($false) -ne (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'UpdatePublicKey.xml') -Raw).Trim()) {
        throw 'The signing key does not match the public key in Lunar.'
    }
    function DescribePackage($file) {
        return @{name=$file.Name; size=$file.Length; sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash}
    }
    $manifest = @{version=$version.ToString(); installer=(DescribePackage $msi)}
    $payload = [Text.Encoding]::UTF8.GetBytes(($manifest | ConvertTo-Json -Depth 3 -Compress))
    $signature = $rsa.SignData($payload, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    $envelope = @{payload=[Convert]::ToBase64String($payload); signature=[Convert]::ToBase64String($signature)}
    [IO.File]::WriteAllText($Destination, ($envelope | ConvertTo-Json -Compress), (New-Object Text.UTF8Encoding $false))
} finally {
    [Array]::Clear($privateKey,0,$privateKey.Length)
    $rsa.Dispose()
}
