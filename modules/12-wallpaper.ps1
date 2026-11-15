# Wallpapers: solid / image (per monitor) / live video (TCWall engine) / slideshow + library

$P_CDESK = 'HKCU:\Control Panel\Desktop'
Register-Tracked 'wallpaper' $P_CDESK 'Wallpaper' 'String' "$env:WINDIR\Web\Wallpaper\Windows\img0.jpg"
Register-Tracked 'wallpaper' $P_CDESK 'WallpaperStyle' 'String' '10'
Register-Tracked 'wallpaper' $P_CDESK 'TileWallpaper' 'String' '0'
Register-Tracked 'wallpaper' 'HKCU:\Control Panel\Colors' 'Background' 'String' '0 0 0'

$TC.ImageExt = @('.jpg', '.jpeg', '.png', '.bmp', '.gif', '.webp', '.jfif', '.tif', '.tiff')
$TC.VideoExt = @('.mp4', '.webm', '.mkv', '.mov', '.wmv', '.avi', '.m4v')
$TC.FitMap = @{ center = 0; tile = 1; stretch = 2; fit = 3; fill = 4; span = 5 }

function Get-Monitors {
    $out = @()
    try {
        $i = 0
        foreach ($line in [TCNative]::WpMonitors()) {
            $f = $line -split '\|', 6
            $out += @{ index = $i; id = $f[0]; width = ([int]$f[3] - [int]$f[1]); height = ([int]$f[4] - [int]$f[2]); x = [int]$f[1]; y = [int]$f[2]; wallpaper = $f[5] }
            $i++
        }
    } catch { Write-TCLog "monitors: $_" }
    # names the engine uses (\\.\DISPLAYn) in the same enumeration order
    $screens = [System.Windows.Forms.Screen]::AllScreens
    for ($i = 0; $i -lt $out.Count -and $i -lt $screens.Count; $i++) { $out[$i].device = $screens[$i].DeviceName; $out[$i].primary = $screens[$i].Primary }
    return ,$out
}

function Set-StaticWallpaper([string]$Path, [string]$Fit = 'fill', $PerMonitor = $null) {
    $pos = $TC.FitMap[$Fit]; if ($null -eq $pos) { $pos = 4 }
    [TCNative]::WpPosition($pos)
    if ($PerMonitor -and $PerMonitor.Count -gt 0) {
        foreach ($k in $PerMonitor.Keys) { if ($PerMonitor[$k]) { [TCNative]::WpSet($k, [string]$PerMonitor[$k]) } }
    } elseif ($Path) { [TCNative]::WpSet($null, $Path) }
}

function Set-SolidBackground([string]$Hex) {
    $rgb = Get-Rgb $Hex
    $colorref = [uint32]($rgb[0] -bor ($rgb[1] -shl 8) -bor ($rgb[2] -shl 16))   # COLORREF 0x00BBGGRR
    Set-RegValue 'HKCU:\Control Panel\Colors' 'Background' ('{0} {1} {2}' -f $rgb[0], $rgb[1], $rgb[2]) 'String'
    try { [TCNative]::WpSet($null, '') } catch {}
    [TCNative]::SetWallpaperAll('')
    [TCNative]::WpBackground($colorref)
}

function Set-TCWallpaper($cfg) {
    $type = if ($cfg.type) { [string]$cfg.type } else { 'image' }
    $eng = Get-EngineConfig
    $eng.wallpaper.enabled = $false; $eng.slideshow.enabled = $false
    # any wallpaper replaces a live shell background (it sits above the desktop picture and would hide it); 'web' picks one
    $eng.liveBg.design = 'none'
    switch ($type) {
        'web' {
            $eng.liveBg.design = [string]$cfg.design
            $eng.liveBg.palette = if ($cfg.palette) { [string]$cfg.palette } else { '' }
        }
        'solid' { if ($cfg.color) { Set-SolidBackground $cfg.color } }
        'image' { Set-StaticWallpaper ([string]$cfg.image) ([string]$cfg.fit) $cfg.perMonitor }
        'live' {
            if ($cfg.color) { try { Set-SolidBackground $cfg.color } catch {} }
            $eng.wallpaper.enabled = $true
            foreach ($k in @('file', 'mode', 'fit', 'volume', 'pauseOnFullscreen', 'pauseOnMaximized', 'pauseOnBattery', 'pauseOnLock')) {
                if ($cfg.Contains($k)) { $eng.wallpaper[$k] = $cfg[$k] }
            }
            if ($cfg.Contains('monitors')) { $eng.wallpaper.monitors = $cfg.monitors }
        }
        'slideshow' {
            $eng.slideshow.enabled = $true
            foreach ($k in @('folder', 'minutes', 'shuffle')) { if ($cfg.Contains($k)) { $eng.slideshow[$k] = $cfg[$k] } }
            if ($cfg.fit) { [TCNative]::WpPosition($TC.FitMap[[string]$cfg.fit]) }
        }
    }
    Save-EngineConfig $eng
    Sync-Engine | Out-Null
}

