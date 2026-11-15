# Icons & Thumbnails: desktop icons, folder icon, drive icons, per-folder icons, folder covers, thumbnail options

$P_CLSID   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID'
$P_SHELLIC = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons'
$P_DRIVEIC = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons'
$TC.DesktopIcons = [ordered]@{
    thispc  = '{20D04FE0-3AEA-1069-A2D8-08002B30309D}'
    recycle = '{645FF040-5081-101B-9F08-00AA002F954E}'
    network = '{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}'
    user    = '{59031a47-3f72-44a7-89c5-5595fe6b30ee}'
}
foreach ($g in $TC.DesktopIcons.Values) { Register-Tracked 'icons' "$P_CLSID\$g\DefaultIcon" '' 'String' $null -Tree }
Register-Tracked 'icons' $P_SHELLIC '' 'String' $null -Tree
Register-Tracked 'icons' $P_DRIVEIC '' 'String' $null -Tree

$TC.IconPacksDir = Join-Path $TC.Assets 'icons'
$TC.UserIconsDir = Join-Path $TC.Data 'icons'
if (-not (Test-Path $TC.UserIconsDir)) { New-Item -ItemType Directory $TC.UserIconsDir -Force | Out-Null }

function Get-IconPacks {
    $packs = @()
    foreach ($root in @($TC.IconPacksDir, $TC.UserIconsDir)) {
        foreach ($d in (Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue)) {
            $meta = Read-Json (Join-Path $d.FullName 'pack.json') @{}
            $files = @{}
            foreach ($k in @('drive', 'folder', 'folderOpen', 'thispc', 'recycleEmpty', 'recycleFull', 'network', 'user')) {
                $f = Join-Path $d.FullName ($k + '.ico'); if (Test-Path $f) { $files[$k] = $f }
            }
            $packs += @{ id = $d.Name; name = if ($meta.name) { $meta.name } else { $d.Name }; desc = $meta.desc; files = $files; dir = $d.FullName
                         preview = $(if (Test-Path (Join-Path $d.FullName 'preview.png')) { Join-Path $d.FullName 'preview.png' } else { $null }); user = ($root -eq $TC.UserIconsDir) }
        }
    }
    return ,$packs
}

function Get-IconPack([string]$Id) { foreach ($p in (Get-IconPacks)) { if ($p.id -eq $Id) { return $p } }; return $null }

function Set-IconValue([string]$Path, [string]$Name, [string]$Icon) {
    if ($Icon) { if ($Icon -notmatch ',-?\d+$') { $Icon = $Icon + ',0' }; Set-RegValue $Path $Name $Icon 'ExpandString' }
    else { Remove-RegValue $Path $Name }
}

function Set-TCIcons($cfg) {
    $pack = $null
    if ($cfg.pack -and $cfg.pack -ne 'default') { $pack = Get-IconPack $cfg.pack }
    if ($cfg.pack -eq 'default') {
        foreach ($g in $TC.DesktopIcons.Values) { Remove-RegKey "$P_CLSID\$g\DefaultIcon" }
        Remove-RegValue $P_SHELLIC '3'; Remove-RegValue $P_SHELLIC '4'; Remove-RegValue $P_SHELLIC '8'
        Remove-RegKey $P_DRIVEIC
    }
    $pick = { param($key) if ($cfg.Contains($key) -and $cfg[$key]) { return [string]$cfg[$key] }; if ($pack -and $pack.files[$key]) { return $pack.files[$key] }; return $null }

    $v = & $pick 'thispc';  if ($v) { Set-IconValue "$P_CLSID\$($TC.DesktopIcons.thispc)\DefaultIcon" '(default)' $v }
    $v = & $pick 'network'; if ($v) { Set-IconValue "$P_CLSID\$($TC.DesktopIcons.network)\DefaultIcon" '(default)' $v }
    $v = & $pick 'user';    if ($v) { Set-IconValue "$P_CLSID\$($TC.DesktopIcons.user)\DefaultIcon" '(default)' $v }
    $e = & $pick 'recycleEmpty'; $f = & $pick 'recycleFull'
    if ($e -or $f) {
        $rp = "$P_CLSID\$($TC.DesktopIcons.recycle)\DefaultIcon"
        if ($e) { Set-IconValue $rp 'Empty' $e }
        if ($f) { Set-IconValue $rp 'Full' $f; Set-IconValue $rp '(default)' $f }
    }
    $fo = & $pick 'folder'
    if ($fo) { Set-IconValue $P_SHELLIC '3' $fo; $fopen = & $pick 'folderOpen'; if (-not $fopen) { $fopen = $fo }; Set-IconValue $P_SHELLIC '4' $fopen }
    $dr = & $pick 'drive'
    if ($dr) { Set-IconValue $P_SHELLIC '8' $dr }
    # Shell Icons 8 covers every fixed drive except the Windows drive, which keeps its own logo icon;
    # give it the pack's drive icon too (a per-drive icon picked below still wins)
    $sys = $env:SystemDrive.Substring(0, 1).ToUpper()
    $sysKey = "$P_DRIVEIC\$sys\DefaultIcon"
    if ($dr) { Set-IconValue $sysKey '(default)' $dr }
    elseif ([string](Get-RegValue $sysKey '(default)') -like "$($TC.Assets)*") { Remove-RegKey "$P_DRIVEIC\$sys" }
    if ($cfg.drives) {
        foreach ($l in $cfg.drives.Keys) {
            $letter = ([string]$l).Substring(0,1).ToUpper()
            if ($letter -notmatch '^[A-Z]$') { continue }
            if ($cfg.drives[$l]) { Set-IconValue "$P_DRIVEIC\$letter\DefaultIcon" '(default)' ([string]$cfg.drives[$l]) } else { Remove-RegKey "$P_DRIVEIC\$letter" }
        }
    }
    if ($cfg.Contains('shortcutArrow')) {
        if ($cfg.shortcutArrow -eq $false) { Set-IconValue $P_SHELLIC '29' (Join-Path $TC.Assets 'icons-extra\blank.ico') } else { Remove-RegValue $P_SHELLIC '29' }
    }
    Update-IconCacheSoft
}

