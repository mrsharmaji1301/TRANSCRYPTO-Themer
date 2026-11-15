function themeMock(p) {
  const pal = p.palette || {};
  const light = p.colors?.systemMode === 'light';
  const acc = p.colors?.accent || pal.accent || '#00f0ff';
  const surf = light ? '#f3f3f7ee' : (pal.surface || '#1b1b28') + 'ee';
  const txt = light ? '#222' : (pal.text || '#eee');
  const wpStyle = p.wallpaper?.type === 'image' && p.wallpaper?.image
    ? { background: `#000 url("${media(p.wallpaper.image)}") center/cover` }
    : { background: `radial-gradient(120% 90% at 80% 10%, ${pal.accent2 || acc}55, transparent 55%), radial-gradient(90% 80% at 10% 90%, ${acc}44, transparent 60%), ${p.wallpaper?.color || pal.bg || '#111'}` };
  const titleCol = p.colors?.accentOnTitlebars ? acc : surf;
  const barCol = p.colors?.accentOnStart ? acc + 'dd' : surf;
  const cur = p.palette?.accent || acc;
  return h('div', { class: 'mock' },
    h('div', { class: 'wp', style: wpStyle }),
    h('div', { class: 'win', style: { background: surf, color: txt } },
      h('div', { class: 'tb', style: { background: titleCol } }, h('i'), h('i'), h('i')),
      h('div', { class: 'bd' }, h('i', { style: { background: acc, width: '40%' } }), h('i', { style: { background: txt, width: '80%' } }), h('i', { style: { background: txt, width: '65%' } }), h('i', { style: { background: txt, width: '72%' } }))),
    h('div', { class: 'start', style: { background: surf } }, Array.from({ length: 8 }, (_, i) => h('i', { style: { background: i === 0 ? acc : (pal.accent2 && i === 3 ? pal.accent2 : txt + '33') } }))),
    h('div', { class: 'bar', style: { background: barCol, color: acc } }, h('i', { class: 'a', style: { background: acc } }), h('i', { style: { background: txt + '66' } }), h('i', { style: { background: txt + '66' } }), h('i', { style: { background: txt + '66' } })),
    h('svg', { class: 'cur', viewBox: '0 0 16 22', html: `<path d="M1 1l13 12-6 1 3 6-2.5 1-3-6L1 19z" fill="${cur}" stroke="#000" stroke-width="1"/>` }));
}

