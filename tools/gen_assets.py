#!/usr/bin/env python3
"""Generates TRANSCRYPTO presets, icon packs (.ico), neon cursor packs (.cur/.ani) and the app icon.
Dev-only: the generated files ship with the app. Run:  python tools/gen_assets.py
Requires Pillow."""
import io, json, math, os, struct
from PIL import Image, ImageDraw, ImageFilter, ImageChops

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
A = os.path.join(ROOT, 'assets')

# ------------------------------------------------------------------ presets
# id, name, desc, (bg, surface, text, accent, accent2), mode, icon style, drive style, cursor style, sounds,
# accent on Start/taskbar, accent on title bars, transparency, wallpaper color
PRESETS = [
    ('transcrypto-neon', 'TRANSCRYPTO Neon', 'Signature cyan x magenta glow on deep black.', ('#07070d', '#15152a', '#eef0ff', '#00f0ff', '#ff2bd6'), 'dark', 'glow', 'cyber', 'neon', 'minimal', True, False, True, '#07070d'),
    ('catppuccin-latte', 'Catppuccin Latte', 'Soft pastel light theme, mauve accents.', ('#eff1f5', '#e6e9ef', '#4c4f69', '#8839ef', '#ea76cb'), 'light', 'soft', 'glass', 'light', 'default', False, True, True, '#dce0e8'),
    ('catppuccin-mocha', 'Catppuccin Mocha', 'Soothing pastel dark theme loved by r/unixporn.', ('#1e1e2e', '#313244', '#cdd6f4', '#cba6f7', '#f5c2e7'), 'dark', 'soft', 'glass', 'accent', 'default', False, False, True, '#1e1e2e'),
    ('dracula', 'Dracula', 'The classic purple-pink dark theme, flat icons.', ('#282a36', '#44475a', '#f8f8f2', '#bd93f9', '#ff79c6'), 'dark', 'flat', 'slab', 'dark', 'default', False, True, True, '#282a36'),
    ('nord', 'Nord', 'Arctic, north-bluish calm with line icons.', ('#2e3440', '#3b4252', '#eceff4', '#88c0d0', '#81a1c1'), 'dark', 'outline', 'minimal', 'dark', 'minimal', False, False, True, '#3b4252'),
    ('nord-snow', 'Nord Snow Storm', 'Nord, but bright: snow white with frost blue.', ('#eceff4', '#e5e9f0', '#2e3440', '#5e81ac', '#88c0d0'), 'light', 'outline', 'minimal', 'light', 'minimal', False, True, True, '#d8dee9'),
    ('tokyo-night', 'Tokyo Night', 'Neon city lights over a dark blue night.', ('#1a1b26', '#24283b', '#c0caf5', '#7aa2f7', '#bb9af7'), 'dark', 'glow', 'ssd', 'neon', 'default', False, False, True, '#1a1b26'),
    ('gruvbox-dark', 'Gruvbox Dark', 'Retro groove, warm and earthy. Orange taskbar.', ('#282828', '#3c3836', '#ebdbb2', '#fe8019', '#b8bb26'), 'dark', 'flat', 'retro', 'accent', 'default', True, False, False, '#32302f'),
    ('gruvbox-light', 'Gruvbox Light', 'Retro groove on warm cream paper.', ('#fbf1c7', '#ebdbb2', '#3c3836', '#d65d0e', '#79740e'), 'light', 'flat', 'retro', 'light', 'default', False, True, False, '#ebdbb2'),
    ('rose-pine', 'Rosé Pine', 'All natural pine, faux fur and a bit of soho vibes.', ('#191724', '#26233a', '#e0def4', '#ebbcba', '#c4a7e7'), 'dark', 'soft', 'glass', 'dark', 'minimal', False, False, True, '#191724'),
    ('rose-pine-dawn', 'Rosé Pine Dawn', 'Morning light version of Rosé Pine.', ('#faf4ed', '#fffaf3', '#575279', '#d7827e', '#907aa9'), 'light', 'soft', 'glass', 'light', 'minimal', False, True, True, '#f2e9e1'),
    ('one-dark', 'One Dark', 'The iconic editor theme, flat and focused.', ('#282c34', '#3e4451', '#abb2bf', '#61afef', '#c678dd'), 'dark', 'flat', 'ssd', 'dark', 'default', False, False, True, '#21252b'),
    ('everforest', 'Everforest', 'Green, comfy and easy on the eyes.', ('#2d353b', '#3d484d', '#d3c6aa', '#a7c080', '#83c092'), 'dark', 'soft', 'retro', 'accent', 'minimal', True, False, True, '#2d353b'),
    ('solarized-light', 'Solarized Light', 'Precision colors, the light way. Line icons.', ('#fdf6e3', '#eee8d5', '#586e75', '#268bd2', '#2aa198'), 'light', 'outline', 'minimal', 'light', 'default', False, False, False, '#eee8d5'),
    ('sakura', 'Sakura', 'Cherry blossom pink with a sky-blue touch.', ('#fff0f5', '#ffe4ec', '#5a3d4a', '#ff5c8a', '#8fd3fe'), 'light', 'soft', 'glass', 'accent', 'default', False, True, True, '#ffd6e5'),
    ('ocean-deep', 'Ocean Deep', 'Deep sea blue with bioluminescent teal.', ('#0b2545', '#13315c', '#e0f2ff', '#13c4a3', '#4cc9f0'), 'dark', 'glow', 'glass', 'neon', 'default', True, True, True, '#0b2545'),
    ('synthwave-sunset', 'Synthwave Sunset', '80s outrun: hot pink, gold and purple haze.', ('#241b2f', '#34294f', '#f8f8f2', '#ff7edb', '#fede5d'), 'dark', 'glow', 'slab', 'neon', 'default', True, False, True, '#2b1640'),
    ('neon-night', 'Neon Night', 'Rain-soaked megacity: acid yellow and cyan.', ('#0a0a12', '#1a1026', '#f0f0f0', '#fcee0a', '#00f0ff'), 'dark', 'glow', 'cyber', 'neon', 'minimal', True, False, True, '#0a0a12'),
    ('matrix', 'Matrix', 'Green phosphor on pure black. Silent.', ('#020a04', '#0b1f10', '#c8ffd4', '#00ff41', '#008f11'), 'dark', 'glow', 'cyber', 'neon', 'silent', True, True, False, '#000000'),
    ('mono-paper', 'Mono Paper', 'Black ink on white paper. No color, no noise.', ('#f4f4f4', '#ffffff', '#1a1a1a', '#2b2b2b', '#8a8a8a'), 'light', 'mono', 'minimal', 'light', 'minimal', False, False, False, '#e9e9e9'),
    ('midnight-mono', 'Midnight Mono', 'Grayscale dark: calm, solid, distraction free.', ('#0d0d0d', '#1a1a1a', '#e6e6e6', '#d0d0d0', '#7a7a7a'), 'dark', 'mono', 'minimal', 'dark', 'silent', False, False, False, '#121212'),
]

