@echo off
rem TRANSCRYPTO Themer - one-click installer (asks for admin once)
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process powershell.exe -Verb RunAs -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"%~dp0installer\Install.ps1\"'"
