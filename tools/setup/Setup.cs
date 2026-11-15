// TRANSCRYPTO Themer setup: one signed .exe that carries the signed release zip (embedded resource "payload.zip").
// It unpacks into a fresh folder only administrators can write (no file swapping between unpack and install),
// runs installer\Install.ps1 -Quiet with the options picked, and shows its progress. Built by tools\make-setup.ps1.
//   Setup.exe          wizard: License -> Options (folder, shortcuts, launch) -> Install
//   Setup.exe /S       silent install; running it this way accepts LICENSE.txt (exit code 0 = installed, 1 = failed)
//       /D=<folder>    install folder, last on the command line (new installs only; an update stays where the app is)
//       /DESKTOP or /NODESKTOP   add or remove the desktop shortcut (neither: an update keeps it as it is)
//       /NOSTARTMENU   no Start menu shortcut     /NOLAUNCH   don't start it
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("TRANSCRYPTO Themer Setup")]
[assembly: AssemblyDescription("Installs TRANSCRYPTO Themer, the all-in-one Windows 11 themer")]
[assembly: AssemblyCompany("TRANSCRYPTO")]
[assembly: AssemblyProduct("TRANSCRYPTO Themer")]
[assembly: AssemblyCopyright("Copyright (c) 2026 SharmaJi (TRANSCRYPTO). Free to use, no redistribution. Made with love by SharmaJi.")]
[assembly: AssemblyVersion(SetupInfo.Version + ".0")]
[assembly: AssemblyFileVersion(SetupInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(SetupInfo.Version)]

static partial class SetupInfo { }

class Options
{
    public string Folder;          // null = Install.ps1 decides (current install, else Program Files)
    public bool? Desktop;              // null = keep what is there
    public bool StartMenu = true, Launch = true;
}

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        bool silent = false; Options o = new Options();
        foreach (string a in args)
        {
            string u = a.ToUpperInvariant();
            if (u == "/S" || u == "/SILENT") silent = true;
            else if (u.StartsWith("/D=")) break;   // the folder is the rest of the command line, spaces included (read below)
            else if (u == "/DESKTOP") o.Desktop = true;
            else if (u == "/NODESKTOP") o.Desktop = false;
            else if (u == "/NOSTARTMENU") o.StartMenu = false;
            else if (u == "/NOLAUNCH") o.Launch = false;
        }
        // like other installers: /D= comes last and takes everything after it, so "Program Files" works without quotes
        string cl = Environment.CommandLine; int di = cl.IndexOf(" /D=", StringComparison.OrdinalIgnoreCase);
        if (di >= 0) o.Folder = cl.Substring(di + 4).Trim().Trim('"');
        if (silent)
        {
            if (o.Folder != null) { string err = Installer.CheckFolder(o.Folder); if (err != null) return 1; }
            return Installer.Run(null, o) ? 0 : 1;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }
}

