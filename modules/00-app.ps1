# App plumbing: window, dialogs, settings, engine control, updates

# The browser that shows the app window: Edge, else Chrome, else Brave - installed under Program Files only
# (the backend only answers browsers from there, see Test-TCCaller)
function Find-TCBrowser {
    foreach ($exe in @('msedge.exe', 'chrome.exe', 'brave.exe')) {
        $p = [string](Get-RegValue "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\$exe" '(default)')
        $p = $p.Trim('"'); if ($p -and (Test-Path $p)) { return $p }
    }
    foreach ($c in @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
                     "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
                     "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe")) { if (Test-Path $c) { return $c } }
    return $null
}

function Get-EdgeArgs([int]$Port, [string]$Token) {
    $prof = Join-Path $TC.Data 'ui-profile'
    return ('--app="http://127.0.0.1:{0}/?t={1}" --user-data-dir="{2}" --window-size=1380,880 --no-first-run --no-default-browser-check ' +
            '--disable-extensions --disable-sync --disable-background-networking --disable-component-update --disable-features=Translate,msEdgeSidebarV2 ' +
            '--edge-skip-compat-layer-relaunch') -f $Port, $Token, $prof
}

# Opens the UI window as the normal user (a shortcut launched through explorer is never elevated)
function Open-TCWindow([int]$Port, [string]$Token) {
    # the native Themer window when it is built (it reads port + token from session.json itself)
    $native = Join-Path $TC.Root 'bin\TRANSCRYPTO.exe'
    if (Test-Path -LiteralPath $native) { Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $native + '"'); return }
    $edge = Find-TCBrowser
    $lnk = Join-Path $TC.Cache 'open-ui.lnk'
    $sh = New-Object -ComObject WScript.Shell
    $s = $sh.CreateShortcut($lnk)
    if ($edge) { $s.TargetPath = $edge; $s.Arguments = Get-EdgeArgs $Port $Token }
    else { $s.TargetPath = "http://127.0.0.1:$Port/?t=$Token" }
    $s.Save()
    Start-Process -FilePath "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $lnk + '"')
}

# ---------------------------------------------------------------- dialogs
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
function New-TopOwner {
    $f = New-Object System.Windows.Forms.Form
    $f.TopMost = $true; $f.ShowInTaskbar = $false; $f.Opacity = 0; $f.StartPosition = 'CenterScreen'; $f.Size = '1,1'
    $f.Show(); $f.Activate(); return $f
}

Register-Api 'app.pickFile' {
    param($a)
    $o = New-TopOwner
    try {
        $d = New-Object System.Windows.Forms.OpenFileDialog
        $d.Title = if ($a.title) { $a.title } else { 'Choose a file' }
        $d.Filter = if ($a.filter) { $a.filter } else { 'All files|*.*' }
        $d.Multiselect = [bool]$a.multi
        if ($d.ShowDialog($o) -eq 'OK') { if ($a.multi) { return ,$d.FileNames } else { return $d.FileName } }
        return $null
    } finally { $o.Close() }
}

Register-Api 'app.pickFolder' {
    param($a)
    $o = New-TopOwner
    try {
        $d = New-Object System.Windows.Forms.FolderBrowserDialog
        $d.Description = if ($a.title) { $a.title } else { 'Choose a folder' }
        if ($d.ShowDialog($o) -eq 'OK') { return $d.SelectedPath }
        return $null
    } finally { $o.Close() }
}

Register-Api 'app.pickSave' {
    param($a)
    $o = New-TopOwner
    try {
        $d = New-Object System.Windows.Forms.SaveFileDialog
        $d.Filter = if ($a.filter) { $a.filter } else { 'All files|*.*' }
        $d.FileName = [string]$a.name
        if ($d.ShowDialog($o) -eq 'OK') { return $d.FileName }
        return $null
    } finally { $o.Close() }
}

