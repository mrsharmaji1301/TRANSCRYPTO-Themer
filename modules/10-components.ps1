# Layer system: preset (base) + per-component user overrides. Also the generic tweak API.
#
#   effective(component) = merge(preset[component], overrides[component])
#
# Themeable components register themselves with Register-Component in their own module.

$global:TCRefresh = @{}          # component -> scriptblock run after restore/defaults
$global:TCDefaultHandlers = @{}  # component -> scriptblock that resets to Windows defaults (instead of registry table)
$global:TCComponentOrder = @('colors', 'wallpaper', 'icons', 'cursors', 'sounds', 'startmenu', 'taskbar', 'explorer', 'lock', 'boot', 'fonts')
$global:TCThemeable = @('colors', 'wallpaper', 'icons', 'cursors', 'sounds')
$global:TCLabels = @{ colors = 'Colors & Accent'; wallpaper = 'Wallpaper'; icons = 'Icons & Thumbnails'; cursors = 'Cursors'; sounds = 'Sounds'
                      startmenu = 'Start Menu'; taskbar = 'Taskbar'; explorer = 'File Explorer'; lock = 'Lock & Login'; boot = 'Boot Screen'; fonts = 'Fonts' }

function Register-Component([string]$Name, [scriptblock]$Apply, [scriptblock]$Read, [scriptblock]$Refresh = $null) {
    $TCComponents[$Name] = @{ Apply = $Apply; Read = $Read }
    if ($Refresh) { $TCRefresh[$Name] = $Refresh }
}

function Get-PresetById([string]$Id) {
    if (-not $Id) { return $null }
    foreach ($p in (Get-AllPresets)) { if ($p.id -eq $Id) { return $p } }
    return $null
}

function Get-Effective([string]$Component, $State = $null) {
    if (-not $State) { $State = Get-TCState }
    $base = $null
    $preset = Get-PresetById $State.preset
    if ($preset -and $preset.Contains($Component)) { $base = $preset[$Component] }
    $over = $null; if ($State.overrides.Contains($Component)) { $over = $State.overrides[$Component] }
    return (Merge-HT $base $over)
}

function Invoke-ComponentApply([string]$Component, $Cfg) {
    if (-not $TCComponents.ContainsKey($Component)) { return }
    if (-not $Cfg -or $Cfg.Count -eq 0) { return }
    & $TCComponents[$Component].Apply $Cfg | Out-Null
}

function Invoke-ComponentRefresh([string]$Component) {
    if ($TCRefresh.ContainsKey($Component)) { try { & $TCRefresh[$Component] | Out-Null } catch { Write-TCLog "refresh $Component failed: $_" } }
}

function Reset-ToWindowsDefault([string]$Component) {
    if ($TCDefaultHandlers.ContainsKey($Component)) { & $TCDefaultHandlers[$Component] | Out-Null }
    else { Reset-ComponentDefaults $Component }
    Invoke-ComponentRefresh $Component
}

Register-Api 'comp.get' {
    param($a)
    $c = [string]$a.component
    $st = Get-TCState
    $cur = $null
    if ($TCComponents.ContainsKey($c) -and $TCComponents[$c].Read) { try { $cur = & $TCComponents[$c].Read } catch { Write-TCLog "read $c failed: $_" } }
    $eff = Get-Effective $c $st
    $over = $null; if ($st.overrides.Contains($c)) { $over = $st.overrides[$c] }
    return @{ component = $c; current = $cur; effective = $eff; override = $over; customized = ($null -ne $over -and $over.Count -gt 0); preset = $st.preset
              values = (Merge-HT $cur $eff) }
}

# values = partial config. Stored as override on top of the preset, then the full effective config is applied.
Register-Api 'comp.set' {
    param($a)
    $c = [string]$a.component
    $vals = ConvertTo-HT $a.values
    $st = Get-TCState
    $old = $null; if ($st.overrides.Contains($c)) { $old = $st.overrides[$c] }
    $st.overrides[$c] = Merge-HT $old $vals
    Save-TCState $st
    Invoke-ComponentApply $c (Get-Effective $c $st)
    return @{ ok = $true; effective = (Get-Effective $c $st); customized = $true }
}

# Drop my overrides for a component -> back to the preset value (or Windows default when no preset)
Register-Api 'comp.reset' {
    param($a)
    $c = [string]$a.component
    $st = Get-TCState
    if ($st.overrides.Contains($c)) { $st.overrides.Remove($c) }
    Save-TCState $st
    $eff = Get-Effective $c $st
    if ($eff -and $eff.Count -gt 0) { Invoke-ComponentApply $c $eff } else { Reset-ToWindowsDefault $c }
    return $true
}

Register-Api 'comp.state' {
    param($a)
    $st = Get-TCState
    $custom = @(); foreach ($k in $st.overrides.Keys) { if ($st.overrides[$k] -and $st.overrides[$k].Count -gt 0) { $custom += $k } }
    return @{ preset = $st.preset; customized = $custom; labels = $TCLabels; order = $TCComponentOrder; themeable = $TCThemeable }
}

# ---------------------------------------------------------------- generic tweaks
Register-Api 'tweaks.list' {
    param($a)
    $tab = [string]$a.tab
    $out = @()
    foreach ($t in $TCTweaks) {
        if ($tab -and $t.tab -ne $tab) { continue }
        $v = Get-TweakValue $t
        $out += @{ id = $t.id; tab = $t.tab; label = $t.label; desc = $t.desc; kind = $t.kind; value = $v; options = $t.options
                   restart = $t.restart; section = $t.section; admin = $t.admin; warn = $t.warn }
    }
    return ,$out
}

Register-Api 'tweaks.set' {
    param($a)
    $t = $null; foreach ($x in $TCTweaks) { if ($x.id -eq [string]$a.id) { $t = $x } }
    if (-not $t) { throw "Unknown tweak $($a.id)" }
    Set-TweakValue $t $a.value
    switch ($t.restart) {
        'explorer-live' { Send-SettingChange @('TraySettings', 'ImmersiveColorSet', 'Policy') }
        'explorer'      { if ($a.restartNow) { Restart-Explorer } else { Send-SettingChange @('TraySettings', 'Policy') } }
    }
    return @{ ok = $true; value = (Get-TweakValue $t); restart = $t.restart }
}

Register-Api 'tweaks.resetTab' {
    param($a)
    Reset-ToWindowsDefault ([string]$a.tab)
    return $true
}