static class Installer
{
    public const string AppFolder = "TRANSCRYPTO Themer";
    public static string DefaultFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppFolder); } }

    // where the app is installed now (an update stays there), or null
    public static string CurrentInstall()
    {
        try
        {
            using (RegistryKey hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey k = hk.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TRANSCRYPTO Themer"))
            {
                string p = k == null ? null : k.GetValue("InstallLocation") as string;
                return !string.IsNullOrEmpty(p) && File.Exists(Path.Combine(p, "version.json")) ? p : null;
            }
        }
        catch { return null; }
    }

    // null = fine; otherwise why the folder can't be used (Install.ps1 checks the same again)
    public static string CheckFolder(string f)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(f) || f.Contains("\"") || !Path.IsPathRooted(f)) return "Enter a full folder path, for example " + DefaultFolder;
            f = Path.GetFullPath(f.Trim()).TrimEnd('\\');
            string root = Path.GetPathRoot(f);
            if (root.StartsWith(@"\\")) return "Network folders can't be used. Pick a folder on this PC.";
            if (f.Length <= root.TrimEnd('\\').Length) return "Pick a folder, not the whole drive.";
            DriveInfo d = new DriveInfo(root);
            if (!d.IsReady || d.DriveType != DriveType.Fixed) return "Pick a folder on a fixed drive of this PC (not removable or network).";
            if (d.DriveFormat != "NTFS") return "The drive must be NTFS (" + root + " is " + d.DriveFormat + ").";
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
            if ((f + "\\").StartsWith(win, StringComparison.OrdinalIgnoreCase)) return "Windows' own folder can't be used.";
            if (Directory.Exists(f) && !File.Exists(Path.Combine(f, "version.json")) && Directory.GetFileSystemEntries(f).Length > 0) return "This folder is not empty. Pick an empty or new folder.";
            if (d.AvailableFreeSpace < PayloadSize() + 20L * 1024 * 1024) return "Not enough free space on " + root;
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    static long size = -1;
    public static long PayloadSize()
    {
        if (size >= 0) return size;
        long n = 0;
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
                foreach (ZipArchiveEntry e in zip.Entries) n += e.Length;
        }
        catch { }
        return size = n + 400L * 1024;   // + the engine built during setup
    }

    // unpack the payload and run Install.ps1; log receives every output line (null = silent)
    public static bool Run(Action<string> log, Options o)
    {
        string dir = null;
        try
        {
            dir = SecureTemp();
            Say(log, "Unpacking…");
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                foreach (ZipArchiveEntry e in zip.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new Exception("Bad entry in package: " + e.FullName);
                    if (e.FullName.EndsWith("/")) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    e.ExtractToFile(target, true);
                }
            }
            string script = Path.Combine(dir, @"installer\Install.ps1");
            if (!File.Exists(script)) throw new Exception("The package has no installer.");
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            string a = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + script + "\" -Quiet";
            if (!string.IsNullOrEmpty(o.Folder)) a += " -Dest \"" + Path.GetFullPath(o.Folder.Trim()).TrimEnd('\\') + "\"";
            if (o.Desktop == true) a += " -DesktopShortcut"; else if (o.Desktop == false) a += " -NoDesktopShortcut";
            if (!o.StartMenu) a += " -NoStartMenu";
            if (!o.Launch) a += " -NoLaunch";
            ProcessStartInfo psi = new ProcessStartInfo(ps, a);
            psi.UseShellExecute = false; psi.CreateNoWindow = true;
            // not the temp folder: whatever setup starts (the app) would inherit it as its current folder and keep it from being deleted
            psi.WorkingDirectory = Environment.SystemDirectory;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (Process p = Process.Start(psi))
            {
                p.OutputDataReceived += (s2, e) => { if (e.Data != null && e.Data.Trim().Length > 0) Say(log, e.Data.Trim()); };
                p.ErrorDataReceived += (s2, e) => { if (e.Data != null && e.Data.Trim().Length > 0) Say(log, "! " + e.Data.Trim()); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
        }
        catch (Exception ex) { Say(log, "Setup failed: " + ex.Message); return false; }
        finally
        {
            for (int i = 0; dir != null && i < 5; i++) { try { Directory.Delete(dir, true); break; } catch { Thread.Sleep(1000); } }
        }
    }

    static void Say(Action<string> log, string t) { if (log != null) log(t); }

    // LICENSE.txt from the payload (shown before installing)
    public static string License()
    {
        try
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            using (ZipArchive zip = new ZipArchive(s, ZipArchiveMode.Read))
            {
                ZipArchiveEntry e = zip.GetEntry("LICENSE.txt");
                if (e != null) using (StreamReader r = new StreamReader(e.Open())) return r.ReadToEnd();
            }
        }
        catch { }
        return "TRANSCRYPTO Themer is free to use. No redistribution. No commercial use except by SharmaJi (TRANSCRYPTO).";
    }

    // %WINDIR%\Temp\TRANSCRYPTO-setup-<guid>, created with an ACL for Administrators + SYSTEM only
    static string SecureTemp()
    {
        string p = Path.Combine(Environment.GetEnvironmentVariable("WINDIR"), @"Temp\TRANSCRYPTO-setup-" + Guid.NewGuid().ToString("N"));
        DirectorySecurity sec = new DirectorySecurity();
        sec.SetAccessRuleProtection(true, false);
        foreach (WellKnownSidType sid in new[] { WellKnownSidType.BuiltinAdministratorsSid, WellKnownSidType.LocalSystemSid })
            sec.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        Directory.CreateDirectory(p, sec);
        return p;
    }
}

class SetupForm : Form
{
    static readonly Color Bg = Color.FromArgb(10, 10, 20), Neon = Color.FromArgb(0, 240, 255), Pink = Color.FromArgb(255, 43, 214),
                          Muted = Color.FromArgb(154, 156, 184), Field = Color.FromArgb(18, 18, 34), Ok = Color.FromArgb(61, 255, 168), Bad = Color.FromArgb(255, 77, 109);
    readonly Button next = new Button(), back = new Button(), cancel = new Button(), browse = new Button();
    readonly Panel pLicense = new Panel(), pOptions = new Panel(), pInstall = new Panel();
    readonly Label step = new Label(), status = new Label(), folderNote = new Label(), space = new Label();
    readonly TextBox license = new TextBox(), folder = new TextBox(), log = new TextBox();
    readonly CheckBox accept = new CheckBox(), cDesktop = new CheckBox(), cStart = new CheckBox(), cLaunch = new CheckBox();
    readonly ProgressBar bar = new ProgressBar();
    readonly string current = Installer.CurrentInstall();
    int page = 0; bool done;

