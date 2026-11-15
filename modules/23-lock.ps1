# Lock & Login: lock screen image, sign-in screen, TRANSCRYPTO Live Lock (video lock screen)

$CSP = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
$PERSPOL = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Personalization'
$SYSPOL = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
$CDM = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager'
$TC.LockStore = Join-Path $env:ProgramData 'TRANSCRYPTO'

foreach ($n in @('LockScreenImagePath', 'LockScreenImageUrl', 'LockScreenImageStatus')) { Register-Tracked 'lock' $CSP $n $(if ($n -eq 'LockScreenImageStatus') { 'DWord' } else { 'String' }) $null }

Register-RegTweak -Tab 'lock' -Section 'Lock screen' -Id 'lock.disable' -Label 'Skip the Windows lock screen' -Desc 'Go straight to the password / PIN box. Needed for Live Lock.' -Path $PERSPOL -Name 'NoLockScreen' -On 1 -Off $null -AbsentIs 'off' -Admin
Register-RegTweak -Tab 'lock' -Section 'Lock screen' -Id 'lock.tips' -Label 'Fun facts, tips and tricks on lock screen' -Path $CDM -Name 'RotatingLockScreenOverlayEnabled' -AbsentIs 'on'
Register-RegTweak -Tab 'lock' -Section 'Lock screen' -Id 'lock.tips2' -Label 'Spotlight suggestions on lock screen' -Path $CDM -Name 'SubscribedContent-338387Enabled' -AbsentIs 'on'
Register-RegTweak -Tab 'lock' -Section 'Lock screen' -Id 'lock.widgets' -Label 'Weather & widgets on lock screen' -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' -Name 'DisableWidgetsOnLockScreen' -On $null -Off 1 -AbsentIs 'on' -Admin
Register-RegTweak -Tab 'lock' -Section 'Sign-in screen' -Id 'login.noBlur' -Label 'Remove blur (acrylic) on sign-in screen' -Path $SYSPOL -Name 'DisableAcrylicBackgroundOnLogon' -On 1 -Off $null -AbsentIs 'off' -Admin
Register-RegTweak -Tab 'lock' -Section 'Sign-in screen' -Id 'login.showPicture' -Label 'Show lock screen picture on sign-in screen' -Path $SYSPOL -Name 'DisableLogonBackgroundImage' -On $null -Off 1 -AbsentIs 'on' -Admin
Register-RegTweak -Tab 'lock' -Section 'Sign-in screen' -Id 'login.network' -Label 'Network button on sign-in screen' -Path $SYSPOL -Name 'DontDisplayNetworkSelectionUI' -On $null -Off 1 -AbsentIs 'on' -Admin
Register-RegTweak -Tab 'lock' -Section 'Sign-in screen' -Id 'login.lastUser' -Label 'Hide name of the last signed-in user' -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DontDisplayLastUserName' -On 1 -Off 0 -AbsentIs 'off' -Admin

function Set-LockImage([string]$Image) {
    Assert-TCFileType $Image @('image')
    if (-not (Test-Path $TC.LockStore)) { New-Item -ItemType Directory $TC.LockStore -Force | Out-Null }
    $dest = Join-Path $TC.LockStore ('lockscreen' + [IO.Path]::GetExtension($Image))
    Get-ChildItem $TC.LockStore -Filter 'lockscreen.*' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $Image -Destination $dest -Force
    Set-RegValue $CSP 'LockScreenImagePath' $dest 'String'
    Set-RegValue $CSP 'LockScreenImageUrl' $dest 'String'
    Set-RegValue $CSP 'LockScreenImageStatus' 1
    return $dest
}

Register-Api 'lock.get' {
    param($a)
    $eng = Get-EngineConfig
    return @{ image = (Get-RegValue $CSP 'LockScreenImagePath'); live = $eng.lock; noLockScreen = ((Get-RegValue $PERSPOL 'NoLockScreen') -eq 1) }
}
Register-Api 'lock.setImage' { param($a) return (Set-LockImage ([string]$a.image)) }
Register-Api 'lock.clearImage' {
    param($a)
    foreach ($n in @('LockScreenImagePath', 'LockScreenImageUrl', 'LockScreenImageStatus')) { Remove-RegValue $CSP $n }
    return $true
}

# Grab a frame of a video for the static lock screen (uses the Windows shell thumbnail of the file)
Register-Api 'lock.frameFromVideo' {
    param($a)
    if (-not ('TCThumb' -as [type])) {
        Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Drawing;
public static class TCThumb {
    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory { [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm); }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);
    public static Bitmap Get(string path, int w, int h) {
        IShellItemImageFactory f;
        SHCreateItemFromParsingName(path, IntPtr.Zero, new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), out f);
        IntPtr hb; int hr = f.GetImage(new SIZE(w, h), 0x8 | 0x1, out hb);   // THUMBNAILONLY | BIGGERSIZEOK
        if (hr != 0) throw new Exception("thumbnail failed " + hr);
        Bitmap b = Image.FromHbitmap(hb); DeleteObject(hb); return b;
    }
}
'@
    }
    $bmp = [TCThumb]::Get([string]$a.video, 1920, 1080)
    $out = Join-Path $TC.Cache ('frame-' + (Get-Date -Format 'HHmmss') + '.jpg')
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Jpeg); $bmp.Dispose()
    if ($a.apply) { Set-LockImage $out | Out-Null }
    return $out
}

# Live Lock config is part of engine.json -> lock
Register-Api 'lock.setLive' {
    param($a)
    $eng = Get-EngineConfig
    $eng.lock = Merge-HT $eng.lock (ConvertTo-HT $a.values)
    Save-EngineConfig $eng
    if ($eng.lock.enabled) { Set-RegValue $PERSPOL 'NoLockScreen' 1 }
    elseif ($a.restoreWindowsLock) { Remove-RegValue $PERSPOL 'NoLockScreen' }
    return @{ running = (Sync-Engine); live = $eng.lock }
}

$TCRefresh['lock'] = {
    $eng = Get-EngineConfig
    if ((Get-RegValue $PERSPOL 'NoLockScreen') -ne 1 -and $eng.lock.enabled) { $eng.lock.enabled = $false; Save-EngineConfig $eng; Sync-Engine | Out-Null }
}
