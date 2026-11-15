/* Mini previews drawn from the same tweak values the presets set */
function taskbarMock(v) {
  const small = Number(v['tb.small']) === 0, ic = small ? 9 : 12, labels = Number(v['tb.combine']) === 2;
  const app = (i) => h('i', { class: 'ap' + (i === 0 ? ' on' : ''), style: { width: (labels ? ic * 3.4 : ic) + 'px', height: ic + 'px' } }, labels ? h('b') : null);
  const search = Number(v['tb.search']);
  const items = [
    h('i', { class: 'st', style: { width: ic + 'px', height: ic + 'px' } }),
    search === 2 ? h('i', { class: 'sb' }) : search === 3 ? h('i', { class: 'sb sm' }) : search === 1 ? h('i', { class: 'ap', style: { width: ic + 'px', height: ic + 'px', borderRadius: '50%' } }) : null,
    v['tb.taskview'] ? h('i', { class: 'ap tv', style: { width: ic + 'px', height: ic + 'px' } }) : null,
    [0, 1, 2, 3].map(app)];
  return h('div', { class: 'lmock tb' },
    h('div', { class: 'lbar' + (v['tb.autohide'] ? ' hide' : '') + (v['tb.fxHover'] ? ' fx' : ''), style: { height: (small ? 16 : 22) + 'px' } },
      v['tb.widgets'] ? h('i', { class: 'wd' }) : null,
      h('div', { class: 'lmid', style: { justifyContent: Number(v['tb.align']) === 0 ? 'flex-start' : 'center' } }, items),
      h('span', { class: 'clk' }, v['tb.seconds'] ? '10:24:07' : '10:24')),
    v['tb.autohide'] ? h('span', { class: 'lnote' }, 'auto-hide') : null);
}

function startMock(v) {
  const layout = Number(v['start.layout']), hideRec = !!v['start.hideRecommended'];
  const rows = hideRec ? 5 : layout === 1 ? 4 : layout === 2 ? 2 : 3;
  const recRows = hideRec ? 0 : layout === 2 ? 4 : layout === 1 ? 1 : 2;
  const folders = Object.keys(v).filter((k) => k.startsWith('start.folder.') && v[k]).length;
  return h('div', { class: 'lmock sm' },
    h('div', { class: 'spanel' },
      h('i', { class: 'ssearch' }),
      h('div', { class: 'spins' }, Array.from({ length: rows * 6 }, () => h('i'))),
      recRows ? h('div', { class: 'srec' }, Array.from({ length: recRows * 2 }, () => h('i'))) : null,
      h('div', { class: 'sfoot' }, h('i', { class: 'su' }), h('span', { class: 'grow' }), Array.from({ length: folders }, () => h('i', { class: 'sf' })), h('i', { class: 'sf pw' }))));
}

async function layoutPanel(kind) {
  const data = await api('layouts.list', { kind });
  const mock = kind === 'taskbar' ? taskbarMock : startMock;
  const grid = h('div', { class: 'grid gauto' }, data.layouts.map((l) => h('div', { class: 'card glass hov tcard' + (data.active === l.id ? ' active' : '') },
    mock(Object.assign({}, data.current, l.values)),
    h('div', { class: 'tbody' },
      h('div', { class: 'row between nowrap' }, h('div', { class: 'tname' }, l.name), data.active === l.id ? h('span', { class: 'tag neon' }, 'Active') : (l.source === 'mine' ? h('span', { class: 'tag pink' }, 'Mine') : null)),
      h('div', { class: 'muted small', style: { minHeight: '34px', marginTop: '2px' } }, l.desc || ''),
      h('div', { class: 'row', style: { marginTop: '10px' } },
        btn(data.active === l.id ? 'Re-apply' : 'Apply layout', async () => {
          const r = await api('layouts.apply', { kind, id: l.id }, { busy: 'Applying ' + l.name + ' & restarting Explorer…' });
          toast(r.failed.length ? 'Applied, but these were blocked by Windows: ' + r.failed.join(', ') : l.name + ' applied', r.failed.length ? 'err' : 'ok');
          go(TC.current);
        }, 'sm primary grow', IC.check),
        l.source === 'mine' ? btn('', async () => { await api('layouts.delete', { kind, id: l.id }); go(TC.current); }, 'sm icon danger', IC.trash) : null)))));
  return h('div', { class: 'stack' },
    h('div', { class: 'row between' }, h('h3', { style: { margin: '4px' } }, 'Layout presets'),
      btn('Save current as layout', async () => { const n = await promptBox('Save layout', 'Name', 'My layout'); if (!n) return; await api('layouts.save', { kind, name: n }); toast('Saved', 'ok'); go(TC.current); }, 'sm', IC.save)),
    grid,
    h('h3', { style: { margin: '4px' } }, 'Fine-tune'));
}