    public SetupForm()
    {
        Text = "TRANSCRYPTO Themer " + SetupInfo.Version + " Setup";
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 500); BackColor = Bg; ForeColor = Color.White; Font = new Font("Segoe UI", 9.5f);

        Label title = new Label { Text = "TRANSCRYPTO Themer", Font = new Font("Segoe UI", 22f, FontStyle.Bold), ForeColor = Neon, AutoSize = true, Location = new Point(28, 18) };
        Label ver = new Label { Text = "Version " + SetupInfo.Version + "  ·  all-in-one Windows 11 themer with 3 custom shells", ForeColor = Muted, AutoSize = true, Location = new Point(32, 64) };
        step.SetBounds(32, 92, 576, 22); step.ForeColor = Color.White; step.Font = new Font("Segoe UI Semibold", 11f);
        foreach (Panel p in new[] { pLicense, pOptions, pInstall }) { p.SetBounds(32, 120, 576, 300); p.BackColor = Bg; }

        // 1. license
        Box(license); license.SetBounds(0, 0, 576, 262); license.ScrollBars = ScrollBars.Vertical;
        license.TabStop = false; license.WordWrap = true;
        license.Text = Installer.License().Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        accept.Text = "I accept the license (free to use, no redistribution, no commercial use)"; accept.ForeColor = Color.Gainsboro;
        accept.SetBounds(0, 272, 576, 24); accept.CheckedChanged += (s, e) => Nav();
        pLicense.Controls.AddRange(new Control[] { license, accept });

        // 2. options
        Label lf = new Label { Text = "Install location", ForeColor = Color.Gainsboro, Location = new Point(0, 4), AutoSize = true };
        Box(folder); folder.SetBounds(0, 28, 456, 26); folder.Multiline = false; folder.ReadOnly = false; folder.BorderStyle = BorderStyle.FixedSingle;
        folder.Text = current ?? Installer.DefaultFolder; folder.TextChanged += (s, e) => CheckFolder();
        Style(browse, "Browse…", false); browse.SetBounds(466, 26, 110, 30); browse.Click += (s, e) => Browse();
        folderNote.SetBounds(0, 60, 576, 36); folderNote.ForeColor = Muted;
        space.SetBounds(0, 96, 576, 20); space.ForeColor = Muted;
        Label ls = new Label { Text = "Options", ForeColor = Color.Gainsboro, Location = new Point(0, 132), AutoSize = true };
        cDesktop.Text = "Create a desktop shortcut";
        cDesktop.Checked = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TRANSCRYPTO Themer.lnk"));   // an update keeps it
        cStart.Text = "Create a Start menu shortcut"; cStart.Checked = true;
        cLaunch.Text = "Launch TRANSCRYPTO Themer when setup finishes"; cLaunch.Checked = true;
        int y = 158; foreach (CheckBox c in new[] { cDesktop, cStart, cLaunch }) { c.ForeColor = Color.Gainsboro; c.SetBounds(0, y, 576, 26); y += 30; }
        Label safe = new Label
        {
            Text = "Your current look is not changed until you apply a theme. TRANSCRYPTO makes a System Restore point before big changes, " +
                   "and Backup & Restore can always go back to Windows 11 default.", ForeColor = Muted, Location = new Point(0, 256), Size = new Size(576, 40), UseMnemonic = false
        };
        pOptions.Controls.AddRange(new Control[] { lf, folder, browse, folderNote, space, ls, cDesktop, cStart, cLaunch, safe });
        if (current != null) { folder.ReadOnly = true; browse.Enabled = false; }

        // 3. install
        status.SetBounds(0, 0, 576, 22); status.ForeColor = Muted;
        bar.SetBounds(0, 26, 576, 8); bar.Style = ProgressBarStyle.Marquee;
        Box(log); log.SetBounds(0, 44, 576, 250); log.ScrollBars = ScrollBars.Vertical;
        pInstall.Controls.AddRange(new Control[] { status, bar, log });

