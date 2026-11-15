# Start Menu, Taskbar and File Explorer tweaks (rendered generically by the UI)

$ADV  = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
$EXPL = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer'
$SRCH = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Search'
$PMS  = 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Start'

# ================================================================ START MENU
Register-RegTweak -Tab 'startmenu' -Section 'Layout' -Id 'start.layout' -Kind 'select' -Label 'Start layout' -Desc 'How much space pins vs recommendations get' -Path $ADV -Name 'Start_Layout' -AbsentIs 0 -Options @(@{ v = 0; l = 'Default' }, @{ v = 1; l = 'More pins' }, @{ v = 2; l = 'More recommendations' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.recentApps' -Label 'Show recently added apps' -Path 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' -Name 'HideRecentlyAddedApps' -On $null -Off 1 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.mostUsed' -Label 'Show most used apps' -Path $ADV -Name 'Start_TrackProgs' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.recentFiles' -Label 'Show recent files (Start, Jump Lists, Explorer)' -Path $ADV -Name 'Start_TrackDocs' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.tips' -Label 'Show tips, app promotions and new apps' -Path $ADV -Name 'Start_IrisRecommendations' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.account' -Label 'Show account-related notifications' -Path $ADV -Name 'Start_AccountNotifications' -AbsentIs 'on' -Restart 'explorer-live'

Register-Tracked 'startmenu' $PMS 'HideRecommendedSection' 'DWord' $null
Register-Tracked 'startmenu' 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Education' 'IsEducationEnvironment' 'DWord' $null
Register-Tracked 'startmenu' 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection' 'DWord' $null
Register-CustomTweak -Tab 'startmenu' -Section 'Recommended' -Id 'start.hideRecommended' -Label 'Remove the whole "Recommended" section' -Desc 'Uses the Education-edition policy. Works on Home/Pro too.' -Admin -Restart 'explorer' -Get {
    return ((Get-RegValue $PMS 'HideRecommendedSection') -eq 1)
} -Set {
    param($v)
    if ($v) {
        Set-RegValue $PMS 'HideRecommendedSection' 1
        Set-RegValue 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Education' 'IsEducationEnvironment' 1
        Set-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection' 1
    } else {
        Remove-RegValue $PMS 'HideRecommendedSection'
        Remove-RegValue 'HKLM:\SOFTWARE\Microsoft\PolicyManager\current\device\Education' 'IsEducationEnvironment'
        Remove-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Explorer' 'HideRecommendedSection'
    }
}

foreach ($f in @(@('Settings', 'Settings'), @('FileExplorer', 'File Explorer'), @('Documents', 'Documents'), @('Downloads', 'Downloads'), @('Music', 'Music'),
                 @('Pictures', 'Pictures'), @('Videos', 'Videos'), @('Network', 'Network'), @('PersonalFolder', 'Personal folder'))) {
    $name = 'AllowPinnedFolder' + $f[0]
    Register-Tracked 'startmenu' $PMS $name 'DWord' $null
    Register-Tracked 'startmenu' $PMS ($name + '_ProviderSet') 'DWord' $null
    $sb = [scriptblock]::Create(@"
param(`$v)
if (`$v) { Set-RegValue '$PMS' '$name' 1; Set-RegValue '$PMS' '${name}_ProviderSet' 1 }
else { Remove-RegValue '$PMS' '$name'; Remove-RegValue '$PMS' '${name}_ProviderSet' }
"@)
    $gb = [scriptblock]::Create("return ((Get-RegValue '$PMS' '$name') -eq 1)")
    Register-CustomTweak -Tab 'startmenu' -Section 'Folders next to Power button' -Id ('start.folder.' + $f[0]) -Label $f[1] -Admin -Restart 'explorer' -Get $gb -Set $sb
}

Register-RegTweak -Tab 'startmenu' -Section 'Search' -Id 'start.webSearch' -Label 'Bing web results in Start search' -Path 'HKCU:\Software\Policies\Microsoft\Windows\Explorer' -Name 'DisableSearchBoxSuggestions' -On $null -Off 1 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'startmenu' -Section 'Search' -Id 'start.bing' -Label 'Bing search integration' -Path $SRCH -Name 'BingSearchEnabled' -On 1 -Off 0 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'startmenu' -Section 'Search' -Id 'start.searchHighlights' -Label 'Search highlights (trending pictures)' -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\SearchSettings' -Name 'IsDynamicSearchBoxEnabled' -On 1 -Off 0 -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'startmenu' -Section 'Layout' -Id 'start.jumpCount' -Kind 'select' -Label 'Jump List items' -Path $ADV -Name 'JumpListItems_Maximum' -AbsentIs 10 -Options @(@{ v = 5; l = '5' }, @{ v = 10; l = '10' }, @{ v = 20; l = '20' }, @{ v = 30; l = '30' }) -Restart 'explorer'

# ================================================================ TASKBAR
Register-RegTweak -Tab 'taskbar' -Section 'Layout' -Id 'tb.align' -Kind 'select' -Label 'Alignment' -Path $ADV -Name 'TaskbarAl' -AbsentIs 1 -Options @(@{ v = 0; l = 'Left' }, @{ v = 1; l = 'Center' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Layout' -Id 'tb.search' -Kind 'select' -Label 'Search on taskbar' -Path $SRCH -Name 'SearchboxTaskbarMode' -AbsentIs 2 -Options @(@{ v = 0; l = 'Hidden' }, @{ v = 1; l = 'Icon' }, @{ v = 3; l = 'Icon + label' }, @{ v = 2; l = 'Search box' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Layout' -Id 'tb.combine' -Kind 'select' -Label 'Combine buttons & hide labels' -Path $ADV -Name 'TaskbarGlomLevel' -AbsentIs 0 -Options @(@{ v = 0; l = 'Always' }, @{ v = 1; l = 'When taskbar is full' }, @{ v = 2; l = 'Never' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Layout' -Id 'tb.combineOther' -Kind 'select' -Label 'Combine buttons on other displays' -Path $ADV -Name 'MMTaskbarGlomLevel' -AbsentIs 0 -Options @(@{ v = 0; l = 'Always' }, @{ v = 1; l = 'When taskbar is full' }, @{ v = 2; l = 'Never' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Layout' -Id 'tb.small' -Kind 'select' -Label 'Small taskbar buttons' -Desc 'Windows 11 24H2+' -Path $ADV -Name 'IconSizePreference' -AbsentIs 2 -Options @(@{ v = 0; l = 'Always' }, @{ v = 2; l = 'When taskbar is full' }, @{ v = 1; l = 'Never' }) -Restart 'explorer'
Register-RegTweak -Tab 'taskbar' -Section 'Buttons' -Id 'tb.taskview' -Label 'Task View button' -Path $ADV -Name 'ShowTaskViewButton' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Buttons' -Id 'tb.copilot' -Label 'Copilot button' -Path $ADV -Name 'ShowCopilotButton' -AbsentIs 'off' -Restart 'explorer-live'
Register-Tracked 'taskbar' $ADV 'TaskbarDa' 'DWord' $null
Register-Tracked 'taskbar' 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests' 'DWord' $null
# The UCPD driver denies TaskbarDa and the Dsh policy to scripts (even elevated), so skip no-op writes and report a block clearly
Register-CustomTweak -Tab 'taskbar' -Section 'Buttons' -Id 'tb.widgets' -Label 'Widgets button' -Desc 'Windows may block this one; then use Settings > Personalization > Taskbar' -Restart 'explorer' -Get {
    if ((Get-RegValue 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh' 'AllowNewsAndInterests') -eq 0) { return $false }
    return ((Get-RegValue $ADV 'TaskbarDa') -ne 0)
} -Set {
    param($v)
    $pol = 'HKLM:\SOFTWARE\Policies\Microsoft\Dsh'
    $on = [bool]$v
    $cur = ((Get-RegValue $pol 'AllowNewsAndInterests') -ne 0) -and ((Get-RegValue $ADV 'TaskbarDa') -ne 0)
    if ($cur -eq $on) { return }
    try {
        if ($on) { Remove-RegValue $pol 'AllowNewsAndInterests'; Set-RegValue $ADV 'TaskbarDa' 1 }
        else { try { Set-RegValue $ADV 'TaskbarDa' 0 } catch { Set-RegValue $pol 'AllowNewsAndInterests' 0 } }
    } catch {
        if ($_.Exception.GetBaseException() -is [UnauthorizedAccessException]) { throw 'Windows protects the Widgets setting (UCPD). Change it in Settings > Personalization > Taskbar.' }
        throw
    }
}
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.seconds' -Label 'Show seconds in the clock' -Path $ADV -Name 'ShowSecondsInSystemClock' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.endtask' -Label '"End task" in taskbar right-click menu' -Path "$ADV\TaskbarDeveloperSettings" -Name 'TaskbarEndTask' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.badges' -Label 'Badges (unread counters) on apps' -Path $ADV -Name 'TaskbarBadges' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.flash' -Label 'Flashing on apps' -Path $ADV -Name 'TaskbarFlashing' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.peek' -Label 'Show desktop by clicking the far corner' -Path $ADV -Name 'TaskbarSd' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.allDisplays' -Label 'Show taskbar on all displays' -Path $ADV -Name 'MMTaskbarEnabled' -AbsentIs 'off' -Restart 'explorer-live'

# Neon effects are drawn by the TCWall engine (Windows has no setting for them), so these live in engine.json
function Set-EngineFx([string]$Key, [bool]$On) {
    $cfg = Get-EngineConfig
    if ([bool]$cfg[$Key].enabled -eq $On) { return }
    $cfg[$Key].enabled = $On
    Save-EngineConfig $cfg
    [void](Sync-Engine)
}
Register-CustomTweak -Tab 'taskbar' -Section 'Effects' -Id 'tb.fxHover' -Label 'Neon glow on hovered taskbar buttons' -Desc 'Drawn by the TRANSCRYPTO engine in your accent color' -Get {
    return [bool](Get-EngineConfig).taskbarFx.enabled
} -Set { param($v) Set-EngineFx 'taskbarFx' ([bool]$v) }
Register-CustomTweak -Tab 'taskbar' -Section 'Effects' -Id 'tb.fxClick' -Label 'Neon ripple on every click' -Desc 'Taskbar, desktop and apps. Style and size are in Cursors' -Get {
    return [bool](Get-EngineConfig).clickFx.enabled
} -Set { param($v) Set-EngineFx 'clickFx' ([bool]$v) }
Register-RegTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.mmMode' -Kind 'select' -Label 'Taskbar apps on multiple displays' -Path $ADV -Name 'MMTaskbarMode' -AbsentIs 0 -Options @(@{ v = 0; l = 'All taskbars' }, @{ v = 1; l = 'Main + where window is open' }, @{ v = 2; l = 'Where window is open' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'taskbar' -Section 'Look' -Id 'tb.animations' -Label 'Taskbar animations' -Path $ADV -Name 'TaskbarAnimations' -AbsentIs 'on' -Restart 'explorer'
Register-RegTweak -Tab 'taskbar' -Section 'Look' -Id 'tb.oled' -Label 'Extra translucent taskbar' -Desc 'More see-through taskbar (build dependent)' -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced' -Name 'UseOLEDTaskbarTransparency' -AbsentIs 'off' -Admin -Restart 'explorer'

$STUCK = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3'
Register-Tracked 'taskbar' $STUCK 'Settings' 'Binary' $null
Register-CustomTweak -Tab 'taskbar' -Section 'Behaviour' -Id 'tb.autohide' -Label 'Automatically hide the taskbar' -Restart 'explorer' -Get {
    $b = Get-RegValue $STUCK 'Settings'; if (-not $b) { return $false }; return ($b[8] -eq 3)
} -Set {
    param($v)
    $b = Get-RegValue $STUCK 'Settings'; if (-not $b) { return }
    $b[8] = if ($v) { 3 } else { 2 }
    Set-RegValue $STUCK 'Settings' ([byte[]]$b) 'Binary'
}

# ================================================================ FILE EXPLORER
$CLASSIC = 'HKCU:\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}'
Register-Tracked 'explorer' $CLASSIC '' 'String' $null -Tree
Register-CustomTweak -Tab 'explorer' -Section 'Context menu' -Id 'ex.classicMenu' -Label 'Classic right-click menu (no "Show more options")' -Restart 'explorer' -Get {
    return (Test-Path "$CLASSIC\InprocServer32")
} -Set {
    param($v)
    if ($v) { Set-RegValue "$CLASSIC\InprocServer32" '(default)' '' 'String' } else { Remove-RegKey $CLASSIC }
}
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.compact' -Label 'Compact view (less padding)' -Path $ADV -Name 'UseCompactMode' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.ext' -Label 'Show file extensions' -Path $ADV -Name 'HideFileExt' -On 0 -Off 1 -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.hidden' -Label 'Show hidden files' -Path $ADV -Name 'Hidden' -On 1 -Off 2 -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.super' -Label 'Show protected system files' -Path $ADV -Name 'ShowSuperHidden' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.checkbox' -Label 'Item check boxes' -Path $ADV -Name 'AutoCheckSelect' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'View' -Id 'ex.fullpath' -Label 'Full path in the title bar' -Path "$EXPL\CabinetState" -Name 'FullPath' -AbsentIs 'off' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Navigation' -Id 'ex.launch' -Kind 'select' -Label 'Open File Explorer to' -Path $ADV -Name 'LaunchTo' -AbsentIs 2 -Options @(@{ v = 2; l = 'Home' }, @{ v = 1; l = 'This PC' }, @{ v = 3; l = 'Downloads' }) -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Navigation' -Id 'ex.recent' -Label 'Show recent files in Home' -Path $EXPL -Name 'ShowRecent' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Navigation' -Id 'ex.frequent' -Label 'Show frequent folders in Home' -Path $EXPL -Name 'ShowFrequent' -AbsentIs 'on' -Restart 'explorer-live'
foreach ($n in @(@('Home', '{f874310e-b6b7-47dc-bc84-b9e6b38f5903}'), @('Gallery', '{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}'))) {
    $p = "HKCU:\Software\Classes\CLSID\$($n[1])"
    Register-RegTweak -Tab 'explorer' -Section 'Navigation' -Id ('ex.nav' + $n[0]) -Label ('Show "' + $n[0] + '" in the navigation pane') -Path $p -Name 'System.IsPinnedToNameSpaceTree' -On $null -Off 0 -AbsentIs 'on' -Restart 'explorer'
}
Register-RegTweak -Tab 'explorer' -Section 'Navigation' -Id 'ex.separate' -Label 'Open folder windows in a separate process' -Desc 'One crash does not take the taskbar down' -Path $ADV -Name 'SeparateProcess' -AbsentIs 'off' -Restart 'explorer'
Register-RegTweak -Tab 'explorer' -Section 'Windows & Snap' -Id 'ex.snapFlyout' -Label 'Snap layouts on maximize-button hover' -Path $ADV -Name 'EnableSnapAssistFlyout' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Windows & Snap' -Id 'ex.snapBar' -Label 'Snap bar when dragging a window to the top' -Path $ADV -Name 'EnableSnapBar' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Windows & Snap' -Id 'ex.snapAssist' -Label 'Suggest what to snap next to it' -Path $ADV -Name 'SnapAssist' -AbsentIs 'on' -Restart 'explorer-live'
Register-RegTweak -Tab 'explorer' -Section 'Effects' -Id 'ex.minAnimate' -Label 'Animate windows when minimizing / maximizing' -Path 'HKCU:\Control Panel\Desktop\WindowMetrics' -Name 'MinAnimate' -Type 'String' -On '1' -Off '0' -AbsentIs 'on' -Restart 'signout'
Register-RegTweak -Tab 'explorer' -Section 'Effects' -Id 'ex.menuDelay' -Kind 'select' -Label 'Menu open delay' -Desc 'Lower = snappier cascading menus' -Path 'HKCU:\Control Panel\Desktop' -Name 'MenuShowDelay' -Type 'String' -AbsentIs '400' -Options @(@{ v = '400'; l = 'Default (400 ms)' }, @{ v = '200'; l = 'Fast (200 ms)' }, @{ v = '50'; l = 'Instant (50 ms)' }) -Restart 'signout'

foreach ($c in @('startmenu', 'taskbar', 'explorer')) { $TCRefresh[$c] = { Restart-Explorer } }
