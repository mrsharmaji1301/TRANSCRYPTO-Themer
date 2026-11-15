# Cursors: TRANSCRYPTO packs, Windows built-in schemes, imported packs, per-role custom files, size

$P_CUR = 'HKCU:\Control Panel\Cursors'
$TC.CursorRoles = @('Arrow', 'Help', 'AppStarting', 'Wait', 'Crosshair', 'IBeam', 'NWPen', 'No', 'SizeNS', 'SizeWE', 'SizeNWSE', 'SizeNESW', 'SizeAll', 'UpArrow', 'Hand', 'Pin', 'Person')
$TC.CursorDefaults = [ordered]@{
    Arrow = '%SystemRoot%\cursors\aero_arrow.cur'; Help = '%SystemRoot%\cursors\aero_helpsel.cur'; AppStarting = '%SystemRoot%\cursors\aero_working.ani'
    Wait = '%SystemRoot%\cursors\aero_busy.ani'; Crosshair = ''; IBeam = ''; NWPen = '%SystemRoot%\cursors\aero_pen.cur'; No = '%SystemRoot%\cursors\aero_unavail.cur'
    SizeNS = '%SystemRoot%\cursors\aero_ns.cur'; SizeWE = '%SystemRoot%\cursors\aero_ew.cur'; SizeNWSE = '%SystemRoot%\cursors\aero_nwse.cur'
    SizeNESW = '%SystemRoot%\cursors\aero_nesw.cur'; SizeAll = '%SystemRoot%\cursors\aero_move.cur'; UpArrow = '%SystemRoot%\cursors\aero_up.cur'
    Hand = '%SystemRoot%\cursors\aero_link.cur'; Pin = '%SystemRoot%\cursors\aero_pin.cur'; Person = '%SystemRoot%\cursors\aero_person.cur'
}
Register-Tracked 'cursors' $P_CUR '(default)' 'String' 'Windows Default'
Register-Tracked 'cursors' $P_CUR 'Scheme Source' 'DWord' 2
Register-Tracked 'cursors' $P_CUR 'CursorBaseSize' 'DWord' $null
foreach ($r in $TC.CursorRoles) { Register-Tracked 'cursors' $P_CUR $r 'ExpandString' $TC.CursorDefaults[$r] }

