/* "changes" from version.json: [{ type: fixed|new|improved, text }] */
const CHANGE_TAG = { fixed: ['Fixed', 'warn'], new: ['New', 'neon'], improved: ['Improved', 'on'] };
function changeList(changes) {
  if (!changes || !changes.length) return h('span');
  return h('div', { class: 'list', style: { margin: '6px 0' } }, changes.map((c) => {
    const [label, cls] = CHANGE_TAG[c.type] || [c.type || 'Change', ''];
    return h('div', { class: 'li' }, h('span', { class: 'tag ' + cls, style: { minWidth: '68px', justifyContent: 'center' } }, label), h('span', { class: 'grow' }, c.text));
  }));
}

async function finishUpdate(r) {
  if (r.nothing) { toast('Already up to date, nothing to copy', 'ok'); return; }
  await modal({ title: `Updated to v${r.version}`, wide: true,
    body: h('div', { class: 'stack' },
      changeList(r.changes),
      h('div', { class: 'muted small' }, `${r.changed.length} file(s) changed, ${r.added.length} new` + (r.engineRebuilt ? ' · engine rebuilt' : ''))),
    actions: [{ label: 'Restart TRANSCRYPTO now', value: true, cls: 'primary' }] });
  await api('app.reloadBackend'); busy(true, 'Restarting backend…'); setTimeout(() => location.reload(), 3500);
}

