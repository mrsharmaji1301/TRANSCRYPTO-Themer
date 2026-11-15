registerView('lock', {
  title: 'Lock & Login', subtitle: 'Lock screen picture · TRANSCRYPTO Live Lock (video lock screen) · sign-in screen',
  async render(el) {
    const s = await api('lock.get');
    const L = Object.assign({}, s.live);

    const imgBox = h('div', { class: 'bootpv', style: { background: s.image ? `#000 url("${media(s.image)}") center/cover` : '#000' } });
    const staticCard = card('Lock screen picture',
      imgBox,
      h('div', { class: 'row', style: { marginTop: '12px' } },
        btn('From library', async () => { const f = await pickFromLibrary('image'); if (f) { await api('lock.setImage', { image: f }, { busy: 'Setting…' }); toast('Lock screen set', 'ok'); go('lock'); } }, 'sm', IC.image),
        btn('From file', async () => { const f = await pickFile(F_IMG, 'Lock screen picture'); if (f) { await api('lock.setImage', { image: f }, { busy: 'Setting…' }); toast('Lock screen set', 'ok'); go('lock'); } }, 'sm', IC.folder),
        btn('Frame from a video', async () => { const f = await pickFromLibrary('video'); if (f) { await api('lock.frameFromVideo', { video: f, apply: true }, { busy: 'Grabbing frame…' }); toast('Lock screen set', 'ok'); go('lock'); } }, 'sm', IC.play),
        s.image ? btn('Remove', async () => { await api('lock.clearImage'); go('lock'); }, 'sm danger', IC.undo) : null),
      h('p', { class: 'muted small' }, 'Also used as the sign-in screen background (unless turned off below).'));

    const vidLbl = h('b', null, fileName(L.file) || 'same as live wallpaper');
    const hk = h('input', { type: 'text', value: L.hotkey || 'Ctrl+Alt+L', style: { width: '150px' } });
    const save = async (extra = {}) => {
      Object.assign(L, extra, { hotkey: hk.value });
      const r = await api('lock.setLive', { values: L, restoreWindowsLock: extra.enabled === false }, { busy: 'Saving…' });
      toast(L.enabled ? (r.running ? 'Live Lock is on' : 'Saved — engine will start') : 'Live Lock off', 'ok');
    };
    const liveCard = card('TRANSCRYPTO Live Lock',
      h('div', { class: 'note neon' }, h('b', null, 'How it works: '), 'when you are idle (or press the hotkey) your live wallpaper takes over the whole screen with a neon clock. Press any key or click and you land straight on the real Windows password / PIN box. The Windows photo lock screen gets switched off for this.'),
      toggleRow('Enable Live Lock', 'Uses the engine (only runs while enabled)', !!L.enabled, (x) => save({ enabled: x })),
      L.enabled ? h('div', { class: 'note' + (s.noLockScreen ? ' neon' : ' danger') },
        s.noLockScreen
          ? 'Windows photo lock screen: skipped. After Live Lock you go straight to the Windows password / PIN box. Quick check: press Win+L, it should show the password box right away.'
          : 'Windows photo lock screen is back on (something reset the policy), so Live Lock stops on the Windows picture screen. Press Re-apply to fix it.',
        s.noLockScreen ? null : btn('Re-apply', async () => { await save({}); go('lock'); }, 'sm primary')) : null,
      h('p', { class: 'muted small' }, 'Your password is typed into Windows\' own sign-in box, never into TRANSCRYPTO.'),
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Video'), h('small', null, 'Pick any video from your library')), vidLbl,
        btn('Choose', async () => { const f = await pickFromLibrary('video'); if (f) { L.file = f; vidLbl.textContent = fileName(f); save(); } }, 'sm'),
        btn('', () => { L.file = ''; vidLbl.textContent = 'same as live wallpaper'; save(); }, 'sm icon', IC.undo)),
      selectRow('Start after being idle for', null, [0, 1, 2, 3, 5, 10, 15, 30].map((m) => ({ v: m, l: m ? m + ' min' : 'Only with hotkey' })), L.idleMinutes ?? 5, (x) => save({ idleMinutes: Number(x) })),
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Hotkey'), h('small', null, 'Like Ctrl+Alt+L or Ctrl+Shift+F12 (Win+L is reserved by Windows)')), hk, btn('Save', () => save(), 'sm')),
      toggleRow('Show clock', null, L.clock !== false, (x) => save({ clock: x })),
      toggleRow('24-hour clock', null, !!L.clock24, (x) => save({ clock24: x })),
      toggleRow('Show date', null, L.showDate !== false, (x) => save({ showDate: x })),
      toggleRow('Don\'t start over fullscreen apps / games', null, L.skipWhenFullscreen !== false, (x) => save({ skipWhenFullscreen: x })),
      h('div', { class: 'tw' }, h('div', { class: 't' }, h('b', null, 'Clock glow color')), colorField(L.accent || '#00f0ff', (x) => save({ accent: x }))),
      h('div', { class: 'row', style: { marginTop: '10px' } }, btn('Test Live Lock now', async () => { await api('engine.lockNow'); }, 'primary', IC.lock)));

    el.append(h('div', { class: 'grid g2' }, staticCard, liveCard), await tweaksPanel('lock'));
  },
});