function Update-IconCacheSoft {
    try { Start-Process "$env:WINDIR\System32\ie4uinit.exe" -ArgumentList '-show' -WindowStyle Hidden -Wait } catch {}
    [TCNative]::RefreshIcons()
}

function Read-TCIcons {
    $drives = [ordered]@{}
    $packDrive = Get-RegValue $P_SHELLIC '8'; $sys = $env:SystemDrive.Substring(0, 1).ToUpper()
    foreach ($d in (Get-ChildItem -LiteralPath $P_DRIVEIC -ErrorAction SilentlyContinue)) {
        $v = Get-RegValue "$P_DRIVEIC\$($d.PSChildName)\DefaultIcon" '(default)'
        if ($d.PSChildName -eq $sys -and $packDrive -and $v -eq $packDrive) { continue }   # the pack's own icon on the Windows drive, not a user pick
        $drives[$d.PSChildName] = $v
    }
    return [ordered]@{
        thispc = Get-RegValue "$P_CLSID\$($TC.DesktopIcons.thispc)\DefaultIcon" '(default)'
        recycleEmpty = Get-RegValue "$P_CLSID\$($TC.DesktopIcons.recycle)\DefaultIcon" 'Empty'
        recycleFull = Get-RegValue "$P_CLSID\$($TC.DesktopIcons.recycle)\DefaultIcon" 'Full'
        network = Get-RegValue "$P_CLSID\$($TC.DesktopIcons.network)\DefaultIcon" '(default)'
        user = Get-RegValue "$P_CLSID\$($TC.DesktopIcons.user)\DefaultIcon" '(default)'
        folder = Get-RegValue $P_SHELLIC '3'; drive = Get-RegValue $P_SHELLIC '8'; drives = $drives
        shortcutArrow = -not [bool](Get-RegValue $P_SHELLIC '29')
    }
}

Register-Component 'icons' { param($cfg) Set-TCIcons $cfg } { Read-TCIcons } { Update-IconCacheSoft }

Register-Api 'icons.packs' { param($a) $r = Get-IconPacks; return ,@($r) }
Register-Api 'icons.drives' {
    param($a)
    $out = @()
    foreach ($d in [IO.DriveInfo]::GetDrives()) {
        $l = $d.Name.Substring(0,1)
        $label = ''; $size = 0; try { $label = $d.VolumeLabel; $size = [math]::Round($d.TotalSize / 1GB) } catch {}
        $out += @{ letter = $l; type = $d.DriveType.ToString(); label = $label; sizeGB = $size; icon = (Get-RegValue "$P_DRIVEIC\$l\DefaultIcon" '(default)') }
    }
    return ,$out
}

