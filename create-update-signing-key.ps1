$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$keyDirectory = Join-Path $localAppData 'Lunar'
$keyPath = Join-Path $keyDirectory 'update-signing.key'
$publicPath = Join-Path $PSScriptRoot 'UpdatePublicKey.xml'
if (Test-Path -LiteralPath $keyPath) { throw 'A signing key already exists; refusing to replace the release identity.' }
$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider 3072
$rsa.PersistKeyInCsp = $false
$privateBytes = $null
$encryptedBytes = $null
$tempPath = $null
try {
    $privateXml = $rsa.ToXmlString($true)
    $publicXml = $rsa.ToXmlString($false)
    $privateBytes = [Text.Encoding]::UTF8.GetBytes($privateXml)
    $encryptedBytes = [Security.Cryptography.ProtectedData]::Protect($privateBytes, $null, [Security.Cryptography.DataProtectionScope]::CurrentUser)
    [IO.Directory]::CreateDirectory($keyDirectory) | Out-Null
    $tempPath = $keyPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    [IO.File]::WriteAllBytes($tempPath, $encryptedBytes)
    [IO.File]::Move($tempPath, $keyPath)
    [IO.File]::WriteAllText($publicPath, $publicXml, (New-Object Text.UTF8Encoding $false))
    Write-Output "Created a DPAPI-protected signing key at $keyPath and updated $publicPath."
} finally {
    if ($privateBytes) { [Array]::Clear($privateBytes, 0, $privateBytes.Length) }
    if ($encryptedBytes) { [Array]::Clear($encryptedBytes, 0, $encryptedBytes.Length) }
    if ($tempPath -and (Test-Path -LiteralPath $tempPath)) { Remove-Item -LiteralPath $tempPath -Force }
    $privateXml = $null
    $rsa.Dispose()
}