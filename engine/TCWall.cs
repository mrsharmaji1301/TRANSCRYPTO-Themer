// TRANSCRYPTO Themer - TCWall engine
// Live video wallpaper (Media Foundation MFPlay, hardware decoded, parented behind the desktop icons),
// TRANSCRYPTO Live Lock (video lock screen -> real Windows sign-in), wallpaper slideshow, custom startup sound.
//
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x csc.exe) so it builds
// on any Windows 11 PC without downloading anything:
//   %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:TCWall.exe
//       /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll
//       /lib:%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll TCWall.cs
// (engine\build.cmd does exactly this.)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Transcrypto
{
    // ------------------------------------------------------------------ MFPlay interop
    [StructLayout(LayoutKind.Sequential)] public struct MFVideoNormalizedRect { public float left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

    [ComImport, Guid("A714590A-58AF-430a-85BF-44F5EC838D85"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFPMediaPlayer
    {
        [PreserveSig] int Play();
        [PreserveSig] int Pause();
        [PreserveSig] int Stop();
        [PreserveSig] int FrameStep();
        [PreserveSig] int SetPosition(ref Guid guidPositionType, IntPtr pvPositionValue);
        [PreserveSig] int GetPosition(ref Guid guidPositionType, IntPtr pvPositionValue);
        [PreserveSig] int GetDuration(ref Guid guidPositionType, IntPtr pvDurationValue);
        [PreserveSig] int SetRate(float flRate);
        [PreserveSig] int GetRate(out float pflRate);
        [PreserveSig] int GetSupportedRates(int fForwardDirection, out float pflSlowestRate, out float pflFastestRate);
        [PreserveSig] int GetState(out int peState);
        [PreserveSig] int CreateMediaItemFromURL([MarshalAs(UnmanagedType.LPWStr)] string pwszURL, int fSync, IntPtr dwUserData, out IntPtr ppMediaItem);
        [PreserveSig] int CreateMediaItemFromObject(IntPtr pIUnknownObj, int fSync, IntPtr dwUserData, out IntPtr ppMediaItem);
        [PreserveSig] int SetMediaItem(IntPtr pIMFPMediaItem);
        [PreserveSig] int ClearMediaItem();
        [PreserveSig] int GetMediaItem(out IntPtr ppIMFPMediaItem);
        [PreserveSig] int GetVolume(out float pflVolume);
        [PreserveSig] int SetVolume(float flVolume);
        [PreserveSig] int GetBalance(out float pflBalance);
        [PreserveSig] int SetBalance(float flBalance);
        [PreserveSig] int GetMute(out int pfMute);
        [PreserveSig] int SetMute(int fMute);
        [PreserveSig] int GetNativeVideoSize(out SIZE pszVideo, out SIZE pszARVideo);
        [PreserveSig] int GetIdealVideoSize(out SIZE pszMin, out SIZE pszMax);
        [PreserveSig] int SetVideoSourceRect(ref MFVideoNormalizedRect pnrcSource);
        [PreserveSig] int GetVideoSourceRect(out MFVideoNormalizedRect pnrcSource);
        [PreserveSig] int SetAspectRatioMode(int dwAspectRatioMode);
        [PreserveSig] int GetAspectRatioMode(out int pdwAspectRatioMode);
        [PreserveSig] int GetVideoWindow(out IntPtr phwndVideo);
        [PreserveSig] int UpdateVideo();
        [PreserveSig] int SetBorderColor(int Clr);
        [PreserveSig] int GetBorderColor(out int pClr);
        [PreserveSig] int InsertEffect(IntPtr pEffect, int fOptional);
        [PreserveSig] int RemoveEffect(IntPtr pEffect);
        [PreserveSig] int RemoveAllEffects();
        [PreserveSig] int Shutdown();
    }

    [ComImport, Guid("766C8FFB-5FDB-4fea-A28D-B912996F51BD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMFPMediaPlayerCallback
    {
        [PreserveSig] void OnMediaPlayerEvent(IntPtr pEventHeader);
    }

    static class Native
    {
        [DllImport("mfplay.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern int MFPCreateMediaPlayer(string pwszURL, int fStartPlayback, int creationOptions,
            IMFPMediaPlayerCallback pCallback, IntPtr hWnd, out IMFPMediaPlayer ppMediaPlayer);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(IntPtr from, IntPtr to, ref RECT pts, int count);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfo(uint a, uint p, string v, uint f);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr v);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        public static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static bool IsCloaked(IntPtr h) { int v; return DwmGetWindowAttribute(h, 14, out v, 4) == 0 && v != 0; }
        public static uint IdleSeconds()
        {
            LASTINPUTINFO l = new LASTINPUTINFO(); l.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            GetLastInputInfo(ref l);
            return ((uint)Environment.TickCount - l.dwTime) / 1000;
        }
        public static void TrimMemory() { try { SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1)); } catch { } }
    }

    // ------------------------------------------------------------------ config helpers
    static class Cfg
    {
        public static Dictionary<string, object> Dict(Dictionary<string, object> d, string k)
        {
            object o; if (d != null && d.TryGetValue(k, out o) && o is Dictionary<string, object>) return (Dictionary<string, object>)o;
            return new Dictionary<string, object>();
        }
        public static string Str(Dictionary<string, object> d, string k, string def)
        {
            object o; if (d != null && d.TryGetValue(k, out o) && o != null) return Convert.ToString(o); return def;
        }
        public static bool Bool(Dictionary<string, object> d, string k, bool def)
        {
            object o; if (d != null && d.TryGetValue(k, out o) && o != null) { if (o is bool) return (bool)o; bool b; if (bool.TryParse(Convert.ToString(o), out b)) return b; }
            return def;
        }
        public static double Num(Dictionary<string, object> d, string k, double def)
        {
            object o; if (d != null && d.TryGetValue(k, out o) && o != null) { try { return Convert.ToDouble(o); } catch { } }
            return def;
        }
        public static Color ParseColor(string hex, Color def)
        {
            if (string.IsNullOrEmpty(hex)) return def;
            try { Color c = ColorTranslator.FromHtml(hex); return c.IsEmpty ? def : c; } catch { return def; }
        }
    }

    // ------------------------------------------------------------------ a window that plays one looping video
    [ComVisible(true)]
    public class PlayerCallback : IMFPMediaPlayerCallback
    {
        VideoSurface owner;
        public PlayerCallback(VideoSurface o) { owner = o; }
        public void OnMediaPlayerEvent(IntPtr hdr) { owner.HandleEvent(hdr); }
    }

    public class VideoSurface : Form
    {
        IMFPMediaPlayer player; PlayerCallback callback;
        string file; string fit; float volume;
        public bool Paused;
        public Screen Screen;
        const int EV_MEDIAITEM_SET = 6, EV_ERROR = 10, EV_PLAYBACK_ENDED = 11;

        public VideoSurface(Screen screen, string file, string fit, float volume)
        {
            Screen = screen; this.file = file; this.fit = fit; this.volume = volume;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = System.Drawing.Color.Black; Bounds = screen.Bounds;
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Start()
        {
            if (player != null) return;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return;
            IMFPMediaPlayer p;
            callback = new PlayerCallback(this);
            int hr = Native.MFPCreateMediaPlayer(file, 1, 0, callback, Handle, out p);
            if (hr != 0 || p == null) { Program.Log("MFPCreateMediaPlayer failed 0x" + hr.ToString("X8") + " for " + file); return; }
            player = p;
        }

        public void HandleEvent(IntPtr hdr)
        {
            try
            {
                int type = Marshal.ReadInt32(hdr, 0);
                int hrEv = Marshal.ReadInt32(hdr, 4);
                if (player == null) return;
                if (type == EV_MEDIAITEM_SET) { ApplyFit(); ApplyVolume(); if (Paused) player.Pause(); }
                else if (type == EV_PLAYBACK_ENDED) { Rewind(); if (!Paused) player.Play(); }
                else if (type == EV_ERROR) { Program.Log("playback error 0x" + hrEv.ToString("X8")); }
            }
            catch (Exception ex) { Program.Log("event: " + ex.Message); }
        }

        void Rewind()
        {
            IntPtr pv = Marshal.AllocHGlobal(24);
            try
            {
                for (int i = 0; i < 24; i++) Marshal.WriteByte(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 20); // VT_I8
                Marshal.WriteInt64(pv, 8, 0);
                Guid g = Guid.Empty;           // MFP_POSITIONTYPE_100NS
                player.SetPosition(ref g, pv);
            }
            finally { Marshal.FreeHGlobal(pv); }
        }

        void ApplyVolume()
        {
            if (volume <= 0) player.SetMute(1); else { player.SetMute(0); player.SetVolume(Math.Min(1f, volume)); }
        }

        void ApplyFit()
        {
            SIZE v, ar;
            if (player.GetNativeVideoSize(out v, out ar) != 0 || v.cx == 0 || v.cy == 0) return;
            MFVideoNormalizedRect r = new MFVideoNormalizedRect(); r.left = 0; r.top = 0; r.right = 1; r.bottom = 1;
            double W = ClientSize.Width, H = ClientSize.Height;
            if (fit == "fit") { player.SetAspectRatioMode(1); player.SetBorderColor(0); }
            else
            {
                player.SetAspectRatioMode(0); // stretch the source rect to the window
                if (fit != "stretch")
                {
                    double scale = Math.Max(W / v.cx, H / v.cy);
                    double fx = (W / scale) / v.cx, fy = (H / scale) / v.cy;
                    r.left = (float)((1 - fx) / 2); r.right = (float)(1 - (1 - fx) / 2);
                    r.top = (float)((1 - fy) / 2); r.bottom = (float)(1 - (1 - fy) / 2);
                }
            }
            player.SetVideoSourceRect(ref r);
            player.UpdateVideo();
        }

        public void SetPaused(bool p)
        {
            if (p == Paused) return;
            Paused = p;
            if (player == null) return;
            if (p) player.Pause(); else player.Play();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (player != null) { try { ApplyFit(); } catch { } } }
        protected override void OnPaint(PaintEventArgs e) { if (player != null) { try { player.UpdateVideo(); } catch { } } else base.OnPaint(e); }

        public void Shutdown()
        {
            if (player != null) { try { player.Shutdown(); } catch { } try { Marshal.ReleaseComObject(player); } catch { } player = null; }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { Shutdown(); base.OnFormClosed(e); }
    }

    // ------------------------------------------------------------------ per-pixel alpha helper for layered windows
    static class Layered
    {
        public static void Push(IntPtr hwnd, Bitmap bmp, int x, int y)
        {
            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            IntPtr memDc = Native.CreateCompatibleDC(screenDc);
            IntPtr hb = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr old = Native.SelectObject(memDc, hb);
            try
            {
                SIZE sz = new SIZE(); sz.cx = bmp.Width; sz.cy = bmp.Height;
                Native.POINT src = new Native.POINT(); Native.POINT dst = new Native.POINT(); dst.X = x; dst.Y = y;
                Native.BLENDFUNCTION bf = new Native.BLENDFUNCTION(); bf.BlendOp = 0; bf.SourceConstantAlpha = 255; bf.AlphaFormat = 1;
                Native.UpdateLayeredWindow(hwnd, screenDc, ref dst, ref sz, memDc, ref src, 0, ref bf, 2);
            }
            finally { Native.SelectObject(memDc, old); Native.DeleteObject(hb); Native.DeleteDC(memDc); Native.ReleaseDC(IntPtr.Zero, screenDc); }
        }
    }

    // ------------------------------------------------------------------ click effect: neon ripple wherever you click (taskbar, desktop, apps)
    public class RippleWindow : Form
    {
        System.Windows.Forms.Timer timer;
        DateTime start; int size; Point center; Color c1, c2; string style;
        public bool Busy; public DateTime Started;
        const int DurationMs = 460;

        public RippleWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Size = new Size(1, 1); Location = new Point(-32000, -32000);
            timer = new System.Windows.Forms.Timer(); timer.Interval = 15; timer.Tick += delegate { Frame(); };
        }
        protected override CreateParams CreateParams
        {
            // layered + click-through + no activation + not in Alt-Tab
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8 /*TOPMOST*/; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        public void Play(Point pt, int sizePx, Color a, Color b, string st)
        {
            center = pt; size = Math.Max(24, sizePx); c1 = a; c2 = b; style = st;
            start = DateTime.Now; Started = start; Busy = true;
            if (!Visible) Show();
            Native.SetWindowPos(Handle, new IntPtr(-1) /*TOPMOST*/, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 /*NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW*/);
            Frame(); timer.Start();
        }

        static Color Mix(Color a, Color b, double t, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void Frame()
        {
            double t = (DateTime.Now - start).TotalMilliseconds / DurationMs;
            if (t >= 1) { timer.Stop(); Busy = false; Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0004 | 0x0080 /*HIDEWINDOW*/); return; }
            double e = 1 - Math.Pow(1 - t, 3);                 // ease-out
            float S = size, mid = S / 2f;
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                int fade = (int)(255 * Math.Pow(1 - t, 1.3));
                Color col = Mix(c1, c2, t, fade);
                if (style == "burst")
                {
                    for (int i = 0; i < 10; i++)
                    {
                        double ang = i * Math.PI / 5;
                        float r0 = (float)(S * 0.12 + S * 0.22 * e), r1 = (float)(S * 0.18 + S * 0.30 * e);
                        using (Pen p = new Pen(i % 2 == 0 ? col : Mix(c2, c1, t, fade), (float)Math.Max(1.5, S * 0.045 * (1 - t))))
                        { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, mid + (float)Math.Cos(ang) * r0, mid + (float)Math.Sin(ang) * r0, mid + (float)Math.Cos(ang) * r1, mid + (float)Math.Sin(ang) * r1); }
                    }
                }
                else
                {
                    int rings = style == "pulse" ? 2 : 1;
                    for (int k = 0; k < rings; k++)
                    {
                        double tk = Math.Max(0, Math.Min(1, (t - k * 0.22) / (1 - k * 0.22)));
                        if (tk <= 0) continue;
                        double ek = 1 - Math.Pow(1 - tk, 3);
                        float r = (float)(S * 0.08 + (S * 0.40) * ek);
                        int a2 = (int)(fade * (k == 0 ? 1 : 0.7));
                        using (Pen glow = new Pen(Mix(c1, c2, tk, a2 / 4), (float)(S * 0.10 * (1 - tk) + 2)))
                            g.DrawEllipse(glow, mid - r, mid - r, r * 2, r * 2);
                        using (Pen ring = new Pen(Mix(c1, c2, tk, a2), (float)Math.Max(1.5, S * 0.05 * (1 - tk))))
                            g.DrawEllipse(ring, mid - r, mid - r, r * 2, r * 2);
                    }
                }
                float fr = (float)(S * 0.16 * (1 - e));
                using (SolidBrush flash = new SolidBrush(Mix(Color.White, c1, 0.35, (int)(170 * Math.Pow(1 - t, 2)))))
                    if (fr > 0.5f) g.FillEllipse(flash, mid - fr, mid - fr, fr * 2, fr * 2);
                Layered.Push(Handle, bmp, center.X - size / 2, center.Y - size / 2);
            }
        }
        protected override void OnFormClosed(FormClosedEventArgs e) { timer.Stop(); timer.Dispose(); base.OnFormClosed(e); }
    }

    public class ClickFx
    {
        delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, LowLevelProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Native.POINT pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dx, out uint dy);

        IntPtr hook = IntPtr.Zero; LowLevelProc proc; Control ui;
        List<RippleWindow> pool = new List<RippleWindow>();
        public int SizeDip = 90; public bool RightClick = true, SkipFullscreen = true; public string Style = "ripple";
        public Color C1 = Color.Cyan, C2 = Color.Magenta;

        public ClickFx(Control uiThread) { ui = uiThread; proc = Hook; }

        public void Enable(bool on)
        {
            if (on && hook == IntPtr.Zero) { hook = SetWindowsHookEx(14 /*WH_MOUSE_LL*/, proc, GetModuleHandle(null), 0); if (hook == IntPtr.Zero) Program.Log("click hook failed " + Marshal.GetLastWin32Error()); }
            if (!on && hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; foreach (RippleWindow r in pool) { r.Close(); r.Dispose(); } pool.Clear(); }
        }

        // Windows silently drops a low-level hook whose callback once ran past LowLevelHooksTimeout
        // (easy during an explorer restart), so it is re-installed periodically while enabled
        public void Rehook()
        {
            if (hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(hook);
            hook = SetWindowsHookEx(14 /*WH_MOUSE_LL*/, proc, GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Program.Log("click re-hook failed " + Marshal.GetLastWin32Error());
        }

        IntPtr Hook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // keep this fast: Windows drops slow low-level hooks
            int msg = wParam.ToInt32();
            if (nCode >= 0 && (msg == 0x0201 || (RightClick && msg == 0x0204)))
            {
                Point pt = new Point(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
                try { ui.BeginInvoke(new MethodInvoker(delegate { Spawn(pt); })); } catch { }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        void Spawn(Point pt)
        {
            if (SkipFullscreen && Engine.IsFullscreenApp(Native.GetForegroundWindow())) return;
            double scale = 1;
            try
            {
                Native.POINT p = new Native.POINT(); p.X = pt.X; p.Y = pt.Y; uint dx, dy;
                if (GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out dx, out dy) == 0) scale = dx / 96.0;
            }
            catch { }
            RippleWindow w = null;
            foreach (RippleWindow r in pool) if (!r.Busy) { w = r; break; }
            if (w == null && pool.Count < 6) { w = new RippleWindow(); pool.Add(w); }
            if (w == null) { w = pool[0]; foreach (RippleWindow r in pool) if (r.Started < w.Started) w = r; }
            w.Play(pt, (int)(SizeDip * scale), C1, C2, Style);
        }
    }

    // ------------------------------------------------------------------ taskbar neon hover: glow frame over the taskbar button under the cursor
    public class GlowWindow : Form
    {
        bool shown;
        public GlowWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Size = new Size(1, 1); Location = new Point(-32000, -32000);
        }
        protected override CreateParams CreateParams
        {
            // layered + click-through + no activation + not in Alt-Tab
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80 | 0x8 /*TOPMOST*/; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }

        public void ShowAt(Rectangle r, Color c1, Color c2, int pad)
        {
            int w = r.Width + pad * 2, hgt = r.Height + pad * 2;
            using (Bitmap bmp = new Bitmap(w, hgt, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                for (int i = pad; i >= 1; i--)   // hard neon bloom: bright near the frame, shifting c1 -> c2 outwards
                {
                    double t = (double)i / (pad + 1);
                    int a = (int)(235.0 * Math.Pow(1 - t, 1.7));
                    Color c = Color.FromArgb(a, (int)(c1.R + (c2.R - c1.R) * t), (int)(c1.G + (c2.G - c1.G) * t), (int)(c1.B + (c2.B - c1.B) * t));
                    using (Pen p = new Pen(c, 2.2f))
                    using (GraphicsPath gp = Round(new RectangleF(pad - i, pad - i, r.Width + i * 2 - 1, r.Height + i * 2 - 1), 8 + i))
                        g.DrawPath(p, gp);
                }
                RectangleF inner = new RectangleF(pad, pad, r.Width - 1, r.Height - 1);
                using (GraphicsPath gp = Round(inner, 8))
                {
                    // light tint only, so the icon and label underneath stay crisp and pop against the glow
                    using (LinearGradientBrush fill = new LinearGradientBrush(inner, Color.FromArgb(70, c1), Color.FromArgb(28, c2), 90f)) g.FillPath(fill, gp);
                    using (LinearGradientBrush lb = new LinearGradientBrush(inner, c1, c2, 45f))
                    using (Pen p = new Pen(lb, 2.6f)) g.DrawPath(p, gp);
                }
                using (GraphicsPath gp = Round(RectangleF.Inflate(inner, -2.5f, -2.5f), 6))
                using (Pen p = new Pen(Color.FromArgb(110, 255, 255, 255), 1f)) g.DrawPath(p, gp);   // inner highlight
                float bw = Math.Min(inner.Width * 0.5f, 28f), bx = pad + (inner.Width - bw) / 2, by = pad + inner.Height - 4;   // neon underline
                using (LinearGradientBrush ub = new LinearGradientBrush(new RectangleF(bx, by - 2, bw + 1, 4), c1, c2, 0f))
                using (Pen p = new Pen(ub, 3f)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, bx, by, bx + bw, by); }
                Layered.Push(Handle, bmp, r.X - pad, r.Y - pad);
            }
            if (!Visible) Show();
            // the taskbar is topmost too, so re-assert our place above it every time
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0040 /*NOSIZE|NOMOVE|NOACTIVATE|SHOWWINDOW*/);
            shown = true;
        }

        public void HideGlow()
        {
            if (!shown) return;
            Native.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0004 | 0x0080 /*HIDEWINDOW*/);
            shown = false;
        }
    }

    public class TaskbarHover
    {
        [DllImport("user32.dll")] static extern bool GetCursorPos(out Native.POINT p);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Native.POINT p);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);

        Control ui; GlowWindow glow; System.Threading.Timer timer; int busy; volatile bool on;
        Native.POINT lastPt; Rectangle lastRect = Rectangle.Empty;
        public Color C1 = Color.Cyan, C2 = Color.Magenta;

        public TaskbarHover(Control uiThread) { ui = uiThread; }

        public void Enable(bool enable)
        {
            if (enable == on) return;
            on = enable;
            if (on) { glow = new GlowWindow(); lastRect = Rectangle.Empty; lastPt = new Native.POINT(); timer = new System.Threading.Timer(Poll, null, 0, 45); }
            else
            {
                if (timer != null) { timer.Dispose(); timer = null; }
                if (glow != null) { glow.Close(); glow.Dispose(); glow = null; }
            }
        }

        // runs on a pool thread: UI Automation calls into explorer and must never block the engine's UI thread
        void Poll(object state)
        {
            if (!on || Interlocked.Exchange(ref busy, 1) == 1) return;
            try
            {
                Native.POINT p; if (!GetCursorPos(out p)) return;
                if (p.X == lastPt.X && p.Y == lastPt.Y) return;
                lastPt = p;
                Rectangle r = Rectangle.Empty;
                IntPtr root = GetAncestor(WindowFromPoint(p), 2 /*GA_ROOT*/);
                string cls = Native.ClassOf(root);
                if (cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") r = ButtonAt(p, root);
                if (r == lastRect) return;
                lastRect = r;
                ui.BeginInvoke(new MethodInvoker(delegate
                {
                    if (glow == null || !on) return;
                    if (r.IsEmpty) glow.HideGlow(); else glow.ShowAt(r, C1, C2, 12);
                }));
            }
            catch (Exception ex) { LogOnce("hover: " + ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        DateTime lastLog = DateTime.MinValue;
        void LogOnce(string s) { if ((DateTime.Now - lastLog).TotalSeconds > 30) { lastLog = DateTime.Now; Program.Log(s); } }

        // explorer restarted: drop every cached button list
        public void Reset() { lock (cache) { cache.Clear(); cachedAt.Clear(); } lastPt = new Native.POINT(); lastRect = Rectangle.Empty; }

        // Windows 11's taskbar is a XAML island: FromPoint only returns the outer pane, so the buttons are
        // enumerated from the tree (~40 ms) and cached per taskbar, then hit-tested locally on every move.
        // The enumeration runs on its own thread: a UIA call into a restarting explorer can hang, and it
        // must never stall the hover loop (a refresh stuck for 3 s is abandoned and a new one started).
        Dictionary<IntPtr, List<Rectangle>> cache = new Dictionary<IntPtr, List<Rectangle>>();
        Dictionary<IntPtr, DateTime> cachedAt = new Dictionary<IntPtr, DateTime>();
        Dictionary<IntPtr, DateTime> refreshing = new Dictionary<IntPtr, DateTime>();
        static readonly Condition ButtonCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

        Rectangle ButtonAt(Native.POINT p, IntPtr bar)
        {
            List<Rectangle> list = null; DateTime at, since;
            lock (cache)
            {
                cache.TryGetValue(bar, out list);
                bool stale = list == null || !cachedAt.TryGetValue(bar, out at) || (DateTime.Now - at).TotalMilliseconds > 1500;
                bool running = refreshing.TryGetValue(bar, out since) && (DateTime.Now - since).TotalMilliseconds < 3000;
                if (stale && !running) { refreshing[bar] = DateTime.Now; StartRefresh(bar); }
            }
            if (list == null) { lastPt = new Native.POINT(); return Rectangle.Empty; }   // re-check once the first list arrives
            foreach (Rectangle r in list)
                if (r.Contains(p.X, p.Y)) return Rectangle.Inflate(r, -1, -Math.Max(2, r.Height / 12));   // XAML hit boxes span the full bar height
            return Rectangle.Empty;
        }

        void StartRefresh(IntPtr bar)
        {
            Thread t = new Thread(delegate ()
            {
                try
                {
                    List<Rectangle> l = Buttons(bar);
                    lock (cache) { cache[bar] = l; cachedAt[bar] = DateTime.Now; refreshing.Remove(bar); }
                }
                catch (Exception ex) { lock (cache) refreshing.Remove(bar); LogOnce("taskbar buttons: " + ex.Message); }
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.MTA); t.Start();
        }

        static List<Rectangle> Buttons(IntPtr bar)
        {
            List<Rectangle> list = new List<Rectangle>();
            Native.RECT br; Native.GetWindowRect(bar, out br);
            int barW = br.Right - br.Left;
            AutomationElementCollection all = AutomationElement.FromHandle(bar).FindAll(TreeScope.Descendants, ButtonCond);
            foreach (AutomationElement e in all)
            {
                System.Windows.Rect b = e.Current.BoundingRectangle;
                if (b.IsEmpty || b.Width < 16 || b.Height < 16 || b.Width > barW * 0.6) continue;   // skip slivers (Show Desktop) and containers
                list.Add(new Rectangle((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height));
            }
            return list;
        }
    }

    // ------------------------------------------------------------------ Explorer tint: preset-colored gradients over File Explorer's dark panes
    // Windows has no setting for Explorer's pane colors, so a click-through layered window owned by each
    // Explorer window lays a translucent gradient over it. Owned windows stay just above their owner in the
    // z-order and hide with it, so other apps still cover Explorer normally. Over the near-black dark-mode
    // background the tint reads as "preset color fading into black"; text stays readable because alpha is low.
    public class TintWindow : Form
    {
        public TintWindow()
        {
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            Size = new Size(1, 1); Location = new Point(-32000, -32000);
        }
        protected override CreateParams CreateParams
        {
            // layered + click-through + no activation + not in Alt-Tab (not topmost: the owner decides the z-order)
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public string Geometry = "";
        public bool IsBar;
    }

    public class ExplorerTint
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, Native.EnumProc cb, IntPtr l);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

        [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attr, out Native.RECT r, int size);

        Control ui; bool on; IntPtr evHook = IntPtr.Zero; WinEventProc evProc;
        Dictionary<IntPtr, TintWindow> tints = new Dictionary<IntPtr, TintWindow>();
        HashSet<IntPtr> dirty = new HashSet<IntPtr>();
        System.Windows.Forms.Timer scan, debounce;
        public Color C1 = Color.Cyan, C2 = Color.Magenta; public double Strength = 0.6;
        public bool ExplorerOn, BarOn;                                   // which windows get a tint
        public Color BarC1 = Color.Cyan, BarC2 = Color.Magenta; public string BarStyle = "gradient"; public double BarStrength = 0.7;
        public string BarDesign = "none";   // "none" = plain skin (BarStyle) or one of BarDesigns.Names

        // taskbar sections read through UI Automation, refreshed off the UI thread
        public class BarItem { public Rectangle R; public string Kind; public bool Running; }
        Dictionary<IntPtr, List<BarItem>> barItems = new Dictionary<IntPtr, List<BarItem>>();
        Dictionary<IntPtr, string> barSig = new Dictionary<IntPtr, string>();
        List<IntPtr> barHandles = new List<IntPtr>();
        System.Threading.Timer itemTimer; DateTime itemsStarted = DateTime.MinValue; volatile bool itemsRunning;

        public ExplorerTint(Control uiThread)
        {
            ui = uiThread; evProc = OnEvent;
            scan = new System.Windows.Forms.Timer(); scan.Interval = 700; scan.Tick += delegate { Scan(); };
            debounce = new System.Windows.Forms.Timer(); debounce.Interval = 16; debounce.Tick += delegate { debounce.Stop(); Flush(); };
        }

        // call after setting ExplorerOn / BarOn / colors / styles
        public void Apply()
        {
            bool want = ExplorerOn || BarOn;
            if (want && !on)
            {
                on = true;
                // FOREGROUND (0x3) + REORDER (0x8004) for z-order, LOCATIONCHANGE (0x800B) for moves/resizes/auto-hide, SHOW/HIDE (0x8002-3), minimize/restore (0x16-17)
                evHook = SetWinEventHook(0x0003, 0x800B, IntPtr.Zero, evProc, 0, 0, 0x0000 /*OUTOFCONTEXT*/);
                scan.Start();
            }
            else if (!want && on)
            {
                on = false; scan.Stop(); debounce.Stop();
                if (evHook != IntPtr.Zero) { UnhookWinEvent(evHook); evHook = IntPtr.Zero; }
            }
            bool designs = on && BarOn;   // every skin uses the button layout (gaps get the strong color)
            if (designs && itemTimer == null) itemTimer = new System.Threading.Timer(PollItems, null, 0, 1500);
            else if (!designs && itemTimer != null) { itemTimer.Dispose(); itemTimer = null; }
            foreach (TintWindow t in tints.Values) t.Geometry = "";      // colors or styles may have changed: redraw all
            Scan();
        }

        // Re-read every taskbar's buttons; when apps open/close or the layout changes, that bar is redrawn.
        // Runs on its own thread: a UIA call into a restarting explorer can hang, so a stuck read is abandoned after 4 s.
        void PollItems(object state)
        {
            if (itemsRunning && (DateTime.Now - itemsStarted).TotalSeconds < 4) return;
            itemsRunning = true; itemsStarted = DateTime.Now;
            Thread th = new Thread(delegate ()
            {
                try
                {
                    List<IntPtr> bars; lock (barHandles) bars = new List<IntPtr>(barHandles);
                    foreach (IntPtr bar in bars)
                    {
                        List<BarItem> items = ReadBar(bar);
                        StringBuilder sb = new StringBuilder();
                        foreach (BarItem i in items) sb.Append(i.Kind).Append(i.Running ? "*" : "").Append(i.R.X).Append(',').Append(i.R.Y).Append(',').Append(i.R.Width).Append(';');
                        string sig = sb.ToString(); bool changed;
                        lock (barItems) { string old; barSig.TryGetValue(bar, out old); changed = old != sig; barItems[bar] = items; barSig[bar] = sig; }
                        if (changed) { IntPtr b = bar; try { ui.BeginInvoke(new MethodInvoker(delegate { dirty.Add(b); Flush(); })); } catch { } }
                    }
                }
                catch (Exception ex) { Program.Log("taskbar items: " + ex.Message); }
                finally { itemsRunning = false; }
            });
            th.IsBackground = true; th.Start();
        }

        static readonly Condition BtnCond = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button);

        static List<BarItem> ReadBar(IntPtr bar)
        {
            List<BarItem> list = new List<BarItem>();
            foreach (AutomationElement e in AutomationElement.FromHandle(bar).FindAll(TreeScope.Descendants, BtnCond))
            {
                System.Windows.Rect b = e.Current.BoundingRectangle;
                if (b.IsEmpty || b.Width < 4 || b.Height < 4) continue;
                string cls = e.Current.ClassName ?? "", id = e.Current.AutomationId ?? "", name = e.Current.Name ?? "";
                string kind = null; bool running = false;
                if (id == "StartButton") kind = "start";
                else if (id == "SearchButton") kind = "search";
                else if (id == "TaskViewButton") kind = "taskview";
                else if (id == "WidgetsButton") kind = "widgets";
                else if (cls.StartsWith("Taskbar.TaskListButton")) { kind = "app"; running = name.IndexOf("running window", StringComparison.OrdinalIgnoreCase) >= 0; }
                else if (cls == "SystemTray.ShowDesktopButton") kind = "desktop";
                else if (cls == "SystemTray.OmniButton" && name.IndexOf(':') >= 0) kind = "clock";
                else if (cls.StartsWith("SystemTray.Omni") || cls == "SystemTray.AccentButton") kind = "sys";
                else if (cls == "SystemTray.NormalButton") kind = name.IndexOf("Hidden", StringComparison.OrdinalIgnoreCase) >= 0 ? "overflow" : "tray";
                if (kind == null) continue;
                BarItem it = new BarItem(); it.R = new Rectangle((int)b.X, (int)b.Y, (int)b.Width, (int)b.Height); it.Kind = kind; it.Running = running;
                list.Add(it);
            }
            list.Sort(delegate (BarItem x, BarItem y) { return x.R.X.CompareTo(y.R.X); });
            return list;
        }

        public void Disable() { ExplorerOn = BarOn = false; Apply(); }

        static bool IsBarClass(string cls) { return cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd"; }

        void OnEvent(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || obj != 0 /*OBJID_WINDOW*/) return;
            if (ev == 0x0003 /*FOREGROUND*/ || ev == 0x8004 /*REORDER*/) { RestackAll(); return; }   // another window came forward
            if (ev != 0x800B && ev != 0x8002 && ev != 0x8003 && ev != 0x0016 && ev != 0x0017) return;
            IntPtr root = GetAncestor(hwnd, 2 /*GA_ROOT*/);
            if (!tints.ContainsKey(root)) return;
            dirty.Add(root);
            if (!debounce.Enabled) debounce.Start();
        }

        // pick up new Explorer windows / taskbars (also after an explorer restart) and drop closed or disabled ones
        void Scan()
        {
            List<IntPtr> found = new List<IntPtr>();
            if (on) Native.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                string cls = Native.ClassOf(h);
                if ((ExplorerOn && cls == "CabinetWClass") || (BarOn && IsBarClass(cls))) found.Add(h);
                return true;
            }, IntPtr.Zero);
            foreach (IntPtr h in new List<IntPtr>(tints.Keys))
                if (!found.Contains(h) || !Native.IsWindow(h)) { tints[h].Close(); tints[h].Dispose(); tints.Remove(h); }
            foreach (IntPtr h in found)
            {
                if (!tints.ContainsKey(h)) tints[h] = NewTint(h, IsBarClass(Native.ClassOf(h)));
                dirty.Add(h);   // also catches tab switches, pane splitter drags and dark/light changes
            }
            lock (barHandles) { barHandles.Clear(); foreach (IntPtr h in found) if (tints[h].IsBar) barHandles.Add(h); }
            Flush();
        }

        TintWindow NewTint(IntPtr owner, bool bar)
        {
            TintWindow t = new TintWindow(); t.IsBar = bar;
            IntPtr force = t.Handle;
            // owned by its window: stays right above it in the z-order, hides and minimizes with it
            if (IntPtr.Size == 8) SetWindowLongPtr64(t.Handle, -8 /*GWLP_HWNDPARENT*/, owner); else SetWindowLong32(t.Handle, -8, owner.ToInt32());
            return t;
        }

        void Flush()
        {
            foreach (IntPtr h in dirty) { TintWindow t; if (tints.TryGetValue(h, out t)) { try { if (t.IsBar) UpdateBar(h, t); else Update(h, t); } catch (Exception ex) { Program.Log("tint: " + ex.Message); } } }
            dirty.Clear();
        }

        static Rectangle RectOf(IntPtr h) { Native.RECT r; Native.GetWindowRect(h, out r); return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); }

        // the visible window without the invisible resize borders (a maximized window's rect starts at -8)
        static Rectangle FrameOf(IntPtr h)
        {
            Native.RECT r;
            if (DwmGetWindowAttribute(h, 9 /*DWMWA_EXTENDED_FRAME_BOUNDS*/, out r, Marshal.SizeOf(typeof(Native.RECT))) == 0) return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return RectOf(h);
        }

        void Hide(TintWindow t) { ShowWindow(t.Handle, 0); t.Geometry = ""; }

        void Update(IntPtr owner, TintWindow t)
        {
            if (!Native.IsWindowVisible(owner) || Native.IsIconic(owner) || Native.IsCloaked(owner)) { Hide(t); return; }
            // panes of the visible tab; everything above the tab is the header (tab strip + address/command bar)
            Rectangle tab = Rectangle.Empty, nav = Rectangle.Empty, view = Rectangle.Empty, caption = Rectangle.Empty;
            EnumChildWindows(owner, delegate (IntPtr c, IntPtr l)
            {
                if (!Native.IsWindowVisible(c)) return true;
                string cls = Native.ClassOf(c);
                if (cls == "ShellTabWindowClass" && tab.IsEmpty) tab = RectOf(c);
                else if (cls == "SysTreeView32" && nav.IsEmpty) nav = RectOf(c);
                else if (cls == "SHELLDLL_DefView" && view.IsEmpty) view = RectOf(c);
                else if (cls == "TITLE_BAR_SCAFFOLDING_WINDOW_CLASS" && caption.IsEmpty) caption = RectOf(c);
                return true;
            }, IntPtr.Zero);
            Rectangle frame = FrameOf(owner);
            if (tab.IsEmpty || frame.Width < 64 || frame.Height < 64) { Hide(t); return; }
            Rectangle all = Rectangle.FromLTRB(frame.Left, frame.Top, frame.Right, Math.Min(frame.Bottom, Math.Max(tab.Bottom, frame.Top + 64)));
            Rectangle header = tab.Top > all.Top + 8 ? Rectangle.FromLTRB(all.Left, all.Top, all.Right, tab.Top) : Rectangle.Empty;
            Rectangle strip = Rectangle.Empty, bar = header;
            if (!header.IsEmpty)
            {
                int split = (!caption.IsEmpty && caption.Bottom > header.Top + 8 && caption.Bottom < header.Bottom - 8) ? caption.Bottom : header.Top + header.Height * 2 / 7;
                strip = Rectangle.FromLTRB(header.Left, header.Top, header.Right, split);
                bar = Rectangle.FromLTRB(header.Left, split, header.Right, header.Bottom);
            }
            bool round = !Native.IsZoomed(owner);
            string rel = RelativeKey(all, strip, bar, nav, view) + (round ? "r" : "");
            if (t.Geometry == all.ToString() + rel) { Restack(owner, t); return; }
            // a pure move keeps the drawn bitmap: only shift the window
            if (t.Geometry.Length > 0 && t.Tag is Size && (Size)t.Tag == all.Size && t.Geometry.EndsWith(rel))
            {
                Native.SetWindowPos(t.Handle, IntPtr.Zero, all.X, all.Y, 0, 0, 0x0001 | 0x0004 | 0x0010 /*NOSIZE|NOZORDER|NOACTIVATE*/);
                t.Geometry = all.ToString() + rel;
                Restack(owner, t);
                return;
            }
            Render(t, all, strip, bar, nav, view, round);
            t.Tag = all.Size;
            t.Geometry = all.ToString() + rel;
            ShowWindow(t.Handle, 4 /*SW_SHOWNOACTIVATE*/);
            Restack(owner, t);
        }

        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);

        // Showing a window puts it on top of everything, and ownership alone only keeps it *somewhere* above its
        // owner, so the tint could end up over other apps. Pin it directly above its Explorer window instead.
        static void Restack(IntPtr owner, TintWindow t)
        {
            IntPtr above = GetWindow(owner, 3 /*GW_HWNDPREV*/);
            if (above == t.Handle) return;
            Native.SetWindowPos(t.Handle, above == IntPtr.Zero ? IntPtr.Zero /*HWND_TOP*/ : above, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 /*NOSIZE|NOMOVE|NOACTIVATE*/);
        }

        void RestackAll() { foreach (KeyValuePair<IntPtr, TintWindow> kv in tints) if (!kv.Value.IsBar && Native.IsWindowVisible(kv.Value.Handle)) Restack(kv.Key, kv.Value); }

        void UpdateBar(IntPtr owner, TintWindow t)
        {
            if (!Native.IsWindowVisible(owner)) { Hide(t); return; }
            Rectangle r = RectOf(owner);
            if (r.Width < 32 || r.Height < 8) { Hide(t); return; }
            string sig = ""; lock (barItems) barSig.TryGetValue(owner, out sig);
            string geo = r.Size.ToString() + "#" + BarDesign + "#" + sig;   // a design redraws when the buttons change
            if (t.Geometry == r.ToString() + geo) return;
            if (t.Geometry.EndsWith(geo))
                Native.SetWindowPos(t.Handle, IntPtr.Zero, r.X, r.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);
            else
                RenderBar(t, r, owner);
            t.Geometry = r.ToString() + geo;
            ShowWindow(t.Handle, 4 /*SW_SHOWNOACTIVATE*/);
            // the taskbar is topmost: keep the skin in the topmost band too (the hover glow re-asserts itself above it)
            Native.SetWindowPos(t.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        static string RelativeKey(Rectangle all, params Rectangle[] rs)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Rectangle r in rs) sb.Append(r.IsEmpty ? "|-" : "|" + (r.X - all.X) + "," + (r.Y - all.Y) + "," + r.Width + "," + r.Height);
            return sb.ToString();
        }

        static Color A(Color c, double a) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)a)), c); }

        public string Style = "gradient";   // gradient | aero | frost | aurora | neon

        void Render(TintWindow t, Rectangle all, Rectangle strip, Rectangle header, Rectangle nav, Rectangle view, bool round)
        {
            double s = Math.Max(0.15, Math.Min(1.0, Strength)) * 110;   // peak alpha of the tint
            Func<Rectangle, RectangleF> rel = delegate (Rectangle r) { return r.IsEmpty ? RectangleF.Empty : new RectangleF(r.X - all.X, r.Y - all.Y, r.Width, r.Height); };
            RectangleF T = rel(strip), H = rel(header), N = rel(nav), V = rel(view);
            float contentBottom = !V.IsEmpty ? V.Bottom : (!N.IsEmpty ? N.Bottom : -1);
            RectangleF S = (contentBottom > 0 && contentBottom < all.Height - 4) ? new RectangleF(0, contentBottom, all.Width, all.Height - contentBottom) : RectangleF.Empty;
            using (Bitmap bmp = new Bitmap(all.Width, all.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                    if (round) using (GraphicsPath clip = RoundRect(new RectangleF(0, 0, all.Width, all.Height), 8)) g.SetClip(clip);   // Windows 11 window corners
                    DrawStyle(g, Style, C1, C2, new Rectangle(0, 0, all.Width, all.Height), T, H, N, V, S, s);
                }
                if (Style == "frost" || Style == "aero") Grain(bmp, Style == "frost" ? 14 : 6);
                Layered.Push(t.Handle, bmp, all.X, all.Y);
            }
        }

        // the taskbar skin: the chosen style over the whole bar, plus a neon edge with a glow running into the bar
        void RenderBar(TintWindow t, Rectangle r, IntPtr owner)
        {
            double s = Math.Max(0.15, Math.Min(1.0, BarStrength)) * 110;
            List<BarItem> items = null; lock (barItems) barItems.TryGetValue(owner, out items);
            using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                    if (BarDesign != "none" && items != null)
                    {
                        BarDesigns.Draw(g, BarDesign, r.Width, r.Height, r.Location, items, BarC1, BarC2);
                        g.Dispose(); Layered.Push(t.Handle, bmp, r.X, r.Y); return;
                    }
                    RectangleF whole = new RectangleF(0, 0, r.Width, r.Height);
                    if (items != null && items.Count > 0)   // strong color in the empty parts of the bar, the style on top
                    {
                        using (Region gaps = new Region(new Rectangle(0, 0, r.Width, r.Height)))
                        {
                            foreach (BarItem i in items) gaps.Exclude(new Rectangle(i.R.X - r.X, i.R.Y - r.Y, i.R.Width, i.R.Height));
                            Region keep = g.Clip; g.Clip = gaps;
                            Color d1 = Color.FromArgb(BarC1.R * 2 / 5, BarC1.G * 2 / 5, BarC1.B * 2 / 5), d2 = Color.FromArgb(BarC2.R * 2 / 5, BarC2.G * 2 / 5, BarC2.B * 2 / 5);
                            Fill(g, whole, A(d1, 70 + s * 1.5), A(d2, 70 + s * 1.5), 0f, null, null);
                            g.Clip = keep;
                        }
                    }
                    Color k1 = C1, k2 = C2; C1 = BarC1; C2 = BarC2;   // the Draw* helpers read C1/C2
                    try
                    {
                        if (BarStyle == "aero") DrawStyle(g, "aero", BarC1, BarC2, new Rectangle(0, 0, r.Width, r.Height), whole, RectangleF.Empty, RectangleF.Empty, RectangleF.Empty, RectangleF.Empty, s);
                        else if (BarStyle == "frost") DrawFrost(g, RectangleF.Empty, whole, RectangleF.Empty, RectangleF.Empty, RectangleF.Empty, s);
                        else if (BarStyle == "aurora") DrawAurora(g, new Rectangle(0, 0, r.Width, r.Height), RectangleF.Empty, RectangleF.Empty, s);
                        else if (BarStyle == "neon") Fill(g, whole, A(BarC1, s * 0.3), A(BarC2, s * 0.3), 0f, null, null);
                        else Fill(g, whole, BarC1, BarC2, 0f, new float[] { 0f, 0.5f, 1f }, new Color[] { A(BarC1, s * 1.2), A(Color.FromArgb((BarC1.R + BarC2.R) / 2, (BarC1.G + BarC2.G) / 2, (BarC1.B + BarC2.B) / 2), s * 0.5), A(BarC2, s * 1.2) });
                        float glow = Math.Min(14f, r.Height * 0.35f);   // neon edge on top + its glow into the bar
                        Fill(g, new RectangleF(0, 0, r.Width, glow), A(BarC1, BarStyle == "neon" ? 170 : 130), A(BarC1, 0), 90f, null, null);
                        using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-1, 0, r.Width + 2, 3), A(BarC1, 240), A(BarC2, 240), 0f)) g.FillRectangle(b, 0, 0, r.Width, 2f);
                    }
                    finally { C1 = k1; C2 = k2; }
                }
                if (BarStyle == "frost" || BarStyle == "aero") Grain(bmp, BarStyle == "frost" ? 14 : 6);
                Layered.Push(t.Handle, bmp, r.X, r.Y);
            }
        }

        void DrawStyle(Graphics g, string style, Color c1, Color c2, Rectangle all, RectangleF T, RectangleF H, RectangleF N, RectangleF V, RectangleF S, double s)
        {
            if (style == "aero") DrawAero(g, all, T, H, N, V, S, s);
            else if (style == "frost") DrawFrost(g, T, H, N, V, S, s);
            else if (style == "aurora") DrawAurora(g, all, N, V, s);
            else if (style == "neon") DrawNeon(g, T, H, N, V, S, s);
            else DrawGradient(g, T, H, N, V, S, s);
        }

        static void Fill(Graphics g, RectangleF r, Color a, Color b, float angle, float[] pos, Color[] cols)
        {
            if (r.IsEmpty || r.Width < 1 || r.Height < 1) return;
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y - 1, r.Width + 2, r.Height + 2), a, b, angle))
            {
                if (pos != null) { ColorBlend cb = new ColorBlend(); cb.Positions = pos; cb.Colors = cols; br.InterpolationColors = cb; }
                g.FillRectangle(br, r);
            }
        }

        // neon line between the navigation pane and the items: a 3 px core centered in the gap, with a soft glow either side
        void Divider(Graphics g, RectangleF n, RectangleF v, int alpha)
        {
            if (n.IsEmpty || v.IsEmpty) return;
            float x = (n.Right + v.Left) / 2f;
            RectangleF bounds = new RectangleF(x - 6, v.Y - 1, 12, v.Height + 2);
            for (int i = 3; i >= 1; i--)
                using (LinearGradientBrush b = new LinearGradientBrush(bounds, A(C1, alpha / (i * 3)), A(C2, alpha / (i * 3)), 90f))
                    g.FillRectangle(b, x - 1.5f - i * 1.5f, v.Y, 3f + i * 3f, v.Height);
            using (LinearGradientBrush b = new LinearGradientBrush(bounds, A(C1, alpha), A(C2, alpha), 90f))
                g.FillRectangle(b, x - 1.5f, v.Y, 3f, v.Height);
        }

        // 1. preset color fading into black, a different direction per pane
        void DrawGradient(Graphics g, RectangleF T, RectangleF H, RectangleF N, RectangleF V, RectangleF S, double s)
        {
            Fill(g, T, A(C2, s * 1.0), A(C1, s * 0.55), 0f, null, null);   // tab strip: its own sweep, the other way round
            Fill(g, H, C1, C2, 0f, new float[] { 0f, 0.5f, 1f }, new Color[] { A(C1, s * 0.8), A(Color.Black, 0), A(C2, s * 0.8) });
            Fill(g, N, A(C2, s), A(C1, s * 0.35), 90f, null, null);
            Fill(g, V, C1, C2, 35f, new float[] { 0f, 0.42f, 0.58f, 1f }, new Color[] { A(C1, s * 0.85), A(C1, 0), A(C2, 0), A(C2, s * 0.75) });
            Fill(g, S, A(C1, s * 0.9), A(C2, s * 0.9), 0f, null, null);
            Divider(g, N, V, 200);
        }

        // 2. Windows 7 Aero Glass: even color wash, glossy top highlight and diagonal light streaks
        void DrawAero(Graphics g, Rectangle all, RectangleF T, RectangleF H, RectangleF N, RectangleF V, RectangleF S, double s)
        {
            Fill(g, new RectangleF(0, 0, all.Width, all.Height), A(C1, s * 0.75), A(C2, s * 0.6), 25f, null, null);
            RectangleF top = !T.IsEmpty ? T : H;   // glossy highlight on the topmost band
            if (!top.IsEmpty) Fill(g, new RectangleF(top.X, top.Y, top.Width, top.Height * 0.55f), A(Color.White, 48), A(Color.White, 6), 90f, null, null);
            if (!T.IsEmpty && !H.IsEmpty) using (Pen p = new Pen(A(Color.White, 45), 1f)) g.DrawLine(p, T.X, T.Bottom, T.Right, T.Bottom);
            float h = all.Height;
            float[][] bands = { new float[] { 0.10f, 0.07f, 26 }, new float[] { 0.24f, 0.03f, 18 }, new float[] { 0.55f, 0.11f, 20 }, new float[] { 0.71f, 0.04f, 14 } };
            foreach (float[] bd in bands)   // the classic sheen: soft white bands running up-right
            {
                float x = all.Width * bd[0], w = all.Width * bd[1];
                PointF[] poly = { new PointF(x, h), new PointF(x + w, h), new PointF(x + w + h * 0.6f, 0), new PointF(x + h * 0.6f, 0) };
                using (GraphicsPath gp = new GraphicsPath())
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(x, 0, w + h * 0.6f + 1, h), A(Color.White, 0), A(Color.White, 0), 0f))
                {
                    ColorBlend cb = new ColorBlend(); cb.Positions = new float[] { 0f, 0.5f, 1f };
                    cb.Colors = new Color[] { A(Color.White, 0), A(Color.White, bd[2]), A(Color.White, 0) }; b.InterpolationColors = cb;
                    gp.AddPolygon(poly); g.FillPath(b, gp);
                }
            }
            using (Pen p = new Pen(A(Color.White, 60), 1f))
            {
                if (!H.IsEmpty) g.DrawLine(p, H.X, H.Bottom - 1, H.Right, H.Bottom - 1);
                if (!S.IsEmpty) g.DrawLine(p, S.X, S.Y, S.Right, S.Y);
            }
            Divider(g, N, V, 150);
        }

        // 3. glassmorphism: frosted rounded glass cards per pane with a soft light edge
        void DrawFrost(Graphics g, RectangleF T, RectangleF H, RectangleF N, RectangleF V, RectangleF S, double s)
        {
            foreach (RectangleF r in new RectangleF[] { T, H, N, V, S })
            {
                if (r.IsEmpty || r.Width < 8 || r.Height < 8) continue;
                RectangleF c = RectangleF.Inflate(r, -3, -3);
                using (GraphicsPath gp = RoundRect(c, Math.Min(14f, c.Height / 2)))
                {
                    using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(c, 1, 1), A(Color.White, s * 0.30), A(C1, s * 0.55), 60f)) g.FillPath(b, gp);
                    using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(c, 1, 1), A(C2, 0), A(C2, s * 0.55), 90f)) g.FillPath(b, gp);
                    using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(c, 1, 1), A(Color.White, 90), A(Color.White, 12), 60f))
                    using (Pen p = new Pen(b, 1.2f)) g.DrawPath(p, gp);
                }
            }
        }

        // 4. aurora: big soft color clouds drifting over the panes
        void DrawAurora(Graphics g, Rectangle all, RectangleF N, RectangleF V, double s)
        {
            float W = all.Width, Ht = all.Height;
            Blob(g, new RectangleF(-W * 0.15f, -Ht * 0.35f, W * 0.7f, Ht * 0.9f), C1, s * 1.15);
            Blob(g, new RectangleF(W * 0.45f, Ht * 0.35f, W * 0.75f, Ht * 0.95f), C2, s * 1.05);
            Color mid = Color.FromArgb((C1.R + C2.R) / 2, (C1.G + C2.G) / 2, (C1.B + C2.B) / 2);
            Blob(g, new RectangleF(W * 0.30f, -Ht * 0.25f, W * 0.45f, Ht * 0.6f), mid, s * 0.7);
            Divider(g, N, V, 120);
        }

        static void Blob(Graphics g, RectangleF r, Color c, double a)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(r);
                using (PathGradientBrush b = new PathGradientBrush(gp))
                {
                    b.CenterColor = A(c, a); b.SurroundColors = new Color[] { A(c, 0) };
                    b.FocusScales = new PointF(0.15f, 0.15f);
                    g.FillPath(b, gp);
                }
            }
        }

        // 5. neon edges: nearly black panes outlined by a glowing accent -> accent2 frame
        void DrawNeon(Graphics g, RectangleF T, RectangleF H, RectangleF N, RectangleF V, RectangleF S, double s)
        {
            foreach (RectangleF r in new RectangleF[] { T, H, N, V, S }) Fill(g, r, A(C1, s * 0.22), A(C2, s * 0.22), 45f, null, null);
            // the two frames meet on one seam halfway across the gap between navigation and items, instead of each
            // being inset on its own (which left a wide dead strip between them)
            RectangleF nf = N, vf = V;
            if (!N.IsEmpty && !V.IsEmpty)
            {
                float seam = (N.Right + V.Left) / 2f;
                nf = RectangleF.FromLTRB(N.Left, N.Top, seam - 1.5f, N.Bottom);
                vf = RectangleF.FromLTRB(seam + 1.5f, V.Top, V.Right, V.Bottom);
            }
            foreach (RectangleF r in new RectangleF[] { nf, vf })
            {
                if (r.IsEmpty || r.Width < 12 || r.Height < 12) continue;
                RectangleF c = RectangleF.FromLTRB(r.Left + (r == vf && !N.IsEmpty ? 0 : 2), r.Top + 2, r.Right - (r == nf && !V.IsEmpty ? 0 : 2), r.Bottom - 2);
                for (int i = 5; i >= 1; i--)
                    using (GraphicsPath gp = RoundRect(RectangleF.Inflate(c, i - 1, i - 1), 10 + i))
                    using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(c, 6, 6), A(C1, 150.0 / (i + 1)), A(C2, 150.0 / (i + 1)), 45f))
                    using (Pen p = new Pen(b, 2f)) g.DrawPath(p, gp);
                using (GraphicsPath gp = RoundRect(c, 10))
                using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(c, 1, 1), A(C1, 230), A(C2, 230), 45f))
                using (Pen p = new Pen(b, 2.4f)) g.DrawPath(p, gp);
            }
        }

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }

        // frosted-glass grain: tiny random light/dark specks, only where the tint is drawn
        static void Grain(Bitmap bmp, int amount)
        {
            BitmapData d = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int n = d.Stride * d.Height; byte[] px = new byte[n];
                Marshal.Copy(d.Scan0, px, 0, n);
                Random rnd = new Random(7);
                for (int i = 0; i < n; i += 4)
                {
                    if (px[i + 3] == 0) continue;
                    int v = rnd.Next(-amount, amount + 1), tgt = v > 0 ? 255 : 0, k = Math.Abs(v);
                    px[i] = (byte)(px[i] + (tgt - px[i]) * k / 64); px[i + 1] = (byte)(px[i + 1] + (tgt - px[i + 1]) * k / 64); px[i + 2] = (byte)(px[i + 2] + (tgt - px[i + 2]) * k / 64);
                    px[i + 3] = (byte)Math.Min(255, px[i + 3] + k / 2);
                }
                Marshal.Copy(px, 0, d.Scan0, n);
            }
            finally { bmp.UnlockBits(d); }
        }
    }

    // ------------------------------------------------------------------ taskbar designs: per-section shapes drawn over the real taskbar
    // Each design styles the sections Windows exposes through UI Automation: Start / Search / Task View, the app buttons
    // (running ones get a marker), "Show hidden icons" + tray icons, network/volume, the clock and Show Desktop.
    // The layer sits above the icons, so heavy color only goes into the empty gaps between sections; over the
    // buttons themselves there are only frames, outlines and light glass, which keeps every icon crisp.
    static class BarDesigns
    {
        public static readonly string[] Names = { "cyberhud", "islands", "neondock", "synthwave", "terminal", "sharmaji" };

        static Color A(Color c, double a) { return Color.FromArgb(Math.Max(0, Math.Min(255, (int)a)), c); }
        static Color Mix(Color a, Color b, double t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); float d = Math.Max(1f, Math.Min(rad * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure();
            return p;
        }

        // HUD panel: top-left and bottom-right corners cut off
        static GraphicsPath Chamfer(RectangleF r, float c)
        {
            GraphicsPath p = new GraphicsPath();
            p.AddPolygon(new PointF[] { new PointF(r.X + c, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, r.Y + c) });
            return p;
        }

        static RectangleF Group(List<ExplorerTint.BarItem> items, Point origin, float padX, float padY, int h, params string[] kinds)
        {
            RectangleF u = RectangleF.Empty;
            foreach (ExplorerTint.BarItem i in items)
                if (Array.IndexOf(kinds, i.Kind) >= 0)
                {
                    RectangleF r = new RectangleF(i.R.X - origin.X, i.R.Y - origin.Y, i.R.Width, i.R.Height);
                    u = u.IsEmpty ? r : RectangleF.Union(u, r);
                }
            if (u.IsEmpty) return u;
            return RectangleF.FromLTRB(u.Left - padX, Math.Max(padY, u.Top + padY), u.Right + padX, Math.Min(h - padY, u.Bottom - padY));
        }

        static void Glow(Graphics g, GraphicsPath p, RectangleF b, Color c1, Color c2, float width, int rings, int peak)
        {
            RectangleF gb = RectangleF.Inflate(b, 12, 12);
            for (int i = rings; i >= 1; i--)
                using (LinearGradientBrush br = new LinearGradientBrush(gb, A(c1, peak / (i + 1.0)), A(c2, peak / (i + 1.0)), 0f))
                using (Pen pen = new Pen(br, width + i * 2.6f)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
            using (LinearGradientBrush br = new LinearGradientBrush(gb, c1, c2, 0f))
            using (Pen pen = new Pen(br, width)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, p); }
        }

        static void FillPath(Graphics g, GraphicsPath p, RectangleF b, Color top, Color bottom)
        {
            using (LinearGradientBrush br = new LinearGradientBrush(RectangleF.Inflate(b, 1, 1), top, bottom, 90f)) g.FillPath(br, p);
        }

        static void Brackets(Graphics g, RectangleF r, float len, Color c1, Color c2, float w)
        {
            using (LinearGradientBrush br = new LinearGradientBrush(RectangleF.Inflate(r, 2, 2), c1, c2, 0f))
            using (Pen p = new Pen(br, w))
            {
                p.StartCap = LineCap.Square; p.EndCap = LineCap.Square;
                g.DrawLines(p, new PointF[] { new PointF(r.Left, r.Top + len), new PointF(r.Left, r.Top), new PointF(r.Left + len, r.Top) });
                g.DrawLines(p, new PointF[] { new PointF(r.Right - len, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Top + len) });
                g.DrawLines(p, new PointF[] { new PointF(r.Left, r.Bottom - len), new PointF(r.Left, r.Bottom), new PointF(r.Left + len, r.Bottom) });
                g.DrawLines(p, new PointF[] { new PointF(r.Right - len, r.Bottom), new PointF(r.Right, r.Bottom), new PointF(r.Right, r.Bottom - len) });
            }
        }

        // terminal style [ ] around a section
        static void SquareBrackets(Graphics g, RectangleF r, Color c, float w)
        {
            float s = 6;
            using (Pen glow = new Pen(A(c, 70), w + 4)) using (Pen p = new Pen(c, w))
                foreach (Pen pen in new Pen[] { glow, p })
                {
                    g.DrawLines(pen, new PointF[] { new PointF(r.Left + s, r.Top), new PointF(r.Left, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Left + s, r.Bottom) });
                    g.DrawLines(pen, new PointF[] { new PointF(r.Right - s, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Right - s, r.Bottom) });
                }
        }

        static void Scanlines(Graphics g, int w, int h, Color c, int alpha, int step)
        {
            using (Pen p = new Pen(A(c, alpha), 1f)) for (int y = 1; y < h; y += step) g.DrawLine(p, 0, y, w, y);
        }

        static void SoftDot(Graphics g, float cx, float cy, float r, Color c)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(cx - r * 3, cy - r * 3, r * 6, r * 6);
                using (PathGradientBrush b = new PathGradientBrush(gp)) { b.CenterColor = A(c, 150); b.SurroundColors = new Color[] { A(c, 0) }; g.FillPath(b, gp); }
            }
            using (SolidBrush b = new SolidBrush(c)) g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
        }

        static void Bar(Graphics g, float x, float y, float w, float hh, Color c1, Color c2)
        {
            RectangleF r = new RectangleF(x, y, w, hh);
            using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(r, 6, 6), A(c1, 90), A(c2, 90), 0f)) g.FillRectangle(b, RectangleF.Inflate(r, 3, 2.5f));
            using (LinearGradientBrush b = new LinearGradientBrush(RectangleF.Inflate(r, 1, 1), c1, c2, 0f)) g.FillRectangle(b, r);
        }

        // running apps: marker under the icon
        static void RunMarks(Graphics g, List<ExplorerTint.BarItem> items, Point o, int h, string kind, Color c1, Color c2)
        {
            foreach (ExplorerTint.BarItem i in items)
            {
                if (i.Kind != "app" || !i.Running) continue;
                float cx = i.R.X - o.X + i.R.Width / 2f;
                if (kind == "dot") SoftDot(g, cx, h - 6.5f, 2.6f, c1);
                else if (kind == "block") using (SolidBrush b = new SolidBrush(c1)) g.FillRectangle(b, cx - 6, h - 8, 12, 4);
                else Bar(g, cx - 10, h - 6, 20, 3, c1, c2);
            }
        }

        // the empty parts of the bar: everything except the buttons
        static Region Gaps(List<ExplorerTint.BarItem> items, Point o, int w, int h)
        {
            Region r = new Region(new Rectangle(0, 0, w, h));
            foreach (ExplorerTint.BarItem i in items) r.Exclude(new Rectangle(i.R.X - o.X, i.R.Y - o.Y, i.R.Width, i.R.Height));
            return r;
        }

        static void FillGaps(Graphics g, Region gaps, int w, int h, Color left, Color right, int alpha)
        {
            Region keep = g.Clip; g.Clip = gaps;
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(-1, 0, w + 2, h), A(left, alpha), A(right, alpha), 0f)) g.FillRectangle(b, 0, 0, w, h);
            g.Clip = keep;
        }

        static void TopEdge(Graphics g, int w, Color[] cols, float thick, Color glowCol)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(0, 0, w, 12), A(glowCol, 120), A(glowCol, 0), 90f)) g.FillRectangle(b, 0, 0, w, 12);
            using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-1, 0, w + 2, thick + 1), cols[0], cols[cols.Length - 1], 0f))
            {
                if (cols.Length > 2) { ColorBlend cb = new ColorBlend(); cb.Colors = cols; float[] pos = new float[cols.Length]; for (int k = 0; k < pos.Length; k++) pos[k] = k / (float)(pos.Length - 1); cb.Positions = pos; b.InterpolationColors = cb; }
                g.FillRectangle(b, 0, 0, w, thick);
            }
        }

        public static void Draw(Graphics g, string design, int w, int h, Point o, List<ExplorerTint.BarItem> items, Color c1, Color c2)
        {
            if (design == "cyberhud") CyberHud(g, w, h, o, items, c1, c2);
            else if (design == "islands") Islands(g, w, h, o, items, c1, c2);
            else if (design == "neondock") NeonDock(g, w, h, o, items, c1, c2);
            else if (design == "synthwave") Synthwave(g, w, h, o, items);
            else if (design == "terminal") Terminal(g, w, h, o, items);
            else if (design == "sharmaji") SharmaJi(g, w, h, o, items, c1, c2);
        }

        static readonly string[] LEFT = { "start", "search", "taskview", "widgets" }, APPS = { "app" }, TRAY = { "overflow", "tray" }, SYS = { "sys" }, CLOCK = { "clock" }, RIGHT = { "overflow", "tray", "sys" };

        // 1. Cyber HUD: cut-corner panels, bracketed app zone, scanlines, neon run bars
        static void CyberHud(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it, Color c1, Color c2)
        {
            using (Region gaps = Gaps(it, o, w, h)) FillGaps(g, gaps, w, h, Mix(c1, Color.Black, 0.8), Mix(c2, Color.Black, 0.8), 215);
            Scanlines(g, w, h, c1, 20, 3);
            TopEdge(g, w, new Color[] { c1, c2 }, 2f, c1);
            RectangleF l = Group(it, o, 4, 4, h, LEFT), a = Group(it, o, 8, 5, h, APPS), r = Group(it, o, 5, 5, h, RIGHT), c = Group(it, o, 6, 4, h, CLOCK);
            foreach (RectangleF p in new RectangleF[] { l, r })
                if (!p.IsEmpty) using (GraphicsPath gp = Chamfer(p, 9)) { FillPath(g, gp, p, A(p == l ? c1 : c2, 55), A(p == l ? c1 : c2, 15)); Glow(g, gp, p, p == l ? c1 : c2, p == l ? c1 : c2, 1.8f, 3, 150); }
            if (!a.IsEmpty)
            {
                using (Pen dash = new Pen(A(c2, 130), 1f)) { dash.DashPattern = new float[] { 3, 4 }; g.DrawLine(dash, a.Left + 16, a.Top, a.Right - 16, a.Top); g.DrawLine(dash, a.Left + 16, a.Bottom, a.Right - 16, a.Bottom); }
                Brackets(g, a, 12, c1, c2, 3f);
            }
            if (!c.IsEmpty)
            {
                using (GraphicsPath gp = Chamfer(c, 8)) { FillPath(g, gp, c, A(c1, 45), A(c2, 20)); Glow(g, gp, c, c1, c2, 2f, 2, 130); }
                using (GraphicsPath gp = Chamfer(RectangleF.Inflate(c, -3, -3), 6)) using (Pen p = new Pen(A(c2, 170), 1f)) g.DrawPath(p, gp);
                using (Pen p = new Pen(c1, 1.5f)) for (int k = 0; k < 3; k++) g.DrawLine(p, c.Left - 6 - k * 4, c.Top + 6, c.Left - 6 - k * 4, c.Bottom - 6);
            }
            RunMarks(g, it, o, h, "bar", c1, c2);
        }

        // 2. Floating Islands: three rounded glass islands, glowing dots under running apps
        static void Islands(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it, Color c1, Color c2)
        {
            using (Region gaps = Gaps(it, o, w, h)) FillGaps(g, gaps, w, h, Mix(c1, Color.Black, 0.82), Mix(c2, Color.Black, 0.82), 205);
            RectangleF l = Group(it, o, 6, 5, h, LEFT), a = Group(it, o, 10, 5, h, APPS), r = Group(it, o, 8, 5, h, "overflow", "tray", "sys", "clock");
            foreach (RectangleF p in new RectangleF[] { l, a, r })
            {
                if (p.IsEmpty) continue;
                using (GraphicsPath gp = Round(p, p.Height / 2f))
                {
                    FillPath(g, gp, p, A(Color.White, 40), A(c1, 45));
                    FillPath(g, gp, p, A(c2, 0), A(c2, 45));
                    Glow(g, gp, p, c1, c2, 1.8f, 3, 110);
                }
                using (GraphicsPath hi = Round(RectangleF.Inflate(p, -1.5f, -1.5f), p.Height / 2f - 1.5f))
                using (LinearGradientBrush b = new LinearGradientBrush(p, A(Color.White, 120), A(Color.White, 0), 90f)) using (Pen pen = new Pen(b, 1f)) g.DrawPath(pen, hi);
            }
            RunMarks(g, it, o, h, "dot", c1, c2);
        }

        // 3. Neon Dock: glowing Start orb, thick neon dock behind the apps, capsules on the right
        static void NeonDock(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it, Color c1, Color c2)
        {
            using (Region gaps = Gaps(it, o, w, h)) FillGaps(g, gaps, w, h, Mix(c1, Color.Black, 0.9), Mix(c2, Color.Black, 0.9), 225);
            TopEdge(g, w, new Color[] { A(c1, 0), c1, c2, A(c2, 0) }, 1.5f, c2);
            RectangleF s = Group(it, o, 0, 0, h, "start");
            if (!s.IsEmpty)
            {
                float cx = s.X + s.Width / 2, cy = h / 2f, rr = Math.Min(h / 2f - 4, 19);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(cx - rr, cy - rr, rr * 2, rr * 2);
                    using (PathGradientBrush b = new PathGradientBrush(gp)) { b.CenterColor = A(c1, 25); b.SurroundColors = new Color[] { A(c1, 95) }; g.FillPath(b, gp); }
                    Glow(g, gp, new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2), c1, c2, 2.4f, 4, 190);
                }
            }
            RectangleF ts = Group(it, o, 3, 7, h, "search", "taskview", "widgets");
            if (!ts.IsEmpty) using (GraphicsPath gp = Round(ts, ts.Height / 2)) using (Pen p = new Pen(A(c1, 170), 1.3f)) g.DrawPath(p, gp);
            RectangleF a = Group(it, o, 12, 3, h, APPS);
            if (!a.IsEmpty) using (GraphicsPath gp = Round(a, 13)) { FillPath(g, gp, a, A(c1, 50), A(c2, 50)); Glow(g, gp, a, c1, c2, 2.6f, 5, 180); }
            RectangleF r = Group(it, o, 6, 7, h, RIGHT), c = Group(it, o, 8, 6, h, CLOCK);
            if (!r.IsEmpty) using (GraphicsPath gp = Round(r, r.Height / 2)) { FillPath(g, gp, r, A(c2, 35), A(c2, 10)); Glow(g, gp, r, c2, c2, 1.6f, 2, 120); }
            if (!c.IsEmpty) using (GraphicsPath gp = Round(c, c.Height / 2)) { FillPath(g, gp, c, A(c1, 55), A(c2, 30)); Glow(g, gp, c, c1, c2, 2.2f, 3, 160); }
            RunMarks(g, it, o, h, "bar", c1, c2);
        }

        // 4. Synthwave: sunset sky in the gaps, perspective grid, striped sun on Start, chrome outlines (own retro palette)
        static void Synthwave(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it)
        {
            Color pink = Color.FromArgb(255, 43, 214), orange = Color.FromArgb(255, 138, 0), cyan = Color.FromArgb(0, 229, 255), purple = Color.FromArgb(43, 10, 61);
            using (Region gaps = Gaps(it, o, w, h))
            {
                Region keep = g.Clip; g.Clip = gaps;
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, -1, w, h + 2), A(Mix(pink, purple, 0.35), 225), A(purple, 235), 90f)) g.FillRectangle(b, 0, 0, w, h);
                float hz = h * 0.48f;
                using (Pen p = new Pen(A(cyan, 75), 1f))
                {
                    foreach (float f in new float[] { 0.62f, 0.76f, 0.88f, 0.97f }) g.DrawLine(p, 0, h * f, w, h * f);
                    float vx = w / 2f, vy = -h * 1.2f;
                    for (float x = -w; x < w * 2; x += 70) { float t = (hz - vy) / (h - vy); g.DrawLine(p, vx + (x - vx) * t, hz, x, h); }
                }
                using (LinearGradientBrush b = new LinearGradientBrush(new RectangleF(-1, hz - 1, w + 2, 3), orange, pink, 0f)) g.FillRectangle(b, 0, hz - 1, w, 2);
                g.Clip = keep;
            }
            TopEdge(g, w, new Color[] { orange, pink, cyan }, 2f, pink);
            RectangleF s = Group(it, o, 0, 0, h, "start");
            if (!s.IsEmpty)
            {
                float cx = s.X + s.Width / 2, cy = h / 2f, rr = Math.Min(h / 2f - 4, 18);
                RectangleF sb = new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2);
                using (GraphicsPath gp = new GraphicsPath())
                {
                    gp.AddEllipse(sb);
                    FillPath(g, gp, sb, A(orange, 60), A(pink, 60));
                    Glow(g, gp, sb, orange, pink, 2.6f, 3, 170);
                }
                using (Pen cut = new Pen(A(purple, 200), 1.6f)) for (int k = 1; k <= 3; k++) { float y = cy + rr * (0.25f + k * 0.2f); float dx = (float)Math.Sqrt(Math.Max(0, rr * rr - (y - cy) * (y - cy))); g.DrawLine(cut, cx - dx, y, cx + dx, y); }
            }
            RectangleF a = Group(it, o, 10, 4, h, APPS);
            if (!a.IsEmpty)
                using (GraphicsPath gp = Round(a, 9))
                {
                    RectangleF gb = RectangleF.Inflate(a, 12, 12);
                    for (int i = 3; i >= 1; i--) using (Pen p = new Pen(A(pink, 120 / (i + 1)), 2.2f + i * 2.6f)) g.DrawPath(p, gp);
                    using (LinearGradientBrush b = new LinearGradientBrush(gb, cyan, orange, 0f))
                    {
                        ColorBlend cb = new ColorBlend(); cb.Colors = new Color[] { cyan, Color.White, pink, orange }; cb.Positions = new float[] { 0f, 0.35f, 0.7f, 1f }; b.InterpolationColors = cb;
                        using (Pen p = new Pen(b, 2.2f)) g.DrawPath(p, gp);
                    }
                }
            RectangleF r = Group(it, o, 5, 7, h, RIGHT), c = Group(it, o, 8, 5, h, CLOCK);
            if (!r.IsEmpty) using (GraphicsPath gp = Round(r, 6)) using (Pen p = new Pen(A(cyan, 200), 1.5f)) g.DrawPath(p, gp);
            if (!c.IsEmpty) using (GraphicsPath gp = Round(c, c.Height / 2)) { FillPath(g, gp, c, A(orange, 70), A(pink, 70)); Glow(g, gp, c, orange, pink, 2f, 3, 160); }
            RunMarks(g, it, o, h, "bar", pink, orange);
        }

        // 5. Terminal: black + scanlines, every section in [ ], // between sections, block cursor for running apps
        static void Terminal(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it)
        {
            Color green = Color.FromArgb(0, 255, 102), dim = Color.FromArgb(6, 40, 20);
            using (Region gaps = Gaps(it, o, w, h)) FillGaps(g, gaps, w, h, dim, Color.Black, 235);
            Scanlines(g, w, h, green, 26, 3);
            using (Pen p = new Pen(A(green, 200), 1.5f)) { p.DashPattern = new float[] { 6, 3 }; g.DrawLine(p, 0, 1, w, 1); }
            List<RectangleF> groups = new List<RectangleF>();
            foreach (string[] k in new string[][] { LEFT, APPS, RIGHT, CLOCK }) { RectangleF r = Group(it, o, 6, 6, h, k); if (!r.IsEmpty) groups.Add(r); }
            foreach (RectangleF r in groups) SquareBrackets(g, r, green, 2.2f);
            using (Font f = new Font("Consolas", h * 0.3f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (SolidBrush b = new SolidBrush(A(green, 220)))
            using (StringFormat sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                for (int k = 1; k < groups.Count; k++)
                {
                    float gap = groups[k].Left - groups[k - 1].Right;
                    if (gap >= 24) g.DrawString("//", f, b, new RectangleF(groups[k - 1].Right, 0, gap, h), sf);
                }
                if (groups.Count > 0 && groups[0].Left >= 30) g.DrawString(">_", f, b, new RectangleF(0, 0, groups[0].Left, h), sf);
            }
            RunMarks(g, it, o, h, "block", green, green);
        }

        // 6. SharmaJi Special: saffron x cyan desi-cyberpunk; chakra ring on Start, HUD dock, tricolor stripe under the clock
        static void SharmaJi(Graphics g, int w, int h, Point o, List<ExplorerTint.BarItem> it, Color c1, Color c2)
        {
            Color saffron = Color.FromArgb(255, 153, 51), white = Color.FromArgb(245, 245, 245), green = Color.FromArgb(19, 168, 8), gold = Color.FromArgb(255, 209, 102);
            using (Region gaps = Gaps(it, o, w, h))
            {
                Region keep = g.Clip; g.Clip = gaps;
                using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(-1, 0, w + 2, h), Color.Black, Color.Black, 0f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[] { A(Mix(c1, Color.Black, 0.62), 230), A(Color.FromArgb(10, 8, 14), 235), A(Mix(c2, Color.Black, 0.62), 230) };
                    cb.Positions = new float[] { 0f, 0.5f, 1f }; b.InterpolationColors = cb;
                    g.FillRectangle(b, 0, 0, w, h);
                }
                using (Pen p = new Pen(A(c1, 26), 1f)) for (int x = -h; x < w; x += 9) g.DrawLine(p, x, h, x + h, 0);   // fine diagonal hatch
                g.Clip = keep;
            }
            TopEdge(g, w, new Color[] { saffron, white, green }, 2.2f, c1);
            RectangleF s = Group(it, o, 0, 0, h, "start");
            if (!s.IsEmpty)   // Ashoka-chakra style ring: 24 spokes on the outer band, around (not over) the Start icon
            {
                float cx = s.X + s.Width / 2, cy = h / 2f, rr = Math.Min(h / 2f - 3, 20);
                RectangleF rb = new RectangleF(cx - rr, cy - rr, rr * 2, rr * 2);
                using (GraphicsPath gp = new GraphicsPath()) { gp.AddEllipse(rb); Glow(g, gp, rb, c2, c1, 2.2f, 4, 190); }
                using (Pen p = new Pen(A(c2, 210), 1.2f))
                    for (int k = 0; k < 24; k++)
                    {
                        double an = k * Math.PI / 12;
                        g.DrawLine(p, cx + (float)Math.Cos(an) * (rr - 5.5f), cy + (float)Math.Sin(an) * (rr - 5.5f), cx + (float)Math.Cos(an) * (rr - 1.5f), cy + (float)Math.Sin(an) * (rr - 1.5f));
                    }
            }
            RectangleF ts = Group(it, o, 3, 7, h, "search", "taskview", "widgets");
            if (!ts.IsEmpty) using (GraphicsPath gp = Chamfer(ts, 6)) using (Pen p = new Pen(A(c1, 190), 1.4f)) g.DrawPath(p, gp);
            RectangleF a = Group(it, o, 10, 3, h, APPS);
            if (!a.IsEmpty) using (GraphicsPath gp = Chamfer(a, 11)) { FillPath(g, gp, a, A(c1, 45), A(c2, 40)); Glow(g, gp, a, c1, c2, 2.6f, 4, 185); }
            RectangleF r = Group(it, o, 6, 7, h, RIGHT), c = Group(it, o, 8, 4, h, CLOCK);
            if (!r.IsEmpty) using (GraphicsPath gp = Round(r, r.Height / 2)) { FillPath(g, gp, r, A(c2, 40), A(c2, 12)); Glow(g, gp, r, c2, c2, 1.8f, 2, 130); }
            if (!c.IsEmpty)
            {
                using (GraphicsPath gp = Round(c, 8)) { FillPath(g, gp, c, A(c1, 50), A(c1, 15)); Glow(g, gp, c, c1, c2, 2.2f, 3, 170); }
                float sw = (c.Width - 16) / 3f, sy = c.Bottom - 4.5f;
                using (SolidBrush b1 = new SolidBrush(saffron)) using (SolidBrush b2 = new SolidBrush(white)) using (SolidBrush b3 = new SolidBrush(green))
                { g.FillRectangle(b1, c.X + 8, sy, sw, 2.5f); g.FillRectangle(b2, c.X + 8 + sw, sy, sw, 2.5f); g.FillRectangle(b3, c.X + 8 + sw * 2, sy, sw, 2.5f); }
            }
            RunMarks(g, it, o, h, "bar", gold, c1);
        }
    }

    // ------------------------------------------------------------------ per-pixel alpha clock for Live Lock
    public class ClockOverlay : Form
    {
        Color accent; bool h24; bool showDate;
        System.Windows.Forms.Timer t;
        public ClockOverlay(Rectangle area, Color accent, bool h24, bool showDate)
        {
            this.accent = accent; this.h24 = h24; this.showDate = showDate;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual; TopMost = true;
            Bounds = area;
            t = new System.Windows.Forms.Timer(); t.Interval = 1000; t.Tick += delegate { Render(); }; t.Start();
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80000 /*LAYERED*/ | 0x20 /*TRANSPARENT*/ | 0x80 | 0x08000000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e) { base.OnShown(e); Render(); }

        void Render()
        {
            int w = Width, h = Height;
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAlias; g.Clear(Color.Transparent);
                DateTime now = DateTime.Now;
                string time = h24 ? now.ToString("HH:mm") : now.ToString("h:mm");
                string date = now.ToString("dddd, d MMMM");
                float big = h * 0.16f;
                using (FontFamily ff = new FontFamily("Segoe UI"))
                using (GraphicsPath path = new GraphicsPath())
                {
                    StringFormat sf = new StringFormat(); sf.Alignment = StringAlignment.Center;
                    path.AddString(time, ff, (int)FontStyle.Bold, big, new PointF(w / 2f, h * 0.18f), sf);
                    if (showDate) path.AddString(date, ff, (int)FontStyle.Regular, big * 0.22f, new PointF(w / 2f, h * 0.18f + big * 1.12f), sf);
                    for (int i = 6; i >= 1; i--)
                        using (Pen glow = new Pen(Color.FromArgb(18, accent), i * 4)) { glow.LineJoin = LineJoin.Round; g.DrawPath(glow, path); }
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(240, 255, 255, 255))) g.FillPath(b, path);
                }
                using (Font f = new Font("Segoe UI", Math.Max(10, h * 0.014f)))
                using (SolidBrush hint = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                {
                    StringFormat sf2 = new StringFormat(); sf2.Alignment = StringAlignment.Center;
                    g.DrawString("Press any key or click to sign in", f, hint, new RectangleF(0, h * 0.9f, w, h * 0.05f), sf2);
                }
                Layered.Push(Handle, bmp, Left, Top);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e) { t.Stop(); t.Dispose(); base.OnFormClosed(e); }
    }

    // ------------------------------------------------------------------ Live Lock: video on every screen, any input -> Windows sign-in
    public class LockScreen : VideoSurface
    {
        public event EventHandler Dismissed;
        Point firstMouse = new Point(-1, -1);
        bool done;
        public ClockOverlay Clock;

        public LockScreen(Screen s, string file, string fit) : base(s, file, fit, 0)
        {
            TopMost = true; KeyPreview = true;
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle &= ~0x08000000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return false; } }
        protected override void OnKeyDown(KeyEventArgs e) { Dismiss(); }
        protected override void OnMouseDown(MouseEventArgs e) { Dismiss(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (firstMouse.X < 0) { firstMouse = e.Location; return; }
            if (Math.Abs(e.X - firstMouse.X) + Math.Abs(e.Y - firstMouse.Y) > 40) Dismiss();
        }
        void Dismiss() { if (done) return; done = true; if (Dismissed != null) Dismissed(this, EventArgs.Empty); }
    }

    // ------------------------------------------------------------------ the engine (hidden message window + tray)
    public class Engine : Form
    {
        string cfgPath;
        Dictionary<string, object> cfg = new Dictionary<string, object>();
        List<VideoSurface> desk = new List<VideoSurface>();
        List<LockScreen> locks = new List<LockScreen>();
        System.Windows.Forms.Timer tick, slideTimer, reloadDebounce;
        FileSystemWatcher watcher;
        NotifyIcon tray;
        ToolStripMenuItem miPause;
        uint wmTaskbarCreated;
        bool userPaused, sessionLocked;
        int hotkeyId = 0x7C01;
        List<string> slideFiles = new List<string>(); int slideIdx = -1; Random rnd = new Random();
        EventWaitHandle evExit, evLock, evReload;
        DateTime lastLockEnd = DateTime.MinValue;
        ClickFx fx;
        TaskbarHover hover;
        ExplorerTint tint;
        DriveBars drives;
        ShellServer shellSrv; ShellHost shell; WebBackground webBg;
        int shellHotkeyId = 0x7C02; string suspendedFor;
        int tickCount;

        public Engine(string cfgPath)
        {
            this.cfgPath = cfgPath;
            ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; Opacity = 0; Size = new Size(1, 1);
            IntPtr force = Handle; // create the window now
            wmTaskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");

            evExit = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TRANSCRYPTO_TCWall_Exit");
            evLock = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TRANSCRYPTO_TCWall_LockNow");
            evReload = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\TRANSCRYPTO_TCWall_Reload");
            ThreadPool.RegisterWaitForSingleObject(evExit, delegate { BeginInvoke(new MethodInvoker(Quit)); }, null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(evLock, delegate { BeginInvoke(new MethodInvoker(delegate { ShowLock(true); })); }, null, -1, false);
            ThreadPool.RegisterWaitForSingleObject(evReload, delegate { BeginInvoke(new MethodInvoker(LoadAndApply)); }, null, -1, false);

            tick = new System.Windows.Forms.Timer(); tick.Interval = 1000; tick.Tick += delegate { Tick(); }; tick.Start();
            slideTimer = new System.Windows.Forms.Timer(); slideTimer.Tick += delegate { NextSlide(); };
            reloadDebounce = new System.Windows.Forms.Timer(); reloadDebounce.Interval = 600; reloadDebounce.Tick += delegate { reloadDebounce.Stop(); LoadAndApply(); };

            watcher = new FileSystemWatcher(Path.GetDirectoryName(cfgPath), Path.GetFileName(cfgPath));
            watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
            FileSystemEventHandler h = delegate { try { BeginInvoke(new MethodInvoker(delegate { reloadDebounce.Stop(); reloadDebounce.Start(); })); } catch { } };
            watcher.Changed += h; watcher.Created += h; watcher.Renamed += delegate { try { BeginInvoke(new MethodInvoker(delegate { reloadDebounce.Stop(); reloadDebounce.Start(); })); } catch { } };
            watcher.EnableRaisingEvents = true;

            SystemEvents.SessionSwitch += OnSessionSwitch;
            SystemEvents.DisplaySettingsChanged += delegate { BeginInvoke(new MethodInvoker(RebuildDesktop)); };
            
            BuildTray();
            fx = new ClickFx(this);
            hover = new TaskbarHover(this);
            tint = new ExplorerTint(this);
            drives = new DriveBars(this);
            string appRoot = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
            shellSrv = new ShellServer(this, appRoot);
            shell = new ShellHost(this, shellSrv);
            shell.OnExit = delegate { Program.Log("shell: exit from the bar"); Quit(); };
            shellSrv.OnAction = delegate (string a, Dictionary<string, string> q) { return shell.Action(a, q); };
            shellSrv.OnTray = delegate (Dictionary<string, string> q) { return shell.Tray(q); };
            shellSrv.Extra = delegate { Dictionary<string, object> x = new Dictionary<string, object>(); Dictionary<string, object> sh = Cfg.Dict(cfg, "shell");
                object pr; x["projects"] = sh.TryGetValue("projects", out pr) ? pr : null; x["palette"] = Cfg.Str(sh, "palette", ""); return x; };
            webBg = new WebBackground(shellSrv, FindWallpaperHost);
            shell.OnBgMetrics = delegate (int vw, int vh) { webBg.Metrics(vw, vh); };
            try { LegacyChrome.Cleanup(FindWallpaperHost()); } catch (Exception ex) { Program.Log("old Chrome cleanup: " + ex.Message); }
            // never leave the user without a taskbar: restore it if the engine dies
            AppDomain.CurrentDomain.UnhandledException += delegate { try { shell.RestoreTaskbar(); } catch { } };
            Native.RegisterHotKey(Handle, shellHotkeyId, 0x7 /*CTRL+ALT+SHIFT*/, 0x54 /*T*/);
            LoadAndApply();
        }

        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        void BuildTray()
        {
            tray = new NotifyIcon();
            try { tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { tray.Icon = SystemIcons.Application; }
            tray.Text = "TRANSCRYPTO engine";
            ContextMenuStrip m = new ContextMenuStrip();
            miPause = new ToolStripMenuItem("Pause live wallpaper", null, delegate { userPaused = !userPaused; miPause.Text = userPaused ? "Resume live wallpaper" : "Pause live wallpaper"; Tick(); });
            m.Items.Add(miPause);
            m.Items.Add(new ToolStripMenuItem("Live Lock now", null, delegate { ShowLock(true); }));
            m.Items.Add(new ToolStripMenuItem("Open TRANSCRYPTO Themer", null, delegate { OpenApp(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem("Exit engine", null, delegate { Quit(); }));
            tray.ContextMenuStrip = m;
            tray.DoubleClick += delegate { OpenApp(); };
        }

        void OpenApp()
        {
            try
            {
                string root = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
                Process.Start("wscript.exe", "\"" + Path.Combine(root, "TRANSCRYPTO.vbs") + "\"");
            }
            catch (Exception ex) { Program.Log("open app: " + ex.Message); }
        }

        void LoadAndApply()
        {
            try
            {
                if (File.Exists(cfgPath))
                {
                    string txt = File.ReadAllText(cfgPath, Encoding.UTF8);
                    Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(txt) as Dictionary<string, object>;
                    if (d != null) cfg = d;
                }
            }
            catch (Exception ex) { Program.Log("config: " + ex.Message); return; }

            bool live = Cfg.Bool(Cfg.Dict(cfg, "wallpaper"), "enabled", false);
            bool slide = Cfg.Bool(Cfg.Dict(cfg, "slideshow"), "enabled", false);
            bool lck = Cfg.Bool(Cfg.Dict(cfg, "lock"), "enabled", false);
            string shellDesign = Cfg.Str(Cfg.Dict(cfg, "shell"), "design", "none"), bgDesign = Cfg.Str(Cfg.Dict(cfg, "liveBg"), "design", "none");
            if (shellDesign != suspendedFor) suspendedFor = null;
            bool shellOn = shellDesign != "none" && suspendedFor == null, bgOn = bgDesign != "none";
            bool clicks = Cfg.Bool(Cfg.Dict(cfg, "clickFx"), "enabled", false) || Cfg.Bool(Cfg.Dict(cfg, "taskbarFx"), "enabled", false) || Cfg.Bool(Cfg.Dict(cfg, "explorerFx"), "enabled", false) || Cfg.Bool(Cfg.Dict(cfg, "driveBars"), "enabled", true) || shellOn || bgOn;
            tray.Visible = Cfg.Bool(cfg, "tray", true) && (live || slide || lck || clicks);
            SetupClickFx();
            SetupTaskbarFx();
            if (shellOn) { hover.Enable(false); tint.BarOn = false; }   // the Windows taskbar is hidden while our shell runs
            shell.Apply(shellOn ? shellDesign : "none", Cfg.Str(Cfg.Dict(cfg, "shell"), "palette", ""), bgDesign, Cfg.Str(Cfg.Dict(cfg, "liveBg"), "palette", ""), Cfg.Bool(Cfg.Dict(cfg, "shell"), "autohide", false));
            SetupExplorerFx();
            SetupDriveBars();

            RebuildDesktop();
            // after the video wallpaper settled: removing a video repaints the picture, and Explorer rebuilds the layer the scene lives in
            webBg.Apply(bgDesign, Cfg.Str(Cfg.Dict(cfg, "liveBg"), "palette", ""));
            SetupSlideshow();
            SetupHotkey();
            if (!live && !slide && !lck && !clicks) BeginInvoke(new MethodInvoker(Quit));
        }

        // ---------------- desktop wallpaper
        internal static IntPtr FindWallpaperHost()
        {
            IntPtr progman = Native.FindWindow("Progman", null);
            IntPtr r;
            Native.SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), 0, 1000, out r);
            Native.SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out r);
            // Windows 11 24H2+: the WorkerW lives inside Progman, behind SHELLDLL_DefView
            IntPtr inner = Native.FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
            if (inner != IntPtr.Zero) return inner;
            // Older builds: top-level WorkerW right after the one hosting the icons
            IntPtr worker = IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr top, IntPtr l)
            {
                IntPtr shell = Native.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shell != IntPtr.Zero) worker = Native.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                return true;
            }, IntPtr.Zero);
            return worker != IntPtr.Zero ? worker : progman;
        }

        void ClearDesktop()
        {
            foreach (VideoSurface s in desk) { try { s.Close(); s.Dispose(); } catch { } }
            desk.Clear();
        }

        void RebuildDesktop()
        {
            bool hadVideo = desk.Count > 0;
            ClearDesktop();
            Dictionary<string, object> w = Cfg.Dict(cfg, "wallpaper");
            // the picture is repainted only when a video just left the desktop: repainting makes Explorer rebuild its wallpaper
            // layer, which would also tear down the live scene living there on every settings change
            if (!Cfg.Bool(w, "enabled", false)) { if (hadVideo) RepaintStaticWallpaper(); Native.TrimMemory(); return; }
            string mode = Cfg.Str(w, "mode", "same"), file = Cfg.Str(w, "file", ""), fit = Cfg.Str(w, "fit", "fill");
            float vol = (float)(Cfg.Num(w, "volume", 0) / 100.0);
            Dictionary<string, object> mons = Cfg.Dict(w, "monitors");
            IntPtr host = FindWallpaperHost();
            bool first = true;
            foreach (Screen s in Screen.AllScreens)
            {
                string f = file;
                if (mode == "per") { f = Cfg.Str(mons, s.DeviceName, ""); if (f == "") f = file; }
                else if (mode == "span" && !first) continue;
                if (string.IsNullOrEmpty(f) || !File.Exists(f)) { first = false; continue; }
                Screen target = s;
                VideoSurface v = new VideoSurface(target, f, fit, first ? vol : 0);
                if (mode == "span") v.Bounds = SystemInformation.VirtualScreen;
                IntPtr vh = v.Handle; // create without showing, then move it behind the icons
                Native.SetParent(vh, host);
                int style = Native.GetWindowLong(v.Handle, -16);
                Native.SetWindowLong(v.Handle, -16, (style | 0x40000000 /*CHILD*/) & ~unchecked((int)0x80000000) /*POPUP*/);
                Native.RECT rc = new Native.RECT(); Rectangle b = mode == "span" ? SystemInformation.VirtualScreen : s.Bounds;
                rc.Left = b.Left; rc.Top = b.Top; rc.Right = b.Right; rc.Bottom = b.Bottom;
                Native.MapWindowPoints(IntPtr.Zero, host, ref rc, 2);
                Native.SetWindowPos(vh, IntPtr.Zero, rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top, 0x0010 /*NOACTIVATE*/ | 0x0040 /*SHOWWINDOW*/);
                v.Show();
                v.Start();
                desk.Add(v);
                first = false;
            }
            Tick();
        }

        static void RepaintStaticWallpaper()
        {
            try
            {
                string wp = Convert.ToString(Registry.GetValue("HKEY_CURRENT_USER\\Control Panel\\Desktop", "Wallpaper", ""));
                Native.SystemParametersInfo(0x0014, 0, wp, 0);
            }
            catch { }
        }

        // ---------------- pause rules
        void Tick()
        {
            if (++tickCount % 20 == 0) fx.Rehook();   // keep the click hook alive (see ClickFx.Rehook)
            Dictionary<string, object> w = Cfg.Dict(cfg, "wallpaper");
            bool battery = Cfg.Bool(w, "pauseOnBattery", true) && SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
            bool pauseAll = userPaused || battery || locks.Count > 0 || (sessionLocked && Cfg.Bool(w, "pauseOnLock", true));

            HashSet<string> busyScreens = new HashSet<string>();
            if (!pauseAll && desk.Count > 0)
            {
                IntPtr fg = Native.GetForegroundWindow();
                if (Cfg.Bool(w, "pauseOnFullscreen", true) && IsFullscreenApp(fg)) busyScreens.Add(Screen.FromHandle(fg).DeviceName);
                if (Cfg.Bool(w, "pauseOnMaximized", false))
                {
                    Native.EnumWindows(delegate(IntPtr h, IntPtr l)
                    {
                        if (Native.IsWindowVisible(h) && Native.IsZoomed(h) && !Native.IsIconic(h) && !Native.IsCloaked(h) && !IsShellWindow(h))
                            busyScreens.Add(Screen.FromHandle(h).DeviceName);
                        return true;
                    }, IntPtr.Zero);
                }
            }
            foreach (VideoSurface s in desk) s.SetPaused(pauseAll || busyScreens.Contains(s.Screen.DeviceName));

            // Live Lock idle trigger
            Dictionary<string, object> lk = Cfg.Dict(cfg, "lock");
            if (Cfg.Bool(lk, "enabled", false) && locks.Count == 0 && !sessionLocked)
            {
                double mins = Cfg.Num(lk, "idleMinutes", 5);
                if (mins > 0 && Native.IdleSeconds() >= mins * 60 && (DateTime.Now - lastLockEnd).TotalSeconds > 30)
                {
                    IntPtr fg = Native.GetForegroundWindow();
                    if (!(Cfg.Bool(lk, "skipWhenFullscreen", true) && IsFullscreenApp(fg))) ShowLock(false);
                }
            }
        }

        internal static bool IsShellWindow(IntPtr h)
        {
            string c = Native.ClassOf(h);
            return c == "Progman" || c == "WorkerW" || c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd" || c == "Windows.UI.Core.CoreWindow";
        }

        public static bool IsFullscreenApp(IntPtr fg)
        {
            if (fg == IntPtr.Zero || IsShellWindow(fg)) return false;
            Native.RECT r; if (!Native.GetWindowRect(fg, out r)) return false;
            Rectangle b = Screen.FromHandle(fg).Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }

        // ---------------- slideshow
        void SetupSlideshow()
        {
            slideTimer.Stop();
            Dictionary<string, object> sl = Cfg.Dict(cfg, "slideshow");
            if (!Cfg.Bool(sl, "enabled", false)) return;
            string folder = Cfg.Str(sl, "folder", "");
            slideFiles.Clear();
            if (Directory.Exists(folder))
                foreach (string f in Directory.GetFiles(folder))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".jpg" || e == ".jpeg" || e == ".png" || e == ".bmp" || e == ".jfif" || e == ".webp") slideFiles.Add(f);
                }
            if (slideFiles.Count == 0) return;
            slideTimer.Interval = (int)Math.Max(10000, Cfg.Num(sl, "minutes", 30) * 60000);
            slideTimer.Start();
            NextSlide();
        }

        void NextSlide()
        {
            if (slideFiles.Count == 0) return;
            bool shuffle = Cfg.Bool(Cfg.Dict(cfg, "slideshow"), "shuffle", true);
            slideIdx = shuffle ? rnd.Next(slideFiles.Count) : (slideIdx + 1) % slideFiles.Count;
            Native.SystemParametersInfo(0x0014, 0, slideFiles[slideIdx], 0x03);
        }

        // ---------------- click effect
        void SetupClickFx()
        {
            Dictionary<string, object> c = Cfg.Dict(cfg, "clickFx");
            fx.SizeDip = (int)Cfg.Num(c, "size", 90);
            fx.RightClick = Cfg.Bool(c, "rightClick", true);
            fx.SkipFullscreen = Cfg.Bool(c, "skipFullscreen", true);
            fx.Style = Cfg.Str(c, "style", "ripple");
            string c1 = Cfg.Str(c, "color", "accent"), c2 = Cfg.Str(c, "color2", "accent");
            Color acc = AccentColor();
            fx.C1 = c1 == "accent" ? acc : Cfg.ParseColor(c1, acc);
            fx.C2 = c2 == "accent" ? Color.FromArgb(Math.Min(255, acc.R + 90), Math.Min(255, acc.G + 90), Math.Min(255, acc.B + 90)) : Cfg.ParseColor(c2, Color.Magenta);
            fx.Enable(Cfg.Bool(c, "enabled", false));
        }

        // ---------------- taskbar neon hover
        void SetupTaskbarFx()
        {
            Dictionary<string, object> c = Cfg.Dict(cfg, "taskbarFx");
            FxColors(c, out hover.C1, out hover.C2);
            bool on = Cfg.Bool(c, "enabled", false);
            hover.Enable(on);
            // taskbar skin: the bar itself in the preset's colors with a neon edge (same layer type as the Explorer tint)
            tint.BarOn = on && Cfg.Bool(c, "skin", true);
            tint.BarC1 = hover.C1; tint.BarC2 = hover.C2;
            tint.BarStyle = Cfg.Str(c, "style", "gradient");
            tint.BarStrength = Cfg.Num(c, "strength", 0.85);
            tint.BarDesign = Cfg.Str(c, "design", "none");
        }

        // ---------------- Explorer tint (runs after SetupTaskbarFx: one layer manager serves both)
        void SetupExplorerFx()
        {
            Dictionary<string, object> c = Cfg.Dict(cfg, "explorerFx");
            FxColors(c, out tint.C1, out tint.C2);
            tint.Style = Cfg.Str(c, "style", "gradient");
            tint.Strength = Cfg.Num(c, "strength", 0.6);
            tint.ExplorerOn = Cfg.Bool(c, "enabled", false);
            tint.Apply();
        }

        // ---------------- themed drive bars in File Explorer: the style follows the active preset (presetColors.id)
        void SetupDriveBars()
        {
            Dictionary<string, object> c = Cfg.Dict(cfg, "driveBars");
            Dictionary<string, object> pc = Cfg.Dict(cfg, "presetColors");
            FxColors(c, out drives.C1, out drives.C2);
            string style = Cfg.Str(c, "style", "preset");
            if (style == "preset") style = DriveBarStyle(Cfg.Str(pc, "id", ""), Cfg.Str(Cfg.Dict(cfg, "shell"), "design", "none"));
            drives.Style = style;
            drives.On = Cfg.Bool(c, "enabled", true);
            drives.Apply();
        }
        static string DriveBarStyle(string preset, string shellDesign)
        {
            switch (preset)
            {
                case "orbital": return "orbital";
                case "sharmaji-command-center": return "hud";
                case "projector": return "beam";
                case "transcrypto-neon": case "neon-night": case "synthwave-sunset": case "tokyo-night": case "dracula": return "neon";
                case "matrix": return "matrix";
                case "mono-paper": case "midnight-mono": case "windows-11": return "flat";
                case "": return shellDesign == "orbital" ? "orbital" : shellDesign == "sharmaji" ? "hud" : shellDesign == "projector" ? "beam" : "soft";
                default: return "soft";
            }
        }

        // effect colors: "preset" = the active preset's accent pair (presetColors, written by the app),
        // "accent" = the Windows accent, anything else = a custom #hex picked by the user
        void FxColors(Dictionary<string, object> c, out Color c1, out Color c2)
        {
            Color acc = AccentColor(), light = Color.FromArgb(Math.Min(255, acc.R + 90), Math.Min(255, acc.G + 90), Math.Min(255, acc.B + 90));
            Dictionary<string, object> pc = Cfg.Dict(cfg, "presetColors");
            Color p1 = Cfg.ParseColor(Cfg.Str(pc, "accent", ""), acc), p2 = Cfg.ParseColor(Cfg.Str(pc, "accent2", ""), light);
            string s1 = Cfg.Str(c, "color", "preset"), s2 = Cfg.Str(c, "color2", "preset");
            c1 = s1 == "accent" ? acc : s1 == "preset" ? p1 : Cfg.ParseColor(s1, p1);
            c2 = s2 == "accent" ? light : s2 == "preset" ? p2 : Cfg.ParseColor(s2, p2);
        }

        static Color AccentColor()
        {
            try
            {
                object v = Registry.GetValue("HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\DWM", "AccentColor", null);
                if (v != null) { int abgr = (int)v; return Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF); }
            }
            catch { }
            return Color.FromArgb(0, 240, 255);
        }

        // ---------------- Live Lock
        void SetupHotkey()
        {
            Native.UnregisterHotKey(Handle, hotkeyId);
            Dictionary<string, object> lk = Cfg.Dict(cfg, "lock");
            if (!Cfg.Bool(lk, "enabled", false)) return;
            string hk = Cfg.Str(lk, "hotkey", "Ctrl+Alt+L");
            uint mods = 0x4000; /*NOREPEAT*/ uint vk = 0;
            foreach (string part in hk.Split('+'))
            {
                string p = part.Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control") mods |= 2; else if (p == "alt") mods |= 1; else if (p == "shift") mods |= 4; else if (p == "win") mods |= 8;
                else { try { vk = (uint)(Keys)Enum.Parse(typeof(Keys), part.Trim(), true); } catch { } }
            }
            if (vk != 0) Native.RegisterHotKey(Handle, hotkeyId, mods, vk);
        }

        void ShowLock(bool manual)
        {
            if (locks.Count > 0) return;
            Dictionary<string, object> lk = Cfg.Dict(cfg, "lock");
            string file = Cfg.Str(lk, "file", "");
            if (string.IsNullOrEmpty(file)) file = Cfg.Str(Cfg.Dict(cfg, "wallpaper"), "file", "");
            Color accent = Cfg.ParseColor(Cfg.Str(lk, "accent", "#00f0ff"), Color.Cyan);
            bool showClock = Cfg.Bool(lk, "clock", true);
            foreach (Screen s in Screen.AllScreens)
            {
                LockScreen l = new LockScreen(s, file, "fill");
                l.Dismissed += delegate { EndLock(true); };
                l.Show();
                l.Start();
                if (showClock)
                {
                    l.Clock = new ClockOverlay(s.Bounds, accent, Cfg.Bool(lk, "clock24", false), Cfg.Bool(lk, "showDate", true));
                    l.Clock.Show(l);
                }
                locks.Add(l);
            }
            if (locks.Count > 0) { locks[0].Activate(); locks[0].Focus(); }
            Cursor.Hide();
            Tick();
        }

        void EndLock(bool signIn)
        {
            if (locks.Count == 0) return;
            foreach (LockScreen l in locks) { try { if (l.Clock != null) l.Clock.Close(); l.Close(); l.Dispose(); } catch { } }
            locks.Clear();
            Cursor.Show();
            lastLockEnd = DateTime.Now;
            if (signIn) Native.LockWorkStation();   // with NoLockScreen=1 this goes straight to the password / PIN box
            Native.TrimMemory();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock) sessionLocked = true;
            if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.SessionLogon) { sessionLocked = false; lastLockEnd = DateTime.Now; }
            try { BeginInvoke(new MethodInvoker(Tick)); } catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == hotkeyId) ShowLock(true);
            else if (m.Msg == 0x0312 && m.WParam.ToInt32() == shellHotkeyId) { suspendedFor = shell.Design; shell.Stop(); Program.Log("shell: emergency stop (Ctrl+Alt+Shift+T), Windows taskbar restored"); }
            else if (wmTaskbarCreated != 0 && m.Msg == (int)wmTaskbarCreated) BeginInvoke(new MethodInvoker(delegate { RebuildDesktop(); hover.Reset(); fx.Rehook(); })); // explorer restarted
            else if (m.Msg == 0x001A && fx != null) BeginInvoke(new MethodInvoker(SetupClickFx));                               // accent may have changed
            base.WndProc(ref m);
        }

        public void Quit()
        {
            try
            {
                tick.Stop(); slideTimer.Stop(); watcher.EnableRaisingEvents = false;
                Native.UnregisterHotKey(Handle, hotkeyId);
                if (fx != null) fx.Enable(false);
                if (hover != null) hover.Enable(false);
                if (tint != null) tint.Disable();
                if (shell != null) shell.Stop();
                if (webBg != null) webBg.Stop();
                if (shellSrv != null) shellSrv.Stop();
                EndLock(false);
                ClearDesktop();
                RepaintStaticWallpaper();
                tray.Visible = false; tray.Dispose();
                SystemEvents.SessionSwitch -= OnSessionSwitch;
            }
            catch { }
            Application.Exit();
        }
    }

    static class Program
    {
        static string logPath;
        public static void Log(string s)
        {
            try { File.AppendAllText(logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [engine] " + s + Environment.NewLine); } catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try { if (!Native.SetProcessDpiAwarenessContext(new IntPtr(-4))) Native.SetProcessDPIAware(); } catch { try { Native.SetProcessDPIAware(); } catch { } }
            // the shell pages run in WebView2: it must use the browser Windows installed and our own flags, never ones planted in the environment
            foreach (string v in new[] { "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_USER_DATA_FOLDER",
                                         "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER", "WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER" })
                Environment.SetEnvironmentVariable(v, null);
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRANSCRYPTO");
            Directory.CreateDirectory(Path.Combine(data, "logs"));
            logPath = Path.Combine(data, "logs\\transcrypto.log");
            string cfgPath = Path.Combine(data, "engine.json");

            bool logon = Array.IndexOf(args, "--logon") >= 0;
            if (logon) PlayStartupSound(cfgPath);

            bool created;
            using (Mutex mtx = new Mutex(true, "Local\\TRANSCRYPTO_TCWall", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log("ui: " + e.Exception); };
                Engine eng = new Engine(cfgPath);
                if (eng.IsDisposed) return;
                Application.Run(new ApplicationContext());
            }
        }

        static void PlayStartupSound(string cfgPath)
        {
            try
            {
                Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(cfgPath)) as Dictionary<string, object>;
                Dictionary<string, object> s = Cfg.Dict(d, "startupSound");
                string f = Cfg.Str(s, "file", "");
                if (Cfg.Bool(s, "enabled", false) && File.Exists(f))
                {
                    Thread t = new Thread(delegate() { try { using (System.Media.SoundPlayer p = new System.Media.SoundPlayer(f)) p.PlaySync(); } catch { } });
                    t.IsBackground = false; t.Start();
                }
            }
            catch { }
        }
    }
}
