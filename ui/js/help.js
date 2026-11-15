/* TRANSCRYPTO Themer — top menu: Help, Features (knowledge base), What's new, Updates, About */
'use strict';

/* What every section changes in Windows. The exact registry values come live from the backend (app.knowledge). */
const KB = [
  { id: 'themes', title: 'Themes', comp: [],
    what: 'One click applies a whole look: wallpaper or live scene, colors, icons, cursors, sounds, Start menu/taskbar layout and the custom shell. "Keep my customizations" applies the preset under the changes you made yourself.',
    changes: ['Everything the other sections change, as one bundle', 'My Themes: saved to %LOCALAPPDATA%\\TRANSCRYPTO\\themes', 'Export / import as a .tctheme file (zip with the theme and its media)'] },
  { id: 'shells', title: '3 custom shells: SharmaJi Command Center, Orbital, Projector', comp: [], view: 'taskbar', tab: '',
    what: 'Replace the Windows taskbar and Start menu with a fully animated TRANSCRYPTO shell. Each design brings its own taskbar, Start menu, live background, icon pack and preset.',
    changes: [
      'SharmaJi Command Center: saffron x cyan HUD, pin tiles, All Apps next to PINNED, three palettes (SharmaJi, TRANSCRYPTO, Alien)',
      'Orbital: icons orbit a central core with your initials; All Apps bubble on the ring; recent files stack on the left, live CPU / GPU / RAM / network cards on the right',
      'Projector: cinematic projector-beam Start menu with All apps',
      'Real running apps (click, minimize, restore, right-click), your pins from the original Windows taskbar with the applied icon pack, drag to reorder, tray with hidden icons, clock, notifications with Clear all, search',
      'Win key opens Start, Esc closes it, right-click Start = Task Manager, Settings, Run, Power… (like Win+X)',
      'The Windows taskbar is hidden while a shell runs and comes back the moment the engine stops',
      'Runs in TCWall.exe as you (never as administrator); safety watchdogs hand the screen back if the shell page stops answering'] },
  { id: 'wallpapers', title: 'Wallpapers', comp: ['wallpaper'],
    what: 'Solid color, picture (per monitor), slideshow, live video wallpaper and live scenes (animated pages from the shell designs). A wallpaper you pick always wins over a preset scene.',
    changes: ['Desktop wallpaper through the Windows IDesktopWallpaper API (per monitor)', 'HKCU\\Control Panel\\Desktop and \\Colors (fit, solid color)', 'Live video / scene: drawn by TCWall.exe behind the desktop icons', 'Auto-pause when a full-screen app or game runs, on battery, or when covered', 'Library: %LOCALAPPDATA%\\TRANSCRYPTO\\library'] },
  { id: 'colors', title: 'Colors & accent', comp: ['colors'],
    what: 'Dark / light mode for Windows and apps, accent color, transparency, accent on Start/taskbar and title bars, automatic accent from the wallpaper.',
    changes: ['HKCU\\…\\Themes\\Personalize (mode, transparency, accent on Start)', 'HKCU\\…\\Explorer\\Accent (accent palette)', 'HKCU\\Software\\Microsoft\\Windows\\DWM (title bar accent)'] },
  { id: 'icons', title: 'Icons & thumbnails', comp: ['icons'],
    what: 'Icon packs for This PC, Network, user folder, Recycle Bin, folders and drives; per-drive and per-folder icons, folder covers made from a picture, shortcut arrow on/off, thumbnail options.',
    changes: ['HKCU\\…\\Explorer\\CLSID\\{…}\\DefaultIcon (desktop icons)', 'HKLM\\…\\Explorer\\Shell Icons (folders, drives, shortcut arrow) - admin', 'HKLM\\…\\Explorer\\DriveIcons (per drive) - admin', 'desktop.ini inside a folder (per-folder icon / cover)', 'The icon cache is refreshed for you'] },
  { id: 'cursors', title: 'Cursors', comp: ['cursors'],
    what: 'TRANSCRYPTO cursor packs, Windows schemes, imported packs (.inf / zip), any file per pointer role, size, and a neon click effect everywhere in Windows.',
    changes: ['HKCU\\Control Panel\\Cursors (each pointer + scheme name, size)', 'HKCU\\Software\\Microsoft\\Accessibility (cursor size)', 'Click effect: drawn by TCWall.exe, nothing changed in Windows'] },
  { id: 'sounds', title: 'Sounds', comp: ['sounds'],
    what: 'Sound schemes (Windows default, Silent, Minimal, packs) and a custom sound for any Windows event.',
    changes: ['HKCU\\AppEvents\\Schemes\\Apps\\…\\.Current (each event)', 'HKCU\\AppEvents\\Schemes (scheme name)'] },
  { id: 'startmenu', title: 'Start menu', comp: ['startmenu'],
    what: 'The real Windows 11 Start menu: layout, recommended section, folders next to Power, Bing/web search, layout presets.',
    changes: ['HKCU\\…\\Explorer\\Advanced (layout, recommendations)', 'HKLM PolicyManager / Policies (hide Recommended, folders) - admin', 'Explorer restarts when needed'] },
  { id: 'taskbar', title: 'Taskbar', comp: ['taskbar'],
    what: 'The real Windows taskbar: alignment, buttons (Search, Task view, Widgets, Copilot), auto-hide, combine buttons, and the custom shell / live background designs.',
    changes: ['HKCU\\…\\Explorer\\Advanced (TaskbarAl, TaskbarDa, …)', 'HKCU\\…\\Explorer\\StuckRects3 (auto-hide)', 'Custom shell: engine config in %LOCALAPPDATA%\\TRANSCRYPTO'] },
  { id: 'explorer', title: 'File Explorer', comp: ['explorer'],
    what: 'File Explorer behavior: file extensions, hidden files, open to This PC, compact view, classic right-click menu and more.',
    changes: ['HKCU\\…\\Explorer\\Advanced', 'HKCU\\Software\\Classes\\CLSID\\{86ca1aa0-…}\\InprocServer32 (classic context menu)'] },
  { id: 'lock', title: 'Lock & sign-in', comp: ['lock'],
    what: 'Lock screen picture, sign-in screen options, and TRANSCRYPTO Live Lock: a video lock screen shown before Windows asks for your PIN or password.',
    changes: ['HKLM\\…\\PersonalizationCSP (lock image) - admin', 'Picture copy in %ProgramData%\\TRANSCRYPTO', 'Live Lock: HKLM\\Software\\Policies\\…\\Personalization NoLockScreen - admin; drawn by TCWall.exe'] },
  { id: 'boot', title: 'Boot screen', comp: ['boot'],
    what: 'UEFI boot logo (bundled HackBGRT), firmware / Secure Boot / BitLocker checks, boot flags, custom startup sound.',
    changes: ['EFI system partition: HackBGRT boot loader + your logo - admin, Secure Boot must be off', 'Boot configuration (BCD) - a backup is made first', 'HKLM\\…\\BootAnimation (startup sound)'] },
  { id: 'fonts', title: 'Fonts', comp: ['fonts'],
    what: 'Replace the Windows UI font (Segoe UI) with any installed font, text size, install new fonts.',
    changes: ['HKLM\\…\\Windows NT\\CurrentVersion\\Fonts and \\FontSubstitutes - admin', 'Fonts copied to C:\\Windows\\Fonts', 'Sign out / restart to see everything change'] },
  { id: 'backup', title: 'Backup & restore', comp: [],
    what: 'Selective or full backups of everything TRANSCRYPTO manages, restore, "Restore EVERYTHING to Windows 11 default", System Restore points.',
    changes: ['Backups: %LOCALAPPDATA%\\TRANSCRYPTO\\backups (.tcbackup)', 'A restore only touches the values TRANSCRYPTO tracks (listed per section here)', 'System Restore point before big changes'] },
  { id: 'settings', title: 'Settings & engine', comp: [],
    what: 'App look, start with Windows, engine (TCWall.exe) control and memory, updates, log.',
    changes: ['HKCU\\…\\CurrentVersion\\Run "TRANSCRYPTO Engine" (start with Windows)', 'Settings and logs: %LOCALAPPDATA%\\TRANSCRYPTO'] },
];

