# Start menu & taskbar layout presets: named bundles of the tweaks registered in 20-shell-tweaks.ps1

$TC.UserLayoutsFile = Join-Path $TC.Data 'layouts.json'
$TC.StartFolders = @('Settings', 'FileExplorer', 'Documents', 'Downloads', 'Music', 'Pictures', 'Videos', 'Network', 'PersonalFolder')

function Get-Tweak([string]$Id) { foreach ($t in $TCTweaks) { if ($t.id -eq $Id) { return $t } }; return $null }

function Get-Layouts([string]$Kind) {
    $built = Read-Json (Join-Path $TC.Presets 'layouts.json') @{}
    $mine = Read-Json $TC.UserLayoutsFile @{}
    $out = @()
    foreach ($l in @($built[$Kind])) { if ($l) { $l.source = 'builtin'; $out += $l } }
    foreach ($l in @($mine[$Kind])) { if ($l) { $l.source = 'mine'; $out += $l } }
    return ,$out
}

# Folders next to Power become ordinary tweak values so presets stay one flat map
function Expand-LayoutValues($Layout) {
    $v = [ordered]@{}
    foreach ($k in $Layout.values.Keys) { $v[$k] = $Layout.values[$k] }
    if ($Layout.Contains('folders')) { foreach ($f in $TC.StartFolders) { $v['start.folder.' + $f] = (@($Layout.folders) -contains $f) } }
    return $v
}

function Get-CurrentLayoutValues([string]$Kind) {
    $v = [ordered]@{}
    $prefix = if ($Kind -eq 'taskbar') { 'tb.' } else { 'start.' }
    foreach ($t in $TCTweaks) { if ($t.tab -eq $Kind -and $t.id.StartsWith($prefix)) { $v[$t.id] = Get-TweakValue $t } }
    return $v
}

Register-Api 'layouts.list' {
    param($a)
    $kind = [string]$a.kind
    $cur = Get-CurrentLayoutValues $kind
    $list = Get-Layouts $kind
    $active = $null
    foreach ($l in $list) {
        $l.values = Expand-LayoutValues $l
        $match = $true
        foreach ($k in $l.values.Keys) { if ($cur.Contains($k) -and [string]$cur[$k] -ne [string]$l.values[$k]) { $match = $false; break } }
        if ($match -and -not $active) { $active = $l.id }
    }
    return @{ layouts = $list; current = $cur; active = $active }
}

Register-Api 'layouts.apply' {
    param($a)
    $l = $null; foreach ($x in (Get-Layouts ([string]$a.kind))) { if ($x.id -eq [string]$a.id) { $l = $x } }
    if (-not $l) { throw "Layout not found: $($a.id)" }
    $vals = Expand-LayoutValues $l
    $failed = @()
    foreach ($k in $vals.Keys) {
        $t = Get-Tweak $k; if (-not $t) { continue }
        try { Set-TweakValue $t $vals[$k] } catch { $failed += $t.label; Write-TCLog "layout $($l.id) / $k failed: $_" }
    }
    Send-SettingChange @('TraySettings', 'ImmersiveColorSet', 'Policy')
    Restart-Explorer
    return @{ ok = $true; failed = $failed }
}

Register-Api 'layouts.save' {
    param($a)
    $kind = [string]$a.kind; $name = [string]$a.name
    $mine = Read-Json $TC.UserLayoutsFile ([ordered]@{})
    $cur = Get-CurrentLayoutValues $kind
    $entry = [ordered]@{ id = 'my-' + (($name.ToLower() -replace '[^a-z0-9]+', '-').Trim('-')); name = $name; desc = 'Saved ' + (Get-Date -Format 'yyyy-MM-dd'); values = [ordered]@{} }
    if ($kind -eq 'startmenu') { $entry.folders = @($TC.StartFolders | Where-Object { $cur['start.folder.' + $_] }) }
    foreach ($k in $cur.Keys) { if (-not $k.StartsWith('start.folder.')) { $entry.values[$k] = $cur[$k] } }
    $list = @(@($mine[$kind]) | Where-Object { $_ -and $_.id -ne $entry.id }) + $entry
    $mine[$kind] = $list
    Write-Json $mine $TC.UserLayoutsFile
    return $entry
}

Register-Api 'layouts.delete' {
    param($a)
    $mine = Read-Json $TC.UserLayoutsFile ([ordered]@{})
    $mine[[string]$a.kind] = @(@($mine[[string]$a.kind]) | Where-Object { $_ -and $_.id -ne [string]$a.id })
    Write-Json $mine $TC.UserLayoutsFile
    return $true
}
