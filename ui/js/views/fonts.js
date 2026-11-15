registerView('fonts', {
  title: 'Fonts', subtitle: 'Replace the Windows UI font · install fonts · text size',
  async render(el) {
    const [list, cur] = await Promise.all([api('fonts.list'), api('fonts.get')]);
    let chosen = cur.current;
    const pv = h('div', { class: 'fontpv', style: { fontFamily: `"${chosen}"` } }, 'The quick brown fox jumps over the lazy dog — 0123456789');
    const sample = h('input', { type: 'text', value: pv.textContent, style: { width: '100%' } });
    sample.addEventListener('input', () => { pv.textContent = sample.value; });
    const lst = h('div', { class: 'fontlist' });
    const q = h('input', { type: 'search', placeholder: `Search ${list.length} installed fonts…`, style: { width: '100%' } });
    const draw = () => {
      lst.innerHTML = '';
      const f = q.value.toLowerCase();
      list.filter((n) => !f || n.toLowerCase().includes(f)).slice(0, 400).forEach((n) => {
        lst.append(h('div', { class: n === chosen ? 'sel' : '', style: { fontFamily: `"${n}"` }, onclick: (e) => { chosen = n; pv.style.fontFamily = `"${n}"`; $$('div', lst).forEach((d) => d.classList.remove('sel')); e.currentTarget.classList.add('sel'); chosenLbl.textContent = n; } }, n));
      });
    };
    q.addEventListener('input', draw);
    const chosenLbl = h('b', null, chosen);

    el.append(
      h('div', { class: 'note warn' }, 'Swapping the system font replaces Segoe UI everywhere (menus, Explorer, Settings, title bars). Pick a clean, readable font — very decorative fonts can make dialogs hard to read. A restore point is created first, and you need to sign out to see it.'),
      h('div', { class: 'grid g2' },
        card('System UI font',
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Current:'), h('span', { class: 'tag neon' }, cur.current), h('span', { class: 'muted' }, '→'), chosenLbl),
          h('div', { style: { marginTop: '10px' } }, q), h('div', { style: { marginTop: '10px' } }, lst)),
        h('div', { class: 'stack' },
          card('Preview', pv, h('div', { style: { marginTop: '10px' } }, sample)),
          card('Apply',
            h('div', { class: 'row' },
              btn('Use this font', async () => {
                if (!(await confirmBox('Change system font?', `Windows will use <b>${esc(chosen)}</b> instead of Segoe UI after you sign out.`, 'Change'))) return;
                await api('fonts.set', { family: chosen }, { busy: 'Creating restore point & changing font…' });
                if (await confirmBox('Sign out now?', 'The new font shows after signing out.', 'Sign out')) api('app.signOut'); else toast('Saved — sign out later to see it', 'ok');
              }, 'primary', IC.check),
              btn('Back to Segoe UI', async () => { await api('fonts.set', { family: 'Segoe UI' }, { busy: 'Restoring Segoe UI…' }); toast('Restored — sign out to see it', 'ok'); go('fonts'); }, '', IC.undo),
              btn('Install font files…', async () => { const f = await pickFile(F_FONT, 'Install fonts', true); if (f && f.length) { const r = await api('fonts.install', { paths: f }, { busy: 'Installing…' }); toast('Installed ' + r.length + ' font(s)', 'ok'); go('fonts'); } }, '', IC.plus))))),
      await tweaksPanel('fonts'));
    draw();
  },
});
