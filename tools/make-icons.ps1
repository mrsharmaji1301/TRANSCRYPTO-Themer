# Renders the shell icon packs (tools/iconforge.html) with a headless Chromium browser and writes
# multi-size .ico files into assets/icons/<pack>/ (16..256 px, PNG-compressed entries).
#   powershell -ExecutionPolicy Bypass -File tools\make-icons.ps1 [-Packs sharmaji,orbital,projector]
param([string[]]$Packs = @('sharmaji', 'orbital', 'projector'))
$ErrorActionPreference = 'Stop'
$Packs = @($Packs | ForEach-Object { $_ -split ',' } | Where-Object { $_ })   # -File passes 'a,b' as one string
$root = Split-Path -Parent $PSScriptRoot
Add-Type -AssemblyName System.Drawing

$browser = @("${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe", "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe",
             "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
             "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe", "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe",
             "$env:LOCALAPPDATA\BraveSoftware\Brave-Browser\Application\brave.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw 'No Chromium browser (Edge, Chrome or Brave) found' }

$names = @{ sharmaji = @('SharmaJi Command Center', 'Machined gunmetal plates · saffron x cyan rim · tricolor')
            orbital  = @('Orbital', 'Liquid aurora droplets · glossy')
            projector = @('Projector', 'Smoked glass · ice-blue etched glyphs') }
$icons = 'folder', 'folderOpen', 'drive', 'sysdrive', 'thispc', 'network', 'user', 'recycleEmpty', 'recycleFull', 'desktop', 'downloads', 'documents', 'pictures', 'music', 'videos', 'projects', 'games'
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$forge = 'file:///' + ((Join-Path $root 'tools\iconforge.html') -replace '\\', '/')
$tmp = Join-Path $env:TEMP 'tc-iconforge'; New-Item -ItemType Directory $tmp -Force | Out-Null
$profile = Join-Path $tmp 'profile'

function Resize([Drawing.Bitmap]$src, [int]$s) {
    $b = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($b); $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.SmoothingMode = 'HighQuality'
    $g.DrawImage($src, 0, 0, $s, $s); $g.Dispose(); return $b
}

function Write-Ico([string]$png, [string]$out) {
    $src = [Drawing.Bitmap]::FromFile($png)
    $blobs = foreach ($s in $sizes) { $b = Resize $src $s; $ms = New-Object IO.MemoryStream; $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
    $src.Dispose()
    $fs = [IO.File]::Create($out); $w = New-Object IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$blobs[$i].Length); $w.Write([uint32]$offset); $offset += $blobs[$i].Length
    }
    foreach ($b in $blobs) { $w.Write($b) }
    $w.Close()
}

foreach ($pack in $Packs) {
    $dir = Join-Path $root "assets\icons\$pack"; New-Item -ItemType Directory $dir -Force | Out-Null
    foreach ($icon in $icons) {
        $png = Join-Path $tmp "$pack-$icon.png"; Remove-Item $png -ErrorAction SilentlyContinue
        # Start-Process: Chromium writes log noise to stderr, which PowerShell 5.1 would turn into terminating errors
        $argList = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--no-first-run', "--user-data-dir=`"$profile`"", '--default-background-color=00000000',
                     '--window-size=256,256', '--virtual-time-budget=4000', "--screenshot=`"$png`"", "`"$($forge)?pack=$pack&icon=$icon`"")
        Start-Process -FilePath $browser -ArgumentList $argList -Wait -WindowStyle Hidden
        for ($i = 0; $i -lt 40 -and -not (Test-Path $png); $i++) { Start-Sleep -Milliseconds 150 }
        if (-not (Test-Path $png)) { Write-Warning "no render for $pack/$icon"; continue }
        Write-Ico $png (Join-Path $dir "$icon.ico")
    }
    $meta = [ordered]@{ name = $names[$pack][0]; desc = $names[$pack][1] }
    [IO.File]::WriteAllText((Join-Path $dir 'pack.json'), ($meta | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
    Copy-Item (Join-Path $tmp "$pack-folder.png") (Join-Path $dir 'preview.png') -Force -ErrorAction SilentlyContinue
    Write-Host "pack $pack -> $dir" -ForegroundColor Green
}
