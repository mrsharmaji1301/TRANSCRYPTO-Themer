# Builds the installer: dist\TRANSCRYPTO-Themer-Setup-<version>.exe
#  1. the signed release zip (tools\make-update.ps1) is embedded in tools\setup\Setup.cs, compiled with the Windows C# compiler;
#  2. the .exe is Authenticode-signed (SHA-256, timestamped).
# Signing certificate:
#  -PfxPath / -PfxPassword  a purchased code-signing certificate (OV/EV) - what a public release needs (SmartScreen trust);
#  otherwise                a self-signed "CN=TRANSCRYPTO" certificate from CurrentUser\My, created on first use. It proves
#                           the file wasn't changed, but Windows shows "Unknown publisher" for it.
param([switch]$SkipZip, [switch]$NoCopy, [string]$PfxPath, [string]$PfxPassword, [string]$TimestampUrl = 'http://timestamp.digicert.com')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$ver = (Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json).version
$dist = Join-Path $root 'dist'
$zip = Join-Path $dist "TRANSCRYPTO-Themer-$ver.zip"
if (-not $SkipZip -or -not (Test-Path $zip)) { & (Join-Path $PSScriptRoot 'make-update.ps1') | Out-Null }
if (-not (Test-Path $zip)) { throw "No release zip at $zip" }

# ---- compile
$work = Join-Path $env:TEMP ('tc-setup-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $work | Out-Null
try {
    Copy-Item $zip (Join-Path $work 'payload.zip')
    Set-Content (Join-Path $work 'SetupInfo.cs') "static partial class SetupInfo { public const string Version = `"$ver`"; }" -Encoding UTF8
    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
    $out = Join-Path $dist "TRANSCRYPTO-Themer-Setup-$ver.exe"
    $log = & $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" "/win32icon:$(Join-Path $root 'assets\app.ico')" `
        "/win32manifest:$(Join-Path $PSScriptRoot 'setup\setup.manifest')" "/resource:$(Join-Path $work 'payload.zip'),payload.zip" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
        (Join-Path $PSScriptRoot 'setup\Setup.cs') (Join-Path $work 'SetupInfo.cs') 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Write-Host; throw 'Setup build failed' }
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }

# ---- sign
if ($PfxPath) {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword)
} else {
    $cert = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq 'CN=TRANSCRYPTO, O=TRANSCRYPTO' -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Select-Object -First 1
    if (-not $cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=TRANSCRYPTO, O=TRANSCRYPTO' -CertStoreLocation Cert:\CurrentUser\My `
            -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(5)
        Write-Host "Created a self-signed code-signing certificate (thumbprint $($cert.Thumbprint))"
    }
}
$sig = $null
try { $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert -HashAlgorithm SHA256 -TimestampServer $TimestampUrl }
catch { Write-Warning "Timestamp server not reachable ($_); signing without a timestamp"; $sig = Set-AuthenticodeSignature -FilePath $out -Certificate $cert -HashAlgorithm SHA256 }
$check = Get-AuthenticodeSignature $out
$mb = [Math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host "Built $out  (v$ver, $mb MB)"
Write-Host "Signed by: $($check.SignerCertificate.Subject)  status: $($check.Status)  timestamp: $([bool]$check.TimeStamperCertificate)"
if ($check.Status -ne 'Valid') { Write-Host '(self-signed: the file cannot be changed unnoticed, but Windows shows "Unknown publisher" and SmartScreen may warn on first run)' }

# the finished installer can also go to a folder of your choice (only the newest Themer setup is kept there):
# put that folder's path in %USERPROFILE%\.transcrypto\copy-setup-to.txt (outside the repository)
$cfg = Join-Path $env:USERPROFILE '.transcrypto\copy-setup-to.txt'
$done = if (Test-Path $cfg) { (Get-Content $cfg -Raw).Trim() } else { '' }
if ($done -and (Test-Path (Split-Path -Qualifier $done)) -and -not $NoCopy) {
    New-Item -ItemType Directory $done -Force | Out-Null
    Get-ChildItem $done -Filter 'TRANSCRYPTO-Themer-Setup-*.exe' -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne (Split-Path -Leaf $out) } | Remove-Item -Force
    Copy-Item $out $done -Force
    Write-Host "Copied to $done"
}