# Windows cannot go below a 32 px cursor base size, so for 20-31 px we redraw the cursor files
# smaller inside their 32/48/64 px canvases (hotspot scaled too). Works for .cur and animated .ani.
if (-not ('TCCursorShrink' -as [type])) {
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.IO; using System.Collections.Generic; using System.Drawing; using System.Drawing.Drawing2D; using System.Drawing.Imaging;
public static class TCCursorShrink {
    static int[] Sizes = new int[] { 32, 48, 64 };
    public static void ShrinkFile(string src, string dst, double scale) {
        byte[] b = File.ReadAllBytes(src);
        byte[] o = (b.Length > 12 && b[0] == 'R' && b[1] == 'I' && b[8] == 'A') ? ShrinkAni(b, scale) : ShrinkCur(b, scale);
        File.WriteAllBytes(dst, o);
    }
    public static byte[] ShrinkCur(byte[] b, double scale) {
        int n = BitConverter.ToUInt16(b, 4); int best = -1, bestSize = 0;
        for (int i = 0; i < n; i++) { int w = b[6 + 16 * i]; if (w == 0) w = 256; if (w > bestSize) { bestSize = w; best = i; } }
        int e = 6 + 16 * best;
        int hx = BitConverter.ToUInt16(b, e + 4), hy = BitConverter.ToUInt16(b, e + 6);
        int len = BitConverter.ToInt32(b, e + 8), off = BitConverter.ToInt32(b, e + 12);
        Bitmap srcBmp = Decode(b, e, len, off);
        int S = srcBmp.Width;
        MemoryStream ms = new MemoryStream(); BinaryWriter w2 = new BinaryWriter(ms);
        List<byte[]> blobs = new List<byte[]>(); List<int[]> hs = new List<int[]>();
        foreach (int N in Sizes) {
            double f = (double)N / S * scale;
            using (Bitmap c = new Bitmap(N, N, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(c)) {
                g.Clear(Color.Transparent); g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(srcBmp, new Rectangle(0, 0, (int)Math.Round(S * f), (int)Math.Round(S * f)));
                blobs.Add(Dib(c)); hs.Add(new int[] { (int)Math.Round(hx * f), (int)Math.Round(hy * f) });
            }
        }
        srcBmp.Dispose();
        w2.Write((ushort)0); w2.Write((ushort)2); w2.Write((ushort)Sizes.Length);
        int pos = 6 + 16 * Sizes.Length;
        for (int i = 0; i < Sizes.Length; i++) {
            w2.Write((byte)Sizes[i]); w2.Write((byte)Sizes[i]); w2.Write((byte)0); w2.Write((byte)0);
            w2.Write((ushort)hs[i][0]); w2.Write((ushort)hs[i][1]); w2.Write(blobs[i].Length); w2.Write(pos); pos += blobs[i].Length;
        }
        foreach (byte[] x in blobs) w2.Write(x);
        return ms.ToArray();
    }
    static Bitmap Decode(byte[] b, int e, int len, int off) {
        if (b[off] == 0x89 && b[off + 1] == 'P') return new Bitmap(new MemoryStream(b, off, len));
        // DIB frame: wrap it as a one-image .ico and let System.Drawing handle masks and palettes
        MemoryStream ms = new MemoryStream(); BinaryWriter w = new BinaryWriter(ms);
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)1);
        w.Write(b, e, 4); w.Write((ushort)1); w.Write((ushort)32); w.Write(len); w.Write(22); w.Write(b, off, len);
        ms.Position = 0;
        using (Icon ic = new Icon(ms)) return ic.ToBitmap();
    }
    static byte[] Dib(Bitmap c) {
        int N = c.Width; MemoryStream ms = new MemoryStream(); BinaryWriter w = new BinaryWriter(ms);
        int mask = ((N + 31) / 32) * 4 * N;
        w.Write(40); w.Write(N); w.Write(N * 2); w.Write((ushort)1); w.Write((ushort)32); w.Write(0); w.Write(N * N * 4 + mask); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        for (int y = N - 1; y >= 0; y--) for (int x = 0; x < N; x++) { Color p = c.GetPixel(x, y); w.Write(p.B); w.Write(p.G); w.Write(p.R); w.Write(p.A); }
        w.Write(new byte[mask]);
        return ms.ToArray();
    }
    public static byte[] ShrinkAni(byte[] b, double scale) {
        MemoryStream body = new MemoryStream(); BinaryWriter w = new BinaryWriter(body);
        w.Write(new byte[] { (byte)'A', (byte)'C', (byte)'O', (byte)'N' });
        int p = 12;
        while (p + 8 <= b.Length) {
            string id = System.Text.Encoding.ASCII.GetString(b, p, 4); int len = BitConverter.ToInt32(b, p + 4);
            if (id == "LIST" && System.Text.Encoding.ASCII.GetString(b, p + 8, 4) == "fram") {
                MemoryStream fr = new MemoryStream(); BinaryWriter fw = new BinaryWriter(fr);
                fw.Write(new byte[] { (byte)'f', (byte)'r', (byte)'a', (byte)'m' });
                int q = p + 12, end = p + 8 + len;
                while (q + 8 <= end) {
                    int il = BitConverter.ToInt32(b, q + 4); byte[] cur = new byte[il]; Array.Copy(b, q + 8, cur, 0, il);
                    byte[] sm = ShrinkCur(cur, scale);
                    fw.Write(new byte[] { (byte)'i', (byte)'c', (byte)'o', (byte)'n' }); fw.Write(sm.Length); fw.Write(sm); if (sm.Length % 2 == 1) fw.Write((byte)0);
                    q += 8 + il + (il % 2);
                }
                byte[] fb = fr.ToArray();
                w.Write(new byte[] { (byte)'L', (byte)'I', (byte)'S', (byte)'T' }); w.Write(fb.Length); w.Write(fb);
            } else { w.Write(b, p, 8 + len); if (len % 2 == 1) w.Write((byte)0); }
            p += 8 + len + (len % 2);
        }
        byte[] bb = body.ToArray();
        MemoryStream o = new MemoryStream(); BinaryWriter ow = new BinaryWriter(o);
        ow.Write(new byte[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); ow.Write(bb.Length); ow.Write(bb);
        return o.ToArray();
    }
}
'@
}

