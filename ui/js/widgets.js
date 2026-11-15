/* TRANSCRYPTO Themer — reusable widgets */
'use strict';

function card(title, ...kids) {
  return h('div', { class: 'card glass hov' }, title ? h('h3', null, title) : null, ...kids);
}

function switchEl(checked, onchange) {
  const inp = h('input', { type: 'checkbox' }); inp.checked = !!checked;
  inp.addEventListener('change', () => onchange(inp.checked, inp));
  return h('label', { class: 'switch' }, inp, h('i'));
}

function toggleRow(label, desc, checked, onchange, extra) {
  return h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, label), desc ? h('small', null, desc) : null), extra || null, switchEl(checked, onchange));
}

function selectEl(options, value, onchange, attrs = {}) {
  const s = h('select', attrs, options.map((o) => { const opt = h('option', { value: o.v }, o.l); if (String(o.v) === String(value)) opt.selected = true; return opt; }));
  s.addEventListener('change', () => onchange(s.value, s));
  return s;
}

function selectRow(label, desc, options, value, onchange) {
  return h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, label), desc ? h('small', null, desc) : null), selectEl(options, value, onchange));
}

function seg(options, value, onchange) {
  const wrap = h('div', { class: 'seg' });
  options.forEach((o) => {
    const b = h('button', { class: String(o.v) === String(value) ? 'on' : '', onclick: () => { $$('button', wrap).forEach((x) => x.classList.remove('on')); b.classList.add('on'); onchange(o.v); } }, o.l);
    wrap.append(b);
  });
  return wrap;
}

function btn(label, onclick, cls = '', icon) {
  return h('button', { class: 'btn ' + cls, onclick }, icon ? h('span', { html: `<svg viewBox="0 0 24 24">${icon}</svg>`, style: { display: 'contents' } }) : null, label);
}
const IC = {
  plus: '<path d="M12 5v14M5 12h14"/>', folder: '<path d="M3 7h6l2 2h10v10H3z"/>', link: '<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/>',
  trash: '<path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13"/>', play: '<path d="M7 4l13 8-13 8z"/>', check: '<path d="M4 12l5 5L20 6"/>', undo: '<path d="M9 14L4 9l5-5M4 9h11a5 5 0 0 1 0 10h-3"/>',
  save: '<path d="M5 3h11l3 3v15H5z"/><path d="M8 3v6h8V3M8 21v-7h8v7"/>', eye: '<path d="M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
  refresh: '<path d="M20 12a8 8 0 1 1-2.3-5.6M20 4v5h-5"/>', down: '<path d="M12 4v12M6 10l6 6 6-6M4 20h16"/>', up: '<path d="M12 20V8M6 14l6-6 6 6M4 4h16"/>', lock: '<rect x="4" y="10" width="16" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/>',
  bolt: '<path d="M13 2L4 14h7l-1 8 9-12h-7z"/>', image: '<rect x="3" y="4" width="18" height="16" rx="2"/><circle cx="8.5" cy="9.5" r="1.5"/><path d="M21 16l-5-5L5 20"/>',
};

async function pickFile(filter, title, multi = false) { return api('app.pickFile', { filter, title, multi }); }
async function pickFolder(title) { return api('app.pickFolder', { title }); }
const F_IMG = 'Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.jfif|All files|*.*';
const F_VID = 'Videos|*.mp4;*.webm;*.mkv;*.mov;*.wmv;*.m4v|All files|*.*';
const F_MEDIA = 'Pictures & videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.jfif;*.mp4;*.webm;*.mkv;*.mov;*.wmv;*.m4v|All files|*.*';
const F_ICO = 'Icons|*.ico;*.dll;*.exe|All files|*.*';
const F_CUR = 'Cursors|*.cur;*.ani|All files|*.*';
const F_WAV = 'Sounds|*.wav|All files|*.*';
const F_FONT = 'Fonts|*.ttf;*.otf;*.ttc|All files|*.*';

function iconImg(path, size = 40) {
  if (!path) return h('div', { class: 'icobox' }, h('span', { class: 'muted small' }, 'default'));
  const p = String(path);
  if (/\.(dll|exe)(,|$)/i.test(p)) return h('div', { class: 'icobox', title: p }, h('span', { class: 'muted small' }, fileName(p).slice(0, 10)));
  return h('div', { class: 'icobox', title: p }, h('img', { src: media(p), style: { width: size + 'px', height: size + 'px' }, onerror: (e) => { e.target.replaceWith(h('span', { class: 'muted small' }, 'icon')); } }));
}

