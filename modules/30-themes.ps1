# Themes: built-in presets + My Themes, apply (full / keep my customizations), live preview, export / import

function Get-AllPresets {
    $list = @()
    $built = Read-Json (Join-Path $TC.Presets 'themes.json') @{ presets = @() }
    foreach ($p in $built.presets) { $p.source = 'builtin'; $list += $p }
    foreach ($f in (Get-ChildItem -LiteralPath $TC.MyThemes -Filter *.json -ErrorAction SilentlyContinue)) {
        $p = Read-Json $f.FullName; if ($p -and $p.id) { $p.source = 'mine'; $p.file = $f.FullName; $list += $p }
    }
    return ,$list
}

function Get-PreviewFile { return (Join-Path $TC.Cache 'preview-snapshot.json') }

function New-ThemeableSnapshot {
    $snap = [ordered]@{ engine = (Get-EngineConfig); state = (Get-TCState); comps = [ordered]@{} }
    foreach ($c in $TCThemeable) { $snap.comps[$c] = Get-ComponentSnapshot $c }
    return $snap
}

function Restore-ThemeableSnapshot($snap) {
    foreach ($c in $snap.comps.Keys) { Restore-ComponentSnapshot $snap.comps[$c] }
    Save-EngineConfig $snap.engine
    Save-TCState $snap.state
    foreach ($c in $snap.comps.Keys) { Invoke-ComponentRefresh $c }
    $eng = $snap.engine
    if ($eng.wallpaper.enabled -or $eng.slideshow.enabled) { Sync-Engine | Out-Null }
}

function Invoke-PresetApply($preset, [bool]$KeepOverrides, [bool]$SaveState = $true) {
    $st = Get-TCState
    if (-not $KeepOverrides) { $st.overrides = [ordered]@{} }
    $st.preset = $preset.id
    foreach ($c in $TCThemeable) {
        $base = $null; if ($preset.Contains($c)) { $base = $preset[$c] }
        $over = $null; if ($st.overrides.Contains($c)) { $over = $st.overrides[$c] }
        $eff = Merge-HT $base $over
        try { Invoke-ComponentApply $c $eff } catch { Write-TCLog "apply $c failed: $_" }
    }
    # engine effects set to "preset" colors (taskbar glow, Explorer tint) follow the preset's accent pair;
    # the engine watches engine.json and redraws by itself
    if ($preset.palette) {
        try {
            $ec = Get-EngineConfig
            $ec.presetColors = [ordered]@{ id = [string]$preset.id; accent = [string]$preset.palette.accent; accent2 = [string]$preset.palette.accent2 }
            # full shell presets bring their taskbar + Start menu and live background. Every other preset gives the plain Windows
            # taskbar back: no custom shell, no shell scene behind the desktop, no taskbar skin (a theme switch must not leave the
            # previous theme's bar behind)
            if ($preset.Contains('shell')) { $ec.shell = Merge-HT $ec.shell $preset.shell }
            else { $ec.shell.design = 'none'; $ec.taskbarFx.enabled = $false }
            if ($preset.Contains('liveBg')) { $ec.liveBg = Merge-HT $ec.liveBg $preset.liveBg; if ([string]$preset.liveBg.design -ne 'none') { $ec.wallpaper.enabled = $false } }
            elseif (-not $preset.Contains('shell') -and -not ($st.overrides.Contains('wallpaper') -and [string]$st.overrides.wallpaper.type -eq 'web')) { $ec.liveBg.design = 'none' }   # a scene you picked yourself stays
            Save-EngineConfig $ec
            [void](Sync-Engine)   # also starts the engine for the themed drive bars when nothing else needed it
        } catch { Write-TCLog "preset colors for engine failed: $_" }
    }
    if ($SaveState) {
        $h = @($st.history); $h = @(@{ id = $preset.id; at = (Get-Date).ToString('o') }) + $h | Select-Object -First 15
        $st.history = $h
        Save-TCState $st
    }
}

Register-Api 'themes.list' {
    param($a)
    $st = Get-TCState
    return @{ presets = (Get-AllPresets); active = $st.preset; previewing = (Test-Path (Get-PreviewFile)) }
}

# mode: 'full' (preset replaces everything) | 'keep' (preset under my customizations)
Register-Api 'themes.apply' {
    param($a)
    $p = Get-PresetById ([string]$a.id); if (-not $p) { throw "Preset not found: $($a.id)" }
    $s = Get-TCSettings
    if ($s.autoBackupBeforeApply) { try { New-TCBackup -Name ('Before ' + $p.name) -Components $TCThemeable -Auto | Out-Null } catch { Write-TCLog "auto backup failed: $_" } }
    Remove-Item (Get-PreviewFile) -ErrorAction SilentlyContinue
    Invoke-PresetApply $p ([string]$a.mode -eq 'keep')
    return @{ ok = $true; active = $p.id }
}

# Live preview on the real desktop, then revert or keep
Register-Api 'themes.preview' {
    param($a)
    $p = Get-PresetById ([string]$a.id); if (-not $p) { throw "Preset not found" }
    $pf = Get-PreviewFile
    if (-not (Test-Path $pf)) { Write-Json (New-ThemeableSnapshot) $pf }
    Invoke-PresetApply $p $false $false
    return @{ previewing = $p.id }
}

