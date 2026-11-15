/* TRANSCRYPTO live backgrounds - shared: pause while nobody can see the desktop, and a 30 fps cap for the canvas scenes.
   The engine pushes "bg:pause" / "bg:play" (maximised or fullscreen window in front); the shell page pauses its own copy by postMessage. */
'use strict';
const BG = { paused: false, fps: 30, last: 0 };
document.head.insertAdjacentHTML('beforeend', '<style>html.paused *, html.paused *::before, html.paused *::after { animation-play-state: paused !important; }</style>');
BG.set = (p) => { BG.paused = !!p; document.documentElement.classList.toggle('paused', BG.paused); };
/* true when this animation frame should be drawn */
BG.due = (t) => {
  if (BG.paused || document.hidden || t - BG.last < 1000 / BG.fps - 2) return false;
  BG.last = t; return true;
};
addEventListener('message', (e) => { if (e.data && e.data.tcBg) BG.set(e.data.tcBg === 'pause'); });
(function engine() {
  const t = new URLSearchParams(location.search).get('t'); if (!t || top !== window) return;
  fetch('/api/do?' + new URLSearchParams({ t, a: 'bgstate' })).then((r) => r.json()).then((r) => BG.set(r && r.result)).catch(() => {});
  (async function events() {
    let since = -1;
    for (;;) {
      try {
        const r = await (await fetch('/api/events?' + new URLSearchParams({ t, since }))).json();
        if (since < 0) { since = r.seq; continue; }
        since = r.seq;
        for (const e of r.events) { if (e === 'bg:pause') BG.set(true); if (e === 'bg:play') BG.set(false); }
      } catch (e) { await new Promise((ok) => setTimeout(ok, 3000)); }
    }
  })();
})();
