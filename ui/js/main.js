/* TRANSCRYPTO Themer — boot */
'use strict';

function applyLook(s) {
  if (!s) return;
  document.documentElement.style.setProperty('--neon', s.neon || '#00f0ff');
  document.documentElement.style.setProperty('--neon2', s.neon2 || '#ff2bd6');
  document.body.classList.toggle('reduce-motion', !!s.reduceMotion);
}

(async function start() {
  $('#btnReload').addEventListener('click', () => location.reload());
  $('#btnMenu').addEventListener('click', (e) => { if ($('#topMenu')) closeTopMenu(); else openTopMenu(e.currentTarget); });
  $('.top').addEventListener('contextmenu', (e) => { e.preventDefault(); openTopMenu($('#btnMenu')); });
  buildNav();
  try {
    const [s, info, themes] = await Promise.all([api('settings.get'), api('app.info'), api('themes.list')]);
    applyLook(s);
    TC.presetsCache = themes.presets;
    $('#ver').textContent = 'v' + info.version;
    if (!info.admin) toast('Not running as administrator — some tabs will not work. Start TRANSCRYPTO from its shortcut.', 'err');
    if (s.checkUpdatesOnStart) api('update.check', {}, { quiet: true }).then((r) => { if (r.available) toast(`Update v${r.latest} available — Settings → Updates`, 'ok'); }).catch(() => {});
  } catch (e) {
    $('#view').append(h('div', { class: 'note danger' }, 'Backend not reachable. Close this window and start TRANSCRYPTO again from its shortcut.'));
    return;
  }
  await refreshLayer();
  let first = location.hash.slice(1);
  if (!TC.views[first]) { try { first = localStorage.getItem('tc-last-tab') || ''; } catch (e) {} }
  go(TC.views[first] ? first : 'dashboard');
  setInterval(async () => { try { const d = await api('engine.status', {}, { quiet: true }); $('#ram').textContent = d.running ? `engine ${d.ramMB} MB` : 'engine off'; } catch (e) {} }, 15000);
  document.addEventListener('keydown', (e) => { if (e.ctrlKey && e.key.toLowerCase() === 'r') { e.preventDefault(); location.reload(); } });
})();