const HELP_STEPS = [
  ['Pick a theme', 'Themes tab: click a preset (try SharmaJi Command Center, Orbital or Projector). Everything is applied together.'],
  ['Fine-tune', 'Change any single part in its own tab. Your change sits on top of the preset; the chip at the top shows the active preset + your customizations.'],
  ['Stay safe', 'Before big changes TRANSCRYPTO makes a System Restore point. Backup & Restore can put everything back, or reset to Windows 11 default.'],
];
const HELP_TIPS = [
  'Win key opens the custom Start menu; Esc closes it.',
  'Right-click the Start button for Task Manager, Settings, Run, Terminal and Power.',
  'Drag taskbar icons to reorder them; the arrow in the tray shows hidden icons.',
  'Live wallpapers pause on their own during full-screen apps and games.',
  'F1 opens this help, Ctrl+R reloads the window.',
];
const HELP_FIX = [
  ['The taskbar is gone', 'Taskbar tab → Custom taskbar → Design: Windows taskbar (off). Or Settings → Engine → Stop. The Windows taskbar comes back at once.'],
  ['Something looks wrong after a change', 'Use the reset button of that section, or Backup & Restore → restore a backup.'],
  ['A tab says it needs administrator', 'Close the window and start TRANSCRYPTO from its Start menu shortcut.'],
  ['Still stuck', 'Settings → Log shows what happened. Backup & Restore → Restore EVERYTHING to Windows 11 default always works.'],
];