Register-Api 'themes.previewEnd' {
    param($a)
    $pf = Get-PreviewFile
    if (-not (Test-Path $pf)) { return $false }
    if ($a.keep -and $a.id) {
        Remove-Item $pf -Force
        $p = Get-PresetById ([string]$a.id); Invoke-PresetApply $p $false
        return @{ kept = $a.id }
    }
    $snap = Read-Json $pf
    Restore-ThemeableSnapshot $snap
    Remove-Item $pf -Force
    return @{ reverted = $true }
}

# Save the current effective look (preset + my overrides) as a new theme
Register-Api 'themes.saveMine' {
    param($a)
    $st = Get-TCState
    $name = [string]$a.name; if (-not $name) { $name = 'My Theme ' + (Get-Date -Format 'yyyy-MM-dd HH.mm') }
    $id = 'my-' + (($name.ToLower() -replace '[^a-z0-9]+', '-').Trim('-'))
    $base = Get-PresetById $st.preset
    $t = [ordered]@{ id = $id; name = $name; desc = [string]$a.desc; author = $env:USERNAME; tags = @('mine'); created = (Get-Date).ToString('o') }
    $t.palette = if ($a.palette) { ConvertTo-HT $a.palette } elseif ($base) { $base.palette } else { @{ bg = '#101018'; surface = '#1a1a26'; text = '#e8e8f0'; accent = '#00f0ff'; accent2 = '#ff2bd6' } }
    foreach ($c in $TCThemeable) {
        $eff = Get-Effective $c $st
        if ((-not $eff -or $eff.Count -eq 0) -and $TCComponents[$c].Read) { $eff = & $TCComponents[$c].Read }
        $t[$c] = $eff
    }
    if ($t.colors -and $t.colors.accent) { $t.palette.accent = $t.colors.accent }
    Write-Json $t (Join-Path $TC.MyThemes ($id + '.json'))
    $st.preset = $id; $st.overrides = [ordered]@{}; Save-TCState $st
    return $t
}

Register-Api 'themes.deleteMine' {
    param($a)
    $f = Join-Path $TC.MyThemes ([IO.Path]::GetFileName([string]$a.id) + '.json'); if ((Test-TCChildPath $f $TC.MyThemes) -and (Test-Path -LiteralPath $f)) { Remove-Item -LiteralPath $f -Force }
    return $true
}

# Export a theme with every file it references (wallpaper, icons, cursors, sounds) into a .tctheme (zip)
Register-Api 'themes.export' {
    param($a)
    $p = Get-PresetById ([string]$a.id); if (-not $p) { throw 'Theme not found' }
    $work = Join-Path $TC.Cache ('export-' + [Guid]::NewGuid().ToString('N')); New-Item -ItemType Directory "$work\files" -Force | Out-Null
    $json = $p | ConvertTo-Json -Depth 12
    $i = 0
    foreach ($m in [regex]::Matches($json, '"([A-Za-z]:\\\\[^"]+?\.(jpg|jpeg|png|bmp|gif|webp|mp4|webm|mkv|mov|ico|cur|ani|wav))"', 'IgnoreCase')) {
        $orig = $m.Groups[1].Value.Replace('\\', '\')
        if (-not (Test-Path -LiteralPath $orig)) { continue }
        if ($orig.StartsWith($TC.Root, [StringComparison]::OrdinalIgnoreCase)) { continue }   # app assets travel with the app
        $i++; $nm = ('{0:D3}-{1}' -f $i, [IO.Path]::GetFileName($orig))
        Copy-Item -LiteralPath $orig -Destination "$work\files\$nm"
        $json = $json.Replace($m.Groups[1].Value, ('{{FILES}}\\' + $nm))
    }
    [IO.File]::WriteAllText("$work\theme.json", $json, (New-Object Text.UTF8Encoding($false)))
    $dest = if ($a.dest) { [string]$a.dest } else { Join-Path ([Environment]::GetFolderPath('Desktop')) ($p.name + '.tctheme') }
    if ([IO.Path]::GetExtension($dest).ToLower() -ne '.tctheme') { throw 'A theme can only be exported as a .tctheme file.' }
    $zip = $dest + '.zip'; Remove-Item $zip, $dest -ErrorAction SilentlyContinue
    Compress-Archive -Path "$work\*" -DestinationPath $zip -Force
    Move-Item $zip $dest -Force
    Remove-Item $work -Recurse -Force
    return $dest
}

Register-Api 'themes.import' {
    param($a)
    $src = [string]$a.path
    if (@('.tctheme', '.zip') -notcontains [IO.Path]::GetExtension($src).ToLower()) { throw 'Not a TRANSCRYPTO theme file (.tctheme)' }
    $work = Join-Path $TC.Cache ('import-' + [Guid]::NewGuid().ToString('N'))
    $zip = "$work.zip"; Copy-Item -LiteralPath $src $zip
    Expand-TCZip $zip $work; Remove-Item $zip
    $t = Read-Json "$work\theme.json"; if (-not $t) { throw 'Not a TRANSCRYPTO theme file' }
    $id = 'my-' + (($t.name.ToLower() -replace '[^a-z0-9]+', '-').Trim('-'))
    $filesDir = Join-Path $TC.Data ('themes-files\' + $id)
    New-Item -ItemType Directory $filesDir -Force | Out-Null
    if (Test-Path "$work\files") { Copy-Item "$work\files\*" $filesDir -Force }
    $json = [IO.File]::ReadAllText("$work\theme.json").Replace('{{FILES}}', $filesDir.Replace('\', '\\'))
    $t = ConvertTo-HT ($json | ConvertFrom-Json); $t.id = $id
    Write-Json $t (Join-Path $TC.MyThemes ($id + '.json'))
    Remove-Item $work -Recurse -Force
    return $t
}
