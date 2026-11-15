# Boot Screen: UEFI boot logo (HackBGRT, bundled), Secure Boot / BitLocker checks, boot flags, startup sound

$TC.HackBGRTSrc = Join-Path $TC.Vendor 'HackBGRT'
$TC.HackBGRTWork = $null   # set per run: a fresh admin-only folder (New-TCSecureTemp), never a user-writable one
$BOOTANIM = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation'
Register-Tracked 'boot' $BOOTANIM 'DisableStartupSound' 'DWord' 1
Register-Tracked 'boot' 'HKCU:\AppEvents\EventLabels\WindowsLogon' 'ExcludeFromCPL' 'DWord' 1

function Get-FirmwareInfo {
    $uefi = Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State'
    $sb = $null
    try { $sb = Confirm-SecureBootUEFI -ErrorAction Stop } catch { $v = Get-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' 'UEFISecureBootEnabled'; if ($null -ne $v) { $sb = ($v -eq 1) } }
    $bl = 'unknown'
    try { $v = Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop; $bl = $v.ProtectionStatus.ToString() } catch {
        try { $o = (& manage-bde.exe -status $env:SystemDrive 2>$null) -join "`n"; if ($o -match 'Protection On') { $bl = 'On' } elseif ($o -match 'Protection Off') { $bl = 'Off' } } catch {}
    }
    $vendor = ''; $board = ''
    try { $bb = Get-CimInstance Win32_BaseBoard; $vendor = $bb.Manufacturer; $board = $bb.Product } catch {}
    return @{ uefi = $uefi; secureBoot = $sb; bitlocker = $bl; vendor = $vendor; board = $board }
}

function Invoke-WithEsp([scriptblock]$Body) {
    # Without admin, mountvol /S still gives the EFI partition a letter but mountvol /D is denied, so the
    # boot partition would stay visible in This PC. Never mount it from a non-elevated process.
    if (-not (Test-IsAdmin)) { throw 'Administrator rights are needed to read the EFI partition.' }
    $used = (Get-PSDrive -PSProvider FileSystem).Name
    $letter = ('S','T','U','V','W','X','Y','Z' | Where-Object { $used -notcontains $_ } | Select-Object -First 1)
    & mountvol.exe "$($letter):" /S | Out-Null
    try { return (& $Body "$($letter):\") } finally { Remove-EspLetter $letter }
}

# Take the EFI partition off its letter again; if mountvol cannot, drop just the letter link (the partition is untouched)
function Remove-EspLetter([string]$Letter) {
    & mountvol.exe "$($Letter):" /D 2>$null | Out-Null
    if (-not [IO.Directory]::Exists("$($Letter):\")) { return }
    if (-not ('TCDosDevice' -as [type])) { Add-Type -Name TCDosDevice -Namespace '' -MemberDefinition '[DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern bool DefineDosDevice(uint f, string n, string t); [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern uint QueryDosDevice(string n, System.Text.StringBuilder b, int m);' }
    $b = New-Object System.Text.StringBuilder 1024
    [void][TCDosDevice]::QueryDosDevice("$($Letter):", $b, 1024)
    # 7 = DDD_RAW_TARGET_PATH | DDD_REMOVE_DEFINITION | DDD_EXACT_MATCH_ON_REMOVE: removes only this exact letter -> volume link
    if ($b.ToString() -like '\Device\HarddiskVolume*') { [void][TCDosDevice]::DefineDosDevice(7, "$($Letter):", $b.ToString()) }
}

function Get-HackBGRTStatus {
    try {
        return (Invoke-WithEsp { param($esp)
            $dir = Join-Path $esp 'EFI\HackBGRT'
            $cfg = Join-Path $dir 'config.txt'
            @{ installed = (Test-Path $dir); config = $(if (Test-Path $cfg) { (Get-Content $cfg | Where-Object { $_ -match '^(image|resolution)=' }) -join "`n" } else { '' }) }
        })
    } catch { return @{ installed = $false; error = $_.Exception.Message } }
}

$global:TCBcdCache = $null
function Get-BcdFlags {
    # bcdedit is slow and every boot tweak asks; cache for a few seconds. We only ever set these flags to true or
    # delete them, so "listed" means on - this also works on non-English Windows where Yes/No are translated.
    if ($TCBcdCache -and ((Get-Date) - $TCBcdCache.at).TotalSeconds -lt 5) { return $TCBcdCache.v }
    $g = (& bcdedit.exe /enum '{globalsettings}' 2>$null) -join "`n"
    $d = (& bcdedit.exe /enum '{current}' 2>$null) -join "`n"
    $m = (& bcdedit.exe /enum '{bootmgr}' 2>$null) -join "`n"
    $t = 30; if ($m -match 'timeout\s+(\d+)') { $t = [int]$matches[1] }
    $v = @{
        hideLogo = ($g -match 'custom:16000067'); hideMessages = ($g -match 'custom:16000068'); hideSpinner = ($g -match 'custom:16000069')
        timeout = $t; legacyMenu = ($d -match 'bootmenupolicy\s+Legacy')
    }
    $global:TCBcdCache = @{ at = Get-Date; v = $v }
    return $v
}

Register-CustomTweak -Tab 'boot' -Section 'Boot animation' -Id 'boot.hideSpinner' -Label 'Hide the spinning dots' -Admin -Restart 'reboot' -Get { (Get-BcdFlags).hideSpinner } -Set {
    param($v) $global:TCBcdCache = $null; if ($v) { & bcdedit.exe /set '{globalsettings}' custom:16000069 true | Out-Null } else { & bcdedit.exe /deletevalue '{globalsettings}' custom:16000069 | Out-Null }
}
Register-CustomTweak -Tab 'boot' -Section 'Boot animation' -Id 'boot.hideMessages' -Label 'Hide boot messages ("Updating..." text)' -Admin -Restart 'reboot' -Get { (Get-BcdFlags).hideMessages } -Set {
    param($v) $global:TCBcdCache = $null; if ($v) { & bcdedit.exe /set '{globalsettings}' custom:16000068 true | Out-Null } else { & bcdedit.exe /deletevalue '{globalsettings}' custom:16000068 | Out-Null }
}
Register-CustomTweak -Tab 'boot' -Section 'Boot animation' -Id 'boot.hideLogo' -Label 'Hide the boot logo completely (black screen)' -Warn 'Do not combine with a custom HackBGRT logo.' -Admin -Restart 'reboot' -Get { (Get-BcdFlags).hideLogo } -Set {
    param($v) $global:TCBcdCache = $null; if ($v) { & bcdedit.exe /set '{globalsettings}' custom:16000067 true | Out-Null } else { & bcdedit.exe /deletevalue '{globalsettings}' custom:16000067 | Out-Null }
}
Register-CustomTweak -Tab 'boot' -Section 'Boot menu' -Id 'boot.timeout' -Kind 'select' -Label 'Boot menu timeout' -Admin -Restart 'reboot' -Options @(@{ v = 0; l = '0 s' }, @{ v = 3; l = '3 s' }, @{ v = 5; l = '5 s' }, @{ v = 10; l = '10 s' }, @{ v = 30; l = '30 s (default)' }) -Get { (Get-BcdFlags).timeout } -Set {
    param($v) $global:TCBcdCache = $null; & bcdedit.exe /timeout ([int]$v) | Out-Null
}
Register-CustomTweak -Tab 'boot' -Section 'Boot menu' -Id 'boot.legacyMenu' -Label 'Classic black boot menu (F8 Safe Mode)' -Admin -Restart 'reboot' -Get { (Get-BcdFlags).legacyMenu } -Set {
    param($v) $global:TCBcdCache = $null; & bcdedit.exe /set '{current}' bootmenupolicy $(if ($v) { 'Legacy' } else { 'Standard' }) | Out-Null
}
Register-RegTweak -Tab 'boot' -Section 'Startup sound' -Id 'boot.startupSound' -Label 'Play Windows startup sound' -Path $BOOTANIM -Name 'DisableStartupSound' -On 0 -Off 1 -AbsentIs 'off' -Admin

$TCDefaultHandlers['boot'] = {
    Reset-ComponentDefaults 'boot'
    foreach ($c in @('custom:16000067', 'custom:16000068', 'custom:16000069')) { & bcdedit.exe /deletevalue '{globalsettings}' $c 2>$null | Out-Null }
    & bcdedit.exe /set '{current}' bootmenupolicy Standard | Out-Null
}

Register-Api 'boot.status' {
    param($a)
    $fw = Get-FirmwareInfo
    return @{ firmware = $fw; hackbgrt = (Get-HackBGRTStatus); bundled = (Test-Path (Join-Path $TC.HackBGRTSrc 'setup.exe')); flags = (Get-BcdFlags)
              engineSound = (Get-EngineConfig).startupSound }
}

Register-Api 'boot.backupBcd' {
    param($a)
    $f = Join-Path $TC.Backups ('BCD-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bcd')
    $o = & bcdedit.exe /export $f 2>&1 | Out-String
    return @{ file = $f; output = $o.Trim() }
}

Register-Api 'boot.restoreBcd' {
    param($a)
    # only boot-configuration backups this app made itself (a foreign BCD file could change how Windows starts)
    $f = [string]$a.file
    if (-not (Test-TCChildPath $f $TC.Backups) -or [IO.Path]::GetExtension($f).ToLower() -ne '.bcd') { throw 'Only BCD backups from the TRANSCRYPTO backup folder can be restored.' }
    $o = & bcdedit.exe /import $f 2>&1 | Out-String
    return $o.Trim()
}

# Convert any picture to a 24-bit BMP (what UEFI firmwares like best), optional max height
function Convert-ToBootBmp([string]$Src, [string]$Dest, [int]$MaxH = 0) {
    $img = [System.Drawing.Image]::FromFile($Src)
    $w = $img.Width; $h = $img.Height
    if ($MaxH -gt 0 -and $h -gt $MaxH) { $w = [int]($w * $MaxH / $h); $h = $MaxH }
    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp); $g.Clear([System.Drawing.Color]::Black); $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($img, 0, 0, $w, $h); $g.Dispose(); $img.Dispose()
    $bmp.Save($Dest, [System.Drawing.Imaging.ImageFormat]::Bmp); $bmp.Dispose()
}

# mode: image | black | remove | keep ; method: entry | bcdedit | overwrite
Register-Api 'boot.applyLogo' {
    param($a)
    if (-not (Test-Path (Join-Path $TC.HackBGRTSrc 'setup.exe'))) { throw 'HackBGRT files are missing from vendor\HackBGRT' }
    $fw = Get-FirmwareInfo
    if (-not $fw.uefi) { throw 'This PC is not booting in UEFI mode, boot logo change is not possible.' }
    if ($fw.secureBoot -and -not $a.allowSecureBoot) { throw 'SECUREBOOT_ON' }
    if ($fw.bitlocker -eq 'On' -and -not $a.allowBitlocker) { throw 'BITLOCKER_ON' }

    Invoke-SafetyNet 'Boot logo change'
    $bcd = Join-Path $TC.Backups ('BCD-before-bootlogo-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.bcd'); & bcdedit.exe /export $bcd | Out-Null

    $TC.HackBGRTWork = New-TCSecureTemp 'hackbgrt'
    Copy-Item (Join-Path $TC.HackBGRTSrc '*') $TC.HackBGRTWork -Recurse -Force

    $mode = [string]$a.mode; if (-not $mode) { $mode = 'image' }
    $x = if ($null -ne $a.x) { [int]$a.x } else { 0 }; $y = if ($null -ne $a.y) { [int]$a.y } else { -200 }
    $imgLine = switch ($mode) {
        'black'  { 'image=black' }
        'remove' { 'image=remove' }
        'keep'   { 'image=keep' }
        default  {
            Convert-ToBootBmp ([string]$a.image) (Join-Path $TC.HackBGRTWork 'splash.bmp') ([int]$a.maxHeight)
            "image= x=$x y=$y path=splash.bmp"
        }
    }
    $res = if ($a.resolution) { [string]$a.resolution } else { '0x0' }
    $cfg = @('# written by TRANSCRYPTO Themer', 'boot=MS', $imgLine, "resolution=$res", 'log=1', 'debug=0')
    Set-Content -Path (Join-Path $TC.HackBGRTWork 'config.txt') -Value $cfg -Encoding UTF8

    $cmd = @('batch')
    if ($a.allowSecureBoot) { $cmd += 'allow-secure-boot' }
    if ($a.allowBitlocker) { $cmd += 'allow-bitlocker' }
    $cmd += 'install'
    switch ([string]$a.method) { 'bcdedit' { $cmd += 'enable-bcdedit' } 'overwrite' { $cmd += 'enable-overwrite' } default { $cmd += 'enable-entry' } }
    $p = Start-Process -FilePath (Join-Path $TC.HackBGRTWork 'setup.exe') -ArgumentList $cmd -WorkingDirectory $TC.HackBGRTWork -Wait -PassThru -WindowStyle Hidden `
         -RedirectStandardOutput (Join-Path $TC.Cache 'hackbgrt.out') -RedirectStandardError (Join-Path $TC.Cache 'hackbgrt.err')
    $out = ((Get-Content (Join-Path $TC.Cache 'hackbgrt.out') -ErrorAction SilentlyContinue) + (Get-Content (Join-Path $TC.Cache 'hackbgrt.err') -ErrorAction SilentlyContinue)) -join "`n"
    if (Test-Path (Join-Path $TC.HackBGRTWork 'setup.log')) { $out += "`n" + (Get-Content (Join-Path $TC.HackBGRTWork 'setup.log') -Tail 40 -ErrorAction SilentlyContinue) -join "`n" }
    return @{ ok = ($p.ExitCode -eq 0); exitCode = $p.ExitCode; output = $out.Trim(); bcdBackup = $bcd; shimNeeded = [bool]$fw.secureBoot }
}

Register-Api 'boot.removeLogo' {
    param($a)
    # uninstall finds its files on the EFI partition; run setup from a fresh admin-only copy, never from a user-writable folder
    $dir = New-TCSecureTemp 'hackbgrt'; Copy-Item (Join-Path $TC.HackBGRTSrc '*') $dir -Recurse -Force
    $p = Start-Process -FilePath (Join-Path $dir 'setup.exe') -ArgumentList @('batch', 'allow-secure-boot', 'allow-bitlocker', 'uninstall') -WorkingDirectory $dir -Wait -PassThru -WindowStyle Hidden
    return @{ ok = ($p.ExitCode -eq 0); exitCode = $p.ExitCode }
}

Register-Api 'boot.rebootToFirmware' { param($a) Start-Process shutdown.exe -ArgumentList '/r /fw /t 5'; return $true }
Register-Api 'boot.advancedStartup' { param($a) Start-Process shutdown.exe -ArgumentList '/r /o /t 5'; return $true }

# Custom startup sound is played by the engine at sign-in
Register-Api 'boot.setStartupSound' {
    param($a)
    $eng = Get-EngineConfig
    $eng.startupSound = Merge-HT $eng.startupSound (ConvertTo-HT $a.values)
    if ($eng.startupSound.enabled) {
        Set-RegValue $BOOTANIM 'DisableStartupSound' 1   # avoid double sound
        if (-not (Test-Path $TC.EngineExe)) { Build-Engine | Out-Null }
    }
    Save-EngineConfig $eng
    Sync-Engine | Out-Null
    return $eng.startupSound
}
