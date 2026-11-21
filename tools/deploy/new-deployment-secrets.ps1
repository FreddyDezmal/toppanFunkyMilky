<#
.SYNOPSIS
  M14 Step 8: creates the generated deployment secrets and hands them over one at a time through the clipboard.

.DESCRIPTION
  Creates:
    DataProtection__CertificateBase64    a new self-signed RSA 3072 certificate (PKCS#12, base64) that encrypts the
                                         sign-in key ring (decision D-3)
    DataProtection__CertificatePassword  its password
    BACKUP_PASSPHRASE                    the backup encryption passphrase (decision D-12)

  Nothing is printed and nothing is written to disk: each value is copied to the clipboard, you paste it into Koyeb or
  GitHub (and into your password manager), press Enter, and the next one is copied. The clipboard is cleared at the
  end, and the certificate is removed from the Windows certificate store once exported.

  Run it once, at the first deployment. Running it again creates NEW values: replacing the certificate signs everyone
  out; replacing the backup passphrase only affects new backups (keep the old one until its backups expire).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools/deploy/new-deployment-secrets.ps1
#>
$ErrorActionPreference = 'Stop'

function New-RandomSecret([int] $bytes) {
    $buffer = New-Object byte[] $bytes
    # Create().GetBytes works in Windows PowerShell 5.1 and PowerShell 7 alike.
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($buffer) } finally { $rng.Dispose() }
    # URL-safe base64 without padding: safe to paste anywhere.
    return [Convert]::ToBase64String($buffer).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Hand-Over([string] $name, [string] $where, [string] $value) {
    Set-Clipboard -Value $value
    Write-Host ""
    Write-Host "Copied $name to the clipboard ($($value.Length) characters)." -ForegroundColor Cyan
    Write-Host "  Paste it into: $where"
    Write-Host "  and into your password manager."
    [void](Read-Host "Press Enter when done")
}

$certificatePassword = New-RandomSecret 24
$backupPassphrase = New-RandomSecret 32

$cert = New-SelfSignedCertificate -Subject 'CN=Milky Shaky Data Protection' -KeyAlgorithm RSA -KeyLength 3072 `
    -KeyExportPolicy Exportable -KeyUsage KeyEncipherment, DataEncipherment -CertStoreLocation 'Cert:\CurrentUser\My' `
    -NotAfter (Get-Date).AddYears(5)
try {
    $pfx = $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $certificatePassword)
    $certificateBase64 = [Convert]::ToBase64String($pfx)
}
finally {
    Remove-Item -Path "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -ErrorAction SilentlyContinue
}

Write-Host "Three values will be copied to the clipboard one after another. Nothing is shown or saved."
Hand-Over 'DataProtection__CertificateBase64' 'Koyeb: Secrets -> new secret, then the service variable of the same name' $certificateBase64
Hand-Over 'DataProtection__CertificatePassword' 'Koyeb: Secrets -> new secret, then the service variable of the same name' $certificatePassword
Hand-Over 'BACKUP_PASSPHRASE' 'GitHub: repository Settings -> Secrets and variables -> Actions -> New repository secret' $backupPassphrase

Set-Clipboard -Value ' '
Write-Host ""
Write-Host "Done. The clipboard has been cleared." -ForegroundColor Green