# Import a folder of .ico files as a user pack (file names matched by keyword)
Register-Api 'icons.importPack' {
    param($a)
    $src = [string]$a.folder; $name = if ($a.name) { [string]$a.name } else { Split-Path $src -Leaf }
    $id = ($name -replace '[^A-Za-z0-9\-_ ]', '').Trim(); $dest = Join-Path $TC.UserIconsDir $id
    New-Item -ItemType Directory $dest -Force | Out-Null
    $map = [ordered]@{ recycleFull = 'full'; recycleEmpty = 'empty|recycle|trash|bin'; thispc = 'computer|thispc|this pc|pc|monitor'; drive = 'drive|disk|hdd|ssd'
                       folderOpen = 'folder.?open|open'; folder = 'folder|dir'; network = 'network|net|globe'; user = 'user|profile|person|home' }
    $files = Get-ChildItem -LiteralPath $src -Filter *.ico -File
    $used = @{}
    foreach ($k in $map.Keys) {
        foreach ($f in $files) { if (-not $used[$f.FullName] -and $f.BaseName -match $map[$k]) { Copy-Item $f.FullName (Join-Path $dest ($k + '.ico')) -Force; $used[$f.FullName] = $true; break } }
    }
    Write-Json @{ name = $name; desc = 'Imported pack' } (Join-Path $dest 'pack.json')
    return (Get-IconPack $id)
}

# Per-folder custom icon via desktop.ini
function Set-FolderIcon([string]$Folder, [string]$Icon) {
    $ini = Join-Path $Folder 'desktop.ini'
    if (Test-Path -LiteralPath $ini) { (Get-Item -LiteralPath $ini -Force).Attributes = 'Normal' }
    $lines = @()
    if (Test-Path -LiteralPath $ini) { $lines = Get-Content -LiteralPath $ini | Where-Object { $_ -notmatch '^\s*(IconResource|IconFile|IconIndex)\s*=' } }
    if (-not ($lines -match '^\[\.ShellClassInfo\]')) { $lines = @('[.ShellClassInfo]') + $lines }
    $out = @()
    foreach ($l in $lines) { $out += $l; if ($l -match '^\[\.ShellClassInfo\]') { $out += ('IconResource=' + $Icon + ',0') } }
    Set-Content -LiteralPath $ini -Value $out -Encoding Unicode
    (Get-Item -LiteralPath $ini -Force).Attributes = 'Hidden, System, Archive'
    $fi = Get-Item -LiteralPath $Folder -Force; $fi.Attributes = $fi.Attributes -bor [IO.FileAttributes]::ReadOnly
    [TCNative]::RefreshIcons()
}

Register-Api 'icons.setFolderIcon' {
    param($a)
    $icon = [string]$a.icon
    Assert-TCFileType $icon @('icon')
    # copy the icon next to our data so moving the original does not break it
    $store = Join-Path $TC.Data 'folder-icons'; New-Item -ItemType Directory $store -Force | Out-Null
    $dest = Join-Path $store ([IO.Path]::GetFileName($icon))
    if ($icon -ne $dest) { Copy-Item -LiteralPath $icon -Destination $dest -Force }
    Set-FolderIcon ([string]$a.folder) $dest
    return $true
}

Register-Api 'icons.clearFolderIcon' {
    param($a)
    $ini = Join-Path ([string]$a.folder) 'desktop.ini'
    if (Test-Path -LiteralPath $ini) {
        (Get-Item -LiteralPath $ini -Force).Attributes = 'Normal'
        $keep = Get-Content -LiteralPath $ini | Where-Object { $_ -notmatch '^\s*(IconResource|IconFile|IconIndex)\s*=' }
        if (($keep | Where-Object { $_ -and $_ -notmatch '^\[\.ShellClassInfo\]' }).Count -eq 0) { Remove-Item -LiteralPath $ini -Force }
        else { Set-Content -LiteralPath $ini -Value $keep -Encoding Unicode; (Get-Item -LiteralPath $ini -Force).Attributes = 'Hidden, System, Archive' }
    }
    [TCNative]::RefreshIcons()
    return $true
}

