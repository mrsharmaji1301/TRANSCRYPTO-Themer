registerView('sounds', {
  title: 'Sounds', subtitle: 'Sound schemes, sound packs and every Windows event sound',
  async render(el) {
    const { bar } = await layerBar('sounds', () => go('sounds'));
    const [comp, packs, events, winMedia, folder] = await Promise.all([api('comp.get', { component: 'sounds' }), api('sounds.packs'), api('sounds.events'), api('sounds.windowsMedia'), api('sounds.packsFolder')]);
    const eff = comp.effective || {};
    const scheme = eff.scheme || comp.values?.scheme || 'default';
    const audio = new Audio();
    const play = (p) => { if (!p) return toast('No sound set'); audio.src = media(p); audio.play().catch(() => api('sounds.play', { path: p })); };

    const schemeCard = card('Scheme',
      h('div', { class: 'row' },
        seg(packs.map((p) => ({ v: p.id, l: p.name + (p.count ? ` (${p.count})` : '') })), scheme, async (v) => { await setComp('sounds', { scheme: v, events: null }, 'Sound scheme applied'); go('sounds'); })),
      h('p', { class: 'muted small' }, 'Sound pack = a folder of .wav files named after the event (for example ', h('span', { class: 'kbd' }, 'Notification.Default.wav'), ', ', h('span', { class: 'kbd' }, 'SystemHand.wav'), ', ', h('span', { class: 'kbd' }, 'EmptyRecycleBin.wav'), '). Missing events fall back to Windows sounds.'),
      h('div', { class: 'row' }, btn('Open sound packs folder', () => api('app.openPath', { path: folder }), 'sm', IC.folder), btn('Refresh', () => go('sounds'), 'sm', IC.refresh)));

    const opts = [{ v: '', l: '(none)' }].concat(winMedia.map((m) => ({ v: m.path, l: m.name })));
    const list = h('div', { class: 'list' }, events.map((e) => {
      const lbl = h('small', null, e.current ? fileName(e.current) : '(silent)');
      return h('div', { class: 'li' },
        btn('', () => play(e.current), 'sm icon', IC.play),
        h('div', { class: 'grow' }, h('b', null, e.label), lbl),
        selectEl(opts, opts.some((o) => o.v.toLowerCase() === (e.current || '').toLowerCase()) ? opts.find((o) => o.v.toLowerCase() === (e.current || '').toLowerCase()).v : '__x', async (v) => { await setComp('sounds', { events: { [e.id]: v } }); e.current = v; lbl.textContent = v ? fileName(v) : '(silent)'; play(v); }, { style: { width: '220px' } }),
        btn('File…', async () => { const f = await pickFile(F_WAV, e.label); if (f) { await setComp('sounds', { events: { [e.id]: f } }); e.current = f; lbl.textContent = fileName(f); play(f); } }, 'sm'),
        btn('', async () => { await setComp('sounds', { events: { [e.id]: e.windows } }, 'Windows sound'); e.current = e.windows; lbl.textContent = fileName(e.windows) || '(silent)'; }, 'sm icon', IC.undo));
    }));

    el.append(bar, schemeCard, card('Event sounds', h('p', { class: 'muted small' }, 'Every change is stored as your override on top of the preset. Startup sound lives in the Boot Screen tab.'), list));
  },
});