function Read-TCWallpaper {
    $eng = Get-EngineConfig
    $type = 'image'
    if ([string]$eng.liveBg.design -and [string]$eng.liveBg.design -ne 'none') { $type = 'web' }
    elseif ($eng.wallpaper.enabled) { $type = 'live' } elseif ($eng.slideshow.enabled) { $type = 'slideshow' }
    elseif (-not (Get-RegValue $P_CDESK 'Wallpaper')) { $type = 'solid' }
    $bg = (Get-RegValue 'HKCU:\Control Panel\Colors' 'Background')
    $hex = '#000000'; if ($bg) { $p = $bg -split ' '; $hex = '#{0:X2}{1:X2}{2:X2}' -f [int]$p[0], [int]$p[1], [int]$p[2] }
    $fit = 'fill'; try { $pos = [TCNative]::WpGetPosition(); foreach ($k in $TC.FitMap.Keys) { if ($TC.FitMap[$k] -eq $pos) { $fit = $k } } } catch {}
    return [ordered]@{ type = $type; image = (Get-RegValue $P_CDESK 'Wallpaper'); color = $hex; fit = $fit
                       file = $eng.wallpaper.file; mode = $eng.wallpaper.mode; monitors = $eng.wallpaper.monitors; volume = $eng.wallpaper.volume
                       pauseOnFullscreen = $eng.wallpaper.pauseOnFullscreen; pauseOnMaximized = $eng.wallpaper.pauseOnMaximized
                       pauseOnBattery = $eng.wallpaper.pauseOnBattery; pauseOnLock = $eng.wallpaper.pauseOnLock
                       folder = $eng.slideshow.folder; minutes = $eng.slideshow.minutes; shuffle = $eng.slideshow.shuffle
                       design = [string]$eng.liveBg.design; palette = [string]$eng.liveBg.palette }
}

Register-Component 'wallpaper' { param($cfg) Set-TCWallpaper $cfg } { Read-TCWallpaper } {
    $w = Get-RegValue $P_CDESK 'Wallpaper'
    $eng = Get-EngineConfig; $eng.wallpaper.enabled = $false; $eng.slideshow.enabled = $false; Save-EngineConfig $eng; Sync-Engine | Out-Null
    [TCNative]::SetWallpaperAll([string]$w)
}

# ---------------------------------------------------------------- library
function Get-LibraryItems {
    $items = @()
    foreach ($f in (Get-ChildItem -LiteralPath $TC.Library -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)) {
        $ext = $f.Extension.ToLower()
        $kind = if ($TC.VideoExt -contains $ext) { 'video' } elseif ($TC.ImageExt -contains $ext) { 'image' } else { $null }
        if (-not $kind) { continue }
        $items += @{ name = $f.BaseName; file = $f.Name; path = $f.FullName; kind = $kind; sizeMB = [math]::Round($f.Length / 1MB, 1); added = $f.LastWriteTime.ToString('yyyy-MM-dd') }
    }
    return ,$items
}

Register-Api 'wallpapers.library'  { param($a) $r = Get-LibraryItems; return ,@($r) }
Register-Api 'wallpapers.monitors' { param($a) $r = Get-Monitors; return ,@($r) }
Register-Api 'wallpapers.folder'   { param($a) return $TC.Library }
# the TRANSCRYPTO shell scenes (live web backgrounds), with a still preview per palette (tools\make-scenes.ps1)
Register-Api 'wallpapers.scenes' {
    param($a)
    $names = [ordered]@{ sharmaji = 'SharmaJi Command Center'; orbital = 'Orbital'; projector = 'Projector' }
    $pals = [ordered]@{ sharmaji = @('sharmaji', 'transcrypto', 'alien'); orbital = @('aurora', 'lagoon', 'ember'); projector = @('platinum', 'solar', 'rosso') }
    $out = @()
    foreach ($d in $names.Keys) {
        foreach ($p in $pals[$d]) {
            $img = Join-Path $TC.Assets "scenes\$d-$p.png"
            $out += [ordered]@{ design = $d; palette = $p; name = $names[$d]; preview = $(if (Test-Path $img) { $img } else { '' }) }
        }
    }
    return ,@($out)
}

