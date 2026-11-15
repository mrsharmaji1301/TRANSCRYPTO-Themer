# Builds the single-file update for TRANSCRYPTO Themer: dist\TRANSCRYPTO-Themer-<version>.zip
# Before running: bump "version" in version.json and list what changed in "changes", e.g.
#   "changes": [ { "type": "fixed", "text": "Wallpapers tab crashed with one monitor" },
#                { "type": "new", "text": "Neon hover glow on taskbar buttons" } ]
# type is fixed, new or improved. Install the zip from Settings > Updates > "Install update file…",
# or push the same files to GitHub and use "Check now".
# Every package is signed (signature.json): the app refuses unsigned or altered updates. The private key lives in
# %USERPROFILE%\.transcrypto\update-signing-key.xml (tools\new-signing-key.ps1). signature.json is also written to the
# repo root, so commit it with the release for GitHub updates.
param([string]$OutDir, [string]$KeyFile = (Join-Path $env:USERPROFILE '.transcrypto\update-signing-key.xml'))
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'core\common.ps1')   # Get-TCPackageManifest / Get-TCManifestText: the very code the app verifies with
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }

$m = Get-Content (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
if (-not $m.version) { throw 'version.json has no "version"' }
[void][version]$m.version
if (-not @($m.changes).Count) { Write-Warning 'version.json has no "changes" - the app will not be able to show what this update fixes.' }
foreach ($c in @($m.changes)) { if ($c.type -notin 'fixed', 'new', 'improved') { throw "Unknown change type '$($c.type)' (use fixed, new or improved)" } }

# ---- sign the release
if (-not (Test-Path -LiteralPath $KeyFile)) { throw "No signing key at $KeyFile (run tools\new-signing-key.ps1 once)" }
$manifest = Get-TCPackageManifest $root
$text = Get-TCManifestText $manifest
$rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
$pub = New-Object System.Security.Cryptography.RSACryptoServiceProvider
try {
    $rsa.FromXmlString([IO.File]::ReadAllText($KeyFile))
    $sig = [Convert]::ToBase64String($rsa.SignData([Text.Encoding]::UTF8.GetBytes($text), 'SHA256'))
    $pub.FromXmlString($TC.UpdatePublicKey)
    if (-not $pub.VerifyData([Text.Encoding]::UTF8.GetBytes($text), 'SHA256', [Convert]::FromBase64String($sig))) { throw 'The signing key does not match the public key in core\common.ps1' }
} finally { $rsa.Dispose(); $pub.Dispose() }
$sigPath = Join-Path $root 'signature.json'
[IO.File]::WriteAllText($sigPath, ([ordered]@{ version = $m.version; files = $manifest.Count; manifest = $text; signature = $sig } | ConvertTo-Json -Compress))

# ---- zip exactly the signed files, plus the signature
New-Item -ItemType Directory $OutDir -Force | Out-Null
$zip = Join-Path $OutDir ("TRANSCRYPTO-Themer-{0}.zip" -f $m.version)
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$z = [IO.Compression.ZipFile]::Open($zip, 'Create')
$n = 0
try {
    # forward slashes so every unzip tool (Windows, macOS, Linux, GitHub) reads the same paths
    foreach ($rel in $manifest.Keys) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, (Join-Path $root ($rel -replace '/', '\')), $rel, 'Optimal'); $n++ }
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($z, $sigPath, 'signature.json', 'Optimal')
} finally { $z.Dispose() }

# ---- the app's own check must accept what we just built
$check = Join-Path $env:TEMP ('tc-verify-' + [Guid]::NewGuid().ToString('N'))
try { Expand-TCZip $zip $check; Assert-TCPackageSignature $check | Out-Null } finally { Remove-Item $check -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host ("Built {0}  (v{1}, {2} files, {3:N1} MB, signed and verified)" -f $zip, $m.version, $n, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
foreach ($c in @($m.changes)) { Write-Host ("  [{0}] {1}" -f $c.type, $c.text) }
