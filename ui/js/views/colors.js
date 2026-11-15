registerView('colors', {
  title: 'Colors & Accent', subtitle: 'Dark / light, accent color, transparency, where the accent shows',
  async render(el) {
    const { bar } = await layerBar('colors', () => go('colors'));
    const c = (await api('comp.get', { component: 'colors' })).values || {};
    const set = (vals, msg) => setComp('colors', vals, msg);
    const NEON = ['#00F0FF', '#FF2BD6', '#7C4DFF', '#39FF14', '#FFE600', '#FF6B00', '#FF003C', '#00FFA3', '#4D9DFF', '#C77DFF', '#89B4FA', '#F5C2E7', '#BD93F9', '#88C0D0', '#7AA2F7', '#FE8019', '#EBBCBA', '#A7C080', '#268BD2', '#0078D4'];
    let accent = (c.accent || '#0078D4').toUpperCase();

    const strip = h('div', { class: 'palette-strip' });
    const drawStrip = async (a) => { try { const sw = await api('colors.palette', { accent: a }, { quiet: true }); strip.innerHTML = ''; sw.slice(0, 7).forEach((x) => strip.append(h('i', { style: { background: x } }))); } catch (e) {} };
    const sws = h('div', { class: 'swatches' }, NEON.map((x) => h('button', { class: x === accent ? 'on' : '', style: { background: x, color: x }, title: x, onclick: () => { accent = x; picker.querySelector('input[type=color]').value = x.toLowerCase(); picker.querySelector('input[type=text]').value = x; $$('button', sws).forEach((b) => b.classList.toggle('on', b.title === x)); drawStrip(x); } })));
    const picker = colorField(accent.toLowerCase(), (x) => { accent = x.toUpperCase(); drawStrip(accent); });

    const modeCard = card('Mode',
      selectRow('Windows mode', 'Taskbar, Start, Action Center', [{ v: 'dark', l: 'Dark' }, { v: 'light', l: 'Light' }], c.systemMode || 'dark', (x) => set({ systemMode: x })),
      selectRow('App mode', 'Settings, Explorer and apps', [{ v: 'dark', l: 'Dark' }, { v: 'light', l: 'Light' }], c.appsMode || 'dark', (x) => set({ appsMode: x })),
      toggleRow('Transparency effects', 'Mica / acrylic', c.transparency !== false, (x) => set({ transparency: x })));

    const accCard = card('Accent color',
      h('div', { class: 'stack' },
        sws,
        h('div', { class: 'row' }, picker, btn('Apply accent', () => set({ accent, autoAccent: false }, 'Accent applied'), 'primary', IC.check)),
        h('div', null, h('div', { class: 'muted small', style: { marginBottom: '6px' } }, 'Generated Windows accent palette'), strip),
        toggleRow('Pick accent from wallpaper automatically', null, !!c.autoAccent, (x) => set({ autoAccent: x }))));

    const where = card('Show accent color on',
      toggleRow('Start and taskbar', 'Works with dark Windows mode', !!c.accentOnStart, (x) => set({ accentOnStart: x })),
      toggleRow('Title bars and window borders', null, !!c.accentOnTitlebars, (x) => set({ accentOnTitlebars: x })),
      h('p', { class: 'muted small' }, 'If the taskbar does not pick up a change right away, use "Restart Explorer" on the Dashboard.'));

    el.append(bar, h('div', { class: 'grid g2' }, accCard, h('div', { class: 'stack' }, modeCard, where)));
    drawStrip(accent);
  },
});