let kbCache = null;
const knowledge = async () => kbCache || (kbCache = await api('app.knowledge', {}, { quiet: true }));

function helpModal() {
  const body = h('div', { class: 'help' },
    h('h3', null, 'Getting started'),
    h('div', { class: 'list' }, HELP_STEPS.map(([t, d], i) => h('div', { class: 'li' }, h('span', { class: 'tag neon' }, String(i + 1)), h('span', { class: 'grow' }, h('b', null, t), ' — ', d)))),
    h('h3', null, 'Tips'),
    h('ul', null, HELP_TIPS.map((t) => h('li', null, t))),
    h('h3', null, 'Troubleshooting'),
    h('div', { class: 'list' }, HELP_FIX.map(([t, d]) => h('div', { class: 'li col' }, h('b', null, t), h('span', { class: 'muted' }, d)))));
  return modal({ title: 'Help & guide', body, wide: true, actions: [{ label: 'Features & knowledge base', value: 'kb' }, { label: 'Close', value: null, cls: 'primary' }] })
    .then((r) => { if (r === 'kb') featuresModal(); });
}

async function featuresModal() {
  let k = null; try { k = await knowledge(); } catch (e) { /* show the texts anyway */ }
  const q = h('input', { type: 'search', placeholder: 'Search: icon, lock, accent, HKLM, taskbar…', style: { width: '100%', margin: '4px 0 12px' } });
  const list = h('div', { class: 'kb' });
  const sections = KB.map((s) => {
    const tab = s.tab ?? s.id; const tw = k && tab ? k.tweaks.filter((t) => t.tab === tab) : [];
    const reg = k ? [].concat(...s.comp.map((c) => k.tracked[c] || [])) : [];
    const el = h('details', { class: 'kb-sec glass' },
      h('summary', null, h('b', null, s.title), h('span', { class: 'muted small' }, ` ${tw.length ? tw.length + ' options' : ''}`)),
      h('p', null, s.what),
      h('div', { class: 'small muted' }, 'Changes in Windows:'),
      h('ul', null, s.changes.map((c) => h('li', null, c))),
      tw.length ? h('div', null, h('div', { class: 'small muted' }, 'Every option:'),
        h('div', { class: 'kb-rows' }, tw.map((t) => h('div', { class: 'kb-row' },
          h('span', null, t.label, t.admin ? h('span', { class: 'tag warn' }, 'admin') : null, t.restart ? h('span', { class: 'tag' }, 'restarts ' + t.restart) : null),
          h('code', null, t.where || '(several values)'))))) : null,
      reg.length ? h('div', null, h('div', { class: 'small muted' }, 'Registry values tracked for backup / reset:'),
        h('div', { class: 'kb-rows' }, reg.map((r) => h('code', { class: 'kb-reg' }, r)))) : null,
      h('div', { class: 'row', style: { marginTop: '8px' } }, TC.views[s.view || s.id] ? btn('Open ' + s.title.split(':')[0], () => { $$('.modal-back').forEach((b) => b.remove()); go(s.view || s.id); }, 'sm') : null));
    el.dataset.text = (el.textContent || '').toLowerCase();
    return el;
  });
  list.append(...sections);
  q.addEventListener('input', () => {
    const v = q.value.trim().toLowerCase();
    sections.forEach((s) => { const hit = !v || s.dataset.text.includes(v); s.style.display = hit ? '' : 'none'; s.open = !!v && hit; });
  });
  setTimeout(() => q.focus(), 50);
  return modal({ title: 'Features & knowledge base', wide: true,
    body: h('div', null, h('p', { class: 'muted' }, 'TRANSCRYPTO Themer is an all-in-one Windows 11 themer with 3 custom shells. Every section below says what it changes in Windows, down to the registry value.'), q, list),
    actions: [{ label: 'Close', value: null, cls: 'primary' }] });
}

