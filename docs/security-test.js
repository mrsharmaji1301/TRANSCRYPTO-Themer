// Dev-only: security checks against a side instance of the backend (core\server.ps1 -Test: no session file, no engine
// sync, no window, not elevated). Requests come from headless Chrome (a browser, allowed) and from Node (not a browser).
const { spawn } = require('child_process'), fs = require('fs'), path = require('path'), os = require('os');
const PORT = 47899, TOKEN = 'sectest' + Date.now(), DBG = 9336, sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const ROOT = path.join(__dirname, '..'), DATA = path.join(process.env.LOCALAPPDATA, 'TRANSCRYPTO');
const results = []; const rec = (name, ok, info = '') => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${info ? '  (' + info + ')' : ''}`); };

(async () => {
  const srv = spawn('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(ROOT, 'core', 'server.ps1'), '-Port', String(PORT), '-Token', TOKEN, '-Test', '-NoBrowser'], { stdio: 'ignore' });
  for (let i = 0; i < 120; i++) { try { if ((await fetch(`http://127.0.0.1:${PORT}/alive`)).ok) break; } catch (e) { /* starting */ } await sleep(500); }
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, `--user-data-dir=${path.join(os.tmpdir(), 'tcd', 'headless-sec')}`, 'about:blank'], { stdio: 'ignore' });
  for (let i = 0; i < 60; i++) { try { if ((await fetch(`http://127.0.0.1:${DBG}/json/version`)).ok) break; } catch (e) { /* starting */ } await sleep(250); }
  const t = await (await fetch(`http://127.0.0.1:${DBG}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((ok) => { ws.onopen = ok; });
  let id = 0; const pend = new Map();
  ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id && pend.has(d.id)) { pend.get(d.id)(d.result); pend.delete(d.id); } };
  const send = (method, params = {}) => new Promise((ok) => { const i = ++id; pend.set(i, ok); ws.send(JSON.stringify({ id: i, method, params })); });
  const inPage = async (body) => { const r = await send('Runtime.evaluate', { expression: `(async () => { ${body} })()`, awaitPromise: true, returnByValue: true }); return r.exceptionDetails ? { exc: JSON.stringify(r.exceptionDetails).slice(0, 300) } : r.result.value; };
  await send('Page.navigate', { url: `http://127.0.0.1:${PORT}/alive` }); await sleep(800);
  const api = (fn, args = {}) => inPage(`const r = await fetch('/api', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TC-Token': '${TOKEN}' }, body: JSON.stringify({ fn: ${JSON.stringify(fn)}, args: ${JSON.stringify(args)} }) }); return { status: r.status, body: await r.json() };`);
  const get = (url) => inPage(`const r = await fetch(${JSON.stringify(url)}); return { status: r.status, text: (await r.text()).slice(0, 80) };`);

  // ---- who may call
  let r = await api('app.info');
  rec('browser with the token can use the API', r.status === 200 && r.body.ok, 'admin=' + (r.body.data && r.body.data.admin));
  const nodeCall = await fetch(`http://127.0.0.1:${PORT}/api`, { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TC-Token': TOKEN }, body: JSON.stringify({ fn: 'app.info', args: {} }) });
  rec('non-browser program with the token is refused', nodeCall.status === 403, 'status ' + nodeCall.status);
  r = await inPage(`const r = await fetch('/api', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-TC-Token': 'wrong' }, body: '{"fn":"app.info"}' }); return r.status;`);
  rec('wrong token is refused', r === 403, 'status ' + r);

  // ---- files served / accepted
  r = await get(`/media?t=${TOKEN}&p=C:\\Windows\\win.ini`);
  rec('/media refuses non-media files', r.status === 403, 'status ' + r.status);
  const png = path.join(ROOT, 'assets', 'scenes', 'orbital-aurora.png');
  r = await get(`/media?t=${TOKEN}&p=${encodeURIComponent(png)}`);
  rec('/media still serves pictures', r.status === 200 || r.status === 206, 'status ' + r.status);
  r = await get(`/ui/..%5C..%5Cversion.json`);
  rec('static files: no escape from ui\\', r.status !== 200, 'status ' + r.status);
  r = await inPage(`const r = await fetch('/upload?name=evil.ps1', { method: 'POST', headers: { 'X-TC-Token': '${TOKEN}' }, body: 'Write-Host pwned' }); return r.status;`);
  rec('/upload refuses scripts', r === 400, 'status ' + r);
  r = await inPage(`const r = await fetch('/upload?name=sec-test-upload.png&dir=' + encodeURIComponent('C:\\\\Windows'), { method: 'POST', headers: { 'X-TC-Token': '${TOKEN}' }, body: 'x' }); return { s: r.status, b: await r.json() };`);
  const upPath = r.b && r.b.data;
  rec('/upload ignores a target folder (library only)', r.s === 200 && upPath && upPath.toLowerCase().startsWith(path.join(DATA, 'library').toLowerCase()), upPath);
  if (upPath) try { fs.unlinkSync(upPath); } catch (e) { /* gone */ }

  // ---- path locks in the APIs
  const bait = path.join(DATA, 'sec-test-bait.txt'); fs.writeFileSync(bait, 'keep me');
  await api('wallpapers.delete', { path: path.join(DATA, 'library', '..', 'sec-test-bait.txt') });
  rec('wallpapers.delete: "library\\..\\file" is not deleted', fs.existsSync(bait));
  r = await api('backup.delete', { file: bait });
  rec('backup.delete outside the backup folder is refused', fs.existsSync(bait) && r.body && r.body.ok === false, r.body && r.body.error);
  r = await api('wallpapers.rename', { path: bait, name: 'renamed' });
  rec('wallpapers.rename outside the library is refused', fs.existsSync(bait) && r.body.ok === false, r.body.error);
  r = await api('boot.restoreBcd', { file: bait });
  rec('boot.restoreBcd refuses foreign files', r.body.ok === false && /Only BCD backups/.test(r.body.error), r.body.error);
  r = await api('backup.importFile', { path: bait });
  rec('backup.importFile refuses non-backup files', r.body.ok === false, r.body.error);
  r = await api('themes.import', { path: bait });
  rec('themes.import refuses non-theme files', r.body.ok === false, r.body.error);
  r = await api('icons.setFolderIcon', { icon: 'C:\\Windows\\win.ini', folder: path.join(DATA, 'cache') });
  rec('icons.setFolderIcon refuses non-icons', r.body.ok === false, r.body.error);
  r = await api('lock.setImage', { image: 'C:\\Windows\\win.ini' });
  rec('lock.setImage refuses non-images', r.body.ok === false, r.body.error);
  fs.unlinkSync(bait);

  // ---- updates must be signed
  const unsigned = path.join(ROOT, 'dist', 'TRANSCRYPTO-Themer-2.1.0.zip');
  if (fs.existsSync(unsigned)) {
    r = await api('update.inspectFile', { path: unsigned });
    rec('unsigned update package is refused', r.body.ok === false && /not signed/i.test(r.body.error), r.body.error);
  }

  // ---- exploit attempts against the caller check (a program of the user that has read the token)
  // a. a non-browser program renamed to chrome.exe
  const fakeDir = path.join(os.tmpdir(), 'tcd', 'fake'); fs.mkdirSync(fakeDir, { recursive: true });
  const fake = path.join(fakeDir, 'chrome.exe'); try { fs.copyFileSync(process.execPath, fake); } catch (e) { /* in use */ }
  const fakeJs = `fetch('http://127.0.0.1:${PORT}/api',{method:'POST',headers:{'Content-Type':'application/json','X-TC-Token':'${TOKEN}','Origin':'http://127.0.0.1:${PORT}'},body:JSON.stringify({fn:'app.info',args:{}})}).then(r=>console.log(r.status))`;
  const fr = require('child_process').spawnSync(fake, ['-e', fakeJs], { encoding: 'utf8' });
  rec('renamed program (node as chrome.exe) is refused', String(fr.stdout).trim() === '403', 'status ' + String(fr.stdout).trim());
  // b. a page of another origin (file:// or data:) in an allowed browser: no-cors "simple" POST with the token in the URL
  await send('Page.navigate', { url: 'data:text/html,<p>x</p>' }); await sleep(500);
  r = await inPage(`
    const o = { method: 'POST', mode: 'no-cors', headers: { 'Content-Type': 'text/plain' } };
    await fetch('http://127.0.0.1:${PORT}/upload?t=${TOKEN}&name=nocors-test.png', { ...o, body: 'x' }).catch(() => {});
    await fetch('http://127.0.0.1:${PORT}/api?t=${TOKEN}', { ...o, body: JSON.stringify({ fn: 'app.quit', args: {} }) }).catch(() => {});
    let hdr = 'sent'; try { await fetch('http://127.0.0.1:${PORT}/api', { method: 'POST', headers: { 'X-TC-Token': '${TOKEN}' }, body: '{}' }); } catch (e) { hdr = 'blocked'; }
    return hdr;`);
  await sleep(1500);
  const alive = await fetch(`http://127.0.0.1:${PORT}/alive`).then((x) => x.ok).catch(() => false);
  rec('other-origin page: no-cors POST with the URL token does nothing', alive && !fs.existsSync(path.join(DATA, 'library', 'nocors-test.png')), 'server alive=' + alive);
  rec('other-origin page: the token header is blocked by CORS', r === 'blocked', r);
  // c. a browser under remote control (DevTools port) in another profile
  const evil = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', '--remote-debugging-port=9337', '--user-data-dir=' + path.join(os.tmpdir(), 'tcd', 'evil-prof'), `http://127.0.0.1:${PORT}/alive`], { stdio: 'ignore' });
  for (let i = 0; i < 60; i++) { try { if ((await fetch('http://127.0.0.1:9337/json/version')).ok) break; } catch (e) { /* starting */ } await sleep(250); }
  await sleep(1000);
  const et = (await (await fetch('http://127.0.0.1:9337/json')).json()).find((x) => x.type === 'page');
  const ews = new WebSocket(et.webSocketDebuggerUrl); await new Promise((ok) => { ews.onopen = ok; });
  const evilJs = `fetch('/api',{method:'POST',headers:{'Content-Type':'application/json','X-TC-Token':'${TOKEN}'},body:JSON.stringify({fn:'app.info',args:{}})}).then(r=>r.status)`;
  const er = await new Promise((ok) => { ews.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id === 1) ok(d.result.result.value); }; ews.send(JSON.stringify({ id: 1, method: 'Runtime.evaluate', params: { awaitPromise: true, returnByValue: true, expression: evilJs } })); });
  rec('browser with a DevTools port (remote control) is refused', er === 403, 'status ' + er);
  ews.close(); evil.kill();

  ws.close(); chrome.kill();
  spawn('taskkill', ['/pid', String(srv.pid), '/t', '/f'], { stdio: 'ignore' });
  console.log(`\n${results.filter(Boolean).length}/${results.length} passed`);
  setTimeout(() => process.exit(0), 500);
})();