def hex2rgb(h):
    h = h.lstrip('#'); return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))
def mix(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))
def rgba(c, a=255): return (c[0], c[1], c[2], a)

# ------------------------------------------------------------------ drawing helpers (draw big, scale down)
SS = 4
def canvas(n): return Image.new('RGBA', (n * SS, n * SS), (0, 0, 0, 0))
def P(pts, n, unit=32): s = n * SS / unit; return [(x * s, y * s) for x, y in pts]
def finish(img, n): return img.resize((n, n), Image.LANCZOS)

ICON_STYLE = 'glow'   # glow | flat | soft | outline | mono (set per pack in main)

def glow(layer, color, radius):
    if ICON_STYLE in ('flat', 'outline', 'mono'): return layer
    if ICON_STYLE == 'soft':   # drop shadow instead of neon glow
        a = layer.split()[3].filter(ImageFilter.GaussianBlur(radius * 1.2)).point(lambda v: int(v * .45))
        sh = Image.new('RGBA', layer.size, (0, 0, 0, 0)); sh.putalpha(a)
        out = Image.new('RGBA', layer.size, (0, 0, 0, 0)); out.paste(sh, (int(radius * .4), int(radius * .9)))
        return Image.alpha_composite(out, layer)
    a = layer.split()[3].filter(ImageFilter.GaussianBlur(radius))
    g = Image.new('RGBA', layer.size, rgba(color, 0)); g.putalpha(a.point(lambda v: min(255, int(v * 1.6))))
    out = Image.alpha_composite(g, layer); return out

def grad(size, c1, c2, vertical=True):
    m = Image.linear_gradient('L')
    if not vertical: m = m.rotate(90, expand=True).transpose(Image.FLIP_LEFT_RIGHT)
    m = m.resize(size)
    return Image.composite(Image.new('RGBA', size, rgba(c2)), Image.new('RGBA', size, rgba(c1)), m)

def fill_grad(mask, c1, c2):
    if ICON_STYLE in ('flat', 'outline', 'mono'): c2 = c1
    elif ICON_STYLE == 'soft': c1, c2 = mix(c1, (255, 255, 255), .3), mix(c2, (255, 255, 255), .2)
    g = grad(mask.size, c1, c2); out = Image.new('RGBA', mask.size, (0, 0, 0, 0)); out.paste(g, (0, 0), mask); return out