/* Preset/override banner shown on top of themeable tabs */
async function layerBar(component, onReset) {
  const info = await api('comp.get', { component }, { quiet: true }).catch(() => null);
  const presets = TC.presetsCache || [];
  const p = presets.find((x) => x.id === info?.preset);
  const bar = h('div', { class: 'note neon row between' },
    h('div', { class: 'row' },
      h('span', null, 'Layer: '),
      h('span', { class: 'tag neon' }, p ? 'Preset · ' + p.name : 'No preset'),
      info?.customized ? h('span', { class: 'tag pink' }, 'Customized') : h('span', { class: 'tag' }, 'Following preset'),
      h('span', { class: 'muted small' }, 'Changes here are saved as your override on top of the preset.')),
    info?.customized ? btn(p ? 'Reset to preset' : 'Reset to Windows default', async () => {
      await api('comp.reset', { component }, { busy: 'Resetting…' }); toast('Reset done', 'ok'); await refreshLayer(); onReset && onReset();
    }, 'sm', IC.undo) : null);
  return { bar, info };
}

async function setComp(component, values, msg = 'Applied') {
  const r = await api('comp.set', { component, values }, { busy: 'Applying…' });
  toast(msg, 'ok'); refreshLayer();
  return r;
}

const restartText = { explorer: 'Takes effect after Explorer restarts', 'explorer-live': '', signout: 'Sign out to apply', reboot: 'Restart PC to apply' };

/* Generic tweak panel: everything registered with Register-RegTweak / Register-CustomTweak for a tab */
async function tweaksPanel(tab, opts = {}) {
  const list = await api('tweaks.list', { tab });
  const wrap = h('div', { class: 'grid ' + (opts.cols || 'g2') });
  const sections = {};
  let needExplorer = false;
  const explorerBar = h('div', { class: 'note warn row between hidden' }, h('span', null, 'Some changes need an Explorer restart.'),
    btn('Restart Explorer now', async () => { await api('app.restartExplorer', {}, { busy: 'Restarting Explorer…' }); explorerBar.classList.add('hidden'); toast('Explorer restarted', 'ok'); }, 'sm primary', IC.refresh));
  for (const t of list) {
    if (!sections[t.section]) { sections[t.section] = card(t.section); wrap.append(sections[t.section]); }
    const desc = [t.desc, t.warn ? '⚠ ' + t.warn : '', restartText[t.restart] || '', t.admin ? 'admin' : ''].filter(Boolean).join(' · ');
    const onSet = async (v, el) => {
      try {
        await api('tweaks.set', { id: t.id, value: v });
        if (t.restart === 'explorer') { needExplorer = true; explorerBar.classList.remove('hidden'); }
        else if (t.restart === 'signout') toast('Saved — sign out to see it', 'ok');
        else if (t.restart === 'reboot') toast('Saved — restart your PC to see it', 'ok');
        else toast('Applied', 'ok');
      } catch (e) { if (el && el.type === 'checkbox') el.checked = !v; }
    };
    if (t.kind === 'toggle') sections[t.section].append(toggleRow(t.label, desc, !!t.value, onSet));
    else sections[t.section].append(selectRow(t.label, desc, t.options || [], t.value, (v) => onSet(isNaN(v) || v === '' ? v : Number(v))));
  }
  const reset = btn('Reset this tab to Windows 11 defaults', async () => {
    if (!(await confirmBox('Reset to defaults?', 'All settings on this tab go back to how Windows 11 ships. A backup is not created automatically for this button — use Backup tab first if you want one.', 'Reset', 'danger'))) return;
    await api('backup.defaults', { components: [tab] }, { busy: 'Resetting…' }); toast('Back to Windows defaults', 'ok'); go(TC.current);
  }, 'sm danger', IC.undo);
  return h('div', { class: 'stack' }, explorerBar, wrap, opts.noReset ? null : h('div', { class: 'row' }, reset));
}

function colorField(value, onchange) {
  const inp = h('input', { type: 'color', value: value || '#00f0ff' });
  const txt = h('input', { type: 'text', value: value || '#00f0ff', style: { width: '100px' } });
  inp.addEventListener('input', () => { txt.value = inp.value; });
  inp.addEventListener('change', () => onchange(inp.value));
  txt.addEventListener('change', () => { if (/^#[0-9a-f]{6}$/i.test(txt.value)) { inp.value = txt.value; onchange(txt.value); } });
  return h('div', { class: 'row nowrap' }, inp, txt);
}

/* small library picker modal (wallpaper library) */
async function pickFromLibrary(kind) {
  const items = (await api('wallpapers.library')).filter((i) => !kind || i.kind === kind);
  if (!items.length) { toast('Library is empty — add files in the Wallpapers tab'); return null; }
  let chosen = null;
  const grid = h('div', { class: 'grid gauto-s' }, items.map((it) => {
    const t = h('div', { class: 'tile', onclick: () => { $$('.tile', grid).forEach((x) => x.classList.remove('sel')); t.classList.add('sel'); chosen = it.path; } },
      it.kind === 'video' ? h('video', { class: 'thumb', src: media(it.path) + '#t=1', muted: true, preload: 'metadata' }) : h('img', { class: 'thumb', src: media(it.path), loading: 'lazy' }),
      h('div', { class: 'meta' }, h('span', { class: 'grow' }, it.name)));
    return t;
  }));
  const ok = await modal({ title: 'Choose from library', body: grid, wide: true, actions: [{ label: 'Cancel', value: false }, { label: 'Use this', value: true, cls: 'primary' }] });
  return ok ? chosen : null;
}
