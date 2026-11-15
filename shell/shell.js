/* TRANSCRYPTO Shell - shared runtime for the taskbar + Start menu designs.
   Talks to the engine's local server (token in the URL), keeps real data flowing (apps, tray, system),
   tells the engine which parts of the screen the page occupies (window region + reserved strip),
   and swaps the glass backdrop between the wallpaper (bar) and a fresh screenshot (open menu). */
'use strict';

const Q = new URLSearchParams(location.search);
const Shell = {
  token: Q.get('t') || '', palette: Q.get('pal') || '', dpr: window.devicePixelRatio || 1,
  state: null, start: null, open: false, busy: false, listeners: [], onStart: null, onEscape: null, onBlur: null,
};
const $ = (s, r = document) => r.querySelector(s), $$ = (s, r = document) => [...r.querySelectorAll(s)];

Shell.url = (path, params = {}) => `/api/${path}?` + new URLSearchParams(Object.assign({ t: Shell.token }, params)).toString();
// the signed-in Windows user's initials for avatars and badges ("Jane Doe" / "jane.doe" -> "JD", "alex" -> "AL")
Shell.initials = (u) => { const w = String(u || '').split(/[\s._-]+/).filter(Boolean); return ((w.length > 1 ? w[0][0] + w[1][0] : (w[0] || '').slice(0, 2)) || 'TC').toUpperCase(); };
Shell.api = (path, params) => fetch(Shell.url(path, params)).then((r) => r.json()).catch(() => null);
Shell.act = (a, params = {}) => {
  if (a === 'menu') { if (params.open == 1) Shell.setIdle(false); else Shell.goIdle(); }   // the open menu / panels animate; the closed bar rests
  return Shell.api('do', Object.assign({ a }, params)).then((r) => r && r.result);
};

/* ---------- idle: the bar's decorative loops (swirls, spinning rings, glows) only run while the pointer is on it or a menu is open.
   Left running all day they kept the GPU process busy and growing (+40 MB a minute) until the PC froze. */
document.head.insertAdjacentHTML('beforeend', '<style>html.tcIdle #stage *, html.tcIdle #stage *::before, html.tcIdle #stage *::after { animation-play-state: paused !important; }</style>');
let idleT = null;
Shell.setIdle = (on) => { clearTimeout(idleT); document.documentElement.classList.toggle('tcIdle', !!on); };
Shell.goIdle = (ms = 1200) => { clearTimeout(idleT); idleT = setTimeout(() => { if (Shell.open || Shell.panel || Shell.busy) Shell.goIdle(ms); else Shell.setIdle(true); }, ms); };
document.documentElement.addEventListener('mouseenter', () => Shell.setIdle(false));
document.documentElement.addEventListener('mouseleave', () => Shell.goIdle());
Shell.goIdle(2500);
// icons load through "localhost" instead of "127.0.0.1": the browser gives each host its own 6 connections, so a list of
// 128 icons can never queue up in front of the calls that open and close the menu (that once left the menu stuck)
Shell.iconHost = location.hostname === '127.0.0.1' ? `${location.protocol}//localhost:${location.port}` : '';
Shell.icon = (key, size = 64) => Shell.iconHost + Shell.url('icon', { k: key, s: size });

/* ---------- the 1280x720 design stage sits bottom-centre on the real screen */
Shell.layoutStage = (stage) => {
  const fit = () => { const k = Math.min(1, innerWidth / 1280); stage.style.transformOrigin = '50% 100%';
    stage.style.left = (innerWidth - 1280) / 2 + 'px'; stage.style.top = (innerHeight - 720) + 'px'; stage.style.transform = k < 1 ? `scale(${k})` : ''; Shell.K = k; };
  addEventListener('resize', fit); fit();
};

/* ---------- glass backdrop: wallpaper everywhere, plus a screenshot of the screen above the bar while the menu is open */
Shell.bg = Q.get('bg') && Q.get('bg') !== 'none' ? Q.get('bg') : null;
Shell.backdrop = () => {
  if ($('#tcWall')) return;
  // with a live background on the desktop, the same scene runs behind our glass; otherwise the wallpaper picture
  const wall = Shell.bg
    ? `<iframe id="tcWall" src="bg-${Shell.bg}.html?pal=${encodeURIComponent(Q.get('bgpal') || '')}" style="position:fixed;inset:0;width:100vw;height:100vh;border:0;z-index:-2;pointer-events:none"></iframe>`
    : `<img id="tcWall" src="${Shell.url('wallpaper', { v: Date.now() })}" style="position:fixed;inset:0;width:100vw;height:100vh;object-fit:cover;z-index:-2;pointer-events:none" alt="">`;
  document.body.insertAdjacentHTML('afterbegin', wall +
    '<img id="tcShot" style="position:fixed;inset:0;width:100vw;height:100vh;object-fit:fill;z-index:-1;pointer-events:none;opacity:0" alt="">');
  // the copy behind our glass only needs a still frame: the real scene keeps moving on the desktop, this one stops after it has drawn
  const f = $('#tcWall');
  if (f.tagName === 'IFRAME') f.addEventListener('load', () => setTimeout(() => f.contentWindow.postMessage({ tcBg: 'pause' }, '*'), 1200));
};
Shell.useScreenshot = (on) => new Promise((res) => {
  const im = $('#tcShot');
  if (!on) { im.style.opacity = 0; return res(); }
  im.onload = () => { im.style.clipPath = `inset(0 0 ${Shell.reservePx || 0}px 0)`; im.style.opacity = 1; res(); };
  im.onerror = () => res();
  im.src = Shell.url('backdrop', { v: Date.now() });
});
setInterval(() => { if (!Shell.open && !Shell.bg && $('#tcWall')) $('#tcWall').src = Shell.url('wallpaper', { v: Math.floor(Date.now() / 60000) }); }, 60000);

/* ---------- tell the engine what we occupy */
let lastRegion = '';
Shell.region = (els, extra = []) => {
  const d = Shell.dpr, parts = [];
  if (Shell.hidden && !Shell.open) { els = []; extra = []; }   // auto-hidden: nothing on screen, clicks go to the windows below
  els = els.concat($$('#tcBanners .tcBan'), $$('.tcCtx'));                   // notification banners are clickable wherever they are
  if (Shell.dragBand) extra = extra.concat([Shell.dragBand]);    // while an app is dragged: the whole strip above the bar, so its ghost is never clipped
  for (const el of els.flat()) { if (!el || !el.isConnected) continue; const r = el.getBoundingClientRect(); if (r.width < 1 || r.height < 1) continue;
    const rad = parseFloat(el.dataset.radius || getComputedStyle(el).borderTopLeftRadius) || 0;
    parts.push([r.left * d, r.top * d, r.width * d, r.height * d, Math.min(rad, r.height / 2) * d].map((n) => n.toFixed(1)).join(',')); }
  for (const r of extra) parts.push(r.map((n) => (n * d).toFixed(1)).join(','));
  const s = parts.join(';'); if (s === lastRegion) return; lastRegion = s;
  Shell.act('region', { r: s });
};
Shell.metrics = (topOfBarCss) => {
  Shell.reservePx = Math.max(0, innerHeight - (topOfBarCss - (Shell.hidden ? Shell.hideBy : 0)));
  Shell.act('metrics', { vh: Math.round(innerHeight * Shell.dpr), vw: Math.round(innerWidth * Shell.dpr), reserve: Math.round(Shell.reservePx * Shell.dpr) });
};

