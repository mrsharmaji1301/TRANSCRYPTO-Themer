# TRANSCRYPTO Themer: security audit (2026-11-03)

Scope:
- The administrator backend: `core/server.ps1` and every `Register-Api` in `modules/`.
- The update and install path.
- The engine's local shell server (`engine/ShellHost.cs`).
- The shell pages (`shell/`) and the Themer UI (`ui/`).

Threat model: a program running as the same Windows user (no admin) tries to use TRANSCRYPTO to get administrator rights or to damage the system. A malicious website in the user's browser, and a crafted theme, backup or update file, are also covered.

Tests (all local, nothing installed):
- `docs/security-test.js`: an HTTP side instance of the backend, 17 checks.
- `docs/security-unit.ps1`: signature, zip-slip and registry allowlist, 11 checks.
- `docs/shell-test.js`: headless functional run of the three shells, 90 checks.

## Findings

| # | Severity | Finding | Status |
|---|---|---|---|
| 1 | Critical | The backend runs as administrator. Its token is in `%LOCALAPPDATA%\TRANSCRYPTO\session.json` and in the UI window's command line, both readable by any program of the user. Any such program could call every API as administrator (UAC bypass). | Mitigated: API calls must come from a browser process (TCP owner check), not just carry the token. Residual risk below. |
| 2 | Critical | `update.installFile` / `update.apply` installed any zip into Program Files, and it ran as administrator on the next start. | Fixed: packages must carry `signature.json` (RSA-3072, SHA-256) over every installable file. The private key stays on the developer PC (`%USERPROFILE%\.transcrypto`); unsigned, altered or extra files are refused. |
| 3 | Critical | Update packages were unpacked into `%LOCALAPPDATA%\…\cache\update`, which the user can write. A program could swap files between the check and the copy. | Fixed: unpacked and verified in a fresh admin-only folder (`%WINDIR%\Temp\TRANSCRYPTO-update-<guid>`, protected ACL from creation). |
| 4 | Critical | Backup restore wrote any registry value named in the backup file, as administrator (for example an IFEO "debugger" makes Windows run any program). | Fixed: only registry entries TRANSCRYPTO itself tracks are restored; subkey names can't contain `..` or slashes. |
| 5 | Critical | Backup restore copied the backup's files to any path that didn't exist yet (DLL planting, as administrator). | Fixed: files go back only under the user profile, TRANSCRYPTO data or Public, and only as media/icon/cursor/sound/font files; anything else lands in `restored\`. |
| 6 | High | HackBGRT `setup.exe` ran as administrator from `ProgramData\TRANSCRYPTO\HackBGRT`, where users can add files (DLL hijack). A pre-made junction there could also turn the admin "delete" into deleting another folder. | Fixed: a fresh admin-only folder per run, for both install and uninstall. |
| 7 | High | `/upload?dir=` wrote any file anywhere as administrator. | Fixed: library only, pictures/videos only. |
| 8 | High | `/media?p=` returned any file as administrator. | Fixed: media, icon, cursor, sound and font types only. |
| 9 | High | `backup.delete` deleted any file; `wallpapers.delete` could be escaped with `library\..\`; `wallpapers.rename` renamed any file. | Fixed: resolved-path checks (`Test-TCChildPath`) against their own folders. |
| 10 | High | `boot.restoreBcd` imported any file as the boot configuration. | Fixed: only `.bcd` backups in the TRANSCRYPTO backup folder. |
| 11 | High | Zip extraction (themes, backups, updates) used `Expand-Archive`; entries with `..\` could write outside the target. | Fixed: `Expand-TCZip` refuses entries that resolve outside the destination. |
| 12 | Medium | Copy/import APIs read any file as administrator into user-readable folders: `wallpapers.import`, `icons.setFolderIcon`, `lock.setImage`, `fonts.install`, `cursors.importPack`, `backup.importFile`, `themes.import`. | Fixed: each accepts only its own file types. |
| 13 | Medium | `themes.export` wrote to any path; `themes.deleteMine` id could contain `..\`. | Fixed: `.tctheme` only; id reduced to a file name inside the themes folder. |
| 14 | Medium | `wallpapers.fromUrl` saved with whatever extension the server's headers said (`.exe`, `.ps1`). | Fixed: picture/video extensions only. |
| 15 | Low | Static files: prefix check `StartsWith(root)` also matched sibling folders (`…\TRANSCRYPTO Themer-x`). Same in the engine for `shell\` vs `shell-profile\`. | Fixed: checked with a trailing separator / resolved child path. |
| 16 | Low | Notification card used an inline `onerror` handler built from app names; `Shell.esc` didn't escape `'`. | Fixed: no inline handlers; `'` escaped. |

### Second pass (exploit-driven, same day)

Each finding below was tried as a real attack first. The fix was then confirmed by the same attack in `docs/security-test.js`.

| # | Severity | Finding (how it was exploited) | Status |
|---|---|---|---|
| 17 | Critical | The caller check only compared the process **name**. Copying `node.exe` (or curl, python…) to `%TEMP%\chrome.exe` passed it, so a script with the token had full admin API access. | Fixed: the caller's program file must be under Program Files (admin-only) and carry a valid Authenticode signature from a known browser maker (Microsoft, Google, Brave, Mozilla, Opera, Vivaldi). Test: renamed node → 403. |
| 18 | Critical | A malicious program could start a real, signed Chrome/Edge with `--remote-debugging-port` (or headless / automation / side-loaded extension) and drive it to call the API. | Fixed: the browser's main process command line is checked. Remote control, headless, automation, `--load-extension`, proxy and host-rule flags are refused. Test: Chrome with a DevTools port → 403. |
| 19 | Critical | **No-preflight bypass.** A `file://` or `data:` page opened in an allowed browser sent `fetch(…/api?t=TOKEN, {mode:'no-cors', body: '{"fn":…}'})`. A text/plain POST needs no CORS preflight, and the server read the token from the URL, so admin calls ran. | Fixed: `/api` and `/upload` take the token only from the `X-TC-Token` header. A page of another origin can send that header only after a preflight, which this server never answers. They also need POST, and an `Origin` that is this server's own origin when present. Test: no-cors POST does nothing; the header request is blocked by CORS. |
| 20 | High | Without Edge, the window was a tab in the default browser, with all of the user's extensions able to read the page and its token. Any program could also start that browser with any profile. | Fixed: Edge, then Chrome, then Brave (Program Files installs) open the window as an app with TRANSCRYPTO's own `ui-profile` and `--disable-extensions`. When one exists, the backend only answers a browser started that way. |
| 21 | Medium | `/media` served files of the allowed types from anywhere. A crafted file with an image extension could still be sniffed as HTML. | Fixed: `X-Content-Type-Options: nosniff` and `Content-Security-Policy: sandbox`. A preview can never run as a page of the app's origin. |
| 22 | Medium | `app.openPath` passed any string to an elevated `explorer.exe "<arg>"`. Non-existing paths, `shell:` or program names were handed to Explorer. `app.openUrl` accepted any scheme (`file:`, program paths). | Fixed: openPath only opens existing folders and only *selects* files (never runs them). Quotes are refused. openUrl only accepts http(s) URLs. |
| 23 | Low | `icons` per-drive config accepted any first character as a drive letter (`.`, `\`), writing to the wrong DriveIcons key. | Fixed: A–Z only. |

Reviewed and not exploitable:
- Registry writes of caller-controlled values. Icon values are only read for icons (Explorer loads them as data, not code). Cursor and sound values are HKCU (no gain over what the user can already do). Start-menu custom tweaks build their script from fixed names only. The font substitute is a plain name.
- `settings.set` → `updateRepo`: a different repo still has to pass the signature check.
- `engine.setConfig`: the engine runs as the user, so nothing gained.

### 2.4.0: taskbar, Start menu and live scenes on WebView2 (2026-11-05)

Before 2.4 the custom taskbar and the live scenes were hidden Chrome `--app` windows with their own profiles (`shell-profile`, `bg-profile`). They are now WebView2 controls inside the engine's own windows (data folders `shell-webview2`, `bg-webview2`).
- Navigation is locked to the engine's own `http://127.0.0.1:<port>` origin. Any other navigation (a link, a dropped file, a script) is cancelled and logged. New windows, downloads and permission requests are refused.
- Off: DevTools, default context menus, browser accelerator keys (F5, Ctrl+R, Ctrl+P, Ctrl+F), zoom, swipe navigation, autofill, password saving, host objects and the built-in error page.
- Web messages are only enabled for the bar's page, and their source origin is checked. Dropped file paths come from WebView2 itself (`CoreWebView2File`) and are only used when they exist.
- The engine clears the `WEBVIEW2_*` environment variables at start (browser folder, extra browser arguments, user data folder, release channel, script debugger). So a same-user program can't point WebView2 at another runtime or add `--remote-debugging-port` through HKCU\Environment. The policy overrides under `Software\Policies` need admin rights.
- The first 2.4 start closes the old Chrome windows and deletes their profiles. It also deletes `ui-profile` when the native Themer window is installed.

## Checked and fine
- DNS rebinding: the backend `HttpListener` prefix is `127.0.0.1` (Host must match); the engine checks Host.
- CSRF from websites: every API needs the token (header or query). Websites can't read it, and there are no CORS headers.
- The engine (taskbar) starts unelevated (`Start-Unelevated` through explorer.exe). Its `launch` action runs programs as the user only.
- Program Files is writable only by Administrators/SYSTEM, so the code the backend loads can't be changed by the user.
- No secrets in the repository. The signing key is outside it.
- Themer UI: dynamic text goes through text nodes or `esc()`.

## Residual risk (not fixed)
- **Same-user malware inside the app's own browser.** After #17-#20, a program can no longer use scripts, renamed tools, remote-controlled browsers or other-origin pages. What is left needs code running *inside* the real app window. Malware can get there by injecting into the browser process (any same-user program can) or by planting a service worker in the user-writable `ui-profile`. Code files are now always `no-store`, so the disk cache can't be used for this. This is the general Windows rule that UAC is not a boundary against malware already running as the user. The full fix is still to run the backend without admin rights and ask UAC for each admin step (HKLM icons, boot logo, lock screen, fonts, Start policies, restore points). Recommended for v2.3.
- The shell server's token (`shell.json`) lets any user program control the taskbar (launch apps, exit). It runs as the user, so this gives no extra rights.
- `ProgramData\TRANSCRYPTO` (lock-screen image store) is data only. A pre-made junction could redirect where the lock image is copied (low).

## For releases
1. Once: `tools\new-signing-key.ps1` (done; keep an offline copy of `%USERPROFILE%\.transcrypto\update-signing-key.xml`).
2. Every release: `tools\make-update.ps1`. It signs, writes `signature.json` (commit it for GitHub updates), builds the zip and verifies it with the app's own check.
3. Note: installs older than this fix don't check signatures yet. Install this version with `Setup.cmd` (or a zip) once; from then on only signed updates are accepted.
