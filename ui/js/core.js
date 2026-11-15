/* TRANSCRYPTO Themer — core: API bridge, router, toasts, modals, element builder */
'use strict';

const TC = {
  token: new URLSearchParams(location.search).get('t') || sessionStorage.getItem('tc-token') || '',
  views: {},
  current: null,
  state: { preset: null, customized: [], labels: {} },
};
if (TC.token) sessionStorage.setItem('tc-token', TC.token);

/* ---------- API */
// PowerShell's JSON turns a 1-item list into an object and an empty one into {} or null, so list calls are normalized here
const LIST_FNS = new Set(['backup.components', 'backup.list', 'restore.list', 'update.history', 'wallpapers.library', 'wallpapers.monitors', 'icons.packs', 'icons.drives',
  'cursors.packs', 'sounds.packs', 'sounds.events', 'sounds.windowsMedia', 'fonts.list']);
const asList = (x) => Array.isArray(x) ? x : (x == null || (typeof x === 'object' && !Object.keys(x).length)) ? [] : [x];

async function api(fn, args = {}, opts = {}) {
  if (opts.busy) busy(true, opts.busy);
  try {
    const r = await fetch('/api', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TC-Token': TC.token }, body: JSON.stringify({ fn, args }) });
    const j = await r.json();
    if (!j.ok) throw new Error(j.error || 'Unknown error');
    return LIST_FNS.has(fn) ? asList(j.data) : j.data;
  } catch (e) {
    if (e instanceof TypeError) e = new Error('Backend is not running. Close this window and open TRANSCRYPTO again.');
    if (!opts.quiet) toast(e.message, 'err');
    throw e;
  } finally { if (opts.busy) busy(false); }
}
const media = (p) => p ? `/media?t=${encodeURIComponent(TC.token)}&p=${encodeURIComponent(String(p).replace(/,-?\d+$/, '').replace(/^"|"$/g, ''))}` : '';

async function uploadFile(file, dir) {
  const q = `/upload?name=${encodeURIComponent(file.name)}`;
  const r = await fetch(q, { method: 'POST', headers: { 'X-TC-Token': TC.token }, body: file });
  const j = await r.json(); if (!j.ok) throw new Error(j.error); return j.data;
}

/* heartbeat: the backend exits ~25 s after the window closes */
setInterval(() => { api('app.ping', {}, { quiet: true }).catch(() => {}); }, 5000);

/* ---------- element builder */
function h(tag, attrs, ...kids) {
  const el = document.createElement(tag);
  if (attrs) for (const [k, v] of Object.entries(attrs)) {
    if (v == null || v === false) continue;
    if (k === 'class') el.className = v;
    else if (k === 'style' && typeof v === 'object') Object.assign(el.style, v);
    else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2).toLowerCase(), v);
    else if (k === 'html') el.innerHTML = v;
    else if (v === true) el.setAttribute(k, '');
    else el.setAttribute(k, v);
  }
  for (const kid of kids.flat(Infinity)) {
    if (kid == null || kid === false) continue;
    el.append(kid instanceof Node ? kid : document.createTextNode(String(kid)));
  }
  return el;
}
const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const esc = (s) => String(s ?? '').replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const fmtDate = (iso) => { if (!iso) return '—'; const d = new Date(iso); return isNaN(d) ? iso : d.toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' }); };
const ago = (iso) => { if (!iso) return 'never'; const s = (Date.now() - new Date(iso)) / 1000; if (s < 90) return 'just now'; if (s < 3600) return Math.round(s / 60) + ' min ago'; if (s < 86400) return Math.round(s / 3600) + ' h ago'; return Math.round(s / 86400) + ' days ago'; };
const fileName = (p) => String(p || '').split(/[\\/]/).pop().replace(/,-?\d+$/, '');

/* ---------- feedback */
function toast(msg, type = '') {
  const t = h('div', { class: 'toast ' + type }, msg);
  $('#toasts').append(t);
  setTimeout(() => { t.style.transition = '.3s'; t.style.opacity = '0'; setTimeout(() => t.remove(), 320); }, type === 'err' ? 6500 : 3200);
}
function busy(on, text) { $('#busy').classList.toggle('hidden', !on); if (text && text !== true) $('#busyText').textContent = text; else $('#busyText').textContent = 'Working…'; }

function modal({ title, body, actions = [], wide = false }) {
  return new Promise((resolve) => {
    const back = h('div', { class: 'modal-back' });
    const close = (v) => { back.remove(); resolve(v); };
    const box = h('div', { class: 'modal glass', style: wide ? { width: 'min(860px, 94vw)' } : null },
      h('h2', null, title), typeof body === 'string' ? h('div', { html: body }) : body,
      h('div', { class: 'actions' }, actions.map((a) => h('button', { class: 'btn ' + (a.cls || ''), onclick: () => { if (a.check && !a.check()) return; close(a.value); } }, a.label))));
    back.append(box);
    back.addEventListener('mousedown', (e) => { if (e.target === back) close(null); });
    document.addEventListener('keydown', function k(e) { if (e.key === 'Escape') { document.removeEventListener('keydown', k); close(null); } });
    $('#modalRoot').append(back);
  });
}
const confirmBox = (title, html, okLabel = 'Continue', cls = 'primary') => modal({ title, body: html, actions: [{ label: 'Cancel', value: false }, { label: okLabel, value: true, cls }] });
async function promptBox(title, label, value = '') {
  const inp = h('input', { type: 'text', value, style: { width: '100%' } });
  setTimeout(() => inp.focus(), 50);
  const r = await modal({ title, body: h('label', { class: 'field' }, h('span', null, label), inp), actions: [{ label: 'Cancel', value: null }, { label: 'OK', value: 'ok', cls: 'primary' }] });
  return r ? inp.value : null;
}

/* click ripple for every button */
document.addEventListener('pointerdown', (e) => {
  const b = e.target.closest('.btn, .seg button, .nav-item'); if (!b) return;
  const r = b.getBoundingClientRect(); const s = Math.max(r.width, r.height);
  const rp = h('span', { class: 'ripple', style: { width: s + 'px', height: s + 'px', left: (e.clientX - r.left - s / 2) + 'px', top: (e.clientY - r.top - s / 2) + 'px' } });
  if (getComputedStyle(b).position === 'static') b.style.position = 'relative';
  b.append(rp); setTimeout(() => rp.remove(), 600);
});

/* ---------- router */
const ICONS = {
  dashboard: '<path d="M3 13h8V3H3zM13 21h8V11h-8zM3 21h8v-6H3zM13 3v6h8V3z"/>',
  themes: '<path d="M12 3a9 9 0 1 0 0 18c1.1 0 1.6-.9 1.2-1.8-.5-1.1.2-2.2 1.4-2.2H17a4 4 0 0 0 4-4c0-5-4-10-9-10z"/><circle cx="7.5" cy="11" r="1.2"/><circle cx="11" cy="7" r="1.2"/><circle cx="16" cy="8" r="1.2"/>',
  wallpapers: '<rect x="3" y="4" width="18" height="14" rx="2"/><path d="M3 15l5-5 4 4 3-3 6 6"/><path d="M9 21h6"/>',
  colors: '<circle cx="12" cy="12" r="9"/><path d="M12 3v18M3 12h18" opacity=".4"/><path d="M12 3a9 9 0 0 1 0 18z" fill="currentColor" opacity=".25"/>',
  icons: '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
  cursors: '<path d="M5 3l14 7-6 2-2 6z"/>',
  sounds: '<path d="M4 10v4h4l5 4V6L8 10zM16 8a5 5 0 0 1 0 8M19 5a9 9 0 0 1 0 14"/>',
  startmenu: '<rect x="3" y="3" width="8" height="8" rx="1.5"/><rect x="13" y="3" width="8" height="8" rx="1.5"/><rect x="3" y="13" width="8" height="8" rx="1.5"/><rect x="13" y="13" width="8" height="8" rx="1.5"/>',
  taskbar: '<rect x="2" y="15" width="20" height="5" rx="1.5"/><path d="M8 17.5h.01M12 17.5h.01M16 17.5h.01"/>',
  explorer: '<path d="M3 6h7l2 2h9v11H3z"/><path d="M3 10h18"/>',
  lock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
  boot: '<path d="M12 2v10"/><path d="M6.3 6.3a8 8 0 1 0 11.4 0"/>',
  fonts: '<path d="M4 20L10 4h1l6 16M6.5 14h8"/><path d="M17 20h4"/>',
  backup: '<path d="M4 7v10c0 1.7 3.6 3 8 3s8-1.3 8-3V7"/><ellipse cx="12" cy="7" rx="8" ry="3"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>',
  settings: '<circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1.1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1.1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z"/>',
};
const NAV = [
  ['Overview', ['dashboard', 'themes']],
  ['Look', ['wallpapers', 'colors', 'icons', 'cursors', 'sounds']],
  ['Shell', ['startmenu', 'taskbar', 'explorer']],
  ['System', ['lock', 'boot', 'fonts']],
  ['Safety', ['backup', 'settings']],
];
const svg = (name) => `<svg viewBox="0 0 24 24">${ICONS[name] || ''}</svg>`;

function registerView(id, def) { TC.views[id] = def; }

function buildNav() {
  const nav = $('#nav'); nav.innerHTML = '';
  for (const [group, ids] of NAV) {
    nav.append(h('div', { class: 'nav-group' }, group));
    for (const id of ids) {
      const v = TC.views[id]; if (!v) continue;
      const custom = TC.state.customized.includes(id);
      nav.append(h('div', { class: 'nav-item' + (TC.current === id ? ' active' : ''), 'data-id': id, onclick: () => go(id), title: custom ? 'Customized on top of the preset' : null },
        h('span', { html: svg(id) }), v.title, custom ? h('i', { class: 'dot' }) : null));
    }
  }
}

async function refreshLayer() {
  try {
    TC.state = await api('comp.state', {}, { quiet: true });
    const chip = $('#layerChip');
    const presets = TC.presetsCache || [];
    const p = presets.find((x) => x.id === TC.state.preset);
    chip.innerHTML = '';
    chip.append(h('span', { class: 'sw', style: { background: p?.palette?.accent || 'var(--neon)', color: p?.palette?.accent || 'var(--neon)' } }),
      p ? p.name : (TC.state.preset || 'No preset'));
    if (TC.state.customized.length) chip.append(h('span', { class: 'tag pink' }, '+' + TC.state.customized.length + ' customized'));
    buildNav();
  } catch (e) {}
}

async function go(id, params) {
  const v = TC.views[id]; if (!v) return;
  TC.current = id; location.hash = id;
  try { localStorage.setItem('tc-last-tab', id); } catch (e) {}   // reopen on the same tab next time
  $('#title').textContent = v.title;
  $('#subtitle').textContent = v.subtitle || '';
  $$('.nav-item').forEach((n) => n.classList.toggle('active', n.dataset.id === id));
  const view = $('#view'); view.innerHTML = ''; view.scrollTop = 0;
  const holder = h('div', { class: 'stack' }); view.append(holder);
  try { await v.render(holder, params || {}); } catch (e) { holder.append(h('div', { class: 'note danger' }, 'Could not load this tab: ' + e.message)); }
}