async function whatsNewModal() {
  let k = null; try { k = await knowledge(); } catch (e) { toast('Could not read the release notes', 'err'); return; }
  return modal({ title: `What's new in v${k.version}`, wide: true,
    body: h('div', null, h('p', { class: 'muted' }, (k.date ? k.date + ' · ' : '') + (k.notes || '')), changeList(k.changes)),
    actions: [{ label: 'Check for updates', value: 'up' }, { label: 'Close', value: null, cls: 'primary' }] })
    .then((r) => { if (r === 'up') updatesModal(); });
}

async function updatesModal() {
  let r = null;
  try { r = await api('update.check', {}, { busy: 'Checking for updates…', quiet: true }); } catch (e) { r = { error: e.message }; }
  const body = h('div', null,
    r.error ? h('div', { class: 'note warn' }, 'Could not check for updates right now. You can still install a signed update file from Settings → Updates.', h('div', { class: 'small muted' }, r.error))
      : !r.configured ? h('div', { class: 'note' }, `You have v${r.current}. Online updates are not set up in this copy; install a signed update file from Settings → Updates.`)
      : r.available ? h('div', null, h('div', { class: 'note neon' }, h('b', null, `Update available: v${r.latest}`), ` (you have v${r.current})`), h('p', { class: 'muted' }, r.notes || ''), changeList(r.changes))
      : h('div', { class: 'note' }, `You are up to date (v${r.current}).`),
    h('p', { class: 'muted small' }, 'Updates are only installed when they carry TRANSCRYPTO\'s digital signature.'));
  const go2 = await modal({ title: 'Updates', body, actions: [{ label: 'Open Settings → Updates', value: 'set' }, { label: 'Close', value: null, cls: 'primary' }] });
  if (go2 === 'set') go('settings');
}