registerView('startmenu', {
  title: 'Start Menu', subtitle: 'Layout presets · Recommended section · folders next to Power · search',
  async render(el) {
    el.append(
      await layoutPanel('startmenu'),
      h('div', { class: 'note' }, 'Start menu colors (accent on Start, dark/light, transparency) are in ', h('a', { href: '#', onclick: (e) => { e.preventDefault(); go('colors'); } }, 'Colors & Accent'), '. Alignment is in ', h('a', { href: '#', onclick: (e) => { e.preventDefault(); go('taskbar'); } }, 'Taskbar'), '.'),
      await tweaksPanel('startmenu'));
  },
});

/* Custom shell designs (shell/<design>.html) and their live backgrounds (shell/bg-<design>.html) */
const SHELL_DESIGNS = [{ v: 'none', l: 'Windows taskbar (off)' }, { v: 'sharmaji', l: 'SharmaJi Command Center' }, { v: 'orbital', l: 'Orbital' }, { v: 'projector', l: 'Projector' }];
const SHELL_PALETTES = { sharmaji: [{ v: 'sharmaji', l: 'SharmaJi (saffron x cyan)' }, { v: 'transcrypto', l: 'TRANSCRYPTO (cyan x magenta)' }, { v: 'alien', l: 'Alien (green x violet)' }],
  orbital: [{ v: 'aurora', l: 'Aurora' }, { v: 'lagoon', l: 'Lagoon' }, { v: 'ember', l: 'Ember' }],
  projector: [{ v: 'platinum', l: 'Platinum' }, { v: 'solar', l: 'Solar' }, { v: 'rosso', l: 'Rosso' }] };
async function designCard(key, title, desc, extra) {
  const eng = await api('engine.status'), cur = Object.assign({ design: 'none', palette: '' }, eng.config[key]);
  const save = async (vals) => {
    Object.assign(cur, vals);
    const values = { [key]: { design: cur.design, palette: cur.palette } };
    if (key === 'liveBg' && cur.design !== 'none') values.wallpaper = { enabled: false };   // one moving wallpaper at a time
    await api('engine.setConfig', { values }, { busy: cur.design !== 'none' ? 'Starting…' : null });
    toast(cur.design === 'none' ? 'Off' : 'Applied', 'ok'); go(TC.current);
  };
  const pals = SHELL_PALETTES[cur.design] || [];
  return card(title, h('p', { class: 'muted small' }, desc),
    selectRow('Design', null, SHELL_DESIGNS.map((d) => d.v === 'none' && key === 'liveBg' ? { v: 'none', l: 'Off' } : d), cur.design,
      (x) => save({ design: x, palette: (SHELL_PALETTES[x] || [{ v: '' }])[0].v })),
    pals.length ? selectRow('Colors', null, pals, cur.palette || pals[0].v, (x) => save({ palette: x })) : null,
    extra || null);
}

/* Engine-drawn effects (engine.json key) with their own colors: 'preset' | 'accent' | custom #hex */
const TINT_STYLES = [{ v: 'gradient', l: 'Gradient (preset into black)' }, { v: 'aero', l: 'Aero Glass (Windows 7 sheen)' }, { v: 'frost', l: 'Frosted glass (glassmorphism)' },
  { v: 'aurora', l: 'Aurora clouds' }, { v: 'neon', l: 'Neon edges' }];