registerView('settings', {
  title: 'Settings', subtitle: 'Safety nets, updates, engine, appearance, logs',
  async render(el) {
    const [s, info, eng, history] = await Promise.all([api('settings.get'), api('app.info'), api('engine.status'), api('update.history')]);
    const historyBox = history.length ? h('details', { style: { marginTop: '8px' } }, h('summary', { class: 'muted small' }, `Update history (${history.length})`),
      history.map((u) => h('div', { style: { margin: '8px 0' } },
        h('b', null, `v${u.version}`), h('span', { class: 'muted small' }, `  from v${u.from} · ${fmtDate(u.installed)} · ${u.source}`),
        changeList(u.changes)))) : h('span');
    const save = async (vals) => { await api('settings.set', { values: vals }); Object.assign(s, vals); applyLook(s); toast('Saved', 'ok'); };

    const safety = card('Startup & safety',
      toggleRow('Start with Windows', 'At sign-in the engine brings back every live effect: taskbar glow & skin, Explorer colors, click ripple, live wallpaper, Live Lock, startup sound. Your settings are always saved either way.',
        s.startWithWindows !== false, async (x) => { await save({ startWithWindows: x }); await api('engine.start', {}, { quiet: true }).catch(() => {}); }),
      toggleRow('Create a System Restore point before big changes', 'Boot logo, system font, Windows defaults', !!s.autoRestorePoint, (x) => save({ autoRestorePoint: x })),
      toggleRow('Automatic backup before applying a preset', 'Last 8 automatic backups are kept', !!s.autoBackupBeforeApply, (x) => save({ autoBackupBeforeApply: x })));

    const repo = h('input', { type: 'text', value: s.updateRepo, style: { width: '300px' } });
    const branch = h('input', { type: 'text', value: s.updateBranch, style: { width: '110px' } });
    const upOut = h('div', { class: 'muted small' });
    const updates = card('Updates (push to GitHub → app updates itself)',
      h('p', { class: 'muted small' }, 'Put this folder in a GitHub repo, bump the number in version.json and push. The app compares it, downloads the repo zip, copies new files and rebuilds the engine only if TCWall.cs changed.'),
      h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Repo'), repo, h('span', { class: 'muted' }, 'Branch'), branch, btn('Save', () => save({ updateRepo: repo.value.trim(), updateBranch: branch.value.trim() || 'main' }), 'sm')),
      toggleRow('Check for updates on start', null, !!s.checkUpdatesOnStart, (x) => save({ checkUpdatesOnStart: x })),
      h('div', { class: 'row', style: { marginTop: '8px' } },
        btn('Check now', async () => {
          const r = await api('update.check', {}, { busy: 'Checking…' });
          if (!r.configured) { upOut.textContent = 'Set your GitHub repo first.'; return; }
          upOut.innerHTML = r.available ? `<b style="color:var(--neon)">v${esc(r.latest)} available</b> (you have v${esc(r.current)}) ${esc(r.notes || '')}` : `You are up to date (v${esc(r.current)}).`;
          if (r.available) upOut.append(changeList(r.changes), btn('Update now', async () => { finishUpdate(await api('update.apply', {}, { busy: 'Downloading update…' })); }, 'sm primary', IC.down));
        }, 'sm', IC.refresh),
        btn('Install update file…', async () => {
          const path = await api('app.pickFile', { title: 'Choose a TRANSCRYPTO update', filter: 'TRANSCRYPTO update (*.zip;*.tcupdate)|*.zip;*.tcupdate' });
          if (!path) return;
          const info = await api('update.inspectFile', { path }, { busy: 'Reading update…' });
          const nFiles = info.changed.length + info.added.length;
          const ok = await modal({ title: `Update v${info.version}` + (info.date ? ` · ${info.date}` : ''), wide: true,
            body: h('div', { class: 'stack' },
              h('div', { class: info.newer ? 'note neon' : 'note' }, info.newer ? `You have v${info.current}. This installs v${info.version}.` : `You already have v${info.current}. This file is v${info.version}; installing it replaces your files with the ones inside.`),
              info.notes ? h('p', null, info.notes) : null,
              changeList(info.changes),
              h('div', { class: 'muted small' }, nFiles ? `${info.changed.length} file(s) changed, ${info.added.length} new` + (info.changed.includes('engine\\TCWall.cs') || info.added.includes('engine\\TCWall.cs') ? ' · the engine will be rebuilt' : '') : 'Every file is already identical to what you have.')),
            actions: [{ label: 'Cancel', value: false }, { label: info.newer ? 'Install' : 'Install anyway', value: true, cls: 'primary' }] });
          if (ok && nFiles) finishUpdate(await api('update.installFile', { path }, { busy: 'Installing update…' }));
        }, 'sm', IC.down),
        btn('Reload backend', async () => { await api('app.reloadBackend'); busy(true, 'Restarting backend…'); setTimeout(() => location.reload(), 3500); }, 'sm', IC.refresh), upOut),
      h('p', { class: 'muted small' }, 'Manual update: one .zip with the app folders and version.json (make it with tools\\make-update.ps1). The app shows what changed before installing and keeps a history below.'),
      h('p', { class: 'muted small' }, 'Tweaking files yourself? UI files (ui\\…) need only Ctrl+R. PowerShell modules (modules\\…) need "Reload backend".'),
      historyBox);

    const engCard = card('Engine (TCWall)',
      h('div', { class: 'kv' }, h('span', null, 'Status'), h('span', null, eng.running ? `running · ${eng.ramMB} MB` : 'stopped'), h('span', null, 'Built'), h('span', null, eng.built ? 'yes' : 'no')),
      toggleRow('Show engine tray icon', 'Pause / Live Lock / Exit from the tray', eng.config.tray !== false, async (x) => { await api('engine.setConfig', { values: { tray: x } }); toast('Saved', 'ok'); }),
      h('div', { class: 'row', style: { marginTop: '10px' } },
        btn('Rebuild engine', async () => { const r = await api('engine.build', {}, { busy: 'Compiling…' }); modal({ title: r.ok ? 'Engine built' : 'Build failed', body: h('textarea', { rows: 10, style: { width: '100%' } }, r.output || 'OK'), actions: [{ label: 'Close' }] }); }, 'sm', IC.bolt),
        btn('Start', async () => { await api('engine.start'); toast('Engine synced', 'ok'); }, 'sm', IC.play),
        btn('Stop', async () => { await api('engine.stop'); toast('Engine stopped', 'ok'); }, 'sm danger')));

    const look = card('App appearance',
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Neon color')), colorField(s.neon, (x) => save({ neon: x }))),
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Second neon')), colorField(s.neon2, (x) => save({ neon2: x }))),
      toggleRow('Reduce motion', 'Stops the floating background blobs (saves a little GPU)', !!s.reduceMotion, (x) => save({ reduceMotion: x })));

    const logBox = h('textarea', { rows: 14, style: { width: '100%' }, readonly: true });
    const logs = card('Log', logBox, h('div', { class: 'row', style: { marginTop: '8px' } },
      btn('Refresh', async () => { logBox.value = await api('app.log'); logBox.scrollTop = logBox.scrollHeight; }, 'sm', IC.refresh),
      btn('Open app folder', () => api('app.openPath', { path: info.root }), 'sm', IC.folder),
      btn('Open data folder', () => api('app.openPath', { path: info.data }), 'sm', IC.folder)),
      h('p', { class: 'muted small mono' }, `v${info.version} · port ${info.port} · ${info.root}`));

    el.append(h('div', { class: 'grid g2' }, safety, look, updates, engCard), logs);
    logBox.value = await api('app.log'); logBox.scrollTop = logBox.scrollHeight;
  },
});
