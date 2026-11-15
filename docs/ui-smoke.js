// Dev-only: opens every Themer tab and the top menu (Help, Features, What's new, Updates, About) in headless Chrome
// against a side instance of the backend (core\server.ps1 -Test) and fails on any page error.
const { spawn } = require('child_process'), fs = require('fs'), path = require('path'), os = require('os');
const PORT = 47898, TOKEN = 'uismoke' + Date.now(), DBG = 9338, sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const ROOT = path.join(__dirname, '..'), SHOTS = path.join(os.tmpdir(), 'tcd', 'ui-shots');
const results = []; const rec = (name, ok, info = '') => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${info ? '  (' + info + ')' : ''}`); };

(async () => {
  fs.mkdirSync(SHOTS, { recursive: true });
  const srv = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(ROOT, 'core', 'server.ps1'), '-Port', String(PORT), '-Token', TOKEN, '-Test', '-NoBrowser'], { stdio: 'ignore' });
  for (let i = 0; i < 120; i++) { try { if ((await fetch(`http://127.0.0.1:${PORT}/alive`)).ok) break; } catch (e) { /* starting */ } await sleep(500); }
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, '--window-size=1380,880', `--user-data-dir=${path.join(os.tmpdir(), 'tcd', 'headless-sec-ui')}`, 'about:blank'], { stdio: 'ignore' });
  for (let i = 0; i < 60; i++) { try { if ((await fetch(`http://127.0.0.1:${DBG}/json/version`)).ok) break; } catch (e) { /* starting */ } await sleep(250); }
  const t = await (await fetch(`http://127.0.0.1:${DBG}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((ok) => { ws.onopen = ok; });
  let id = 0; const pend = new Map(); const errors = [];
  ws.onmessage = (m) => {
    const d = JSON.parse(m.data);
    if (d.id && pend.has(d.id)) { pend.get(d.id)(d.result); pend.delete(d.id); }
    if (d.method === 'Runtime.exceptionThrown') errors.push(d.params.exceptionDetails.exception ? d.params.exceptionDetails.exception.description : d.params.exceptionDetails.text);
    if (d.method === 'Runtime.consoleAPICalled' && d.params.type === 'error') errors.push(d.params.args.map((a) => a.value || a.description).join(' '));
  };
  const send = (method, params = {}) => new Promise((ok) => { const i = ++id; pend.set(i, ok); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async (expr) => { const r = await send('Runtime.evaluate', { expression: `(async () => { ${expr} })()`, awaitPromise: true, returnByValue: true }); return r.exceptionDetails ? { exc: JSON.stringify(r.exceptionDetails).slice(0, 300) } : r.result.value; };
  const shot = async (name) => { const r = await send('Page.captureScreenshot', { format: 'png' }); fs.writeFileSync(path.join(SHOTS, name + '.png'), Buffer.from(r.data, 'base64')); };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1380, height: 880, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: `http://127.0.0.1:${PORT}/?t=${TOKEN}` }); await sleep(3500);

  for (const v of ['dashboard', 'themes', 'wallpapers', 'colors', 'icons', 'cursors', 'sounds', 'startmenu', 'taskbar', 'explorer', 'lock', 'boot', 'fonts', 'backup', 'settings']) {
    const before = errors.length;
    await ev(`go('${v}');`); await sleep(1800);
    const r = await ev(`return { title: document.querySelector('#title').textContent, n: document.querySelector('#view').children.length, danger: [...document.querySelectorAll('#view .note.danger')].map((x) => x.textContent).join(' | ') };`);
    rec(`tab ${v} renders`, r && r.n > 0 && errors.length === before, `${r && r.title}, ${r && r.n} blocks${errors.length > before ? ', errors: ' + errors.slice(before).join(' / ').slice(0, 200) : ''}${r && r.danger ? ', note: ' + r.danger.slice(0, 120) : ''}`);
  }

  // top menu
  await ev(`go('dashboard');`); await sleep(1200);
  let r = await ev(`document.querySelector('#btnMenu').click(); await new Promise((o) => setTimeout(o, 300)); return [...document.querySelectorAll('#topMenu .tm-item')].map((x) => x.textContent);`);
  rec('menu opens with 6 entries', Array.isArray(r) && r.length === 6, JSON.stringify(r));
  await shot('menu');
  r = await ev(`document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })); return !document.querySelector('#topMenu');`);
  rec('Esc closes the menu', r === true);
  r = await ev(`document.querySelector('.top').dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true })); return !!document.querySelector('#topMenu');`);
  rec('right-click on the top bar opens the menu', r === true);
  await ev(`closeTopMenu();`);

  const openItem = (i) => ev(`openTopMenu(document.querySelector('#btnMenu')); document.querySelectorAll('#topMenu .tm-item')[${i}].click(); await new Promise((o) => setTimeout(o, 1500)); const m = document.querySelector('.modal'); return m ? { title: m.querySelector('h2').textContent, text: m.textContent.slice(0, 4000), secs: m.querySelectorAll('.kb-sec').length, rows: m.querySelectorAll('.kb-row').length, regs: m.querySelectorAll('.kb-reg').length, li: m.querySelectorAll('.li').length } : null;`);
  const closeModal = () => ev(`document.querySelectorAll('.modal-back').forEach((b) => b.remove());`);

  r = await openItem(0); rec('Help & guide', r && r.title === 'Help & guide' && /Troubleshooting/.test(r.text), r && r.title); await shot('help'); await closeModal();
  r = await openItem(1); rec('Features & knowledge base: every section, options and registry values', r && r.secs >= 15 && r.rows > 40 && r.regs > 20, r && `${r.secs} sections, ${r.rows} options, ${r.regs} registry values`);
  await ev(`document.querySelectorAll('.kb-sec')[1].open = true; document.querySelectorAll('.kb-sec')[6].open = true;`); await shot('features');
  r = await ev(`const q = document.querySelector('.modal input[type=search]'); q.value = 'HKLM'; q.dispatchEvent(new Event('input')); return [...document.querySelectorAll('.kb-sec')].filter((s) => s.style.display !== 'none').length;`);
  rec('knowledge base search filters', r > 0 && r < 15, r + ' sections match HKLM'); await closeModal();
  r = await openItem(2); rec("What's new lists this version's changes", r && /What's new in v/.test(r.title) && r.li > 10, r && `${r.title}, ${r.li} changes`); await shot('whatsnew'); await closeModal();
  r = await openItem(3); rec('Check for updates answers (online or not)', r && r.title === 'Updates', r && r.text.slice(7, 120)); await closeModal();
  r = await openItem(4); rec('About: made with love by SharmaJi', r && r.title === 'About' && /Made with .* by SharmaJi/.test(r.text) && /3 crazy custom shells/.test(r.text), r && r.title); await shot('about'); await closeModal();
  r = await ev(`document.dispatchEvent(new KeyboardEvent('keydown', { key: 'F1' })); await new Promise((o) => setTimeout(o, 400)); const m = document.querySelector('.modal h2'); return m && m.textContent;`);
  rec('F1 opens help', r === 'Help & guide', r); await closeModal();
  rec('no page errors at all', errors.length === 0, errors.slice(0, 3).join(' / ').slice(0, 300));

  ws.close(); chrome.kill();
  spawn('taskkill', ['/pid', String(srv.pid), '/t', '/f'], { stdio: 'ignore' });
  console.log(`\n${results.filter(Boolean).length}/${results.length} passed   (screenshots: ${SHOTS})`);
  setTimeout(() => process.exit(0), 500);
})();
