# TRANSCRYPTO Themer 2.1.0: test checklist

Note anything that fails or feels wrong with the theme name and step number, e.g. "Orbital 3.4". A screenshot helps.

## 0. Escape hatches (read first)
- **Ctrl+Alt+Shift+T** stops the custom shell and brings the Windows taskbar back.
- Red X on the bar (double click) exits TRANSCRYPTO completely.
- If the screen ever looks stuck: press **Esc**. The menu closes within a second, or the engine hands the screen back within 5 s.
- Last resort: Ctrl+Alt+Del → Task Manager → end `TCWall.exe`, then open the TRANSCRYPTO Themer once; it brings the Windows taskbar back when the engine isn't running.

## 1. Presets (Themer → Themes)
Apply each one: **SharmaJi Command Center**, **Orbital**, **Projector**, then one normal preset (e.g. Dracula).
- [ ] 1.1 Taskbar, live background, icons, cursors and colours all change together
- [ ] 1.2 The Windows taskbar never shows while a custom shell runs
- [ ] 1.3 The normal preset brings the Windows taskbar back and removes the live background

Do sections 2 to 6 once per shell theme.

## 2. Taskbar (bar)
- [ ] 2.1 Your Windows taskbar pins are all there, in the same order (Store apps too)
- [ ] 2.2 Running apps show a dot; opening more apps makes the bar longer
- [ ] 2.3 Left click: open / bring forward / minimise when it is already in front
- [ ] 2.4 Shift+click or middle click opens a new window
- [ ] 2.5 Drag an app left/right: a ghost (icon + name) follows, and the new order stays after a restart
- [ ] 2.6 Right-click an app: New window / Close window, Task Manager, …
- [ ] 2.7 Right-click empty bar: Task Manager, File Explorer, Show desktop, Settings, Themer
- [ ] 2.8 Right-click the Start button: Win+X style menu (Task Manager first)
- [ ] 2.9 `^` chevron: hidden tray icons open above it, and left/right clicks on them work
- [ ] 2.10 Hover the empty left/right ends: the gear (opens Themer) and red X appear
- [ ] 2.11 Maximise a window: it stops above the bar, nothing covers the bar
- [ ] 2.12 Fullscreen video/game (F11 / YouTube fullscreen): the bar steps aside, then comes back

## 3. Start menu
- [ ] 3.1 Win key and the Start button both open it; Esc and a click outside close it
- [ ] 3.2 It opens fully every time, including the first time after a restart
- [ ] 3.3 Pinned apps = your Windows Start pins; clicking one opens it and the menu closes at once
- [ ] 3.4 With the menu open, click a running app on the bar: the menu goes away at once and the app comes forward
- [ ] 3.5 All Apps (Orbital: bubble above SJ; SharmaJi: next to PINNED; Projector: top right): A-Z list, filter, launch
- [ ] 3.6 Search: type an app name, Enter launches it
- [ ] 3.7 Recent files / projects open the right file or folder
- [ ] 3.8 Lock / Sleep / Restart / Shut down (power buttons need a second click)
- [ ] 3.9 Orbital: the ring doesn't touch the bar; on close the orbiting icons leave first; CPU/GPU/RAM/Network bars move
- [ ] 3.10 Open and close it 10 times quickly: nothing sticks

## 4. Quick settings, notifications, calendar
- [ ] 4.1 Wi-Fi/volume icon (or Win+A): quick settings above the bar; volume slider, media keys and tiles work
- [ ] 4.2 Clock (or Win+N): notification centre and calendar; month arrows work
- [ ] 4.3 A new notification shows a glass banner top right; clicking it opens the app
- [ ] 4.4 Clear all: the list empties and stays empty after restarting the Themer / PC

## 5. Wallpapers
- [ ] 5.1 Wallpapers → Library: the SCENE tiles (3 scenes × 3 palettes) apply with "Apply scene"
- [ ] 5.2 Then apply a picture or a video: the scene is replaced (it doesn't stay on top)
- [ ] 5.3 A maximised window pauses the live background (Task Manager: low CPU)

## 6. Icons
- [ ] 6.1 C: and every other drive get the pack's drive icon (This PC, F5)
- [ ] 6.2 File Explorer on the bar and the Downloads/Documents/... pins use the pack's icons

## 7. Stability (leave it running)
- [ ] 7.1 Use the PC normally for an hour or two: no freeze, no slowdown
- [ ] 7.2 Task Manager: TRANSCRYPTO's `chrome.exe` and `TCWall.exe` memory stays roughly flat
- [ ] 7.3 Sleep/wake and a full restart: the shell comes back on its own (with Start with Windows on)