/* ---------- live data */
Shell.poll = async () => {
  const s = await Shell.api('state');
  if (s) { s.apps = Shell.sortApps(s.apps || []); Shell.state = s; Shell.listeners.forEach((f) => { try { f(s); } catch (e) { console.error(e); } }); }
  setTimeout(Shell.poll, Shell.open ? 900 : 1000);
};
Shell.on = (f) => Shell.listeners.push(f);
Shell.loadStart = async () => { Shell.start = await Shell.api('start'); return Shell.start; };
Shell.appsSig = (apps) => apps.map((a) => a.id + (a.running ? '*' : '') + (a.active ? '!' : '')).join('|');
/* ---------- taskbar order: drag an app along the bar to move it; the order is kept (localStorage of the shell profile) */
Shell.order = (() => { try { return JSON.parse(localStorage.getItem('tcOrder') || '[]'); } catch (e) { return []; } })();
Shell.sortApps = (list) => {
  const rank = new Map(Shell.order.map((id, i) => [id, i]));
  return list.map((a, i) => [a, rank.has(a.id) ? rank.get(a.id) : 100000 + i]).sort((x, y) => x[1] - y[1]).map((x) => x[0]);
};
/* the design tells us which elements are its app buttons, in screen order; each carries ._app */
Shell.enableAppDrag = (getEls) => {
  let drag = null, justDragged = false, mark = null;
  const els = () => getEls().filter((el) => el && el.isConnected && el._app);
  document.addEventListener('pointerdown', (e) => {
    if (e.button !== 0 || Shell.open || Shell.busy) return;
    const el = els().find((x) => x.contains(e.target)); if (!el) return;
    drag = { el, x0: e.clientX, y0: e.clientY, on: false, id: e.pointerId };
  }, true);
  document.addEventListener('pointermove', (e) => {
    if (!drag || e.pointerId !== drag.id) return;
    const dx = e.clientX - drag.x0;
    if (!drag.on) {
      if (Math.abs(dx) < 7 && Math.abs(e.clientY - drag.y0) < 7) return;
      drag.on = true; Shell.tip.hide();
      try { drag.el.setPointerCapture(e.pointerId); } catch (err) { /* fine without */ }
      drag.el.classList.add('tcDragSrc');
      if (!mark) { mark = document.createElement('div'); mark.className = 'tcDropMark'; document.body.append(mark); }
      // a ghost of the app (its icon + name) rides on the cursor
      const r = drag.el.getBoundingClientRect(), img = drag.el.querySelector('img'), a = drag.el._app;
      drag.ghost = document.createElement('div'); drag.ghost.className = 'tcGhost';
      drag.ghost.innerHTML = `<span>${Shell.esc(a.title || a.name)}</span>${img ? `<img src="${img.src}" alt="">` : ''}`;
      document.body.append(drag.ghost); drag.gy = r.top - 8;   // lifted just above the bar
      Shell.dragBand = [0, Math.max(0, r.top - 110), innerWidth, innerHeight - Math.max(0, r.top - 110)];
      if (Shell.refreshRegion) Shell.refreshRegion();
    }
    drag.ghost.style.left = e.clientX + 'px'; drag.ghost.style.top = drag.gy + 'px';
    // where it would land: before the first other app whose centre is right of the pointer
    const others = els().filter((x) => x !== drag.el), k = Shell.K || 1;
    let at = others.length;
    for (let i = 0; i < others.length; i++) { const r = others[i].getBoundingClientRect(); if (e.clientX < r.left + r.width / 2) { at = i; break; } }
    drag.at = at;
    const ref = others[Math.min(at, others.length - 1)]; if (!ref) return;
    const r = ref.getBoundingClientRect(), x = at < others.length ? r.left - 4 : r.right + 4;
    Object.assign(mark.style, { display: 'block', left: Math.round(x - 1) + 'px', top: Math.round(r.top - 2) + 'px', height: Math.round(r.height + 4) + 'px' });
  }, true);
  const end = (e) => {
    if (!drag || (e && e.pointerId !== drag.id)) return;
    const d = drag; drag = null; if (mark) mark.style.display = 'none';
    if (!d.on) return;
    d.el.classList.remove('tcDragSrc'); if (d.ghost) d.ghost.remove();
    Shell.dragBand = null; if (Shell.refreshRegion) setTimeout(Shell.refreshRegion, 60);
    justDragged = true; setTimeout(() => { justDragged = false; }, 250);
    const list = els().map((x) => x._app.id), from = list.indexOf(d.el._app.id); if (from < 0 || d.at == null) return;
    list.splice(from, 1); list.splice(d.at, 0, d.el._app.id);
    Shell.order = list; try { localStorage.setItem('tcOrder', JSON.stringify(list)); } catch (err) { /* private profile */ }
    // re-render after the click that follows pointerup, so that click still lands on (and is swallowed for) the dragged button
    setTimeout(() => { if (Shell.state) { Shell.state.apps = Shell.sortApps(Shell.state.apps); Shell.listeners.forEach((f) => { try { f(Shell.state); } catch (err) { console.error(err); } }); } }, 30);
  };
  document.addEventListener('pointerup', end, true); document.addEventListener('pointercancel', end, true);
  // the icons are <img>: without this Chrome starts its own image drag, which cancels ours (pointercancel) on the first move
  document.addEventListener('dragstart', (e) => { if (els().some((x) => x.contains(e.target))) e.preventDefault(); }, true);
  // the click that ends a drag must not launch / minimise the app
  document.addEventListener('click', (e) => { if (justDragged) { e.stopPropagation(); e.preventDefault(); } }, true);
};


/* ---------- auto-hide: the bar only slides away while a window covers its strip (maximised / dragged low; the engine says "cover:1").
   On the bare desktop it stays. Hidden, it comes back when the cursor touches the bottom edge. */
Shell.autohide = Q.get('ah') === '1'; Shell.hidden = false; Shell.hideBy = 0; Shell.covered = false; Shell.hover = false;
let hideTimer = null;
Shell.setupAutohide = (stage) => {
  if (!Shell.autohide) return;
  Shell.stage = stage; stage.style.transition = 'translate .26s cubic-bezier(.2,.8,.2,1)';
  document.documentElement.addEventListener('mouseleave', () => { Shell.hover = false; Shell.scheduleHide(650); });
  document.documentElement.addEventListener('mouseenter', () => { Shell.hover = true; clearTimeout(hideTimer); });
  Shell.act('cover').then((c) => { Shell.covered = !!c; Shell.scheduleHide(1500); });
};
Shell.scheduleHide = (ms) => { if (!Shell.autohide || !Shell.covered) return; clearTimeout(hideTimer); hideTimer = setTimeout(() => Shell.hideBar(), ms); };
Shell.hideBar = () => {
  if (!Shell.autohide || !Shell.covered || Shell.open || Shell.panel || Shell.busy || Shell.hidden) return;
  Shell.tip.hide(); Shell.hideBy = (Shell.reservePx || 60) + 14; Shell.hidden = true;
  Shell.stage.style.translate = `0 ${Shell.hideBy}px`;
  setTimeout(() => { if (Shell.hidden) { lastRegion = '?'; Shell.region([]); Shell.act('bar', { shown: 0 }); } }, 280);
};
Shell.showBar = () => new Promise((ok) => {
  clearTimeout(hideTimer);
  if (!Shell.hidden) return ok();
  Shell.hidden = false; Shell.stage.style.translate = '0 0';
  if (Shell.refreshRegion) Shell.refreshRegion();
  Shell.scheduleHide(2500);   // if the cursor never reaches the bar, slide away again
  setTimeout(ok, 260);
});

