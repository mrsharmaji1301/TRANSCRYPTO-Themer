# Dashboard: system summary (cached), protection status, what is active

$global:TCSysCache = $null

Register-Api 'dashboard.get' {
    param($a)
    if (-not $TCSysCache -or $a.refresh) {
        $cv = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
        $cpu = ''; $gpu = ''; $ram = 0
        try { $cpu = (Get-CimInstance Win32_Processor | Select-Object -First 1).Name.Trim() } catch {}
        try { $gpu = ((Get-CimInstance Win32_VideoController | Where-Object { $_.Name -notmatch 'Basic|Virtual|Parsec|Meta' } | ForEach-Object { $_.Name }) -join ', ') } catch {}
        try { $ram = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB) } catch {}
        $global:TCSysCache = @{
            os = $cv.ProductName -replace 'Windows 10', 'Windows 11'; edition = $cv.EditionID; version = $cv.DisplayVersion
            build = ('{0}.{1}' -f $cv.CurrentBuild, $cv.UBR); computer = $env:COMPUTERNAME; user = $env:USERNAME; cpu = $cpu; gpu = $gpu; ramGB = $ram
        }
    }
    $st = Get-TCState
    $preset = Get-PresetById $st.preset
    $custom = @(); foreach ($k in $st.overrides.Keys) { if ($st.overrides[$k] -and $st.overrides[$k].Count -gt 0) { $custom += $TCLabels[$k] } }
    $eng = Get-Process TCWall -ErrorAction SilentlyContinue | Select-Object -First 1
    $me = Get-Process -Id $PID
    $sb = Get-RegValue 'HKLM:\SYSTEM\CurrentControlSet\Control\SecureBoot\State' 'UEFISecureBootEnabled'
    $up = (Get-Date) - (Get-CimInstance Win32_OperatingSystem).LastBootUpTime
    $lastBk = Get-ChildItem $TC.Backups -Filter *.tcbackup -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $cfg = Get-EngineConfig
    return @{
        system = $TCSysCache; uptime = ('{0}d {1}h {2}m' -f $up.Days, $up.Hours, $up.Minutes)
        secureBoot = $(if ($null -eq $sb) { 'n/a' } elseif ($sb -eq 1) { 'On' } else { 'Off' })
        lastRestorePoint = (Get-LastRestorePoint)
        lastBackup = $(if ($lastBk) { @{ name = $lastBk.Name; at = $lastBk.LastWriteTime.ToString('o') } } else { $null })
        preset = $(if ($preset) { @{ id = $preset.id; name = $preset.name; palette = $preset.palette } } else { $null })
        customized = $custom
        engine = @{ running = [bool]$eng; ramMB = $(if ($eng) { [math]::Round($eng.WorkingSet64 / 1MB, 1) } else { 0 }); live = $cfg.wallpaper.enabled; lock = $cfg.lock.enabled; slideshow = $cfg.slideshow.enabled }
        backendMB = [math]::Round($me.WorkingSet64 / 1MB, 1)
        admin = (Test-IsAdmin); version = $TC.Version
    }
}
