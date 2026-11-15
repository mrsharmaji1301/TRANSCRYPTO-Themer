// Dev-only: open a shell page headless and evaluate one expression (after optional setup), printing the JSON result.
// node docs/shell-probe.js <design> "<setup js>" "<expression js>"
const { spawn } = require('child_process'), path = require('path'), os = require('os');
const MOCK = 8793, DBG = 9335, sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const [design, setup, expr] = process.argv.slice(2);
(async () => {
  const mock = spawn(process.execPath, [path.join(__dirname, 'shell-mock.js')], { env: Object.assign({}, process.env, { MOCK_PORT: String(MOCK) }), stdio: 'ignore' });
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, `--user-data-dir=${path.join(os.tmpdir(), 'tcd', 'headless-probe')}`, '--window-size=1920,1080', 'about:blank'], { stdio: 'ignore' });
  for (let i = 0; i < 60; i++) { try { if ((await fetch(`http://127.0.0.1:${DBG}/json/version`)).ok) break; } catch (e) { /* wait */ } await sleep(250); }
  const t = await (await fetch(`http://127.0.0.1:${DBG}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl); await new Promise((ok) => { ws.onopen = ok; });
  let id = 0; const pend = new Map();
  ws.onmessage = (m) => { const d = JSON.parse(m.data); if (d.id && pend.has(d.id)) { pend.get(d.id)(d.result); pend.delete(d.id); } };
  const send = (method, params = {}) => new Promise((ok) => { const i = ++id; pend.set(i, ok); ws.send(JSON.stringify({ id: i, method, params })); });
  const ev = async (b) => { const r = await send('Runtime.evaluate', { expression: `(async () => { ${b} })()`, awaitPromise: true, returnByValue: true }); return r.exceptionDetails ? 'EXC ' + JSON.stringify(r.exceptionDetails).slice(0, 400) : r.result.value; };
  await send('Runtime.enable');
  await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
  await send('Page.navigate', { url: `http://127.0.0.1:${MOCK}/shell/${design}.html?t=x&pal=&ah=0` }); await sleep(5000);
  if (setup) await ev(setup);
  console.log(JSON.stringify(await ev(expr), null, 1));
  ws.close(); chrome.kill(); mock.kill(); process.exit(0);
})();