/* events from the engine: the Win key, Esc while the menu is open */
(async function events() {
  let since = -1;   // -1: just learn the current sequence number, without waiting
  for (;;) {
    const r = await Shell.api('events', { since });
    if (!r) { await new Promise((ok) => setTimeout(ok, 2000)); continue; }
    if (since < 0) { since = r.seq; continue; }   // ignore anything from before this page loaded
    since = r.seq;
    for (const e of r.events) {
      if (e === 'reveal') Shell.showBar();
      if (e === 'cover:1') { Shell.covered = true; if (!Shell.hover) Shell.scheduleHide(350); }
      if (e === 'cover:0') { Shell.covered = false; Shell.showBar(); }
      if (e === 'start' && Shell.onStart) { if (Shell.panel) await Shell.closePanel(true); await Shell.showBar(); Shell.onStart(); }
      if (e === 'escape') { if (Shell.panel) Shell.closePanel(); else if (Shell.onEscape) Shell.onEscape(); }
      if (e === 'quick' || e === 'notify') Shell.togglePanel(e);
      if (e === 'notifs') Shell.loadNotifs();
      if (e.startsWith('notif:')) { const [, id, quiet] = e.split(':'); Shell.loadNotifs(quiet ? null : id); }
    }
  }
})();
addEventListener('blur', () => { if (Shell.panel && !Shell.panelBusy) Shell.closePanel(); else if (Shell.open && !Shell.busy && Shell.onBlur) Shell.onBlur(); });
addEventListener('keydown', (e) => { if (e.key !== 'Escape') return; if (Shell.panel) Shell.closePanel(); else if (Shell.open && !Shell.busy && Shell.onEscape) Shell.onEscape(); });

/* ---------- actions */
/* app clicks: with the menu open, it closes first and the app is always brought forward (never minimised) */
let appAnchor = null;   // the bar button that was pressed last (designs put the app on it as ._app)
document.addEventListener('pointerdown', (e) => { for (let el = e.target; el && el !== document.body; el = el.parentElement) if (el._app) { appAnchor = el; break; } }, true);
Shell.launchApp = async (a, newInstance) => {
  const fromMenu = Shell.open;
  // several windows of one app (combined like the Windows taskbar): pick one from a list, like Windows' window previews
  if (!fromMenu && !newInstance && a.running && a.windows && a.windows.length > 1) return Shell.winList(a, appAnchor && appAnchor._app === a ? appAnchor : null);
  if (fromMenu && Shell.close) Shell.close();   // the menu animates away while the app comes forward
  if (a.running && !newInstance && a.windows && a.windows.length) {
    // repeated clicks cycle through the app's windows
    a._i = ((a._i ?? -1) + 1) % a.windows.length;
    return Shell.act('activate', { h: a.windows[a.active && !fromMenu ? a._i : 0], force: fromMenu ? 1 : 0 });
  }
  if (a.path) return Shell.act('launch', { p: a.path });
};
/* ---------- files dragged onto the bar, like the Windows taskbar: hovering an app brings its window forward (so the file can be
   dropped into it), dropping on an app opens the files with that app. The page never navigates to a dropped file. */
{
  let over = null, overT = null;
  const appEl = (t) => { for (let el = t; el && el !== document.body; el = el.parentElement) if (el._app) return el; return null; };
  const leave = () => { clearTimeout(overT); if (over) over.classList.remove('tcDropOn'); over = null; };
  document.addEventListener('dragover', (e) => {
    e.preventDefault();   // without this Chrome would open the file in place of the bar
    const el = appEl(e.target);
    if (e.dataTransfer) e.dataTransfer.dropEffect = el ? 'copy' : 'none';
    if (el === over) return;
    leave(); if (!el) return;
    over = el; el.classList.add('tcDropOn');
    const a = el._app;
    if (a.running && a.windows && a.windows.length)
      overT = setTimeout(() => { if (over === el) Shell.act('activate', { h: a.windows[0], force: 1, show: 1 }); }, 650);
  });
  document.addEventListener('dragleave', (e) => { if (!e.relatedTarget || !document.documentElement.contains(e.relatedTarget)) leave(); });
  document.addEventListener('drop', (e) => {
    e.preventDefault();
    const el = appEl(e.target); leave();
    const list = (e.dataTransfer && e.dataTransfer.files) || [], files = [...list].map((f) => ({ n: f.name, s: f.size }));
    if (!el || !files.length) return;
    const a = el._app, app = a.path || a.exe || '';
    // inside the engine (WebView2) the dropped files themselves go to the engine, with their real paths; names + sizes as backup
    const wv = window.chrome && window.chrome.webview;
    if (wv && wv.postMessageWithAdditionalObjects) {
      try { wv.postMessageWithAdditionalObjects({ type: 'drop', app, f: files.slice(0, 20) }, list); return; } catch (err) { /* older WebView2: the names below */ }
    }
    Shell.act('dropfiles', { app, f: JSON.stringify(files.slice(0, 20)) });
  });
}

/* launching from the menu: close it first so the app isn't hidden behind the full-screen menu */
Shell.open$ = async (path) => { if (Shell.open && Shell.close) Shell.close(); return Shell.act('launch', { p: path }); };
/* power buttons arm on the first click and fire on the second (within 4 s) */
Shell.power = async (n, el) => {
  const r = await Shell.act('sys', { n });
  if (r === 'armed' && el) { el.classList.add('armed'); el.dataset.tip = 'click again'; setTimeout(() => { el.classList.remove('armed'); delete el.dataset.tip; }, 4000); }
  return r;
};
/* the tray and the clock open our own panels; everything else goes to the engine */
let sysAnchor = null;
document.addEventListener('click', (e) => { const el = e.target.closest && e.target.closest('[data-sys]'); if (el) sysAnchor = el; }, true);
Shell.sys = (n) => n === 'quick' || n === 'notify' ? Shell.togglePanel(n, sysAnchor) : n === 'tray' ? Shell.trayIcons(sysAnchor) : Shell.act('sys', { n });
/* show hidden icons in the design's own panel: the engine reads them from Windows' overflow flyout (opened invisibly) and,
   on a click here, replays the click on the real icon under the cursor. If that fails, Windows' own flyout opens above the chevron. */
