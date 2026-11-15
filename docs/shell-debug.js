// Dev-only: reproduce "menu open after a bar click while open" in headless Chrome and log where the opening waits.
const { spawn } = require('child_process'), path = require('path'), os = require('os');
const MOCK = 8792, DBG = 9334, sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const design = process.argv[2] || 'orbital', appSel = { orbital: '.ico', sharmaji: '#apps .plate.app', projector: '#apps .app' }[design];
(async () => {
  const mock = spawn(process.execPath, [path.join(__dirname, 'shell-mock.js')], { env: Object.assign({}, process.env, { MOCK_PORT: String(MOCK) }), stdio: 'ignore' });
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, `--user-data-dir=${path.join(os.tmpdir(), 'tcd', 'headless-dbg')}`, '--window-size=1920,1080', 'about:blank'], { stdio: 'ignore' });
  for (let i = 0; i < 60; i++) { try { if ((await fetch(`http://127.0.0.1:${DBG}/json/version`)).ok) break; } catch (e) { /* wait */ } await sleep(250); }
  const t = await (await fetch(`http://127.0.0.1:${DBG}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((ok) => { ws.onopen = ok; });
  let id = 0; const pend = new Map();
  ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id && pend.has(d.id)) { pend.get(d.id)(d.result); pend.delete(d.id); }
    if (d.method === 'Runtime.consoleAPICalled') console.log('   page:', d.params.args.map((a) => a.value || a.description || '').join(' ').slice(0, 200)); };
  const send = (method, params = {}) => new Promise((ok) => { const i = ++id; pend.set(i, ok); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async (b) => { const r = await send('Runtime.evaluate', { expression: `(async () => { ${b} })()`, awaitPromise: true, returnByValue: true }); return r.exceptionDetails ? 'EXC ' + JSON.stringify(r.exceptionDetails).slice(0, 300) : r.result.value; };
  await send('Runtime.enable'); await send('Page.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: `http://127.0.0.1:${MOCK}/shell/${design}.html?t=x&pal=&ah=0` }); await sleep(5000);
  // trace every awaited helper the opening uses
  await ev(`Shell.poll = () => {};
    const T0 = performance.now(), log = (...a) => console.log('t+' + Math.round(performance.now() - T0), ...a);
    for (const k of ['act', 'useScreenshot', 'loadStart', 'api']) { const f = Shell[k]; Shell[k] = (...a) => { log('-> ' + k, JSON.stringify(a).slice(0, 80)); const p = f(...a); Promise.resolve(p).then(() => log('<- ' + k, JSON.stringify(a).slice(0, 60))); return p; }; }
    return 1;`);
  const step = async (label, body, ms) => { console.log('== ' + label); console.log('  ', await ev(body)); await sleep(ms); console.log('   state', await ev('return JSON.stringify({ open: Shell.open, busy: Shell.busy })')); };
  await step('open', 'Shell.onStart(); return 1', 4500);
  await step('bar click while open', `const e = [...document.querySelectorAll('${appSel}')].find((x) => x._app); e.dispatchEvent(new MouseEvent('mousedown', { bubbles: true })); e.click(); return e._app.name`, 3500);
  await step('open again', 'Shell.onStart(); return 1', 7000);
  ws.close(); chrome.kill(); mock.kill(); process.exit(0);
})();