$TC.SmallCursorsDir = Join-Path $TC.Data 'cursors-small'

# Returns role -> file map with every file redrawn at Size/32 scale
function New-SmallCursorSet($Roles, [int]$Size) {
    $dir = Join-Path $TC.SmallCursorsDir ((Get-Date -Format 'yyyyMMddHHmmss') + "-$Size")
    New-Item -ItemType Directory $dir -Force | Out-Null
    $src = [ordered]@{}; $out = [ordered]@{}
    foreach ($r in $Roles.Keys) {
        $f = [Environment]::ExpandEnvironmentVariables([string]$Roles[$r])
        $f = Resolve-OriginalCursor $f
        $src[$r] = $f
        if (-not $f -or -not (Test-Path -LiteralPath $f)) { $out[$r] = $Roles[$r]; continue }
        $dst = Join-Path $dir ($r + [IO.Path]::GetExtension($f))
        try { [TCCursorShrink]::ShrinkFile($f, $dst, $Size / 32.0); $out[$r] = $dst } catch { Write-TCLog "shrink $r failed: $_"; $out[$r] = $f }
    }
    Write-Json $src (Join-Path $dir 'source.json')
    Get-ChildItem $TC.SmallCursorsDir -Directory | Where-Object { $_.FullName -ne $dir } | Sort-Object Name -Descending | Select-Object -Skip 1 | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    return $out
}

# A file inside cursors-small maps back to the cursor it was made from (never shrink twice)
function Resolve-OriginalCursor([string]$Path) {
    if (-not $Path -or -not $Path.StartsWith($TC.SmallCursorsDir, [StringComparison]::OrdinalIgnoreCase)) { return $Path }
    $map = Read-Json (Join-Path (Split-Path $Path) 'source.json') @{}
    $role = [IO.Path]::GetFileNameWithoutExtension($Path)
    if ($map.Contains($role)) { return [string]$map[$role] }
    return $Path
}

$TC.CursorPacksDir = Join-Path $TC.Assets 'cursors'
$TC.UserCursorsDir = Join-Path $TC.Data 'cursors'
if (-not (Test-Path $TC.UserCursorsDir)) { New-Item -ItemType Directory $TC.UserCursorsDir -Force | Out-Null }