Shell.trayIcons = async (el) => {
  if (Shell.panel === 'tray') return Shell.closePanel();
  if (Shell.panel) await Shell.closePanel(); if (Shell.open && Shell.close) await Shell.close();
  el = el && el.isConnected ? el : $('[data-sys="tray"]'); if (!el) return;
  const r = await Shell.api('tray', { op: 'list' });
  if (r && Array.isArray(r.result)) return Shell.togglePanel('tray', el, () => Shell.renderTray(r.result));
  return Shell.trayFlyout(el);
};
Shell.trayFlyout = async (el) => {
  const r = el.getBoundingClientRect(); let top = r.top;
  for (let p = el.parentElement; p && p !== document.body; p = p.parentElement) { const q = p.getBoundingClientRect(); if (q.height > 140) break; if (q.height >= 1) top = Math.min(top, q.top); }
  return Shell.act('sys', { n: 'tray', x: Math.round((r.left + r.width / 2) * Shell.dpr), y: Math.round(top * Shell.dpr) });
};
Shell.volume = (d) => Shell.act('volume', { d });             // a step up / down (mouse wheel), or 'mute' to toggle
Shell.setVolume = (v) => Shell.act('volume', { set: v });     // an exact level 0..100 (the slider)

/* ---------- formatting helpers */
Shell.time = () => { const d = new Date(); return { hh: String(d.getHours()).padStart(2, '0'), mm: String(d.getMinutes()).padStart(2, '0'), d }; };
Shell.netIcon = (s) => s.net === 'wifi' ? 'ti-wifi' : s.net === 'ethernet' ? 'ti-network' : 'ti-wifi-off';
Shell.volIcon = (s) => s.mute || s.volume === 0 ? 'ti-volume-off' : s.volume < 0 ? 'ti-volume' : s.volume < 34 ? 'ti-volume-3' : s.volume < 67 ? 'ti-volume-2' : 'ti-volume';
Shell.batIcon = (s) => s.battery < 0 ? null : s.charging ? 'ti-battery-charging' : s.battery > 80 ? 'ti-battery-4' : s.battery > 55 ? 'ti-battery-3' : s.battery > 30 ? 'ti-battery-2' : s.battery > 10 ? 'ti-battery-1' : 'ti-battery';
Shell.rate = (b) => b > 1048576 ? (b / 1048576).toFixed(1) + ' MB/s' : b > 1024 ? Math.round(b / 1024) + ' KB/s' : Math.round(b) + ' B/s';
Shell.ago = (iso) => { const m = (Date.now() - new Date(iso)) / 60000; return m < 60 ? Math.max(1, Math.round(m)) + 'm' : m < 1440 ? Math.round(m / 60) + 'h' : Math.round(m / 1440) + 'd'; };
Shell.esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
Shell.projects = () => { const st = Shell.start || {}; return (st.projects && st.projects.length ? st.projects : (st.recentFolders || [])).slice(0, 5); };

/* search over Start menu shortcuts: prefix matches first, then substring */
Shell.search = (q) => {
  const apps = (Shell.start && Shell.start.apps) || [], s = q.trim().toLowerCase(); if (!s) return [];
  const a = apps.filter((x) => x.name.toLowerCase().startsWith(s)), b = apps.filter((x) => !x.name.toLowerCase().startsWith(s) && x.name.toLowerCase().includes(s));
  return a.concat(b).slice(0, 12);
};

/* small tooltip that also joins the window region while visible */
Shell.tip = { el: null, show(target, text) {
  if (!this.el) { this.el = document.createElement('div'); this.el.className = 'tcTip'; document.body.append(this.el); }
  this.el.textContent = text; this.el.style.display = 'block';
  const r = target.getBoundingClientRect(), w = this.el.offsetWidth;
  this.el.style.left = Math.round(r.left + r.width / 2 - w / 2) + 'px'; this.el.style.top = Math.round(r.top - this.el.offsetHeight - 10) + 'px';
  if (Shell.refreshRegion) Shell.refreshRegion();
}, hide() { if (this.el) { this.el.style.display = 'none'; if (Shell.refreshRegion) Shell.refreshRegion(); } } };

/* WebAudio shared by the designs */
let AC = null; Shell.ac = () => AC || (AC = new (window.AudioContext || window.webkitAudioContext)());