# ------------------------------------------------------------------ icons (256 master)
def icon_folder(p, open_=False):
    n = 256; S = n * SS; acc, acc2 = hex2rgb(p['accent']), hex2rgb(p['accent2'])
    img = canvas(n)
    back = Image.new('L', (S, S), 0); d = ImageDraw.Draw(back)
    u = S / 256
    d.rounded_rectangle([18*u, 46*u, 238*u, 222*u], 22*u, fill=255)
    d.rounded_rectangle([18*u, 34*u, 112*u, 70*u], 16*u, fill=255)
    img.alpha_composite(fill_grad(back, mix(acc, (0, 0, 0), .35), mix(acc2, (0, 0, 0), .45)))
    # paper
    pap = Image.new('L', (S, S), 0); ImageDraw.Draw(pap).rounded_rectangle([34*u, 62*u, 222*u, 150*u], 10*u, fill=235)
    img.alpha_composite(fill_grad(pap, (250, 250, 255), (215, 218, 235)))
    front = Image.new('L', (S, S), 0); d = ImageDraw.Draw(front)
    if open_: d.polygon([(30*u, 104*u), (246*u, 104*u), (226*u, 222*u), (14*u, 222*u)], fill=255)
    else: d.rounded_rectangle([18*u, 92*u, 238*u, 222*u], 22*u, fill=255)
    fl = fill_grad(front, mix(acc, (255, 255, 255), .12), mix(acc2, (0, 0, 0), .1))
    img.alpha_composite(fl)
    hl = Image.new('L', (S, S), 0); ImageDraw.Draw(hl).rounded_rectangle([18*u, 92*u, 238*u, 100*u], 4*u, fill=90)
    img.alpha_composite(Image.composite(Image.new('RGBA', (S, S), (255, 255, 255, 255)), Image.new('RGBA', (S, S), (0, 0, 0, 0)), hl))
    return finish(glow(img, acc, 6*u), n)

def icon_drive(p, style):
    n = 256; S = n * SS; u = S / 256
    acc, acc2, surf = hex2rgb(p['accent']), hex2rgb(p['accent2']), hex2rgb(p['surface'])
    dark = mix(surf, (0, 0, 0), .35); lite = mix(surf, (255, 255, 255), .18)
    img = canvas(n); m = Image.new('L', (S, S), 0); d = ImageDraw.Draw(m)
    if style == 'slab':       # classic hard-drive box
        d.rounded_rectangle([20*u, 70*u, 236*u, 190*u], 26*u, fill=255)
        img.alpha_composite(fill_grad(m, lite, dark))
        dd = ImageDraw.Draw(img)
        dd.rounded_rectangle([40*u, 150*u, 150*u, 164*u], 7*u, fill=rgba(mix(dark, (0, 0, 0), .3)))
        for x in (186, 212): dd.ellipse([(x-9)*u, 148*u, (x+9)*u, 166*u], fill=rgba(acc))
    elif style == 'ssd':      # flat card with chips
        d.rounded_rectangle([28*u, 54*u, 228*u, 202*u], 18*u, fill=255)
        img.alpha_composite(fill_grad(m, mix(surf, (0, 0, 0), .1), dark))
        dd = ImageDraw.Draw(img)
        for i, x in enumerate((50, 104, 158)): dd.rounded_rectangle([x*u, 80*u, (x+42)*u, 130*u], 6*u, fill=rgba(mix(acc if i != 1 else acc2, (0, 0, 0), .35)), outline=rgba(acc), width=int(3*u))
        dd.rounded_rectangle([50*u, 160*u, 206*u, 176*u], 8*u, fill=rgba(acc))
    elif style == 'glass':    # translucent rounded slab
        d.rounded_rectangle([22*u, 62*u, 234*u, 196*u], 40*u, fill=200)
        img.alpha_composite(fill_grad(m, mix(acc, (255, 255, 255), .35), mix(acc2, (0, 0, 0), .2)))
        dd = ImageDraw.Draw(img)
        dd.rounded_rectangle([40*u, 76*u, 216*u, 108*u], 16*u, fill=(255, 255, 255, 70))
        dd.ellipse([186*u, 152*u, 206*u, 172*u], fill=(255, 255, 255, 230))
    elif style == 'retro':    # stacked platter cylinder
        for i, y in enumerate((150, 118, 86)):
            m2 = Image.new('L', (S, S), 0); d2 = ImageDraw.Draw(m2)
            d2.rectangle([34*u, (y-26)*u, 222*u, y*u], fill=255); d2.ellipse([34*u, (y-14)*u, 222*u, (y+30)*u], fill=255)
            img.alpha_composite(fill_grad(m2, mix(lite, acc, .15 * i), dark))
            ImageDraw.Draw(img).ellipse([34*u, (y-56)*u, 222*u, (y-12)*u], fill=rgba(mix(lite, acc, .25 + .2 * i)))
        ImageDraw.Draw(img).ellipse([110*u, 40*u, 146*u, 58*u], fill=rgba(acc2))
    elif style == 'cyber':    # hex chassis with neon strip
        hexpts = [(128*u + 108*u*math.cos(math.radians(a)), 128*u + 92*u*math.sin(math.radians(a))) for a in range(0, 360, 60)]
        d.polygon(hexpts, fill=255)
        img.alpha_composite(fill_grad(m, mix(surf, (0, 0, 0), .2), mix(surf, (0, 0, 0), .6)))
        dd = ImageDraw.Draw(img)
        dd.polygon(hexpts, outline=rgba(acc), width=int(5*u))
        dd.rounded_rectangle([64*u, 150*u, 192*u, 162*u], 6*u, fill=rgba(acc2))
        dd.rounded_rectangle([64*u, 96*u, 150*u, 104*u], 4*u, fill=rgba(acc))
    else:                     # minimal ring disk
        d.ellipse([34*u, 34*u, 222*u, 222*u], fill=255)
        img.alpha_composite(fill_grad(m, lite, dark))
        dd = ImageDraw.Draw(img)
        dd.ellipse([60*u, 60*u, 196*u, 196*u], outline=rgba(acc), width=int(10*u))
        dd.ellipse([112*u, 112*u, 144*u, 144*u], fill=rgba(acc2))
    return finish(glow(img, acc, 7*u), n)

