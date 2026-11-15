# TRANSCRYPTO Themer uninstaller
# -Quiet: no questions (your library, backups and saved themes are kept); add -RemoveData to delete them too
param([switch]$Quiet, [switch]$RemoveData)
$ErrorActionPreference = 'SilentlyContinue'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $fwd = ''; if ($Quiet) { $fwd += ' -Quiet' }; if ($RemoveData) { $fwd += ' -RemoveData' }
    Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"' + $fwd); exit
}
$dest = Split-Path -Parent $PSScriptRoot
Write-Host 'Uninstalling TRANSCRYPTO Themer...' -ForegroundColor Magenta
if (-not $Quiet) {
    Write-Host 'Your theme stays applied. To go back to stock Windows first, use Backup & Restore -> Restore EVERYTHING to Windows 11 default.' -ForegroundColor Yellow
    $go = Read-Host 'Continue uninstall? (y/n)'
    if ($go -ne 'y') { exit }
}

try { $e = [System.Threading.EventWaitHandle]::OpenExisting('Local\TRANSCRYPTO_TCWall_Exit'); [void]$e.Set() } catch {}
Start-Sleep -Milliseconds 1500
Get-Process TCWall | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='TRANSCRYPTO.exe'" | Where-Object { $_.ExecutablePath -like "$dest*" } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }   # the Themer window
# whatever state the engine was in, the Windows taskbar must be back
try { . (Join-Path $dest 'core\common.ps1'); Show-WindowsTaskbar } catch {}
$ErrorActionPreference = 'SilentlyContinue'   # common.ps1 sets 'Stop': one missing item must not end the uninstall half way
# the Themer backend and the notification helper, if they are running
try {
    $bpid = [int](Get-Content (Join-Path $env:LOCALAPPDATA 'TRANSCRYPTO\session.json') -Raw | ConvertFrom-Json).pid
    $p = Get-CimInstance Win32_Process -Filter "ProcessId = $bpid"
    if ($p -and $p.CommandLine -like '*server.ps1*') { Stop-Process -Id $bpid -Force }
} catch {}
Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" | Where-Object { $_.CommandLine -like '*notify-watch.ps1*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
if (Test-Path (Join-Path $env:ProgramData 'TRANSCRYPTO')) { Remove-Item (Join-Path $env:ProgramData 'TRANSCRYPTO') -Recurse -Force }
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'TRANSCRYPTO Engine'
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'TRANSCRYPTO Themer.lnk') -Force
# desktop icon from versions before 2.2.2
Remove-Item (Join-Path ([Environment]::GetFolderPath('Desktop')) 'TRANSCRYPTO Themer.lnk') -Force
Remove-Item (Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'TRANSCRYPTO Themer.lnk') -Force
Remove-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TRANSCRYPTO Themer' -Recurse -Force

$wipe = $RemoveData
if (-not $Quiet) { $wipe = (Read-Host 'Keep your library, backups and saved themes (%LOCALAPPDATA%\TRANSCRYPTO)? (y/n)') -eq 'n' }
if ($wipe) { Remove-Item (Join-Path $env:LOCALAPPDATA 'TRANSCRYPTO') -Recurse -Force }

# delete the program folder after this script exits
Start-Process cmd.exe -ArgumentList ('/c timeout /t 2 >nul & rd /s /q "' + $dest + '"') -WindowStyle Hidden
Write-Host 'Removed. Bye!' -ForegroundColor Green
if (-not $Quiet) { Start-Sleep -Seconds 2 }
