registerView('backup', {
  title: 'Backup & Restore', subtitle: 'Selective / full backups · Windows 11 defaults · System Restore points',
  async render(el) {
    const [comps, backups, rps] = await Promise.all([api('backup.components'), api('backup.list'), api('restore.list', {}, { busy: 'Reading restore points…' })]);
    const checks = (prefix) => {
      const boxes = comps.map((c) => { const i = h('input', { type: 'checkbox', value: c.id }); i.checked = true; return { c, i }; });
      const wrap = h('div', { class: 'row' }, boxes.map(({ c, i }) => h('label', { class: 'check' }, i, c.label)));
      return { wrap, get: () => boxes.filter((b) => b.i.checked).map((b) => b.c.id), all: (v) => boxes.forEach((b) => { b.i.checked = v; }) };
    };

    // restore points
    const last = rps[0];
    const rpCard = card('System Restore points',
      h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Last restore point:'), last ? h('b', null, `${fmtDate(last.created)}`) : h('span', { class: 'tag warn' }, 'none'), last ? h('span', { class: 'muted' }, `· ${last.description} (${ago(last.created)})`) : null),
      h('div', { class: 'list', style: { marginTop: '10px', maxHeight: '220px', overflow: 'auto' } }, rps.slice(0, 15).map((r) => h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, r.description), h('small', null, fmtDate(r.created) + ' · #' + r.seq))))),
      h('div', { class: 'row', style: { marginTop: '10px' } },
        btn('Create restore point now', async () => { const d = await promptBox('Create restore point', 'Description', 'TRANSCRYPTO manual restore point'); if (d === null) return; await api('restore.create', { description: d }, { busy: 'Creating restore point… (can take a minute)' }); toast('Restore point created', 'ok'); go('backup'); }, 'primary', IC.save),
        btn('Open System Restore', () => api('restore.openUI'), '', IC.undo)),
      h('p', { class: 'muted small' }, 'Automatic restore points are made before big changes (boot logo, font, Windows defaults) — switch that in Settings.'));

    // create
    const ck = checks('bk');
    const nameIn = h('input', { type: 'text', placeholder: 'Backup name (optional)', style: { width: '260px' } });
    const incFiles = h('input', { type: 'checkbox' }); incFiles.checked = true;
    const incVid = h('input', { type: 'checkbox' });
    const create = card('Create backup',
      h('div', { class: 'row' }, nameIn, btn('All', () => ck.all(true), 'sm ghost'), btn('None', () => ck.all(false), 'sm ghost')),
      h('div', { style: { marginTop: '10px' } }, ck.wrap),
      h('div', { class: 'row', style: { marginTop: '10px' } }, h('label', { class: 'check' }, incFiles, 'Include pictures, icons, cursors & sounds used'), h('label', { class: 'check' }, incVid, 'Include live wallpaper videos (big)')),
      h('div', { class: 'row', style: { marginTop: '12px' } },
        btn('Selective backup', async () => { const c = ck.get(); if (!c.length) return toast('Choose at least one part', 'err'); const r = await api('backup.create', { components: c, name: nameIn.value, includeFiles: incFiles.checked, includeVideos: incVid.checked }, { busy: 'Backing up…' }); toast('Saved ' + fileName(r.file), 'ok'); go('backup'); }, '', IC.save),
        btn('Full backup', async () => { const r = await api('backup.create', { full: true, name: nameIn.value || 'Full backup', includeFiles: incFiles.checked, includeVideos: incVid.checked }, { busy: 'Full backup…' }); toast('Saved ' + fileName(r.file), 'ok'); go('backup'); }, 'primary', IC.save)));

    // list
    const restoreFrom = async (b) => {
      const boxes = (b.components || []).map((c) => { const i = h('input', { type: 'checkbox', value: c }); i.checked = true; return i; });
      const label = (id) => (comps.find((x) => x.id === id) || {}).label || id;
      const ok = await modal({ title: 'Restore ' + b.name, body: h('div', null, h('p', { class: 'muted' }, 'Choose what to restore. A safety backup of the current state is made first.'), h('div', { class: 'row' }, boxes.map((i) => h('label', { class: 'check' }, i, label(i.value))))),
        actions: [{ label: 'Cancel', value: false }, { label: 'Restore selected', value: true, cls: 'primary' }] });
      if (!ok) return;
      const sel = boxes.filter((i) => i.checked).map((i) => i.value);
      if (!sel.length) return;
      await api('backup.restore', { file: b.file, components: sel }, { busy: 'Restoring…' });
      toast('Restored', 'ok'); refreshLayer(); go('backup');
    };
    const listCard = card('Backups',
      h('div', { class: 'row' },
        btn('Import backup file', async () => { const f = await pickFile('TRANSCRYPTO backup|*.tcbackup', 'Import backup'); if (f) { await api('backup.importFile', { path: f }); go('backup'); } }, 'sm', IC.down),
        btn('Open backups folder', async () => api('app.openPath', { path: await api('backup.folder') }), 'sm', IC.folder)),
      backups.length ? h('div', { class: 'list', style: { marginTop: '8px' } }, backups.map((b) => h('div', { class: 'li' },
        h('div', { class: 'grow' }, h('b', null, b.name, ' ', b.auto ? h('span', { class: 'tag' }, 'auto') : null), h('small', null, `${fmtDate(b.created)} · ${b.sizeKB} KB · ${(b.components || []).length} parts`)),
        btn('Restore', () => restoreFrom(b), 'sm primary', IC.undo),
        btn('', () => api('app.openPath', { path: b.file }), 'sm icon', IC.folder),
        btn('', async () => { if (await confirmBox('Delete backup?', esc(b.name), 'Delete', 'danger')) { await api('backup.delete', { file: b.file }); go('backup'); } }, 'sm icon danger', IC.trash)))) : h('div', { class: 'empty', style: { marginTop: '10px' } }, 'No backups yet.'));

    // defaults
    const dk = checks('df');
    dk.all(false);
    const defaults = card('Restore Windows 11 defaults',
      h('p', { class: 'muted small' }, 'Puts the selected parts back exactly how Windows 11 ships. A restore point (if enabled) and an automatic backup are made first, so you can undo this.'),
      dk.wrap,
      h('div', { class: 'row', style: { marginTop: '12px' } },
        btn('Restore selected to default', async () => { const c = dk.get(); if (!c.length) return toast('Choose at least one part', 'err'); if (!(await confirmBox('Restore defaults?', 'Reset: <b>' + c.map((x) => esc((comps.find((y) => y.id === x) || {}).label)).join(', ') + '</b>', 'Reset', 'danger'))) return; await api('backup.defaults', { components: c }, { busy: 'Restoring Windows defaults…' }); toast('Defaults restored', 'ok'); refreshLayer(); go('backup'); }, 'danger', IC.undo),
        btn('Restore EVERYTHING to Windows 11 default', async () => { if (!(await confirmBox('Full Windows 11 reset?', 'Every TRANSCRYPTO-controlled setting goes back to stock Windows 11 (boot logo is removed separately in Boot Screen).', 'Reset everything', 'danger'))) return; await api('backup.defaults', { all: true }, { busy: 'Restoring everything…' }); toast('Windows 11 defaults restored', 'ok'); refreshLayer(); go('backup'); }, 'danger', IC.undo)));

    el.append(h('div', { class: 'grid g2' }, rpCard, create), listCard, defaults);
  },
});
