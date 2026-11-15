# Backup (selective / full), restore, Windows 11 defaults, System Restore points

function New-TCRestorePoint([string]$Description) {
    $srKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore'
    Set-RegValue $srKey 'SystemRestorePointCreationFrequency' 0      # lift the 1-per-24h limit
    try { Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue } catch {}
    Checkpoint-Computer -Description $Description -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    return (Get-LastRestorePoint)
}

function Get-RestorePoints {
    $out = @()
    try {
        foreach ($r in (Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber -Descending)) {
            $dt = [Management.ManagementDateTimeConverter]::ToDateTime($r.CreationTime)
            $out += @{ seq = $r.SequenceNumber; description = $r.Description; created = $dt.ToString('o'); type = $r.RestorePointType }
        }
    } catch {}
    return ,$out
}
function Get-LastRestorePoint { $l = Get-RestorePoints; if ($l.Count -gt 0) { return $l[0] }; return $null }

# Files referenced by registry values are copied into the backup so the backup works on another PC too
function Get-ReferencedFiles($snapshots) {
    $json = $snapshots | ConvertTo-Json -Depth 12
    $files = @()
    foreach ($m in [regex]::Matches($json, '"(?:[^"]*?=)?([A-Za-z]:\\\\[^",]+?\.(jpg|jpeg|png|bmp|gif|webp|ico|cur|ani|wav|mp4|webm|mkv))', 'IgnoreCase')) {
        $p = $m.Groups[1].Value.Replace('\\', '\')
        if ((Test-Path -LiteralPath $p) -and ($files -notcontains $p) -and -not $p.StartsWith($env:WINDIR, [StringComparison]::OrdinalIgnoreCase) -and -not $p.StartsWith($TC.Root, [StringComparison]::OrdinalIgnoreCase)) { $files += $p }
    }
    return ,$files
}

function New-TCBackup([string]$Name, [string[]]$Components, [switch]$Auto, [bool]$IncludeFiles = $true, [bool]$IncludeVideos = $false) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    if (-not $Name) { $Name = 'Backup ' + (Get-Date -Format 'yyyy-MM-dd HH:mm') }
    $work = Join-Path $TC.Cache ('bk-' + $stamp); New-Item -ItemType Directory "$work\files" -Force | Out-Null
    $data = [ordered]@{}
    foreach ($c in $Components) { if ($TCTracked.ContainsKey($c)) { $data[$c] = Get-ComponentSnapshot $c } }
    $extra = [ordered]@{ engine = (Get-EngineConfig); state = (Get-TCState) }
    if ($Components -contains 'boot') { $extra.bcdFlags = (Get-BcdFlags) }
    $map = [ordered]@{}
    if ($IncludeFiles) {
        $refs = Get-ReferencedFiles @($data, $extra)
        $i = 0
        foreach ($f in $refs) {
            $ext = [IO.Path]::GetExtension($f).ToLower()
            if (-not $IncludeVideos -and @('.mp4', '.webm', '.mkv') -contains $ext) { continue }
            $i++; $nm = ('{0:D3}-{1}' -f $i, [IO.Path]::GetFileName($f))
            Copy-Item -LiteralPath $f -Destination "$work\files\$nm" -ErrorAction SilentlyContinue
            $map[$f] = $nm
        }
    }
    $manifest = [ordered]@{ app = 'TRANSCRYPTO Themer'; version = $TC.Version; name = $Name; created = (Get-Date).ToString('o'); auto = [bool]$Auto
                            machine = $env:COMPUTERNAME; components = @($data.Keys); files = $map }
    Write-Json $manifest "$work\manifest.json"
    Write-Json $data "$work\registry.json"
    Write-Json $extra "$work\extra.json"
    $prefix = if ($Auto) { 'auto-' } else { '' }
    $zip = Join-Path $TC.Backups ($prefix + $stamp + '.tcbackup')
    Compress-Archive -Path "$work\*" -DestinationPath "$zip.zip" -Force
    Move-Item "$zip.zip" $zip -Force
    Remove-Item $work -Recurse -Force
    if ($Auto) { Get-ChildItem $TC.Backups -Filter 'auto-*.tcbackup' | Sort-Object Name -Descending | Select-Object -Skip 8 | Remove-Item -Force }
    return @{ file = $zip; name = $Name; components = @($data.Keys) }
}