def icon_pc(p):
    n = 256; S = n * SS; u = S / 256; acc, acc2, surf = hex2rgb(p['accent']), hex2rgb(p['accent2']), hex2rgb(p['surface'])
    img = canvas(n); d = ImageDraw.Draw(img)
    d.rounded_rectangle([20*u, 34*u, 236*u, 182*u], 16*u, fill=rgba(mix(surf, (0, 0, 0), .45)))
    scr = Image.new('L', (S, S), 0); ImageDraw.Draw(scr).rounded_rectangle([32*u, 46*u, 224*u, 170*u], 8*u, fill=255)
    img.alpha_composite(fill_grad(scr, acc, acc2))
    d = ImageDraw.Draw(img)
    d.polygon(P([(14.5, 23), (17.5, 23), (19, 27.5), (13, 27.5)], 256, 32), fill=rgba(mix(surf, (255, 255, 255), .1)))
    d.rounded_rectangle([70*u, 216*u, 186*u, 228*u], 6*u, fill=rgba(mix(surf, (255, 255, 255), .1)))
    d.polygon([(32*u, 46*u), (130*u, 46*u), (60*u, 170*u), (32*u, 170*u)], fill=(255, 255, 255, 38))
    return finish(glow(img, acc, 6*u), n)

def icon_bin(p, full):
    n = 256; S = n * SS; u = S / 256; acc, acc2, surf = hex2rgb(p['accent']), hex2rgb(p['accent2']), hex2rgb(p['surface'])
    img = canvas(n)
    if full:
        d = ImageDraw.Draw(img)
        for i, (x, y, r) in enumerate([(84, 44, -14), (128, 30, 6), (168, 48, 18)]):
            sheet = Image.new('RGBA', (int(70*u), int(80*u)), (0, 0, 0, 0))
            ImageDraw.Draw(sheet).rounded_rectangle([0, 0, 70*u - 1, 80*u - 1], 6*u, fill=rgba(mix((255, 255, 255), acc if i != 1 else acc2, .25)))
            sheet = sheet.rotate(r, expand=True, resample=Image.BICUBIC)
            img.alpha_composite(sheet, (int((x - 40)*u), int(y*u)))
    m = Image.new('L', (S, S), 0); d = ImageDraw.Draw(m)
    d.polygon([(52*u, 92*u), (204*u, 92*u), (188*u, 232*u), (68*u, 232*u)], fill=210)
    img.alpha_composite(fill_grad(m, mix(acc, (255, 255, 255), .2), mix(acc2, (0, 0, 0), .35)))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle([40*u, 78*u, 216*u, 98*u], 9*u, fill=rgba(mix(surf, (255, 255, 255), .25)))
    for x in (98, 128, 158): d.rounded_rectangle([(x-5)*u, 118*u, (x+5)*u, 210*u], 5*u, fill=(255, 255, 255, 80))
    return finish(glow(img, acc, 6*u), n)

def icon_globe(p):
    n = 256; S = n * SS; u = S / 256; acc, acc2 = hex2rgb(p['accent']), hex2rgb(p['accent2'])
    img = canvas(n); m = Image.new('L', (S, S), 0); ImageDraw.Draw(m).ellipse([28*u, 28*u, 228*u, 228*u], fill=255)
    img.alpha_composite(fill_grad(m, mix(acc, (0, 0, 0), .2), mix(acc2, (0, 0, 0), .5)))
    d = ImageDraw.Draw(img); w = int(7*u); c = (255, 255, 255, 170)
    d.ellipse([28*u, 28*u, 228*u, 228*u], outline=c, width=w); d.ellipse([84*u, 28*u, 172*u, 228*u], outline=c, width=w)
    d.line([128*u, 28*u, 128*u, 228*u], fill=c, width=w); d.line([28*u, 128*u, 228*u, 128*u], fill=c, width=w)
    d.arc([40*u, 50*u, 216*u, 120*u], 200, 340, fill=c, width=w); d.arc([40*u, 136*u, 216*u, 206*u], 20, 160, fill=c, width=w)
    return finish(glow(img, acc, 6*u), n)

