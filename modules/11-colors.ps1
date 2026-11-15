# Colors & Accent: dark/light, accent color, transparency, accent surfaces

$P_PERS  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize'
$P_DWM   = 'HKCU:\Software\Microsoft\Windows\DWM'
$P_ACC   = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Accent'
$P_DESK  = 'HKCU:\Control Panel\Desktop'

Register-Tracked 'colors' $P_PERS 'SystemUsesLightTheme' 'DWord' 0
Register-Tracked 'colors' $P_PERS 'AppsUseLightTheme' 'DWord' 1
Register-Tracked 'colors' $P_PERS 'EnableTransparency' 'DWord' 1
Register-Tracked 'colors' $P_PERS 'ColorPrevalence' 'DWord' 0
Register-Tracked 'colors' $P_DWM 'AccentColor' 'DWord' 0xFFD47800
Register-Tracked 'colors' $P_DWM 'ColorizationColor' 'DWord' 0xC40078D4
Register-Tracked 'colors' $P_DWM 'ColorizationAfterglow' 'DWord' 0xC40078D4
Register-Tracked 'colors' $P_DWM 'ColorPrevalence' 'DWord' 0
Register-Tracked 'colors' $P_ACC 'AccentPalette' 'Binary' '99EBFF004CC2FF000091F8000078D4000067C000003E9200001A6800F7630C00'
Register-Tracked 'colors' $P_ACC 'AccentColorMenu' 'DWord' 0xFFD47800
Register-Tracked 'colors' $P_ACC 'StartColorMenu' 'DWord' 0xFFC06700
Register-Tracked 'colors' $P_DESK 'AutoColorization' 'DWord' 0

