registerView('dashboard', {
  title: 'Dashboard', subtitle: 'Your setup at a glance',
  async render(el) {
    const d = await api('dashboard.get');
    const s = d.system;
    const pal = d.preset?.palette;
    const hero = h('div', { class: 'card glass hov', style: { padding: '22px', background: pal ? `linear-gradient(120deg, ${pal.bg}cc, ${pal.surface}88 60%, transparent)` : null } },
      h('div', { class: 'row between' },
        h('div', null,
          h('div', { class: 'muted small' }, 'ACTIVE LOOK'),
          h('div', { class: 'big' }, d.preset ? d.preset.name : 'Windows (no preset)'),
          h('div', { class: 'row', style: { marginTop: '8px' } },
            d.customized.length ? d.customized.map((c) => h('span', { class: 'tag pink' }, c + ' customized')) : h('span', { class: 'tag' }, 'Pure preset'))),
        pal ? h('div', { class: 'palette-strip', style: { width: '220px' } }, ['bg', 'surface', 'accent', 'accent2', 'text'].map((k) => h('i', { style: { background: pal[k] } }))) : null),
      h('div', { class: 'row', style: { marginTop: '16px' } },
        btn('Browse themes', () => go('themes'), 'primary', IC.eye),
        btn('Change wallpaper', () => go('wallpapers'), '', IC.image),
        btn('Backup now', async () => { const r = await api('backup.create', { full: true, name: 'Quick backup' }, { busy: 'Creating full backup…' }); toast('Backup saved: ' + fileName(r.file), 'ok'); go('dashboard'); }, '', IC.save)));

    const rp = d.lastRestorePoint;
    const prot = card('Protection',
      h('div', { class: 'kv' },
        h('span', null, 'Last restore point'), h('span', null, rp ? `${fmtDate(rp.created)} · ${rp.description}` : h('span', { class: 'tag warn' }, 'none found')),
        h('span', null, 'Last backup'), h('span', null, d.lastBackup ? `${fmtDate(d.lastBackup.at)} (${ago(d.lastBackup.at)})` : h('span', { class: 'tag warn' }, 'none yet')),
        h('span', null, 'Secure Boot'), h('span', null, h('span', { class: 'tag ' + (d.secureBoot === 'On' ? 'on' : 'off') }, d.secureBoot)),
        h('span', null, 'Running as admin'), h('span', null, h('span', { class: 'tag ' + (d.admin ? 'on' : 'off') }, d.admin ? 'Yes' : 'No'))),
      h('div', { class: 'row', style: { marginTop: '14px' } },
        btn('Create restore point', async () => { await api('restore.create', {}, { busy: 'Creating restore point (can take a minute)…' }); toast('Restore point created', 'ok'); go('dashboard'); }, 'sm', IC.save),
        btn('Backup & Restore', () => go('backup'), 'sm', IC.folder)));

    const sys = card('System',
      h('div', { class: 'kv' },
        h('span', null, 'Windows'), h('span', null, `${s.os} ${s.version || ''}`),
        h('span', null, 'Build'), h('span', null, s.build),
        h('span', null, 'PC / user'), h('span', null, `${s.computer} / ${s.user}`),
        h('span', null, 'CPU'), h('span', null, s.cpu),
        h('span', null, 'GPU'), h('span', null, s.gpu),
        h('span', null, 'RAM'), h('span', null, s.ramGB + ' GB'),
        h('span', null, 'Uptime'), h('span', null, d.uptime)));

    const e = d.engine;
    const eng = card('Engine & memory',
      h('div', { class: 'kv' },
        h('span', null, 'Wallpaper engine'), h('span', null, h('span', { class: 'tag ' + (e.running ? 'on' : '') }, e.running ? `running · ${e.ramMB} MB` : 'not running (0 MB)')),
        h('span', null, 'Live wallpaper'), h('span', null, e.live ? 'on' : 'off'),
        h('span', null, 'Live Lock'), h('span', null, e.lock ? 'on' : 'off'),
        h('span', null, 'Slideshow'), h('span', null, e.slideshow ? 'on' : 'off'),
        h('span', null, 'This app backend'), h('span', null, d.backendMB + ' MB · exits when you close the window')),
      h('p', { class: 'muted small' }, 'Nothing from TRANSCRYPTO stays in memory after you close it, except the engine — and that only while a live wallpaper, slideshow or Live Lock is switched on.'));

    const quick = card('Quick actions', h('div', { class: 'row' },
      btn('Restart Explorer', async () => { await api('app.restartExplorer', {}, { busy: 'Restarting Explorer…' }); toast('Done', 'ok'); }, 'sm', IC.refresh),
      btn('Restore Windows 11 defaults', () => go('backup'), 'sm danger', IC.undo),
      btn('Sign out', async () => { if (await confirmBox('Sign out?', 'Save your work first.')) api('app.signOut'); }, 'sm'),
      btn('Open data folder', async () => { const i = await api('app.info'); api('app.openPath', { path: i.data }); }, 'sm', IC.folder)));

    el.append(hero, h('div', { class: 'grid g2' }, prot, eng, sys, quick));
  },
});
