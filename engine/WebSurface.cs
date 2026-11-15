// TRANSCRYPTO engine - a shell page shown by Microsoft WebView2 inside one of the engine's own windows: the taskbar +
// Start menu frame (ShellHost) and the live desktop background (WebBackground). WebView2 is part of Windows 11, so there
// is no Chrome process and no foreign window to find, reparent and keep in place any more.
//   - one WebView2 environment (= one browser process tree) per data folder: shell-webview2, bg-webview2
//   - the page can only ever show our own 127.0.0.1 origin; downloads, new windows and permissions are refused
//   - a crashed or hung page is recreated (rate-limited), and the memory of its process tree can be measured
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Transcrypto
{
    public class WebSurface
    {
        public readonly string Name;                 // for the log: "shell" / "live background"
        readonly string dataDir; readonly bool drops;
        Control host; WebView2 view; string url, origin;
        int gen; bool loaded; int navFails;
        readonly List<DateTime> restarts = new List<DateTime>();
        public Action<CoreWebView2WebMessageReceivedEventArgs> OnMessage;   // messages from the page (files dropped on the bar)
        public Action OnLoaded;                                             // the page finished loading (also after a restart)

        // flags for an always-on page that sits behind other windows: no throttling, no occlusion pause, sound without a click
        const string Args = "--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding " +
                            "--disable-backgrounding-occluded-windows --disable-features=CalculateNativeWinOcclusion,msSmartScreenProtection,Translate " +
                            "--overscroll-history-navigation=0 --no-first-run";

        public WebSurface(string name, string dataDir, bool allowFileDrops) { Name = name; this.dataDir = dataDir; drops = allowFileDrops; }

        public bool Ready { get { return view != null && !view.IsDisposed && view.CoreWebView2 != null && loaded; } }
        public volatile bool Up;   // the page is loaded: readable from any thread (the keyboard hook), unlike the control itself
        public bool Exists { get { return view != null && !view.IsDisposed; } }

        // ---------- one environment per data folder, shared by every page of that folder and kept across page restarts
        static readonly Dictionary<string, Task<CoreWebView2Environment>> envs = new Dictionary<string, Task<CoreWebView2Environment>>(StringComparer.OrdinalIgnoreCase);
        static Task<CoreWebView2Environment> Env(string dir)
        {
            Task<CoreWebView2Environment> t;
            if (!envs.TryGetValue(dir, out t) || t.IsFaulted || t.IsCanceled)
            {
                Directory.CreateDirectory(dir);
                envs[dir] = t = CoreWebView2Environment.CreateAsync(null, dir, new CoreWebView2EnvironmentOptions(Args));
            }
            return t;
        }
        static void ForgetEnv(string dir) { envs.Remove(dir); }

        // show url in host (the page fills it); calling it again replaces the page
        public void Show(Control hostControl, string pageUrl)
        {
            host = hostControl; url = pageUrl;
            Uri u = new Uri(url); origin = u.Scheme + "://" + u.Authority;
            restarts.Clear(); navFails = 0;
            Create();
        }

        // another page of the same server in the same window (a different design / palette): the window and WebView2 stay
        public void Navigate(string pageUrl)
        {
            if (host == null || host.IsDisposed || view == null || view.IsDisposed || view.CoreWebView2 == null) { if (host != null) Show(host, pageUrl); return; }
            Uri u = new Uri(pageUrl); string o = u.Scheme + "://" + u.Authority;
            if (o != origin) { Show(host, pageUrl); return; }
            url = pageUrl; navFails = 0;
            try { view.CoreWebView2.Navigate(url); } catch { Show(host, pageUrl); }
        }

        public void Recreate(string why)
        {
            if (host == null || host.IsDisposed) return;
            DateTime now = DateTime.Now; restarts.RemoveAll(d => (now - d).TotalMinutes > 2); restarts.Add(now);
            if (restarts.Count > 5) { Program.Log(Name + ": " + why + " - too many restarts, waiting a minute"); Later(60000, gen, Create); return; }
            Program.Log(Name + ": " + why + ", page restarted");
            Create();
        }

        public void Close() { gen++; DisposeView(); host = null; }

        async void Create()
        {
            if (host == null || host.IsDisposed) return;
            int my = ++gen; loaded = false;
            DisposeView();
            WebView2 v = new WebView2();
            v.Dock = DockStyle.Fill; v.DefaultBackgroundColor = Color.Black;
            try { v.AllowExternalDrop = drops; } catch { }
            host.Controls.Add(v); view = v;
            IntPtr force = v.Handle;   // the control needs its window before WebView2 can attach to it
            try
            {
                CoreWebView2Environment env = await Env(dataDir);
                if (my != gen) return;
                await v.EnsureCoreWebView2Async(env);
                if (my != gen) return;
                Configure(v.CoreWebView2, my);
                v.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                if (my != gen) return;
                Program.Log(Name + ": WebView2 did not start (" + ex.GetType().Name + ": " + ex.Message + ")");
                ForgetEnv(dataDir);
                Later(5000, my, delegate { Recreate("WebView2 start failed"); });
            }
        }

        void Configure(CoreWebView2 core, int my)
        {
            CoreWebView2Settings s = core.Settings;
            Try(delegate { s.AreDevToolsEnabled = false; }); Try(delegate { s.AreDefaultContextMenusEnabled = false; });
            Try(delegate { s.IsStatusBarEnabled = false; }); Try(delegate { s.AreBrowserAcceleratorKeysEnabled = false; });   // no F5 / Ctrl+R / Ctrl+P / Ctrl+F on the bar
            Try(delegate { s.IsZoomControlEnabled = false; }); Try(delegate { s.IsPinchZoomEnabled = false; }); Try(delegate { s.IsSwipeNavigationEnabled = false; });
            Try(delegate { s.IsGeneralAutofillEnabled = false; }); Try(delegate { s.IsPasswordAutosaveEnabled = false; });
            Try(delegate { s.AreHostObjectsAllowed = false; }); Try(delegate { s.IsBuiltInErrorPageEnabled = false; });
            Try(delegate { s.IsWebMessageEnabled = OnMessage != null; });
            // only our own page: a dropped file, a link or a script can never take the bar somewhere else
            core.NavigationStarting += delegate (object o, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!Same(e.Uri)) { e.Cancel = true; Program.Log(Name + ": blocked navigation to " + Short(e.Uri)); }
            };
            core.NewWindowRequested += delegate (object o, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
            core.PermissionRequested += delegate (object o, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
            Try(delegate { core.DownloadStarting += delegate (object o, CoreWebView2DownloadStartingEventArgs e) { e.Cancel = true; }; });
            core.NavigationCompleted += delegate (object o, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (my != gen) return;
                if (e.IsSuccess) { loaded = true; Up = true; navFails = 0; if (OnLoaded != null) OnLoaded(); return; }
                if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;   // a navigation we blocked: the page stays as it is
                // our local server is not answering yet (engine start) or the page broke: try again, slower each time
                navFails++;
                Later(Math.Min(10000, 800 * navFails), my, delegate { if (view != null && view.CoreWebView2 != null) view.CoreWebView2.Navigate(url); });
            };
            core.ProcessFailed += delegate (object o, CoreWebView2ProcessFailedEventArgs e)
            {
                if (my != gen) return;
                CoreWebView2ProcessFailedKind k = e.ProcessFailedKind;
                if (k == CoreWebView2ProcessFailedKind.BrowserProcessExited) { ForgetEnv(dataDir); Later(1000, my, delegate { Recreate("WebView2 browser process ended"); }); }
                else if (k == CoreWebView2ProcessFailedKind.RenderProcessExited || k == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive || k == CoreWebView2ProcessFailedKind.FrameRenderProcessExited)
                    Later(500, my, delegate { Recreate("page " + (k == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive ? "stopped responding" : "crashed")); });
                // GPU / utility processes are restarted by WebView2 itself
            };
            core.WebMessageReceived += delegate (object o, CoreWebView2WebMessageReceivedEventArgs e)
            {
                if (my != gen || OnMessage == null || !Same(e.Source)) return;
                try { OnMessage(e); } catch (Exception ex) { Program.Log(Name + ": page message: " + ex.Message); }
            };
        }

        bool Same(string u) { return u != null && (u == origin || u.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase)); }
        static string Short(string u) { return u == null ? "" : u.Length > 80 ? u.Substring(0, 80) + "..." : u; }
        static void Try(Action a) { try { a(); } catch { } }   // settings a WebView2 Runtime may be too old for

        void DisposeView()
        {
            WebView2 v = view; view = null; loaded = false; Up = false;
            if (v == null) return;
            try { if (v.Parent != null) v.Parent.Controls.Remove(v); } catch { }
            try { v.Dispose(); } catch { }
        }

        // the page's keyboard focus (the Start menu search box)
        public void Focus() { if (view != null && !view.IsDisposed) try { view.Focus(); } catch { } }

        // one-shot timer on the UI thread; skipped when the page was replaced or closed meanwhile
        void Later(int ms, int my, Action a)
        {
            Timer t = new Timer(); t.Interval = Math.Max(1, ms);
            t.Tick += delegate { t.Stop(); t.Dispose(); if (my == gen && host != null && !host.IsDisposed) a(); };
            t.Start();
        }

        // ---------- memory watchdog: private memory of this page's browser process and its children (GPU, renderer, network), MB
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PROCESSENTRY32W { public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile; }
        [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snap, ref PROCESSENTRY32W e);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        public long MemoryMB()
        {
            uint root = 0;
            try { if (view != null && view.CoreWebView2 != null) root = view.CoreWebView2.BrowserProcessId; } catch { return 0; }
            if (root == 0) return 0;
            List<uint> ids = new List<uint> { root };
            IntPtr snap = CreateToolhelp32Snapshot(2 /*TH32CS_SNAPPROCESS*/, 0);
            if (snap != IntPtr.Zero && snap != new IntPtr(-1))
            {
                try
                {
                    PROCESSENTRY32W e = new PROCESSENTRY32W(); e.dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W));
                    if (Process32FirstW(snap, ref e)) do { if (e.th32ParentProcessID == root) ids.Add(e.th32ProcessID); } while (Process32NextW(snap, ref e));
                }
                finally { CloseHandle(snap); }
            }
            long total = 0;
            foreach (uint id in ids) { try { using (Process p = Process.GetProcessById((int)id)) total += p.PrivateMemorySize64; } catch { } }
            return total / (1024 * 1024);
        }
    }
}
