# TRANSCRYPTO Themer - local backend
# Started elevated (hidden) by TRANSCRYPTO.vbs. Serves the UI on 127.0.0.1 and exits
# by itself ~16 s after the app window is closed, so nothing stays in RAM.

param([int]$Port = 0, [string]$Token = '', [int]$WaitPid = 0, [switch]$NoBrowser, [switch]$Test)   # -Test: a side instance for checks (no session file, no engine sync, no window)

if ($WaitPid -gt 0) { try { Wait-Process -Id $WaitPid -Timeout 20 -ErrorAction SilentlyContinue } catch {} }

. (Join-Path $PSScriptRoot 'common.ps1')
foreach ($__m in (Get-ChildItem (Join-Path $TC.Root 'modules') -Filter '*.ps1' | Sort-Object Name)) {
    try { . $__m.FullName } catch { Write-TCLog "module load failed $($__m.Name): $_" }
}

$token = if ($Token) { $Token } else { [Guid]::NewGuid().ToString('N') }
$listener = New-Object System.Net.HttpListener
$ports = if ($Port -gt 0) { @($Port) } else { 47810..47830 }
foreach ($p in $ports) {
    try { $listener = New-Object System.Net.HttpListener; $listener.Prefixes.Add("http://127.0.0.1:$p/"); $listener.Start(); $Port = $p; break } catch { $listener = $null }
}
if (-not $listener) { Write-TCLog 'No free port'; exit 1 }

$session = if ($Test) { Join-Path $TC.Cache 'session-test.json' } else { Join-Path $TC.Data 'session.json' }
Write-Json @{ port = $Port; token = $token; pid = $PID; started = (Get-Date).ToString('o') } $session
Write-TCLog "server up on $Port (admin=$(Test-IsAdmin))"
# bring the engine and its policies (Live Lock's NoLockScreen, autostart) in line with the saved config
if (-not $Test) { try { Sync-Engine | Out-Null } catch { Write-TCLog "startup engine sync failed: $_" } }

$global:TCServer = @{ Running = $true; LastSeen = $null; Started = Get-Date; Token = $token; Port = $Port; WindowSeen = $false; WindowGone = 0; LastWindowCheck = (Get-Date).AddMinutes(-1) }