# Folder cover: draws a picture inside the pack's folder shape and saves it as a 256px PNG-compressed .ico
Register-Api 'icons.makeFolderCover' {
    param($a)
    $img = [System.Drawing.Image]::FromFile([string]$a.image)
    $accent = if ($a.accent) { [string]$a.accent } else { '#00F0FF' }
    $bmp = New-Object System.Drawing.Bitmap 256, 256
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.Clear([System.Drawing.Color]::Transparent)
    $ac = [System.Drawing.ColorTranslator]::FromHtml($accent)
    $back = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, [int]($ac.R*0.55), [int]($ac.G*0.55), [int]($ac.B*0.55)))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(16, 40, 24, 24, 180, 90); $path.AddLine(28, 40, 96, 40); $path.AddLine(96, 40, 116, 60); $path.AddLine(116, 60, 228, 60)
    $path.AddArc(216, 60, 24, 24, 270, 90); $path.AddLine(240, 72, 240, 216); $path.AddArc(216, 204, 24, 24, 0, 90); $path.AddArc(16, 204, 24, 24, 90, 90); $path.CloseFigure()
    $g.FillPath($back, $path)
    # picture area (front flap)
    $front = New-Object System.Drawing.Drawing2D.GraphicsPath
    $front.AddArc(16, 84, 24, 24, 180, 90); $front.AddArc(216, 84, 24, 24, 270, 90); $front.AddArc(216, 204, 24, 24, 0, 90); $front.AddArc(16, 204, 24, 24, 90, 90); $front.CloseFigure()
    $g.SetClip($front)
    $rw = 224; $rh = 144; $sc = [Math]::Max($rw / $img.Width, $rh / $img.Height)
    $dw = $img.Width * $sc; $dh = $img.Height * $sc
    $g.DrawImage($img, [single](16 + ($rw - $dw) / 2), [single](84 + ($rh - $dh) / 2), [single]$dw, [single]$dh)
    $g.ResetClip()
    $pen = New-Object System.Drawing.Pen ($ac, 4); $g.DrawPath($pen, $front)
    $g.Dispose(); $img.Dispose()
    $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    $png = $ms.ToArray()
    $store = Join-Path $TC.Data 'folder-icons'; New-Item -ItemType Directory $store -Force | Out-Null
    $out = Join-Path $store ('cover-' + [IO.Path]::GetFileNameWithoutExtension([string]$a.image) + '-' + (Get-Date -Format 'HHmmss') + '.ico')
    $fs = [IO.File]::Create($out); $bw = New-Object IO.BinaryWriter($fs)
    $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)                 # ICONDIR
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([byte]0)   # 256x256, no palette
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$png.Length); $bw.Write([uint32]22)
    $bw.Write($png); $bw.Close()
    if ($a.folder) { Set-FolderIcon ([string]$a.folder) $out }
    return $out
}

# Rebuild icon + thumbnail cache (restarts explorer)
Register-Api 'icons.rebuildCache' {
    param($a)
    Get-Process explorer -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
    $exp = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
    Remove-Item (Join-Path $env:LOCALAPPDATA 'IconCache.db') -Force -ErrorAction SilentlyContinue
    Get-ChildItem $exp -Filter 'iconcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    if ($a.thumbs) { Get-ChildItem $exp -Filter 'thumbcache_*.db' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Milliseconds 500
    if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer.exe }
    return $true
}

# Thumbnail tweaks live in the same tab
$adv = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
Register-RegTweak -Tab 'icons' -Section 'Thumbnails' -Id 'thumbs.show' -Label 'Show thumbnails instead of icons' -Desc 'Picture/video previews on files and folder covers' -Path $adv -Name 'IconsOnly' -On 0 -Off 1 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Thumbnails' -Id 'thumbs.typeOverlay' -Label 'File-type badge on thumbnails' -Desc 'Small app icon in the corner of thumbnails' -Path $adv -Name 'ShowTypeOverlay' -On 1 -Off 0 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Thumbnails' -Id 'thumbs.noCache' -Label 'Disable thumbnail cache (thumbs.db on network folders)' -Path 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' -Name 'DisableThumbsDBOnNetworkFolders' -On 1 -Off $null -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Thumbnails' -Id 'icons.maxCache' -Kind 'select' -Label 'Icon cache size' -Desc 'Bigger cache = fewer blank icons after theming' -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer' -Name 'Max Cached Icons' -Type 'String' -AbsentIs '500' -Options @(@{ v = '500'; l = 'Default' }, @{ v = '4096'; l = '4 MB' }, @{ v = '8192'; l = '8 MB' }) -Admin -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Desktop icons' -Id 'desk.thispc' -Label 'Show "This PC" on desktop' -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel' -Name '{20D04FE0-3AEA-1069-A2D8-08002B30309D}' -On 0 -Off 1 -AbsentIs 'off' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Desktop icons' -Id 'desk.recycle' -Label 'Show Recycle Bin on desktop' -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel' -Name '{645FF040-5081-101B-9F08-00AA002F954E}' -On 0 -Off 1 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Desktop icons' -Id 'desk.user' -Label 'Show user folder on desktop' -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel' -Name '{59031a47-3f72-44a7-89c5-5595fe6b30ee}' -On 0 -Off 1 -AbsentIs 'off' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Desktop icons' -Id 'desk.network' -Label 'Show Network on desktop' -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\HideDesktopIcons\NewStartPanel' -Name '{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}' -On 0 -Off 1 -AbsentIs 'off' -Restart 'explorer'
Register-RegTweak -Tab 'icons' -Section 'Desktop icons' -Id 'desk.size' -Kind 'select' -Label 'Desktop icon size' -Path 'HKCU:\Software\Microsoft\Windows\Shell\Bags\1\Desktop' -Name 'IconSize' -AbsentIs 48 -Options @(@{ v = 32; l = 'Small' }, @{ v = 48; l = 'Medium' }, @{ v = 64; l = 'Large' }, @{ v = 96; l = 'Extra large' }) -Restart 'explorer'
