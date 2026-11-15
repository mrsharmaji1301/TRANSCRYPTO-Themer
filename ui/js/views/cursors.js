registerView('cursors', {
  title: 'Cursors', subtitle: 'TRANSCRYPTO neon packs · Windows schemes · imported packs · per-pointer files · size',
  async render(el) {
    const { bar } = await layerBar('cursors', () => go('cursors'));
    const [packs, comp] = await Promise.all([api('cursors.packs'), api('comp.get', { component: 'cursors' })]);
    const cur = comp.values || {}; const eff = comp.effective || {};
    const ROLE_LABEL = { Arrow: 'Normal select', Help: 'Help select', AppStarting: 'Working in background', Wait: 'Busy', Crosshair: 'Precision select', IBeam: 'Text select', NWPen: 'Handwriting',
      No: 'Unavailable', SizeNS: 'Vertical resize', SizeWE: 'Horizontal resize', SizeNWSE: 'Diagonal resize 1', SizeNESW: 'Diagonal resize 2', SizeAll: 'Move', UpArrow: 'Alternate select', Hand: 'Link select', Pin: 'Location select', Person: 'Person select' };

    const mk = (p) => {
      const active = eff.pack === p.id;
      return h('div', { class: 'tile' + (active ? ' sel' : ''), onclick: async () => { await setComp('cursors', { pack: p.id, roles: null }, p.name + ' applied'); go('cursors'); } },
        h('div', { class: 'packicons', style: { height: '72px', alignItems: 'center', background: p.color ? `radial-gradient(circle, ${p.color}22, rgba(0,0,0,.3))` : null } },
          p.preview ? h('img', { src: media(p.preview), style: { width: '48px', height: '48px', objectFit: 'contain' }, onerror: (e) => e.target.remove() }) : null),
        h('div', { class: 'meta' }, h('span', { class: 'grow' }, p.name), h('span', { class: 'tag' + (p.source === 'transcrypto' ? ' neon' : '') }, p.source)));
    };
    const ours = packs.filter((p) => p.source === 'transcrypto');
    const others = packs.filter((p) => p.source !== 'transcrypto');

    const sizeLbl = h('span', { class: 'mono' }, (cur.size || 32) + ' px');
    const size = h('input', { type: 'range', min: 20, max: 256, step: 4, value: cur.size || 32, style: { width: '260px' } });
    size.addEventListener('input', () => { sizeLbl.textContent = size.value + ' px'; });
    size.addEventListener('change', () => setComp('cursors', { size: Number(size.value) }, 'Cursor size set'));

    const roles = cur.roles || {};
    const roleCard = card('Individual pointers', h('p', { class: 'muted small' }, 'Swap any single pointer — saved as your override on top of the pack.'),
      h('div', { class: 'list' }, Object.keys(ROLE_LABEL).map((r) => h('div', { class: 'li' },
        h('div', { class: 'grow' }, h('b', null, ROLE_LABEL[r]), h('small', null, roles[r] ? fileName(roles[r]) : 'none (system)')),
        btn('Change', async () => { const f = await pickFile(F_CUR, ROLE_LABEL[r]); if (f) { await setComp('cursors', { roles: { [r]: f } }); go('cursors'); } }, 'sm')))));

    const eng = await api('engine.status');
    const fx = Object.assign({}, eng.config.clickFx);
    const saveFx = async (vals) => { Object.assign(fx, vals); await api('engine.setConfig', { values: { clickFx: fx } }, { busy: fx.enabled ? 'Starting click effect…' : null }); toast(fx.enabled ? 'Click effect on' : 'Click effect off', 'ok'); };
    const fxSize = h('input', { type: 'range', min: 40, max: 220, step: 10, value: fx.size || 90, style: { width: '200px' } });
    fxSize.addEventListener('change', () => saveFx({ size: Number(fxSize.value) }));
    const followAccent = fx.color === 'accent';
    const fxCard = card('Click effect (everywhere in Windows)',
      h('p', { class: 'muted small' }, 'The same neon ripple as this app, on every click: desktop, taskbar, apps. Runs in the engine (no CPU between clicks). Windows draws Start / Search flyouts above everything, so the ripple is hidden behind those.'),
      toggleRow('Enable click effect', null, !!fx.enabled, (x) => saveFx({ enabled: x })),
      selectRow('Style', null, [{ v: 'ripple', l: 'Ripple' }, { v: 'pulse', l: 'Double pulse' }, { v: 'burst', l: 'Burst' }], fx.style || 'ripple', (x) => saveFx({ style: x })),
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Size')), fxSize),
      toggleRow('Follow Windows accent color', 'Changes with your theme', followAccent, (x) => saveFx(x ? { color: 'accent', color2: 'accent' } : { color: '#00f0ff', color2: '#ff2bd6' })),
      followAccent ? null : h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Colors')), colorField(fx.color, (x) => saveFx({ color: x })), colorField(fx.color2, (x) => saveFx({ color2: x }))),
      toggleRow('Right-click too', null, fx.rightClick !== false, (x) => saveFx({ rightClick: x })),
      toggleRow('Off in fullscreen games / video', null, fx.skipFullscreen !== false, (x) => saveFx({ skipFullscreen: x })));

    el.append(bar, fxCard,
      card('Size', h('div', { class: 'row' }, size, sizeLbl, h('span', { class: 'muted small' }, 'Minimum 20 px. Below 32 px TRANSCRYPTO redraws the cursors smaller (Windows itself stops at 32).'))),
      h('div', { class: 'row between' }, h('h3', { style: { margin: '4px' } }, 'TRANSCRYPTO packs'),
        btn('Import cursor pack folder', async () => { const f = await pickFolder('Folder with .cur/.ani files (install.inf supported)'); if (!f) return; const r = await api('cursors.importPack', { folder: f }, { busy: 'Importing…' }); toast(`Imported ${r.mapped.length} of 17 pointers`, 'ok'); go('cursors'); }, 'sm', IC.down)),
      h('div', { class: 'grid gauto-s' }, ours.map(mk)),
      h('h3', { style: { margin: '4px' } }, 'Windows & imported schemes'),
      h('div', { class: 'grid gauto-s' }, h('div', { class: 'tile' + (eff.pack === 'default' ? ' sel' : ''), onclick: async () => { await setComp('cursors', { pack: 'default', roles: null }, 'Windows default cursors'); go('cursors'); } },
        h('div', { class: 'packicons', style: { height: '72px', alignItems: 'center' } }, h('span', { class: 'muted' }, 'Aero')), h('div', { class: 'meta' }, 'Windows Default')), others.map(mk)),
      roleCard);
  },
});