Register-Api 'wallpapers.import' {
    param($a)
    $done = @()
    foreach ($p in @($a.paths)) {
        if (-not $p -or -not (Test-Path -LiteralPath $p)) { continue }
        if (-not (Test-TCFileType $p @('image', 'video'))) { continue }   # only pictures and videos go into the library
        $dest = Join-Path $TC.Library ([IO.Path]::GetFileName($p))
        if ($dest -ne $p) { Copy-Item -LiteralPath $p -Destination $dest -Force }
        $done += $dest
    }
    return ,$done
}

Register-Api 'wallpapers.delete' {
    param($a)
    $p = [string]$a.path
    if (Test-TCChildPath $p $TC.Library) { Remove-Item -LiteralPath $p -Force }   # resolved path: "library\..\..\x" is refused
    return $true
}

Register-Api 'wallpapers.rename' {
    param($a)
    $p = [string]$a.path; $n = ([string]$a.name) -replace '[\\/:*?"<>|]', ''
    if (-not (Test-TCChildPath $p $TC.Library) -or -not $n.Trim('.', ' ')) { throw 'Only files in the wallpaper library can be renamed.' }
    $dest = Join-Path $TC.Library ($n + [IO.Path]::GetExtension($p))
    Rename-Item -LiteralPath $p -NewName ([IO.Path]::GetFileName($dest))
    return $dest
}

# Download from a direct media link or from a wallpaper page (MoeWalls etc.: we look for the video link in the page)
Register-Api 'wallpapers.fromUrl' {
    param($a)
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $url = ([string]$a.url).Trim()
    $ua = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0 Safari/537.36 Edg/126.0'
    $media = $null
    $ext = [IO.Path]::GetExtension(([Uri]$url).AbsolutePath).ToLower()
    if (($TC.VideoExt + $TC.ImageExt) -contains $ext) { $media = $url }
    else {
        $page = Invoke-WebRequest -Uri $url -UseBasicParsing -UserAgent $ua -TimeoutSec 30
        $html = $page.Content
        $cands = @()
        foreach ($m in [regex]::Matches($html, '(https?:)?//[^"''\s<>]+?\.(mp4|webm|mkv|mov)(\?[^"''\s<>]*)?', 'IgnoreCase')) { $cands += $m.Value }
        foreach ($m in [regex]::Matches($html, 'https?://[^"''\s<>]*download\.php\?video=[^"''\s<>]+', 'IgnoreCase')) { $cands = @($m.Value) + $cands }
        foreach ($m in [regex]::Matches($html, '<meta[^>]+property="og:(video|image)"[^>]+content="([^"]+)"', 'IgnoreCase')) { $cands += $m.Groups[2].Value }
        $cands = $cands | ForEach-Object { if ($_.StartsWith('//')) { 'https:' + $_ } else { $_ } } | Select-Object -Unique
        if (-not $cands) { throw 'No video or image link found on that page. Paste the direct download link instead.' }
        $media = $cands | Select-Object -First 1
    }
    $name = if ($a.name) { ([string]$a.name) -replace '[\\/:*?"<>|]', '' } else { [IO.Path]::GetFileNameWithoutExtension(([Uri]$media).AbsolutePath) }
    if (-not $name -or $name -eq 'download') { $name = 'wallpaper-' + (Get-Date -Format 'yyyyMMdd-HHmmss') }
    $tmp = Join-Path $TC.Cache ('dl-' + [Guid]::NewGuid().ToString('N'))
    $resp = Invoke-WebRequest -Uri $media -UseBasicParsing -UserAgent $ua -Headers @{ Referer = $url } -OutFile $tmp -PassThru -TimeoutSec 600
    $e2 = [IO.Path]::GetExtension(([Uri]$media).AbsolutePath).ToLower()
    $ct = [string]$resp.Headers['Content-Type']
    if (-not (($TC.VideoExt + $TC.ImageExt) -contains $e2)) {
        $e2 = switch -Wildcard ($ct) { 'video/webm*' { '.webm' } 'video/*' { '.mp4' } 'image/png*' { '.png' } 'image/*' { '.jpg' } default { '.mp4' } }
        $cd = [string]$resp.Headers['Content-Disposition']
        if ($cd -match 'filename="?([^";]+)') { $e2 = [IO.Path]::GetExtension($matches[1]).ToLower(); if (-not $a.name) { $name = [IO.Path]::GetFileNameWithoutExtension($matches[1]) } }
    }
    if ($ct -like 'text/html*') { Remove-Item $tmp -Force; throw 'The link returned a web page, not a file. Try the direct download link.' }
    if (-not (Test-TCFileType ('x' + $e2) @('image', 'video'))) { $e2 = '.mp4' }   # the server never decides that we save a program or script
    $name = [IO.Path]::GetFileName($name)
    $dest = Join-Path $TC.Library ($name + $e2)
    Move-Item -LiteralPath $tmp -Destination $dest -Force
    return @{ path = $dest; name = $name }
}