{ // block scope: the design pages declare their own helpers (wait, ...) at the top level
/* ---------- quick settings, notification centre + calendar, notification banners (frosted glass, skinned per design in panels.css) */
Shell.design = (location.pathname.split('/').pop() || '').replace(/\.html$/, '');
document.documentElement.dataset.tc = Shell.design;
document.head.insertAdjacentHTML('beforeend', '<link rel="stylesheet" href="panels.css">');
Shell.panel = null; Shell.panelBusy = false; Shell.notifs = [];

const PN = {};
const ensurePanels = () => {
  if (PN.scrim) return PN;
  document.body.insertAdjacentHTML('beforeend', '<div id="tcScrim"></div><div class="tcPanel" id="tcQuick"></div><div class="tcPanel" id="tcCenter"></div><div class="tcPanel" id="tcList"></div><div id="tcBanners"></div>');
  PN.scrim = $('#tcScrim'); PN.quick = $('#tcQuick'); PN.center = $('#tcCenter'); PN.list = $('#tcList'); PN.banners = $('#tcBanners');
  PN.scrim.addEventListener('mousedown', () => Shell.closePanel());
  return PN;
};
const pwait = (ms) => new Promise((ok) => setTimeout(ok, ms));
const ms$ = (page) => () => Shell.panelLaunch('ms-settings:' + page);

/* the panel opens from the bar item that was clicked, centred on it and kept on screen */
const placePanel = (p, anchor) => {
  let r = anchor && anchor.isConnected ? anchor.getBoundingClientRect() : { left: innerWidth - 60, width: 40, top: innerHeight - 60 };
  // sit above the whole bar, not just above the clicked icon: the top of the lowest bar-sized ancestor
  let top = r.top; for (let el = anchor && anchor.parentElement; el && el !== document.body; el = el.parentElement) {
    const q = el.getBoundingClientRect(); if (q.height > 140) break; if (q.height >= 1) top = Math.min(top, q.top); }
  r = { left: r.left, width: r.width, top };
  const w = p.offsetWidth || 370, x =Math.max(12, Math.min(innerWidth - w - 12, r.left + r.width / 2 - w / 2));
  p.style.left = x + 'px'; p.style.bottom = Math.round(innerHeight - r.top + 14) + 'px';
  p.style.maxHeight = Math.round(r.top - 28) + 'px';
  p.style.setProperty('--ox', Math.round(r.left + r.width / 2 - x) + 'px');
};

/* quick = quick settings, notify = notification centre; any other name is a list panel (tray icons, an app's windows) drawn by render() */
const panelEl = (n) => n === 'quick' ? PN.quick : n === 'notify' ? PN.center : PN.list;
Shell.togglePanel = async (n, anchor, render) => {
  if (Shell.panelBusy || Shell.busy) return;
  if (Shell.panel === n) return Shell.closePanel();
  Shell.panelBusy = true;
  try {
    const switching = !!Shell.panel;
    if (switching) await Shell.closePanel(true);
    if (Shell.open && Shell.close) await Shell.close();
    await Shell.showBar();
    if (!anchor || !anchor.isConnected || !anchor.getBoundingClientRect().width) anchor = $(`[data-sys="${n}"]`);
    ensurePanels();
    const p = panelEl(n);
    if (!switching) { await Shell.act('menu', { open: 1 }); await Shell.useScreenshot(true); }   // full-screen region, focus, fresh glass backdrop
    Shell.panel = n;
    if (n === 'quick') renderQuick(); else if (n === 'notify') renderCenter(); else if (render) render();
    p.classList.remove('out'); p.classList.add('on'); placePanel(p, anchor);
    PN.scrim.classList.add('on');
    if (n === 'notify') PN.banners.innerHTML = '';
  } finally { Shell.panelBusy = false; }
};

Shell.closePanel = async (keepOpen) => {
  if (!Shell.panel) return;
  const p = panelEl(Shell.panel);
  Shell.panel = null; clearInterval(PN.tick);
  p.classList.remove('on'); p.classList.add('out'); PN.scrim.classList.remove('on');
  await pwait(170); if (!p.classList.contains('on')) p.classList.remove('out');
  if (keepOpen) return;
  await Shell.act('menu', { open: 0 }); await Shell.useScreenshot(false);
  if (Shell.refreshRegion) Shell.refreshRegion();
  Shell.scheduleHide(1500);
};
/* settings pages and apps from a panel: the panel goes away first */
Shell.panelLaunch = async (path) => { await Shell.closePanel(); return Shell.act('launch', { p: path }); };

/* ---------- an app with several windows: one row per window (title), click = bring it forward, x = close it */
Shell.winList = (a, anchor) => Shell.togglePanel('wins', anchor, () => {
  const p = PN.list, wins = a.windows || [], titles = a.titles || [];
  p.innerHTML = `<div class="lHead"><img class="lIco" src="${Shell.icon(a.icon, 48)}" alt=""><b>${Shell.esc(a.name)}</b><small>${wins.length} windows</small></div>
    <div class="lRows">${wins.map((h, i) => `<div class="lRow" data-h="${Shell.esc(h)}"><img class="lIco" src="${Shell.icon(a.icon, 32)}" alt="">
      <span class="lT">${Shell.esc(titles[i] || a.name)}</span><button class="lX" title="Close window"><i class="ti ti-x"></i></button></div>`).join('')}</div>
    ${a.path ? '<button class="lNew"><i class="ti ti-plus"></i> New window</button>' : ''}`;
  p.onclick = async (e) => {
    const row = e.target.closest('.lRow'), x = e.target.closest('.lX');
    if (x && row) { Shell.act('close', { h: row.dataset.h }); row.remove(); if (!p.querySelector('.lRow')) Shell.closePanel(); return; }
    if (row) { await Shell.closePanel(); return Shell.act('activate', { h: row.dataset.h, force: 1 }); }
    if (e.target.closest('.lNew')) return Shell.panelLaunch(a.path);
  };
});

/* ---------- hidden tray icons: tiles in the design's glass; left click = the icon's click, right click = its own menu */
Shell.renderTray = (items) => {
  const p = PN.list;
  p.innerHTML = `<div class="lHead"><i class="ti ti-chevron-up lHi"></i><b>Hidden icons</b><small>${items.length || 'none'}</small></div>
    ${items.length ? `<div class="tGrid">${items.map((it) => `<button class="tTile" data-i="${it.i}" title="${Shell.esc(it.name)}">${it.icon
      ? `<img src="${Shell.icon(it.icon, 48)}" alt="">` : '<i class="ti ti-app-window"></i>'}<span>${Shell.esc(it.label)}</span></button>`).join('')}</div>`
      : '<div class="lEmpty">No hidden icons right now</div>'}
    <button class="lNew" data-k="settings"><i class="ti ti-settings"></i> Choose which icons show on the bar</button>`;
  const click = async (e, b) => {
    const t = e.target.closest('.tTile');
    if (!t) { if (b === 'left' && e.target.closest('[data-k="settings"]')) Shell.panelLaunch('ms-settings:taskbar'); return; }
    const it = items.find((x) => String(x.i) === t.dataset.i); if (!it) return;
    await Shell.closePanel();   // the bar gives the screen back, then the engine clicks the real icon where the cursor is
    Shell.api('tray', { op: 'click', i: it.i, n: it.name, b });
  };
  p.onclick = (e) => click(e, 'left');
  p.oncontextmenu = (e) => { e.preventDefault(); e.stopPropagation(); click(e, 'right'); };
};

/* ---------- quick settings */
let volDrag = false, volTimer = null;
const renderQuick = () => {
  const p = PN.quick, s = (Shell.state && Shell.state.sys) || {};
  const user = s.user || 'user';
  p.innerHTML = `
    <div class="qHead"><div class="qAv">${Shell.esc(user[0].toUpperCase())}</div>
      <div class="qWho"><b>${Shell.esc(user)}</b><small id="qMach">this PC</small></div>
      <div class="qBat" id="qBat"></div></div>
    <div class="qGrid">
      <button class="qTile wide" id="qNet"><span class="qI"><i class="ti ti-wifi"></i></span><span class="qT"><b>Network</b><small>checking...</small></span></button>
      <button class="qTile" data-ms="bluetooth"><span class="qI"><i class="ti ti-bluetooth"></i></span><span class="qT"><b>Bluetooth</b><small>devices</small></span></button>
      <button class="qTile" data-ms="quiethours"><span class="qI"><i class="ti ti-moon"></i></span><span class="qT"><b>Focus</b><small>do not disturb</small></span></button>
      <button class="qTile" data-ms="nightlight"><span class="qI"><i class="ti ti-sun-low"></i></span><span class="qT"><b>Night light</b><small>display</small></span></button>
      <button class="qTile" data-ms="display"><span class="qI"><i class="ti ti-device-desktop"></i></span><span class="qT"><b>Display</b><small>scale, HDR</small></span></button>
      <button class="qTile" data-ms="sound"><span class="qI"><i class="ti ti-headphones"></i></span><span class="qT"><b>Audio output</b><small>devices</small></span></button>
      <button class="qTile" data-ms="powersleep"><span class="qI"><i class="ti ti-bolt"></i></span><span class="qT"><b>Power</b><small>plan, sleep</small></span></button>
    </div>
    <div class="qCard"><div class="qVolHd"><div class="h">Volume</div><button id="qOut" class="qOut" title="Output device"><i class="ti ti-device-speaker"></i><span>…</span><i class="ti ti-chevron-down qOutC"></i></button></div>
      <div class="qRow"><button id="qMute"><i class="ti ti-volume"></i></button><input class="qSlide" id="qVol" type="range" min="0" max="100" step="1"><span id="qVolN">--</span></div>
      <div class="qOuts" id="qOuts"></div></div>
    <div class="qCard qMedia"><span class="qI" style="font-size:20px;width:28px"><i class="ti ti-music"></i></span><span class="qT"><b>Media</b><small>whatever is playing</small></span>
      <button data-k="prev"><i class="ti ti-player-skip-back"></i></button><button class="big" data-k="play"><i class="ti ti-player-play"></i></button><button data-k="next"><i class="ti ti-player-skip-forward"></i></button></div>
    <div class="qCard"><div class="qStats">
      <div class="qStat">CPU<b id="qCpu">-</b><u id="qCpuB"></u></div><div class="qStat">RAM<b id="qRam">-</b><u id="qRamB"></u></div>
      <div class="qStat">GPU<b id="qGpu">-</b><u id="qGpuB"></u></div><div class="qStat">DISK<b id="qDisk">-</b><u id="qDiskB"></u></div></div>
      <div class="qNet"><span id="qDown"></span><span id="qUp"></span></div></div>
    <div class="qFoot">
      <button data-tip="Settings" data-ms=""><i class="ti ti-settings"></i></button>
      <button data-tip="File Explorer" data-a="explorer"><i class="ti ti-folder"></i></button>
      <button data-tip="Task Manager" data-a="taskmgr"><i class="ti ti-activity"></i></button>
      <button data-tip="Lock" data-a="lock"><i class="ti ti-lock"></i></button>
      <button data-tip="Sleep" data-pw="sleep"><i class="ti ti-zzz"></i></button>
      <button data-tip="Restart" data-pw="restart"><i class="ti ti-refresh"></i></button>
      <button data-tip="Shut down" data-pw="shutdown"><i class="ti ti-power"></i></button>
    </div>`;
  $$('[data-ms]', p).forEach((b) => b.onclick = ms$(b.dataset.ms));
  $('#qNet', p).onclick = ms$('network');
  $$('[data-a]', p).forEach((b) => b.onclick = async () => { await Shell.closePanel(); Shell.act('sys', { n: b.dataset.a }); });
  $$('[data-pw]', p).forEach((b) => b.onclick = async () => { const r = await Shell.power(b.dataset.pw, b); if (r === 'done') Shell.closePanel(); });
  $$('[data-k]', p).forEach((b) => b.onclick = () => { Shell.act('sys', { n: b.dataset.k });
    if (b.dataset.k === 'play') { const i = b.firstElementChild; i.className = i.className.includes('play') ? 'ti ti-player-pause' : 'ti ti-player-play'; } });
  $$('[data-tip]', p).forEach((b) => { b.onmouseenter = () => Shell.tip.show(b, b.dataset.tip); b.onmouseleave = () => Shell.tip.hide(); });
  const vol = $('#qVol', p);
  vol.oninput = () => { volDrag = true; vol.style.setProperty('--v', vol.value + '%'); $('#qVolN', p).textContent = vol.value + '%';
    clearTimeout(volTimer); volTimer = setTimeout(() => Shell.setVolume(vol.value), 40); };
  vol.onchange = () => { clearTimeout(volTimer); Shell.setVolume(vol.value); setTimeout(() => { volDrag = false; }, 600); };
  $('#qMute', p).onclick = () => Shell.volume('mute');
  fillQuick(s);
  PN.tick = setInterval(() => fillQuick((Shell.state && Shell.state.sys) || {}), 1000);
  Shell.api('quick').then((q) => {
    if (!q || Shell.panel !== 'quick') return;
    const t = $('#qNet', p), wifi = q.net === 'wifi', on = q.net !== 'none';
    t.classList.toggle('act', on);
    t.querySelector('i').className = 'ti ' + (wifi ? 'ti-wifi' : on ? 'ti-network' : 'ti-wifi-off');
    t.querySelector('b').textContent = wifi ? 'Wi-Fi' : on ? 'Ethernet' : 'Offline';
    t.querySelector('small').textContent = wifi ? (q.ssid || 'connected') + (q.signal >= 0 ? `  ·  ${q.signal}%` : '') : on ? (q.adapter || 'connected') : 'not connected';
    $('#qMach', p).textContent = q.machine || 'this PC';
    fillOutputs(q.outputs || []);
  });
  // output devices, like Windows' own volume flyout: the current one on the card, the list under it
  const fillOutputs = (outs) => {
    const cur = outs.find((o) => o.default), btn = $('#qOut', p), box = $('#qOuts', p);
    btn.querySelector('span').textContent = cur ? cur.name : (outs.length ? 'Choose output' : 'No output device');
    box.innerHTML = outs.map((o, i) => `<button class="qOutI${o.default ? ' on' : ''}" data-i="${i}"><i class="ti ${o.default ? 'ti-circle-check-filled' : 'ti-device-speaker'}"></i><span>${Shell.esc(o.name)}</span></button>`).join('');
    $$('.qOutI', box).forEach((b) => b.onclick = async () => {
      const o = outs[+b.dataset.i]; if (o.default) { box.classList.remove('on'); return; }
      $$('.qOutI', box).forEach((x) => x.disabled = true);
      await Shell.act('audioout', { id: o.id });
      const q2 = await Shell.api('quick'); if (Shell.panel === 'quick' && q2) fillOutputs(q2.outputs || []);
      box.classList.remove('on');
    });
  };
  $('#qOut', p).onclick = () => $('#qOuts', p).classList.toggle('on');
};
const fillQuick = (s) => {
  const p = PN.quick; if (!p || Shell.panel !== 'quick') return;
  const bar = (id, v) => { const el = $('#' + id, p); if (el) el.style.setProperty('--v', Math.max(0, Math.min(100, v)) + '%'); };
  const set = (id, t) => { const el = $('#' + id, p); if (el) el.textContent = t; };
  set('qCpu', s.cpu + '%'); bar('qCpuB', s.cpu); set('qRam', s.ram + '%'); bar('qRamB', s.ram);
  set('qGpu', s.gpu < 0 ? 'n/a' : s.gpu + '%'); bar('qGpuB', s.gpu); set('qDisk', s.disk + '%'); bar('qDiskB', s.disk);
  set('qDown', '↓ ' + Shell.rate(s.down || 0)); set('qUp', '↑ ' + Shell.rate(s.up || 0));
  const bi = Shell.batIcon(s), b = $('#qBat', p);
  if (b) b.innerHTML = bi ? `<i class="ti ${bi}"></i>${s.battery}%` : '<i class="ti ti-plug-connected"></i>AC';
  if (!volDrag && s.volume >= 0) { const v = $('#qVol', p); v.value = s.mute ? 0 : s.volume; v.style.setProperty('--v', v.value + '%'); set('qVolN', s.mute ? 'mute' : s.volume + '%'); }
  const m = $('#qMute i', p); if (m) m.className = 'ti ' + Shell.volIcon(s);
};

/* ---------- notification centre + calendar */
let calMonth = null;
const renderCenter = () => {
  const p = PN.center, now = new Date();
  calMonth = new Date(now.getFullYear(), now.getMonth(), 1);
  p.innerHTML = `
    <div class="ncHead"><div class="ncTime" id="ncTime"></div><div class="ncDate" id="ncDate"></div></div>
    <div class="ncBar"><span class="h">Notifications</span><button id="ncClear">Clear all</button></div>
    <div class="ncList" id="ncList"></div>
    <div class="ncCal"><div class="ncCalHd"><button id="ncPrev"><i class="ti ti-chevron-left"></i></button><b id="ncMon"></b><button id="ncNext"><i class="ti ti-chevron-right"></i></button></div>
      <div class="ncGrid" id="ncGrid"></div></div>`;
  $('#ncClear', p).onclick = () => { $$('.nCard', p).forEach((c) => c.classList.add('gone')); setTimeout(() => Shell.act('notif', { op: 'clear' }), 180); };
  $('#ncPrev', p).onclick = () => { calMonth.setMonth(calMonth.getMonth() - 1); drawCal(); };
  $('#ncNext', p).onclick = () => { calMonth.setMonth(calMonth.getMonth() + 1); drawCal(); };
  $('#ncMon', p).onclick = () => { const n = new Date(); calMonth = new Date(n.getFullYear(), n.getMonth(), 1); drawCal(); };
  const clock = () => { const d = new Date(); $('#ncTime', p).textContent = d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
    $('#ncDate', p).innerHTML = d.toLocaleDateString([], { weekday: 'long' }) + '<br>' + d.toLocaleDateString([], { day: 'numeric', month: 'long', year: 'numeric' }); };
  clock(); PN.tick = setInterval(clock, 1000);
  drawList(); drawCal();
  Shell.loadNotifs();
};
const drawCal = () => {
  const p = PN.center, y = calMonth.getFullYear(), m = calMonth.getMonth(), t = new Date();
  $('#ncMon', p).textContent = calMonth.toLocaleDateString([], { month: 'long', year: 'numeric' });
  const first = (new Date(y, m, 1).getDay() + 6) % 7, days = new Date(y, m + 1, 0).getDate(), prev = new Date(y, m, 0).getDate();
  let h = ['Mo', 'Tu', 'We', 'Th', 'Fr', 'Sa', 'Su'].map((d) => `<span class="wd">${d}</span>`).join('');
  for (let i = 0; i < 42; i++) {
    const d = i - first + 1;
    if (d < 1) h += `<span class="o">${prev + d}</span>`;
    else if (d > days) { if (i >= 35 && first + days <= 35) break; h += `<span class="o">${d - days}</span>`; }
    else h += `<span class="${y === t.getFullYear() && m === t.getMonth() && d === t.getDate() ? 'td' : ''}">${d}</span>`;
  }
  $('#ncGrid', p).innerHTML = h;
};

/* one card, shared by the centre list and the banners */
const cardHtml = (n) => {
  const name = n.app || 'Windows', ico = n.aumid ? Shell.icon('shell:AppsFolder\\' + n.aumid, 64) : '';
  return `<div class="nCard" data-id="${Shell.esc(n.id)}">
    ${ico ? `<img class="nIco" src="${ico}" alt="" data-letter="${Shell.esc(name[0].toUpperCase())}">` : `<span class="nIco letter">${Shell.esc(name[0].toUpperCase())}</span>`}
    <div class="nTxt"><div class="nTop"><b>${Shell.esc(name)}</b><span>${n.time ? Shell.ago(n.time) : ''}</span></div>
      ${n.title ? `<div class="nTitle">${Shell.esc(n.title)}</div>` : ''}${n.body ? `<div class="nBody">${Shell.esc(n.body)}</div>` : ''}</div>
    <span class="nX"><i class="ti ti-x"></i></span></div>`;
};
const drawList = () => {
  const l = PN.center && $('#ncList', PN.center); if (!l) return;
  l.innerHTML = Shell.notifs.length ? Shell.notifs.map(cardHtml).join('') : '<div class="ncEmpty"><i class="ti ti-bell-off"></i>No new notifications</div>';
  $$('.nCard', l).forEach((c) => {
    c.onclick = async (e) => {
      if (e.target.closest('.nX')) { c.classList.add('gone'); setTimeout(() => Shell.act('notif', { op: 'dismiss', id: c.dataset.id }), 180); return; }
      await Shell.closePanel(); Shell.act('notif', { op: 'open', id: c.dataset.id });
    };
  });
};

/* an app icon that fails to load becomes its first letter (no inline handlers: notification text is untrusted) */
document.addEventListener('error', (e) => { const t = e.target;
  if (t && t.tagName === 'IMG' && t.classList.contains('nIco')) { const s = document.createElement('span'); s.className = 'nIco letter'; s.textContent = t.dataset.letter || '?'; t.replaceWith(s); } }, true);

/* the list comes from the engine; a new id gets a banner unless the centre is open (or a fullscreen app is in front) */
Shell.loadNotifs = async (bannerId) => {
  const r = await Shell.api('notifs'); if (!r) return;
  Shell.notifs = r.items || [];
  const n = Shell.notifs.length;
  $$('[data-sys="notify"]').forEach((el) => { if (getComputedStyle(el).position === 'static') el.style.position = 'relative'; el.dataset.badge = n ? (n > 9 ? '9+' : n) : ''; });
  if (Shell.panel === 'notify') drawList();
  if (bannerId && Shell.panel !== 'notify') { const it = Shell.notifs.find((x) => String(x.id) === String(bannerId)); if (it) banner(it); }
};

const banner = (n) => {
  ensurePanels();
  if ($(`.tcBan[data-id="${n.id}"]`, PN.banners)) return;
  const b = document.createElement('div'); b.className = 'tcBan'; b.dataset.id = n.id; b.dataset.radius = 18; b.innerHTML = cardHtml(n);
  PN.banners.prepend(b);
  while (PN.banners.children.length > 3) PN.banners.lastElementChild.remove();
  let timer = null;
  const gone = () => { clearTimeout(timer); if (!b.isConnected) return; b.classList.add('out'); setTimeout(() => { b.remove(); refresh(); }, 280); };
  const arm = (ms) => { clearTimeout(timer); timer = setTimeout(gone, ms); };
  b.onmouseenter = () => clearTimeout(timer); b.onmouseleave = () => arm(2500);
  b.querySelector('.nCard').onclick = (e) => {
    if (e.target.closest('.nX')) return gone();   // the banner goes, the notification stays in the centre
    gone(); Shell.act('notif', { op: 'open', id: n.id });
  };
  arm(6500); refresh();
  try { const ac = Shell.ac(), o = ac.createOscillator(), g = ac.createGain(); o.type = 'sine'; o.frequency.setValueAtTime(1046, ac.currentTime); o.frequency.setValueAtTime(1568, ac.currentTime + .08);
    g.gain.setValueAtTime(.0001, ac.currentTime); g.gain.exponentialRampToValueAtTime(.05, ac.currentTime + .02); g.gain.exponentialRampToValueAtTime(.0001, ac.currentTime + .35);
    o.connect(g).connect(ac.destination); o.start(); o.stop(ac.currentTime + .4); } catch (e) { /* audio is optional */ }
};
const refresh = () => { if (Shell.refreshRegion && !Shell.open) Shell.refreshRegion(); else if (!Shell.open) Shell.region([]); };
setTimeout(() => Shell.loadNotifs(), 1500);
}

