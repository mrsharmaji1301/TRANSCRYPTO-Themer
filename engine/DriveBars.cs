// TRANSCRYPTO engine - themed drive bars in File Explorer ("This PC" / "Devices and drives").
// Windows draws those capacity bars from its visual style (blue, red when nearly full) and offers no setting for them.
// Instead of patching system theme files, the engine finds the bars in a PrintWindow copy of the Explorer window (our
// overlays are never in that copy) and draws the active preset's own bars exactly on top, in a click-through layered
// window that sits right above its Explorer window. Scrolling or resizing hides them at once; they come back when the
// view settles. All capture and scanning runs on a worker thread.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Transcrypto
{
    public class DriveBars
    {
        delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc fn, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, Native.EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] static extern bool IsHungAppWindow(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")] static extern IntPtr SetWindowLongPtr64(IntPtr h, int idx, IntPtr v);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int idx, int v);

        public bool On;
        public string Style = "soft";     // orbital | hud | beam | neon | matrix | flat | soft
        public Color C1 = Color.Cyan, C2 = Color.Magenta;

        class Hit { public Rectangle R; public float Frac; public bool Warn; public Color Bg; }   // R: relative to the Explorer window
        class Entry { public TintWindow W; public List<Hit> Hits = new List<Hit>(); public Rectangle Win; public string Sig = ""; public volatile bool Busy; }

        readonly Control ui; bool hooked; IntPtr hook = IntPtr.Zero; readonly WinEventProc proc;
        readonly Dictionary<IntPtr, Entry> wins = new Dictionary<IntPtr, Entry>();
        readonly HashSet<IntPtr> pending = new HashSet<IntPtr>();
        readonly System.Windows.Forms.Timer settle, slow;
        readonly uint myPid;

        public DriveBars(Control uiThread)
        {
            ui = uiThread; proc = OnEvent;
            uint p; GetWindowThreadProcessId(uiThread.Handle, out p); myPid = p;
            settle = new System.Windows.Forms.Timer(); settle.Interval = 180; settle.Tick += delegate { settle.Stop(); foreach (IntPtr h in new List<IntPtr>(pending)) Detect(h); pending.Clear(); };
            slow = new System.Windows.Forms.Timer(); slow.Interval = 5000; slow.Tick += delegate { Scan(); };
        }

        // call after changing On / Style / colors
        public void Apply()
        {
            if (On && !hooked)
            {
                hooked = true;
                // FOREGROUND, scrolling start/end, minimize/restore (0x3-0x17) .. object show/hide, reorder, location, value (0x8002-0x800E)
                hook = SetWinEventHook(0x0003, 0x800E, IntPtr.Zero, proc, 0, 0, 0x0000 /*OUTOFCONTEXT*/);
                slow.Start();
            }
            else if (!On && hooked)
            {
                hooked = false; slow.Stop(); settle.Stop();
                if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
                foreach (Entry e in wins.Values) { e.W.Close(); e.W.Dispose(); }
                wins.Clear();
                return;
            }
            foreach (Entry e in wins.Values) e.Sig = "";   // style or colors changed: redraw
            Scan();
        }

        // pick up Explorer windows; drop closed ones
        void Scan()
        {
            if (!On) return;
            List<IntPtr> found = new List<IntPtr>();
            Native.EnumWindows(delegate (IntPtr h, IntPtr l) { if (Native.ClassOf(h) == "CabinetWClass") found.Add(h); return true; }, IntPtr.Zero);
            foreach (IntPtr h in new List<IntPtr>(wins.Keys))
                if (!found.Contains(h) || !Native.IsWindow(h)) { wins[h].W.Close(); wins[h].W.Dispose(); wins.Remove(h); }
            foreach (IntPtr h in found) Detect(h);
        }

        void OnEvent(IntPtr hk, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero || !On) return;
            if (ev == 0x0003 || ev == 0x8004) { foreach (KeyValuePair<IntPtr, Entry> kv in wins) Restack(kv.Key, kv.Value); if (ev == 0x0003) Kick(GetAncestor(hwnd, 2)); return; }
            IntPtr root = GetAncestor(hwnd, 2 /*GA_ROOT*/);
            if (root == IntPtr.Zero || Native.ClassOf(root) != "CabinetWClass") return;
            Entry e; wins.TryGetValue(root, out e);
            if (e != null)
            {
                if (hwnd == root && ev == 0x800B && obj == 0)
                {
                    // the window itself moved: a pure move just carries the bars along
                    Rectangle now = RectOf(root);
                    if (now.Size == e.Win.Size && e.Win.Width > 0 && e.W.Visible)
                    {
                        e.Win = now; Place(e);
                        return;
                    }
                }
                // content changed (scroll, resize, navigation, minimise): no stale bars on screen while it settles
                if (ev == 0x0012 || ev == 0x0016 || ev == 0x8003 || (ev == 0x800B && hwnd != root) || ev == 0x800E || (hwnd == root && ev == 0x800B)) HideBars(e);
            }
            Kick(root);
        }

        void Kick(IntPtr root)
        {
            if (root == IntPtr.Zero || Native.ClassOf(root) != "CabinetWClass") return;
            pending.Add(root); settle.Stop(); settle.Start();
        }

        void HideBars(Entry e) { if (e.W.Visible) ShowWindow(e.W.Handle, 0); e.Sig = ""; }

        static Rectangle RectOf(IntPtr h) { Native.RECT r; Native.GetWindowRect(h, out r); return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); }

        // capture + scan on a worker thread, draw on the UI thread
        void Detect(IntPtr owner)
        {
            Entry e;
            if (!wins.TryGetValue(owner, out e))
            {
                e = new Entry(); e.W = new TintWindow();
                IntPtr force = e.W.Handle;
                if (IntPtr.Size == 8) SetWindowLongPtr64(e.W.Handle, -8 /*GWLP_HWNDPARENT*/, owner); else SetWindowLong32(e.W.Handle, -8, owner.ToInt32());
                wins[owner] = e;
            }
            if (e.Busy) { pending.Add(owner); settle.Stop(); settle.Start(); return; }
            if (!Native.IsWindowVisible(owner) || Native.IsIconic(owner) || Native.IsCloaked(owner)) { HideBars(e); return; }
            e.Busy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Hit> hits = null; Rectangle win = Rectangle.Empty;
                try { if (!IsHungAppWindow(owner)) hits = Find(owner, out win); }
                catch (Exception ex) { Program.Log("drive bars: " + ex.Message); }
                try
                {
                    ui.BeginInvoke(new MethodInvoker(delegate
                    {
                        e.Busy = false;
                        Entry cur; if (!wins.TryGetValue(owner, out cur) || cur != e) return;
                        List<Hit> nh = hits ?? new List<Hit>();
                        if (nh.Count != e.Hits.Count) Program.Log("drive bars: " + nh.Count + " in an Explorer window (" + Style + ")");
                        e.Hits = nh; e.Win = win;
                        Draw(owner, e);
                    }));
                }
                catch { e.Busy = false; }
            });
        }

        // ---------- finding the bars in the window copy
        static bool IsFill(int r, int g, int b)
        {
            int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            if (mx - mn < 80) return false;
            return (b > r + 70 && b > g + 25) || (r > g + 80 && r > b + 80);   // the visual style's blue, or its red (nearly full)
        }
        static bool IsRed(int r, int g, int b) { return r > g + 80 && r > b + 80; }
        // the empty part of the bar is a light gray (#E6E6E6); white page backgrounds (light Control Panel pages) are not
        static bool IsTrack(int r, int g, int b) { int mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)); return mn >= 200 && mx <= 242 && mx - mn <= 14; }

        List<Hit> Find(IntPtr owner, out Rectangle win)
        {
            win = RectOf(owner);
            List<Hit> hits = new List<Hit>();
            Rectangle view = Rectangle.Empty;
            EnumChildWindows(owner, delegate (IntPtr c, IntPtr l)
            {
                if (Native.IsWindowVisible(c) && Native.ClassOf(c) == "SHELLDLL_DefView") { Rectangle r = RectOf(c); if (r.Width * r.Height > view.Width * view.Height) view = r; }
                return true;
            }, IntPtr.Zero);
            if (view.Width < 80 || view.Height < 30 || win.Width < 80) return hits;
            view.Intersect(win);
            using (Bitmap bmp = new Bitmap(win.Width, win.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    try { if (!PrintWindow(owner, hdc, 2 /*PW_RENDERFULLCONTENT*/)) return hits; } finally { g.ReleaseHdc(hdc); }
                }
                Rectangle area = new Rectangle(view.X - win.X, view.Y - win.Y, view.Width, view.Height);
                area.Intersect(new Rectangle(0, 0, win.Width, win.Height));
                BitmapData bd = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                int stride = bd.Stride; byte[] px = new byte[stride * bmp.Height];
                Marshal.Copy(bd.Scan0, px, 0, px.Length); bmp.UnlockBits(bd);
                // per row: runs of [fill][track] at least 70 px long
                List<int[]> runs = new List<int[]>();   // y, x0, x1, fillEnd, red
                for (int y = area.Top; y < area.Bottom; y++)
                {
                    int x = area.Left, row = y * stride;
                    while (x < area.Right)
                    {
                        int i = row + x * 4; int b = px[i], gg = px[i + 1], r = px[i + 2];
                        // a bar always starts with its colored part (a drive bar with nothing used is left as Windows draws it)
                        if (!IsFill(r, gg, b)) { x++; continue; }
                        bool f = true;
                        int x0 = x, fillEnd = x; bool red = false, inFill = f;
                        while (x < area.Right)
                        {
                            i = row + x * 4; b = px[i]; gg = px[i + 1]; r = px[i + 2];
                            if (inFill && IsFill(r, gg, b)) { red |= IsRed(r, gg, b); x++; fillEnd = x; continue; }
                            if (IsTrack(r, gg, b)) { inFill = false; x++; continue; }
                            break;
                        }
                        if (x - x0 >= 70 && x - x0 <= 900 && fillEnd - x0 >= 2) runs.Add(new int[] { y, x0, x, fillEnd, red ? 1 : 0 });
                        if (x == x0) x++;
                    }
                }
                // stack rows into bars: rows (top to bottom) join a bar whose span they match within a few px; the edge rows of
                // the native bar are anti-aliased and a little shorter, so an exact match would cut the bar in pieces
                List<int[]> groups = new List<int[]>();   // x0, x1 (widest), top, bottom, fillEnd, red
                foreach (int[] s in runs)   // already in row order
                {
                    int[] gr = groups.Find(q => s[0] <= q[3] + 2 && Math.Abs(s[1] - q[0]) <= 3 && Math.Abs(s[2] - q[1]) <= 6);
                    if (gr == null) { groups.Add(new int[] { s[1], s[2], s[0], s[0], s[3], s[4] }); continue; }
                    gr[0] = Math.Min(gr[0], s[1]); gr[1] = Math.Max(gr[1], s[2]); gr[3] = s[0]; gr[4] = Math.Max(gr[4], s[3]); gr[5] |= s[4];
                }
                foreach (int[] q in groups)
                {
                    int hgt = q[3] - q[2] + 1;
                    if (hgt < 6 || hgt > 24) continue;
                    Hit h = new Hit(); h.R = Rectangle.FromLTRB(q[0], q[2], q[1], q[3] + 1);
                    h.Frac = Math.Max(0f, Math.Min(1f, (q[4] - q[0]) / (float)Math.Max(1, q[1] - q[0]))); h.Warn = q[5] != 0;
                    // the tile behind the bar (plain, hovered or selected): sampled just right of the bar, mid height
                    int sx = Math.Min(win.Width - 1, q[1] + 3), sy = (q[2] + q[3]) / 2, si = sy * stride + sx * 4;
                    h.Bg = Color.FromArgb(255, px[si + 2], px[si + 1], px[si]);
                    hits.Add(h);
                }
            }
            if (hits.Count > 48) { Program.Log("drive bars: " + hits.Count + " bar-like shapes, not a drive view - ignored"); return new List<Hit>(); }
            // every drive tile has the same bar: one size for all (the largest found), each kept on its own centre line,
            // so no bar ends up thinner, shorter or shifted and no edge of Windows' bar peeks out
            if (hits.Count > 1)
            {
                int W = 0, H = 0; foreach (Hit h in hits) { W = Math.Max(W, h.R.Width); H = Math.Max(H, h.R.Height); }
                foreach (Hit h in hits)
                {
                    if (h.R.Width < W * 0.8 || h.R.Height < H * 0.6) continue;   // a different kind of bar: leave it
                    float used = h.Frac * h.R.Width; int cy = h.R.Y + h.R.Height / 2;
                    h.R = new Rectangle(h.R.X, cy - H / 2, W, H);
                    h.Frac = Math.Max(0f, Math.Min(1f, used / W));
                }
            }
            return hits;
        }

        // ---------- drawing
        void Draw(IntPtr owner, Entry e)
        {
            if (e.Hits.Count == 0 || e.Win.Width <= 0) { HideBars(e); return; }
            Rectangle u = Rectangle.Empty;
            foreach (Hit h in e.Hits) u = u.IsEmpty ? h.R : Rectangle.Union(u, h.R);
            u.Inflate(10, 10);   // room for glows
            System.Text.StringBuilder sb = new System.Text.StringBuilder(Style + C1.ToArgb() + "," + C2.ToArgb() + "|");
            foreach (Hit h in e.Hits) sb.Append(h.R).Append(h.Frac.ToString("0.000")).Append(h.Warn ? "!" : "").Append(h.Bg.ToArgb()).Append(';');
            string sig = sb.ToString();
            if (sig != e.Sig)
            {
                using (Bitmap bmp = new Bitmap(u.Width, u.Height, PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                        foreach (Hit h in e.Hits) DrawBar(g, new RectangleF(h.R.X - u.X, h.R.Y - u.Y, h.R.Width, h.R.Height), h.Frac, h.Warn, h.Bg);
                    }
                    Layered.Push(e.W.Handle, bmp, e.Win.X + u.X, e.Win.Y + u.Y);
                }
                e.Sig = sig; e.W.Tag = new Point(u.X, u.Y);
            }
            else Place(e);
            if (!e.W.Visible) ShowWindow(e.W.Handle, 4 /*SW_SHOWNOACTIVATE*/);
            Restack(owner, e);
        }

        static void Place(Entry e)
        {
            if (!(e.W.Tag is Point)) return;
            Point o = (Point)e.W.Tag;
            Native.SetWindowPos(e.W.Handle, IntPtr.Zero, e.Win.X + o.X, e.Win.Y + o.Y, 0, 0, 0x0001 | 0x0004 | 0x0010);
        }

        // right above its Explorer window and above our other overlays of it (the tint)
        void Restack(IntPtr owner, Entry e)
        {
            if (!e.W.Visible) return;
            IntPtr above = GetWindow(owner, 3 /*GW_HWNDPREV*/);
            while (above != IntPtr.Zero && above != e.W.Handle) { uint pid; GetWindowThreadProcessId(above, out pid); if (pid != myPid) break; above = GetWindow(above, 3); }
            if (above == e.W.Handle) return;
            Native.SetWindowPos(e.W.Handle, above, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        static Color A(Color c, int a) { return Color.FromArgb(Math.Max(0, Math.Min(255, a)), c); }
        static Color Mix(Color a, Color b, float t) { return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t)); }
        static GraphicsPath Pill(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath(); rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2f)); float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure(); return p;
        }
        static void Grad(Graphics g, RectangleF r, Color a, Color b, GraphicsPath clip)
        {
            if (r.Width < 1 || r.Height < 1) return;
            using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X - 1, r.Y, r.Width + 2, r.Height), a, b, 0f))
            { if (clip != null) g.FillPath(br, clip); else g.FillRectangle(br, r); }
        }
        static void Glow(Graphics g, RectangleF r, Color c, int layers, int alpha)
        {
            for (int i = layers; i >= 1; i--)
                using (GraphicsPath p = Pill(RectangleF.Inflate(r, i * 1.6f, i * 1.6f), r.Height / 2 + i * 1.6f))
                using (SolidBrush b = new SolidBrush(A(c, alpha / (i + 1)))) g.FillPath(b, p);
        }

        void DrawBar(Graphics g, RectangleF r, float frac, bool warn, Color back)
        {
            Color c1 = C1, c2 = C2, bad = Color.FromArgb(255, 77, 109);
            if (warn) { c1 = Mix(c1, bad, 0.55f); c2 = bad; }
            float w = Math.Max(frac > 0 ? 2f : 0f, r.Width * frac);
            // the native bar underneath must not show through: an opaque base in the Explorer background color first
            using (SolidBrush bg = new SolidBrush(back)) g.FillRectangle(bg, RectangleF.Inflate(r, 2, 2));
            switch (Style)
            {
                case "orbital":   // Orbital's live stats cards: glass pill, gradient fill with a glow and a bright tip
                {
                    RectangleF t = new RectangleF(r.X, r.Y + r.Height * 0.2f, r.Width, r.Height * 0.6f);
                    using (GraphicsPath p = Pill(t, t.Height / 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillPath(b, p);
                    if (w > 0)
                    {
                        RectangleF f = new RectangleF(t.X, t.Y, Math.Max(t.Height, w), t.Height);
                        Glow(g, f, c2, 4, 120);
                        using (GraphicsPath p = Pill(f, f.Height / 2)) Grad(g, f, c1, c2, p);
                        using (GraphicsPath p = Pill(new RectangleF(f.X + 2, f.Y + 1, f.Width - 4, f.Height * 0.35f), f.Height * 0.2f))
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, 255, 255, 255))) g.FillPath(b, p);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(235, 255, 255, 255))) g.FillEllipse(b, f.Right - f.Height + 1, f.Y + 1, f.Height - 2, f.Height - 2);
                    }
                    break;
                }
                case "hud":       // SharmaJi Command Center: segmented HUD gauge, saffron to cyan, with corner brackets
                {
                    using (Pen o = new Pen(A(c2, 150), 1f)) g.DrawRectangle(o, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                    int segs = Math.Max(8, (int)(r.Width / 9)); float sw = (r.Width - 4) / segs; int lit = (int)Math.Round(segs * frac);
                    for (int i = 0; i < segs; i++)
                    {
                        RectangleF s = new RectangleF(r.X + 2 + i * sw + 1, r.Y + 3, sw - 2, r.Height - 6);
                        Color c = i < lit ? Mix(c1, c2, segs <= 1 ? 0 : i / (float)(segs - 1)) : Color.FromArgb(38, 255, 255, 255);
                        using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, s);
                    }
                    using (Pen k = new Pen(c1, 2f))
                    {
                        float L = Math.Min(8, r.Height);
                        g.DrawLines(k, new[] { new PointF(r.X - 2, r.Y + L), new PointF(r.X - 2, r.Y - 2), new PointF(r.X + L, r.Y - 2) });
                        g.DrawLines(k, new[] { new PointF(r.Right + 2, r.Bottom - L), new PointF(r.Right + 2, r.Bottom + 2), new PointF(r.Right - L, r.Bottom + 2) });
                    }
                    break;
                }
                case "beam":      // Projector: a thin platinum rail with a beam of light and a bright lamp at its head
                {
                    float cy = r.Y + r.Height / 2;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(55, 220, 230, 240))) g.FillRectangle(b, r.X, cy - 1, r.Width, 2);
                    if (w > 0)
                    {
                        RectangleF beam = new RectangleF(r.X, cy - r.Height * 0.32f, w, r.Height * 0.64f);
                        using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(beam.X - 1, beam.Y, beam.Width + 2, beam.Height), Color.FromArgb(10, c1), Color.FromArgb(150, c2), 0f))
                            g.FillRectangle(br, beam);
                        using (LinearGradientBrush br = new LinearGradientBrush(new RectangleF(r.X - 1, cy - 1, w + 2, 2), Color.FromArgb(120, 255, 255, 255), Color.FromArgb(255, 255, 255, 255), 0f))
                            g.FillRectangle(br, r.X, cy - 1, w, 2);
                        Glow(g, new RectangleF(r.X + w - 4, cy - 4, 8, 8), Color.White, 4, 160);
                        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, r.X + w - 3.5f, cy - 3.5f, 7, 7);
                    }
                    break;
                }
                case "neon":      // neon presets: a glowing tube
                {
                    using (GraphicsPath p = Pill(r, r.Height / 2)) using (Pen o = new Pen(A(c1, 110), 1f)) g.DrawPath(o, p);
                    if (w > 0)
                    {
                        RectangleF f = new RectangleF(r.X + 2, r.Y + 2, Math.Max(r.Height - 4, w - 4), r.Height - 4);
                        Glow(g, f, c1, 5, 150);
                        using (GraphicsPath p = Pill(f, f.Height / 2)) Grad(g, f, c1, c2, p);
                        using (GraphicsPath p = Pill(new RectangleF(f.X + 2, f.Y + f.Height * 0.38f, f.Width - 4, f.Height * 0.24f), 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(190, 255, 255, 255))) g.FillPath(b, p);
                    }
                    break;
                }
                case "matrix":    // Matrix: a terminal gauge of green blocks
                {
                    using (Pen o = new Pen(A(c2, 160), 1f)) g.DrawRectangle(o, r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
                    int segs = Math.Max(10, (int)(r.Width / 6)); float sw = (r.Width - 4) / segs; int lit = (int)Math.Round(segs * frac);
                    for (int i = 0; i < lit; i++) using (SolidBrush b = new SolidBrush(i == lit - 1 ? Color.FromArgb(220, 255, 220) : c1)) g.FillRectangle(b, r.X + 2 + i * sw, r.Y + 2, sw - 1, r.Height - 4);
                    break;
                }
                case "flat":      // mono presets: quiet, square, single tone
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(46, 255, 255, 255))) g.FillRectangle(b, r.X, r.Y + 2, r.Width, r.Height - 4);
                    if (w > 0) using (SolidBrush b = new SolidBrush(c1)) g.FillRectangle(b, r.X, r.Y + 2, w, r.Height - 4);
                    break;
                }
                default:          // soft: the preset's accent pair in a rounded glossy bar
                {
                    using (GraphicsPath p = Pill(r, r.Height / 2)) using (SolidBrush b = new SolidBrush(Color.FromArgb(44, 255, 255, 255))) g.FillPath(b, p);
                    if (w > 0)
                    {
                        RectangleF f = new RectangleF(r.X, r.Y, Math.Max(r.Height, w), r.Height);
                        using (GraphicsPath p = Pill(f, f.Height / 2)) Grad(g, f, c1, c2, p);
                        using (GraphicsPath p = Pill(new RectangleF(f.X + 3, f.Y + 1.5f, f.Width - 6, f.Height * 0.4f), f.Height * 0.2f)) using (SolidBrush b = new SolidBrush(Color.FromArgb(80, 255, 255, 255))) g.FillPath(b, p);
                    }
                    break;
                }
            }
        }
    }
}
