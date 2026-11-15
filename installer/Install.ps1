# TRANSCRYPTO Themer installer: copies the app, builds the engine with the Windows C# compiler,
# creates the shortcuts that were picked and an Apps & Features uninstall entry.
# -Quiet: run by TRANSCRYPTO-Themer-Setup.exe (no window prompts; exit code 1 on failure)
# -Dest <folder>     install folder (default: Program Files\TRANSCRYPTO Themer; an update keeps the current folder)
# -DesktopShortcut / -NoDesktopShortcut   add / remove the desktop icon (neither: an update keeps what is there)
# -NoStartMenu       no Start menu shortcut
# -NoLaunch          don't start the app when done
param([switch]$Quiet, [string]$Dest, [switch]$DesktopShortcut, [switch]$NoDesktopShortcut, [switch]$NoStartMenu, [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
try { $Host.UI.RawUI.WindowTitle = 'TRANSCRYPTO Themer setup' } catch {}
$src = Split-Path -Parent $PSScriptRoot
$defaultDest = Join-Path $env:ProgramFiles 'TRANSCRYPTO Themer'
$uninstKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TRANSCRYPTO Themer'
$current = try { [string](Get-ItemProperty $uninstKey -ErrorAction Stop).InstallLocation } catch { '' }
if ($current -and -not (Test-Path -LiteralPath (Join-Path $current 'version.json'))) { $current = '' }   # folder deleted by hand: a fresh install
$dest = if ($Dest) { [IO.Path]::GetFullPath($Dest.Trim().TrimEnd('\')) } elseif ($current) { $current } else { $defaultDest }

function Say($t, $c = 'Cyan') { Write-Host $t -ForegroundColor $c }
Say ''
Say '  T R A N S C R Y P T O   T H E M E R' 'Magenta'
Say '  ------------------------------------'
Say "  Installing to $dest"

try {
    # ask a running engine to quit on its own first: it gives the Windows taskbar back when a custom shell is on
    try { $e = [System.Threading.EventWaitHandle]::OpenExisting('Local\TRANSCRYPTO_TCWall_Exit'); [void]$e.Set(); Start-Sleep -Milliseconds 1500 } catch {}
    Get-Process TCWall -ErrorAction SilentlyContinue | Stop-Process -Force
    # the native Themer window of an earlier install: closed normally (its file is about to be rebuilt)
    foreach ($w in @(Get-CimInstance Win32_Process -Filter "Name='TRANSCRYPTO.exe'" -ErrorAction SilentlyContinue | Where-Object { $_.ExecutablePath -eq (Join-Path $dest 'bin\TRANSCRYPTO.exe') })) {
        try { $gp = Get-Process -Id $w.ProcessId -ErrorAction Stop; [void]$gp.CloseMainWindow(); if (-not $gp.WaitForExit(5000)) { $gp.Kill() } } catch {}
    }
    # close open Themer windows (their own browser profile): they talk to the old backend and would be left behind as dead windows
    $ui = Join-Path $env:LOCALAPPDATA 'TRANSCRYPTO\ui-profile'
    $uiProcs = @(Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe' OR Name='brave.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine.IndexOf($ui, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and $_.CommandLine -notmatch '--type=' })
    if ($uiProcs) {
        # every window of that browser gets a normal close (a killed browser can leave its profile half written)
        Add-Type -Namespace TCSetup -Name Win -MemberDefinition @'
public delegate bool P(System.IntPtr h, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool EnumWindows(P p, System.IntPtr l);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint pid);
[System.Runtime.InteropServices.DllImport("user32.dll")] public static extern bool PostMessage(System.IntPtr h, uint m, System.IntPtr w, System.IntPtr l);
public static void CloseAll(uint pid) { EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) PostMessage(h, 0x10, System.IntPtr.Zero, System.IntPtr.Zero); return true; }, System.IntPtr.Zero); }
'@
        foreach ($p in $uiProcs) { [TCSetup.Win]::CloseAll([uint32]$p.ProcessId) }
        foreach ($p in $uiProcs) { try { $gp = Get-Process -Id $p.ProcessId -ErrorAction Stop; if (-not $gp.WaitForExit(5000)) { $gp.Kill() } } catch {} }
        Say '  Closed the open Themer window' 'Green'
    }
    # stop a running backend, otherwise the launcher reuses it and the old code stays loaded
    $sess = Join-Path $env:LOCALAPPDATA 'TRANSCRYPTO\session.json'
    if (Test-Path $sess) {
        try {
            $bpid = [int](Get-Content $sess -Raw | ConvertFrom-Json).pid
            $proc = Get-CimInstance Win32_Process -Filter "ProcessId = $bpid" -ErrorAction SilentlyContinue
            if ($proc -and $proc.CommandLine -like '*server.ps1*') { Stop-Process -Id $bpid -Force; Say '  Stopped the running backend' 'Green' }
        } catch {}
        Remove-Item $sess -Force -ErrorAction SilentlyContinue
    }
    # ---- where: an update stays where the app is; a new folder must be a safe place for code that runs as administrator
    if ($current -and (Test-Path -LiteralPath (Join-Path $current 'version.json')) -and $current.TrimEnd('\') -ne $dest) {
        throw "TRANSCRYPTO Themer is already installed in $current. Uninstall it first to install it somewhere else."
    }
    $drive = [IO.Path]::GetPathRoot($dest)
    if (-not $drive -or $drive.StartsWith('\\') -or $dest.TrimEnd('\') -eq $drive.TrimEnd('\')) { throw "Pick a folder on a local drive, not a drive root or a network path: $dest" }
    $di = New-Object IO.DriveInfo($drive)
    if ($di.DriveType -ne 'Fixed' -or $di.DriveFormat -ne 'NTFS') { throw "The install folder must be on a fixed NTFS drive ($drive is $($di.DriveType) / $($di.DriveFormat))" }
    if ((Test-Path -LiteralPath $dest) -and -not (Test-Path -LiteralPath (Join-Path $dest 'version.json')) -and (Get-ChildItem -LiteralPath $dest -Force | Select-Object -First 1)) {
        throw "The folder is not empty: $dest. Pick an empty or new folder."
    }
    New-Item -ItemType Directory $dest -Force | Out-Null
    if ((Get-Item -LiteralPath $dest -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "The install folder can't be a link or junction: $dest" }
    # The backend runs this code as administrator, so only administrators may change it (what Program Files gives for
    # free). Set that explicitly - also outside Program Files - before any file is copied.
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $admins = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
    foreach ($r in @(@('S-1-5-32-544', 'FullControl'), @('S-1-5-18', 'FullControl'), @('S-1-5-32-545', 'ReadAndExecute'))) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($r[0])), $r[1], 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    $acl.SetOwner($admins)
    Set-Acl -LiteralPath $dest -AclObject $acl
    foreach ($item in @('core', 'modules', 'ui', 'engine', 'shell', 'presets', 'assets', 'vendor', 'installer', 'TRANSCRYPTO.vbs', 'version.json', 'README.md', 'LICENSE.txt', 'Setup.cmd')) {
        $p = Join-Path $src $item
        if (Test-Path $p) { Copy-Item $p -Destination $dest -Recurse -Force }
    }
    Get-ChildItem $dest -Recurse -File | Unblock-File -ErrorAction SilentlyContinue
    Say '  Files copied' 'Green'

    # Microsoft WebView2 (part of Windows 11) draws the Themer window and the custom taskbar / live scenes: its two .NET
    # libraries and the loader for this CPU go next to the programs (a file still in use keeps its identical copy)
    $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
    New-Item -ItemType Directory (Join-Path $dest 'bin') -Force | Out-Null
    $wv = Join-Path $dest 'vendor\WebView2'
    $arch = switch ($env:PROCESSOR_ARCHITECTURE) { 'ARM64' { 'arm64' } 'x86' { 'x86' } default { 'x64' } }
    foreach ($f in @((Join-Path $wv 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $wv 'Microsoft.Web.WebView2.WinForms.dll'), (Join-Path $wv "$arch\WebView2Loader.dll"))) {
        $to = Join-Path $dest ('bin\' + (Split-Path -Leaf $f))
        try { Copy-Item $f $to -Force -ErrorAction Stop }
        catch { if (-not (Test-Path $to) -or (Get-FileHash $to).Hash -ne (Get-FileHash $f).Hash) { throw "Could not update $(Split-Path -Leaf $f): $_" } }
    }

    # build the live wallpaper + shell engine
    $exe = Join-Path $dest 'bin\TCWall.exe'
    # the old engine must be gone, or its locked TCWall.exe makes the build fail and the OLD engine keeps running
    for ($i = 0; $i -lt 3; $i++) {
        Get-Process TCWall -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        for ($w = 0; $w -lt 25 -and (Get-Process TCWall -ErrorAction SilentlyContinue); $w++) { Start-Sleep -Milliseconds 200 }
        if (Test-Path $exe) { try { Move-Item $exe "$exe.old" -Force -ErrorAction Stop; break } catch { Start-Sleep -Milliseconds 500 } } else { break }
    }
    Remove-Item "$exe.old" -Force -ErrorAction SilentlyContinue
    $out = & $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$exe" "/win32icon:$dest\assets\app.ico" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/lib:$(Join-Path (Split-Path -Parent $csc) 'WPF')" `
        /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll "/r:$wv\Microsoft.Web.WebView2.Core.dll" "/r:$wv\Microsoft.Web.WebView2.WinForms.dll" `
        "$dest\engine\*.cs" 2>&1
    if ($LASTEXITCODE -eq 0) {
        Set-Content -Path "$dest\bin\TCWall.hash" -Value ((Get-ChildItem "$dest\engine" -Filter *.cs | Sort-Object Name | ForEach-Object { (Get-FileHash $_.FullName).Hash }) -join '')
        Say '  Engine built (TCWall.exe)' 'Green'
    } else {
        Say '  Engine build failed - the app still works, live wallpaper will retry from Settings:' 'Yellow'
        $out | ForEach-Object { Say "    $_" 'Yellow' }
    }

    # the native Themer window (bin\TRANSCRYPTO.exe): Microsoft WebView2 instead of a Chrome/Edge app window
    $hostExe = Join-Path $dest 'bin\TRANSCRYPTO.exe'
    if (Test-Path $hostExe) { try { Move-Item $hostExe "$hostExe.old" -Force -ErrorAction Stop } catch {} }
    Remove-Item "$hostExe.old" -Force -ErrorAction SilentlyContinue
    $out = & $csc /nologo /target:winexe /optimize+ /platform:anycpu "/out:$hostExe" "/win32icon:$dest\assets\app.ico" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll "/r:$wv\Microsoft.Web.WebView2.Core.dll" "/r:$wv\Microsoft.Web.WebView2.WinForms.dll" `
        "$dest\core\ThemerWindow.cs" 2>&1
    if ($LASTEXITCODE -eq 0) { Say '  Themer window built (TRANSCRYPTO.exe)' 'Green' }
    else { Say '  Themer window build failed - the Themer opens in a browser window instead:' 'Yellow'; $out | ForEach-Object { Say "    $_" 'Yellow' } }

    # shortcuts: the ones picked in setup (an earlier version's icon is removed when it wasn't picked this time)
    $sh = New-Object -ComObject WScript.Shell
    $lnkStart = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'TRANSCRYPTO Themer.lnk'
    $lnkDesk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'TRANSCRYPTO Themer.lnk'
    $made = @()
    $wantDesk = if ($DesktopShortcut) { $true } elseif ($NoDesktopShortcut) { $false } else { Test-Path -LiteralPath $lnkDesk }   # not asked: keep it as it is
    foreach ($l in @(@($lnkStart, -not $NoStartMenu), @($lnkDesk, $wantDesk))) {
        if (-not $l[1]) { Remove-Item $l[0] -Force -ErrorAction SilentlyContinue; continue }
        $s = $sh.CreateShortcut($l[0])
        $s.TargetPath = "$env:WINDIR\System32\wscript.exe"
        $s.Arguments = '"' + (Join-Path $dest 'TRANSCRYPTO.vbs') + '"'
        $s.WorkingDirectory = $dest
        $s.IconLocation = (Join-Path $dest 'assets\app.ico') + ',0'
        $s.Description = 'TRANSCRYPTO Themer - all-in-one Windows 11 themer'
        $s.Save(); $made += $l[0]
    }
    Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'TRANSCRYPTO Themer.lnk') -Force -ErrorAction SilentlyContinue
    Say ('  Shortcuts: ' + $(if ($made) { (@($(if (-not $NoStartMenu) { 'Start menu' }), $(if ($wantDesk) { 'Desktop' })) | Where-Object { $_ }) -join ' + ' } else { 'none' })) 'Green'

    # uninstall entry
    $u = $uninstKey
    New-Item $u -Force | Out-Null
    $ver = (Get-Content (Join-Path $dest 'version.json') -Raw | ConvertFrom-Json).version
    Set-ItemProperty $u DisplayName 'TRANSCRYPTO Themer'
    Set-ItemProperty $u DisplayVersion $ver
    Set-ItemProperty $u Publisher 'TRANSCRYPTO'
    Set-ItemProperty $u DisplayIcon (Join-Path $dest 'assets\app.ico')
    Set-ItemProperty $u InstallLocation $dest
    Set-ItemProperty $u UninstallString ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $dest 'installer\Uninstall.ps1') + '"')
    Set-ItemProperty $u NoModify 1 -Type DWord
    Set-ItemProperty $u NoRepair 1 -Type DWord
    Set-ItemProperty $u EstimatedSize ([int]((Get-ChildItem $dest -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)) -Type DWord
    Set-ItemProperty $u InstallDate (Get-Date -Format 'yyyyMMdd')
    Say '  Registered in Apps & Features' 'Green'
    Say ''
    if ($NoLaunch) { Say '  Done!' 'Magenta' }
    else {
        Say '  Done! Launching TRANSCRYPTO Themer...' 'Magenta'
        # through explorer: the app starts as the normal user, not as this elevated setup
        Start-Process "$env:WINDIR\explorer.exe" -WorkingDirectory $dest -ArgumentList ('"' + (Join-Path $dest 'TRANSCRYPTO.vbs') + '"')
    }
    if (-not $Quiet) { Start-Sleep -Seconds 3 }
} catch {
    Say "  Setup failed: $_" 'Red'
    if ($Quiet) { exit 1 }
    Read-Host '  Press Enter to close'
}