# ---- who is calling? The backend runs as administrator, and its token sits in a file any program of this user can read.
# So a token alone is not enough: the request must also come from a browser process (the app window). That keeps plain
# scripts and tools (PowerShell, curl, Python, ...) from using this server to act as administrator without a UAC prompt.
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class TCConn {
    [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int af, int cls, uint reserved);
    static int Swap(uint p) { return (int)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF)); }
    // owning process of the client side of a loopback connection (client port -> server port), or 0
    public static int ClientPid(int clientPort, int serverPort) {
        int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) return 0;
            int n = Marshal.ReadInt32(buf);
            for (int i = 0; i < n; i++) {
                IntPtr row = new IntPtr(buf.ToInt64() + 4 + i * 24);   // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, pid
                if (Swap((uint)Marshal.ReadInt32(row, 8)) == clientPort && Swap((uint)Marshal.ReadInt32(row, 16)) == serverPort) return Marshal.ReadInt32(row, 20);
            }
            return 0;
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
"@
# The caller must be:
#  1. a browser installed under Program Files (only administrators can put programs there) whose file carries a valid
#     signature from a known browser maker - renaming curl.exe to chrome.exe gets nowhere;
#  2. either the WebView2 inside TRANSCRYPTO's own window (bin\TRANSCRYPTO.exe, admin-only folder, its own ui-webview2 data),
#     or - without that window - a browser started with TRANSCRYPTO's own profile (ui-profile) and no extensions;
#  3. not under remote control: no DevTools port/pipe, headless or automation mode, no side-loaded extensions.
$TCCallerOk = @{}; $TCSignerOk = @{}
$TCTrustedSigners = 'O=(Microsoft Corporation|Google LLC|Brave Software, Inc\.|Mozilla Corporation|Opera Norway AS|Vivaldi Technologies AS)'
$TCRemoteFlags = '--remote-debugging|--headless|--enable-automation|--load-extension|--disable-extensions-except|--remote-allow-origins|--auto-open-devtools|--js-flags|--proxy-server|--host-resolver-rules'
$TCAppBrowser = [bool](Find-TCBrowser)   # false only when the window opens as a tab of some other browser
$TCUiProfile = Join-Path $TC.Data 'ui-profile'
$TCWebViewData = Join-Path $TC.Data 'ui-webview2'
$TCHostExe = Join-Path $TC.Root 'bin\TRANSCRYPTO.exe'
$TCHasHost = Test-Path -LiteralPath $TCHostExe
function Get-TCProc([int]$Id) { Get-CimInstance Win32_Process -Filter "ProcessId = $Id" -ErrorAction SilentlyContinue }
function Test-TCCaller($ctx) {
    try {
        $callerPid = [TCConn]::ClientPid($ctx.Request.RemoteEndPoint.Port, $ctx.Request.LocalEndPoint.Port)
        if ($callerPid -le 0) { return $false }
        $gp = Get-Process -Id $callerPid -ErrorAction Stop
        $key = "$callerPid|$($gp.StartTime.Ticks)"   # a reused process id is a different key
        $hit = $TCCallerOk[$key]; if ($hit -and ((Get-Date) - $hit.at).TotalSeconds -lt 60) { return $hit.ok }
        $ok = $false; $why = ''
        $p = Get-TCProc $callerPid
        $exe = [string]$p.ExecutablePath
        $roots = @($env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ }
        if (-not $exe -or -not ($roots | Where-Object { Test-TCChildPath $exe $_ })) { $why = "not an installed browser ($exe)" }
        else {
            if (-not $TCSignerOk.ContainsKey($exe)) {
                $sig = Get-AuthenticodeSignature -LiteralPath $exe
                $TCSignerOk[$exe] = ($sig.Status -eq 'Valid' -and $sig.SignerCertificate -and $sig.SignerCertificate.Subject -match $TCTrustedSigners)
            }
            if (-not $TCSignerOk[$exe]) { $why = "unsigned or unknown program ($exe)" }
            else {
                # the browser's main process (its helpers - network service, renderers - share the program file)
                $root = $p
                for ($i = 0; $i -lt 4; $i++) { $par = Get-TCProc ([int]$root.ParentProcessId); if ($par -and $par.ExecutablePath -eq $exe) { $root = $par } else { break } }
                $cl = [string]$root.CommandLine
                if ($Test -and $cl -match 'headless-sec') { $ok = $true }   # -Test side instance: the security self-test's own browser
                elseif ($cl -match $TCRemoteFlags) { $why = 'browser under remote control' }
                elseif ($cl -match '--embedded-browser-webview') {
                    # WebView2: its own data folder, and started by our window (the window is the parent of its browser process)
                    $hostProc = Get-TCProc ([int]$root.ParentProcessId)
                    if ($cl.IndexOf($TCWebViewData, [StringComparison]::OrdinalIgnoreCase) -lt 0) { $why = 'a WebView2 of another app' }
                    elseif (-not $hostProc -or [string]$hostProc.ExecutablePath -ne $TCHostExe) { $why = "WebView2 not hosted by the TRANSCRYPTO window ($([string]$hostProc.ExecutablePath))" }
                    else { $ok = $true }
                }
                elseif ($TCHasHost -and -not $Test) { $why = 'not the TRANSCRYPTO window' }   # the native window exists: browser profiles are not accepted
                elseif ($TCAppBrowser -and ($cl.IndexOf($TCUiProfile, [StringComparison]::OrdinalIgnoreCase) -lt 0 -or $cl -notmatch '--disable-extensions(\s|$)')) { $why = 'not the TRANSCRYPTO window' }
                else { $ok = $true }
            }
        }
        $TCCallerOk[$key] = @{ ok = $ok; at = Get-Date }
        if (-not $ok) { Write-TCLog "refused an API call (pid $callerPid): $why" }
        return $ok
    } catch { return $false }
}

function Test-TCWindow {
    try {
        if (Get-CimInstance Win32_Process -Filter "Name='TRANSCRYPTO.exe'" -ErrorAction Stop | Where-Object { $_.ExecutablePath -eq $TCHostExe } | Select-Object -First 1) { return $true }
        return [bool](Get-CimInstance Win32_Process -Filter "(Name='msedge.exe' OR Name='chrome.exe' OR Name='brave.exe') AND CommandLine LIKE '%TRANSCRYPTO%ui-profile%'" -ErrorAction Stop | Select-Object -First 1)
    } catch { return $true }
}

Register-Api 'app.ping'  { param($a) $global:TCServer.LastSeen = Get-Date; return 'pong' }
Register-Api 'app.quit'  { param($a) $global:TCServer.Running = $false; return $true }
Register-Api 'app.info'  { param($a) return @{ version = $TC.Version; root = $TC.Root; data = $TC.Data; admin = (Test-IsAdmin); port = $TCServer.Port } }

$mime = @{
    '.html' = 'text/html; charset=utf-8'; '.css' = 'text/css; charset=utf-8'; '.js' = 'application/javascript; charset=utf-8'
    '.json' = 'application/json; charset=utf-8'; '.svg' = 'image/svg+xml'; '.png' = 'image/png'; '.jpg' = 'image/jpeg'; '.jpeg' = 'image/jpeg'
    '.gif' = 'image/gif'; '.webp' = 'image/webp'; '.bmp' = 'image/bmp'; '.ico' = 'image/x-icon'; '.cur' = 'image/x-icon'; '.ani' = 'application/octet-stream'
    '.mp4' = 'video/mp4'; '.webm' = 'video/webm'; '.mkv' = 'video/x-matroska'; '.mov' = 'video/quicktime'; '.wmv' = 'video/x-ms-wmv'
    '.wav' = 'audio/wav'; '.mp3' = 'audio/mpeg'; '.woff2' = 'font/woff2'; '.ttf' = 'font/ttf'
}

