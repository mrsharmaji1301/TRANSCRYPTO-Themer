# Fonts: replace the system UI font (Segoe UI) with any installed font, text size, install fonts

$FONTS = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts'
$FSUB  = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes'
$TC.SegoeFiles = [ordered]@{
    'Segoe UI (TrueType)' = 'segoeui.ttf'; 'Segoe UI Black (TrueType)' = 'seguibl.ttf'; 'Segoe UI Black Italic (TrueType)' = 'seguibli.ttf'
    'Segoe UI Bold (TrueType)' = 'segoeuib.ttf'; 'Segoe UI Bold Italic (TrueType)' = 'segoeuiz.ttf'; 'Segoe UI Italic (TrueType)' = 'segoeuii.ttf'
    'Segoe UI Light (TrueType)' = 'segoeuil.ttf'; 'Segoe UI Light Italic (TrueType)' = 'seguili.ttf'; 'Segoe UI Semibold (TrueType)' = 'seguisb.ttf'
    'Segoe UI Semibold Italic (TrueType)' = 'seguisbi.ttf'; 'Segoe UI Semilight (TrueType)' = 'segoeuisl.ttf'; 'Segoe UI Semilight Italic (TrueType)' = 'seguisli.ttf'
}
foreach ($k in $TC.SegoeFiles.Keys) { Register-Tracked 'fonts' $FONTS $k 'String' $TC.SegoeFiles[$k] }
Register-Tracked 'fonts' $FSUB 'Segoe UI' 'String' $null

Register-RegTweak -Tab 'fonts' -Section 'Text' -Id 'font.textScale' -Kind 'select' -Label 'Text size (accessibility)' -Path 'HKCU:\Software\Microsoft\Accessibility' -Name 'TextScaleFactor' -AbsentIs 100 -Options @(@{ v = 100; l = '100%' }, @{ v = 110; l = '110%' }, @{ v = 125; l = '125%' }, @{ v = 150; l = '150%' }) -Restart 'signout'
Register-RegTweak -Tab 'fonts' -Section 'Text' -Id 'font.smoothing' -Label 'Font smoothing (ClearType)' -Path 'HKCU:\Control Panel\Desktop' -Name 'FontSmoothing' -Type 'String' -On '2' -Off '0' -AbsentIs 'on' -Restart 'signout'

Register-Api 'fonts.list' {
    param($a)
    $c = New-Object System.Drawing.Text.InstalledFontCollection
    return ,@($c.Families | ForEach-Object { $_.Name })
}

Register-Api 'fonts.get' {
    param($a)
    $sub = Get-RegValue $FSUB 'Segoe UI'
    $replaced = ((Get-RegValue $FONTS 'Segoe UI (TrueType)') -eq '')
    return @{ current = $(if ($replaced -and $sub) { $sub } else { 'Segoe UI' }); replaced = $replaced }
}

Register-Api 'fonts.set' {
    param($a)
    $fam = [string]$a.family
    Invoke-SafetyNet "System font -> $fam"
    if (-not $fam -or $fam -eq 'Segoe UI') {
        foreach ($k in $TC.SegoeFiles.Keys) { Set-RegValue $FONTS $k $TC.SegoeFiles[$k] 'String' }
        Remove-RegValue $FSUB 'Segoe UI'
    } else {
        foreach ($k in $TC.SegoeFiles.Keys) { Set-RegValue $FONTS $k '' 'String' }
        Set-RegValue $FSUB 'Segoe UI' $fam 'String'
    }
    return @{ ok = $true; needsSignOut = $true }
}

Register-Api 'fonts.install' {
    param($a)
    $done = @()
    $shell = New-Object -ComObject Shell.Application
    $fontsFolder = $shell.Namespace(0x14)
    foreach ($p in @($a.paths)) {
        if (-not (Test-Path -LiteralPath $p) -or -not (Test-TCFileType $p @('font'))) { continue }
        $name = [IO.Path]::GetFileName($p)
        if (-not (Test-Path (Join-Path "$env:WINDIR\Fonts" $name))) { $fontsFolder.CopyHere($p, 0x10 + 0x4) }
        $done += $name
    }
    return ,$done
}
