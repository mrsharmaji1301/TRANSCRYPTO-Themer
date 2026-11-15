# Creates the update signing key pair (once). The PRIVATE key stays on the developer PC, outside the repo:
#   %USERPROFILE%\.transcrypto\update-signing-key.xml   (keep a safe offline copy; never commit or share it)
# The PUBLIC key is printed; it belongs in core\common.ps1 ($TC.UpdatePublicKey). Every update package must be signed
# with the private key (tools\make-update.ps1 does it), or the app refuses to install it.
param([switch]$Force)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE '.transcrypto'
$key = Join-Path $dir 'update-signing-key.xml'
if ((Test-Path $key) -and -not $Force) { throw "A signing key already exists: $key (use -Force only if you really want to replace it; old updates then stop verifying)" }
New-Item -ItemType Directory $dir -Force | Out-Null
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider 3072
try {
    [IO.File]::WriteAllText($key, $rsa.ToXmlString($true))
    # only this user (and SYSTEM / Administrators) may read the private key
    $acl = New-Object System.Security.AccessControl.FileSecurity
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($id in @([Security.Principal.WindowsIdentity]::GetCurrent().Name, 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators')) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'Allow')))
    }
    Set-Acl -LiteralPath $key -AclObject $acl
    "Private key: $key"
    "Public key (for core\common.ps1):"
    $rsa.ToXmlString($false)
} finally { $rsa.Dispose() }