function Send-Bytes($ctx, [byte[]]$bytes, [string]$type = 'application/json; charset=utf-8', [int]$code = 200) {
    $r = $ctx.Response
    $r.StatusCode = $code; $r.ContentType = $type
    $r.Headers['Cache-Control'] = 'no-store'
    $r.ContentLength64 = $bytes.Length
    try { $r.OutputStream.Write($bytes, 0, $bytes.Length) } catch {}
    try { $r.OutputStream.Close() } catch {}
}
function Send-Text($ctx, [string]$text, [string]$type = 'application/json; charset=utf-8', [int]$code = 200) {
    Send-Bytes $ctx ([Text.Encoding]::UTF8.GetBytes($text)) $type $code
}

function Send-File($ctx, [string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { Send-Text $ctx 'not found' 'text/plain' 404; return }
    $ext = [IO.Path]::GetExtension($path).ToLower()
    $type = $mime[$ext]; if (-not $type) { $type = 'application/octet-stream' }
    $fs = [IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
        $len = $fs.Length; $start = 0; $end = $len - 1; $code = 200
        $range = $ctx.Request.Headers['Range']
        $r = $ctx.Response
        $r.Headers['Accept-Ranges'] = 'bytes'
        if ($range -and $range -match 'bytes=(\d*)-(\d*)') {
            if ($matches[1] -ne '') { $start = [int64]$matches[1] }
            if ($matches[2] -ne '') { $end = [int64]$matches[2] } else { $end = [Math]::Min($len - 1, $start + 2MB) }   # small chunks keep the API responsive
            if ($matches[1] -eq '' -and $matches[2] -ne '') { $start = $len - [int64]$matches[2]; $end = $len - 1 }
            $code = 206
            $r.Headers['Content-Range'] = "bytes $start-$end/$len"
        }
        $count = $end - $start + 1
        $r.StatusCode = $code; $r.ContentType = $type; $r.ContentLength64 = $count
        # code is never taken from the browser's disk cache (a planted cache entry would run with the token)
        $r.Headers['Cache-Control'] = if ($ext -in @('.html', '.js', '.css', '.json')) { 'no-store' } else { 'max-age=60' }
        $fs.Seek($start, 'Begin') | Out-Null
        $buf = New-Object byte[] 65536
        while ($count -gt 0) {
            $n = $fs.Read($buf, 0, [Math]::Min($buf.Length, $count)); if ($n -le 0) { break }
            $r.OutputStream.Write($buf, 0, $n); $count -= $n
        }
    } catch {} finally { $fs.Close(); try { $ctx.Response.OutputStream.Close() } catch {} }
}

function Read-Body($ctx) {
    $sr = New-Object IO.StreamReader($ctx.Request.InputStream, [Text.Encoding]::UTF8)
    $t = $sr.ReadToEnd(); $sr.Close(); return $t
}

function Invoke-Route($ctx) {
    $req = $ctx.Request
    $path = $req.Url.AbsolutePath
    $q = $req.QueryString

    if ($path -eq '/' -or $path -eq '/index.html') { Send-File $ctx (Join-Path $TC.Root 'ui\index.html'); return }
    if ($path -eq '/alive') { Send-Text $ctx 'ok' 'text/plain'; return }

    if ($path.StartsWith('/ui/') -or $path.StartsWith('/assets/')) {
        $rel = [Uri]::UnescapeDataString($path.TrimStart('/')).Replace('/', '\')
        $full = [IO.Path]::GetFullPath((Join-Path $TC.Root $rel))
        # resolved path must be inside ui\ or assets\ (a plain prefix check also matched sibling folders)
        if (-not ((Test-TCChildPath $full (Join-Path $TC.Root 'ui')) -or (Test-TCChildPath $full (Join-Path $TC.Root 'assets')))) { Send-Text $ctx 'no' 'text/plain' 403; return }
        Send-File $ctx $full; return
    }

    # everything below needs the per-session token
    # /media (GET, pictures for <img>/<video>) may carry the token in the URL. Calls that change things (/api, /upload)
    # need it in the X-TC-Token header: a page of another origin (a file:// or data: page, a website) can only send that
    # header after a CORS preflight, which this server never answers. They must also come from this server's own page.
    $hdrTok = $req.Headers['X-TC-Token']
    $tok = if ($hdrTok) { $hdrTok } else { $q['t'] }
    if ($tok -ne $token) { Send-Text $ctx '{"ok":false,"error":"bad token"}' 'application/json' 403; return }
    if ($path -ne '/media') {
        $origin = $req.Headers['Origin']
        if ($req.HttpMethod -ne 'POST' -or $hdrTok -ne $token -or ($origin -and $origin -ne "http://127.0.0.1:$Port")) { Send-Text $ctx '{"ok":false,"error":"refused"}' 'application/json' 403; return }
    }
    if (-not (Test-TCCaller $ctx)) { Send-Text $ctx '{"ok":false,"error":"only the TRANSCRYPTO window may use this server"}' 'application/json' 403; return }
    $global:TCServer.LastSeen = Get-Date

    # previews only: pictures, videos, icons, cursors, sounds, fonts (no reading arbitrary files as administrator)
    if ($path -eq '/media') {
        $mp = [Environment]::ExpandEnvironmentVariables([string]$q['p'])
        if (-not (Test-TCFileType $mp @('image', 'video', 'icon', 'cursor', 'sound', 'font'))) { Send-Text $ctx 'not a media file' 'text/plain' 403; return }
        # never rendered as a page of this origin, whatever the file really contains
        $ctx.Response.Headers['X-Content-Type-Options'] = 'nosniff'; $ctx.Response.Headers['Content-Security-Policy'] = "sandbox; default-src 'none'"
        Send-File $ctx $mp; return
    }

    # uploads land in the wallpaper library only, as pictures or videos
    if ($path -eq '/upload') {
        $name = [IO.Path]::GetFileName([string]$q['name'])
        if (-not $name -or -not (Test-TCFileType $name @('image', 'video'))) { Send-Text $ctx '{"ok":false,"error":"only pictures and videos can be added"}' 'application/json' 400; return }
        $dest = Join-Path $TC.Library $name
        $fs = [IO.File]::Create($dest); try { $req.InputStream.CopyTo($fs) } finally { $fs.Close() }
        Send-Text $ctx (@{ ok = $true; data = $dest } | ConvertTo-Json -Compress); return
    }

    if ($path -eq '/api') {
        $res = $null
        try {
            $body = (Read-Body $ctx) | ConvertFrom-Json
            $fn = [string]$body.fn
            if (-not $API.ContainsKey($fn)) { throw "Unknown API: $fn" }
            $args2 = $body.args; if ($null -eq $args2) { $args2 = New-Object PSObject }
            $data = & $API[$fn] $args2
            $res = @{ ok = $true; data = $data }
        } catch {
            Write-TCLog "API $fn error: $($_.Exception.Message)"
            $res = @{ ok = $false; error = $_.Exception.Message }
        }
        $json = $null
        try { $json = ConvertTo-Json -InputObject $res -Depth 10 -Compress } catch { $json = '{"ok":false,"error":"serialize failed"}' }
        Send-Text $ctx $json; return
    }
    Send-Text $ctx 'not found' 'text/plain' 404
}

# open the app window (non elevated Edge in app mode) unless the launcher does it
if (-not $NoBrowser -and -not $Test) { try { Open-TCWindow $Port $token } catch { Write-TCLog "window open failed: $_" } }

try {
    while ($TCServer.Running) {
        $ar = $listener.BeginGetContext($null, $null)
        while (-not $ar.AsyncWaitHandle.WaitOne(1000)) {
            # Edge throttles/freezes timers of a minimized window, so heartbeats alone are not proof the window closed.
            # Primary signal: is our Edge app window (its own ui-profile) still running?
            $now = Get-Date
            if (($now - $TCServer.LastWindowCheck).TotalSeconds -ge 8) {
                $TCServer.LastWindowCheck = $now
                $alive = Test-TCWindow
                if ($alive) { $TCServer.WindowSeen = $true; $TCServer.WindowGone = 0 }
                elseif ($TCServer.WindowSeen) { $TCServer.WindowGone++; if ($TCServer.WindowGone -ge 2) { $TCServer.Running = $false } }
            }
            # Fallback when the UI runs in some other browser: no request at all for 10 minutes
            if (-not $TCServer.WindowSeen) {
                $last = if ($TCServer.LastSeen) { $TCServer.LastSeen } else { $TCServer.Started }
                if (($now - $last).TotalMinutes -gt 10) { $TCServer.Running = $false }
            }
            if (-not $TCServer.Running) { break }
        }
        if (-not $TCServer.Running) { break }
        $ctx = $null
        try { $ctx = $listener.EndGetContext($ar) } catch { continue }
        try { Invoke-Route $ctx } catch { Write-TCLog "route error: $_"; try { $ctx.Response.Abort() } catch {} }
    }
} finally {
    Write-TCLog 'server shutting down'
    try { $listener.Stop(); $listener.Close() } catch {}
    Remove-Item $session -ErrorAction SilentlyContinue
}