def icon_user(p):
    n = 256; S = n * SS; u = S / 256; acc, acc2, surf = hex2rgb(p['accent']), hex2rgb(p['accent2']), hex2rgb(p['surface'])
    img = canvas(n); m = Image.new('L', (S, S), 0); ImageDraw.Draw(m).ellipse([24*u, 24*u, 232*u, 232*u], fill=255)
    img.alpha_composite(fill_grad(m, mix(surf, (255, 255, 255), .1), mix(surf, (0, 0, 0), .4)))
    pm = Image.new('L', (S, S), 0); d = ImageDraw.Draw(pm)
    d.ellipse([90*u, 56*u, 166*u, 132*u], fill=255); d.chord([54*u, 140*u, 202*u, 290*u], 180, 360, fill=255)
    cut = Image.new('L', (S, S), 0); ImageDraw.Draw(cut).ellipse([24*u, 24*u, 232*u, 232*u], fill=255)
    pm = ImageChops.multiply(pm, cut)
    img.alpha_composite(fill_grad(pm, acc, acc2))
    ImageDraw.Draw(img).ellipse([24*u, 24*u, 232*u, 232*u], outline=rgba(acc), width=int(6*u))
    return finish(glow(img, acc, 6*u), n)

def stylize(img, pal):
    """Post-pass for styles that change the whole icon: line-art outline or monochrome."""
    if ICON_STYLE == 'outline':
        from PIL import ImageOps
        a = img.split()[3].point(lambda v: 255 if v > 90 else 0)
        inner = a.filter(ImageFilter.MinFilter(9))
        edge = ImageChops.subtract(a, inner)
        # inner details: where flat colour regions meet
        gray = ImageOps.grayscale(img.convert('RGB')).filter(ImageFilter.FIND_EDGES).point(lambda v: 255 if v > 18 else 0)
        detail = ImageChops.multiply(gray.filter(ImageFilter.MaxFilter(5)), inner.filter(ImageFilter.MinFilter(5)))
        edge = ImageChops.lighter(edge, detail)
        out = Image.new('RGBA', img.size, rgba(hex2rgb(pal['accent']), 0)); out.putalpha(inner.point(lambda v: v * 22 // 255))
        line = Image.new('RGBA', img.size, rgba(hex2rgb(pal['accent']))); line.putalpha(edge)
        return Image.alpha_composite(out, line)
    if ICON_STYLE == 'mono':
        from PIL import ImageOps
        gray = ImageOps.grayscale(img.convert('RGB'))
        light = pal['bg'] if sum(hex2rgb(pal['bg'])) < 384 else pal['text']
        dark = pal['text'] if light == pal['bg'] else pal['bg']
        col = ImageOps.colorize(gray, black=mix(hex2rgb(dark), hex2rgb(light), .25), white=hex2rgb(light) if False else mix(hex2rgb(light), (255, 255, 255), .1)).convert('RGBA')
        col.putalpha(img.split()[3]); return col
    return img

ICO_SIZES = [(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (96, 96), (128, 128), (256, 256)]
def save_ico(img, path, pal=None): (stylize(img, pal) if pal else img).save(path, format='ICO', sizes=ICO_SIZES)

# ------------------------------------------------------------------ cursors
def cur_bytes(frames):
    """frames: list of (PIL RGBA image, hotspot(x,y)) at different sizes -> .cur bytes. <=48px as DIB, larger as PNG."""
    entries, blobs = [], []
    for im, (hx, hy) in frames:
        w, h = im.size
        if w > 48:
            b = io.BytesIO(); im.save(b, 'PNG'); data = b.getvalue()
        else:
            px = im.tobytes('raw', 'BGRA')
            rows = [px[y * w * 4:(y + 1) * w * 4] for y in range(h)][::-1]
            xor = b''.join(rows)
            mask_row = ((w + 31) // 32) * 4
            andm = bytes(mask_row * h)  # alpha channel carries transparency
            hdr = struct.pack('<IiiHHIIiiII', 40, w, h * 2, 1, 32, 0, len(xor) + len(andm), 0, 0, 0, 0)
            data = hdr + xor + andm
        entries.append((w if w < 256 else 0, h if h < 256 else 0, hx, hy, len(data))); blobs.append(data)
    out = struct.pack('<HHH', 0, 2, len(frames)); off = 6 + 16 * len(frames)
    for (w, h, hx, hy, ln) in entries:
        out += struct.pack('<BBBBHHII', w, h, 0, 0, hx, hy, ln, off); off += ln
    return out + b''.join(blobs)

def ani_bytes(frame_curs, rate=4):
    def chunk(tag, data): return tag + struct.pack('<I', len(data)) + data + (b'\0' if len(data) % 2 else b'')
    w = 32
    anih = struct.pack('<IIIIIIIII', 36, len(frame_curs), len(frame_curs), 0, 0, 0, 0, rate, 1)
    fram = b'fram' + b''.join(chunk(b'icon', c) for c in frame_curs)
    body = b'ACON' + chunk(b'anih', anih) + chunk(b'LIST', fram)
    return b'RIFF' + struct.pack('<I', len(body)) + body

class Pen:
    """Draws a neon cursor glyph on a 32-unit grid."""
    def __init__(self, n, fill, line, glowc, shadow=False):
        self.n = n; self.S = n * SS_CUR; self.u = self.S / 32; self.shadow = shadow
        self.shape = Image.new('L', (self.S, self.S), 0); self.d = ImageDraw.Draw(self.shape)
        self.cfill, self.cline, self.cglow = fill, line, glowc
    def poly(self, pts): self.d.polygon([(x * self.u, y * self.u) for x, y in pts], fill=255)
    def rect(self, x0, y0, x1, y1, r=0): self.d.rounded_rectangle([x0 * self.u, y0 * self.u, x1 * self.u, y1 * self.u], r * self.u, fill=255)
    def ellipse(self, x0, y0, x1, y1, w=None):
        if w: self.d.ellipse([x0 * self.u, y0 * self.u, x1 * self.u, y1 * self.u], outline=255, width=int(w * self.u))
        else: self.d.ellipse([x0 * self.u, y0 * self.u, x1 * self.u, y1 * self.u], fill=255)
    def arc(self, x0, y0, x1, y1, a0, a1, w): self.d.arc([x0 * self.u, y0 * self.u, x1 * self.u, y1 * self.u], a0, a1, fill=255, width=int(w * self.u))
    def line(self, pts, w): self.d.line([(x * self.u, y * self.u) for x, y in pts], fill=255, width=int(w * self.u), joint='curve')
    def render(self):
        S, u = self.S, self.u
        outline = self.shape.filter(ImageFilter.GaussianBlur(1.1 * u)).point(lambda v: 255 if v > 40 else v * 6)
        img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        if self.shadow:
            g = outline.filter(ImageFilter.GaussianBlur(1.2 * u)).point(lambda v: int(v * .5))
            sh = Image.new('RGBA', (S, S), (0, 0, 0, 0)); sh.putalpha(g)
            img.paste(sh, (int(.8 * u), int(1.2 * u)))
        else:
            g = outline.filter(ImageFilter.GaussianBlur(1.6 * u))
            gl = Image.new('RGBA', (S, S), rgba(self.cglow)); gl.putalpha(g.point(lambda v: int(v * .75)))
            img.alpha_composite(gl)
        ol = Image.new('RGBA', (S, S), rgba(self.cline)); ol.putalpha(outline); img.alpha_composite(ol)
        fl = Image.new('RGBA', (S, S), rgba(self.cfill)); fl.putalpha(self.shape); img.alpha_composite(fl)
        return img.resize((self.n, self.n), Image.LANCZOS)

ARROW = [(2, 2), (2, 22.5), (7, 18), (10.5, 26), (13.5, 24.7), (10.2, 17), (17, 17)]
def g_arrow(p, s): p.poly([(x * s, y * s) for x, y in ARROW])
def g_double(p, angle):
    pts = [(16, 3), (22, 9), (18.2, 9), (18.2, 23), (22, 23), (16, 29), (10, 23), (13.8, 23), (13.8, 9), (10, 9)]
    a = math.radians(angle); c, s_ = math.cos(a), math.sin(a)
    p.poly([(16 + (x - 16) * c - (y - 16) * s_, 16 + (x - 16) * s_ + (y - 16) * c) for x, y in pts])

def glyph(role, p, frame=0, frames=1):
    if role == 'Arrow': g_arrow(p, 1)
    elif role == 'Help':
        g_arrow(p, .82); p.arc(17, 12, 27, 22, 180, 400, 2.6); p.line([(22, 21.5), (22, 24.5)], 2.6); p.ellipse(20.6, 26.2, 23.4, 29)
    elif role == 'AppStarting':
        g_arrow(p, .82)
        a0 = (frame / frames) * 360; p.arc(17, 17, 29, 29, a0, a0 + 270, 2.8)
    elif role == 'Wait':
        a0 = (frame / frames) * 360; p.arc(5, 5, 27, 27, a0, a0 + 280, 3.6); p.ellipse(13.5, 13.5, 18.5, 18.5)
    elif role == 'Crosshair':
        p.line([(16, 2), (16, 12)], 2.2); p.line([(16, 20), (16, 30)], 2.2); p.line([(2, 16), (12, 16)], 2.2); p.line([(20, 16), (30, 16)], 2.2); p.ellipse(14.6, 14.6, 17.4, 17.4)
    elif role == 'IBeam':
        p.line([(16, 5), (16, 27)], 2.4); p.line([(11.5, 4), (20.5, 4)], 2.4); p.line([(11.5, 28), (20.5, 28)], 2.4)
    elif role == 'NWPen':
        p.poly([(3, 29), (5, 22), (24, 3), (29, 8), (10, 27)])
    elif role == 'No':
        p.ellipse(4, 4, 28, 28, w=3.6); p.line([(8.5, 23.5), (23.5, 8.5)], 3.6)
    elif role == 'SizeNS': g_double(p, 0)
    elif role == 'SizeWE': g_double(p, 90)
    elif role == 'SizeNWSE': g_double(p, -45)
    elif role == 'SizeNESW': g_double(p, 45)
    elif role == 'SizeAll':
        p.poly([(16, 2), (21, 7.5), (17.6, 7.5), (17.6, 14.4), (24.5, 14.4), (24.5, 11), (30, 16), (24.5, 21), (24.5, 17.6), (17.6, 17.6), (17.6, 24.5), (21, 24.5), (16, 30),
                (11, 24.5), (14.4, 24.5), (14.4, 17.6), (7.5, 17.6), (7.5, 21), (2, 16), (7.5, 11), (7.5, 14.4), (14.4, 14.4), (14.4, 7.5), (11, 7.5)])
    elif role == 'UpArrow':
        p.poly([(16, 2), (25, 12), (19, 12), (19, 29), (13, 29), (13, 12), (7, 12)])
    elif role == 'Hand':
        p.rect(10, 2, 15, 18, 2.5)            # index finger
        p.rect(15, 9, 19.5, 18, 2.2); p.rect(19.5, 10.5, 24, 19, 2.2); p.rect(24, 12, 28, 20, 2)
        p.rect(10, 14, 28, 27, 5)             # palm
        p.poly([(4.5, 15), (7.5, 13), (12, 19), (11, 23)])  # thumb
    elif role == 'Pin':
        g_arrow(p, .78); p.ellipse(17, 14, 29, 26, w=2.6); p.poly([(18.5, 23), (27.5, 23), (23, 31)])
    elif role == 'Person':
        g_arrow(p, .78); p.ellipse(19.5, 13, 26.5, 20); p.d.chord([16.5 * p.u, 21 * p.u, 29.5 * p.u, 36 * p.u], 180, 360, fill=255)

HOT = {'Arrow': (2, 2), 'Help': (2, 2), 'AppStarting': (2, 2), 'Wait': (16, 16), 'Crosshair': (16, 16), 'IBeam': (16, 16), 'NWPen': (3, 29), 'No': (16, 16),
       'SizeNS': (16, 16), 'SizeWE': (16, 16), 'SizeNWSE': (16, 16), 'SizeNESW': (16, 16), 'SizeAll': (16, 16), 'UpArrow': (16, 2), 'Hand': (12, 2), 'Pin': (2, 2), 'Person': (2, 2)}
CUR_SIZES = [32, 48, 64, 96]
SS_CUR = 3

def make_cursor_frames(role, colors, frame=0, frames=1, sizes=None):
    fill, line, gl, shadow = colors
    out = []
    for n in (sizes or CUR_SIZES):
        pen = Pen(n, fill, line, gl, shadow); glyph(role, pen, frame, frames)
        hx, hy = HOT[role]
        out.append((pen.render(), (int(round(hx * n / 32)), int(round(hy * n / 32)))))
    return out

def build_cursor_pack(dirpath, colors, name):
    os.makedirs(dirpath, exist_ok=True)
    for role in HOT:
        if role in ('Wait', 'AppStarting'):
            fr = 8
            curs = [cur_bytes(make_cursor_frames(role, colors, i, fr, [32, 48, 64])) for i in range(fr)]
            open(os.path.join(dirpath, role + '.ani'), 'wb').write(ani_bytes(curs, 6))
        else:
            open(os.path.join(dirpath, role + '.cur'), 'wb').write(cur_bytes(make_cursor_frames(role, colors)))
    pv = Pen(96, *colors); glyph('Arrow', pv); pv.render().save(os.path.join(dirpath, 'preview.png'))
    json.dump({'name': name, 'color': '#%02x%02x%02x' % colors[2]}, open(os.path.join(dirpath, 'pack.json'), 'w'), indent=2)

# ------------------------------------------------------------------ app icon
def app_icon():
    n = 256; S = n * SS; u = S / 256
    c1, c2 = hex2rgb('#00f0ff'), hex2rgb('#ff2bd6')
    img = canvas(n)
    bgm = Image.new('L', (S, S), 0); ImageDraw.Draw(bgm).rounded_rectangle([12*u, 12*u, 244*u, 244*u], 56*u, fill=255)
    img.alpha_composite(fill_grad(bgm, (18, 16, 36), (6, 6, 12)))
    m = Image.new('L', (S, S), 0); d = ImageDraw.Draw(m)
    d.polygon([(52*u, 62*u), (204*u, 62*u), (204*u, 96*u), (146*u, 96*u), (146*u, 202*u), (110*u, 202*u), (110*u, 96*u), (52*u, 96*u)], fill=255)
    d.line([(78*u, 196*u), (104*u, 170*u)], fill=255, width=int(14*u)); d.line([(178*u, 196*u), (152*u, 170*u)], fill=255, width=int(14*u))
    g = fill_grad(m, c1, c2) if False else None
    gg = grad((S, S), c1, c2, vertical=False); t = Image.new('RGBA', (S, S), (0, 0, 0, 0)); t.paste(gg, (0, 0), m)
    img = glow(img, c1, 2) ; img.alpha_composite(glow(t, c1, 10*u))
    ImageDraw.Draw(img).rounded_rectangle([12*u, 12*u, 244*u, 244*u], 56*u, outline=(255, 255, 255, 40), width=int(3*u))
    img = finish(img, n)
    img.save(os.path.join(A, 'app.ico'), format='ICO', sizes=ICO_SIZES)
    img.save(os.path.join(A, 'app.png'))

def main():
    os.makedirs(A, exist_ok=True)
    app_icon()
    os.makedirs(os.path.join(A, 'icons-extra'), exist_ok=True)
    Image.new('RGBA', (32, 32), (0, 0, 0, 0)).save(os.path.join(A, 'icons-extra', 'blank.ico'), format='ICO', sizes=[(16, 16), (32, 32), (48, 48)])
    global ICON_STYLE
    presets = []
    CUR_LABEL = {'neon': 'Neon', 'light': 'Light', 'dark': 'Dark', 'accent': 'Color'}
    for (pid, name, desc, (bg, surf, text, acc, acc2), mode, istyle, dstyle, cstyle, snd, accStart, accTitle, transp, wp) in PRESETS:
        pal = {'bg': bg, 'surface': surf, 'text': text, 'accent': acc, 'accent2': acc2}
        ICON_STYLE = istyle
        idir = os.path.join(A, 'icons', pid); os.makedirs(idir, exist_ok=True)
        save_ico(icon_folder(pal), os.path.join(idir, 'folder.ico'), pal)
        save_ico(icon_folder(pal, True), os.path.join(idir, 'folderOpen.ico'), pal)
        save_ico(icon_drive(pal, dstyle), os.path.join(idir, 'drive.ico'), pal)
        save_ico(icon_pc(pal), os.path.join(idir, 'thispc.ico'), pal)
        save_ico(icon_bin(pal, False), os.path.join(idir, 'recycleEmpty.ico'), pal)
        save_ico(icon_bin(pal, True), os.path.join(idir, 'recycleFull.ico'), pal)
        save_ico(icon_globe(pal), os.path.join(idir, 'network.ico'), pal)
        save_ico(icon_user(pal), os.path.join(idir, 'user.ico'), pal)
        json.dump({'name': name, 'desc': f'{istyle.capitalize()} icons · {dstyle} drives'}, open(os.path.join(idir, 'pack.json'), 'w'), indent=2)

        A_, T_, S_ = hex2rgb(acc), hex2rgb(text), hex2rgb(surf)
        colors = {
            'neon':   (mix(S_, (0, 0, 0), .45), A_, A_, False),
            'light':  ((255, 255, 255), mix(T_, (0, 0, 0), .4), A_, True),
            'dark':   ((16, 16, 20), (240, 240, 245), A_, True),
            'accent': (A_, (255, 255, 255), A_, True),
        }[cstyle]
        build_cursor_pack(os.path.join(A, 'cursors', 'tc-' + pid), colors, name + ' ' + CUR_LABEL[cstyle])

        presets.append({
            'id': pid, 'name': name, 'desc': desc, 'author': 'TRANSCRYPTO', 'tags': [mode, istyle + ' icons', CUR_LABEL[cstyle].lower() + ' cursors'],
            'palette': pal,
            'colors': {'systemMode': mode, 'appsMode': mode, 'accent': acc.upper(), 'autoAccent': False, 'transparency': transp, 'accentOnStart': accStart and mode == 'dark', 'accentOnTitlebars': accTitle},
            'wallpaper': {'type': 'solid', 'color': wp},
            'cursors': {'pack': 'pack:tc-' + pid},
            'icons': {'pack': pid, 'shortcutArrow': True},
            'sounds': {'scheme': snd},
        })
    ICON_STYLE = 'glow'
    # extra drive-only styles so every style exists for every taste
    presets.append({
        'id': 'windows-11', 'name': 'Windows 11 Default', 'desc': 'Stock Windows 11 look — light mode, blue accent, Bloom wallpaper.', 'author': 'Microsoft', 'tags': ['light', 'stock'],
        'palette': {'bg': '#cfe3f7', 'surface': '#f3f3f3', 'text': '#1b1b1b', 'accent': '#0078d4', 'accent2': '#4cc2ff'},
        'colors': {'systemMode': 'light', 'appsMode': 'light', 'accent': '#0078D4', 'autoAccent': False, 'transparency': True, 'accentOnStart': False, 'accentOnTitlebars': False},
        'wallpaper': {'type': 'image', 'image': 'C:\\Windows\\Web\\Wallpaper\\Windows\\img0.jpg', 'fit': 'fill'},
        'cursors': {'pack': 'default', 'size': 32}, 'icons': {'pack': 'default', 'shortcutArrow': True}, 'sounds': {'scheme': 'default'},
    })
    os.makedirs(os.path.join(ROOT, 'presets'), exist_ok=True)
    json.dump({'version': 1, 'presets': presets}, open(os.path.join(ROOT, 'presets', 'themes.json'), 'w'), indent=2)
    print('presets:', len(presets))

if __name__ == '__main__':
    main()