/* ---------- right-click on the bar: Task Manager and the usual taskbar menu; on an app also its own actions */
{
  let menu = null, leaveT = null;
  const hide = () => { clearTimeout(leaveT); if (!menu) return; menu.remove(); menu = null; if (Shell.refreshRegion) Shell.refreshRegion(); };
  Shell.closeCtx = hide;
  const appOf = (t) => { for (let el = t; el && el !== document.body; el = el.parentElement) if (el._app) return el._app; return null; };
  document.addEventListener('contextmenu', (e) => {
    if (e.defaultPrevented) return;   // a design's own right-click (e.g. mute on the volume icon)
    e.preventDefault();
    if (Shell.panel || Shell.busy) return;
    hide(); Shell.tip.hide();
    const a = appOf(e.target), items = [], L = (p) => () => Shell.act('launch', { p });
    // inside the Start menu: a pin, an app of All apps, a recent file or a search result (every design tags them with their path)
    const entry = Shell.open && e.target.closest ? e.target.closest('[data-p], [data-open]') : null;
    if (Shell.open && !entry && !a) return;   // empty space in the menu: nothing to offer
    if (entry) {
      const p = entry.dataset.p || entry.dataset.open, file = /^[a-z]:\\/i.test(p), store = /^shell:AppsFolder\\/i.test(p);
      const name = entry.title && !file ? entry.title : (entry.textContent || '').trim() || p.split('\\').pop().replace(/\.lnk$/i, '');
      const out = (go) => () => { if (Shell.close) Shell.close(); go(); };   // the menu leaves first, like a normal launch
      items.push({ head: name.length > 40 ? name.slice(0, 40) + '…' : name });
      items.push({ ic: 'ti-external-link', t: 'Open', go: () => Shell.open$(p) });
      if (file && /\.(lnk|exe|bat|cmd|msc)$/i.test(p)) items.push({ ic: 'ti-shield-check', t: 'Run as administrator', go: out(() => Shell.act('launch', { p, admin: 1 })) });
      if (file) items.push({ ic: 'ti-folder-open', t: 'Open file location', go: out(() => Shell.act('reveal', { p })) });
      if (store || /\.lnk$/i.test(p)) items.push({ ic: 'ti-trash', t: 'Uninstall…', go: out(() => Shell.act('launch', { p: 'ms-settings:appsfeatures' })) });
      if (file) items.push({ ic: 'ti-copy', t: 'Copy path', go: () => navigator.clipboard && navigator.clipboard.writeText(p).catch(() => {}) });
    } else if (e.target.closest && e.target.closest('#start, #coreIco')) {
      // the Start button: like Windows' Win+X menu
      items.push({ ic: 'ti-activity', t: 'Task Manager', go: () => Shell.sys('taskmgr') });
      items.push({ ic: 'ti-terminal-2', t: 'Terminal', go: () => Shell.act('launch', { p: 'wt.exe' }).then((r) => { if (r !== true) Shell.act('launch', { p: 'powershell.exe' }); }) });
      items.push({ ic: 'ti-folder', t: 'File Explorer', go: () => Shell.sys('explorer') });
      items.push({ ic: 'ti-settings', t: 'Settings', go: () => Shell.sys('settings') });
      items.push({ ic: 'ti-player-play', t: 'Run', go: L('shell:::{2559a1f3-21d7-11d4-bdaf-00c04f60b9f0}') });
      items.push({ sep: 1 });
      items.push({ ic: 'ti-cpu', t: 'Device Manager', go: L('devmgmt.msc') });
      items.push({ ic: 'ti-database', t: 'Disk Management', go: L('diskmgmt.msc') });
      items.push({ ic: 'ti-network', t: 'Network Connections', go: L('ncpa.cpl') });
      items.push({ ic: 'ti-bolt', t: 'Power Options', go: L('powercfg.cpl') });
      items.push({ ic: 'ti-list-details', t: 'Event Viewer', go: L('eventvwr.msc') });
      items.push({ ic: 'ti-info-circle', t: 'System', go: L('ms-settings:about') });
      items.push({ sep: 1 });
      items.push({ ic: 'ti-layout-bottombar-collapse', t: 'Show desktop', go: () => Shell.sys('desktop') });
      items.push({ ic: 'ti-palette', t: 'TRANSCRYPTO Themer', go: () => Shell.sys('themer') });
    } else if (a) {
      items.push({ head: a.title || a.name });
      items.push({ ic: 'ti-external-link', t: a.running ? 'New window' : 'Open', go: () => Shell.launchApp(a, true) });
      if (a.running && a.windows && a.windows.length)
        items.push({ ic: 'ti-x', t: a.windows.length > 1 ? `Close all windows (${a.windows.length})` : 'Close window', go: () => a.windows.forEach((h) => Shell.act('close', { h })) });
      items.push({ sep: 1 });
    }
    if (!entry && (!items.length || a)) {
      items.push({ ic: 'ti-activity', t: 'Task Manager', go: () => Shell.sys('taskmgr') });
      items.push({ ic: 'ti-folder', t: 'File Explorer', go: () => Shell.sys('explorer') });
      items.push({ ic: 'ti-layout-bottombar-collapse', t: 'Show desktop', go: () => Shell.sys('desktop') });
      items.push({ sep: 1 });
      items.push({ ic: 'ti-settings', t: 'Settings', go: () => Shell.sys('settings') });
      items.push({ ic: 'ti-palette', t: 'TRANSCRYPTO Themer', go: () => Shell.sys('themer') });
    }
    menu = document.createElement('div'); menu.className = 'tcCtx'; menu.dataset.radius = 12;
    menu.innerHTML = items.map((it, i) => it.head ? `<div class="h">${Shell.esc(it.head)}</div>` : it.sep ? '<hr>' : `<button data-i="${i}"><i class="ti ${it.ic}"></i>${Shell.esc(it.t)}</button>`).join('');
    document.body.append(menu);
    menu.querySelectorAll('button').forEach((b) => b.onclick = (ev) => { ev.stopPropagation(); const it = items[+b.dataset.i]; hide(); it.go(); });
    // above the bar, at the cursor, kept on screen
    let top = innerHeight; for (let el = e.target; el && el !== document.body; el = el.parentElement) { const q = el.getBoundingClientRect(); if (q.height > 140) break; if (q.height >= 1) top = Math.min(top, q.top); }
    const w = menu.offsetWidth, h = menu.offsetHeight;
    menu.style.left = Math.max(8, Math.min(innerWidth - w - 8, e.clientX - 12)) + 'px';
    menu.style.top = Math.max(8, Math.min(e.clientY, top) - h - 8) + 'px';
    if (Shell.refreshRegion) Shell.refreshRegion(); else Shell.region([]);
    // the bar never takes focus, so "click elsewhere" can't be seen: close once the pointer has left us for a moment
    menu.onmouseenter = () => clearTimeout(leaveT);
  });
  document.documentElement.addEventListener('mouseleave', () => { if (menu) { clearTimeout(leaveT); leaveT = setTimeout(hide, 700); } });
  document.documentElement.addEventListener('mouseenter', () => clearTimeout(leaveT));
  document.addEventListener('pointerdown', (e) => { if (menu && !menu.contains(e.target)) hide(); }, true);
  addEventListener('keydown', (e) => { if (e.key === 'Escape') hide(); });
}