Register-Api 'app.openPath' {
    param($a)
    $p = [string]$a.path
    if ($p.Contains('"')) { throw 'Bad path' }
    # a file is only ever shown in its folder (never opened: it could be a program), a folder is opened
    if (Test-Path -LiteralPath $p -PathType Leaf) { Start-Process "$env:WINDIR\explorer.exe" -ArgumentList ('/select,"' + $p + '"') }
    elseif (Test-Path -LiteralPath $p -PathType Container) { Start-Process "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $p + '"') }
    else { throw 'Not found' }
    return $true
}
Register-Api 'app.openUrl'  {
    param($a)
    $u = $null   # web links only (no file:, shell: or program paths)
    if (-not [Uri]::TryCreate([string]$a.url, 'Absolute', [ref]$u) -or $u.Scheme -notin @('http', 'https')) { throw 'Only web links can be opened' }
    Start-Process "$env:WINDIR\explorer.exe" -ArgumentList ('"' + $u.AbsoluteUri + '"'); return $true
}
Register-Api 'app.restartExplorer' { param($a) Restart-Explorer; return $true }
Register-Api 'app.signOut'  { param($a) Start-Process shutdown.exe -ArgumentList '/l'; return $true }
Register-Api 'app.reboot'   { param($a) Start-Process shutdown.exe -ArgumentList '/r /t 5'; return $true }
Register-Api 'app.log'      { param($a) $f = Join-Path $TC.Logs 'transcrypto.log'; if (Test-Path $f) { return ((Get-Content $f -Tail 200) -join "`n") }; return '' }

# Restart the backend in place (after editing a module or after an update). Same port + token, so the page just reloads.
Register-Api 'app.reloadBackend' {
    param($a)
    $ps = "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe"
    $argsLine = '-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "{0}" -Port {1} -Token {2} -WaitPid {3} -NoBrowser' -f (Join-Path $TC.Root 'core\server.ps1'), $TCServer.Port, $TCServer.Token, $PID
    Start-Process $ps -ArgumentList $argsLine -WindowStyle Hidden
    $global:TCServer.Running = $false
    return $true
}

# ---------------------------------------------------------------- settings
Register-Api 'settings.get' { param($a) return (Get-TCSettings) }
Register-Api 'settings.set' {
    param($a)
    $s = Merge-HT (Get-TCSettings) (ConvertTo-HT $a.values)
    Save-TCSettings $s
    return $s
}

# ---------------------------------------------------------------- engine (TCWall.exe)
$TC.EngineExe = Join-Path $TC.Bin 'TCWall.exe'
$TC.EngineSrc = Join-Path $TC.Root 'engine\TCWall.cs'
# the engine is built from every engine\*.cs (TCWall.cs + the shell host files)
function Get-EngineSources { return @(Get-ChildItem (Join-Path $TC.Root 'engine') -Filter *.cs | Sort-Object Name | ForEach-Object { $_.FullName }) }
function Get-EngineSourceHash { return ((Get-EngineSources | ForEach-Object { (Get-FileHash $_).Hash }) -join '') }

function Get-Csc {
    foreach ($c in @("$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe", "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe")) { if (Test-Path $c) { return $c } }
    return $null
}

function Build-Engine {
    $csc = Get-Csc
    if (-not $csc) { throw '.NET Framework 4 compiler (csc.exe) not found' }
    if (-not (Test-Path $TC.Bin)) { New-Item -ItemType Directory $TC.Bin -Force | Out-Null }
    $running = Get-Process TCWall -ErrorAction SilentlyContinue
    if ($running) { Stop-Engine; Start-Sleep -Milliseconds 800 }
    # a TCWall.exe still locked by a dying engine would fail the build: force it out and move the old file aside
    Get-Process TCWall -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    if (Test-Path $TC.EngineExe) { try { Move-Item $TC.EngineExe "$($TC.EngineExe).old" -Force -ErrorAction Stop } catch { Start-Sleep -Milliseconds 800 } }
    Remove-Item "$($TC.EngineExe).old" -Force -ErrorAction SilentlyContinue
    $icon = Join-Path $TC.Root 'assets\app.ico'
    # the shell pages run in WebView2: its two .NET libraries and the loader for this CPU live next to the engine
    $wv = Join-Path $TC.Root 'vendor\WebView2'
    $arch = switch ($env:PROCESSOR_ARCHITECTURE) { 'ARM64' { 'arm64' } 'x86' { 'x86' } default { 'x64' } }
    foreach ($f in @((Join-Path $wv 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'), (Join-Path $wv "$arch\WebView2Loader.dll"))) {
        try { Copy-Item $f $TC.Bin -Force -ErrorAction Stop } catch { Write-TCLog "engine build: could not copy $(Split-Path -Leaf $f): $_" }
    }
    $argList = @('/nologo', '/target:winexe', '/optimize+', '/platform:anycpu', ('/out:' + $TC.EngineExe),
                 '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
                 ('/lib:' + (Join-Path (Split-Path -Parent $csc) 'WPF')), '/r:UIAutomationClient.dll', '/r:UIAutomationTypes.dll', '/r:WindowsBase.dll',
                 ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.Core.dll')), ('/r:' + (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll')))
    if (Test-Path $icon) { $argList += ('/win32icon:' + $icon) }
    $argList += Get-EngineSources
    $out = & $csc @argList 2>&1 | Out-String
    $ok = ($LASTEXITCODE -eq 0) -and (Test-Path $TC.EngineExe)
    if ($ok) { Set-Content -Path (Join-Path $TC.Bin 'TCWall.hash') -Value (Get-EngineSourceHash) }
    if ($running -and $ok) { Sync-Engine | Out-Null }
    return @{ ok = $ok; output = $out.Trim() }
}

function Test-EngineRunning { return [bool](Get-Process TCWall -ErrorAction SilentlyContinue) }

function Send-EngineSignal([string]$Name) {
    try { $e = [System.Threading.EventWaitHandle]::OpenExisting("Local\TRANSCRYPTO_TCWall_$Name"); [void]$e.Set(); $e.Close(); return $true } catch { return $false }
}

function Stop-Engine {
    if (-not (Send-EngineSignal 'Exit')) { Get-Process TCWall -ErrorAction SilentlyContinue | Stop-Process -Force }
    for ($i = 0; $i -lt 10; $i++) { if (-not (Test-EngineRunning)) { break }; Start-Sleep -Milliseconds 200 }
}

function Set-EngineAutostart([bool]$On) {
    $run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if ($On) { Set-RegValue $run 'TRANSCRYPTO Engine' ('"' + $TC.EngineExe + '" --logon') 'String' } else { Remove-RegValue $run 'TRANSCRYPTO Engine' }
}

# Start / stop the engine depending on what is enabled in engine.json
function Sync-Engine {
    $cfg = Get-EngineConfig
    # effects set to "preset" colors need the active preset's accent pair; fill it in if the preset was applied before
    # this existed (otherwise the engine falls back to the Windows accent)
    if (-not $cfg.presetColors.accent -or -not $cfg.presetColors.id) {
        try {
            $p = Get-PresetById ([string](Get-TCState).preset)
            if ($p -and $p.palette) { $cfg.presetColors = [ordered]@{ id = [string]$p.id; accent = [string]$p.palette.accent; accent2 = [string]$p.palette.accent2 }; Save-EngineConfig $cfg }
        } catch { Write-TCLog "preset colors: $_" }
    }
    # Live Lock ends with LockWorkStation(); only with NoLockScreen=1 does Windows open the password / PIN box directly
    # instead of its picture curtain. Feature updates and other tools reset policies, so re-assert it (the backend is elevated).
    if ($cfg.lock.enabled) {
        $pol = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
        if ((Get-RegValue $pol 'NoLockScreen') -ne 1) {
            try { Set-RegValue $pol 'NoLockScreen' 1; Write-TCLog 'Live Lock: NoLockScreen policy was missing, set it again' } catch { Write-TCLog "Live Lock: could not set NoLockScreen: $_" }
        }
    }
    $shellOn = [string]$cfg.shell.design -and [string]$cfg.shell.design -ne 'none'; $bgOn = [string]$cfg.liveBg.design -and [string]$cfg.liveBg.design -ne 'none'
    $need = $cfg.wallpaper.enabled -or $cfg.slideshow.enabled -or $cfg.lock.enabled -or $cfg.startupSound.enabled -or $cfg.clickFx.enabled -or $cfg.taskbarFx.enabled -or $cfg.explorerFx.enabled -or $cfg.driveBars.enabled -or $shellOn -or $bgOn
    # Settings > Start with Windows: the engine brings every live effect back at sign-in
    Set-EngineAutostart ($need -and [bool](Get-TCSettings).startWithWindows)
    if ($need) {
        if (-not (Test-Path $TC.EngineExe)) { $b = Build-Engine; if (-not $b.ok) { throw "Engine build failed:`n$($b.output)" } }
        else {
            # the engine was built from older sources (an update whose build failed): rebuild it, or the old engine keeps running
            $hf = Join-Path $TC.Bin 'TCWall.hash'
            $built = if (Test-Path $hf) { ([string](Get-Content $hf -Raw)).Trim() } else { '' }
            if ($built -ne (Get-EngineSourceHash) -and (Test-IsAdmin)) {
                Write-TCLog 'engine is older than its sources: rebuilding'
                $b = Build-Engine; if (-not $b.ok) { Write-TCLog "engine rebuild failed: $($b.output)" }
            }
        }
        if (-not (Test-EngineRunning)) {
            $anyLive = $cfg.wallpaper.enabled -or $cfg.slideshow.enabled -or $cfg.lock.enabled -or $cfg.clickFx.enabled -or $cfg.taskbarFx.enabled -or $cfg.explorerFx.enabled -or $cfg.driveBars.enabled -or $shellOn -or $bgOn
            if ($anyLive) { Start-Unelevated $TC.EngineExe }
        } else { [void](Send-EngineSignal 'Reload') }
    } elseif (Test-EngineRunning) { Stop-Engine }
    # safety net: whenever the engine isn't running, the Windows taskbar must be visible (the custom shell hides it)
    if (-not (Test-EngineRunning)) { Show-WindowsTaskbar }
    return (Test-EngineRunning)
}

Register-Api 'engine.status' {
    param($a)
    $p = Get-Process TCWall -ErrorAction SilentlyContinue | Select-Object -First 1
    return @{ running = [bool]$p; ramMB = if ($p) { [math]::Round($p.WorkingSet64 / 1MB, 1) } else { 0 }
              cpu = if ($p) { [math]::Round($p.CPU, 1) } else { 0 }; built = (Test-Path $TC.EngineExe); config = (Get-EngineConfig) }
}
Register-Api 'engine.build'  { param($a) return (Build-Engine) }
Register-Api 'engine.start'  { param($a) return (Sync-Engine) }
Register-Api 'engine.stop'   { param($a) Stop-Engine; return $true }
Register-Api 'engine.lockNow' {
    param($a)
    if (-not (Send-EngineSignal 'LockNow')) { [TCNative]::LockWorkStation() | Out-Null }
    return $true
}
Register-Api 'engine.setConfig' {
    param($a)
    $cfg = Merge-HT (Get-EngineConfig) (ConvertTo-HT $a.values)
    Save-EngineConfig $cfg
    return @{ running = (Sync-Engine); config = $cfg }
}

# ---------------------------------------------------------------- updates (GitHub or a single update file)
# One format for both: a .zip (or .tcupdate) holding the app folders plus version.json
# { app, version, date, notes, changes: [ { type: fixed|new|improved, text } ] }. GitHub's repo zip is the same thing.
$TC.UpdateItems = @('core', 'modules', 'ui', 'engine', 'shell', 'presets', 'assets', 'vendor', 'installer', 'version.json', 'TRANSCRYPTO.vbs', 'README.md', 'LICENSE.txt', 'Setup.cmd')
$TC.UpdateHistory = Join-Path $TC.Data 'updates.json'

function ConvertTo-TCVersion([string]$v) { try { return [version]$v } catch { return [version]'0.0.0' } }

function Get-ChangeList($manifest) {
    return ,@(@($manifest.changes) | Where-Object { $_ } | ForEach-Object { @{ type = [string]$_.type; text = [string]$_.text } })
}

# Extract a package and return the folder that holds version.json + core\server.ps1 (repo zips add a top folder)
function Open-TCPackage([string]$Zip) {
    $dir = New-TCSecureTemp 'update'   # verified and installed from a folder no normal program can change
    try {
        Expand-TCZip $Zip $dir
        $roots = @(Get-Item $dir) + @(Get-ChildItem $dir -Directory)
        foreach ($r in $roots) {
            if ((Test-Path (Join-Path $r.FullName 'version.json')) -and (Test-Path (Join-Path $r.FullName 'core\server.ps1'))) {
                $m = Get-Content (Join-Path $r.FullName 'version.json') -Raw | ConvertFrom-Json
                if ($m.app -and $m.app -ne 'TRANSCRYPTO Themer') { throw "This file is an update for '$($m.app)', not TRANSCRYPTO Themer." }
                # an update runs as administrator on the next start: only packages signed with the TRANSCRYPTO key get this far
                Assert-TCPackageSignature $r.FullName | Out-Null
                return @{ root = $r.FullName; manifest = $m; tmp = $dir }
            }
        }
        throw 'Not a TRANSCRYPTO Themer update: version.json and core\server.ps1 were not found inside.'
    } catch { Close-TCPackage @{ tmp = $dir }; throw }
}

# remove only the temp folder made for a package (never a parent of it)
function Close-TCPackage($pkg) {
    try {
        $t = [string]$pkg.tmp
        if ($t -and ([IO.Path]::GetFileName($t) -like '*update-*') -and ((Test-TCChildPath $t "$env:WINDIR\Temp") -or (Test-TCChildPath $t $TC.Cache))) { Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction SilentlyContinue }
    } catch {}
}

# Which files the package would add or change, by content hash
function Compare-TCPackage([string]$Root) {
    $added = @(); $changed = @()
    foreach ($item in $TC.UpdateItems) {
        $from = Join-Path $Root $item
        if (-not (Test-Path $from)) { continue }
        foreach ($f in @(Get-ChildItem $from -Recurse -File -ErrorAction SilentlyContinue) + @(Get-Item $from | Where-Object { -not $_.PSIsContainer })) {
            if (-not $f) { continue }
            $rel = $f.FullName.Substring($Root.Length).TrimStart('\')
            $dst = Join-Path $TC.Root $rel
            if (-not (Test-Path $dst)) { $added += $rel }
            elseif ((Get-FileHash $f.FullName).Hash -ne (Get-FileHash $dst).Hash) { $changed += $rel }
        }
    }
    return @{ added = $added; changed = $changed }
}

function Get-TCPackageInfo($pkg) {
    $m = $pkg.manifest; $diff = Compare-TCPackage $pkg.root
    return @{ current = $TC.Version; version = [string]$m.version; date = [string]$m.date; notes = [string]$m.notes; changes = (Get-ChangeList $m)
              newer = ((ConvertTo-TCVersion $m.version) -gt (ConvertTo-TCVersion $TC.Version)); added = $diff.added; changed = $diff.changed }
}

function Install-TCPackage($pkg, [string]$Source) {
    $info = Get-TCPackageInfo $pkg
    if (($info.added.Count + $info.changed.Count) -eq 0) { Close-TCPackage $pkg; $info.ok = $true; $info.nothing = $true; return $info }
    $engineChanged = [bool](@($info.added + $info.changed) | Where-Object { $_ -like 'engine\*.cs' })
    foreach ($item in $TC.UpdateItems) {
        $from = Join-Path $pkg.root $item
        if (Test-Path $from) { Copy-Item $from -Destination $TC.Root -Recurse -Force }
    }
    Get-ChildItem $TC.Root -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    $info.engineRebuilt = $false
    if ($engineChanged) { $b = Build-Engine; $info.engineRebuilt = [bool]$b.ok; if (-not $b.ok) { Write-TCLog "update: engine build failed`n$($b.output)" } }

    $hist = @(Read-Json $TC.UpdateHistory @())
    $entry = [ordered]@{ from = $TC.Version; version = $info.version; date = $info.date; installed = (Get-Date).ToString('o'); source = $Source
                         notes = $info.notes; changes = $info.changes; filesChanged = $info.changed.Count; filesAdded = $info.added.Count }
    Write-Json (@($entry) + @($hist | Where-Object { $_ } | Select-Object -First 49)) $TC.UpdateHistory
    Write-TCLog "updated $($TC.Version) -> $($info.version) from $Source ($($info.changed.Count) changed, $($info.added.Count) new files)"
    Close-TCPackage $pkg
    $info.ok = $true
    return $info
}

Register-Api 'update.check' {
    param($a)
    $s = Get-TCSettings
    if (-not $s.updateRepo -or $s.updateRepo -like 'YOUR-GITHUB-USER*') { return @{ configured = $false; current = $TC.Version } }
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = "https://raw.githubusercontent.com/$($s.updateRepo)/$($s.updateBranch)/version.json"
    $remote = Invoke-RestMethod -Uri $url -UseBasicParsing -TimeoutSec 10
    $newer = (ConvertTo-TCVersion $remote.version) -gt (ConvertTo-TCVersion $TC.Version)
    return @{ configured = $true; current = $TC.Version; latest = $remote.version; date = $remote.date; notes = $remote.notes; changes = (Get-ChangeList $remote); available = $newer }
}

Register-Api 'update.apply' {
    param($a)
    $s = Get-TCSettings
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $zip = Join-Path $TC.Cache 'update.zip'
    Invoke-WebRequest -Uri "https://github.com/$($s.updateRepo)/archive/refs/heads/$($s.updateBranch).zip" -OutFile $zip -UseBasicParsing
    return (Install-TCPackage (Open-TCPackage $zip) "GitHub $($s.updateRepo)")
}

# Manual update from a file: inspect first (what is in it), then install
Register-Api 'update.inspectFile' {
    param($a)
    $p = [string]$a.path
    if (-not (Test-Path -LiteralPath $p)) { throw "File not found: $p" }
    $pkg = Open-TCPackage $p
    try { return (Get-TCPackageInfo $pkg) } finally { Close-TCPackage $pkg }
}
Register-Api 'update.installFile' {
    param($a)
    $p = [string]$a.path
    if (-not (Test-Path -LiteralPath $p)) { throw "File not found: $p" }
    return (Install-TCPackage (Open-TCPackage $p) ('file ' + [IO.Path]::GetFileName($p)))
}
# Help menu: this version's release notes, plus every registry value each section can change (from the live tweak and
# tracking registries, so the knowledge base always matches the code)
Register-Api 'app.knowledge' {
    param($a)
    $v = Read-Json (Join-Path $TC.Root 'version.json') @{}
    $tracked = @{}
    foreach ($k in $TCTracked.Keys) {
        $tracked[$k] = @(@($TCTracked[$k]) | ForEach-Object { if ($_.tree) { [string]$_.p + '\*' } else { [string]$_.p + ' : ' + [string]$_.n } })
    }
    $tweaks = @($TCTweaks | ForEach-Object {
        @{ tab = $_.tab; section = $_.section; label = $_.label; desc = $_.desc; admin = $_.admin; restart = $_.restart
           where = $(if ($_.custom) { '' } else { [string]$_.path + ' : ' + [string]$_.name }) }
    })
    $lic = Join-Path $TC.Root 'LICENSE.txt'
    $license = if (Test-Path -LiteralPath $lic) { [IO.File]::ReadAllText($lic) } else { '' }
    return @{ version = $TC.Version; date = [string]$v.date; notes = [string]$v.notes; changes = (Get-ChangeList $v); tracked = $tracked; tweaks = $tweaks; license = $license }
}

Register-Api 'update.history' { param($a) $r = Read-Json $TC.UpdateHistory @(); return ,@($r) }
