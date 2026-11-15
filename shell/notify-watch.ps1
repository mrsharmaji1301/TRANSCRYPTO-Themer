# TRANSCRYPTO shell - notification watcher (started by the engine while a custom shell runs).
# Reads Windows notifications through UserNotificationListener and writes one JSON line per change to stdout:
#   {"op":"add","id":..,"app":..,"aumid":..,"title":..,"body":..,"time":..}   {"op":"remove","id":..}
# Commands on stdin: "remove <id>", "clear".
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.UI.Notifications.Management.UserNotificationListener, Windows.UI.Notifications, ContentType = WindowsRuntime]
[void][Windows.UI.Notifications.UserNotification, Windows.UI.Notifications, ContentType = WindowsRuntime]

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
function Await($op, [Type]$t) { $task = $asTask.MakeGenericMethod($t).Invoke($null, @($op)); [void]$task.Wait(5000); return $task.Result }

$listener = [Windows.UI.Notifications.Management.UserNotificationListener]::Current
if ($listener.GetAccessStatus() -ne 'Allowed') {
    $st = Await ($listener.RequestAccessAsync()) ([Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus])
    if ($st -ne 'Allowed') { '{"op":"denied"}'; exit 1 }
}
$listType = [System.Collections.Generic.IReadOnlyList[Windows.UI.Notifications.UserNotification]]
$known = @{}
$stdin = New-Object IO.StreamReader([Console]::OpenStandardInput())   # [Console]::In.ReadLineAsync() blocks in Windows PowerShell; this one does not
$readTask = $stdin.ReadLineAsync()

function Out-Json($o) { [Console]::Out.WriteLine(($o | ConvertTo-Json -Compress)); [Console]::Out.Flush() }

while ($true) {
    try {
        $list = Await ($listener.GetNotificationsAsync([Windows.UI.Notifications.NotificationKinds]::Toast)) $listType
        $seen = @{}
        foreach ($n in $list) {
            $seen[$n.Id] = 1
            if ($known.ContainsKey($n.Id)) { continue }
            $known[$n.Id] = 1
            $texts = @()
            try { $b = $n.Notification.Visual.GetBinding([Windows.UI.Notifications.KnownNotificationBindings]::ToastGeneric); if ($b) { $texts = @($b.GetTextElements() | ForEach-Object { $_.Text }) } } catch {}
            $app = ''; $aumid = ''
            try { $app = $n.AppInfo.DisplayInfo.DisplayName; $aumid = $n.AppInfo.AppUserModelId } catch {}
            Out-Json ([ordered]@{ op = 'add'; id = $n.Id; app = $app; aumid = $aumid; title = ($texts | Select-Object -First 1); body = (($texts | Select-Object -Skip 1) -join "`n"); time = $n.CreationTime.LocalDateTime.ToString('o') })
        }
        foreach ($id in @($known.Keys)) { if (-not $seen.ContainsKey($id)) { $known.Remove($id); Out-Json ([ordered]@{ op = 'remove'; id = $id }) } }
    } catch { Out-Json ([ordered]@{ op = 'error'; message = $_.Exception.Message }) }

    # commands from the engine
    while ($readTask.IsCompleted) {
        $line = $readTask.Result
        if ($null -eq $line) { exit 0 }   # engine went away
        if ($line -like 'remove *') { try { $listener.RemoveNotification([uint32]$line.Substring(7)) } catch {} }
        elseif ($line -eq 'clear') {
            # ClearNotifications() silently does nothing for a desktop app; removing them one by one works
            foreach ($id in @($known.Keys)) { try { $listener.RemoveNotification([uint32]$id) } catch {} }
            try { $listener.ClearNotifications() } catch {}
        }
        $readTask = $stdin.ReadLineAsync()
    }
    Start-Sleep -Milliseconds 700
}
