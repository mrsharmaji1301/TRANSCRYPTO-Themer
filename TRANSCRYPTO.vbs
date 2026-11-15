' TRANSCRYPTO Themer launcher
' 1) starts the PowerShell backend elevated (one UAC prompt, hidden window)
' 2) opens the UI in the native Themer window (bin\TRANSCRYPTO.exe, WebView2); a browser app window only if it is missing
' The backend shuts itself down ~25 s after the window is closed.
Option Explicit
Dim fso, sh, app, root, data, sess, ps, i, port, token

Set fso = CreateObject("Scripting.FileSystemObject")
Set sh = CreateObject("WScript.Shell")
Set app = CreateObject("Shell.Application")
root = fso.GetParentFolderName(WScript.ScriptFullName)
data = sh.ExpandEnvironmentStrings("%LOCALAPPDATA%") & "\TRANSCRYPTO"
sess = data & "\session.json"
port = "" : token = ""

' Already running? Just open another window on it.
If fso.FileExists(sess) Then
  If ReadSession() Then
    If Alive() Then
      OpenWindow
      WScript.Quit
    End If
  End If
  On Error Resume Next
  fso.DeleteFile sess, True
  On Error GoTo 0
  port = "" : token = ""
End If

ps = sh.ExpandEnvironmentStrings("%WINDIR%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
app.ShellExecute ps, "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & root & "\core\server.ps1"" -NoBrowser", root, "runas", 0

For i = 1 To 200
  WScript.Sleep 150
  If fso.FileExists(sess) Then
    If ReadSession() Then Exit For
  End If
Next

If token = "" Then
  MsgBox "The TRANSCRYPTO backend did not start." & vbCrLf & "(Was the admin prompt cancelled? Check " & data & "\logs)", 48, "TRANSCRYPTO Themer"
  WScript.Quit
End If
OpenWindow

Function ReadSession()
  Dim f, txt, re, m
  ReadSession = False
  On Error Resume Next
  Set f = fso.OpenTextFile(sess, 1)
  If Err.Number <> 0 Then Exit Function
  txt = f.ReadAll : f.Close
  On Error GoTo 0
  Set re = New RegExp
  re.Pattern = """port""\s*:\s*(\d+)"
  Set m = re.Execute(txt)
  If m.Count = 0 Then Exit Function
  port = m(0).SubMatches(0)
  re.Pattern = """token""\s*:\s*""([0-9a-fA-F]+)"""
  Set m = re.Execute(txt)
  If m.Count = 0 Then Exit Function
  token = m(0).SubMatches(0)
  ReadSession = True
End Function

Function Alive()
  Dim x
  Alive = False
  On Error Resume Next
  Set x = CreateObject("MSXML2.ServerXMLHTTP.6.0")
  x.setTimeouts 800, 800, 800, 800
  x.Open "GET", "http://127.0.0.1:" & port & "/alive", False
  x.Send
  If Err.Number = 0 Then
    If x.Status = 200 Then Alive = True
  End If
  On Error GoTo 0
End Function

' Edge, else Chrome, else Brave, installed under Program Files (same order as Find-TCBrowser in modules\00-app.ps1)
Function FindEdge()
  Dim p, exe, c, pf, pf86
  FindEdge = ""
  For Each exe In Array("msedge.exe", "chrome.exe", "brave.exe")
    p = ""
    On Error Resume Next
    p = sh.RegRead("HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" & exe & "\")
    On Error GoTo 0
    p = Replace(p, """", "")
    If p <> "" Then
      If fso.FileExists(p) Then FindEdge = p : Exit Function
    End If
  Next
  pf = sh.ExpandEnvironmentStrings("%ProgramFiles%") : pf86 = sh.ExpandEnvironmentStrings("%ProgramFiles(x86)%")
  For Each c In Array(pf86 & "\Microsoft\Edge\Application\msedge.exe", pf & "\Microsoft\Edge\Application\msedge.exe", _
                      pf & "\Google\Chrome\Application\chrome.exe", pf86 & "\Google\Chrome\Application\chrome.exe", _
                      pf & "\BraveSoftware\Brave-Browser\Application\brave.exe")
    If fso.FileExists(c) Then FindEdge = c : Exit Function
  Next
End Function

Sub OpenWindow()
  Dim edge, url, args, native
  ' the native Themer window (WebView2), built by the installer; a browser app window only when it is missing
  native = root & "\bin\TRANSCRYPTO.exe"
  If fso.FileExists(native) Then
    sh.Run """" & native & """", 1, False
    Exit Sub
  End If
  url = "http://127.0.0.1:" & port & "/?t=" & token
  edge = FindEdge()
  If edge = "" Then
    sh.Run url
    Exit Sub
  End If
  args = " --app=""" & url & """ --user-data-dir=""" & data & "\ui-profile"" --window-size=1380,880 --no-first-run --no-default-browser-check" & _
         " --disable-extensions --disable-sync --disable-background-networking --disable-component-update --disable-features=Translate,msEdgeSidebarV2"
  sh.Run """" & edge & """" & args, 1, False
End Sub
