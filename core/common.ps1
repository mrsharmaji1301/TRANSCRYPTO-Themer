# TRANSCRYPTO Themer - shared helpers
# Windows PowerShell 5.1 compatible. Loaded once by core\server.ps1.

$ErrorActionPreference = 'Stop'
Set-StrictMode -Off

# ---------------------------------------------------------------- paths
$global:TC = @{}
$TC.Root      = Split-Path -Parent $PSScriptRoot
$TC.Data      = Join-Path $env:LOCALAPPDATA 'TRANSCRYPTO'
$TC.Library   = Join-Path $TC.Data 'library'
$TC.Backups   = Join-Path $TC.Data 'backups'
$TC.MyThemes  = Join-Path $TC.Data 'mythemes'
$TC.Logs      = Join-Path $TC.Data 'logs'
$TC.Cache     = Join-Path $TC.Data 'cache'
$TC.StateFile    = Join-Path $TC.Data 'state.json'
$TC.SettingsFile = Join-Path $TC.Data 'settings.json'
$TC.EngineFile   = Join-Path $TC.Data 'engine.json'
$TC.Assets    = Join-Path $TC.Root 'assets'
$TC.Presets   = Join-Path $TC.Root 'presets'
$TC.Vendor    = Join-Path $TC.Root 'vendor'
$TC.Bin       = Join-Path $TC.Root 'bin'
$TC.Version   = '1.0.0'
try { $TC.Version = (Get-Content (Join-Path $TC.Root 'version.json') -Raw | ConvertFrom-Json).version } catch {}
foreach ($d in @($TC.Data, $TC.Library, $TC.Backups, $TC.MyThemes, $TC.Logs, $TC.Cache)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

# ---------------------------------------------------------------- security helpers
# Update packages must carry signature.json, signed with the developer's private key (tools\new-signing-key.ps1,
# tools\make-update.ps1). This is the matching public key; a package that doesn't verify is never installed.
$TC.UpdatePublicKey = '<RSAKeyValue><Modulus>vAcBCfwAlIa7GuAZh1SY8de2UqySgTFtkTKISRkYPPiwrn3n6Ehv2I1+692ISlZc8E4vrcYxAjj2zZ67ZyvgRAesX1D3dzNx/VrcSfWnYwlRJ2idTooeIs+hGYsCv790u0f/zSqffvflwuTYCMxFUFuAdmZEGhvqYtMeDic4eNTSQpBndHe3ZhhNdpfnCaMjtWDahb61p/o+AI2nOjAGsf9h5tLSDRsaLU57jABtAGuvMWLwOyiHx63DFxe3YnkrYtZoEK24Togki+uXS+UEmpe0A043jSoyCkh/fV0ASJDWpBpBTsneupNtL3f1cTHY2b09hRIPeEA3x+mT+vc8qdFtpAR76Fi5ZgFb9f0PtXQLM7eHCRKXExWkaMyLY+yNMKEGli68mQD0zY7HVgMb11nw3k3ev2muOzZbJ21DC4pCoJIw1CSxS0EZOWqvX4IMnlHwCLGxNunB9QKZ/FjRl6VOUmB/5NNIJDq3EKveZQOSObdmcSMqAwfdGHd/MVTh</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>'

# $Path is $Root itself or inside it, after resolving "..", short names and mixed slashes (plain StartsWith is not enough)
# file types the backend may copy, import or serve; anything else (scripts, programs, documents) is refused
$TC.MediaTypes = @{
    image = @('.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp')
    video = @('.mp4', '.webm', '.mkv', '.mov', '.wmv', '.avi', '.m4v')
    icon = @('.ico'); cursor = @('.cur', '.ani'); sound = @('.wav', '.mp3'); font = @('.ttf', '.otf', '.ttc', '.fon')
}
function Test-TCFileType([string]$Path, [string[]]$Kinds) {
    $ext = [IO.Path]::GetExtension([string]$Path).ToLower()
    foreach ($k in $Kinds) { if ($TC.MediaTypes[$k] -contains $ext) { return $true } }
    return $false
}
function Assert-TCFileType([string]$Path, [string[]]$Kinds) {
    if (-not (Test-TCFileType $Path $Kinds)) { throw "Not a supported $($Kinds -join '/') file: $([IO.Path]::GetFileName([string]$Path))" }
}

function Test-TCChildPath([string]$Path, [string]$Root) {
    if (-not $Path -or -not $Root) { return $false }
    try { $p = [IO.Path]::GetFullPath($Path); $r = [IO.Path]::GetFullPath($Root).TrimEnd('\') } catch { return $false }
    return ($p -eq $r) -or $p.StartsWith($r + '\', [StringComparison]::OrdinalIgnoreCase)
}

# A fresh folder only Administrators and SYSTEM can write to (random name under %WINDIR%\Temp), for anything the
# administrator backend runs or installs. Folders a normal user can write to (LocalAppData, ProgramData) are never used
# for that: a program could swap verified files or drop a DLL there between our check and our use.
function New-TCSecureTemp([string]$Prefix) {
    $p = Join-Path "$env:WINDIR\Temp" ('TRANSCRYPTO-' + $Prefix + '-' + [Guid]::NewGuid().ToString('N'))
    if (Test-IsAdmin) {
        $sec = New-Object System.Security.AccessControl.DirectorySecurity
        $sec.SetAccessRuleProtection($true, $false)
        foreach ($id in 'BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM') {
            $sec.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
        }
        [void][IO.Directory]::CreateDirectory($p, $sec)   # created with its ACL: no moment where it is open
    } else { $p = Join-Path $TC.Cache ($Prefix + '-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory $p -Force | Out-Null }   # side/test instances only
    return $p
}

# unzip without letting an entry escape the destination ("zip slip": names like ..\..\Windows\x.dll)
function Expand-TCZip([string]$Zip, [string]$Dest) {
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    New-Item -ItemType Directory $Dest -Force | Out-Null
    $root = [IO.Path]::GetFullPath($Dest).TrimEnd('\') + '\'
    $z = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        foreach ($e in $z.Entries) {
            $target = [IO.Path]::GetFullPath((Join-Path $root ($e.FullName -replace '/', '\')))
            if (-not $target.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path inside the archive: $($e.FullName)" }
            if ($e.FullName.EndsWith('/') -or $e.FullName.EndsWith('\')) { New-Item -ItemType Directory $target -Force | Out-Null; continue }
            New-Item -ItemType Directory (Split-Path -Parent $target) -Force | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $target, $true)
        }
    } finally { $z.Dispose() }
}

# SHA-256 of a package file; text files are hashed with LF line ends so the same release verifies from a zip and from GitHub
$TC.TextExt = @('.ps1', '.psm1', '.js', '.css', '.html', '.htm', '.json', '.cs', '.md', '.txt', '.vbs', '.cmd', '.bat', '.svg', '.xml', '.ini', '.reg', '.py', '.csv')
function Get-TCFileHash([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($TC.TextExt -contains [IO.Path]::GetExtension($Path).ToLower()) {
        $t = [Text.Encoding]::UTF8.GetString($bytes)
        if ($t.Length -gt 0 -and [int]$t[0] -eq 0xFEFF) { $t = $t.Substring(1) }
        $bytes = [Text.Encoding]::UTF8.GetBytes($t.Replace("`r`n", "`n"))
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLower() } finally { $sha.Dispose() }
}

# every installable file of a package root (the same items the installer and updater copy), relative path -> hash
$TC.PackageItems = @('core', 'modules', 'ui', 'engine', 'shell', 'presets', 'assets', 'vendor', 'installer', 'version.json', 'TRANSCRYPTO.vbs', 'README.md', 'LICENSE.txt', 'Setup.cmd')
function Get-TCPackageManifest([string]$Root) {
    $root = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $m = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($item in $TC.PackageItems) {
        $p = Join-Path $root $item
        if (-not (Test-Path -LiteralPath $p)) { continue }
        $files = if ((Get-Item -LiteralPath $p).PSIsContainer) { Get-ChildItem -LiteralPath $p -Recurse -File -Force } else { @(Get-Item -LiteralPath $p) }
        foreach ($f in $files) { $m[$f.FullName.Substring($root.Length).Replace('\', '/')] = Get-TCFileHash $f.FullName }
    }
    return $m
}
function Get-TCManifestText($Manifest) { return (($Manifest.GetEnumerator() | ForEach-Object { $_.Value + '  ' + $_.Key }) -join "`n") }

# throws unless every installable file in $Root is listed in a valid signature.json with the right hash, and nothing is missing
function Assert-TCPackageSignature([string]$Root) {
    $sigFile = Join-Path $Root 'signature.json'
    if (-not (Test-Path -LiteralPath $sigFile)) { throw 'This update is not signed by TRANSCRYPTO, so it was not installed.' }
    $sig = Get-Content -LiteralPath $sigFile -Raw | ConvertFrom-Json
    $text = [string]$sig.manifest
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.FromXmlString($TC.UpdatePublicKey)
        $ok = $rsa.VerifyData([Text.Encoding]::UTF8.GetBytes($text), 'SHA256', [Convert]::FromBase64String([string]$sig.signature))
    } catch { $ok = $false } finally { $rsa.Dispose() }
    if (-not $ok) { throw 'The update signature is not valid. It was not installed.' }
    $actual = Get-TCManifestText (Get-TCPackageManifest $Root)
    if ($actual -ne $text) { throw 'The update files do not match their signature (changed or extra files). It was not installed.' }
    return $true
}

$global:API      = @{}           # 'module.action' -> scriptblock param($a)
$global:TCTracked = @{}          # component -> list of tracked registry entries
$global:TCTweaks  = New-Object System.Collections.ArrayList
$global:TCComponents = @{}       # themeable component -> @{ Apply = {param($cfg)}; Read = {} }

function Register-Api([string]$Name, [scriptblock]$Body) { $global:API[$Name] = $Body }

function Write-TCLog([string]$msg) {
    try {
        $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $msg
        Add-Content -Path (Join-Path $TC.Logs 'transcrypto.log') -Value $line -Encoding UTF8
    } catch {}
}

# ---------------------------------------------------------------- JSON
function ConvertTo-HT($obj) {
    if ($null -eq $obj) { return $null }
    if ($obj -is [System.Collections.IDictionary]) {
        $h = [ordered]@{}; foreach ($k in $obj.Keys) { $h[$k] = ConvertTo-HT $obj[$k] }; return $h
    }
    if ($obj -is [System.Management.Automation.PSCustomObject]) {
        $h = [ordered]@{}; foreach ($p in $obj.PSObject.Properties) { $h[$p.Name] = ConvertTo-HT $p.Value }; return $h
    }
    if ($obj -is [System.Collections.IEnumerable] -and -not ($obj -is [string])) {
        $l = New-Object System.Collections.ArrayList
        foreach ($i in $obj) { [void]$l.Add((ConvertTo-HT $i)) }
        return ,$l.ToArray()
    }
    return $obj
}

function Read-Json([string]$Path, $Default = $null) {
    if (-not (Test-Path $Path)) { return $Default }
    try {
        $raw = [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
        if ([string]::IsNullOrWhiteSpace($raw)) { return $Default }
        return ConvertTo-HT ($raw | ConvertFrom-Json)
    } catch { Write-TCLog "Read-Json failed $Path : $_"; return $Default }
}

function Write-Json($Obj, [string]$Path) {
    $json = $Obj | ConvertTo-Json -Depth 12
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [IO.File]::WriteAllText($Path, $json, (New-Object Text.UTF8Encoding($false)))
}

# Deep merge: values of $Over win over $Base (both hashtables)
function Merge-HT($Base, $Over) {
    $out = [ordered]@{}
    if ($Base) { foreach ($k in $Base.Keys) { $out[$k] = $Base[$k] } }
    if ($Over) {
        foreach ($k in $Over.Keys) {
            if ($out.Contains($k) -and ($out[$k] -is [System.Collections.IDictionary]) -and ($Over[$k] -is [System.Collections.IDictionary])) {
                $out[$k] = Merge-HT $out[$k] $Over[$k]
            } else { $out[$k] = $Over[$k] }
        }
    }
    return $out
}

# ---------------------------------------------------------------- registry
function Get-RegValue([string]$Path, [string]$Name) {
    try {
        if (-not (Test-Path $Path)) { return $null }
        $k = Get-Item -LiteralPath $Path
        $n = $Name; if ($Name -eq '(default)') { $n = '' }
        if ($k.GetValueNames() -notcontains $n) { return $null }
        return $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
    } catch { return $null }
}

function Get-RegKind([string]$Path, [string]$Name) {
    try {
        $k = Get-Item -LiteralPath $Path
        $n = $Name; if ($Name -eq '(default)') { $n = '' }
        return $k.GetValueKind($n).ToString()
    } catch { return 'String' }
}

function Get-NetKey([string]$Path, [bool]$Write = $false, [bool]$Create = $false) {
    $hive = $Path.Substring(0, $Path.IndexOf(':')).ToUpper()
    $sub  = $Path.Substring($Path.IndexOf(':') + 1).TrimStart('\')
    $root = switch ($hive) { 'HKLM' { [Microsoft.Win32.Registry]::LocalMachine } 'HKCR' { [Microsoft.Win32.Registry]::ClassesRoot } 'HKU' { [Microsoft.Win32.Registry]::Users } default { [Microsoft.Win32.Registry]::CurrentUser } }
    if ($Create) { return $root.CreateSubKey($sub) }
    return $root.OpenSubKey($sub, $Write)
}

function Set-RegValue([string]$Path, [string]$Name, $Value, [string]$Type = 'DWord') {
    $n = $Name; if ($Name -eq '(default)') { $n = '' }
    if ($Type -eq 'DWord') {
        if ($Value -is [bool]) { $Value = [int]$Value }
        $l = [int64]$Value
        if ($l -gt [int]::MaxValue) { $Value = [BitConverter]::ToInt32([BitConverter]::GetBytes([uint32]$l), 0) } else { $Value = [int]$l }
    }
    if ($Type -eq 'QWord') { $Value = [int64]$Value }
    if ($Type -eq 'String' -or $Type -eq 'ExpandString') { $Value = [string]$Value }
    $k = Get-NetKey $Path $true $true
    try { $k.SetValue($n, $Value, [Microsoft.Win32.RegistryValueKind]$Type) } finally { $k.Close() }
}

function Remove-RegValue([string]$Path, [string]$Name) {
    $n = $Name; if ($Name -eq '(default)') { $n = '' }
    $k = Get-NetKey $Path $true $false
    if ($null -eq $k) { return }
    try { $k.DeleteValue($n, $false) } finally { $k.Close() }
}

function Remove-RegKey([string]$Path) { if (Test-Path $Path) { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue } }

# Dump a whole key tree to a hashtable (for backup of things like DriveIcons)
function Export-RegTree([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $k = Get-Item -LiteralPath $Path
    $vals = @()
    foreach ($n in $k.GetValueNames()) {
        $kind = $k.GetValueKind($n).ToString()
        $v = $k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')
        if ($kind -eq 'Binary') { $v = [Convert]::ToBase64String([byte[]]$v) }
        $vals += @{ n = $n; t = $kind; v = $v }
    }
    $subs = @()
    foreach ($s in (Get-ChildItem -LiteralPath $Path -ErrorAction SilentlyContinue)) {
        $subs += @{ name = $s.PSChildName; tree = (Export-RegTree ($Path + '\' + $s.PSChildName)) }
    }
    return @{ values = $vals; subkeys = $subs }
}

function Import-RegTree([string]$Path, $Tree) {
    Remove-RegKey $Path
    if ($null -eq $Tree) { return }
    New-Item -Path $Path -Force | Out-Null
    foreach ($v in $Tree.values) {
        $val = $v.v
        if ($v.t -eq 'Binary') { $val = [Convert]::FromBase64String($val) }
        $name = $v.n; if ($name -eq '') { $name = '(default)' }
        Set-RegValue $Path $name $val $v.t
    }
    foreach ($s in $Tree.subkeys) {
        # a subkey name from a file must stay a single name: ".." or slashes would walk out of the tracked tree
        $sn = [string]$s.name
        if (-not $sn -or $sn -eq '.' -or $sn -eq '..' -or $sn.IndexOfAny([char[]]'\/') -ge 0) { Write-TCLog "restore: skipped unsafe subkey name '$sn'"; continue }
        Import-RegTree ($Path + '\' + $sn) $s.tree
    }
}

# ---------------------------------------------------------------- tracking (backup / restore / defaults)
# Entry: @{ p = path; n = name; t = type; d = windows default ($null => absent) } or @{ p = path; tree = $true }
function Register-Tracked([string]$Component, [string]$Path, [string]$Name, [string]$Type = 'DWord', $Default = $null, [switch]$Tree) {
    if (-not $TCTracked.ContainsKey($Component)) { $TCTracked[$Component] = New-Object System.Collections.ArrayList }
    foreach ($e in $TCTracked[$Component]) { if ($e.p -eq $Path -and $e.n -eq $Name) { return } }
    [void]$TCTracked[$Component].Add(@{ p = $Path; n = $Name; t = $Type; d = $Default; tree = [bool]$Tree })
}

function Get-ComponentSnapshot([string]$Component) {
    $out = @()
    foreach ($e in $TCTracked[$Component]) {
        if ($e.tree) { $out += @{ p = $e.p; tree = $true; data = (Export-RegTree $e.p) }; continue }
        $v = Get-RegValue $e.p $e.n
        $kind = $e.t
        if ($null -ne $v) { $kind = Get-RegKind $e.p $e.n }
        if ($kind -eq 'Binary' -and $null -ne $v) { $v = [Convert]::ToBase64String([byte[]]$v) }
        $out += @{ p = $e.p; n = $e.n; t = $kind; exists = ($null -ne $v); v = $v }
    }
    return ,$out
}

# Entries can come from a backup or theme file: only registry places TRANSCRYPTO itself tracks are ever written
# (an arbitrary path from a file, written as administrator, could make Windows run any program)
function Test-TCTrackedEntry($e) {
    foreach ($list in $TCTracked.Values) {
        foreach ($t in $list) {
            if ([string]$t.p -ne [string]$e.p) { continue }
            if ($e.tree) { if ($t.tree) { return $true } else { continue } }
            if (-not $t.tree -and [string]$t.n -eq [string]$e.n -and (-not $e.t -or [string]$t.t -eq [string]$e.t)) { return $true }
        }
    }
    return $false
}

function Restore-ComponentSnapshot($Entries) {
    foreach ($e in $Entries) {
        if (-not (Test-TCTrackedEntry $e)) { Write-TCLog "restore: skipped untracked registry entry $($e.p)$($e.n)"; continue }
        try {
            if ($e.tree) { Import-RegTree $e.p $e.data; continue }
            if (-not $e.exists) { Remove-RegValue $e.p $e.n; continue }
            $v = $e.v
            if ($e.t -eq 'Binary') { $v = [Convert]::FromBase64String($v) }
            if ($e.t -eq 'MultiString') { $v = [string[]]$v }
            Set-RegValue $e.p $e.n $v $e.t
        } catch { Write-TCLog "restore entry failed $($e.p)\$($e.n): $_" }
    }
}

function Reset-ComponentDefaults([string]$Component) {
    foreach ($e in $TCTracked[$Component]) {
        try {
            if ($e.tree) { Remove-RegKey $e.p; continue }
            if ($null -eq $e.d) { Remove-RegValue $e.p $e.n }
            else {
                $v = $e.d
                if ($e.t -eq 'Binary') { $v = [byte[]]($v -split '(..)' | Where-Object { $_ } | ForEach-Object { [Convert]::ToByte($_, 16) }) }
                Set-RegValue $e.p $e.n $v $e.t
            }
        } catch { Write-TCLog "default entry failed $($e.p)\$($e.n): $_" }
    }
}

# ---------------------------------------------------------------- tweaks (generic toggles / selects rendered by the UI)
# Kind 'toggle': On/Off registry values, AbsentIs says what Windows does when the value does not exist.
# Kind 'select': Options = @(@{v=0;l='Left'}, ...)
function Register-RegTweak {
    param([string]$Tab, [string]$Id, [string]$Label, [string]$Desc = '', [string]$Kind = 'toggle',
          [string]$Path, [string]$Name, [string]$Type = 'DWord', $On = 1, $Off = 0, [string]$AbsentIs = 'off',
          $Options = $null, $Default = $null, [string]$Restart = '', [string]$Section = 'General', [switch]$Admin, [string]$Warn = '')
    $t = @{ tab = $Tab; id = $Id; label = $Label; desc = $Desc; kind = $Kind; path = $Path; name = $Name; type = $Type;
            on = $On; off = $Off; absentIs = $AbsentIs; options = $Options; restart = $Restart; section = $Section; admin = [bool]$Admin; warn = $Warn; custom = $false }
    [void]$TCTweaks.Add($t)
    Register-Tracked $Tab $Path $Name $Type $Default
}

# Custom tweak with scriptblocks. Get returns the value, Set receives it.
function Register-CustomTweak {
    param([string]$Tab, [string]$Id, [string]$Label, [string]$Desc = '', [string]$Kind = 'toggle', [scriptblock]$Get, [scriptblock]$Set,
          $Options = $null, [string]$Restart = '', [string]$Section = 'General', [switch]$Admin, [string]$Warn = '')
    [void]$TCTweaks.Add(@{ tab = $Tab; id = $Id; label = $Label; desc = $Desc; kind = $Kind; get = $Get; set = $Set; options = $Options;
                          restart = $Restart; section = $Section; admin = [bool]$Admin; warn = $Warn; custom = $true })
}

function Get-TweakValue($t) {
    if ($t.custom) { try { return (& $t.get) } catch { return $null } }
    $v = Get-RegValue $t.path $t.name
    if ($t.kind -eq 'toggle') {
        if ($null -eq $v) { return ($t.absentIs -eq 'on') }
        return ([string]$v -eq [string]$t.on)
    }
    if ($null -eq $v) { return $t.absentIs }   # for select AbsentIs holds the default option value
    return $v
}

function Set-TweakValue($t, $value) {
    if ($t.custom) { & $t.set $value; return }
    if ($t.kind -eq 'toggle') {
        $b = [bool]$value
        $target = if ($b) { $t.on } else { $t.off }
        if ($null -eq $target) { Remove-RegValue $t.path $t.name } else { Set-RegValue $t.path $t.name $target $t.type }
    } else {
        if ($t.type -eq 'DWord') { $value = [int]$value }
        Set-RegValue $t.path $t.name $value $t.type
    }
}

# ---------------------------------------------------------------- native
if (-not ('TCNative' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

[ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDesktopWallpaper {
    void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint monitorIndex);
    uint GetMonitorDevicePathCount();
    TCRect GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorID);
    void SetBackgroundColor(uint color);
    uint GetBackgroundColor();
    void SetPosition(int position);
    int GetPosition();
    void SetSlideshow(IntPtr items);
    IntPtr GetSlideshow();
    void SetSlideshowOptions(int options, uint slideshowTick);
    void GetSlideshowOptions(out int options, out uint slideshowTick);
    void AdvanceSlideshow([MarshalAs(UnmanagedType.LPWStr)] string monitorID, int direction);
    int GetStatus();
    void Enable(bool enable);
}
[StructLayout(LayoutKind.Sequential)] public struct TCRect { public int Left, Top, Right, Bottom; }

public static class TCNative {
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, string vparam, uint winIni);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr vparam, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("shell32.dll")] public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();

    public static void Broadcast(string area) {
        IntPtr r;
        SendMessageTimeout(new IntPtr(0xffff), 0x001A, IntPtr.Zero, area, 0x0002, 3000, out r);
    }
    public static void RefreshIcons() { SHChangeNotify(0x08000000, 0x1000, IntPtr.Zero, IntPtr.Zero); }
    public static void ReloadCursors() { SystemParametersInfo(0x0057, 0, IntPtr.Zero, 0x03); }
    public static void SetWallpaperAll(string path) { SystemParametersInfo(0x0014, 0, path, 0x03); }

    // PowerShell only sees IDispatch on a returned COM object, so every IDesktopWallpaper call stays in C#.
    static IDesktopWallpaper Wallpaper() {
        Type t = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD"));
        return (IDesktopWallpaper)Activator.CreateInstance(t);
    }
    public static void WpSet(string monitorId, string path) { Wallpaper().SetWallpaper(monitorId, path); }
    public static void WpPosition(int position) { Wallpaper().SetPosition(position); }
    public static int WpGetPosition() { return Wallpaper().GetPosition(); }
    public static void WpBackground(uint colorref) { Wallpaper().SetBackgroundColor(colorref); }
    // id|left|top|right|bottom|wallpaper per monitor
    public static string[] WpMonitors() {
        IDesktopWallpaper w = Wallpaper();
        uint n = w.GetMonitorDevicePathCount();
        string[] o = new string[n];
        for (uint i = 0; i < n; i++) {
            string id = w.GetMonitorDevicePathAt(i); TCRect r = w.GetMonitorRECT(id); string wp = "";
            try { wp = w.GetWallpaper(id); } catch { }
            o[i] = id + "|" + r.Left + "|" + r.Top + "|" + r.Right + "|" + r.Bottom + "|" + wp;
        }
        return o;
    }
}
'@
}

# the custom shell hides the Windows taskbar; this brings it back (used whenever the engine is not running)
function Show-WindowsTaskbar {
    try {
        if (-not ('TCTaskbar' -as [type])) { Add-Type -Name TCTaskbar -Namespace '' -MemberDefinition '[DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);' }
        $t = [TCTaskbar]::FindWindow('Shell_TrayWnd', $null)
        if ($t -ne [IntPtr]::Zero -and -not [TCTaskbar]::IsWindowVisible($t)) { [void][TCTaskbar]::ShowWindow($t, 5) }
    } catch {}
}

function Send-SettingChange([string[]]$Areas = @('ImmersiveColorSet')) {
    foreach ($a in $Areas) { try { [TCNative]::Broadcast($a) } catch {} }
}

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    return (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Restart-Explorer {
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    for ($i = 0; $i -lt 10; $i++) { Start-Sleep -Milliseconds 500; if (Get-Process explorer -ErrorAction SilentlyContinue) { return } }
    Start-Process explorer.exe
}

# Launch a program as the normal (non elevated) desktop user
function Start-Unelevated([string]$Exe) {
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $Exe + '"')
}

function ConvertTo-ABGR([string]$Hex, [int]$Alpha = 255) {
    $h = $Hex.TrimStart('#'); $r = [Convert]::ToInt32($h.Substring(0,2),16); $g = [Convert]::ToInt32($h.Substring(2,2),16); $b = [Convert]::ToInt32($h.Substring(4,2),16)
    return [BitConverter]::ToInt32([byte[]]@($r, $g, $b, $Alpha), 0)   # little endian => 0xAABBGGRR
}
function ConvertTo-ARGB([string]$Hex, [int]$Alpha = 0xC4) {
    $h = $Hex.TrimStart('#'); $r = [Convert]::ToInt32($h.Substring(0,2),16); $g = [Convert]::ToInt32($h.Substring(2,2),16); $b = [Convert]::ToInt32($h.Substring(4,2),16)
    return [BitConverter]::ToInt32([byte[]]@($b, $g, $r, $Alpha), 0)   # => 0xAARRGGBB
}
function ConvertFrom-ABGR($Value) {
    if ($null -eq $Value) { return $null }
    $b = [BitConverter]::GetBytes([int]$Value)
    return ('#{0:X2}{1:X2}{2:X2}' -f $b[0], $b[1], $b[2])
}

# ---------------------------------------------------------------- state / settings
function Get-TCState {
    $s = Read-Json $TC.StateFile
    if (-not $s) { $s = [ordered]@{ preset = $null; overrides = [ordered]@{}; history = @() } }
    if (-not $s.overrides) { $s.overrides = [ordered]@{} }
    return $s
}
function Save-TCState($s) { Write-Json $s $TC.StateFile }

function Get-TCSettings {
    $def = [ordered]@{
        autoRestorePoint = $true; autoBackupBeforeApply = $true; updateRepo = 'mrsharmaji1301/TRANSCRYPTO-Themer'; updateBranch = 'main'
        checkUpdatesOnStart = $true; uiScale = 1; reduceMotion = $false; neon = '#00f0ff'; neon2 = '#ff2bd6'; startWithWindows = $true
    }
    $s = Read-Json $TC.SettingsFile
    return (Merge-HT $def $s)
}
function Save-TCSettings($s) { Write-Json $s $TC.SettingsFile }

function Get-EngineConfig {
    $def = [ordered]@{
        wallpaper = [ordered]@{ enabled = $false; mode = 'same'; file = ''; monitors = [ordered]@{}; fit = 'fill'; volume = 0;
                                pauseOnFullscreen = $true; pauseOnMaximized = $false; pauseOnBattery = $true; pauseOnLock = $true }
        slideshow = [ordered]@{ enabled = $false; folder = ''; minutes = 30; shuffle = $true }
        lock      = [ordered]@{ enabled = $false; file = ''; idleMinutes = 5; hotkey = 'Ctrl+Alt+L'; clock = $true; clock24 = $false; accent = '#00f0ff'; skipWhenFullscreen = $true; showDate = $true }
        startupSound = [ordered]@{ enabled = $false; file = '' }
        clickFx   = [ordered]@{ enabled = $false; style = 'ripple'; size = 90; color = 'accent'; color2 = 'accent'; rightClick = $true; skipFullscreen = $true }
        taskbarFx = [ordered]@{ enabled = $false; color = 'preset'; color2 = 'preset'; skin = $true; style = 'gradient'; strength = 0.85 }
        explorerFx = [ordered]@{ enabled = $false; style = 'gradient'; strength = 0.7; color = 'preset'; color2 = 'preset' }
        driveBars = [ordered]@{ enabled = $true; style = 'preset'; color = 'preset'; color2 = 'preset' }   # themed capacity bars in File Explorer
        presetColors = [ordered]@{ id = ''; accent = ''; accent2 = '' }
        shell     = [ordered]@{ design = 'none'; palette = ''; autohide = $false; projects = $null }   # custom taskbar + Start menu (shell\<design>.html)
        liveBg    = [ordered]@{ design = 'none'; palette = '' }                     # live web background (shell\bg-<design>.html)
        tray = $true
    }
    return (Merge-HT $def (Read-Json $TC.EngineFile))
}
function Save-EngineConfig($cfg) { Write-Json $cfg $TC.EngineFile }

# Big-change guard: optional automatic restore point (rate limited by Windows unless frequency key set)
function Invoke-SafetyNet([string]$Reason) {
    $s = Get-TCSettings
    if ($s.autoRestorePoint) {
        try { New-TCRestorePoint ("TRANSCRYPTO: " + $Reason) | Out-Null } catch { Write-TCLog "auto restore point failed: $_" }
    }
}