        Label love = new Label { Text = "Made with ❤ by SharmaJi", ForeColor = Pink, AutoSize = true, Location = new Point(32, 458) };
        Style(back, "Back", false); back.SetBounds(256, 448, 110, 36); back.Click += (s, e) => { page--; Nav(); };
        Style(next, "Next", true); next.SetBounds(498, 448, 110, 36); next.Click += (s, e) => Next();
        Style(cancel, "Cancel", false); cancel.SetBounds(376, 448, 110, 36); cancel.Click += (s, e) => Close();
        Controls.AddRange(new Control[] { title, ver, step, pLicense, pOptions, pInstall, love, back, cancel, next });
        AcceptButton = next;
        CheckFolder(); Nav();
        Shown += (s, e) => { license.Select(0, 0); accept.Focus(); };
    }

    static void Box(TextBox t)
    {
        t.Multiline = true; t.ReadOnly = true; t.BackColor = Field; t.ForeColor = Color.FromArgb(200, 202, 232);
        t.BorderStyle = BorderStyle.None; t.Font = new Font("Consolas", 9f);
    }

    static void Style(Button b, string text, bool primary)
    {
        b.Text = text; b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand; b.ForeColor = Color.White;
        b.BackColor = primary ? Color.FromArgb(0, 120, 140) : Color.FromArgb(28, 28, 48);
        b.FlatAppearance.BorderColor = primary ? Neon : Color.FromArgb(60, 60, 90);
    }

    string FolderError() { return current != null ? null : Installer.CheckFolder(folder.Text); }

    void CheckFolder()
    {
        string err = FolderError();
        if (current != null) { folderNote.ForeColor = Muted; folderNote.Text = "TRANSCRYPTO Themer is already installed here; setup updates it in place. Uninstall it first to move it."; }
        else if (err != null) { folderNote.ForeColor = Bad; folderNote.Text = err; }
        else { folderNote.ForeColor = Muted; folderNote.Text = "Only administrators will be able to change the files in this folder (the app runs part of its code as administrator)."; }
        try
        {
            DriveInfo d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(folder.Text)));
            space.Text = string.Format("Space needed: {0:0} MB     Free on {1}: {2:0.0} GB", Installer.PayloadSize() / 1048576.0, d.Name.TrimEnd('\\'), d.AvailableFreeSpace / 1073741824.0);
        }
        catch { space.Text = ""; }
        Nav();
    }

    void Browse()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Pick where to install TRANSCRYPTO Themer (a \"" + Installer.AppFolder + "\" folder is created inside it)";
            d.ShowNewFolderButton = true;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string p = d.SelectedPath.TrimEnd('\\');
            if (!p.EndsWith("\\" + Installer.AppFolder, StringComparison.OrdinalIgnoreCase)) p = Path.Combine(p, Installer.AppFolder);
            folder.Text = p;
        }
    }

    void Nav()
    {
        pLicense.Visible = page == 0; pOptions.Visible = page == 1; pInstall.Visible = page == 2;
        step.Text = page == 0 ? "1 / 3   License" : page == 1 ? "2 / 3   Install options" : "3 / 3   Installing";
        back.Visible = page == 1;
        if (page == 0) { next.Text = "Next"; next.Enabled = accept.Checked; }
        if (page == 1) { next.Text = current != null ? "Update" : "Install"; next.Enabled = FolderError() == null && accept.Checked; }
    }

    void Next()
    {
        if (page == 0) { page = 1; Nav(); return; }
        if (page == 1) { Install(); return; }
        if (done) Close();
    }

    void Install()
    {
        Options o = new Options { Folder = current != null ? null : Path.GetFullPath(folder.Text.Trim()).TrimEnd('\\'), Desktop = cDesktop.Checked, StartMenu = cStart.Checked, Launch = cLaunch.Checked };
        page = 2; Nav(); next.Enabled = false; cancel.Enabled = false; status.ForeColor = Muted; status.Text = "Installing…"; bar.Style = ProgressBarStyle.Marquee;
        Thread t = new Thread(() =>
        {
            bool ok = Installer.Run(line => BeginInvoke((Action)(() => { log.AppendText(line + Environment.NewLine); })), o);
            BeginInvoke((Action)(() =>
            {
                bar.Style = ProgressBarStyle.Continuous; bar.Value = ok ? 100 : 0;
                status.ForeColor = ok ? Ok : Bad;
                status.Text = ok ? (o.Launch ? "Installed. TRANSCRYPTO Themer is starting." : "Installed. Start it from the Start menu.") : "Setup did not finish - see the details below.";
                if (ok) { done = true; next.Text = "Finish"; next.Enabled = true; cancel.Visible = false; }
                else { page = 1; back.Visible = false; next.Text = "Retry"; next.Enabled = true; cancel.Enabled = true; cancel.Text = "Close"; pInstall.Visible = true; pOptions.Visible = false; }
            }));
        });
        t.IsBackground = true; t.Start();
    }
}