async function aboutModal() {
  let v = ''; try { v = (await knowledge()).version; } catch (e) { /* offline backend */ }
  const logo = $('.brand .logo').cloneNode(true);
  const body = h('div', { class: 'about' },
    h('div', { class: 'about-head' }, logo, h('div', null, h('div', { class: 'about-name' }, 'TRANSCRYPTO Themer'), h('div', { class: 'muted' }, `Version ${v}`))),
    h('p', null, 'A fully featured all-in-one Windows 11 themer with 3 crazy custom shells — SharmaJi Command Center, Orbital and Projector — plus live wallpapers, icons, cursors, sounds, lock and boot screens, fonts, Start menu, taskbar and File Explorer tweaks, with backups for everything.'),
    h('p', { class: 'about-love' }, 'Made with ', h('span', { class: 'heart' }, '\u2764'), ' by SharmaJi'),
    h('div', { class: 'kv small' },
      h('span', null, 'Publisher'), h('span', null, 'TRANSCRYPTO'),
      h('span', null, 'Updates'), h('span', null, 'Signed (RSA-3072); unsigned packages are refused'),
      h('span', null, 'Your data'), h('span', null, '%LOCALAPPDATA%\\TRANSCRYPTO'),
      h('span', null, 'License'), h('span', null, 'Free to use · no redistribution · no commercial use except by SharmaJi (TRANSCRYPTO)'),
      h('span', null, 'Boot logo'), h('span', null, 'HackBGRT by Lauri Kenttä (MIT license)')),
    h('p', { class: 'muted small' }, `© ${new Date().getFullYear()} SharmaJi (TRANSCRYPTO). All rights reserved.`));
  return modal({ title: 'About', body, actions: [{ label: 'License', value: 'lic' }, { label: "What's new", value: 'new' }, { label: 'Close', value: null, cls: 'primary' }] })
    .then((r) => { if (r === 'new') whatsNewModal(); if (r === 'lic') licenseModal(); });
}

async function licenseModal() {
  let k = null; try { k = await knowledge(); } catch (e) { /* offline backend */ }
  return modal({ title: 'License', wide: true, body: h('pre', { class: 'license' }, (k && k.license) || 'See LICENSE.txt in the app folder.'), actions: [{ label: 'Close', value: null, cls: 'primary' }] });
}

/* ---------- the menu */
const MENU = [
  ['Help & guide', 'F1', helpModal],
  ['Features & knowledge base', '', featuresModal],
  ["What's new", '', whatsNewModal],
  ['Check for updates', '', updatesModal],
  null,
  ['About TRANSCRYPTO Themer', '', aboutModal],
  ['License', '', licenseModal],
];
function openTopMenu(anchor) {
  closeTopMenu();
  const r = anchor.getBoundingClientRect();
  const m = h('div', { class: 'topmenu glass', id: 'topMenu', role: 'menu', style: { top: (r.bottom + 8) + 'px', right: (innerWidth - r.right) + 'px' } },
    MENU.map((it) => it ? h('button', { class: 'tm-item', role: 'menuitem', onclick: () => { closeTopMenu(); it[2](); } }, h('span', null, it[0]), it[1] ? h('kbd', null, it[1]) : null) : h('div', { class: 'tm-sep' })),
    h('div', { class: 'tm-foot' }, 'Made with ', h('span', { class: 'heart' }, '\u2764'), ' by SharmaJi'));
  document.body.append(m);
  m.querySelector('.tm-item').focus();
}
function closeTopMenu() { const m = $('#topMenu'); if (m) m.remove(); }
document.addEventListener('mousedown', (e) => { if (!e.target.closest('#topMenu, #btnMenu')) closeTopMenu(); });
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') closeTopMenu();
  if (e.key === 'F1') { e.preventDefault(); closeTopMenu(); helpModal(); }
  const m = $('#topMenu'); if (!m || !['ArrowDown', 'ArrowUp'].includes(e.key)) return;
  e.preventDefault(); const items = $$('.tm-item', m); const i = items.indexOf(document.activeElement);
  items[(i + (e.key === 'ArrowDown' ? 1 : items.length - 1)) % items.length].focus();
});
window.addEventListener('blur', closeTopMenu);
window.addEventListener('resize', closeTopMenu);
