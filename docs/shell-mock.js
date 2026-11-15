// Dev-only: serves shell/ and forwards read-only /api/* calls to the running engine, while /api/do and /api/events are stubbed,
// so the shell pages can be previewed in a normal browser tab without moving the real taskbar (node docs/shell-mock.js, port 8790)
const http = require('http'), fs = require('fs'), path = require('path');
const shellDir = path.join(__dirname, '..', 'shell');
const cfg = () => JSON.parse(fs.readFileSync(path.join(process.env.LOCALAPPDATA, 'TRANSCRYPTO', 'shell.json'), 'utf8'));
const types = { '.html': 'text/html', '.js': 'text/javascript', '.css': 'text/css', '.png': 'image/png', '.jpg': 'image/jpeg', '.mp3': 'audio/mpeg' };
let evSeq = 0; const waiting = [], actions = [];   // actions: every /api/do call, for the headless tests (GET /__actions, ?clear=1)
const handler = (req, res) => {
  const u = new URL(req.url, 'http://x');
  if (u.pathname.startsWith('/shell/')) {
    const file = path.join(shellDir, u.pathname.slice(7));
    if (!file.startsWith(shellDir)) { res.writeHead(403); return res.end(); }
    return fs.readFile(file, (e, d) => { if (e) { res.writeHead(404); return res.end(); } res.writeHead(200, { 'Content-Type': types[path.extname(file)] || 'application/octet-stream', 'Cache-Control': 'no-store' }); res.end(d); });
  }
  if (u.pathname === '/__actions') { const out = JSON.stringify(actions); if (u.searchParams.get('clear')) actions.length = 0; res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(out); }
  if (u.pathname === '/api/do') {
    const a = u.searchParams.get('a'); const rec = {}; u.searchParams.forEach((v, k) => { if (k !== 't') rec[k] = v; }); rec.at = Date.now(); actions.push(rec); if (actions.length > 2000) actions.shift();
    if (a === 'push') { evSeq++; const e = u.searchParams.get('e'); waiting.splice(0).forEach((w) => w(e)); }
    res.writeHead(200, { 'Content-Type': 'application/json' }); return res.end(JSON.stringify({ ok: true, result: a === 'menu' ? true : null }));
  }
  if (u.pathname === '/api/events') {
    const since = +u.searchParams.get('since');
    const send = (e) => { res.writeHead(200, { 'Content-Type': 'application/json' }); res.end(JSON.stringify({ seq: evSeq, events: e ? [e] : [] })); };
    if (since < 0) return send(null);
    const t = setTimeout(() => send(null), 20000); waiting.push((e) => { clearTimeout(t); send(e); }); return;
  }
  if (u.pathname === '/api/backdrop') { res.writeHead(404); return res.end(); }   // never a capture of the real screen
  if (u.pathname === '/api/tray') {   // hidden tray icons: two fake ones; clicks are recorded, never replayed on the real tray
    const op = u.searchParams.get('op');
    if (op === 'click') { const rec = { a: 'trayclick', at: Date.now() }; u.searchParams.forEach((v, k) => { if (k !== 't' && k !== 'op') rec[k] = v; }); actions.push(rec); }
    res.writeHead(200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ result: op === 'click' ? true : [{ i: 0, name: 'Discord', label: 'Discord', icon: '' }, { i: 1, name: 'Steam\nOnline', label: 'Steam', icon: '' }] }));
  }
  if (u.pathname === '/api/quick') {   // quick settings: two fake audio outputs (switching is recorded as an action)
    res.writeHead(200, { 'Content-Type': 'application/json' });
    return res.end(JSON.stringify({ net: 'ethernet', adapter: 'Ethernet', machine: 'MOCK', outputs: [{ id: 'dev-1', name: 'Speakers (Mock Audio)', default: true }, { id: 'dev-2', name: 'Headphones (Mock USB)', default: false }] }));
  }
  if (u.pathname.startsWith('/api/')) {
    const c = cfg(); u.searchParams.set('t', c.token);
    return http.get(`http://127.0.0.1:${c.port}${u.pathname}?${u.searchParams}`, (r) => { res.writeHead(r.statusCode, r.headers); r.pipe(res); }).on('error', () => { res.writeHead(502); res.end(); });
  }
  res.writeHead(404); res.end();
};
// loopback only, on both IPv4 and IPv6 (icons load through "localhost", the page through 127.0.0.1)
const PORT = +process.env.MOCK_PORT || 8790;
http.createServer(handler).listen(PORT, '127.0.0.1');
http.createServer(handler).listen(PORT, '::1');
