registerView('boot', {
  title: 'Boot Screen', subtitle: 'UEFI boot logo · Secure Boot check & guide · boot animation · startup sound',
  async render(el) {
    const st = await api('boot.status', {}, { busy: 'Reading firmware status…' });
    const fw = st.firmware;
    const tag = (ok, yes, no) => h('span', { class: 'tag ' + (ok ? 'on' : 'off') }, ok ? yes : no);

    const status = card('Firmware status',
      h('div', { class: 'kv' },
        h('span', null, 'Boot mode'), h('span', null, fw.uefi ? tag(true, 'UEFI', '') : h('span', { class: 'tag off' }, 'Legacy BIOS — logo change not possible')),
        h('span', null, 'Secure Boot'), h('span', null, fw.secureBoot == null ? h('span', { class: 'tag' }, 'unknown') : h('span', { class: 'tag ' + (fw.secureBoot ? 'warn' : 'on') }, fw.secureBoot ? 'ON' : 'OFF')),
        h('span', null, 'BitLocker (system drive)'), h('span', null, h('span', { class: 'tag ' + (fw.bitlocker === 'On' ? 'warn' : '') }, fw.bitlocker)),
        h('span', null, 'Motherboard'), h('span', null, `${fw.vendor || '?'} ${fw.board || ''}`),
        h('span', null, 'Custom boot logo'), h('span', null, st.hackbgrt.installed ? h('span', { class: 'tag neon' }, 'installed') : h('span', { class: 'tag' }, 'not installed'))),
      st.hackbgrt.config ? h('pre', { class: 'mono note', style: { whiteSpace: 'pre-wrap' } }, st.hackbgrt.config) : null,
      h('div', { class: 'row', style: { marginTop: '12px' } },
        btn('Re-check', () => go('boot'), 'sm', IC.refresh),
        btn('Back up boot config (BCD)', async () => { const r = await api('boot.backupBcd'); toast('Saved ' + fileName(r.file), 'ok'); }, 'sm', IC.save),
        btn('Restore BCD backup…', async () => { const f = await pickFile('BCD backup|*.bcd', 'Choose BCD backup'); if (f && await confirmBox('Restore boot configuration?', 'This replaces your current boot configuration with the backup.', 'Restore', 'danger')) { const o = await api('boot.restoreBcd', { file: f }); toast(o, 'ok'); } }, 'sm')));

    const V = [
      ['ASUS', 'Del or F2', 'Advanced Mode (F7) → Boot → Secure Boot → OS Type: "Other OS". To fully disable: Key Management → Clear Secure Boot keys.'],
      ['MSI', 'Del', 'Settings → Security (or Advanced → Windows OS Configuration) → Secure Boot → Disabled.'],
      ['Gigabyte / AORUS', 'Del', 'Boot → Secure Boot → Secure Boot Enable: Disabled.'],
      ['ASRock', 'F2 or Del', 'Security → Secure Boot → Disabled.'],
      ['Dell', 'F2', 'Boot Configuration (or Secure Boot) → Enable Secure Boot: Off.'],
      ['HP', 'F10', 'Advanced (or Security) → Secure Boot Configuration → uncheck / Legacy Support Disable + Secure Boot Disable.'],
      ['Lenovo', 'F1 / F2 / Novo button', 'Security → Secure Boot → Disabled.'],
      ['Acer', 'F2', 'Set a Supervisor Password first (Security tab), then Boot → Secure Boot → Disabled.'],
    ];
    const guide = card('Secure Boot — check & disable (if you want to)',
      h('div', { class: 'note' }, 'You do NOT have to disable Secure Boot. With it ON, the logo still works: on the first reboot you approve it once in a blue "MOK manager" screen (steps below). If you prefer the simple way, turn Secure Boot off in your BIOS — menu names differ a bit between boards.'),
      h('div', { class: 'row', style: { margin: '12px 0' } },
        btn('Restart into BIOS / UEFI', async () => { if (await confirmBox('Restart into BIOS now?', 'Save your work. The PC restarts in 5 seconds straight into firmware setup.', 'Restart', 'danger')) api('boot.rebootToFirmware'); }, 'sm', IC.refresh),
        btn('Advanced startup', async () => { if (await confirmBox('Restart to Advanced startup?', 'Troubleshoot / UEFI Firmware Settings / Safe Mode menu.', 'Restart')) api('boot.advancedStartup'); }, 'sm')),
      V.map(([n, k, s]) => h('details', { class: 'guide' }, h('summary', null, `${n}  ·  key: ${k}`), h('ol', null, h('li', null, `Restart and keep tapping ${k} (or use the button above).`), h('li', null, s), h('li', null, 'Save & Exit (usually F10). To turn it back on later, choose Enabled / "Windows UEFI mode" again.')))),
      h('details', { class: 'guide' }, h('summary', null, 'Keeping Secure Boot ON — first boot after installing the logo'),
        h('ol', null, h('li', null, '"Verification failed: Security Violation" appears → press Enter on OK.'), h('li', null, 'Quickly press any key to open MOK management.'),
          h('li', null, 'Choose "Enroll hash from disk" → your disk → EFI → HackBGRT → grubx64.efi.'), h('li', null, 'Continue → Yes → Reboot. Done — it never asks again.'))),
      h('details', { class: 'guide' }, h('summary', null, 'If something goes wrong'),
        h('ol', null, h('li', null, 'Open your firmware boot menu (F8 / F11 / F12 / Esc at power-on) and pick "Windows Boot Manager" — Windows still boots normally.'),
          h('li', null, 'Then come back here and press "Remove custom boot logo".'), h('li', null, 'BitLocker on? Keep your recovery key handy (aka.ms/myrecoverykey) — firmware changes can ask for it.'))));

    // ---- logo designer
    let img = '', mode = 'image', y = -200, res = '0x0', method = 'entry';
    const pv = h('div', { class: 'bootpv' }); const pvImg = h('img', { class: 'hidden' }); const dots = h('div', { class: 'dots' }, Array.from({ length: 5 }, () => h('i')));
    pv.append(pvImg, dots);
    const drawPv = () => {
      pvImg.classList.toggle('hidden', mode !== 'image' || !img);
      if (img) pvImg.src = media(img);
      pvImg.style.top = `calc(50% + ${(y / 1080) * 100}%)`;
      pv.style.background = mode === 'keep' ? 'radial-gradient(circle, #222 0 8%, #000 9%)' : '#000';
    };
    const yLbl = h('span', { class: 'mono' }, y + ' px');
    const ySl = h('input', { type: 'range', min: -500, max: 400, step: 10, value: y, style: { width: '220px' } });
    ySl.addEventListener('input', () => { y = Number(ySl.value); yLbl.textContent = y + ' px'; drawPv(); });
    const imgLbl = h('b', null, 'no picture chosen');

    const designer = card('Custom boot logo',
      h('div', { class: 'grid g2' },
        h('div', null, pv, h('p', { class: 'muted small' }, 'Preview at 1080p. Windows spinner drawn for reference.')),
        h('div', { class: 'stack' },
          seg([{ v: 'image', l: 'My picture' }, { v: 'black', l: 'Black' }, { v: 'remove', l: 'No logo' }, { v: 'keep', l: 'Vendor logo' }], mode, (v) => { mode = v; drawPv(); }),
          h('div', { class: 'row' }, imgLbl,
            btn('Choose picture', async () => { const f = await pickFile(F_IMG, 'Boot logo (PNG with black background works best)'); if (f) { img = f; imgLbl.textContent = fileName(f); drawPv(); } }, 'sm', IC.image),
            btn('From library', async () => { const f = await pickFromLibrary('image'); if (f) { img = f; imgLbl.textContent = fileName(f); drawPv(); } }, 'sm')),
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Vertical position'), ySl, yLbl),
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Resolution'), selectEl([{ v: '0x0', l: 'Maximum (recommended)' }, { v: '-1x-1', l: 'Keep firmware resolution' }], res, (v) => { res = v; })),
          h('div', { class: 'row' }, h('span', { class: 'muted' }, 'Install method'), selectEl([{ v: 'entry', l: 'New boot entry (safest)' }, { v: 'bcdedit', l: 'Boot entry via bcdedit' }, { v: 'overwrite', l: 'Overwrite Windows loader (legacy, risky)' }], method, (v) => { method = v; })),
          h('div', { class: 'row' },
            btn('Apply boot logo', async () => {
              if (mode === 'image' && !img) return toast('Choose a picture first', 'err');
              const chk = h('input', { type: 'checkbox' });
              const body = h('div', null,
                h('div', { class: 'note danger' }, h('b', null, 'Disclaimer. '), 'Changing the UEFI boot logo installs a small boot program (HackBGRT, open-source, MIT) on your EFI partition. If it goes wrong your PC can fail to boot until you pick "Windows Boot Manager" in the firmware boot menu or repair from a Windows USB. You are doing this at your own risk.'),
                h('ul', { class: 'small' },
                  h('li', null, 'A System Restore point and a BCD backup are created first.'),
                  fw.secureBoot ? h('li', null, h('b', null, 'Secure Boot is ON: '), 'on the next reboot you must approve HackBGRT once in the blue MOK screen (see the guide on this page).') : h('li', null, 'Secure Boot is off — no extra steps.'),
                  fw.bitlocker === 'On' ? h('li', null, h('b', null, 'BitLocker is ON: '), 'have your recovery key ready, Windows may ask for it once.') : null,
                  h('li', null, 'The firmware logo may flash for a split second before yours — that is normal.')),
                h('label', { class: 'check', style: { marginTop: '8px' } }, chk, 'I understand and want to continue'));
              const ok = await modal({ title: 'Change boot logo?', body, actions: [{ label: 'Cancel', value: false }, { label: 'Apply', value: true, cls: 'primary', check: () => { if (!chk.checked) { toast('Tick the box to continue', 'err'); return false; } return true; } }] });
              if (!ok) return;
              const r = await api('boot.applyLogo', { mode, image: img, y, resolution: res, method, allowSecureBoot: !!fw.secureBoot, allowBitlocker: fw.bitlocker === 'On' }, { busy: 'Creating restore point & installing boot logo…' });
              modal({ title: r.ok ? 'Boot logo installed' : 'Installer reported a problem', wide: true,
                body: h('div', null, r.ok ? h('p', null, 'Restart your PC to see it.', r.shimNeeded ? ' Remember the one-time MOK approval (Secure Boot is on).' : '') : null, h('textarea', { rows: 12, style: { width: '100%' } }, r.output || ('exit code ' + r.exitCode))),
                actions: [{ label: 'Close' }] });
              go('boot');
            }, 'primary', IC.bolt),
            btn('Remove custom boot logo', async () => { if (!(await confirmBox('Remove custom boot logo?', 'Uninstalls HackBGRT and restores the normal Windows boot.', 'Remove', 'danger'))) return; const r = await api('boot.removeLogo', {}, { busy: 'Removing…' }); toast(r.ok ? 'Removed' : 'Exit code ' + r.exitCode, r.ok ? 'ok' : 'err'); go('boot'); }, 'danger', IC.undo)))));
    drawPv();

    // ---- custom startup sound
    const ss = Object.assign({ enabled: false, file: '' }, st.engineSound);
    const ssLbl = h('b', null, fileName(ss.file) || 'no file');
    const sound = card('Custom startup sound',
      h('p', { class: 'muted small' }, 'Plays your own .wav when you sign in (Windows 11 does not allow replacing its own startup sound file, so TRANSCRYPTO plays it instead and mutes the Windows one).'),
      toggleRow('Play my startup sound', null, !!ss.enabled, async (x) => { await api('boot.setStartupSound', { values: { enabled: x, file: ss.file } }); toast('Saved', 'ok'); }),
      h('div', { class: 'row' }, ssLbl, btn('Choose .wav', async () => { const f = await pickFile(F_WAV, 'Startup sound'); if (f) { ss.file = f; ssLbl.textContent = fileName(f); await api('boot.setStartupSound', { values: { file: f } }); toast('Saved', 'ok'); } }, 'sm', IC.folder),
        btn('', () => { if (ss.file) new Audio(media(ss.file)).play(); }, 'sm icon', IC.play)));

    el.append(h('div', { class: 'grid g2' }, status, sound), designer, guide, h('h3', { style: { margin: '4px' } }, 'Boot animation & menu'), await tweaksPanel('boot'));
  },
});
