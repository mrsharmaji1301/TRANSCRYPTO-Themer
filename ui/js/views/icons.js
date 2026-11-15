registerView('icons', {
  title: 'Icons & Thumbnails', subtitle: 'Icon packs · desktop icons · drive icons · folder icons & covers · thumbnails',
  async render(el) {
    const { bar } = await layerBar('icons', () => go('icons'));
    const [packs, comp, drives] = await Promise.all([api('icons.packs'), api('comp.get', { component: 'icons' }), api('icons.drives')]);
    const cur = comp.values || {};
    const eff = comp.effective || {};

    const choosePackIcon = async (key) => {
      const opts = packs.filter((p) => p.files[key]);
      let chosen = null;
      const grid = h('div', { class: 'grid gauto-s' }, opts.map((p) => {
        const t = h('div', { class: 'tile', onclick: () => { $$('.tile', grid).forEach((x) => x.classList.remove('sel')); t.classList.add('sel'); chosen = p.files[key]; } },
          h('div', { class: 'packicons' }, h('img', { src: media(p.files[key]) })), h('div', { class: 'meta' }, p.name));
        return t;
      }));
      const r = await modal({ title: 'Choose icon', body: grid, wide: true, actions: [{ label: 'Cancel', value: null }, { label: 'From file…', value: 'file' }, { label: 'Use', value: 'use', cls: 'primary' }] });
      if (r === 'file') return pickFile(F_ICO, 'Choose an icon');
      return r === 'use' ? chosen : null;
    };

    const packCard = (p) => {
      const active = eff.pack === p.id;
      return h('div', { class: 'card glass hov tcard' + (active ? ' active' : '') },
        h('div', { class: 'packicons' }, ['thispc', 'folder', 'drive', 'recycleEmpty', 'recycleFull'].filter((k) => p.files[k]).map((k) => h('img', { src: media(p.files[k]), title: k }))),
        h('div', { class: 'tbody' }, h('div', { class: 'row between' }, h('b', null, p.name), active ? h('span', { class: 'tag neon' }, 'Active') : (p.user ? h('span', { class: 'tag pink' }, 'Imported') : null)),
          h('div', { class: 'muted small' }, p.desc || ''),
          h('div', { class: 'row', style: { marginTop: '10px' } }, btn('Apply pack', async () => { await setComp('icons', { pack: p.id }, p.name + ' icons applied'); go('icons'); }, 'sm primary grow', IC.check))));
    };
    const defCard = h('div', { class: 'card glass hov tcard' + (eff.pack === 'default' ? ' active' : '') },
      h('div', { class: 'packicons', style: { height: '68px', alignItems: 'center' } }, h('span', { class: 'muted' }, 'Windows 11 icons')),
      h('div', { class: 'tbody' }, h('b', null, 'Windows Default'), h('div', { class: 'row', style: { marginTop: '10px' } }, btn('Apply', async () => { await setComp('icons', { pack: 'default', thispc: '', recycleEmpty: '', recycleFull: '', network: '', user: '', folder: '', drive: '' }, 'Windows icons restored'); go('icons'); }, 'sm grow', IC.undo))));

    const ITEMS = [['thispc', 'This PC'], ['recycleEmpty', 'Recycle Bin (empty)'], ['recycleFull', 'Recycle Bin (full)'], ['network', 'Network'], ['user', 'User folder'], ['folder', 'All folders'], ['drive', 'All hard drives']];
    const singles = card('Individual icons', h('div', { class: 'list' }, ITEMS.map(([k, label]) => h('div', { class: 'li' },
      iconImg(cur[k] || (packs.find((p) => p.id === eff.pack)?.files[k])), h('div', { class: 'grow' }, h('b', null, label), h('small', null, cur[k] ? fileName(cur[k]) : 'pack / Windows default')),
      btn('Change', async () => { const f = await choosePackIcon(k); if (f) { await setComp('icons', { [k]: f }); go('icons'); } }, 'sm'),
      btn('', async () => { await setComp('icons', { [k]: '' }, 'Cleared'); go('icons'); }, 'sm icon', IC.undo)))));

    const driveCard = card('Drive icons (per drive)', h('div', { class: 'list' }, drives.map((d) => h('div', { class: 'li' },
      iconImg(d.icon), h('div', { class: 'grow' }, h('b', null, `${d.letter}:  ${d.label || ''}`), h('small', null, `${d.type}${d.sizeGB ? ' · ' + d.sizeGB + ' GB' : ''}`)),
      btn('Change', async () => { const f = await choosePackIcon('drive'); if (f) { await setComp('icons', { drives: { [d.letter]: f } }); go('icons'); } }, 'sm'),
      btn('', async () => { await setComp('icons', { drives: { [d.letter]: '' } }, 'Cleared'); go('icons'); }, 'sm icon', IC.undo)))),
      h('div', { class: 'row', style: { marginTop: '10px' } }, toggleRow('Shortcut arrows on icons', 'Turn off for clean desktop icons', cur.shortcutArrow !== false, async (x) => { await setComp('icons', { shortcutArrow: x }); })));

    let fFolder = '', fIcon = '', cImg = '';
    const fl = h('small', null, 'no folder chosen'), il = h('small', null, 'no icon chosen'), cl = h('small', null, 'no picture chosen');
    let coverAccent = (TC.presetsCache || []).find((p) => p.id === comp.preset)?.palette?.accent || '#00f0ff';
    const folderTools = card('Folder icons & covers',
      h('div', { class: 'note' }, 'Give any single folder its own icon, or turn a picture into a folder cover (the picture is drawn inside a folder shape and used as that folder\'s icon/thumbnail).'),
      h('div', { class: 'list', style: { marginTop: '10px' } },
        h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, '1 · Folder'), fl), btn('Choose folder', async () => { const f = await pickFolder('Folder to customize'); if (f) { fFolder = f; fl.textContent = f; } }, 'sm', IC.folder)),
        h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, '2a · Icon'), il), btn('Choose icon', async () => { const f = await choosePackIcon('folder'); if (f) { fIcon = f; il.textContent = f; } }, 'sm'),
          btn('Set icon', async () => { if (!fFolder || !fIcon) return toast('Choose a folder and an icon', 'err'); await api('icons.setFolderIcon', { folder: fFolder, icon: fIcon }, { busy: 'Setting…' }); toast('Folder icon set', 'ok'); }, 'sm primary')),
        h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, '2b · Cover picture'), cl), colorField(coverAccent, (x) => { coverAccent = x; }), btn('Choose picture', async () => { const f = await pickFile(F_IMG, 'Cover picture'); if (f) { cImg = f; cl.textContent = f; } }, 'sm', IC.image),
          btn('Make cover', async () => { if (!fFolder || !cImg) return toast('Choose a folder and a picture', 'err'); await api('icons.makeFolderCover', { folder: fFolder, image: cImg, accent: coverAccent }, { busy: 'Drawing cover…' }); toast('Folder cover set', 'ok'); }, 'sm primary'))),
      h('div', { class: 'row', style: { marginTop: '10px' } },
        btn('Remove custom icon from folder', async () => { if (!fFolder) return toast('Choose a folder', 'err'); await api('icons.clearFolderIcon', { folder: fFolder }); toast('Removed', 'ok'); }, 'sm danger', IC.undo),
        btn('Rebuild icon cache', async () => { await api('icons.rebuildCache', {}, { busy: 'Rebuilding icon cache…' }); toast('Icon cache rebuilt', 'ok'); }, 'sm', IC.refresh),
        btn('Rebuild icon + thumbnail cache', async () => { await api('icons.rebuildCache', { thumbs: true }, { busy: 'Rebuilding caches…' }); toast('Caches rebuilt', 'ok'); }, 'sm', IC.refresh)));

    const importBtn = btn('Import icon pack folder', async () => { const f = await pickFolder('Folder with .ico files'); if (!f) return; const n = await promptBox('Pack name', 'Name', f.split('\\').pop()); if (!n) return; await api('icons.importPack', { folder: f, name: n }, { busy: 'Importing…' }); toast('Pack imported', 'ok'); go('icons'); }, 'sm', IC.down);

    el.append(bar,
      h('div', { class: 'row between' }, h('h3', { style: { margin: '4px' } }, 'Icon packs'), importBtn),
      h('div', { class: 'grid gauto' }, defCard, packs.map(packCard)),
      h('div', { class: 'grid g2' }, singles, driveCard),
      folderTools,
      h('h3', { style: { margin: '4px' } }, 'Thumbnails & desktop'),
      await tweaksPanel('icons', { noReset: true }));
  },
});