if (-not ('TCDwm' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class TCDwm {
    [StructLayout(LayoutKind.Sequential)]
    public struct DWM_COLORIZATION_PARAMS { public uint clrColor, clrAfterGlow, nIntensity, clrAfterGlowBalance, clrBlurBalance, clrGlassReflectionIntensity, fOpaque; }
    [DllImport("dwmapi.dll", EntryPoint = "#127")] static extern int DwmGetColorizationParameters(ref DWM_COLORIZATION_PARAMS p);
    [DllImport("dwmapi.dll", EntryPoint = "#131")] static extern int DwmSetColorizationParameters(ref DWM_COLORIZATION_PARAMS p, uint unknown);
    public static void Apply(uint argb) {
        try {
            DWM_COLORIZATION_PARAMS p = new DWM_COLORIZATION_PARAMS();
            DwmGetColorizationParameters(ref p);
            p.clrColor = argb; p.clrAfterGlow = argb;
            DwmSetColorizationParameters(ref p, 0);
        } catch { }
    }
}
'@
}

function Get-Rgb([string]$hex) { $h = $hex.TrimStart('#'); return @([Convert]::ToInt32($h.Substring(0,2),16), [Convert]::ToInt32($h.Substring(2,2),16), [Convert]::ToInt32($h.Substring(4,2),16)) }
function Mix-Rgb($rgb, $to, [double]$t) { return @([int]($rgb[0] + ($to[0]-$rgb[0])*$t), [int]($rgb[1] + ($to[1]-$rgb[1])*$t), [int]($rgb[2] + ($to[2]-$rgb[2])*$t)) }
function To-Hex($rgb) { return ('#{0:X2}{1:X2}{2:X2}' -f $rgb[0], $rgb[1], $rgb[2]) }

# 8 swatches: 3 lighter, base, 3 darker, complement - same layout Windows uses
function New-AccentPalette([string]$hex) {
    $c = Get-Rgb $hex; $w = @(255,255,255); $k = @(0,0,0)
    $sw = @((Mix-Rgb $c $w 0.6), (Mix-Rgb $c $w 0.38), (Mix-Rgb $c $w 0.18), $c, (Mix-Rgb $c $k 0.18), (Mix-Rgb $c $k 0.42), (Mix-Rgb $c $k 0.66), @(0xF7,0x63,0x0C))
    $bytes = New-Object byte[] 32
    for ($i = 0; $i -lt 8; $i++) { $bytes[$i*4] = $sw[$i][0]; $bytes[$i*4+1] = $sw[$i][1]; $bytes[$i*4+2] = $sw[$i][2]; $bytes[$i*4+3] = 0 }
    return @{ bytes = $bytes; dark1 = (To-Hex $sw[4]); swatches = ($sw | ForEach-Object { To-Hex $_ }) }
}

function Set-TCColors($cfg) {
    if ($cfg.Contains('systemMode')) { Set-RegValue $P_PERS 'SystemUsesLightTheme' ([int]($cfg.systemMode -eq 'light')) }
    if ($cfg.Contains('appsMode'))   { Set-RegValue $P_PERS 'AppsUseLightTheme'   ([int]($cfg.appsMode -eq 'light')) }
    if ($cfg.Contains('transparency')) { Set-RegValue $P_PERS 'EnableTransparency' ([int][bool]$cfg.transparency) }
    if ($cfg.Contains('accentOnStart')) { Set-RegValue $P_PERS 'ColorPrevalence' ([int][bool]$cfg.accentOnStart) }
    if ($cfg.Contains('accentOnTitlebars')) { Set-RegValue $P_DWM 'ColorPrevalence' ([int][bool]$cfg.accentOnTitlebars) }
    if ($cfg.Contains('autoAccent')) { Set-RegValue $P_DESK 'AutoColorization' ([int][bool]$cfg.autoAccent) }
    if ($cfg.accent -and -not $cfg.autoAccent) {
        $pal = New-AccentPalette $cfg.accent
        Set-RegValue $P_ACC 'AccentPalette' $pal.bytes 'Binary'
        Set-RegValue $P_ACC 'AccentColorMenu' (ConvertTo-ABGR $cfg.accent)
        Set-RegValue $P_ACC 'StartColorMenu' (ConvertTo-ABGR $pal.dark1)
        Set-RegValue $P_DWM 'AccentColor' (ConvertTo-ABGR $cfg.accent)
        $argb = ConvertTo-ARGB $cfg.accent 0xC4
        Set-RegValue $P_DWM 'ColorizationColor' $argb
        Set-RegValue $P_DWM 'ColorizationAfterglow' $argb
        [TCDwm]::Apply([uint32]([BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$argb), 0)))
    }
    Send-SettingChange @('ImmersiveColorSet', 'WindowsThemeElement')
}

function Read-TCColors {
    return [ordered]@{
        systemMode = if ((Get-RegValue $P_PERS 'SystemUsesLightTheme') -eq 1) { 'light' } else { 'dark' }
        appsMode   = if ((Get-RegValue $P_PERS 'AppsUseLightTheme') -eq 0) { 'dark' } else { 'light' }
        transparency = ((Get-RegValue $P_PERS 'EnableTransparency') -ne 0)
        accentOnStart = ((Get-RegValue $P_PERS 'ColorPrevalence') -eq 1)
        accentOnTitlebars = ((Get-RegValue $P_DWM 'ColorPrevalence') -eq 1)
        autoAccent = ((Get-RegValue $P_DESK 'AutoColorization') -eq 1)
        accent = (ConvertFrom-ABGR (Get-RegValue $P_ACC 'AccentColorMenu'))
    }
}

Register-Component 'colors' { param($cfg) Set-TCColors $cfg } { Read-TCColors } {
    $c = Read-TCColors
    if ($c.accent) { [TCDwm]::Apply([uint32]([BitConverter]::ToUInt32([BitConverter]::GetBytes([int](ConvertTo-ARGB $c.accent 0xC4)), 0))) }
    Send-SettingChange @('ImmersiveColorSet', 'WindowsThemeElement')
}

Register-Api 'colors.palette' { param($a) $p = New-AccentPalette ([string]$a.accent); return ,$p.swatches }
