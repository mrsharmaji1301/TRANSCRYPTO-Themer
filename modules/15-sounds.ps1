# Sounds: schemes (Windows default / Silent / Minimal / pack folder) + per-event custom sounds

$P_APPS = 'HKCU:\AppEvents\Schemes\Apps'
$TC.SoundEvents = [ordered]@{
    '.Default\.Default'               = 'Default beep'
    '.Default\SystemAsterisk'         = 'Asterisk / info'
    '.Default\SystemExclamation'      = 'Exclamation / warning'
    '.Default\SystemHand'             = 'Critical stop / error'
    '.Default\SystemNotification'     = 'System notification'
    '.Default\SystemQuestion'         = 'Question'
    '.Default\Notification.Default'   = 'Notification'
    '.Default\Notification.Reminder'  = 'Calendar reminder'
    '.Default\Notification.IM'        = 'Instant message'
    '.Default\Notification.Mail'      = 'New mail'
    '.Default\Notification.SMS'       = 'New text message'
    '.Default\DeviceConnect'          = 'Device connect'
    '.Default\DeviceDisconnect'       = 'Device disconnect'
    '.Default\DeviceFail'             = 'Device failed to connect'
    '.Default\WindowsUAC'             = 'User Account Control'
    '.Default\LowBatteryAlarm'        = 'Low battery'
    '.Default\CriticalBatteryAlarm'   = 'Critical battery'
    '.Default\WindowsLogon'           = 'Windows logon'
    '.Default\WindowsLogoff'          = 'Windows logoff'
    '.Default\WindowsUnlock'          = 'Windows unlock'
    '.Default\Minimize'               = 'Minimize'
    '.Default\Maximize'               = 'Maximize'
    '.Default\Close'                  = 'Close program'
    'Explorer\EmptyRecycleBin'        = 'Empty Recycle Bin'
    'Explorer\Navigating'             = 'Start navigation (click)'
    'Explorer\MoveMenuItem'           = 'Move menu item'
}
foreach ($e in $TC.SoundEvents.Keys) { Register-Tracked 'sounds' "$P_APPS\$e\.Current" '(default)' 'ExpandString' $null }
Register-Tracked 'sounds' 'HKCU:\AppEvents\Schemes' '(default)' 'String' '.Default'
$TC.MinimalKeep = @('.Default\SystemHand', '.Default\Notification.Default', '.Default\DeviceConnect', '.Default\DeviceDisconnect', '.Default\WindowsUAC', '.Default\CriticalBatteryAlarm')

# Pack folder: wav files named like the event (e.g. "Notification.Default.wav", "SystemHand.wav", "EmptyRecycleBin.wav")
$TC.SoundPacksDir = Join-Path $TC.Data 'sounds'
if (-not (Test-Path $TC.SoundPacksDir)) { New-Item -ItemType Directory $TC.SoundPacksDir -Force | Out-Null }

function Get-SoundDefault([string]$Event) { return Get-RegValue "$P_APPS\$Event\.Default" '(default)' }

function Set-TCSounds($cfg) {
    $scheme = [string]$cfg.scheme
    $map = [ordered]@{}
    foreach ($e in $TC.SoundEvents.Keys) {
        switch -Wildcard ($scheme) {
            'default' { $map[$e] = Get-SoundDefault $e }
            'silent'  { $map[$e] = '' }
            'minimal' { $map[$e] = if ($TC.MinimalKeep -contains $e) { Get-SoundDefault $e } else { '' } }
            'pack:*'  {
                $dir = Join-Path $TC.SoundPacksDir ($scheme.Substring(5))
                $leaf = Split-Path $e -Leaf
                $f = Join-Path $dir ($leaf + '.wav')
                if (Test-Path $f) { $map[$e] = $f } elseif ($cfg.packFallback -eq 'silent') { $map[$e] = '' } else { $map[$e] = Get-SoundDefault $e }
            }
        }
    }
    if ($cfg.events) { foreach ($e in $cfg.events.Keys) { $map[$e] = [string]$cfg.events[$e] } }
    foreach ($e in $map.Keys) { if ($null -ne $map[$e]) { Set-RegValue "$P_APPS\$e\.Current" '(default)' ([string]$map[$e]) 'ExpandString' } }
    $sn = switch ($scheme) { 'default' { '.Default' } 'silent' { '.None' } default { '.Current' } }
    if ($cfg.events -and $cfg.events.Count -gt 0 -and $scheme -ne 'silent') { $sn = '.Current' }
    Set-RegValue 'HKCU:\AppEvents\Schemes' '(default)' $sn 'String'
}

function Read-TCSounds {
    $ev = [ordered]@{}
    foreach ($e in $TC.SoundEvents.Keys) { $ev[$e] = Get-RegValue "$P_APPS\$e\.Current" '(default)' }
    $sn = Get-RegValue 'HKCU:\AppEvents\Schemes' '(default)'
    $scheme = switch ($sn) { '.None' { 'silent' } '.Default' { 'default' } default { 'custom' } }
    return [ordered]@{ scheme = $scheme; events = $ev }
}

Register-Component 'sounds' { param($cfg) Set-TCSounds $cfg } { Read-TCSounds }
$TCDefaultHandlers['sounds'] = { Set-TCSounds @{ scheme = 'default' } }

Register-Api 'sounds.events' {
    param($a)
    $out = @()
    foreach ($e in $TC.SoundEvents.Keys) {
        $out += @{ id = $e; label = $TC.SoundEvents[$e]; current = [Environment]::ExpandEnvironmentVariables([string](Get-RegValue "$P_APPS\$e\.Current" '(default)'))
                   windows = [Environment]::ExpandEnvironmentVariables([string](Get-SoundDefault $e)) }
    }
    return ,$out
}

Register-Api 'sounds.packs' {
    param($a)
    $out = @(@{ id = 'default'; name = 'Windows Default' }, @{ id = 'minimal'; name = 'Minimal (only important)' }, @{ id = 'silent'; name = 'Silent' })
    foreach ($d in (Get-ChildItem -LiteralPath $TC.SoundPacksDir -Directory -ErrorAction SilentlyContinue)) {
        $out += @{ id = 'pack:' + $d.Name; name = $d.Name; count = @(Get-ChildItem -LiteralPath $d.FullName -Filter *.wav).Count }
    }
    return ,$out
}

Register-Api 'sounds.packsFolder' { param($a) return $TC.SoundPacksDir }

Register-Api 'sounds.windowsMedia' {
    param($a)
    return ,@(Get-ChildItem "$env:WINDIR\Media" -Filter *.wav -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { @{ name = $_.BaseName; path = $_.FullName } })
}

Register-Api 'sounds.play' {
    param($a)
    $p = New-Object System.Media.SoundPlayer ([Environment]::ExpandEnvironmentVariables([string]$a.path))
    $p.Play()
    return $true
}
