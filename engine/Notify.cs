// TRANSCRYPTO Themer - Windows notifications for the custom shells.
// A hidden PowerShell helper (shell\notify-watch.ps1) reads them through UserNotificationListener and streams JSON lines;
// this side keeps the list for /api/notifs, tells the page about new ones ("notif:<id>") and passes dismiss/clear back.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Transcrypto
{
    public class NotifyWatch
    {
        readonly string script; Process proc; Thread reader; volatile bool running;
        readonly List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        public Action<string> OnEvent;   // "notif:<id>" for a new one, "notifs" when the list changed
        public bool Denied;
        // what the user cleared or dismissed stays gone, also after a restart and even if Windows keeps it in its own centre
        readonly string hiddenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TRANSCRYPTO", "notifs-hidden.json");
        readonly HashSet<string> hidden = new HashSet<string>(); DateTime clearedAt = DateTime.MinValue;

        public NotifyWatch(string appRoot) { script = Path.Combine(appRoot, "shell", "notify-watch.ps1"); LoadHidden(); }

        void LoadHidden()
        {
            try
            {
                if (!File.Exists(hiddenPath)) return;
                Dictionary<string, object> d = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(hiddenPath));
                object v;
                if (d.TryGetValue("clearedAt", out v)) DateTime.TryParse(Convert.ToString(v), null, System.Globalization.DateTimeStyles.RoundtripKind, out clearedAt);
                if (d.TryGetValue("ids", out v) && v is System.Collections.ArrayList) foreach (object id in (System.Collections.ArrayList)v) hidden.Add(Convert.ToString(id));
            }
            catch { }
        }

        void SaveHidden()
        {
            try
            {
                List<string> ids; lock (hidden) ids = new List<string>(hidden);
                if (ids.Count > 400) ids = ids.GetRange(ids.Count - 400, 400);
                File.WriteAllText(hiddenPath, Json.Serialize(new Dictionary<string, object> { { "clearedAt", clearedAt.ToString("o") }, { "ids", ids } }));
            }
            catch { }
        }

        bool IsHidden(string id, object time)
        {
            lock (hidden) if (hidden.Contains(id)) return true;
            DateTime t;
            return clearedAt > DateTime.MinValue && DateTime.TryParse(Convert.ToString(time), null, System.Globalization.DateTimeStyles.RoundtripKind, out t) && t.ToUniversalTime() <= clearedAt.ToUniversalTime();
        }

        public void Start()
        {
            if (running || !File.Exists(script)) return;
            running = true; Denied = false;
            reader = new Thread(Run); reader.IsBackground = true; reader.Start();
        }

        public void Stop()
        {
            running = false;
            Process p = proc; proc = null;
            try { if (p != null && !p.HasExited) p.Kill(); } catch { }
            lock (list) list.Clear();
        }

        void Run()
        {
            int fails = 0;
            while (running && fails < 5)
            {
                try
                {
                    ProcessStartInfo si = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"");
                    si.UseShellExecute = false; si.CreateNoWindow = true; si.RedirectStandardOutput = true; si.RedirectStandardInput = true;
                    si.StandardOutputEncoding = Encoding.UTF8;
                    proc = Process.Start(si);
                    string line;
                    while (running && (line = proc.StandardOutput.ReadLine()) != null) Handle(line);
                }
                catch (Exception ex) { Program.Log("notifications: " + ex.Message); }
                if (Denied) break;
                fails++; Thread.Sleep(3000);   // helper died: start it again, a few times
            }
            running = false;
        }

        void Handle(string line)
        {
            Dictionary<string, object> m;
            try { m = Json.Deserialize<Dictionary<string, object>>(line); } catch { return; }
            object op; m.TryGetValue("op", out op);
            string id = m.ContainsKey("id") ? Convert.ToString(m["id"]) : "";
            switch (op as string)
            {
                case "add":
                    object tm; m.TryGetValue("time", out tm);
                    if (IsHidden(id, tm)) { uint n; if (uint.TryParse(id, out n)) Send("remove " + n); break; }   // cleared before: try Windows' copy again, never show it
                    m.Remove("op"); m["id"] = id;
                    lock (list) { list.RemoveAll(x => (string)x["id"] == id); list.Insert(0, m); if (list.Count > 60) list.RemoveAt(list.Count - 1); }
                    if (OnEvent != null) OnEvent("notif:" + id);
                    break;
                case "remove":
                    lock (list) list.RemoveAll(x => (string)x["id"] == id);
                    if (OnEvent != null) OnEvent("notifs");
                    break;
                case "denied": Denied = true; Program.Log("notifications: access denied"); break;
            }
        }

        public List<Dictionary<string, object>> List() { lock (list) return new List<Dictionary<string, object>>(list); }

        void Send(string cmd) { try { Process p = proc; if (p != null && !p.HasExited) { p.StandardInput.WriteLine(cmd); p.StandardInput.Flush(); } } catch { } }

        public void Dismiss(string id)
        {
            uint n; if (!uint.TryParse(id, out n)) return;
            lock (list) list.RemoveAll(x => (string)x["id"] == id);
            lock (hidden) hidden.Add(id); SaveHidden();
            Send("remove " + n);
            if (OnEvent != null) OnEvent("notifs");
        }

        public void Clear()
        {
            lock (list) { foreach (Dictionary<string, object> x in list) lock (hidden) hidden.Add((string)x["id"]); list.Clear(); }
            clearedAt = DateTime.UtcNow; SaveHidden();
            Send("clear"); if (OnEvent != null) OnEvent("notifs");
        }

        public string Aumid(string id) { lock (list) foreach (Dictionary<string, object> x in list) if ((string)x["id"] == id) return x["aumid"] as string; return null; }
    }
}