registerView('themes', {
  title: 'Themes', subtitle: 'Community-loved presets · live preview · your own themes',
  async render(el) {
    const data = await api('themes.list');
    TC.presetsCache = data.presets;
    const state = await api('comp.state');
    let previewing = data.previewing ? (sessionStorage.getItem('tc-preview') || '') : '';

    const banner = h('div');
    const drawBanner = () => {
      banner.innerHTML = '';
      if (!previewing) return;
      const p = data.presets.find((x) => x.id === previewing);
      banner.append(h('div', { class: 'note neon row between' },
        h('span', null, h('b', null, 'Live preview: '), (p ? p.name : previewing), ' is on your desktop right now.'),
        h('div', { class: 'row' },
          btn('Keep it', async () => { await api('themes.previewEnd', { keep: true, id: previewing }, { busy: 'Applying…' }); previewing = ''; sessionStorage.removeItem('tc-preview'); toast('Theme applied', 'ok'); await refreshLayer(); go('themes'); }, 'sm primary', IC.check),
          btn('Revert', async () => { await api('themes.previewEnd', {}, { busy: 'Reverting…' }); previewing = ''; sessionStorage.removeItem('tc-preview'); toast('Reverted', 'ok'); go('themes'); }, 'sm', IC.undo))));
    };

    const head = h('div', { class: 'card glass' },
      h('div', { class: 'row between' },
        h('div', null, h('h3', { style: { margin: 0 } }, 'How presets & customizations work'),
          h('div', { class: 'muted small', style: { marginTop: '4px' } }, 'A preset sets Colors, Wallpaper, Icons, Cursors and Sounds. Anything you then change in those tabs is kept as your override on top — no conflicts. When you switch preset you choose to keep or drop your overrides.')),
        h('div', { class: 'row' },
          btn('Save current look as My Theme', async () => { const n = await promptBox('Save as My Theme', 'Theme name', 'My Theme'); if (!n) return; await api('themes.saveMine', { name: n }, { busy: 'Saving…' }); toast('Saved', 'ok'); go('themes'); }, 'primary', IC.save),
          btn('Import .tctheme', async () => { const f = await pickFile('TRANSCRYPTO theme|*.tctheme;*.zip', 'Import theme'); if (!f) return; await api('themes.import', { path: f }, { busy: 'Importing…' }); toast('Imported', 'ok'); go('themes'); }, '', IC.down))),
      state.customized.length ? h('div', { class: 'row', style: { marginTop: '12px' } }, h('span', { class: 'muted small' }, 'Your overrides:'),
        state.customized.map((c) => h('span', { class: 'tag pink' }, state.labels[c] || c,
          h('a', { href: '#', style: { marginLeft: '4px' }, title: 'Drop this override', onclick: async (e) => { e.preventDefault(); await api('comp.reset', { component: c }, { busy: 'Resetting…' }); await refreshLayer(); go('themes'); } }, '×')))) : null);

    const makeCard = (p) => {
      const active = state.preset === p.id;
      const c = h('div', { class: 'card glass hov tcard' + (active ? ' active' : '') + (previewing === p.id ? ' previewing' : '') });
      const apply = async () => {
        let mode = 'full';
        if (state.customized.length) {
          mode = await modal({ title: 'Apply ' + p.name, body: `<p>You have customized <b>${state.customized.map((x) => esc(state.labels[x] || x)).join(', ')}</b>.</p><p class="muted">Keep them on top of the new preset, or apply the preset exactly as designed?</p>`,
            actions: [{ label: 'Cancel', value: null }, { label: 'Apply full preset', value: 'full' }, { label: 'Keep my customizations', value: 'keep', cls: 'primary' }] });
          if (!mode) return;
        }
        await api('themes.apply', { id: p.id, mode }, { busy: 'Applying ' + p.name + '…' });
        sessionStorage.removeItem('tc-preview');
        toast(p.name + ' applied', 'ok'); await refreshLayer(); go('themes');
      };
      const pvSwitch = switchEl(previewing === p.id, async (on, inp) => {
        try {
          if (on) { await api('themes.preview', { id: p.id }, { busy: 'Previewing on your desktop…' }); previewing = p.id; sessionStorage.setItem('tc-preview', p.id); }
          else { await api('themes.previewEnd', {}, { busy: 'Reverting…' }); previewing = ''; sessionStorage.removeItem('tc-preview'); }
          $$('.tcard').forEach((x) => x.classList.remove('previewing')); if (previewing === p.id) c.classList.add('previewing');
          $$('.tcard .switch input').forEach((x) => { if (x !== inp) x.checked = false; });
          drawBanner();
        } catch (e) { inp.checked = !on; }
      });
      c.append(themeMock(p),
        h('div', { class: 'tbody' },
          h('div', { class: 'row between nowrap' }, h('div', { class: 'tname' }, p.name), active ? h('span', { class: 'tag neon' }, 'Active') : (p.source === 'mine' ? h('span', { class: 'tag pink' }, 'Mine') : null)),
          h('div', { class: 'muted small', style: { minHeight: '34px', marginTop: '2px' } }, p.desc || ''),
          h('div', { class: 'row', style: { gap: '4px', marginTop: '6px' } }, (p.tags || []).map((t) => h('span', { class: 'tag' }, t))),
          h('div', { class: 'row between', style: { marginTop: '8px' } },
            h('div', { class: 'pal' }, ['bg', 'surface', 'accent', 'accent2', 'text'].map((k) => h('i', { style: { background: p.palette?.[k] } }))),
            h('div', { class: 'row', style: { gap: '6px' } }, h('span', { class: 'muted small' }, 'Preview'), pvSwitch)),
          h('div', { class: 'row', style: { marginTop: '10px' } },
            btn(active ? 'Re-apply' : 'Apply', apply, 'sm primary grow', IC.check),
            btn('', async () => { const r = await api('themes.export', { id: p.id }, { busy: 'Exporting…' }); toast('Exported to ' + r, 'ok'); }, 'sm icon', IC.up),
            p.source === 'mine' ? btn('', async () => { if (await confirmBox('Delete ' + p.name + '?', 'This removes the saved theme file.', 'Delete', 'danger')) { await api('themes.deleteMine', { id: p.id }); go('themes'); } }, 'sm icon danger', IC.trash) : null)));
      return c;
    };

    const mine = data.presets.filter((p) => p.source === 'mine');
    const built = data.presets.filter((p) => p.source !== 'mine');
    drawBanner();
    el.append(banner, head,
      h('h3', { style: { margin: '6px 4px 0' } }, 'Presets'), h('div', { class: 'grid gauto' }, built.map(makeCard)),
      h('h3', { style: { margin: '6px 4px 0' } }, 'My Themes'),
      mine.length ? h('div', { class: 'grid gauto' }, mine.map(makeCard)) : h('div', { class: 'empty' }, 'Nothing yet — customize a preset and hit "Save current look as My Theme".'));
  },
});