async function engineFxCard(key, title, desc, { enable = true, style = false, skin = false } = {}) {
  const eng = await api('engine.status');
  const fx = Object.assign({}, eng.config[key]);
  const save = async (vals, rerender) => {
    Object.assign(fx, vals);
    await api('engine.setConfig', { values: { [key]: fx } }, { busy: vals.enabled ? 'Starting the engine…' : null });
    toast('Saved', 'ok'); if (rerender) go(TC.current);
  };
  const mode = !fx.color || fx.color === 'preset' ? 'preset' : fx.color === 'accent' ? 'accent' : 'custom';
  const strength = h('input', { type: 'range', min: 15, max: 100, step: 5, value: Math.round((fx.strength ?? 0.7) * 100), style: { width: '220px' } });
  strength.addEventListener('change', () => save({ strength: Number(strength.value) / 100 }));
  return card(title,
    h('p', { class: 'muted small' }, desc),
    enable ? toggleRow('Enable', null, !!fx.enabled, (x) => save({ enabled: x })) : null,
    skin ? toggleRow('Color the taskbar itself', 'Preset gradient across the bar with a neon top edge (needs the hover glow on in Effects below)', fx.skin !== false, (x) => save({ skin: x })) : null,
    style ? selectRow('Style', null, TINT_STYLES, fx.style || 'gradient', (x) => save({ style: x })) : null,
    style ? h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Strength'), h('small', null, 'Higher = more color, text stays readable')), strength) : null,
    selectRow('Colors', 'Follow preset = the active theme\'s two accent colors', [{ v: 'preset', l: 'Follow preset' }, { v: 'accent', l: 'Windows accent' }, { v: 'custom', l: 'Custom colors' }], mode,
      (x) => save(x === 'custom' ? { color: '#00f0ff', color2: '#ff2bd6' } : { color: x, color2: x }, true)),
    mode === 'custom' ? h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Custom colors'), h('small', null, 'First and second gradient color')),
      colorField(fx.color, (x) => save({ color: x })), colorField(fx.color2, (x) => save({ color2: x }))) : null);
}

/* "This PC" capacity bars drawn in the preset's own style (Windows' own bars are always blue / red) */
const DRIVE_STYLES = [{ v: 'preset', l: 'Follow preset' }, { v: 'orbital', l: 'Orbital - stats-card glow' }, { v: 'hud', l: 'SharmaJi - HUD segments' },
  { v: 'beam', l: 'Projector - light beam' }, { v: 'neon', l: 'Neon tube' }, { v: 'matrix', l: 'Matrix blocks' }, { v: 'soft', l: 'Soft gradient' }, { v: 'flat', l: 'Flat' }];
async function driveBarsCard() {
  const c = await engineFxCard('driveBars', 'Drive bars (This PC)',
    'The free-space bars under each drive in This PC, drawn in your preset\'s style: Orbital gets its stats-card glow, SharmaJi a HUD gauge, Projector a light beam, every other preset its own colors. Nearly full drives turn red-pink, like Windows. Drawn by the engine on top of Windows\' own bars (Windows has no setting for their color).');
  const eng = await api('engine.status'); const fx = Object.assign({}, eng.config.driveBars);
  c.append(selectRow('Style', 'Follow preset = picked by the active theme', DRIVE_STYLES, fx.style || 'preset', async (x) => {
    await api('engine.setConfig', { values: { driveBars: Object.assign(fx, { style: x }) } }); toast('Saved', 'ok'); }));
  return c;
}

registerView('taskbar', {
  title: 'Taskbar', subtitle: 'Layout presets · alignment · button size · behaviour · multi-monitor',
  async render(el) {
    el.append(
      await designCard('shell', 'Custom taskbar + Start menu', 'Replaces the Windows taskbar with a fully animated TRANSCRYPTO shell: real apps, your Start pins, tray, clock and search. The Win key opens it.',
        h('div', { class: 'note' }, h('b', null, 'Safety: '), 'Ctrl+Alt+Shift+T brings the Windows taskbar back instantly. The hover-only red X on the bar exits TRANSCRYPTO (click it twice). If the engine ever stops, the Windows taskbar returns by itself.')),
      await layoutPanel('taskbar'),
      h('div', { class: 'row' }, btn('Restart Explorer', async () => { await api('app.restartExplorer', {}, { busy: 'Restarting Explorer…' }); toast('Explorer restarted', 'ok'); }, 'sm', IC.refresh),
        h('span', { class: 'muted small' }, 'Taskbar color follows your accent — see Colors & Accent.')),
      await engineFxCard('taskbarFx', 'Taskbar skin & neon glow', 'The taskbar in your preset\'s colors plus a hard neon glow on the button under the cursor. Turn it on or off in Effects below or with a layout preset.', { enable: false, style: true, skin: true }),
      await tweaksPanel('taskbar'));
  },
});

registerView('explorer', {
  title: 'File Explorer', subtitle: 'Classic context menu, view options, navigation pane, snap & effects',
  async render(el) {
    el.append(
      await engineFxCard('explorerFx', 'Explorer colors & glass',
        'Colors File Explorer\'s dark panes (header, navigation, items, status bar) with your preset\'s colors, each pane its own gradient. Drawn by the TRANSCRYPTO engine as a click-through layer, so it follows every Explorer window. Windows itself has no setting for this.',
        { style: true }),
      await driveBarsCard(),
      await tweaksPanel('explorer'));
  },
});