function Read-TCBackup([string]$File) {
    $work = Join-Path $TC.Cache ('rs-' + [Guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $File "$work.zip"; Expand-TCZip "$work.zip" $work; Remove-Item "$work.zip"
    return @{ dir = $work; manifest = (Read-Json "$work\manifest.json"); data = (Read-Json "$work\registry.json"); extra = (Read-Json "$work\extra.json") }
}

Register-Api 'backup.components' {
    param($a)
    $out = @(); foreach ($c in $TCComponentOrder) { $out += @{ id = $c; label = $TCLabels[$c]; entries = @($TCTracked[$c]).Count } }
    return ,$out
}

Register-Api 'backup.create' {
    param($a)
    $comps = @($a.components); if ($a.full -or $comps.Count -eq 0) { $comps = $TCComponentOrder }
    return (New-TCBackup -Name ([string]$a.name) -Components $comps -IncludeFiles ($a.includeFiles -ne $false) -IncludeVideos ([bool]$a.includeVideos))
}

Register-Api 'backup.list' {
    param($a)
    $out = @()
    foreach ($f in (Get-ChildItem $TC.Backups -Filter *.tcbackup -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
        $m = $null
        try {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $z = [IO.Compression.ZipFile]::OpenRead($f.FullName)
            $e = $z.GetEntry('manifest.json'); $sr = New-Object IO.StreamReader($e.Open()); $m = ConvertTo-HT ($sr.ReadToEnd() | ConvertFrom-Json); $sr.Close(); $z.Dispose()
        } catch {}
        $out += @{ file = $f.FullName; name = $(if ($m) { $m.name } else { $f.BaseName }); created = $f.LastWriteTime.ToString('o'); auto = $(if ($m) { $m.auto } else { $false })
                   components = $(if ($m) { $m.components } else { @() }); sizeKB = [math]::Round($f.Length / 1KB) }
    }
    return ,$out
}

Register-Api 'backup.delete' {
    param($a)
    $f = [string]$a.file
    if (-not (Test-TCChildPath $f $TC.Backups)) { throw 'Only backups in the TRANSCRYPTO backup folder can be deleted.' }
    Remove-Item -LiteralPath $f -Force; return $true
}
Register-Api 'backup.folder' { param($a) return $TC.Backups }

Register-Api 'backup.importFile' {
    param($a)
    if ([IO.Path]::GetExtension([string]$a.path).ToLower() -ne '.tcbackup') { throw 'Not a TRANSCRYPTO backup file (.tcbackup)' }
    $dest = Join-Path $TC.Backups ([IO.Path]::GetFileName([string]$a.path))
    Copy-Item -LiteralPath ([string]$a.path) $dest -Force
    return $dest
}

Register-Api 'backup.restore' {
    param($a)
    $b = Read-TCBackup ([string]$a.file)
    try {
        $comps = @($a.components); if ($comps.Count -eq 0) { $comps = @($b.manifest.components) }
        try { New-TCBackup -Name 'Before restore' -Components $comps -Auto | Out-Null } catch {}
        # put files back: same place if possible, otherwise into our data folder
        $restoredDir = Join-Path $TC.Data ('restored\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $remap = @{}
        if ($b.manifest.files) {
            foreach ($orig in $b.manifest.files.Keys) {
                $src = Join-Path $b.dir ('files\' + $b.manifest.files[$orig])
                if (Test-Path -LiteralPath $orig) { continue }
                $safeHome = (Test-TCChildPath $orig $env:USERPROFILE) -or (Test-TCChildPath $orig $TC.Data) -or (Test-TCChildPath $orig $env:PUBLIC)
                $safeType = @('.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp', '.mp4', '.webm', '.mkv', '.mov', '.wmv', '.ico', '.cur', '.ani', '.wav', '.mp3', '.ttf', '.otf') -contains [IO.Path]::GetExtension($orig).ToLower()
                if (-not ($safeHome -and $safeType)) { $orig = Join-Path $restoredDir ([IO.Path]::GetFileName([string]$orig)) }
                try { New-Item -ItemType Directory (Split-Path $orig) -Force -ErrorAction Stop | Out-Null; Copy-Item $src $orig -Force -ErrorAction Stop }
                catch { New-Item -ItemType Directory $restoredDir -Force | Out-Null; $np = Join-Path $restoredDir ([IO.Path]::GetFileName($orig)); Copy-Item $src $np -Force; $remap[$orig] = $np }
            }
        }
        foreach ($c in $comps) {
            if (-not $b.data.Contains($c)) { continue }
            $entries = $b.data[$c]
            if ($remap.Count -gt 0) {
                $j = $entries | ConvertTo-Json -Depth 12
                foreach ($o in $remap.Keys) { $j = $j.Replace($o.Replace('\', '\\'), $remap[$o].Replace('\', '\\')) }
                $entries = ConvertTo-HT ($j | ConvertFrom-Json)
            }
            Restore-ComponentSnapshot $entries
            if ($c -eq 'boot' -and $b.extra.bcdFlags) {
                $f = $b.extra.bcdFlags
                foreach ($pair in @(@('hideLogo', 'custom:16000067'), @('hideMessages', 'custom:16000068'), @('hideSpinner', 'custom:16000069'))) {
                    if ($f[$pair[0]]) { & bcdedit.exe /set '{globalsettings}' $pair[1] true | Out-Null } else { & bcdedit.exe /deletevalue '{globalsettings}' $pair[1] 2>$null | Out-Null }
                }
            }
        }
        if ($comps -contains 'wallpaper' -or $comps -contains 'lock' -or $comps -contains 'boot') {
            $eng = Get-EngineConfig
            if ($comps -contains 'wallpaper') { $eng.wallpaper = $b.extra.engine.wallpaper; $eng.slideshow = $b.extra.engine.slideshow }
            if ($comps -contains 'lock') { $eng.lock = $b.extra.engine.lock }
            if ($comps -contains 'boot') { $eng.startupSound = $b.extra.engine.startupSound }
            Save-EngineConfig $eng; Sync-Engine | Out-Null
        }
        if ($a.restoreThemeState -ne $false -and $b.extra.state) { Save-TCState $b.extra.state }
        $needsExplorer = $false
        foreach ($c in $comps) { if (@('startmenu', 'taskbar', 'explorer', 'icons') -contains $c) { $needsExplorer = $true } elseif ($c -ne 'wallpaper') { Invoke-ComponentRefresh $c } }
        if ($comps -contains 'wallpaper') { $w = Get-RegValue 'HKCU:\Control Panel\Desktop' 'Wallpaper'; [TCNative]::SetWallpaperAll([string]$w) }
        if ($needsExplorer) { Restart-Explorer }
        return @{ ok = $true; restored = $comps }
    } finally { Remove-Item $b.dir -Recurse -Force -ErrorAction SilentlyContinue }
}

Register-Api 'backup.defaults' {
    param($a)
    $comps = @($a.components); if ($a.all -or $comps.Count -eq 0) { $comps = $TCComponentOrder }
    Invoke-SafetyNet 'Restore Windows 11 defaults'
    try { New-TCBackup -Name 'Before Windows defaults' -Components $comps -Auto | Out-Null } catch {}
    $st = Get-TCState
    $needsExplorer = $false
    foreach ($c in $comps) {
        if ($st.overrides.Contains($c)) { $st.overrides.Remove($c) }
        if ($TCDefaultHandlers.ContainsKey($c)) { & $TCDefaultHandlers[$c] | Out-Null } else { Reset-ComponentDefaults $c }
        if (@('startmenu', 'taskbar', 'explorer', 'icons') -contains $c) { $needsExplorer = $true } else { Invoke-ComponentRefresh $c }
    }
    if ($comps -contains 'fonts') { Remove-RegValue 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes' 'Segoe UI' }
    if ((@($TCThemeable) | Where-Object { $comps -notcontains $_ }).Count -eq 0) { $st.preset = $null }
    Save-TCState $st
    if ($comps -contains 'wallpaper') { [TCNative]::SetWallpaperAll("$env:WINDIR\Web\Wallpaper\Windows\img0.jpg") }
    if ($needsExplorer) { Restart-Explorer }
    return @{ ok = $true; reset = $comps }
}

Register-Api 'restore.list'   { param($a) $r = Get-RestorePoints; return ,@($r) }
Register-Api 'restore.last'   { param($a) return (Get-LastRestorePoint) }
Register-Api 'restore.create' { param($a) $d = if ($a.description) { [string]$a.description } else { 'TRANSCRYPTO manual restore point' }; return (New-TCRestorePoint $d) }
Register-Api 'restore.openUI' { param($a) Start-Process "$env:WINDIR\System32\rstrui.exe"; return $true }
