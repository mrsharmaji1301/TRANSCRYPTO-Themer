// TRANSCRYPTO Themer - custom shell (taskbar + Start menu) and live web backgrounds.
//
// The designs are HTML pages (shell\*.html) rendered by Microsoft WebView2 (part of Windows 11) inside the engine's own
// windows (WebSurface.cs) - no browser process of its own. The engine turns that page into the shell:
//   * the frame window covers the whole monitor, but SetWindowRgn limits it to what the page says is visible
//     (the bar, a hover label, or the full screen while the Start menu is open), so clicks elsewhere fall
//     through to the windows underneath and nothing has to be resized while animating;
//   * the frame is a borderless topmost tool window that never takes focus, except while the Start menu is open;
//   * an AppBar reserves the space at the bottom and the Windows taskbar is hidden (restored on exit, on crash
//     and with the emergency hotkey Ctrl+Alt+Shift+T);
//   * a small HTTP server on 127.0.0.1 (random token, Host header checked) feeds the page real data: apps and
//     their icons, running windows, Start menu shortcuts, recent items, clock, volume, network, battery, CPU/RAM/GPU/net,
//     a screenshot used as the frosted backdrop of the menu, and events such as the Win key.
// Written for the C# 5 compiler that ships with Windows.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
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
    // ------------------------------------------------------------------ native bits used only by the shell
    static class SN
    {
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")] public static extern IntPtr GetLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] public static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint f);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern int SetWindowRgn(IntPtr h, IntPtr rgn, bool redraw);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateRoundRectRgn(int a, int b, int c, int d, int w, int h);
        [DllImport("gdi32.dll")] public static extern int CombineRgn(IntPtr dst, IntPtr a, IntPtr b, int mode);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr o);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string c, string n);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string c, string n);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetClassLongPtr(IntPtr h, int i);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int v, int size);
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder s, ref int size);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] public static extern void SHCreateItemFromParsingName(string path, IntPtr ctx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory f);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, ref BITMAP b);

        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
        [StructLayout(LayoutKind.Sequential)] public struct MEMSTATUS { public uint dwLength, dwMemoryLoad; public ulong total, avail, totalPage, availPage, totalVirtual, availVirtual, availExtended; }
        [StructLayout(LayoutKind.Sequential)] public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }

        public static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString(); }
        public static string TitleOf(IntPtr h) { int n = GetWindowTextLength(h); if (n <= 0) return ""; StringBuilder sb = new StringBuilder(n + 1); GetWindowText(h, sb, n + 1); return sb.ToString(); }
        public static bool Cloaked(IntPtr h) { int v; return DwmGetWindowAttribute(h, 14, out v, 4) == 0 && v != 0; }
        public static string ExeOf(IntPtr h)
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            IntPtr p = OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (p == IntPtr.Zero) return "";
            try { StringBuilder sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(p, 0, sb, ref n) ? sb.ToString() : ""; }
            finally { CloseHandle(p); }
        }
        // bring a window forward even when another process owns the foreground
        public static void ForceForeground(IntPtr h)
        {
            if (IsIconic(h)) ShowWindow(h, 9 /*SW_RESTORE*/);
            IntPtr fgw = GetForegroundWindow();
            uint pid, fg = GetWindowThreadProcessId(fgw, out pid), me = GetCurrentThreadId();
            // never share input with a window that isn't responding: that can stall our thread and, with it, the keyboard
            bool attach = fg != me && fg != 0 && !IsHungAppWindow(fgw);
            if (attach) AttachThreadInput(me, fg, true);
            keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);   // an Alt tap lifts the foreground lock
            SetForegroundWindow(h);
            if (attach) AttachThreadInput(me, fg, false);
        }
        public static void Keys(params byte[] vks)
        {
            foreach (byte k in vks) keybd_event(k, 0, 0, UIntPtr.Zero);
            for (int i = vks.Length - 1; i >= 0; i--) keybd_event(vks[i], 0, 2, UIntPtr.Zero);
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SN.SIZE size, int flags, out IntPtr hbmp); }

    // ------------------------------------------------------------------ window AppUserModelID (property store)
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint c);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY k);
        [PreserveSig] int GetValue(ref PROPERTYKEY k, out PROPVARIANT v);
        [PreserveSig] int SetValue(ref PROPERTYKEY k, ref PROPVARIANT v);
        [PreserveSig] int Commit();
    }
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] struct PROPVARIANT { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; }
    static class AppId
    {
        [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore ps);
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        public static string Of(IntPtr h)
        {
            IPropertyStore ps = null;
            try
            {
                Guid iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(h, ref iid, out ps) != 0 || ps == null) return null;
                PROPERTYKEY k = new PROPERTYKEY(); k.fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); k.pid = 5;
                PROPVARIANT v; if (ps.GetValue(ref k, out v) != 0) return null;
                string r = v.vt == 31 /*VT_LPWSTR*/ ? Marshal.PtrToStringUni(v.p) : null;
                PropVariantClear(ref v); return r;
            }
            catch { return null; }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }
    }

    // ------------------------------------------------------------------ .lnk resolution
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class CShellLink { }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int max, IntPtr fd, int flags);
        void GetIDList(out IntPtr ppidl); void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int max); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int max); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int max); void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string s);
        void GetHotkey(out short k); void SetHotkey(short k); void GetShowCmd(out int c); void SetShowCmd(int c);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder s, int max, out int i); void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string s, int i);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string s, int r); void Resolve(IntPtr h, int flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string s);
    }
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistFile { void GetClassID(out Guid c); [PreserveSig] int IsDirty(); void Load([MarshalAs(UnmanagedType.LPWStr)] string f, int mode); void Save([MarshalAs(UnmanagedType.LPWStr)] string f, bool remember); void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string f); void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string f); }

    // ------------------------------------------------------------------ Core Audio (master volume)
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumeratorCo { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    // vtable order matters; only the methods up to the ones used are declared
    interface IMMDeviceEnumerator { [PreserveSig] int EnumAudioEndpoints(int flow, int stateMask, out IMMDeviceCollection devs); [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice dev); }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { [PreserveSig] int GetCount(out uint n); [PreserveSig] int Item(uint i, out IMMDevice dev); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPStruct)] Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore ps);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }
    // the default-device switch that Windows' own sound flyout uses (not in the public SDK; EarTrumpet and SoundSwitch call the same)
    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] class PolicyConfigCo { }
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(); [PreserveSig] int GetDeviceFormat(); [PreserveSig] int ResetDeviceFormat(); [PreserveSig] int SetDeviceFormat();
        [PreserveSig] int GetProcessingPeriod(); [PreserveSig] int SetProcessingPeriod(); [PreserveSig] int GetShareMode(); [PreserveSig] int SetShareMode();
        [PreserveSig] int GetPropertyValue(); [PreserveSig] int SetPropertyValue();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility();
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr n); void UnregisterControlChangeNotify(IntPtr n); void GetChannelCount(out int c);
        void SetMasterVolumeLevel(float db, ref Guid ctx); void SetMasterVolumeLevelScalar(float v, ref Guid ctx);
        void GetMasterVolumeLevel(out float db); void GetMasterVolumeLevelScalar(out float v);
        void SetChannelVolumeLevel(uint c, float db, ref Guid ctx); void SetChannelVolumeLevelScalar(uint c, float v, ref Guid ctx);
        void GetChannelVolumeLevel(uint c, out float db); void GetChannelVolumeLevelScalar(uint c, out float v);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }

    // ------------------------------------------------------------------ data the shell pages ask for
    public class ShellData
    {
        static readonly string[] SkipClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow", "Button", "TopLevelWindowForOverflowXamlIsland", "XamlExplorerHostIslandWindow" };
        public IntPtr ShellHwnd = IntPtr.Zero, BgHwnd = IntPtr.Zero, FrameHwnd = IntPtr.Zero;
        Dictionary<string, byte[]> icons = new Dictionary<string, byte[]>();
        Dictionary<string, string> lnkTargets = new Dictionary<string, string>();
        PerformanceCounter cpu, disk; List<PerformanceCounter> gpu; DateTime gpuAt = DateTime.MinValue;
        long lastRx = -1, lastTx = -1; DateTime lastNet = DateTime.MinValue;
        public double Cpu, Ram, Gpu = -1, Disk, NetDown, NetUp;
        System.Threading.Timer statTimer;

        public ShellData()
        {
            statTimer = new System.Threading.Timer(delegate { try { Sample(); } catch { } }, null, 500, 1000);
        }

        void Sample()
        {
            try { if (cpu == null) { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } else Cpu = cpu.NextValue(); } catch { }
            try { if (disk == null) { disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); disk.NextValue(); } else Disk = Math.Min(100, disk.NextValue()); } catch { }
            SN.MEMSTATUS m = new SN.MEMSTATUS(); m.dwLength = (uint)Marshal.SizeOf(typeof(SN.MEMSTATUS)); if (SN.GlobalMemoryStatusEx(ref m)) Ram = m.dwMemoryLoad;
            try   // GPU 3D engines; instances come and go with processes, so the list is rebuilt every 10 s
            {
                if (gpu == null || (DateTime.Now - gpuAt).TotalSeconds > 10)
                {
                    gpu = new List<PerformanceCounter>(); gpuAt = DateTime.Now;
                    PerformanceCounterCategory cat = new PerformanceCounterCategory("GPU Engine");
                    foreach (string inst in cat.GetInstanceNames()) if (inst.EndsWith("engtype_3D")) { PerformanceCounter pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst); pc.NextValue(); gpu.Add(pc); }
                }
                else { double s = 0; foreach (PerformanceCounter pc in gpu) { try { s += pc.NextValue(); } catch { } } Gpu = Math.Min(100, s); }
            }
            catch { Gpu = -1; }
            long rx = 0, tx = 0;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                try { IPv4InterfaceStatistics st = ni.GetIPv4Statistics(); rx += st.BytesReceived; tx += st.BytesSent; } catch { }
            }
            double dt = (DateTime.Now - lastNet).TotalSeconds;
            if (lastRx >= 0 && dt > 0) { NetDown = Math.Max(0, (rx - lastRx) / dt); NetUp = Math.Max(0, (tx - lastTx) / dt); }
            lastRx = rx; lastTx = tx; lastNet = DateTime.Now;
        }

        // ---------- taskbar apps: pinned shortcuts merged with running windows
        public List<Dictionary<string, object>> Apps()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            Dictionary<string, Dictionary<string, object>> byExe = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            string pinDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(pinDir))
                foreach (string lnk in Directory.GetFiles(pinDir, "*.lnk"))
                {
                    string target = LinkTarget(lnk);
                    Dictionary<string, object> a = new Dictionary<string, object>();
                    a["id"] = "pin:" + lnk; a["name"] = Path.GetFileNameWithoutExtension(lnk); a["path"] = lnk; a["exe"] = target; a["icon"] = lnk;
                    a["pinned"] = true; a["running"] = false; a["active"] = false; a["windows"] = new List<string>();
                    list.Add(a);
                    if (!string.IsNullOrEmpty(target) && !byExe.ContainsKey(target)) byExe[target] = a;
                }
            Dictionary<string, Dictionary<string, object>> byAumid = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            List<string[]> tp = TaskbarPins();
            if (tp != null && tp.Count > 0)
            {
                // the Windows taskbar's pinned list is the truth (it also has Store apps, which have no .lnk); a matching .lnk keeps its own icon
                List<Dictionary<string, object>> ordered = new List<Dictionary<string, object>>();
                foreach (string[] pin in tp)
                {
                    string appid = pin[0], name = pin[1];
                    Dictionary<string, object> a = list.Find(x => !ordered.Contains(x) && (string.Equals((string)x["name"], name, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrEmpty((string)x["exe"]) && string.Equals((string)x["exe"], appid, StringComparison.OrdinalIgnoreCase))));
                    if (a == null)
                    {
                        bool isPath = appid.Length > 2 && appid[1] == ':';
                        a = new Dictionary<string, object>();
                        a["id"] = "pin:" + appid; a["name"] = name; a["path"] = isPath ? appid : @"shell:AppsFolder\" + appid; a["exe"] = isPath ? appid : "";
                        a["icon"] = isPath ? appid : @"shell:AppsFolder\" + appid;
                        a["pinned"] = true; a["running"] = false; a["active"] = false; a["windows"] = new List<string>();
                        if (isPath && !byExe.ContainsKey(appid)) byExe[appid] = a;
                    }
                    a["aumid"] = appid; byAumid[appid] = a;
                    ordered.Add(a);
                }
                foreach (Dictionary<string, object> x in list) if (!ordered.Contains(x)) ordered.Add(x);   // a pin the taskbar list didn't name
                list = ordered;
            }
            foreach (Dictionary<string, object> x in list) PackIcon(x);
            IntPtr fg = SN.GetForegroundWindow();
            foreach (IntPtr h in TaskWindows())
            {
                string exe = SN.ExeOf(h), key = exe;
                bool frame = exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
                if (frame || string.IsNullOrEmpty(exe)) key = "win:" + SN.TitleOf(h);
                // the Themer's own window is a browser app window: its own button, not one more "Google Chrome" window
                bool themer = SN.TitleOf(h) == "TRANSCRYPTO Themer" && Array.Exists(new[] { "msedge.exe", "chrome.exe", "brave.exe" }, x => exe.EndsWith("\\" + x, StringComparison.OrdinalIgnoreCase));
                if (themer) key = "app:transcrypto";
                Dictionary<string, object> a = null;
                string aumid = byAumid.Count > 0 && !themer ? AppId.Of(h) : null;
                if (aumid != null) byAumid.TryGetValue(aumid, out a);
                if (a == null && byAumid.Count > 0 && !themer) a = PinForExe(exe, byAumid);
                if (a == null && themer && !byExe.ContainsKey(key))
                {
                    string root = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
                    a = new Dictionary<string, object>();
                    a["id"] = "run:" + key; a["name"] = "TRANSCRYPTO Themer"; a["path"] = Path.Combine(root, "TRANSCRYPTO.vbs"); a["exe"] = "";
                    a["icon"] = Path.Combine(root, @"assets\app.ico");
                    a["pinned"] = false; a["running"] = false; a["active"] = false; a["windows"] = new List<string>();
                    list.Add(a); byExe[key] = a;
                }
                if (a == null && !byExe.TryGetValue(key, out a))
                {
                    a = new Dictionary<string, object>();
                    a["id"] = "run:" + key; a["name"] = frame ? SN.TitleOf(h) : AppName(exe, h); a["path"] = frame ? "" : exe; a["exe"] = exe; a["icon"] = frame || exe == "" ? "hwnd:" + h.ToInt64() : exe;
                    a["pinned"] = false; a["running"] = false; a["active"] = false; a["windows"] = new List<string>();
                    PackIcon(a);
                    list.Add(a); byExe[key] = a;
                }
                a["running"] = true;
                ((List<string>)a["windows"]).Add(h.ToInt64().ToString());
                if (!a.ContainsKey("titles")) a["titles"] = new List<string>();
                ((List<string>)a["titles"]).Add(SN.TitleOf(h));
                if (h == fg) { a["active"] = true; a["title"] = SN.TitleOf(h); }
            }
            return list;
        }

        static string AppName(string exe, IntPtr h)
        {
            try { FileVersionInfo v = FileVersionInfo.GetVersionInfo(exe); if (!string.IsNullOrEmpty(v.FileDescription)) return v.FileDescription; } catch { }
            return string.IsNullOrEmpty(exe) ? SN.TitleOf(h) : Path.GetFileNameWithoutExtension(exe);
        }

        // ---------- the Windows taskbar's pinned apps, read through UI Automation from its (hidden) XAML; {appid, name} in taskbar order
        List<string[]> uiaPins; DateTime uiaAt = DateTime.MinValue; volatile bool uiaBusy;
        List<string[]> TaskbarPins()
        {
            if (!uiaBusy && (DateTime.Now - uiaAt).TotalSeconds > (uiaPins == null ? 5 : 30))
            {
                uiaBusy = true;
                ThreadPool.QueueUserWorkItem(delegate { try { List<string[]> r = ReadTaskbarPins(); if (r != null && r.Count > 0) uiaPins = r; } catch { } uiaAt = DateTime.Now; uiaBusy = false; });
            }
            return uiaPins;
        }
        static List<string[]> ReadTaskbarPins()
        {
            IntPtr tray = SN.FindWindow("Shell_TrayWnd", null);
            IntPtr bridge = tray == IntPtr.Zero ? IntPtr.Zero : SN.FindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
            if (bridge == IntPtr.Zero) return null;
            AutomationElementCollection all = AutomationElement.FromHandle(bridge).FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"));
            List<string[]> res = new List<string[]>();
            foreach (AutomationElement e in all)
            {
                string id = e.Current.AutomationId ?? "", name = e.Current.Name ?? "";
                if (!id.StartsWith("Appid: ") || !name.EndsWith(" pinned")) continue;   // running-only buttons come from our own window list
                name = System.Text.RegularExpressions.Regex.Replace(name.Substring(0, name.Length - 7), @"\s+-\s+\d+ running windows?$", "");
                res.Add(new string[] { id.Substring(7), name });
            }
            return res;
        }

        // windows that don't report the pin's AppUserModelID: Store apps run from WindowsApps\<Name>_<version>_<arch>__<hash>\,
        // which matches a pin "<Name>_<hash>!App"; File Explorer windows belong to the "Microsoft.Windows.Explorer" pin
        static Dictionary<string, object> PinForExe(string exe, Dictionary<string, Dictionary<string, object>> byAumid)
        {
            if (string.IsNullOrEmpty(exe)) return null;
            Dictionary<string, object> a;
            if (exe.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase) && byAumid.TryGetValue("Microsoft.Windows.Explorer", out a)) return a;
            foreach (KeyValuePair<string, Dictionary<string, object>> kv in byAumid)
            {
                int bang = kv.Key.IndexOf('!'), us = bang > 0 ? kv.Key.LastIndexOf('_', bang) : -1;
                if (us <= 0) continue;
                string name = kv.Key.Substring(0, us), hash = kv.Key.Substring(us + 1, bang - us - 1);
                if (exe.IndexOf(@"\" + name + "_", StringComparison.OrdinalIgnoreCase) >= 0 && exe.IndexOf("__" + hash + @"\", StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
            }
            return null;
        }

        // the applied TRANSCRYPTO icon pack (where Shell Icons 3 points) gives File Explorer and the known folders their pack icons
        static string PackDir()
        {
            try
            {
                string v = (string)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons", "3", null);
                if (string.IsNullOrEmpty(v) || v.IndexOf(@"\assets\icons\", StringComparison.OrdinalIgnoreCase) < 0) return null;
                return Path.GetDirectoryName(v.Split(',')[0].Trim('"'));
            }
            catch { return null; }
        }
        static readonly Dictionary<string, string> PackNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "File Explorer", "folder" }, { "Explorer", "folder" }, { "This PC", "thispc" }, { "Recycle Bin", "recycleFull" }, { "Downloads", "downloads" },
            { "Documents", "documents" }, { "Pictures", "pictures" }, { "Music", "music" }, { "Videos", "videos" }, { "Desktop", "desktop" }, { "Network", "network" } };
        public static void PackIcon(Dictionary<string, object> a)
        {
            string dir = PackDir(); if (dir == null) return;
            object o; string key = null;
            string name = a.TryGetValue("name", out o) ? o as string ?? "" : "", exe = a.TryGetValue("exe", out o) ? o as string ?? "" : "", aumid = a.TryGetValue("aumid", out o) ? o as string ?? "" : "";
            if (!PackNames.TryGetValue(name, out key) && (exe.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase) || aumid == "Microsoft.Windows.Explorer")) key = "folder";
            if (key == null) return;
            string ico = Path.Combine(dir, key + ".ico"); if (File.Exists(ico)) a["icon"] = ico;
        }

        // the windows the Windows taskbar would show
        public List<IntPtr> TaskWindows()
        {
            List<IntPtr> res = new List<IntPtr>();
            SN.EnumWindows(delegate (IntPtr h, IntPtr l)
            {
                if (h == ShellHwnd || h == BgHwnd || h == FrameHwnd || !SN.IsWindowVisible(h) || SN.Cloaked(h)) return true;
                long ex = SN.GetLong(h, -20).ToInt64();
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;            // tool window without APPWINDOW
                if (SN.GetWindow(h, 4 /*GW_OWNER*/) != IntPtr.Zero && (ex & 0x40000) == 0) return true;
                if (SN.GetWindowTextLength(h) == 0) return true;
                if (Array.IndexOf(SkipClasses, SN.ClassOf(h)) >= 0) return true;
                res.Add(h); return true;
            }, IntPtr.Zero);
            return res;
        }

        public string LinkTarget(string lnk)
        {
            string t;
            lock (lnkTargets) if (lnkTargets.TryGetValue(lnk, out t)) return t;
            t = "";
            try
            {
                IShellLinkW sl = (IShellLinkW)new CShellLink(); ((IPersistFile)sl).Load(lnk, 0);
                StringBuilder sb = new StringBuilder(1024); sl.GetPath(sb, sb.Capacity, IntPtr.Zero, 0); t = sb.ToString();
                Marshal.ReleaseComObject(sl);
            }
            catch { }
            lock (lnkTargets) lnkTargets[lnk] = t;
            return t;
        }

        // ---------- the real Start menu: pinned apps (Export-StartLayout) and every app (Get-StartApps, incl. Store apps)
        // Everything launches through shell:AppsFolder\<AppID>, which also gives the right icon for packaged apps.
        List<Dictionary<string, object>> startApps, startPins; DateTime startAt = DateTime.MinValue;
        public List<Dictionary<string, object>> StartPins() { StartApps(); return startPins ?? new List<Dictionary<string, object>>(); }
        public List<Dictionary<string, object>> StartApps()
        {
            if (startApps != null && (DateTime.Now - startAt).TotalMinutes < 5) return startApps;
            try { if (ReadStartLayout()) { startAt = DateTime.Now; return startApps; } } catch (Exception ex) { Program.Log("start layout: " + ex.Message); }
            return LegacyStartApps();
        }

        bool ReadStartLayout()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "tc-startlayout.json");
            string cmd = "$ErrorActionPreference='SilentlyContinue'; Export-StartLayout -Path '" + tmp + "'; " +
                         "$p = if (Test-Path '" + tmp + "') { (Get-Content -Raw '" + tmp + "' | ConvertFrom-Json).pinnedList } else { @() }; " +
                         "[Console]::OutputEncoding = [Text.Encoding]::UTF8; @{ pins = @($p); apps = @(Get-StartApps | Select-Object Name, AppID) } | ConvertTo-Json -Depth 4 -Compress";
            ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + cmd.Replace("\"", "\\\"") + "\"");
            psi.UseShellExecute = false; psi.RedirectStandardOutput = true; psi.CreateNoWindow = true; psi.StandardOutputEncoding = Encoding.UTF8;
            string json;
            using (Process p = Process.Start(psi)) { json = p.StandardOutput.ReadToEnd(); p.WaitForExit(15000); }
            Dictionary<string, object> d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return false;
            Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            List<Dictionary<string, object>> apps = new List<Dictionary<string, object>>();
            foreach (object o in AsList(d, "apps"))
            {
                Dictionary<string, object> a = o as Dictionary<string, object>; if (a == null) continue;
                string n = Convert.ToString(a["Name"]), id = Convert.ToString(a["AppID"]);
                if (string.IsNullOrEmpty(id) || n.ToLowerInvariant().Contains("uninstall")) continue;
                names[id] = n;
                Dictionary<string, object> x = new Dictionary<string, object>(); x["name"] = n; x["path"] = @"shell:AppsFolder\" + id; x["icon"] = @"shell:AppsFolder\" + id; apps.Add(x);
            }
            apps.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { return string.Compare((string)x["name"], (string)y["name"], StringComparison.OrdinalIgnoreCase); });
            List<Dictionary<string, object>> pins = new List<Dictionary<string, object>>();
            foreach (object o in AsList(d, "pins"))
            {
                Dictionary<string, object> pin = o as Dictionary<string, object>; if (pin == null) continue;
                Dictionary<string, object> x = new Dictionary<string, object>(); object v;
                if (pin.TryGetValue("desktopAppLink", out v))
                {
                    string lnk = Environment.ExpandEnvironmentVariables(Convert.ToString(v));
                    if (!File.Exists(lnk)) continue;
                    x["name"] = Path.GetFileNameWithoutExtension(lnk); x["path"] = lnk; x["icon"] = lnk;
                }
                else if (pin.TryGetValue("packagedAppId", out v))
                {
                    string id = Convert.ToString(v), n;
                    x["name"] = names.TryGetValue(id, out n) ? n : id.Split('!')[0].Split('.')[1 < id.Split('.').Length ? 1 : 0];
                    x["path"] = @"shell:AppsFolder\" + id; x["icon"] = @"shell:AppsFolder\" + id;
                }
                else continue;
                pins.Add(x);
            }
            foreach (Dictionary<string, object> p in pins) PackIcon(p);
            startApps = apps; startPins = pins;
            return apps.Count > 0;
        }

        static List<object> AsList(Dictionary<string, object> d, string k)
        {
            object v; List<object> l = new List<object>();
            if (!d.TryGetValue(k, out v) || v == null) return l;
            if (v is object[]) l.AddRange((object[])v); else if (v is System.Collections.ArrayList) foreach (object o in (System.Collections.ArrayList)v) l.Add(o); else l.Add(v);
            return l;
        }

        // fallback when PowerShell is unavailable: Start menu shortcut folders
        List<Dictionary<string, object>> LegacyStartApps()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in new string[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), Environment.GetFolderPath(Environment.SpecialFolder.StartMenu) })
            {
                string dir = Path.Combine(root, "Programs");
                if (!Directory.Exists(dir)) continue;
                string[] files; try { files = Directory.GetFiles(dir, "*.lnk", SearchOption.AllDirectories); } catch { continue; }
                foreach (string f in files)
                {
                    string n = Path.GetFileNameWithoutExtension(f), low = n.ToLowerInvariant();
                    if (low.Contains("uninstall") || low.Contains("readme") || low.StartsWith("help") || !seen.Add(n)) continue;
                    Dictionary<string, object> a = new Dictionary<string, object>(); a["name"] = n; a["path"] = f; a["icon"] = f; list.Add(a);
                }
            }
            list.Sort(delegate (Dictionary<string, object> x, Dictionary<string, object> y) { return string.Compare((string)x["name"], (string)y["name"], StringComparison.OrdinalIgnoreCase); });
            startApps = list; startAt = DateTime.Now; if (startPins == null) startPins = new List<Dictionary<string, object>>(); return list;
        }

        // ---------- recent files and folders (Windows' own Recent list)
        public List<Dictionary<string, object>> Recent(bool folders, int max)
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (!Directory.Exists(dir)) return list;
            FileInfo[] files; try { files = new DirectoryInfo(dir).GetFiles("*.lnk"); } catch { return list; }
            Array.Sort(files, delegate (FileInfo a, FileInfo b) { return b.LastWriteTime.CompareTo(a.LastWriteTime); });
            foreach (FileInfo f in files)
            {
                string t = LinkTarget(f.FullName);
                if (string.IsNullOrEmpty(t)) continue;
                bool isDir = Directory.Exists(t);
                if (isDir != folders || (!isDir && !File.Exists(t))) continue;
                Dictionary<string, object> a = new Dictionary<string, object>();
                a["name"] = Path.GetFileName(t.TrimEnd('\\')); a["path"] = t; a["icon"] = t; a["when"] = f.LastWriteTime.ToString("o");
                list.Add(a);
                if (list.Count >= max) break;
            }
            return list;
        }

        // ---------- system tray state
        public Dictionary<string, object> Sys()
        {
            Dictionary<string, object> s = new Dictionary<string, object>();
            s["cpu"] = Math.Round(Cpu); s["ram"] = Math.Round(Ram); s["gpu"] = Gpu < 0 ? -1 : Math.Round(Gpu); s["disk"] = Math.Round(Disk);
            s["down"] = Math.Round(NetDown); s["up"] = Math.Round(NetUp);
            float vol = -1; bool mute = false; Volume(out vol, out mute); s["volume"] = vol < 0 ? -1 : Math.Round(vol * 100); s["mute"] = mute;
            string type = "none"; bool online = NetworkInterface.GetIsNetworkAvailable();
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) >= 0 || ni.Description.IndexOf("vpn", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                type = ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "wifi" : "ethernet"; break;
            }
            s["net"] = online ? type : "none";
            PowerStatus ps = SystemInformation.PowerStatus;
            s["battery"] = (ps.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 ? -1 : Math.Round(ps.BatteryLifePercent * 100);
            s["charging"] = ps.PowerLineStatus == PowerLineStatus.Online;
            s["user"] = Environment.UserName;
            return s;
        }

        // ---------- quick settings panel: network name, signal, adapter (netsh is slow, so cached for a few seconds)
        Dictionary<string, object> quick; DateTime quickAt;
        public void ForgetQuick() { quick = null; }
        public Dictionary<string, object> Quick()
        {
            if (quick != null && (DateTime.Now - quickAt).TotalSeconds < 5) return quick;
            Dictionary<string, object> q = new Dictionary<string, object>(Sys());
            string ssid = "", signal = "";
            try
            {
                ProcessStartInfo si = new ProcessStartInfo("netsh.exe", "wlan show interfaces") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(si))
                {
                    string all = p.StandardOutput.ReadToEnd(); p.WaitForExit(3000);
                    foreach (string raw in all.Split('\n'))
                    {
                        int c = raw.IndexOf(':'); if (c < 0) continue;
                        string k = raw.Substring(0, c).Trim(), v = raw.Substring(c + 1).Trim();
                        if (k == "SSID") ssid = v; else if (k == "Signal") signal = v.TrimEnd('%');
                    }
                }
            }
            catch { }
            q["ssid"] = ssid; int sg; q["signal"] = int.TryParse(signal, out sg) ? sg : -1;
            string adapter = "";
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                if (ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                    && ni.Description.IndexOf("virtual", StringComparison.OrdinalIgnoreCase) < 0) { adapter = ni.Name; break; }
            q["adapter"] = adapter;
            q["machine"] = Environment.MachineName;
            q["outputs"] = AudioOutputs();
            quick = q; quickAt = DateTime.Now;
            return q;
        }

        static IAudioEndpointVolume Endpoint()
        {
            IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo(); IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out dev) != 0 || dev == null) return null;
            object o; dev.Activate(typeof(IAudioEndpointVolume).GUID, 23 /*CLSCTX_ALL*/, IntPtr.Zero, out o);
            return o as IAudioEndpointVolume;
        }
        // ---------- audio outputs (the device list of the quick settings volume card)
        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PROPVARIANT v);
        public static List<Dictionary<string, object>> AudioOutputs()
        {
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            try
            {
                IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
                IMMDevice def; string defId = null;
                if (en.GetDefaultAudioEndpoint(0, 1, out def) == 0 && def != null) { def.GetId(out defId); Marshal.ReleaseComObject(def); }
                IMMDeviceCollection devs; if (en.EnumAudioEndpoints(0 /*eRender*/, 1 /*DEVICE_STATE_ACTIVE*/, out devs) != 0 || devs == null) return list;
                uint n; devs.GetCount(out n);
                PROPERTYKEY name = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };   // PKEY_Device_FriendlyName
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice d; if (devs.Item(i, out d) != 0 || d == null) continue;
                    string id; d.GetId(out id); string label = id;
                    IPropertyStore ps;
                    if (d.OpenPropertyStore(0 /*STGM_READ*/, out ps) == 0 && ps != null)
                    {
                        PROPVARIANT v; if (ps.GetValue(ref name, out v) == 0) { if (v.vt == 31 /*VT_LPWSTR*/) label = Marshal.PtrToStringUni(v.p); PropVariantClear(ref v); }
                        Marshal.ReleaseComObject(ps);
                    }
                    Dictionary<string, object> x = new Dictionary<string, object>(); x["id"] = id; x["name"] = label; x["default"] = id == defId;
                    list.Add(x); Marshal.ReleaseComObject(d);
                }
                Marshal.ReleaseComObject(devs);
            }
            catch { }
            return list;
        }
        public static bool SetAudioOutput(string id)
        {
            bool ok = false;
            foreach (Dictionary<string, object> d in AudioOutputs()) if ((string)d["id"] == id) ok = true;
            if (!ok) return false;   // only a real, active output device
            try
            {
                IPolicyConfig pc = (IPolicyConfig)new PolicyConfigCo();
                for (int role = 0; role < 3; role++) pc.SetDefaultEndpoint(id, role);   // console, multimedia, communications
                Marshal.ReleaseComObject(pc); return true;
            }
            catch { return false; }
        }

        public static void Volume(out float level, out bool mute)
        {
            level = -1; mute = false;
            try { IAudioEndpointVolume v = Endpoint(); if (v == null) return; v.GetMasterVolumeLevelScalar(out level); v.GetMute(out mute); Marshal.ReleaseComObject(v); } catch { }
        }
        public static void SetVolume(float delta, bool toggleMute)
        {
            try
            {
                IAudioEndpointVolume v = Endpoint(); if (v == null) return; Guid g = Guid.Empty;
                if (toggleMute) { bool m; v.GetMute(out m); v.SetMute(!m, ref g); }
                else { float l; v.GetMasterVolumeLevelScalar(out l); v.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, l + delta)), ref g); }
                Marshal.ReleaseComObject(v);
            }
            catch { }
        }
        // the volume slider: an exact level (0..1); moving it up from silence also unmutes, like Windows' own slider
        public static void SetVolumeLevel(float level)
        {
            try
            {
                IAudioEndpointVolume v = Endpoint(); if (v == null) return; Guid g = Guid.Empty;
                v.SetMasterVolumeLevelScalar(Math.Max(0, Math.Min(1, level)), ref g);
                bool m; v.GetMute(out m); if (m && level > 0) v.SetMute(false, ref g);
                Marshal.ReleaseComObject(v);
            }
            catch { }
        }

        // ---------- icons: shell thumbnails with real alpha, cached as PNG
        public byte[] Icon(string key, int size)
        {
            string ck = key + "|" + size; byte[] png;
            lock (icons) if (icons.TryGetValue(ck, out png)) return png;
            Bitmap bmp = null;
            try
            {
                if (key.StartsWith("hwnd:")) bmp = WindowIcon(new IntPtr(long.Parse(key.Substring(5))));
                else bmp = ShellImage(key, size);
            }
            catch { }
            if (bmp == null) return null;
            using (MemoryStream ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = ms.ToArray(); }
            bmp.Dispose();
            if (!key.StartsWith("hwnd:")) lock (icons) icons[ck] = png;
            return png;
        }

        static Bitmap ShellImage(string path, int size)
        {
            IShellItemImageFactory f;
            SN.SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out f);
            SN.SIZE sz = new SN.SIZE(); sz.cx = size; sz.cy = size; IntPtr hb;
            if (f.GetImage(sz, 0x1 /*BIGGERSIZEOK*/ | 0x4 /*ICONONLY*/, out hb) != 0 || hb == IntPtr.Zero) { Marshal.ReleaseComObject(f); return null; }
            Marshal.ReleaseComObject(f);
            try { return FromHBitmap(hb); } finally { SN.DeleteObject(hb); }
        }

        // copy a 32-bit DIB section keeping its alpha (Image.FromHbitmap would drop it)
        static Bitmap FromHBitmap(IntPtr hb)
        {
            SN.BITMAP b = new SN.BITMAP(); SN.GetObject(hb, Marshal.SizeOf(typeof(SN.BITMAP)), ref b);
            if (b.bmBits == IntPtr.Zero || b.bmBitsPixel != 32) return Image.FromHbitmap(hb);
            Bitmap res = new Bitmap(b.bmWidth, b.bmHeight, PixelFormat.Format32bppPArgb);
            BitmapData d = res.LockBits(new Rectangle(0, 0, b.bmWidth, b.bmHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            byte[] row = new byte[b.bmWidthBytes];
            for (int y = 0; y < b.bmHeight; y++)   // DIB sections from the shell are bottom-up
            {
                Marshal.Copy(new IntPtr(b.bmBits.ToInt64() + (long)(b.bmHeight - 1 - y) * b.bmWidthBytes), row, 0, row.Length);
                Marshal.Copy(row, 0, new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), Math.Min(row.Length, d.Stride));
            }
            res.UnlockBits(d);
            return res;
        }

        static Bitmap WindowIcon(IntPtr h)
        {
            IntPtr ic = SN.SendMessage(h, 0x7F /*WM_GETICON*/, new IntPtr(1) /*ICON_BIG*/, IntPtr.Zero);
            if (ic == IntPtr.Zero) ic = SN.GetClassLongPtr(h, -14 /*GCLP_HICON*/);
            if (ic == IntPtr.Zero) return null;
            using (Icon i = System.Drawing.Icon.FromHandle(ic)) return i.ToBitmap();
        }
    }
}
