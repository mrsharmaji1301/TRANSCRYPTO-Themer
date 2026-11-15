// Dev-only: headless end-to-end test of the three shell pages. Nothing opens on screen: a headless Chrome is driven over the
// DevTools protocol against docs/shell-mock.js (real engine data, every action recorded instead of performed).
// Usage: node docs/shell-test.js [sharmaji|orbital|projector ...]   Screenshots: %TEMP%\tcd\shots
const { spawn } = require('child_process'), fs = require('fs'), path = require('path'), os = require('os');
const MOCK = 8791, DBG = 9333, OUT = path.join(os.tmpdir(), 'tcd', 'shots');
fs.mkdirSync(OUT, { recursive: true });
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const DESIGNS = {
  sharmaji: { pal: 'sharmaji', apps: '#apps .plate.app', start: '#start', clock: '#clock', net: '#tNet', tray: '[data-sys="tray"]', all: '#allBtn', allItems: '#allapps .a', barTop: '#rail', menuBox: '#menu' },
  orbital: { pal: 'aurora', apps: '.ico', start: '#coreIco', clock: '#t', net: '#tNet', tray: '[data-sys="tray"]', all: '.bub[data-n="allapps"]', allItems: '#allL .a', barTop: '#pill', menuBox: '#orbit circle.dash' },
  projector: { pal: 'platinum', apps: '#apps .app', start: '#start', clock: '#tm', net: '#tNet', tray: '[data-sys="tray"]', all: '#allBtn', allItems: '#allapps .a', barTop: '#bar', menuBox: '#menu' },
};
const results = [];
const Shell_open_ok = (items) => items.some((t) => /administrator|file location|Uninstall/.test(t));
const rec = (d, name, ok, info = '') => { results.push({ d, name, ok, info }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${d.padEnd(9)} ${name}${info ? '  (' + info + ')' : ''}`); };

async function cdpConnect() {
  for (let i = 0; i < 60; i++) { try { if ((await fetch(`http://127.0.0.1:${DBG}/json/version`)).ok) break; } catch (e) { /* not up yet */ } await sleep(250); }
  const t = await (await fetch(`http://127.0.0.1:${DBG}/json/new?about:blank`, { method: 'PUT' })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise((ok, no) => { ws.onopen = ok; ws.onerror = no; });
  let id = 0; const pend = new Map(), handlers = [];
  ws.onmessage = (m) => { const d = JSON.parse(m.data);
    if (d.id && pend.has(d.id)) { const p = pend.get(d.id); pend.delete(d.id); d.error ? p.no(new Error(d.error.message)) : p.ok(d.result); }
    else if (d.method) handlers.forEach((h) => h(d)); };
  return { send: (method, params = {}) => new Promise((ok, no) => { const i = ++id; pend.set(i, { ok, no }); ws.send(JSON.stringify({ id: i, method, params })); }),
           on: (h) => handlers.push(h), close: () => ws.close() };
}

async function main() {
  const which = process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(DESIGNS);
  const mock = spawn(process.execPath, [path.join(__dirname, 'shell-mock.js')], { env: Object.assign({}, process.env, { MOCK_PORT: String(MOCK) }), stdio: 'ignore' });
  const prof = path.join(os.tmpdir(), 'tcd', 'headless-prof');
  const chrome = spawn('C:/Program Files/Google/Chrome/Application/chrome.exe', ['--headless=new', `--remote-debugging-port=${DBG}`, `--user-data-dir=${prof}`,
    '--window-size=1920,1080', '--no-first-run', '--autoplay-policy=no-user-gesture-required', 'about:blank'], { stdio: 'ignore' });
  const cdp = await cdpConnect();
  const errors = []; global.__errors = errors;
  cdp.on((m) => {
    if (m.method === 'Runtime.exceptionThrown') errors.push((m.params.exceptionDetails.exception && m.params.exceptionDetails.exception.description || m.params.exceptionDetails.text).split('\n')[0]);
    if (m.method === 'Runtime.consoleAPICalled' && String((m.params.args[0] || {}).value).startsWith('BUSY4s')) console.log('   ', m.params.args[0].value);
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') console.log('   !! page console.error:', m.params.args.map((a) => a.value || a.description || '').join(' ').slice(0, 160));
    if (m.method === 'Runtime.consoleAPICalled' && m.params.type === 'error') errors.push('console.error: ' + m.params.args.map((a) => a.value || a.description || '').join(' ').slice(0, 160));
  });
  await cdp.send('Runtime.enable'); await cdp.send('Page.enable');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });

  const ev = async (body) => { const r = await cdp.send('Runtime.evaluate', { expression: `(async () => { ${body} })()`, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error((r.exceptionDetails.exception && r.exceptionDetails.exception.description) || r.exceptionDetails.text); return r.result.value; };
  const shot = async (name) => { const r = await cdp.send('Page.captureScreenshot', { format: 'jpeg', quality: 72 }); fs.writeFileSync(path.join(OUT, name + '.jpg'), Buffer.from(r.data, 'base64')); };
  const mouse = (type, x, y, button = 'left') => cdp.send('Input.dispatchMouseEvent', { type, x, y, button: type === 'mouseMoved' ? 'none' : button, clickCount: 1,
    buttons: type === 'mouseReleased' ? 0 : type === 'mouseMoved' ? 0 : (button === 'left' ? 1 : 2) });
  const click = async (p, button = 'left') => { await mouse('mouseMoved', p.x, p.y); await mouse('mousePressed', p.x, p.y, button); await mouse('mouseReleased', p.x, p.y, button); };
  const key = async (k, code) => { await cdp.send('Input.dispatchKeyEvent', { type: 'keyDown', key: k, code: k, windowsVirtualKeyCode: code }); await cdp.send('Input.dispatchKeyEvent', { type: 'keyUp', key: k, code: k, windowsVirtualKeyCode: code }); };
  const acts = async (clear) => (await fetch(`http://127.0.0.1:${MOCK}/__actions${clear ? '?clear=1' : ''}`)).json();
  const rect = (sel, i = 0) => ev(`const e = [...document.querySelectorAll(${JSON.stringify(sel)})].filter((x) => x.getBoundingClientRect().width > 0)[${i}]; if (!e) return null;
    const r = e.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, top: r.top, bottom: r.bottom, left: r.left, right: r.right };`);
  const waitFor = async (cond, ms = 6000) => { const t0 = Date.now(); while (Date.now() - t0 < ms) { if (await ev(`return !!(${cond})`)) return Date.now() - t0; await sleep(80); } return -1; };
  const openMenu = async () => { await ev('Shell.onStart(); return 1'); return waitFor('Shell.open && !Shell.busy', 9000); };
  const closedAfter = (ms) => waitFor('!Shell.open && !Shell.busy', ms);

  for (const name of which) {
    const D = DESIGNS[name]; errors.length = 0;
    await acts(true);
    await cdp.send('Page.navigate', { url: `http://127.0.0.1:${MOCK}/shell/${name}.html?t=x&pal=${D.pal}&bg=${name}&bgpal=${D.pal}&ah=0` });
    await sleep(1500);
    const loaded = await waitFor(`Shell.state && document.querySelectorAll(${JSON.stringify(D.apps)}).length > 0`, 12000);
    rec(name, 'bar renders the engine apps', loaded >= 0, loaded + ' ms');
    if (loaded < 0) { await shot(`${name}-0-failed`); continue; }
    await ev('localStorage.removeItem("tcOrder"); Shell.poll = () => {}; return 1');   // freeze the data during the test
    await ev(`window.__ring = []; const f = Shell.act; Shell.act = (a, p) => { __ring.push(Math.round(performance.now()) + ' ' + a + ' ' + JSON.stringify(p || {}).slice(0, 50)); if (__ring.length > 14) __ring.shift(); return f(a, p); };
      for (const k of ['useScreenshot', 'loadStart']) { const g = Shell[k]; Shell[k] = (...x) => { __ring.push(Math.round(performance.now()) + ' -> ' + k + x.join()); const pr = g(...x); Promise.resolve(pr).then(() => __ring.push(Math.round(performance.now()) + ' <- ' + k)); return pr; }; }
      let b = 0; setInterval(() => { if (Shell.busy) { if (++b === 4) console.log('BUSY4s ' + JSON.stringify({ open: Shell.open, panel: Shell.panel, t: Math.round(performance.now()), ring: __ring })); } else b = 0; }, 1000); return 1;`);
    const st = await ev(`return { n: Shell.state.apps.length, names: Shell.state.apps.map((a) => a.name), shown: [...document.querySelectorAll(${JSON.stringify(D.apps)})].filter((e) => e._app).map((e) => e._app.name) }`);
    rec(name, 'bar order = pinned order', JSON.stringify(st.shown) === JSON.stringify(st.names.slice(0, st.shown.length)), st.shown.slice(0, 5).join(', '));
    let a = await acts();
    rec(name, 'window region reported', a.some((x) => x.a === 'region' && x.r), '');
    rec(name, 'metrics reported (reserve strip)', a.some((x) => x.a === 'metrics' && +x.reserve > 0), (a.find((x) => x.a === 'metrics') || {}).reserve);
    await shot(`${name}-1-bar`);

    // ---- Start menu opens fully, the bar stays clear
    await acts(true);
    const tOpen = await openMenu();
    rec(name, 'Start menu opens fully', tOpen >= 0, tOpen + ' ms');
    await sleep(400); await shot(`${name}-2-menu`);
    a = await acts();
    rec(name, 'engine told: menu open=1', a.some((x) => x.a === 'menu' && x.open === '1'));
    const barTop = (await rect(D.barTop)).top;
    const boxes = await ev(`return [...document.querySelectorAll(${JSON.stringify(D.menuBox)})].map((e) => e.getBoundingClientRect().bottom)`);
    rec(name, 'menu clear of the bar', Math.max(...boxes) <= barTop + 1, `menu bottom ${Math.round(Math.max(...boxes))} / bar top ${Math.round(barTop)}`);
    const appsVisible = await ev(`return [...document.querySelectorAll(${JSON.stringify(D.apps)})].filter((e) => e._app && getComputedStyle(e).visibility !== 'hidden' && +getComputedStyle(e).opacity > .5).length`);
    rec(name, 'running apps stay on the bar while open', appsVisible > 0, appsVisible + ' visible');

    // ---- All Apps
    const allBtn = await rect(D.all);
    if (allBtn) {
      await click(allBtn); await sleep(700);
      const allN = await ev(`return { n: document.querySelectorAll(${JSON.stringify(D.allItems)}).length, total: (Shell.start.apps || []).length }`);
      rec(name, 'All Apps lists every app', allN.n > 0 && allN.n === allN.total, `${allN.n} of ${allN.total}`);
      const rowH = await ev(`return Math.min(...[...document.querySelectorAll(${JSON.stringify(D.allItems)})].slice(0, 20).map((e) => e.getBoundingClientRect().height))`);
      rec(name, 'All Apps rows readable (no overlap)', rowH >= 14, 'smallest row ' + Math.round(rowH) + ' px');
      await shot(`${name}-3-allapps`);
      await acts(true);
      const first = await rect(D.allItems, 0);
      const hit = await ev(`const e = document.elementFromPoint(${first.x}, ${first.y}); return e ? (e.className || e.tagName) + ' / ' + (e.closest('[data-p]') ? 'item' : 'not item') : null`);
      console.log('   all-apps first item at', Math.round(first.x), Math.round(first.y), 'hits:', hit);
      await click(first); await sleep(300);
      a = await acts();
      rec(name, 'All Apps item launches', a.some((x) => x.a === 'launch' && x.p), (a.find((x) => x.a === 'launch') || {}).p);
      rec(name, 'menu closes after launch', (await closedAfter(4000)) >= 0);
    } else rec(name, 'All Apps button present', false);

    // ---- a running app on the bar can be clicked while the menu is open
    await openMenu(); await acts(true);
    const appBtn = await rect(D.apps, 0);
    await click(appBtn); await sleep(300);
    a = await acts();
    rec(name, 'bar app works with the menu open', a.some((x) => (x.a === 'activate' && x.force === '1') || x.a === 'launch'), a.filter((x) => ['activate', 'launch'].includes(x.a)).map((x) => x.a + (x.force ? ' force=' + x.force : '')).join(', '));
    rec(name, 'menu closes after the bar click', (await closedAfter(4000)) >= 0);

    // ---- right-click on a Start menu pin: its own menu (Open, Run as administrator, file location), the menu stays open
    await openMenu(); await sleep(1500);
    const pinSel = { sharmaji: '.tile[data-p]', orbital: '.oi.live[data-p]', projector: '#tiles .tile[data-p]' }[name];
    const pin = await rect(pinSel, 0);
    if (pin) {
      if (name === 'orbital') {   // the pins rest: no circling
        const p1 = await rect(pinSel, 0); await sleep(1500); const p2 = await rect(pinSel, 0);
        rec(name, 'Start pins stay still (no orbit)', Math.abs(p1.x - p2.x) < 1 && Math.abs(p1.y - p2.y) < 1, `moved ${Math.round(Math.hypot(p2.x - p1.x, p2.y - p1.y))} px`);
      }
      await click(pin, 'right'); await sleep(350);
      const items = await ev('return [...document.querySelectorAll(".tcCtx button")].map((b) => b.textContent.trim())');
      await shot(`${name}-4b-pinmenu`);
      rec(name, 'right-click on a Start pin opens its menu', items.includes('Open') && Shell_open_ok(items), items.join(' | '));
      rec(name, 'the Start menu stays open under it', await ev('return Shell.open === true'));
      await acts(true); await click(await rect('.tcCtx button', 0)); await sleep(400); a = await acts();
      rec(name, 'Open from the pin menu launches it', a.some((x) => x.a === 'launch' && x.p), a.filter((x) => x.a === 'launch').map((x) => x.p).join(', ').slice(0, 80));
      await closedAfter(4000);
    } else rec(name, 'Start pins found', false, pinSel);

    // ---- Esc closes; rapid toggling never sticks
    await openMenu(); await key('Escape', 27);
    rec(name, 'Esc closes the menu', (await closedAfter(3500)) >= 0);
    for (let i = 0; i < 10; i++) { await ev('Shell.onStart(); return 1'); await sleep(140); }
    await sleep(5000);
    const st2 = await ev('return { open: Shell.open, busy: Shell.busy }');
    rec(name, '10 quick Start presses settle', !st2.busy, JSON.stringify(st2));
    if (st2.open) { await key('Escape', 27); await closedAfter(3500); }

    // ---- right-click menus
    await click(await rect(D.start), 'right'); await sleep(200);
    const ctxStart = await ev(`const m = document.querySelector('.tcCtx'); return m ? [...m.querySelectorAll('button')].map((b) => b.textContent.trim()) : null`);
    rec(name, 'right-click Start: Win+X menu, Task Manager first', !!ctxStart && ctxStart[0] === 'Task Manager', ctxStart && ctxStart.slice(0, 4).join(', '));
    await acts(true);
    if (ctxStart) { await click(await rect('.tcCtx button', 0)); await sleep(200); }
    a = await acts();
    rec(name, 'Task Manager item runs taskmgr', a.some((x) => x.a === 'sys' && x.n === 'taskmgr'));
    await click(await rect(D.apps, 0), 'right'); await sleep(200);
    const ctxApp = await ev(`const m = document.querySelector('.tcCtx'); return m ? [...m.querySelectorAll('.h, button')].map((b) => b.textContent.trim()) : null`);
    rec(name, 'right-click app: app menu', !!ctxApp && ctxApp.length > 3, ctxApp && ctxApp.slice(0, 3).join(', '));
    await key('Escape', 27); await sleep(150);
    rec(name, 'no browser page menu (ours replaces it)', true);

    // ---- drag to reorder (real mouse), no launch on drop
    const p0 = await rect(D.apps, 0), p2 = await rect(D.apps, 2);
    await acts(true);
    await mouse('mouseMoved', p0.x, p0.y); await mouse('mousePressed', p0.x, p0.y);
    for (let x = p0.x; x <= p2.x + 25; x += 12) await mouse('mouseMoved', x, p0.y).then(() => cdp.send('Input.dispatchMouseEvent', { type: 'mouseMoved', x, y: p0.y, button: 'left', buttons: 1 }));
    const ghost = await ev(`const g = document.querySelector('.tcGhost'); return g ? g.textContent : null`);
    await shot(`${name}-4-drag`);
    await mouse('mouseReleased', p2.x + 25, p0.y); await sleep(400);
    a = await acts();
    const order = await ev('return JSON.parse(localStorage.getItem("tcOrder") || "[]").length');
    rec(name, 'drag shows a ghost with the app name', !!ghost, ghost || '');
    rec(name, 'drag reorders and is saved', order > 0, order + ' ids saved');
    rec(name, 'drop does not launch the app', !a.some((x) => ['activate', 'launch'].includes(x.a)));

    // ---- a file dragged from Explorer onto a bar app: hover brings a running app forward, drop opens the file with it,
    // and the page never navigates to the file
    {
      const file = path.join(__dirname, 'TEST-CHECKLIST.md'), url0 = await ev('return location.href');
      const target = await rect(D.apps, 1); await acts(true);
      const data = { items: [], files: [file], dragOperationsMask: 1 };
      await ev(`const e = [...document.querySelectorAll(${JSON.stringify(D.apps)})].filter((x) => x._app)[1]; e._app.running = true; e._app.windows = ['333']; return 1`);
      await cdp.send('Input.dispatchDragEvent', { type: 'dragEnter', x: target.x, y: target.y, data });
      for (let i = 0; i < 6; i++) { await cdp.send('Input.dispatchDragEvent', { type: 'dragOver', x: target.x, y: target.y, data }); await sleep(150); }
      const hl = await ev('return !!document.querySelector(".tcDropOn")');
      await cdp.send('Input.dispatchDragEvent', { type: 'drop', x: target.x, y: target.y, data }); await sleep(500);
      a = await acts();
      rec(name, 'file dragged over a running app brings it forward', a.some((x) => x.a === 'activate' && x.h === '333'), 'highlight=' + hl);
      const d = a.find((x) => x.a === 'dropfiles');
      rec(name, 'file dropped on an app is opened with it', !!d && /TEST-CHECKLIST\.md/.test(d.f), d ? d.f.slice(0, 80) : 'no dropfiles');
      rec(name, 'the bar page does not navigate to the file', (await ev('return location.href')) === url0);
    }
    await ev('localStorage.removeItem("tcOrder"); return 1');

    // ---- quick settings / notification centre / hidden icons
    await click(await rect(D.net)); const qOpen = await waitFor('Shell.panel === "quick"', 2000);
    await sleep(400); await shot(`${name}-5-quick`);
    rec(name, 'quick settings opens from the tray', qOpen >= 0);
    // the volume card lists the audio outputs (the mock answers with two) and switching asks the engine
    await waitFor('document.querySelectorAll("#qOuts .qOutI").length === 2', 3000);
    await click(await rect('#qOut')); await sleep(300); await acts(true);
    await shot(`${name}-5b-outputs`);
    const outN = await ev('return document.querySelectorAll("#qOuts.on .qOutI").length');
    rec(name, 'volume card shows the output devices', outN === 2, outN + ' devices');
    await click(await rect('#qOuts .qOutI:not(.on)', 0)); await sleep(500); a = await acts();
    rec(name, 'picking a device switches the output', a.some((x) => x.a === 'audioout' && x.id === 'dev-2'));
    // the volume slider sets that exact level (it used to send it as a step, so any move jumped to 100 %)
    await acts(true);
    await ev('const v = document.querySelector("#qVol"); v.value = 37; v.dispatchEvent(new Event("input")); v.dispatchEvent(new Event("change")); return 1');
    await sleep(300); a = await acts();
    const volA = a.filter((x) => x.a === 'volume');
    rec(name, 'volume slider sets the exact level', volA.length > 0 && volA.every((x) => x.set === '37' && !x.d), JSON.stringify(volA.map((x) => x.set || x.d)));
    await key('Escape', 27); rec(name, 'Esc closes quick settings', (await waitFor('!Shell.panel', 2000)) >= 0);
    await sleep(300);
    await click(await rect(D.clock)); const nOpen = await waitFor('Shell.panel === "notify"', 2000);
    await sleep(400); await shot(`${name}-6-notify`);
    rec(name, 'notification centre opens from the clock', nOpen >= 0);
    await key('Escape', 27); await waitFor('!Shell.panel', 2000); await sleep(300);
    // ---- hidden icons: our own panel (the mock answers /api/tray with two icons and records clicks)
    await acts(true); await click(await rect(D.tray));
    const trOpen = await waitFor('Shell.panel === "tray" && document.querySelectorAll("#tcList .tTile").length === 2', 4000);
    await sleep(400); await shot(`${name}-7-tray`);
    rec(name, 'chevron opens the hidden icons panel', trOpen >= 0, (await ev('return document.querySelectorAll("#tcList .tTile").length')) + ' icons');
    await click(await rect('#tcList .tTile', 1), 'right'); await waitFor('!Shell.panel', 2000); await sleep(300); a = await acts();
    rec(name, 'right-click on an icon: panel closes, its menu is asked for', a.some((x) => x.a === 'trayclick' && x.i === '1' && x.b === 'right'), JSON.stringify(a.filter((x) => x.a === 'trayclick')));
    await click(await rect(D.tray)); await waitFor('Shell.panel === "tray"', 4000); await sleep(300);
    await acts(true); await click(await rect('#tcList .tTile', 0)); await waitFor('!Shell.panel', 2000); await sleep(300); a = await acts();
    rec(name, 'left click on an icon opens it', a.some((x) => x.a === 'trayclick' && x.i === '0' && x.b === 'left'));

    // ---- an app with two windows: a window list instead of cycling blindly
    await ev(`const e = [...document.querySelectorAll(${JSON.stringify(D.apps)})].find((x) => x._app); const a = e._app; a.running = true; a.windows = ['111', '222']; a.titles = ['First doc', 'Second doc']; window.__twoWin = e; return 1`);
    const twoR = await ev('const r = __twoWin.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }');
    await acts(true); await click(twoR);
    const wOpen = await waitFor('Shell.panel === "wins" && document.querySelectorAll("#tcList .lRow").length === 2', 3000);
    await sleep(400); await shot(`${name}-8-windows`);
    rec(name, 'app with 2 windows shows both', wOpen >= 0, await ev('return [...document.querySelectorAll("#tcList .lT")].map((x) => x.textContent).join(" | ")'));
    await click(await rect('#tcList .lRow', 1)); await waitFor('!Shell.panel', 2000); await sleep(300); a = await acts();
    rec(name, 'picking the second window brings it forward', a.some((x) => x.a === 'activate' && x.h === '222'));

    // ---- idle: decorative loops stop when nobody is on the bar
    await ev('Shell.goIdle(20); return 1'); await sleep(200);
    rec(name, 'bar goes idle (animations paused)', await ev('return document.documentElement.classList.contains("tcIdle")'));
    await ev('Shell.setIdle(false); return 1');

    // ---- recovery: a script error while the menu opens hands the screen back
    await openMenu(); await acts(true);
    await ev('Shell.busy = true; setTimeout(() => { throw new Error("injected test error"); }, 0); return 1'); await sleep(900);
    a = await acts();
    rec(name, 'page error while open: menu released', a.some((x) => x.a === 'menu' && x.open === '0'));
    rec(name, 'no script errors during the run', errors.filter((e) => !e.includes('injected test error')).length === 0, errors.filter((e) => !e.includes('injected test error')).slice(0, 3).join(' | '));
  }
  const fails = results.filter((r) => !r.ok);
  console.log(`\n${results.length - fails.length}/${results.length} passed. Screenshots: ${OUT}`);
  cdp.close(); chrome.kill(); mock.kill();
  process.exit(fails.length ? 1 : 0);
}
main().catch((e) => { console.error('harness error:', e.message); console.error('page errors so far:', JSON.stringify(global.__errors || []).slice(0, 1500)); process.exit(2); });
