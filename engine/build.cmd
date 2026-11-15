@echo off
rem Builds TCWall.exe with the C# compiler that ships with Windows (.NET Framework 4.x). No downloads.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
for %%I in ("%CSC%") do set WPF=%%~dpIWPF
if not exist "%~dp0..\bin" mkdir "%~dp0..\bin"
taskkill /im TCWall.exe /f >nul 2>&1
"%CSC%" /nologo /target:winexe /optimize+ /platform:anycpu /out:"%~dp0..\bin\TCWall.exe" /win32icon:"%~dp0..\assets\app.ico" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /lib:"%WPF%" /r:UIAutomationClient.dll /r:UIAutomationTypes.dll /r:WindowsBase.dll "%~dp0*.cs"
if errorlevel 1 (echo. & echo BUILD FAILED & pause & exit /b 1)
echo Built bin\TCWall.exe
