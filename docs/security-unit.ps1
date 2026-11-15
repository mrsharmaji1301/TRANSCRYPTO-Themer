# Dev-only: unit checks for the update signature, safe unzip and the registry restore allowlist (no admin, no installs).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'core\common.ps1')
foreach ($m in (Get-ChildItem (Join-Path $root 'modules') -Filter '*.ps1' | Sort-Object Name)) { try { . $m.FullName } catch {} }   # registers the tracked registry entries
$pass = 0; $fail = 0
function Check([string]$Name, [bool]$Ok, [string]$Info = '') { if ($Ok) { $script:pass++ } else { $script:fail++ }; '{0}  {1}{2}' -f $(if ($Ok) { 'PASS' } else { 'FAIL' }), $Name, $(if ($Info) { "  ($Info)" } else { '' }) }
function Throws([scriptblock]$Sb) { try { & $Sb | Out-Null; return $false } catch { return $true } }

$work = Join-Path $env:TEMP ('tc-sec-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $work | Out-Null
try {
    # ---- a small fake package, signed with the real private key the same way tools\make-update.ps1 does
    $pkg = Join-Path $work 'pkg'
    foreach ($d in 'core', 'modules', 'shell') { New-Item -ItemType Directory (Join-Path $pkg $d) -Force | Out-Null }
    Set-Content (Join-Path $pkg 'core\server.ps1') "# server`r`nWrite-Host hi" -NoNewline
    Set-Content (Join-Path $pkg 'modules\a.ps1') 'function A {}' -NoNewline
    Set-Content (Join-Path $pkg 'version.json') '{"app":"TRANSCRYPTO Themer","version":"9.9.9"}' -NoNewline
    $sign = {
        param($dir)
        $man = Get-TCPackageManifest $dir; $text = Get-TCManifestText $man
        $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
        $rsa.FromXmlString([IO.File]::ReadAllText((Join-Path $env:USERPROFILE '.transcrypto\update-signing-key.xml')))
        $sig = [Convert]::ToBase64String($rsa.SignData([Text.Encoding]::UTF8.GetBytes($text), 'SHA256')); $rsa.Dispose()
        [IO.File]::WriteAllText((Join-Path $dir 'signature.json'), (@{ manifest = $text; signature = $sig } | ConvertTo-Json -Compress))
    }
    & $sign $pkg
    Check 'signed package verifies' (-not (Throws { Assert-TCPackageSignature $pkg }))

    # line endings changed (as GitHub may serve them): still the same release
    $crlf = Join-Path $pkg 'core\server.ps1'; [IO.File]::WriteAllText($crlf, ([IO.File]::ReadAllText($crlf) -replace "`r`n", "`n"))
    Check 'LF/CRLF difference still verifies' (-not (Throws { Assert-TCPackageSignature $pkg }))

    Set-Content (Join-Path $pkg 'modules\a.ps1') 'function A { Start-Process calc }' -NoNewline
    Check 'changed file is refused' (Throws { Assert-TCPackageSignature $pkg })
    & $sign $pkg
    Set-Content (Join-Path $pkg 'modules\evil.ps1') 'Start-Process calc' -NoNewline
    Check 'extra (unsigned) file is refused' (Throws { Assert-TCPackageSignature $pkg })
    Remove-Item (Join-Path $pkg 'modules\evil.ps1')
    $s = Get-Content (Join-Path $pkg 'signature.json') -Raw | ConvertFrom-Json
    $s.signature = [Convert]::ToBase64String([byte[]](1..384 | ForEach-Object { $_ % 256 })); [IO.File]::WriteAllText((Join-Path $pkg 'signature.json'), ($s | ConvertTo-Json -Compress))
    Check 'forged signature is refused' (Throws { Assert-TCPackageSignature $pkg })
    Remove-Item (Join-Path $pkg 'signature.json')
    Check 'missing signature is refused' (Throws { Assert-TCPackageSignature $pkg })

    # ---- zip slip
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $zip = Join-Path $work 'slip.zip'
    $z = [IO.Compression.ZipFile]::Open($zip, 'Create')
    $e = $z.CreateEntry('../../escaped-by-zip.txt'); $w = New-Object IO.StreamWriter($e.Open()); $w.Write('x'); $w.Close(); $z.Dispose()
    $dest = Join-Path $work 'out\a\b'
    Check 'zip entry with ..\ is refused' ((Throws { Expand-TCZip $zip $dest }) -and -not (Test-Path (Join-Path $work 'out\escaped-by-zip.txt')))

    # ---- registry restore allowlist
    $evil = @{ p = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\notepad.exe'; n = 'Debugger'; t = 'String'; v = 'calc.exe'; exists = $true }
    Check 'untracked registry entry (IFEO debugger) is refused' (-not (Test-TCTrackedEntry $evil))
    $first = $null; foreach ($l in $TCTracked.Values) { foreach ($t in $l) { if (-not $t.tree) { $first = $t; break } }; if ($first) { break } }
    Check 'tracked registry entry is allowed' (Test-TCTrackedEntry @{ p = $first.p; n = $first.n; t = $first.t }) "$($first.p)\$($first.n)"
    Check 'path helper: library\..\x is outside' (-not (Test-TCChildPath (Join-Path $TC.Library '..\x') $TC.Library))
    Check 'path helper: sibling folder with the same prefix is outside' (-not (Test-TCChildPath ($TC.Library + '-evil\x') $TC.Library))
} finally { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
"`n$pass/$($pass + $fail) passed"
