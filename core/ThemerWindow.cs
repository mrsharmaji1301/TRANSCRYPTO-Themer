// TRANSCRYPTO Themer - the native app window (bin\TRANSCRYPTO.exe). It shows the Themer UI that the backend serves on
// 127.0.0.1, inside Microsoft WebView2 (part of Windows 11), instead of opening a Chrome/Edge app window.
// Built by installer\Install.ps1 against vendor\WebView2 (Microsoft.Web.WebView2 WinForms + loader).
//   TRANSCRYPTO.exe            reads %LOCALAPPDATA%\TRANSCRYPTO\session.json (port + token) and opens the UI
//   TRANSCRYPTO.exe <url>      opens that URL (must be http://127.0.0.1:<port>/...)
// Only one window: starting it again brings the open one forward.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyTitle("TRANSCRYPTO Themer")]
[assembly: AssemblyProduct("TRANSCRYPTO Themer")]
[assembly: AssemblyCompany("TRANSCRYPTO")]
[assembly: AssemblyCopyright("Copyright (c) 2026 SharmaJi (TRANSCRYPTO). Made with love by SharmaJi.")]

namespace Transcrypto
{
    static class ThemerApp
    {
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr v);

        [STAThread]
        static int Main(string[] args)
        {
            // already open: bring that window forward
            Process me = Process.GetCurrentProcess();
            foreach (Process p in Process.GetProcessesByName(me.ProcessName))
            {
                if (p.Id == me.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                if (IsIconic(p.MainWindowHandle)) ShowWindow(p.MainWindowHandle, 9 /*SW_RESTORE*/);
                SetForegroundWindow(p.MainWindowHandle);
                return 0;
            }
            // WebView2 must use the browser Windows installed and the window's own settings, never ones planted in the environment
            foreach (string v in new[] { "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_USER_DATA_FOLDER",
                                         "WEBVIEW2_RELEASE_CHANNEL_PREFERENCE", "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER", "WEBVIEW2_WAIT_FOR_SCRIPT_DEBUGGER" })
                Environment.SetEnvironmentVariable(v, null);
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
            string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRANSCRYPTO");
            string url = args.Length > 0 ? args[0] : FromSession(Path.Combine(data, "session.json"));
            if (url == null || !Regex.IsMatch(url, @"^http://127\.0\.0\.1:\d{2,5}/"))
            {
                MessageBox.Show("The TRANSCRYPTO backend is not running. Start TRANSCRYPTO Themer from its shortcut.", "TRANSCRYPTO Themer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ThemerForm(url, Path.Combine(data, "ui-webview2")));
            return 0;
        }

        static string FromSession(string path)
        {
            try
            {
                string t = File.ReadAllText(path);
                Match port = Regex.Match(t, "\"port\"\\s*:\\s*(\\d+)"), tok = Regex.Match(t, "\"token\"\\s*:\\s*\"([0-9a-fA-F]+)\"");
                if (port.Success && tok.Success) return "http://127.0.0.1:" + port.Groups[1].Value + "/?t=" + tok.Groups[1].Value;
            }
            catch { }
            return null;
        }
    }

    class ThemerForm : Form
    {
        readonly WebView2 web = new WebView2();
        readonly string url, origin, profile;

        public ThemerForm(string url, string profile)
        {
            this.url = url; this.profile = profile;
            Uri u = new Uri(url); origin = u.Scheme + "://" + u.Authority;
            Text = "TRANSCRYPTO Themer";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            BackColor = Color.FromArgb(7, 7, 13);
            StartPosition = FormStartPosition.CenterScreen;
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1380, wa.Width - 40), Math.Min(880, wa.Height - 40));
            MinimumSize = new Size(900, 600);
            web.Dock = DockStyle.Fill; web.DefaultBackgroundColor = BackColor;
            Controls.Add(web);
            Load += async delegate
            {
                try
                {
                    CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, profile,
                        new CoreWebView2EnvironmentOptions("--disable-features=Translate,msSmartScreenProtection --no-first-run"));
                    await web.EnsureCoreWebView2Async(env);
                    CoreWebView2Settings s = web.CoreWebView2.Settings;
                    s.AreDevToolsEnabled = false; s.IsStatusBarEnabled = false; s.AreHostObjectsAllowed = false;
                    s.IsPasswordAutosaveEnabled = false; s.IsGeneralAutofillEnabled = false; s.IsZoomControlEnabled = true;
                    // the window only ever shows the Themer; web links open in the normal browser
                    web.CoreWebView2.NavigationStarting += (o, e) =>
                    {
                        if (e.Uri.StartsWith(origin + "/", StringComparison.OrdinalIgnoreCase) || e.Uri == origin) return;
                        e.Cancel = true; External(e.Uri);
                    };
                    web.CoreWebView2.NewWindowRequested += (o, e) => { e.Handled = true; External(e.Uri); };
                    web.CoreWebView2.DocumentTitleChanged += (o, e) => { string t = web.CoreWebView2.DocumentTitle; if (!string.IsNullOrEmpty(t) && !t.StartsWith("http")) Text = t; };
                    web.CoreWebView2.Navigate(url);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Microsoft WebView2 could not start, so the Themer window can't open.\n\n" + ex.Message +
                        "\n\nInstall the WebView2 Runtime from Microsoft (go.microsoft.com/fwlink/p/?LinkId=2124703) and start TRANSCRYPTO again.",
                        "TRANSCRYPTO Themer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Close();
                }
            };
        }

        static void External(string uri)
        {
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { }
        }
    }
}
