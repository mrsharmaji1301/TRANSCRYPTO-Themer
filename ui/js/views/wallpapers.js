registerView('wallpapers', {
  title: 'Wallpapers', subtitle: 'Solid · Picture · Live video (built-in light engine) · Slideshow',
  async render(el, params) {
    const { bar } = await layerBar('wallpaper', () => go('wallpapers'));
    const comp = await api('comp.get', { component: 'wallpaper' });
    const v = Object.assign({}, comp.values || {});
    const [lib, mons, eng, scenes] = await Promise.all([api('wallpapers.library'), api('wallpapers.monitors'), api('engine.status'), api('wallpapers.scenes').catch(() => [])]);
    let mode = params.mode || v.type || 'image';
    let filter = 'all';
    const sel = { image: v.image, file: v.file, perImage: {}, perVideo: Object.assign({}, v.monitors || {}), scene: v.type === 'web' ? (scenes.find((x) => x.design === v.design && x.palette === v.palette) || scenes.find((x) => x.design === v.design)) : null };
    mons.forEach((m) => { sel.perImage[m.id] = m.wallpaper; });

    const settings = h('div');
    const libGrid = h('div', { class: 'grid gauto-s' });

    const engCard = card('Engine', h('div', { class: 'row' },
      h('span', { class: 'tag ' + (eng.running ? 'on' : '') }, eng.running ? `running · ${eng.ramMB} MB RAM` : 'stopped'),
      h('span', { class: 'tag ' + (eng.built ? '' : 'warn') }, eng.built ? 'built' : 'not built yet'),
      btn('Build / rebuild engine', async () => { const r = await api('engine.build', {}, { busy: 'Compiling TCWall.exe with the Windows C# compiler…' }); modal({ title: r.ok ? 'Engine built' : 'Build failed', body: h('textarea', { rows: 10, style: { width: '100%' } }, r.output || 'OK'), actions: [{ label: 'Close' }] }); }, 'sm', IC.bolt),
      eng.running ? btn('Stop engine', async () => { await api('engine.stop'); go('wallpapers', { mode }); }, 'sm danger') : null),
      h('p', { class: 'muted small' }, 'Media Foundation hardware decoding, rendered behind your desktop icons. Pauses automatically on fullscreen apps, battery and lock. Use H.264/H.265 MP4 for the lowest CPU/GPU use.'));

    const monLabel = (m, i) => `Display ${i + 1}${m.primary ? ' (main)' : ''} · ${m.width}×${m.height}`;

    const draw = () => {
      settings.innerHTML = '';
      if (mode === 'solid') {
        let color = v.color || '#101018';
        settings.append(card('Solid color', h('div', { class: 'row' }, colorField(color, (c) => { color = c; }),
          btn('Apply color', () => setComp('wallpaper', { type: 'solid', color }), 'primary', IC.check))));
      }
      if (mode === 'image') {
        let fit = v.fit || 'fill'; let per = false;
        const perBox = h('div', { class: 'hidden list' }, mons.map((m, i) => h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, monLabel(m, i)), h('small', null, sel.perImage[m.id] || '—')),
          btn('Choose', async () => { const f = await pickFromLibrary('image'); if (f) { sel.perImage[m.id] = f; draw(); } }, 'sm'))));
        const cur = h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Selected:'), h('b', null, fileName(sel.image) || 'click a picture in the library below'));
        settings.append(card('Picture',
          cur,
          h('div', { class: 'row', style: { marginTop: '12px' } }, h('span', { class: 'muted' }, 'Fit'),
            selectEl([{ v: 'fill', l: 'Fill' }, { v: 'fit', l: 'Fit' }, { v: 'stretch', l: 'Stretch' }, { v: 'center', l: 'Center' }, { v: 'tile', l: 'Tile' }, { v: 'span', l: 'Span across displays' }], fit, (x) => { fit = x; }),
            mons.length > 1 ? h('label', { class: 'check' }, (() => { const c = h('input', { type: 'checkbox' }); c.addEventListener('change', () => { per = c.checked; perBox.classList.toggle('hidden', !per); }); return c; })(), 'Different picture per display') : null),
          perBox,
          h('div', { class: 'row', style: { marginTop: '12px' } },
            btn('Apply picture', async () => {
              if (per) await setComp('wallpaper', { type: 'image', fit, perMonitor: sel.perImage });
              else { if (!sel.image) return toast('Pick a picture from the library first', 'err'); await setComp('wallpaper', { type: 'image', fit, image: sel.image, perMonitor: {} }); }
            }, 'primary', IC.check),
            btn('Use as lock screen too', async () => { if (!sel.image) return; await api('lock.setImage', { image: sel.image }, { busy: 'Setting lock screen…' }); toast('Lock screen set', 'ok'); }, '', IC.lock))));
      }
      if (mode === 'live') {
        let fit = eng.config.wallpaper.fit || 'fill';
        let monMode = v.mode || 'same';
        let vol = Number(v.volume || 0);
        const opts = { pauseOnFullscreen: v.pauseOnFullscreen !== false, pauseOnMaximized: !!v.pauseOnMaximized, pauseOnBattery: v.pauseOnBattery !== false, pauseOnLock: v.pauseOnLock !== false };
        const perBox = h('div', { class: (monMode === 'per' ? '' : 'hidden ') + 'list' }, mons.map((m, i) => h('div', { class: 'li' }, h('div', { class: 'grow' }, h('b', null, monLabel(m, i)), h('small', null, fileName(sel.perVideo[m.device]) || 'same as main video')),
          btn('Choose', async () => { const f = await pickFromLibrary('video'); if (f) { sel.perVideo[m.device] = f; draw(); } }, 'sm'))));
        const volLbl = h('span', { class: 'mono' }, vol + '%');
        const volIn = h('input', { type: 'range', min: 0, max: 100, value: vol, style: { width: '180px' } }); volIn.addEventListener('input', () => { vol = Number(volIn.value); volLbl.textContent = vol + '%'; });
        settings.append(h('div', { class: 'grid g2' },
          card('Live wallpaper',
            h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Video:'), h('b', null, fileName(sel.file) || 'click a video in the library below')),
            h('div', { class: 'row', style: { marginTop: '12px' } }, h('span', { class: 'muted' }, 'Fit'), selectEl([{ v: 'fill', l: 'Fill (crop)' }, { v: 'fit', l: 'Fit (bars)' }, { v: 'stretch', l: 'Stretch' }], fit, (x) => { fit = x; }),
              mons.length > 1 ? selectEl([{ v: 'same', l: 'Same video on every display' }, { v: 'per', l: 'Different video per display' }, { v: 'span', l: 'One video spanning all displays' }], monMode, (x) => { monMode = x; perBox.classList.toggle('hidden', x !== 'per'); }) : null),
            perBox,
            h('div', { class: 'row', style: { marginTop: '12px' } }, h('span', { class: 'muted' }, 'Sound'), volIn, volLbl),
            h('div', { class: 'row', style: { marginTop: '14px' } },
              btn('Apply live wallpaper', async () => {
                if (!sel.file && monMode !== 'per') return toast('Pick a video from the library first', 'err');
                await setComp('wallpaper', Object.assign({ type: 'live', file: sel.file || '', fit, mode: monMode, volume: vol, monitors: monMode === 'per' ? sel.perVideo : {} }, opts), 'Live wallpaper on');
                go('wallpapers', { mode: 'live' });
              }, 'primary', IC.play),
              btn('Use a frame as lock screen', async () => { if (!sel.file) return; await api('lock.frameFromVideo', { video: sel.file, apply: true }, { busy: 'Grabbing frame…' }); toast('Lock screen set from video', 'ok'); }, '', IC.lock))),
          card('Auto-pause (saves CPU, GPU & battery)',
            toggleRow('Pause while a fullscreen app or game runs', 'Per display', opts.pauseOnFullscreen, (x) => { opts.pauseOnFullscreen = x; }),
            toggleRow('Pause when a maximized window covers the display', null, opts.pauseOnMaximized, (x) => { opts.pauseOnMaximized = x; }),
            toggleRow('Pause on battery', null, opts.pauseOnBattery, (x) => { opts.pauseOnBattery = x; }),
            toggleRow('Pause while PC is locked', null, opts.pauseOnLock, (x) => { opts.pauseOnLock = x; }),
            h('p', { class: 'muted small' }, 'Press "Apply live wallpaper" to save these.'))), engCard);
      }
      if (mode === 'web') {
        const sc = sel.scene;
        settings.append(card('Live scene',
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Scene:'), h('b', null, sc ? `${sc.name} · ${sc.palette}` : 'click a scene in the library below')),
          h('p', { class: 'muted small' }, 'The animated backgrounds of the TRANSCRYPTO shells, running behind your desktop icons. They pause behind maximised windows. Picking any other wallpaper turns the scene off.'),
          h('div', { class: 'row', style: { marginTop: '12px' } },
            btn('Apply scene', async () => { if (!sel.scene) return toast('Pick a scene from the library first', 'err'); await setComp('wallpaper', { type: 'web', design: sel.scene.design, palette: sel.scene.palette }, 'Live scene on'); go('wallpapers', { mode: 'web' }); }, 'primary', IC.play))));
      }
      if (mode === 'slideshow') {
        let folder = v.folder || ''; let minutes = v.minutes || 30; let shuffle = v.shuffle !== false; let fit = v.fit || 'fill';
        const fl = h('b', null, folder || 'none');
        settings.append(card('Slideshow',
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Folder:'), fl, btn('Choose folder', async () => { const f = await pickFolder('Slideshow folder'); if (f) { folder = f; fl.textContent = f; } }, 'sm', IC.folder),
            btn('Use library', () => { api('wallpapers.folder').then((f) => { folder = f; fl.textContent = f; }); }, 'sm')),
          h('div', { class: 'row', style: { marginTop: '12px' } }, h('span', { class: 'muted' }, 'Change every'),
            selectEl([1, 5, 10, 15, 30, 60, 180, 1440].map((m) => ({ v: m, l: m < 60 ? m + ' min' : m / 60 + ' h' })), minutes, (x) => { minutes = Number(x); }),
            selectEl([{ v: 'fill', l: 'Fill' }, { v: 'fit', l: 'Fit' }, { v: 'span', l: 'Span' }], fit, (x) => { fit = x; }),
            h('label', { class: 'check' }, (() => { const c = h('input', { type: 'checkbox' }); c.checked = shuffle; c.addEventListener('change', () => { shuffle = c.checked; }); return c; })(), 'Shuffle')),
          h('div', { class: 'row', style: { marginTop: '12px' } }, btn('Start slideshow', async () => { if (!folder) return toast('Choose a folder', 'err'); await setComp('wallpaper', { type: 'slideshow', folder, minutes, shuffle, fit }, 'Slideshow running'); }, 'primary', IC.play))), engCard);
      }
      drawLib();
    };

    const drawLib = () => {
      libGrid.innerHTML = '';
      const want = mode === 'live' ? 'video' : (mode === 'image' ? 'image' : null);
      const items = lib.filter((i) => filter === 'all' || i.kind === filter);
      // the shell scenes come first: one tile per design and palette
      if (filter === 'all' || filter === 'scene') for (const sc of scenes) {
        const isSel = sel.scene && sel.scene.design === sc.design && sel.scene.palette === sc.palette;
        const t = h('div', { class: 'tile' + (isSel ? ' sel' : ''), title: `${sc.name} · ${sc.palette}`, onclick: () => {
          sel.scene = sc; mode = 'web';
          $$('.seg button', modeSeg).forEach((b, i) => b.classList.toggle('on', MODES[i] === mode));
          draw();
        } },
          sc.preview ? h('img', { class: 'thumb', src: media(sc.preview), loading: 'lazy' }) : h('div', { class: 'thumb' }),
          h('span', { class: 'badge tag warn' }, 'SCENE'),
          h('div', { class: 'meta' }, h('span', { class: 'grow' }, sc.name), h('span', { class: 'muted small' }, sc.palette)));
        if (mode !== 'web' && want) t.style.opacity = '.55';
        libGrid.append(t);
      }
      if (filter === 'scene') return;
      if (!items.length) { libGrid.append(h('div', { class: 'empty spanall' }, 'Library is empty. Add pictures or videos — drag & drop works too.')); return; }
      for (const it of items) {
        const isSel = (it.kind === 'image' && it.path === sel.image) || (it.kind === 'video' && it.path === sel.file);
        const media_ = it.kind === 'video'
          ? h('video', { class: 'thumb', src: media(it.path) + '#t=1.5', muted: true, loop: true, preload: 'metadata', onmouseenter: (e) => e.target.play().catch(() => {}), onmouseleave: (e) => { e.target.pause(); } })
          : h('img', { class: 'thumb', src: media(it.path), loading: 'lazy' });
        const t = h('div', { class: 'tile' + (isSel ? ' sel' : ''), title: it.path, onclick: () => {
          if (it.kind === 'video') { sel.file = it.path; if (mode !== 'live') mode = 'live'; } else { sel.image = it.path; if (mode !== 'image') mode = 'image'; }
          $$('.seg button', modeSeg).forEach((b, i) => b.classList.toggle('on', MODES[i] === mode));
          draw();
        } },
          media_,
          h('span', { class: 'badge tag ' + (it.kind === 'video' ? 'pink' : 'neon') }, it.kind === 'video' ? 'LIVE' : 'PIC'),
          h('div', { class: 'tools' },
            btn('', async (e) => { e.stopPropagation(); if (await confirmBox('Delete ' + it.name + '?', 'The file is removed from the library folder.', 'Delete', 'danger')) { await api('wallpapers.delete', { path: it.path }); lib.splice(lib.indexOf(it), 1); drawLib(); } }, 'sm icon danger', IC.trash)),
          h('div', { class: 'meta' }, h('span', { class: 'grow' }, it.name), h('span', { class: 'muted small' }, it.sizeMB + ' MB')));
        if (want && it.kind !== want) t.style.opacity = '.55';
        libGrid.append(t);
      }
    };

    const reloadLib = async () => { const l = await api('wallpapers.library'); lib.length = 0; lib.push(...l); drawLib(); };
    const drop = h('div', { class: 'drop' }, 'Drop pictures or videos here');
    drop.addEventListener('dragover', (e) => { e.preventDefault(); drop.classList.add('over'); });
    drop.addEventListener('dragleave', () => drop.classList.remove('over'));
    drop.addEventListener('drop', async (e) => {
      e.preventDefault(); drop.classList.remove('over');
      busy(true, 'Copying into library…');
      try { for (const f of e.dataTransfer.files) await uploadFile(f); } catch (err) { toast(err.message, 'err'); } finally { busy(false); }
      reloadLib();
    });

    const MODES = ['solid', 'image', 'live', 'web', 'slideshow'];
    const modeSeg = seg([{ v: 'solid', l: 'Solid' }, { v: 'image', l: 'Picture' }, { v: 'live', l: 'Live video' }, { v: 'web', l: 'Live scene' }, { v: 'slideshow', l: 'Slideshow' }], mode, (m) => { mode = m; draw(); });
    const libCard = card('Library',
      h('div', { class: 'row between' },
        seg([{ v: 'all', l: 'All' }, { v: 'scene', l: 'Scenes' }, { v: 'image', l: 'Pictures' }, { v: 'video', l: 'Videos' }], 'all', (f) => { filter = f; drawLib(); }),
        h('div', { class: 'row' },
          btn('Add files', async () => { const f = await pickFile(F_MEDIA, 'Add wallpapers', true); if (f && f.length) { await api('wallpapers.import', { paths: f }, { busy: 'Copying…' }); reloadLib(); } }, 'sm', IC.plus),
          btn('Add from URL', async () => {
            const url = h('input', { type: 'url', placeholder: 'https://moewalls.com/… or a direct .mp4 / .jpg link', style: { width: '100%' } });
            const name = h('input', { type: 'text', placeholder: 'Name (optional)', style: { width: '100%', marginTop: '8px' } });
            const ok = await modal({ title: 'Add from URL', body: h('div', null, h('p', { class: 'muted small' }, 'Paste a wallpaper page (MoeWalls, etc.) or a direct file link. TRANSCRYPTO finds the video on the page and downloads it into your library.'), url, name),
              actions: [{ label: 'Cancel', value: false }, { label: 'Download', value: true, cls: 'primary' }] });
            if (!ok || !url.value) return;
            const r = await api('wallpapers.fromUrl', { url: url.value, name: name.value }, { busy: 'Downloading…' });
            toast('Added ' + r.name, 'ok'); reloadLib();
          }, 'sm', IC.link),
          btn('Open folder', async () => api('app.openPath', { path: await api('wallpapers.folder') }), 'sm', IC.folder))),
      h('div', { style: { marginTop: '12px' } }, drop), h('div', { style: { marginTop: '12px' } }, libGrid));

    el.append(bar,
      h('div', { class: 'row' }, modeSeg, h('span', { class: 'muted small' }, 'Current: ' + (v.type || 'image'))), settings, libCard);
    draw();
  },
});