/* ---------- if the page ever breaks while a menu is opening or open, it gives the screen back instead of covering it
   (a script error mid-animation once left the full-screen menu stuck, eating every click) */
Shell.recover = (why) => {
  console.error('shell recover:', why);
  try { Shell.act('menu', { open: 0 }); } catch (e) { /* the engine also has its own watchdog */ }
  const last = +sessionStorage.getItem('tcRecover') || 0;
  sessionStorage.setItem('tcRecover', String(Date.now()));
  if (Date.now() - last > 15000) setTimeout(() => location.reload(), 300);   // a fresh page, but never a reload loop
};
addEventListener('error', (e) => { if (Shell.busy || Shell.open || Shell.panel) Shell.recover(e.message); });
addEventListener('unhandledrejection', (e) => { if (Shell.busy || Shell.open || Shell.panel) Shell.recover(String(e.reason)); });
{
  // each opening or closing is timed on its own (a close followed at once by an open is two short episodes, not one long one)
  let busy = !!Shell.busy, busyAt = 0;
  Object.defineProperty(Shell, 'busy', { configurable: true, get: () => busy, set: (v) => { if (v && !busy) busyAt = Date.now(); busy = !!v; } });
  setInterval(() => { if (busy && Date.now() - busyAt > 8000) { busyAt = Date.now(); Shell.recover('one opening/closing took over 8 s'); } }, 500);
}
