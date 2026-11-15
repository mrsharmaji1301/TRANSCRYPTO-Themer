// TRANSCRYPTO Themer - shell host: local data server, the WebView2 page that becomes the taskbar + Start menu,
// the live web background behind the desktop icons, and the Win key hook. See Shell.cs for the overview.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Transcrypto
{
    // ------------------------------------------------------------------ 127.0.0.1-only HTTP server for the shell pages
    public class ShellServer
    {
        TcpListener listener; Thread loop; volatile bool running;
        public int Port; public string Token; string root;
        public ShellData Data;
        public NotifyWatch Notifs;
        public DateTime LastPoll = DateTime.Now;   // last API call from the page: its heartbeat
        public int InFlight;                        // API calls being answered right now (a slow first Start-menu load is not a stuck page)
        public bool BgPaused;   // the live background stops animating while a maximised / fullscreen window hides the desktop
        public Func<string, Dictionary<string, string>, object> OnAction;   // invoked on the UI thread
        public Func<Dictionary<string, string>, object> OnTray;             // hidden tray icons: list / click (a server thread, not the UI thread)
        public Func<Dictionary<string, object>> Extra;                      // config bits for /api/start (projects, palette)
        public byte[] Backdrop;
        Control ui;
        readonly object evLock = new object(); List<string> evLog = new List<string>(); int evSeq;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

        public ShellServer(Control uiThread, string appRoot) { ui = uiThread; root = appRoot; Data = new ShellData(); Notifs = new NotifyWatch(appRoot); Notifs.OnEvent = Push; }

        public void Start()
        {
            if (running) return;
            Token = Guid.NewGuid().ToString("N");
            foreach (int p in new int[] { 47830, 47831, 47832, 0 })
            {
                try { listener = new TcpListener(IPAddress.Loopback, p); listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port; break; }
                catch { listener = null; }
            }
            if (listener == null) { Program.Log("shell server: no port"); return; }
            running = true;
            loop = new Thread(Accept); loop.IsBackground = true; loop.Start();
            // the Themer backend reads this to preview and test the shell
            try { File.WriteAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRANSCRYPTO", "shell.json"), Json.Serialize(new Dictionary<string, object> { { "port", Port }, { "token", Token } })); } catch { }
            Program.Log("shell server on " + Port);
        }

        public void Stop() { running = false; try { if (listener != null) listener.Stop(); } catch { } lock (evLock) Monitor.PulseAll(evLock); }

        public void Push(string ev) { lock (evLock) { evLog.Add(ev); evSeq++; if (evLog.Count > 50) evLog.RemoveAt(0); Monitor.PulseAll(evLock); } }

        void Accept()
        {
            while (running)
            {
                try { TcpClient c = listener.AcceptTcpClient(); ThreadPool.QueueUserWorkItem(Serve, c); }
                catch { if (!running) return; }
            }
        }

        void Serve(object o)
        {
            TcpClient c = (TcpClient)o;
            try
            {
                c.ReceiveTimeout = 10000; c.SendTimeout = 10000;
                NetworkStream ns = c.GetStream();
                string head = ReadHead(ns); if (head == null) return;
                string[] lines = head.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                string[] first = lines[0].Split(' '); if (first.Length < 2) return;
                string host = "";
                foreach (string l in lines) if (l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase)) host = l.Substring(5).Trim().ToLowerInvariant();
                // DNS-rebinding guard: only pages loaded from our own loopback origin get answers
                if (!(host.StartsWith("127.0.0.1:") || host.StartsWith("localhost:"))) { Send(ns, 403, "text/plain", Encoding.UTF8.GetBytes("no"), false); return; }
                string target = first[1], path = target, qs = "";
                int qi = target.IndexOf('?'); if (qi >= 0) { path = target.Substring(0, qi); qs = target.Substring(qi + 1); }
                Dictionary<string, string> q = Query(qs);
                // any API call from the page (other than the long poll) is its heartbeat; while one is still being answered, the page is not stuck
                bool beat = path.StartsWith("/api/") && path != "/api/events";
                if (beat) { LastPoll = DateTime.Now; Interlocked.Increment(ref InFlight); }
                try { Route(ns, Uri.UnescapeDataString(path), q); }
                finally { if (beat) { Interlocked.Decrement(ref InFlight); LastPoll = DateTime.Now; } }
            }
            catch (Exception ex) { if (!(ex is IOException)) Program.Log("shell http: " + ex.Message); }
            finally { try { c.Close(); } catch { } }
        }

        static string ReadHead(NetworkStream ns)
        {
            byte[] buf = new byte[16384]; int n = 0;
            while (n < buf.Length)
            {
                int r = ns.Read(buf, n, buf.Length - n); if (r <= 0) return null; n += r;
                string s = Encoding.ASCII.GetString(buf, 0, n); int e = s.IndexOf("\r\n\r\n");
                if (e >= 0) return s.Substring(0, e);
            }
            return null;
        }

        static Dictionary<string, string> Query(string qs)
        {
            Dictionary<string, string> q = new Dictionary<string, string>();
            foreach (string kv in qs.Split('&'))
            {
                if (kv.Length == 0) continue; int e = kv.IndexOf('=');
                string k = e < 0 ? kv : kv.Substring(0, e), v = e < 0 ? "" : kv.Substring(e + 1);
                q[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' '));
            }
            return q;
        }

        static void Send(NetworkStream ns, int code, string type, byte[] body, bool cache)
        {
            string status = code == 200 ? "OK" : code == 404 ? "Not Found" : code == 403 ? "Forbidden" : "Error";
            string h = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                       "\r\nCache-Control: " + (cache ? "max-age=3600" : "no-store") + "\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(h); ns.Write(hb, 0, hb.Length); ns.Write(body, 0, body.Length);
        }
        static void SendJson(NetworkStream ns, object o) { Send(ns, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(Json.Serialize(o)), false); }

        static readonly Dictionary<string, string> Mime = new Dictionary<string, string> {
            { ".html", "text/html; charset=utf-8" }, { ".js", "text/javascript; charset=utf-8" }, { ".css", "text/css; charset=utf-8" },
            { ".png", "image/png" }, { ".jpg", "image/jpeg" }, { ".svg", "image/svg+xml" }, { ".json", "application/json" }, { ".woff2", "font/woff2" }, { ".mp3", "audio/mpeg" } };

        void Route(NetworkStream ns, string path, Dictionary<string, string> q)
        {
            if (path.StartsWith("/shell/"))
            {
                string full = Path.GetFullPath(Path.Combine(root, "shell", path.Substring(7).Replace('/', '\\')));
                // with the separator: "shell-profile\..." is not inside "shell\"
                if (!full.StartsWith(Path.Combine(root, "shell") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) { Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("not found"), false); return; }
                string mt; if (!Mime.TryGetValue(Path.GetExtension(full).ToLowerInvariant(), out mt)) mt = "application/octet-stream";
                Send(ns, 200, mt, File.ReadAllBytes(full), false); return;
            }
            string tok; if (!q.TryGetValue("t", out tok) || tok != Token) { Send(ns, 403, "text/plain", Encoding.UTF8.GetBytes("bad token"), false); return; }
            switch (path)
            {
                case "/api/state":
                {
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["apps"] = Data.Apps(); s["sys"] = Data.Sys(); s["time"] = DateTime.Now.ToString("o");
                    SendJson(ns, s); return;
                }
                case "/api/start":
                {
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["apps"] = Data.StartApps(); s["pins"] = Data.StartPins(); s["recentFiles"] = Data.Recent(false, 8); s["recentFolders"] = Data.Recent(true, 8);
                    if (Extra != null) foreach (KeyValuePair<string, object> kv in Extra()) s[kv.Key] = kv.Value;
                    SendJson(ns, s); return;
                }
                case "/api/icon":
                {
                    string k; int size = 64; string sz;
                    if (q.TryGetValue("s", out sz)) int.TryParse(sz, out size);
                    byte[] png = q.TryGetValue("k", out k) ? Data.Icon(k, Math.Max(16, Math.Min(256, size))) : null;
                    if (png == null) Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("none"), false); else Send(ns, 200, "image/png", png, !k.StartsWith("hwnd:"));
                    return;
                }
                case "/api/notifs":
                {
                    Dictionary<string, object> s = new Dictionary<string, object>();
                    s["items"] = Notifs.List(); s["denied"] = Notifs.Denied;
                    SendJson(ns, s); return;
                }
                case "/api/tray":
                {
                    Dictionary<string, object> r = new Dictionary<string, object>(); r["result"] = OnTray == null ? null : OnTray(q);
                    SendJson(ns, r); return;
                }
                case "/api/quick":   // details for the quick settings panel (slow-ish calls stay off the UI thread)
                {
                    SendJson(ns, Data.Quick()); return;
                }
                case "/api/backdrop":
                    if (Backdrop == null) Send(ns, 404, "text/plain", new byte[0], false); else Send(ns, 200, "image/jpeg", Backdrop, false);
                    return;
                case "/api/wallpaper":
                {
                    byte[] w = Wallpaper();
                    if (w == null) Send(ns, 404, "text/plain", new byte[0], false); else Send(ns, 200, "image/jpeg", w, false);
                    return;
                }
                case "/api/events":
                {
                    int since = 0; string s; if (q.TryGetValue("since", out s)) int.TryParse(s, out since);
                    List<string> outEv = new List<string>(); int seq;
                    lock (evLock)
                    {
                        if (evSeq <= since) Monitor.Wait(evLock, 25000);
                        seq = evSeq; int fresh = Math.Min(evLog.Count, seq - since);
                        for (int i = evLog.Count - fresh; i < evLog.Count; i++) if (i >= 0) outEv.Add(evLog[i]);
                    }
                    Dictionary<string, object> r = new Dictionary<string, object>(); r["seq"] = seq; r["events"] = outEv;
                    SendJson(ns, r); return;
                }
                case "/api/do":
                {
                    string a; q.TryGetValue("a", out a);
                    object res = null;
                    if (OnAction != null) { try { res = ui.Invoke(new Func<object>(delegate { return OnAction(a ?? "", q); })); } catch (Exception ex) { res = "error: " + ex.Message; } }
                    Dictionary<string, object> r = new Dictionary<string, object>(); r["ok"] = true; r["result"] = res;
                    SendJson(ns, r); return;
                }
            }
            Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("unknown"), false);
        }

        // the desktop picture, scaled to the primary screen, for the glass behind the bar
        byte[] wpCache; string wpKey;
        byte[] Wallpaper()
        {
            try
            {
                string file = (string)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", "");
                string tc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (string.IsNullOrEmpty(file) || !File.Exists(file)) file = File.Exists(tc) ? tc : "";
                Rectangle b = Screen.PrimaryScreen.Bounds;
                string key = file + "|" + (file != "" ? File.GetLastWriteTimeUtc(file).Ticks : 0) + "|" + b.Size;
                if (key == wpKey && wpCache != null) return wpCache;
                using (Bitmap bmp = new Bitmap(b.Width, b.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    object bg = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Colors", "Background", "0 0 0");
                    string[] rgb = ((bg as string) ?? "0 0 0").Split(' ');
                    try { g.Clear(Color.FromArgb(int.Parse(rgb[0]), int.Parse(rgb[1]), int.Parse(rgb[2]))); } catch { g.Clear(Color.Black); }
                    if (file != "")
                        using (Image im = Image.FromFile(file))
                        {   // "fill": cover the screen, centred
                            double s = Math.Max((double)b.Width / im.Width, (double)b.Height / im.Height);
                            int w = (int)(im.Width * s), h = (int)(im.Height * s);
                            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            g.DrawImage(im, (b.Width - w) / 2, (b.Height - h) / 2, w, h);
                        }
                    wpCache = Jpeg(bmp, 88); wpKey = key; return wpCache;
                }
            }
            catch (Exception ex) { Program.Log("shell wallpaper: " + ex.Message); return null; }
        }

        public static byte[] Jpeg(Bitmap bmp, long quality)
        {
            ImageCodecInfo codec = null; foreach (ImageCodecInfo c in ImageCodecInfo.GetImageEncoders()) if (c.MimeType == "image/jpeg") codec = c;
            EncoderParameters ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, codec, ep); return ms.ToArray(); }
        }
    }

    // ------------------------------------------------------------------ the shell: taskbar + Start menu
    public class ShellHost
    {
        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);

        Control ui; ShellServer srv; WebSurface web;   // the page: WebView2 inside our own frame window
        public string Design = "none", Palette = "", Bg = "none", BgPalette = "";
        public bool AutoHide = false;  // default: the bar reserves its strip like the Windows taskbar (maximised windows end above it); true: it hides and slides in at the bottom edge
        System.Windows.Forms.Timer edge; bool revealSent;
        Rectangle mon; bool menuOpen; IntPtr prevFg = IntPtr.Zero;
        List<int[]> barRects = new List<int[]>(); int reserve = 0;
        System.Windows.Forms.Timer guard;
        AppBarWindow appbar; volatile IntPtr tray = IntPtr.Zero; volatile bool trayHidden; int trayState = -1;   // (also read on the hook thread)
        IntPtr kbHook = IntPtr.Zero; LowLevelProc kbProc; bool winDown, winCombo;
        Thread hookThread; volatile uint hookThreadId; DateTime menuAt, escAt;
        [StructLayout(LayoutKind.Sequential)] struct HMSG { public IntPtr hwnd; public uint message; public IntPtr w, l; public uint time; public int x, y; }
        [DllImport("user32.dll")] static extern int GetMessage(out HMSG m, IntPtr h, uint a, uint b);
        [DllImport("user32.dll")] static extern bool PostThreadMessage(uint t, uint msg, IntPtr w, IntPtr l);
        void StartHook()
        {
            if (hookThread != null) return;
            hookThread = new Thread(delegate ()
            {
                hookThreadId = SN.GetCurrentThreadId();
                kbHook = SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, kbProc, GetModuleHandle(null), 0);
                IntPtr trayEv = TrayWatch();
                HMSG m; while (GetMessage(out m, IntPtr.Zero, 0, 0) > 0)
                    if (m.message == WM_TRAYCHANGED) { if (trayEv != IntPtr.Zero) UnhookWinEvent(trayEv); trayEv = TrayWatch(); }   // Explorer restarted
                if (trayEv != IntPtr.Zero) UnhookWinEvent(trayEv);
                if (kbHook != IntPtr.Zero) { UnhookWindowsHookEx(kbHook); kbHook = IntPtr.Zero; }
            });
            hookThread.IsBackground = true; hookThread.Start();
        }
        // Explorer shows its taskbar again by itself when the work area changes (our bar reserves a new height after a design
        // switch): hide it the moment it shows, on the hook thread, instead of up to a timer tick later
        const uint WM_TRAYCHANGED = 0x8001;   // WM_APP + 1, to the hook thread
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindowAsync(IntPtr h, int cmd);
        WinEventProc trayProc; int trayHides; DateTime trayHideAt;
        IntPtr TrayWatch()
        {
            IntPtr t = tray; if (t == IntPtr.Zero) return IntPtr.Zero;
            uint pid; SN.GetWindowThreadProcessId(t, out pid); if (pid == 0) return IntPtr.Zero;
            if (trayProc == null) trayProc = TrayShown;
            return SetWinEventHook(0x8002 /*EVENT_OBJECT_SHOW*/, 0x8002, IntPtr.Zero, trayProc, pid, 0, 0x0000 /*OUTOFCONTEXT*/);
        }
        void TrayShown(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (obj != 0 /*OBJID_WINDOW*/ || hwnd != tray || !trayHidden) return;
            DateTime now = DateTime.Now;
            if ((now - trayHideAt).TotalSeconds > 1) { trayHideAt = now; trayHides = 0; }
            if (++trayHides > 20) return;   // Explorer insisting: no tight loop, the regular check takes over
            ShowWindowAsync(hwnd, 0 /*SW_HIDE*/);
        }
        void StopHook()
        {
            if (hookThread == null) return;
            if (hookThreadId != 0) PostThreadMessage(hookThreadId, 0x0012 /*WM_QUIT*/, IntPtr.Zero, IntPtr.Zero);
            hookThread = null; hookThreadId = 0;
        }

        // give the screen back without refocusing the previous window (an app is about to come forward)
        void LeaveMenu() { menuOpen = false; srv.Backdrop = null; ApplyRegion(); }

        // the open menu covers the whole screen; if the page stops answering (or ignores Esc) the engine takes the screen back by itself
        void MenuWatchdog()
        {
            if (!menuOpen) return;
            DateTime now = DateTime.Now;
            bool stuck = (now - menuAt).TotalSeconds > 5 && (now - srv.LastPoll).TotalSeconds > 5 && srv.InFlight == 0;
            bool esc = escAt > menuAt && (now - escAt).TotalMilliseconds > 700;
            if (!stuck && !esc) return;
            Program.Log(stuck ? "shell: the page stopped responding with the menu open; screen given back" : "shell: Esc closed the menu from the engine");
            menuOpen = false; srv.Backdrop = null; ApplyRegion();
            if (prevFg != IntPtr.Zero && SN.IsWindow(prevFg)) SN.ForceForeground(prevFg);
            srv.Push("escape");
            if (stuck) web.Recreate("the page stopped responding with the menu open");
        }
        DateTime armed = DateTime.MinValue; string armedAction = "";

        public bool Active { get { return Design != "none"; } }
        public Action OnExit; public Action<int, int> OnBgMetrics;   // red X on the bar: quit the engine (the shell stops and the Windows taskbar comes back)

        public ShellHost(Control uiThread, ShellServer server)
        {
            ui = uiThread; srv = server; kbProc = KeyHook;
            web = new WebSurface("shell", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TRANSCRYPTO\shell-webview2"), true);
            web.OnMessage = PageMessage;
            guard = new System.Windows.Forms.Timer(); guard.Interval = 1000; guard.Tick += delegate { Guard(); };
            edge = new System.Windows.Forms.Timer(); edge.Interval = 60; edge.Tick += delegate { EdgeCheck(); };
            // new notifications over a fullscreen game/video only land in the list, without a banner
            srv.Notifs.OnEvent = delegate(string e) { srv.Push(e.StartsWith("notif:") && Engine.IsFullscreenApp(SN.GetForegroundWindow()) ? e + ":quiet" : e); };
        }

        // bg: the live background in use, so the glass behind the bar shows the same scene instead of the static wallpaper
        public void Apply(string design, string palette, string bg, string bgPalette, bool autoHide)
        {
            if (design == Design && palette == Palette && bg == Bg && bgPalette == BgPalette && autoHide == AutoHide && (design == "none" || frame != null)) return;
            // another design, palette or scene behind the glass while a bar is up: only the page changes; the frame, the reserved strip
            // and the hidden Windows taskbar stay (no gap without a taskbar). The new page reports its own bar.
            if (frame != null && !frame.IsDisposed && design != "none" && Design != "none" && autoHide == AutoHide)
            {
                Design = design; Palette = palette; Bg = bg; BgPalette = bgPalette;
                if (menuOpen) LeaveMenu();
                barRects.Clear(); ApplyRegion(); revealSent = false;   // the new page starts with its bar hidden (auto-hide)
                web.Navigate(ShellUrl());
                Program.Log("shell: " + design);
                return;
            }
            Stop();
            Design = design; Palette = palette; Bg = bg; BgPalette = bgPalette; AutoHide = autoHide;
            if (design == "none") return;
            srv.Start(); srv.Notifs.Start();
            mon = Screen.PrimaryScreen.Bounds;
            string url = ShellUrl();
            // our frame covers the screen but shows nothing until the page reports where its bar is (the region starts empty)
            frame = new ShellFrame(mon);
            IntPtr fh = frame.Handle;
            srv.Data.ShellHwnd = fh; srv.Data.FrameHwnd = fh;
            ApplyRegion();
            frame.Show(); Place();
            web.Show(frame, url);
            HideTaskbar();   // right away: the Windows taskbar must not sit there while the page starts
            guard.Start(); edge.Start(); revealSent = false; coverState = -1; fsHidden = false; lastUp = DateTime.Now;
            StartHook();
            Program.Log("shell: " + design);
        }

        string ShellUrl()
        {
            return string.Format("http://127.0.0.1:{0}/shell/{1}.html?t={2}&pal={3}&bg={4}&bgpal={5}&ah={6}", srv.Port, Design, srv.Token, Uri.EscapeDataString(Palette ?? ""),
                Uri.EscapeDataString(Bg ?? "none"), Uri.EscapeDataString(BgPalette ?? ""), AutoHide ? 1 : 0);
        }

        public void Stop()
        {
            guard.Stop(); edge.Stop(); srv.Notifs.Stop();
            StopHook();
            web.Close(); menuOpen = false; barRects.Clear(); reserve = 0;
            if (frame != null) { frame.AllowClose(); frame.Close(); frame.Dispose(); frame = null; }
            srv.Data.ShellHwnd = IntPtr.Zero; srv.Data.FrameHwnd = IntPtr.Zero;
            if (appbar != null) { appbar.Remove(); appbar = null; }
            RestoreTaskbar();
            Design = "none";
        }

        // ---------- keep everything in place: window alive, on top, Windows taskbar hidden
        int memTick;
        void Guard()
        {
            if (frame == null || frame.IsDisposed) return;
            // watchdog: a page that keeps growing is restarted long before it can starve the PC (a browser froze it once)
            if (++memTick % 30 == 0 && !menuOpen && web.Ready)
            {
                long mb = web.MemoryMB();
                if (mb > 1200) { web.Recreate("WebView2 at " + mb + " MB"); return; }
            }
            if (!web.Exists) web.Recreate("page missing");
            // a new resolution or primary screen: the frame (and with it the page) follows, the page reports its new bar
            Rectangle now = Screen.PrimaryScreen.Bounds;
            if (now != mon)
            {
                mon = now; Place(); ApplyRegion();
                if (reserve > 0 && !AutoHide && appbar != null) appbar.Reserve(mon, reserve);
                Program.Log("shell: screen is now " + mon.Width + "x" + mon.Height);
            }
            if (!menuOpen) SN.SetWindowPos(frame.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            if (HoldTaskbar) HideTaskbar();
            else if (trayHidden) { RestoreTaskbar(); Program.Log("shell: the page is not up, the Windows taskbar is back until it is"); }
        }

        // the Windows taskbar stays hidden while our page is up, and for a short start / restart; never for longer without a bar
        DateTime lastUp;
        bool HoldTaskbar { get { if (web.Up) lastUp = DateTime.Now; return (DateTime.Now - lastUp).TotalSeconds < 15; } }

        ShellFrame frame;

        void Place()
        {
            SN.SetWindowPos(frame.Handle, new IntPtr(-1), mon.X, mon.Y, mon.Width, mon.Height, 0x0010 | 0x0040);
        }

        void ApplyRegion()
        {
            if (frame == null) return;
            IntPtr rgn;
            if (menuOpen) rgn = SN.CreateRectRgn(0, 0, mon.Width, mon.Height);
            else
            {
                rgn = SN.CreateRectRgn(0, 0, 0, 0);
                foreach (int[] r in barRects)
                {
                    IntPtr p = r[4] > 0 ? SN.CreateRoundRectRgn(r[0], r[1], r[0] + r[2] + 1, r[1] + r[3] + 1, r[4] * 2, r[4] * 2)
                                        : SN.CreateRectRgn(r[0], r[1], r[0] + r[2], r[1] + r[3]);
                    SN.CombineRgn(rgn, rgn, p, 2 /*RGN_OR*/); SN.DeleteObject(p);
                }
            }
            SN.SetWindowRgn(frame.Handle, rgn, true);   // the window owns the region from here on
            frame.NoActivate(!menuOpen);
        }

        // our top-level window: borderless, topmost, no taskbar button; the page (WebView2) fills it
        class ShellFrame : Form
        {
            public ShellFrame(Rectangle b)
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; Bounds = b; TopMost = true;
                BackColor = Color.Black; Text = "TRANSCRYPTO Shell Frame";
            }
            protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 | 0x08000000; return cp; } }
            protected override bool ShowWithoutActivation { get { return true; } }
            // Alt+F4 with the Start menu open must not close the taskbar itself; only the engine closes it
            bool closable;
            public void AllowClose() { closable = true; }
            protected override void OnFormClosing(FormClosingEventArgs e) { if (!closable && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; } base.OnFormClosing(e); }
            // bar clicks must not steal focus from the app in front (like the real taskbar); the open menu needs it for typing
            public void NoActivate(bool on)
            {
                long ex = SN.GetLong(Handle, -20).ToInt64();
                long nx = on ? (ex | 0x08000000L) : (ex & ~0x08000000L);
                if (nx != ex) SN.SetLong(Handle, -20, new IntPtr(nx));
            }
        }

        // ---------- auto-hide: the cursor touching the bottom edge reveals the bar (not over fullscreen games/video)
        void EdgeCheck()
        {
            if (!Active) return;
            MenuWatchdog();
            if (tray != IntPtr.Zero && trayHidden && SN.IsWindowVisible(tray)) SN.ShowWindow(tray, 0);   // no fighting with the Windows taskbar
            if (web.Up) { if (AutoHide) CoverCheck(); else FullscreenCheck(); }
            if (!AutoHide || menuOpen || revealSent || !web.Up) return;
            SN.POINT p; if (!SN.GetCursorPos(out p)) return;
            if (p.Y < mon.Bottom - 1 || p.X < mon.Left || p.X >= mon.Right) return;
            if (Engine.IsFullscreenApp(SN.GetForegroundWindow())) return;
            revealSent = true; srv.Push("reveal");
        }

        // ---------- auto-hide only hides when it has to: the window in front reaches down into the bar's strip (maximised, or dragged low).
        // On the bare desktop, or with small windows, the bar stays put. The page gets "cover:1" / "cover:0".
        int coverState = -1, coverTick;
        void CoverCheck()
        {
            if (++coverTick % 4 != 0) return;   // ~4 times a second is plenty
            IntPtr fg = SN.GetForegroundWindow();
            if (fg == IntPtr.Zero || (frame != null && fg == frame.Handle)) return;   // our own menu / panel in front: keep the last state
            int c = 0;
            if (!Engine.IsShellWindow(fg) && SN.IsWindowVisible(fg) && !SN.IsIconic(fg))
            {
                SN.RECT r; int strip = Math.Max(reserve, 48);
                if (SN.GetWindowRect(fg, out r) && r.Bottom > mon.Bottom - strip + 8 && r.Top < mon.Bottom && r.Right > mon.Left + 8 && r.Left < mon.Right - 8) c = 1;
            }
            if (c != coverState) { coverState = c; srv.Push("cover:" + c); }
        }

        // ---------- reserved-strip mode: the bar is topmost, so it steps aside while a fullscreen game / video is in front
        bool fsHidden;
        void FullscreenCheck()
        {
            if (++coverTick % 4 != 0 || frame == null || menuOpen) return;
            IntPtr fg = SN.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == frame.Handle) return;
            bool fs = Engine.IsFullscreenApp(fg);
            if (fs != fsHidden) { fsHidden = fs; SN.ShowWindow(frame.Handle, fs ? 0 /*SW_HIDE*/ : 8 /*SW_SHOWNA*/); }
        }

        // ---------- "Show hidden icons": Windows' own overflow flyout (the real icons, with their clicks and menus), opened through
        // UI Automation on the hidden Windows taskbar and moved above our chevron. A second press closes it.
        AutomationElement trayChevron;
        AutomationElement Chevron()
        {
            AutomationElement b = trayChevron;
            try { if (b != null && b.Current.ClassName == null) b = null; } catch { b = null; }   // explorer restarted: find it again
            if (b != null) return b;
            IntPtr tray = SN.FindWindow("Shell_TrayWnd", null);
            IntPtr bridge = SN.FindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
            if (bridge == IntPtr.Zero) { Program.Log("shell: no taskbar XAML for hidden icons"); return null; }
            AutomationElement root = AutomationElement.FromHandle(bridge);
            b = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "Show Hidden Icons"));
            if (b == null)   // other display languages: the chevron is the first plain tray button
                b = root.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                                                                             new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton")));
            if (b == null) Program.Log("shell: hidden icons button not found");
            return trayChevron = b;
        }

        void TrayOverflow(int ax, int ay)
        {
            try
            {
                IntPtr fly = SN.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                bool wasOpen = fly != IntPtr.Zero && SN.IsWindowVisible(fly);
                if (fly != IntPtr.Zero) Ghost(fly, false);
                AutomationElement b = Chevron(); if (b == null) return;
                object pat;
                if (!b.TryGetCurrentPattern(InvokePattern.Pattern, out pat)) return;
                ((InvokePattern)pat).Invoke();
                if (wasOpen) return;
                // the flyout opens where Windows' (hidden) taskbar would be; bring it above our chevron, twice, as it settles after its animation
                for (int i = 0, moved = 0; i < 40 && moved < 2; i++)
                {
                    Thread.Sleep(moved == 0 ? 25 : 150);
                    fly = SN.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                    if (fly == IntPtr.Zero || !SN.IsWindowVisible(fly)) continue;
                    SN.RECT r; if (!SN.GetWindowRect(fly, out r)) continue;
                    int w = r.Right - r.Left, h = r.Bottom - r.Top;
                    int x = Math.Max(mon.Left + 8, Math.Min(mon.Right - w - 8, ax - w / 2)), y = Math.Max(mon.Top + 8, ay - h - 10);
                    SN.SetWindowPos(fly, new IntPtr(-1), x, y, 0, 0, 0x0001 /*NOSIZE*/);
                    moved++;
                }
            }
            catch (Exception ex) { Program.Log("shell: hidden icons: " + ex.Message); }
        }

        // ---------- "Show hidden icons" in the design's own panel. The icons are read from Windows' overflow flyout, opened
        // invisibly (alpha 1) and closed again. A click on one of them in our panel replays a real click on that icon where
        // the cursor is: the flyout is moved so the icon sits under it. Left opens the app, right shows its own menu.
        // Called on a server thread (never the UI thread: UI Automation and the waits would stall the bar).
        [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint data, UIntPtr extra);
        [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
        [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
        readonly object trayLock = new object();
        const string FlyoutClass = "TopLevelWindowForOverflowXamlIsland";

        static void Ghost(IntPtr fly, bool on)
        {
            long ex = SN.GetLong(fly, -20).ToInt64();
            if (on) { SN.SetLong(fly, -20, new IntPtr(ex | 0x80000L)); SetLayeredWindowAttributes(fly, 0, 1, 2 /*LWA_ALPHA*/); }
            else if ((ex & 0x80000L) != 0) { SetLayeredWindowAttributes(fly, 0, 255, 2); SN.SetLong(fly, -20, new IntPtr(ex & ~0x80000L)); }
        }

        IntPtr OpenFlyoutHidden()
        {
            IntPtr fly = SN.FindWindow(FlyoutClass, null);
            if (fly != IntPtr.Zero && SN.IsWindowVisible(fly)) { Ghost(fly, true); return fly; }
            AutomationElement b = Chevron(); object pat;
            if (b == null || !b.TryGetCurrentPattern(InvokePattern.Pattern, out pat)) return IntPtr.Zero;
            if (fly != IntPtr.Zero) Ghost(fly, true);   // Windows reuses the window: make it invisible before it shows
            ((InvokePattern)pat).Invoke();
            for (int i = 0; i < 150; i++)
            {
                fly = SN.FindWindow(FlyoutClass, null);
                if (fly != IntPtr.Zero && SN.IsWindowVisible(fly)) { Ghost(fly, true); return fly; }
                Thread.Sleep(4);
            }
            return IntPtr.Zero;
        }

        // close it (the chevron toggles it) and make it normal again for Windows' own use
        void CloseFlyout(IntPtr fly)
        {
            try
            {
                AutomationElement b = Chevron(); object pat;
                if (SN.IsWindowVisible(fly) && b != null && b.TryGetCurrentPattern(InvokePattern.Pattern, out pat)) ((InvokePattern)pat).Invoke();
            }
            catch { }
            RestoreWhenHidden(fly, 3000);
        }
        static void RestoreWhenHidden(IntPtr fly, int ms)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int t = 0; t < ms && SN.IsWindow(fly) && SN.IsWindowVisible(fly); t += 100) Thread.Sleep(100);
                if (SN.IsWindow(fly) && !SN.IsWindowVisible(fly)) Ghost(fly, false);
                else if (SN.IsWindow(fly)) RestoreWhenHidden(fly, 120000);   // still open (an icon's menu is up): wait for it
            });
        }

        static List<AutomationElement> FlyoutItems(IntPtr fly)
        {
            List<AutomationElement> r = new List<AutomationElement>();
            AutomationElement root = AutomationElement.FromHandle(fly);
            for (int t = 0; t < 25 && r.Count == 0; t++)
            {
                foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)))
                    if (!string.IsNullOrEmpty(e.Current.Name)) r.Add(e);
                if (r.Count == 0) Thread.Sleep(40);
            }
            return r;
        }

        // which program owns a tray icon: Windows keeps each icon's program and first tooltip in NotifyIconSettings
        static string KnownFolder(string p)
        {
            if (p == null || !p.StartsWith("{")) return p;
            int e = p.IndexOf('}'); if (e < 0) return p;
            try
            {
                IntPtr s; if (SHGetKnownFolderPath(new Guid(p.Substring(0, e + 1)), 0, IntPtr.Zero, out s) != 0) return p;
                string root = Marshal.PtrToStringUni(s); Marshal.FreeCoTaskMem(s);
                return root + p.Substring(e + 1);
            }
            catch { return p; }
        }
        static readonly Dictionary<string, string> exeDesc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string IconOwner(string name)
        {
            string first = name.Split('\n')[0].Trim(), best = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
                {
                    if (k == null) return null;
                    foreach (string sub in k.GetSubKeyNames())
                        using (RegistryKey s = k.OpenSubKey(sub))
                        {
                            string exe = KnownFolder(s.GetValue("ExecutablePath") as string), tip = (s.GetValue("InitialTooltip") as string ?? "").Split('\n')[0].Trim();
                            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
                            if (tip.Length > 0 && (first.StartsWith(tip, StringComparison.OrdinalIgnoreCase) || tip.StartsWith(first, StringComparison.OrdinalIgnoreCase))) return exe;
                            string d;
                            if (!exeDesc.TryGetValue(exe, out d))
                            {
                                d = ""; try { FileVersionInfo v = FileVersionInfo.GetVersionInfo(exe); d = (v.FileDescription ?? "") + "|" + (v.ProductName ?? ""); } catch { }
                                exeDesc[exe] = d;
                            }
                            string fn = Path.GetFileNameWithoutExtension(exe);
                            if (best == null && (first.IndexOf(fn, StringComparison.OrdinalIgnoreCase) >= 0
                                || Array.Exists(d.Split('|'), x => x.Length > 2 && first.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0))) best = exe;
                        }
                }
            }
            catch { }
            return best;
        }

        public object Tray(Dictionary<string, string> q)
        {
            string op; q.TryGetValue("op", out op);
            lock (trayLock)
            {
                IntPtr fly = IntPtr.Zero;
                try
                {
                    fly = OpenFlyoutHidden();
                    if (fly == IntPtr.Zero) return null;
                    List<AutomationElement> items = FlyoutItems(fly);
                    if (op == "click")
                    {
                        string v, name; int i = -1; q.TryGetValue("i", out v); int.TryParse(v, out i); q.TryGetValue("n", out name); q.TryGetValue("b", out v);
                        bool right = v == "right";
                        AutomationElement it = i >= 0 && i < items.Count && items[i].Current.Name == name ? items[i] : items.Find(x => x.Current.Name == name);
                        if (it == null) { CloseFlyout(fly); return false; }
                        SN.POINT p; SN.GetCursorPos(out p);
                        for (int tries = 0; tries < 3; tries++)
                        {
                            System.Windows.Rect br = it.Current.BoundingRectangle; SN.RECT wr; SN.GetWindowRect(fly, out wr);
                            int dx = p.X - (int)(br.Left + br.Width / 2), dy = p.Y - (int)(br.Top + br.Height / 2);
                            if (Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2) break;
                            SN.SetWindowPos(fly, new IntPtr(-1), wr.Left + dx, wr.Top + dy, 0, 0, 0x0001 | 0x0010);   // NOSIZE | NOACTIVATE
                            Thread.Sleep(40);
                        }
                        mouse_event(right ? 0x0008u : 0x0002u, 0, 0, 0, UIntPtr.Zero); Thread.Sleep(25);
                        mouse_event(right ? 0x0010u : 0x0004u, 0, 0, 0, UIntPtr.Zero);
                        // the (invisible) flyout must not keep catching clicks at that spot: move it off screen; it closes on its own
                        // when it loses focus, and the icon's menu (its own window) stays where it opened
                        IntPtr f = fly;
                        ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(450); SN.SetWindowPos(f, IntPtr.Zero, -32000, -32000, 0, 0, 0x0001 | 0x0004 | 0x0010); });
                        RestoreWhenHidden(fly, 120000);
                        return true;
                    }
                    List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
                    for (int i = 0; i < items.Count; i++)
                    {
                        string n = items[i].Current.Name;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["i"] = i; d["name"] = n; d["label"] = n.Split('\n')[0].Trim(); d["icon"] = IconOwner(n) ?? "";
                        list.Add(d);
                    }
                    CloseFlyout(fly);
                    return list;
                }
                catch (Exception ex) { Program.Log("shell: tray: " + ex.Message); if (fly != IntPtr.Zero) CloseFlyout(fly); return null; }
            }
        }

        // ---------- Windows taskbar: autohide (so it stops reserving space) and hidden; restored on stop
        void HideTaskbar()
        {
            if (tray == IntPtr.Zero || !SN.IsWindow(tray))
            {
                tray = SN.FindWindow("Shell_TrayWnd", null); trayHidden = false;
                if (tray != IntPtr.Zero && hookThreadId != 0) PostThreadMessage(hookThreadId, WM_TRAYCHANGED, IntPtr.Zero, IntPtr.Zero);
            }
            if (tray == IntPtr.Zero) return;
            if (!trayHidden)
            {
                SN.APPBARDATA d = new SN.APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = tray;
                if (trayState < 0) trayState = SN.SHAppBarMessage(4 /*ABM_GETSTATE*/, ref d).ToInt32();
                d.lParam = new IntPtr(1 /*ABS_AUTOHIDE*/); SN.SHAppBarMessage(10 /*ABM_SETSTATE*/, ref d);
                trayHidden = true;
            }
            if (SN.IsWindowVisible(tray)) SN.ShowWindow(tray, 0);
        }

        public void RestoreTaskbar()
        {
            IntPtr t = SN.FindWindow("Shell_TrayWnd", null);
            if (t == IntPtr.Zero) return;
            if (trayState >= 0)
            {
                SN.APPBARDATA d = new SN.APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = t; d.lParam = new IntPtr(trayState);
                SN.SHAppBarMessage(10 /*ABM_SETSTATE*/, ref d); trayState = -1;
            }
            trayHidden = false;   // first: the hook thread must not hide it again as it shows
            SN.ShowWindow(t, 5 /*SW_SHOW*/);
        }

        // ---------- Win key opens our Start menu: a lone Win press is cancelled with a dummy key and turned into an event
        IntPtr KeyHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Active && web.Up)   // (hook thread: Up is a plain flag, the page itself is only touched on the UI thread)
            {
                int vk = Marshal.ReadInt32(lParam), flags = Marshal.ReadInt32(lParam, 8), msg = wParam.ToInt32();
                bool injected = (flags & 0x10) != 0, down = msg == 0x100 || msg == 0x104, up = msg == 0x101 || msg == 0x105;
                if (!injected)
                {
                    if (vk == 0x5B || vk == 0x5C)
                    {
                        if (down && !winDown) { winDown = true; winCombo = false; }
                        else if (up && winDown)
                        {
                            winDown = false;
                            if (!winCombo)
                            {
                                SN.keybd_event(0xE8, 0, 0, UIntPtr.Zero); SN.keybd_event(0xE8, 0, 2, UIntPtr.Zero);   // "used in a combo": Windows' Start stays closed
                                ui.BeginInvoke(new MethodInvoker(delegate { srv.Push("start"); }));   // the page opens the menu, which takes focus
                            }
                        }
                    }
                    else if (winDown && (vk == 0x41 || vk == 0x4E))   // Win+A / Win+N: our quick settings / notification centre instead of Windows' corner panels
                    {
                        winCombo = true;
                        if (down)
                        {
                            SN.keybd_event(0xE8, 0, 0, UIntPtr.Zero); SN.keybd_event(0xE8, 0, 2, UIntPtr.Zero);
                            string ev = vk == 0x41 ? "quick" : "notify";
                            ui.BeginInvoke(new MethodInvoker(delegate { srv.Push(ev); }));
                        }
                        return new IntPtr(1);
                    }
                    else if (down && winDown) winCombo = true;
                    else if (down && vk == 0x1B && menuOpen) { escAt = DateTime.Now; ui.BeginInvoke(new MethodInvoker(delegate { srv.Push("escape"); })); }
                }
            }
            return CallNextHookEx(kbHook, nCode, wParam, lParam);
        }

        // ---------- what the page asks for
        // ---------- files dropped on the bar. The page only learns names and sizes (browsers hide paths), so the real files are
        // looked up where a drag starts: the selection of each open Explorer window, then the desktop folders.
        static object Com(object o, string member, params object[] args)
        {
            return o.GetType().InvokeMember(member, args.Length == 0 && member != "Item" ? System.Reflection.BindingFlags.GetProperty | System.Reflection.BindingFlags.InvokeMethod
                : System.Reflection.BindingFlags.InvokeMethod, null, o, args);
        }
        List<string> DroppedPaths(string json)
        {
            List<string> found = new List<string>();
            List<KeyValuePair<string, long>> want = new List<KeyValuePair<string, long>>();
            try
            {
                foreach (object o in (object[])new JavaScriptSerializer().DeserializeObject(json ?? "[]"))
                {
                    Dictionary<string, object> d = o as Dictionary<string, object>; if (d == null) continue;
                    string n = Convert.ToString(d["n"]); long s = Convert.ToInt64(d["s"]);
                    if (!string.IsNullOrEmpty(n) && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) want.Add(new KeyValuePair<string, long>(n, s));
                }
            }
            catch { return found; }
            Func<string, bool> take = delegate (string p)
            {
                try
                {
                    if (string.IsNullOrEmpty(p) || found.Contains(p, StringComparer.OrdinalIgnoreCase) || !File.Exists(p)) return false;
                    string n = Path.GetFileName(p); long len = new FileInfo(p).Length;
                    foreach (KeyValuePair<string, long> w in want) if (string.Equals(w.Key, n, StringComparison.OrdinalIgnoreCase) && w.Value == len) { found.Add(p); return true; }
                }
                catch { }
                return false;
            };
            try   // Explorer windows: their selected items
            {
                object shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
                object wins = Com(shell, "Windows"); int count = (int)Com(wins, "Count");
                for (int i = 0; i < count && found.Count < want.Count; i++)
                {
                    try
                    {
                        object w = Com(wins, "Item", i); if (w == null) continue;
                        object sel = Com(Com(w, "Document"), "SelectedItems"); int n = (int)Com(sel, "Count");
                        for (int j = 0; j < n; j++) take((string)Com(Com(sel, "Item", j), "Path"));
                    }
                    catch { }
                }
            }
            catch { }
            // the desktop (its icons are not an Explorer window) and the usual places files are dragged from
            foreach (string dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                                           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads") })
                foreach (KeyValuePair<string, long> w in want) if (!string.IsNullOrEmpty(dir)) take(Path.Combine(dir, w.Key));
            return found;
        }
        object OpenWith(string app, List<string> paths)
        {
            string exe = app ?? "";
            if (exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) exe = srv.Data.LinkTarget(exe);
            StringBuilder args = new StringBuilder();
            foreach (string p in paths) args.Append(args.Length > 0 ? " " : "").Append('"').Append(p.EndsWith("\\") ? p + "\\" : p).Append('"');   // "C:\" must not escape its closing quote
            try
            {
                if (exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe))
                    Process.Start(new ProcessStartInfo(exe, args.ToString()) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(paths[0]) });
                else foreach (string p in paths) Process.Start(new ProcessStartInfo(p) { UseShellExecute = true });   // Store apps: the files open with their default app
                return true;
            }
            catch (Exception ex) { return ex.Message; }
        }

        public object Action(string a, Dictionary<string, string> q)
        {
            string v;
            switch (a)
            {
                case "metrics":   // the page reports the height it needs at the bottom (its viewport is our frame exactly)
                {
                    int res; q.TryGetValue("reserve", out v); int.TryParse(v, out res);
                    if (res > 0 && res != reserve)
                    {
                        reserve = res;
                        if (!AutoHide) { if (appbar == null) appbar = new AppBarWindow(); appbar.Reserve(mon, reserve); }
                        HideTaskbar();
                    }
                    return 0;
                }
                case "region":    // "x,y,w,h,radius;..." in screen pixels
                {
                    barRects.Clear();
                    if (q.TryGetValue("r", out v))
                        foreach (string part in v.Split(';'))
                        {
                            string[] n = part.Split(','); if (n.Length < 4) continue;
                            int[] r = new int[5]; for (int i = 0; i < 5; i++) r[i] = i < n.Length ? (int)Math.Round(double.Parse(n[i], System.Globalization.CultureInfo.InvariantCulture)) : 0;
                            barRects.Add(r);
                        }
                    if (!menuOpen) ApplyRegion();
                    return barRects.Count;
                }
                case "menu":
                {
                    bool open = q.TryGetValue("open", out v) && v == "1";
                    if (frame == null) return false;
                    if (open && !menuOpen)
                    {
                        IntPtr fg = SN.GetForegroundWindow(); if (fg != frame.Handle) prevFg = fg;
                        if (fsHidden) { fsHidden = false; SN.ShowWindow(frame.Handle, 8 /*SW_SHOWNA*/); }   // Win key over a fullscreen app
                        srv.Backdrop = Capture(mon);
                        menuOpen = true; menuAt = DateTime.Now; srv.LastPoll = DateTime.Now; ApplyRegion();
                        SN.ForceForeground(frame.Handle); web.Focus();   // keyboard into the page: the search box types
                    }
                    else if (!open && menuOpen)
                    {
                        menuOpen = false; ApplyRegion();
                        if (prevFg != IntPtr.Zero && SN.IsWindow(prevFg)) SN.ForceForeground(prevFg);
                    }
                    return menuOpen;
                }
                case "activate":   // taskbar click: restore, focus, or minimise when it is already in front
                {
                    long h; q.TryGetValue("h", out v); if (!long.TryParse(v, out h)) return false;
                    IntPtr w = new IntPtr(h); if (!SN.IsWindow(w)) return false;
                    IntPtr front = menuOpen ? prevFg : SN.GetForegroundWindow();   // the bar never takes focus, so this is the real front window
                    bool force = q.TryGetValue("force", out v) && v == "1";            // from the menu: always bring it forward
                    if (!force && !menuOpen && w == front && !SN.IsIconic(w)) { SN.ShowWindow(w, 6 /*SW_MINIMIZE*/); return "min"; }
                    if (menuOpen) LeaveMenu();   // like Windows: the Start menu gets out of the way at once, its closing animation plays on the bar only
                    SN.ForceForeground(w); prevFg = w; return "front";
                }
                case "bgstate": return srv.BgPaused;
                case "cover": return coverState > 0;   // the page asks once at start; changes arrive as events
                case "bar": { if (q.TryGetValue("shown", out v) && v == "0") revealSent = false; return true; }   // the page hid its bar again
                case "bgmetrics": { int vw, vh; q.TryGetValue("vw", out v); int.TryParse(v, out vw); q.TryGetValue("vh", out v); int.TryParse(v, out vh); if (OnBgMetrics != null) OnBgMetrics(vw, vh); return true; }
                case "push": { if (q.TryGetValue("e", out v)) srv.Push(v); return true; }   // e.g. "start" to toggle the menu from outside
                case "notif":   // op=dismiss|clear|open, id=<notification id>
                {
                    string op, id; q.TryGetValue("op", out op); q.TryGetValue("id", out id);
                    if (op == "clear") srv.Notifs.Clear();
                    else if (op == "dismiss") srv.Notifs.Dismiss(id);
                    else if (op == "open")
                    {
                        string aumid = srv.Notifs.Aumid(id); srv.Notifs.Dismiss(id);
                        if (!string.IsNullOrEmpty(aumid)) try { Process.Start(new ProcessStartInfo("explorer.exe", "shell:AppsFolder\\" + aumid) { UseShellExecute = true }); } catch { }
                    }
                    return true;
                }
                case "close": { long h; q.TryGetValue("h", out v); if (long.TryParse(v, out h)) SN.PostMessage(new IntPtr(h), 0x10, IntPtr.Zero, IntPtr.Zero); return true; }
                case "dropfiles":   // files dropped on a bar app: open them with that app
                {
                    string app, f; q.TryGetValue("app", out app); q.TryGetValue("f", out f);
                    List<string> paths = DroppedPaths(f);
                    if (paths.Count == 0) { Program.Log("shell: dropped files not found in Explorer / desktop"); return false; }
                    if (menuOpen) LeaveMenu();
                    return OpenWith(app, paths);
                }
                case "launch":
                {
                    if (!q.TryGetValue("p", out v) || string.IsNullOrEmpty(v)) return false;
                    if (menuOpen) LeaveMenu();
                    string adm; q.TryGetValue("admin", out adm);
                    try
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(v) { UseShellExecute = true };
                        if (adm == "1") psi.Verb = "runas";   // "Run as administrator": Windows asks UAC as usual
                        Process.Start(psi); return true;
                    }
                    catch (System.ComponentModel.Win32Exception ex) { return ex.NativeErrorCode == 1223 ? "cancelled" : ex.Message; }   // 1223: UAC declined
                    catch (Exception ex) { return ex.Message; }
                }
                case "reveal":   // "Open file location": the file selected in its folder
                {
                    if (!q.TryGetValue("p", out v) || string.IsNullOrEmpty(v) || v.IndexOf('"') >= 0) return false;
                    if (menuOpen) LeaveMenu();
                    if (!File.Exists(v) && !Directory.Exists(v)) return false;
                    try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + v + "\"") { UseShellExecute = true }); return true; } catch (Exception ex) { return ex.Message; }
                }
                case "volume":   // set=<0..100>: the slider's exact level; d=<+-n>: a step (mouse wheel); d=mute: toggle mute
                {
                    float d;
                    if (q.TryGetValue("set", out v) && float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d))
                    {
                        ShellData.SetVolumeLevel(Math.Max(0, Math.Min(100, d)) / 100f); return true;
                    }
                    q.TryGetValue("d", out v);
                    if (v == "mute") ShellData.SetVolume(0, true); else if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d)) ShellData.SetVolume(d / 100f, false);
                    return true;
                }
                case "audioout":   // make another output device the default (quick settings volume card)
                {
                    if (!q.TryGetValue("id", out v) || string.IsNullOrEmpty(v)) return false;
                    bool ok = ShellData.SetAudioOutput(v); srv.Data.ForgetQuick(); return ok;
                }
                case "sys":
                {
                    q.TryGetValue("n", out v);
                    switch (v)
                    {
                        case "lock": SN.LockWorkStation(); return true;
                        case "quick": SN.Keys(0x5B, 0x41); return true;              // Win+A quick settings (Wi-Fi, volume, battery)
                        case "notify": SN.Keys(0x5B, 0x4E); return true;             // Win+N notifications + calendar
                        case "desktop": SN.Keys(0x5B, 0x44); return true;            // Win+D
                        case "tray":                                                  // "show hidden icons" above our chevron (x = centre, y = top of the bar, screen px)
                        {
                            int ax, ay; q.TryGetValue("x", out v); int.TryParse(v, out ax); q.TryGetValue("y", out v); int.TryParse(v, out ay);
                            ax += mon.X; ay += mon.Y;
                            ThreadPool.QueueUserWorkItem(delegate { TrayOverflow(ax, ay); });
                            return true;
                        }
                        case "play": SN.Keys(0xB3); return true;                      // media keys: play/pause, next, previous
                        case "next": SN.Keys(0xB0); return true;
                        case "prev": SN.Keys(0xB1); return true;
                        case "settings": Process.Start("ms-settings:"); return true;
                        case "explorer": Process.Start("explorer.exe"); return true;
                        case "taskmgr": Process.Start("taskmgr.exe"); return true;
                        case "themer": Process.Start("wscript.exe", "\"" + Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath)), "TRANSCRYPTO.vbs") + "\""); return true;
                        case "winstart": RestoreTaskbar(); SN.Keys(0x5B); return true;
                        case "exit":
                            if (armedAction != v || (DateTime.Now - armed).TotalSeconds > 4) { armedAction = v; armed = DateTime.Now; return "armed"; }
                            armedAction = ""; if (OnExit != null) ui.BeginInvoke(OnExit); return "done";
                        case "sleep": case "restart": case "shutdown": case "signout":
                            // power actions need a second click within 4 s (the page shows the "click again" state)
                            if (armedAction != v || (DateTime.Now - armed).TotalSeconds > 4) { armedAction = v; armed = DateTime.Now; return "armed"; }
                            armedAction = "";
                            if (v == "sleep") SN.SetSuspendState(false, false, false);
                            else Process.Start(new ProcessStartInfo("shutdown.exe", v == "restart" ? "/r /t 0" : v == "shutdown" ? "/s /t 0" : "/l") { UseShellExecute = false, CreateNoWindow = true });
                            return "done";
                    }
                    return false;
                }
            }
            return null;
        }

        // ---------- messages from the page. "drop": files dropped on a bar app. WebView2 hands over the real files (their paths) with
        // the message; the names + sizes in it are the fallback (looked up in Explorer's selection and the desktop, like in a browser).
        void PageMessage(Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> m = new JavaScriptSerializer().DeserializeObject(e.WebMessageAsJson) as Dictionary<string, object>;
            if (m == null || !m.ContainsKey("type") || Convert.ToString(m["type"]) != "drop") return;
            string app = m.ContainsKey("app") ? Convert.ToString(m["app"]) : "";
            List<string> paths = new List<string>();
            try
            {
                IReadOnlyList<object> objs = e.AdditionalObjects;
                for (int i = 0; objs != null && i < objs.Count && paths.Count < 20; i++)
                {
                    Microsoft.Web.WebView2.Core.CoreWebView2File f = objs[i] as Microsoft.Web.WebView2.Core.CoreWebView2File;
                    if (f != null && !string.IsNullOrEmpty(f.Path) && (File.Exists(f.Path) || Directory.Exists(f.Path))) paths.Add(f.Path);   // a folder too (onto an editor)
                }
            }
            catch (Exception ex) { Program.Log("shell: dropped files: " + ex.Message); }
            bool direct = paths.Count > 0;
            if (!direct && m.ContainsKey("f")) paths = DroppedPaths(new JavaScriptSerializer().Serialize(m["f"]));
            if (paths.Count == 0) { Program.Log("shell: dropped files could not be found"); return; }
            if (menuOpen) LeaveMenu();
            object r = OpenWith(app, paths);
            Program.Log("shell: " + paths.Count + " file(s) dropped on " + (string.IsNullOrEmpty(app) ? "an app" : Path.GetFileName(app)) +
                (direct ? " (paths from the drop)" : " (found by name)") + (r is bool && (bool)r ? "" : " - " + r));
        }

        static byte[] Capture(Rectangle r)
        {
            try
            {
                using (Bitmap b = new Bitmap(r.Width, r.Height))
                {
                    using (Graphics g = Graphics.FromImage(b)) g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size);
                    return ShellServer.Jpeg(b, 82);
                }
            }
            catch { return null; }
        }

        // ---------- AppBar: reserves the bottom strip so maximised windows stop above our bar
        class AppBarWindow : NativeWindow
        {
            bool registered;
            public AppBarWindow() { CreateHandle(new CreateParams { Caption = "TRANSCRYPTO AppBar", ExStyle = 0x80 }); }
            public void Reserve(Rectangle mon, int height)
            {
                SN.APPBARDATA d = new SN.APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = Handle;
                if (!registered) { d.uCallbackMessage = 0x8000 + 77; SN.SHAppBarMessage(0 /*ABM_NEW*/, ref d); registered = true; }
                d.uEdge = 3 /*ABE_BOTTOM*/; d.rc.Left = mon.Left; d.rc.Right = mon.Right; d.rc.Top = mon.Bottom - height; d.rc.Bottom = mon.Bottom;
                SN.SHAppBarMessage(2 /*ABM_QUERYPOS*/, ref d);
                d.rc.Top = d.rc.Bottom - height;
                SN.SHAppBarMessage(3 /*ABM_SETPOS*/, ref d);
            }
            public void Remove()
            {
                if (registered) { SN.APPBARDATA d = new SN.APPBARDATA(); d.cbSize = Marshal.SizeOf(d); d.hWnd = Handle; SN.SHAppBarMessage(1 /*ABM_REMOVE*/, ref d); registered = false; }
                DestroyHandle();
            }
        }
    }

    // ------------------------------------------------------------------ live web background behind the desktop icons
    // A window of ours inside the desktop's wallpaper layer (the WorkerW behind the icons, where video wallpapers go too), with the
    // scene page in WebView2. It is never on top of anything, never takes focus and has no taskbar button: the desktop icons and
    // every app stay above it. Explorer restarting replaces that layer; the guard notices and builds the window again.
    public class WebBackground
    {
        [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr h);
        readonly ShellServer srv; readonly System.Windows.Forms.Timer guard; readonly Func<IntPtr> host; readonly WebSurface web;
        BgForm form; IntPtr parent = IntPtr.Zero; string url; DateTime nextTry = DateTime.MinValue;
        public string Design = "none", Palette = ""; Rectangle mon;

        class BgForm : Form
        {
            public BgForm()
            {
                FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
                BackColor = Color.Black; Text = "TRANSCRYPTO Background";
            }
            protected override CreateParams CreateParams { get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/; return cp; } }
            protected override bool ShowWithoutActivation { get { return true; } }
            bool closable;
            public void AllowClose() { closable = true; }
            protected override void OnFormClosing(FormClosingEventArgs e) { if (!closable && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; } base.OnFormClosing(e); }
        }

        public WebBackground(ShellServer server, Func<IntPtr> wallpaperHost)
        {
            srv = server; host = wallpaperHost;
            web = new WebSurface("live background", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TRANSCRYPTO\bg-webview2"), false);
            guard = new System.Windows.Forms.Timer(); guard.Interval = 1500; guard.Tick += delegate { Guard(); };
        }

        public void Apply(string design, string palette)
        {
            if (design == Design && palette == Palette && (design == "none" || form != null)) return;
            // another scene or palette: the same window, only the page changes
            if (design != "none" && Design != "none" && form != null && !form.IsDisposed && form.IsHandleCreated && SN.IsWindow(parent))
            {
                Design = design; Palette = palette;
                url = string.Format("http://127.0.0.1:{0}/shell/bg-{1}.html?t={2}&pal={3}", srv.Port, design, srv.Token, Uri.EscapeDataString(palette ?? ""));
                web.Navigate(url);
                return;
            }
            Stop(); Design = design; Palette = palette;
            if (design == "none") return;
            srv.Start(); mon = Screen.PrimaryScreen.Bounds;
            url = string.Format("http://127.0.0.1:{0}/shell/bg-{1}.html?t={2}&pal={3}", srv.Port, design, srv.Token, Uri.EscapeDataString(palette ?? ""));
            Build();
            guard.Start();
        }

        // our window as a child of the wallpaper layer, over the primary screen, with the page inside
        void Build()
        {
            Teardown();
            IntPtr h = host();
            if (h == IntPtr.Zero) { nextTry = DateTime.Now.AddSeconds(10); Program.Log("live background: no desktop wallpaper layer found, trying again"); return; }
            form = new BgForm();
            IntPtr fh = form.Handle;
            SN.SetParent(fh, h);
            long st = SN.GetLong(fh, -16).ToInt64();
            SN.SetLong(fh, -16, new IntPtr((st | 0x40000000L /*CHILD*/) & ~0x80000000L /*POPUP*/));
            parent = h;
            Position();
            form.Show();
            srv.Data.BgHwnd = fh;
            web.Show(form, url);
        }

        void Position()
        {
            Native.RECT rc = new Native.RECT(); rc.Left = mon.Left; rc.Top = mon.Top; rc.Right = mon.Right; rc.Bottom = mon.Bottom;
            Native.MapWindowPoints(IntPtr.Zero, parent, ref rc, 2);   // the layer spans every screen: the primary one sits at an offset in it
            SN.SetWindowPos(form.Handle, IntPtr.Zero, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, 0x0010 /*NOACTIVATE*/ | 0x0040 /*SHOW*/ | 0x0004 /*NOZORDER*/);
        }

        void Teardown()
        {
            web.Close();
            if (form != null) { try { form.AllowClose(); form.Close(); form.Dispose(); } catch { } form = null; }
            parent = IntPtr.Zero; srv.Data.BgHwnd = IntPtr.Zero;
        }

        public void Stop() { guard.Stop(); Teardown(); Design = "none"; srv.BgPaused = false; }

        // nobody sees the desktop behind a maximised or fullscreen window: tell the page to stop animating ("bg:pause" / "bg:play")
        void PauseCheck()
        {
            IntPtr fg = SN.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == srv.Data.FrameHwnd || fg == srv.Data.ShellHwnd) return;   // our Start menu / panels: keep the last state
            bool hidden = !Engine.IsShellWindow(fg) && SN.IsWindowVisible(fg) && !SN.IsIconic(fg)
                && Screen.FromHandle(fg).Primary && (SN.IsZoomed(fg) || Engine.IsFullscreenApp(fg));
            if (hidden != srv.BgPaused) { srv.BgPaused = hidden; srv.Push(hidden ? "bg:pause" : "bg:play"); }
        }

        int memTick;
        void Guard()
        {
            PauseCheck();
            // Explorer restarted (a new wallpaper layer) or our window went down with the old one: build it again
            if (form == null || form.IsDisposed || !form.IsHandleCreated || !SN.IsWindow(parent) || GetParent(form.Handle) != parent)
            {
                if (DateTime.Now < nextTry) return;
                if (form != null) Program.Log("live background: the desktop's wallpaper layer changed, placed again");
                Build(); return;
            }
            Rectangle now = Screen.PrimaryScreen.Bounds;
            if (now != mon) { mon = now; Position(); }
            if (++memTick % 20 == 0 && web.Ready)
            {
                long mb = web.MemoryMB();
                if (mb > 1200) web.Recreate("WebView2 at " + mb + " MB");
            }
        }

        // the page reports its viewport; it fills our window exactly, so there is nothing to adjust any more
        public void Metrics(int vw, int vh) { }
    }

    // ------------------------------------------------------------------ the Chrome windows of earlier versions
    // Before 2.4 the bar and the live scene were Chrome --app windows with their own profiles. A scene window lived inside the
    // desktop's wallpaper layer, so it could outlive an engine that was killed. Close any that are left and delete their profiles.
    public static class LegacyChrome
    {
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, SN.EnumProc cb, IntPtr l);
        public static void Cleanup(IntPtr wallpaperHost)
        {
            int closed = 0;
            SN.EnumProc close = delegate (IntPtr h, IntPtr l)
            {
                if (SN.ClassOf(h) == "Chrome_WidgetWin_1")
                {
                    string t = SN.TitleOf(h);
                    if (t.StartsWith("TRANSCRYPTO Shell") || t.StartsWith("TRANSCRYPTO Background")) { SN.PostMessage(h, 0x0010 /*WM_CLOSE*/, IntPtr.Zero, IntPtr.Zero); closed++; }
                }
                return true;
            };
            SN.EnumWindows(close, IntPtr.Zero);
            if (wallpaperHost != IntPtr.Zero) EnumChildWindows(wallpaperHost, close, IntPtr.Zero);
            if (closed > 0) Program.Log("closed " + closed + " Chrome window(s) of an earlier version");
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRANSCRYPTO");
            List<string> dirs = new List<string> { Path.Combine(data, "shell-profile"), Path.Combine(data, "bg-profile") };
            // the Themer window's Chrome profile too, once the native Themer window (bin\TRANSCRYPTO.exe) is installed
            if (File.Exists(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "TRANSCRYPTO.exe"))) dirs.Add(Path.Combine(data, "ui-profile"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (string d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    string lk = Path.Combine(d, "lockfile");
                    for (int i = 0; i < 30; i++)   // a closing Chrome holds its profile's lockfile for a few seconds
                    {
                        try { if (File.Exists(lk)) using (new FileStream(lk, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } break; }
                        catch { Thread.Sleep(500); }
                    }
                    try { Directory.Delete(d, true); Program.Log("removed the old Chrome profile " + Path.GetFileName(d)); }
                    catch (Exception ex) { Program.Log("old Chrome profile " + Path.GetFileName(d) + " kept for now: " + ex.Message); }
                }
            });
        }
    }
}