function Get-CursorPacks {
    $packs = @()
    foreach ($root in @($TC.CursorPacksDir, $TC.UserCursorsDir)) {
        foreach ($d in (Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $meta = Read-Json (Join-Path $d.FullName 'pack.json') @{}
            $roles = [ordered]@{}
            foreach ($r in $TC.CursorRoles) {
                foreach ($e in @('.ani', '.cur')) { $f = Join-Path $d.FullName ($r + $e); if (Test-Path $f) { $roles[$r] = $f; break } }
            }
            $packs += @{ id = 'pack:' + $d.Name; name = if ($meta.name) { $meta.name } else { $d.Name }; source = $(if ($root -eq $TC.UserCursorsDir) { 'imported' } else { 'transcrypto' })
                         color = $meta.color; roles = $roles; preview = $(if (Test-Path (Join-Path $d.FullName 'preview.png')) { Join-Path $d.FullName 'preview.png' } else { $roles.Arrow }) }
        }
    }
    # Windows built-in schemes (HKLM system schemes + HKCU user schemes)
    foreach ($src in @(@{ p = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes'; s = 'windows' }, @{ p = 'HKCU:\Control Panel\Cursors\Schemes'; s = 'user' })) {
        $k = Get-Item -LiteralPath $src.p -ErrorAction SilentlyContinue
        if (-not $k) { continue }
        foreach ($n in $k.GetValueNames()) {
            $list = ([string]$k.GetValue($n, '', 'DoNotExpandEnvironmentNames')) -split ','
            $roles = [ordered]@{}
            for ($i = 0; $i -lt $TC.CursorRoles.Count -and $i -lt $list.Count; $i++) { $roles[$TC.CursorRoles[$i]] = $list[$i] }
            $packs += @{ id = 'scheme:' + $n; name = $n; source = $src.s; roles = $roles; preview = [Environment]::ExpandEnvironmentVariables([string]$roles.Arrow) }
        }
    }
    return ,$packs
}

function Set-TCCursors($cfg) {
    $roles = [ordered]@{}
    $schemeName = 'TRANSCRYPTO'
    if ($cfg.pack) {
        if ($cfg.pack -eq 'default') { foreach ($r in $TC.CursorRoles) { $roles[$r] = $TC.CursorDefaults[$r] }; $schemeName = 'Windows Default' }
        else {
            $p = $null; foreach ($x in (Get-CursorPacks)) { if ($x.id -eq $cfg.pack) { $p = $x } }
            if ($p) { foreach ($r in $p.roles.Keys) { $roles[$r] = $p.roles[$r] }; $schemeName = $p.name }
        }
    }
    if ($cfg.roles) { foreach ($r in $cfg.roles.Keys) { if ($cfg.roles[$r]) { $roles[$r] = [string]$cfg.roles[$r] } } }
    $size = if ($cfg.size) { [int]$cfg.size } else { 0 }
    if ($size -gt 0) {
        $size = [Math]::Max(20, [Math]::Min(256, $size))   # 20 px is the forced minimum
        if ($roles.Count -eq 0) { $cur = (Read-TCCursors).roles; foreach ($r in $cur.Keys) { if ($cur[$r]) { $roles[$r] = Resolve-OriginalCursor $cur[$r] } } }
        if ($size -lt 32) { $roles = New-SmallCursorSet $roles $size }
        else { foreach ($r in @($roles.Keys)) { $roles[$r] = Resolve-OriginalCursor $roles[$r] } }
        Set-RegValue $P_CUR 'CursorBaseSize' ([Math]::Max(32, $size))
        Set-RegValue 'HKCU:\Software\Microsoft\Accessibility' 'CursorSize' ([int][Math]::Max(1, [Math]::Round(([Math]::Max(32, $size) - 32) / 16) + 1))
    }
    foreach ($r in $roles.Keys) { Set-RegValue $P_CUR $r ([string]$roles[$r]) 'ExpandString' }
    if ($roles.Count -gt 0) { Set-RegValue $P_CUR '(default)' $schemeName 'String'; Set-RegValue $P_CUR 'Scheme Source' 1 }
    [TCNative]::ReloadCursors()
}

function Read-TCCursors {
    $roles = [ordered]@{}; foreach ($r in $TC.CursorRoles) { $roles[$r] = Get-RegValue $P_CUR $r }
    $size = Get-RegValue $P_CUR 'CursorBaseSize'; if (-not $size) { $size = 32 }
    $first = [string]$roles.Arrow
    if ($first.StartsWith($TC.SmallCursorsDir, [StringComparison]::OrdinalIgnoreCase)) { $size = [int]((Split-Path (Split-Path $first) -Leaf) -split '-')[-1] }
    return [ordered]@{ scheme = (Get-RegValue $P_CUR '(default)'); roles = $roles; size = $size }
}

Register-Component 'cursors' { param($cfg) Set-TCCursors $cfg } { Read-TCCursors } { [TCNative]::ReloadCursors() }

Register-Api 'cursors.packs' { param($a) $r = Get-CursorPacks; return ,@($r) }
Register-Api 'cursors.roles' { param($a) return ,$TC.CursorRoles }

# Import a downloaded cursor pack folder (with or without install.inf)
Register-Api 'cursors.importPack' {
    param($a)
    $src = [string]$a.folder
    $name = if ($a.name) { [string]$a.name } else { Split-Path $src -Leaf }
    $id = ($name -replace '[^A-Za-z0-9\-_ ]', '').Trim()
    $dest = Join-Path $TC.UserCursorsDir $id
    New-Item -ItemType Directory $dest -Force | Out-Null
    $files = Get-ChildItem -LiteralPath $src -File -Recurse | Where-Object { $_.Extension -in '.cur', '.ani' }
    $byRole = @{}
    $inf = Get-ChildItem -LiteralPath $src -Filter *.inf -Recurse | Select-Object -First 1
    if ($inf) {
        # [Strings] pointer = "Normal.ani" etc. -> map by key name
        $keyMap = @{ pointer = 'Arrow'; arrow = 'Arrow'; normal = 'Arrow'; help = 'Help'; work = 'AppStarting'; working = 'AppStarting'; busy = 'Wait'; wait = 'Wait'
                     cross = 'Crosshair'; precision = 'Crosshair'; text = 'IBeam'; beam = 'IBeam'; hand = 'NWPen'; handwriting = 'NWPen'; pen = 'NWPen'; unavailable = 'No'; unavail = 'No'
                     vert = 'SizeNS'; horz = 'SizeWE'; dgn1 = 'SizeNWSE'; dgn2 = 'SizeNESW'; move = 'SizeAll'; alternate = 'UpArrow'; alt = 'UpArrow'; link = 'Hand'; pin = 'Pin'; location = 'Pin'; person = 'Person' }
        foreach ($line in (Get-Content -LiteralPath $inf.FullName)) {
            if ($line -match '^\s*([A-Za-z0-9_]+)\s*=\s*"?([^"]+\.(cur|ani))"?') {
                $k = $matches[1].ToLower(); $fn = [IO.Path]::GetFileName($matches[2])
                if ($keyMap.ContainsKey($k)) { $f = $files | Where-Object { $_.Name -ieq $fn } | Select-Object -First 1; if ($f) { $byRole[$keyMap[$k]] = $f.FullName } }
            }
        }
    }
    $rx = [ordered]@{ Arrow = 'arrow|normal|pointer|default'; Help = 'help'; AppStarting = 'work|appstart|background'; Wait = 'busy|wait|loading'; Crosshair = 'cross|precision'
                      IBeam = 'text|beam|ibeam|select'; NWPen = 'pen|handwrit'; No = 'unavail|no|forbid'; SizeNS = 'vert|ns|size.?ns'; SizeWE = 'horz|horiz|ew|size.?we'
                      SizeNWSE = 'dgn1|nwse|diag.?1'; SizeNESW = 'dgn2|nesw|diag.?2'; SizeAll = 'move|sizeall'; UpArrow = 'alt|up'; Hand = 'link|hand'; Pin = 'pin|location'; Person = 'person' }
    foreach ($r in $rx.Keys) {
        if ($byRole[$r]) { continue }
        $f = $files | Where-Object { $_.BaseName -match ('^(' + $rx[$r] + ')') -or $_.BaseName -match ('(' + $rx[$r] + ')$') } | Select-Object -First 1
        if ($f) { $byRole[$r] = $f.FullName }
    }
    foreach ($r in @($byRole.Keys)) { if (-not (Test-TCFileType $byRole[$r] @('cursor'))) { $byRole.Remove($r) } }   # cursor files only
    foreach ($r in $byRole.Keys) { Copy-Item -LiteralPath $byRole[$r] -Destination (Join-Path $dest ($r + [IO.Path]::GetExtension($byRole[$r]))) -Force }
    Write-Json @{ name = $name } (Join-Path $dest 'pack.json')
    return @{ id = 'pack:' + $id; mapped = @($byRole.Keys); total = $files.Count }
}
