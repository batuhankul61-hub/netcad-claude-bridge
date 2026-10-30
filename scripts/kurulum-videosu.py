"""'Nasıl kurulur?' animasyon videosu: docs/kurulum-videosu.mp4 (1920x1080, 30 fps, sessiz).

Kullanım:
  python scripts/kurulum-videosu.py                 videoyu üretir (ffmpeg gerekir)
  python scripts/kurulum-videosu.py --kareler DIR   her sahneden birkaç kareyi PNG olarak DIR'e yazar (gözden geçirme)
"""
import subprocess
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "kurulum-videosu.mp4"
W, H, FPS = 1920, 1080, 30
FONT_DIR = Path("C:/Windows/Fonts")
FONT_FILES = {"r": "segoeui.ttf", "sb": "seguisb.ttf", "b": "segoeuib.ttf", "l": "segoeuil.ttf",
              "m": "consola.ttf", "mb": "consolab.ttf"}

NAVY = (15, 23, 42)
INK = (15, 23, 42)
GRAY = (71, 85, 105)
MUTED = (148, 163, 184)
LINE = (203, 213, 225)
BLUE = (37, 99, 235)
SKY = (147, 197, 253)
GREEN = (22, 163, 74)
TERM_GREEN = (74, 222, 128)
ORANGE = (217, 119, 87)
WHITE = (255, 255, 255)
YELLOW = (250, 204, 21)

URL = "github.com/batuhankul61-hub/netcad-claude-bridge"

# ---------------------------------------------------------------- temel yardımcılar

_fonts = {}


def font(w, size):
    key = (w, size)
    if key not in _fonts:
        _fonts[key] = ImageFont.truetype(str(FONT_DIR / FONT_FILES[w]), size)
    return _fonts[key]


def clamp(x, a=0.0, b=1.0):
    return max(a, min(b, x))


def ease(x):
    x = clamp(x)
    return 4 * x * x * x if x < 0.5 else 1 - (-2 * x + 2) ** 3 / 2


def p(t, a, b):
    """t, a..b aralığında 0'dan 1'e yumuşak geçer."""
    return ease((t - a) / (b - a))


def lerp(a, b, k):
    return a + (b - a) * k


def typed(s, t, a, b):
    return s[:int(round(len(s) * clamp((t - a) / (b - a))))]


def txt(d, xy, s, size, w="r", fill=INK, anchor="la"):
    d.text(xy, s, font=font(w, size), fill=fill, anchor=anchor)


def tw(s, size, w="r"):
    return font(w, size).getlength(s)


def fade_in(img, k, fn):
    """fn(img) ile çizilen öğeyi k saydamlığıyla karışık gösterir."""
    if k <= 0:
        return
    if k >= 1:
        fn(img)
        return
    before = img.copy()
    fn(img)
    img.paste(Image.blend(before, img, k))


_shadows = {}


def shadow(img, box, r=12, strength=110, blur=18, dy=12):
    x0, y0, x1, y1 = [int(v) for v in box]
    w, h = x1 - x0, y1 - y0
    key = (w, h, r, strength, blur)
    if key not in _shadows:
        pad = blur * 3
        m = Image.new("L", (w + 2 * pad, h + 2 * pad), 0)
        ImageDraw.Draw(m).rounded_rectangle((pad, pad, pad + w, pad + h), r, fill=strength)
        _shadows[key] = (m.filter(ImageFilter.GaussianBlur(blur)), pad)
    m, pad = _shadows[key]
    img.paste((0, 0, 0), (x0 - pad, y0 - pad + dy), m)


def gradient(c00, c10, c01, c11):
    g = Image.new("RGB", (2, 2))
    g.putdata([c00, c10, c01, c11])
    return g.resize((W, H), Image.BILINEAR)


def _cursor():
    s, m = 4, 3
    pts = [(0, 0), (0, 32), (8, 25), (13, 37), (18, 35), (13, 23), (23, 23)]
    big = Image.new("RGBA", ((26 + 2 * m) * s, (40 + 2 * m) * s), (0, 0, 0, 0))
    ImageDraw.Draw(big).polygon([((x + m) * s, (y + m) * s) for x, y in pts], fill=WHITE, outline=(0, 0, 0), width=2 * s)
    return big.resize((big.width // s, big.height // s), Image.LANCZOS), m


CURSOR, CURSOR_M = _cursor()


def ripple(img, x, y, k, color=BLUE):
    r = int(14 + 34 * k)
    size = 2 * r + 10
    lay = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    ImageDraw.Draw(lay).ellipse((5, 5, size - 5, size - 5), outline=color + (int(220 * (1 - k)),), width=5)
    img.paste(lay, (int(x) - size // 2, int(y) - size // 2), lay)


def cursor(img, t, keys, clicks=(), right=()):
    """keys: [(zaman, (x, y)), ...]; clicks/right: sol/sağ tık zamanları."""
    pos = keys[0][1]
    for (ta, pa), (tb, pb) in zip(keys, keys[1:]):
        if t >= tb:
            pos = pb
        elif t > ta:
            k = ease((t - ta) / (tb - ta))
            pos = (lerp(pa[0], pb[0], k), lerp(pa[1], pb[1], k))
            break
    for c in clicks:
        if c <= t < c + 0.5:
            ripple(img, *pos, (t - c) / 0.5)
    for c in right:
        if c <= t < c + 0.5:
            ripple(img, *pos, (t - c) / 0.5, ORANGE)
    img.paste(CURSOR, (int(pos[0]) - CURSOR_M, int(pos[1]) - CURSOR_M), CURSOR)


def caption(img, t, num, text, note=None):
    def draw(im):
        d = ImageDraw.Draw(im)
        size = 32
        w = 24 + 52 + 22 + tw(text, size, "sb") + 36
        x0, y0 = 960 - w / 2, 922
        shadow(im, (x0, y0, x0 + w, y0 + 80), 40, 90, 14, 6)
        d.rounded_rectangle((x0, y0, x0 + w, y0 + 80), 40, fill=NAVY)
        cx = x0 + 24 + 26
        d.ellipse((cx - 26, y0 + 14, cx + 26, y0 + 66), fill=ORANGE if num == "B" else BLUE)
        txt(d, (cx, y0 + 40), num, 28, "b", WHITE, "mm")
        txt(d, (cx + 26 + 22, y0 + 40), text, size, "sb", WHITE, "lm")
        if note:
            nw = tw(note, 22) + 40
            nx = 960 - nw / 2
            d.rounded_rectangle((nx, y0 - 52, nx + nw, y0 - 12), 20, fill=(30, 41, 59))
            txt(d, (960, y0 - 32), note, 22, "r", (203, 213, 225), "mm")
    fade_in(img, p(t, 0.2, 0.7), draw)


# ---------------------------------------------------------------- arka planlar

TITLE_BG = gradient((15, 23, 42), (30, 41, 82), (12, 18, 36), (20, 30, 60))
FADE_BG = Image.new("RGB", (W, H), (12, 18, 36))


def _desktop():
    img = gradient((30, 58, 138), (37, 99, 235), (14, 116, 144), (56, 189, 248))
    glow = Image.new("L", (W, H), 0)
    ImageDraw.Draw(glow).ellipse((1100, -300, 2300, 800), fill=70)
    img.paste((255, 255, 255), (0, 0), glow.filter(ImageFilter.GaussianBlur(160)))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 1024, W, H), fill=(17, 24, 39))
    x = 960 - 3 * 56
    for kind in ("start", "search", "folder", "browser", "term", "netcad"):
        b = (x, 1034, x + 36, 1070)
        if kind == "start":
            for i in range(2):
                for j in range(2):
                    d.rectangle((x + 3 + j * 16, 1037 + i * 16, x + 17 + j * 16, 1051 + i * 16), fill=(96, 165, 250))
        elif kind == "search":
            d.ellipse((x + 6, 1040, x + 26, 1060), outline=WHITE, width=3)
            d.line((x + 24, 1058, x + 32, 1066), fill=WHITE, width=3)
        elif kind == "folder":
            icon(d, "folder", x + 2, 1036, 32)
        elif kind == "browser":
            d.ellipse(b, fill=(59, 130, 246))
            d.ellipse((x + 10, 1044, x + 26, 1060), fill=WHITE)
        elif kind == "term":
            d.rounded_rectangle(b, 6, fill=(40, 40, 40))
            txt(d, (x + 18, 1052), ">_", 16, "mb", WHITE, "mm")
        else:
            d.rounded_rectangle(b, 6, fill=(234, 88, 12))
            txt(d, (x + 18, 1052), "N", 22, "b", WHITE, "mm")
        x += 56
    txt(d, (1880, 1043), "14:12", 18, "r", WHITE, "rm")
    txt(d, (1880, 1063), "30.10.2026", 16, "r", (203, 213, 225), "rm")
    return img


def icon(d, kind, x, y, s=28):
    if kind in ("folder", "zip"):
        d.rounded_rectangle((x, y + s * 0.12, x + s * 0.45, y + s * 0.34), 3, fill=(217, 150, 20))
        d.rounded_rectangle((x, y + s * 0.24, x + s, y + s * 0.9), 3, fill=(251, 191, 36))
        if kind == "zip":
            cx = x + s * 0.5
            for i in range(4):
                yy = y + s * (0.3 + i * 0.14)
                d.rectangle((cx - 3 + (i % 2) * 3, yy, cx + (i % 2) * 3, yy + s * 0.08), fill=(120, 53, 15))
    elif kind == "bat":
        d.rounded_rectangle((x + 1, y + 3, x + s - 1, y + s - 3), 3, fill=(229, 231, 235), outline=(107, 114, 128))
        d.rectangle((x + 2, y + 4, x + s - 2, y + 9), fill=(55, 65, 81))
        d.ellipse((x + s * 0.32, y + s * 0.42, x + s * 0.68, y + s * 0.78), outline=BLUE, width=3)
    elif kind == "txt":
        d.rectangle((x + 4, y, x + s - 4, y + s), fill=WHITE, outline=(148, 163, 184))
        for i in range(4):
            yy = y + 6 + i * 5
            d.line((x + 8, yy, x + s - 8, yy), fill=(148, 163, 184), width=1)


DESKTOP = _desktop()


# ---------------------------------------------------------------- pencereler

def window(img, box, title, dark=False, close_hover=False):
    d = ImageDraw.Draw(img)
    x0, y0, x1, y1 = box
    shadow(img, box)
    body, bar = ((12, 12, 12), (31, 31, 31)) if dark else (WHITE, (243, 244, 246))
    fg = (229, 231, 235) if dark else INK
    d.rounded_rectangle(box, 12, fill=body, outline=(60, 60, 60) if dark else LINE)
    d.rounded_rectangle((x0, y0, x1, y0 + 44), 12, fill=bar)
    d.rectangle((x0 + 1, y0 + 30, x1 - 1, y0 + 44), fill=bar)
    txt(d, (x0 + 20, y0 + 22), title, 19, "r", fg, "lm")
    if close_hover:
        d.rounded_rectangle((x1 - 46, y0, x1, y0 + 44), 12, fill=(232, 17, 35))
        d.rectangle((x1 - 46, y0, x1 - 30, y0 + 44), fill=(232, 17, 35))
        d.rectangle((x1 - 46, y0 + 30, x1, y0 + 44), fill=(232, 17, 35))
    cx = x1 - 23
    xc = WHITE if close_hover else fg
    d.line((cx - 7, y0 + 15, cx + 7, y0 + 29), fill=xc, width=2)
    d.line((cx - 7, y0 + 29, cx + 7, y0 + 15), fill=xc, width=2)
    d.rectangle((x1 - 76, y0 + 15, x1 - 62, y0 + 29), outline=fg, width=2)
    d.line((x1 - 122, y0 + 22, x1 - 108, y0 + 22), fill=fg, width=2)
    return (x0, y0 + 44, x1, y1)


def explorer(img, box, title, path, rows, tint=None, nav_sel="İndirilenler"):
    """rows: [(tür, ad, tarih, dosya türü, boyut)]. tint: {ad: renk}. Satırların y merkezlerini döndürür."""
    tint = tint or {}
    cx0, cy0, cx1, cy1 = window(img, box, title)
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((cx0 + 16, cy0 + 12, cx1 - 16, cy0 + 52), 8, fill=(248, 250, 252), outline=LINE)
    txt(d, (cx0 + 34, cy0 + 32), path, 20, "r", GRAY, "lm")
    d.rectangle((cx0 + 1, cy0 + 64, cx0 + 260, cy1 - 12), fill=(248, 250, 252))
    for i, (kind, name) in enumerate((("folder", "Masaüstü"), ("folder", "İndirilenler"), ("folder", "Belgeler"),
                                      ("folder", "Bu bilgisayar"))):
        y = cy0 + 96 + i * 46
        if name == nav_sel:
            d.rounded_rectangle((cx0 + 10, y - 20, cx0 + 250, y + 20), 6, fill=(219, 234, 254))
        icon(d, kind, cx0 + 24, y - 12, 24)
        txt(d, (cx0 + 60, y), name, 20, "r", INK, "lm")
    nx, dx, tx, sx = cx0 + 330, cx0 + 780, cx0 + 980, cx1 - 30
    hy = cy0 + 92
    for x, s, a in ((nx, "Ad", "lm"), (dx, "Değiştirme tarihi", "lm"), (tx, "Tür", "lm"), (sx, "Boyut", "rm")):
        txt(d, (x, hy), s, 18, "r", GRAY, a)
    d.line((cx0 + 290, hy + 20, cx1 - 16, hy + 20), fill=LINE)
    ys = []
    for i, (kind, name, date, typ, size) in enumerate(rows):
        y = hy + 56 + i * 52
        ys.append(y)
        if name in tint:
            d.rounded_rectangle((cx0 + 290, y - 23, cx1 - 16, y + 23), 6, fill=tint[name])
        icon(d, kind, nx - 42, y - 14, 28)
        txt(d, (nx, y), name, 21, "r", INK, "lm")
        txt(d, (dx, y), date, 19, "r", GRAY, "lm")
        txt(d, (tx, y), typ, 19, "r", GRAY, "lm")
        txt(d, (sx, y), size, 19, "r", GRAY, "rm")
    return ys


def parcel_points(cv):
    x0, y0, x1, y1 = cv
    fr = [(0.20, 0.24), (0.70, 0.18), (0.78, 0.80), (0.16, 0.86)]
    return [(lerp(x0, x1, a), lerp(y0, y1, b)) for a, b in fr]


def netcad(img, box, title, panel_w=0, close_hover=False):
    cx0, cy0, cx1, cy1 = window(img, box, title, close_hover=close_hover)
    d = ImageDraw.Draw(img)
    d.rectangle((cx0 + 1, cy0, cx1 - 1, cy0 + 84), fill=(243, 244, 246))
    x = cx0 + 20
    for i, s in enumerate(("Dosya", "Giriş", "Çizim", "Düzenle", "Uygula", "Hesap", "Görünüm")):
        txt(d, (x, cy0 + 16), s, 17, "sb" if i == 1 else "r", BLUE if i == 1 else GRAY)
        x += tw(s, 17) + 30
    for i in range(16):
        bx = cx0 + 20 + i * 52
        d.rounded_rectangle((bx, cy0 + 44, bx + 40, cy0 + 76), 5, fill=(229, 231, 235))
    cv = (cx0 + 1, cy0 + 84, cx1 - 1 - panel_w, cy1 - 1)
    d.rectangle(cv, fill=(10, 10, 10))
    vx0, vy0, vx1, vy1 = cv
    d.line((vx0, lerp(vy0, vy1, 0.95), vx1, lerp(vy0, vy1, 0.9)), fill=(90, 90, 90), width=2)
    d.line((lerp(vx0, vx1, 0.9), vy0, lerp(vx0, vx1, 0.86), vy1), fill=(90, 90, 90), width=2)
    pts = parcel_points(cv)
    d.line(pts + [pts[0]], fill=YELLOW, width=3, joint="curve")
    return cv, (cx1 - panel_w, cy0 + 84, cx1 - 1, cy1 - 1)


def console(img, box, title, lines, t, start, step):
    """Satırları sırayla gösterir: lines = [(metin, renk)]."""
    cx0, cy0, cx1, cy1 = window(img, box, title, dark=True)
    d = ImageDraw.Draw(img)
    shown = 0
    for i, (s, c) in enumerate(lines):
        if t < start + i * step:
            break
        txt(d, (cx0 + 24, cy0 + 26 + i * 40), s, 26, "m", c)
        shown = i + 1
    if int(t * 2.5) % 2 == 0:
        i = max(shown - 1, 0)
        last = lines[i][0] if shown else ""
        x = cx0 + 24 + tw(last, 26, "m")
        y = cy0 + 26 + i * 40
        d.rectangle((x + 4, y + 4, x + 18, y + 30), fill=(229, 231, 235))
    return cx0, cy0, cx1, cy1


def keycap(img, x, y, label, k):
    def draw(im):
        d = ImageDraw.Draw(im)
        w = tw(label, 26, "sb") + 48
        down = 4 if k > 0.6 else 0
        d.rounded_rectangle((x - w / 2, y + 6, x + w / 2, y + 66), 12, fill=(148, 163, 184))
        d.rounded_rectangle((x - w / 2, y + down, x + w / 2, y + 58 + down), 12, fill=WHITE, outline=(148, 163, 184), width=2)
        txt(d, (x, y + 29 + down), label, 26, "sb", INK, "mm")
    fade_in(img, clamp(k * 3), draw)


# ---------------------------------------------------------------- sahneler

def s_title(t):
    img = TITLE_BG.copy()
    d = ImageDraw.Draw(img)
    k = p(t, 0.2, 1.0)
    fade_in(img, k, lambda im: txt(ImageDraw.Draw(im), (960, 420 + (1 - k) * 40), "Kul AI · NETCAD", 116, "b", WHITE, "mm"))
    lw = 520 * p(t, 0.6, 1.4)
    d.rounded_rectangle((960 - lw / 2, 500, 960 + lw / 2, 506), 3, fill=ORANGE)
    k2 = p(t, 0.8, 1.6)
    fade_in(img, k2, lambda im: txt(ImageDraw.Draw(im), (960, 580 + (1 - k2) * 30), "Nasıl kurulur?", 68, "sb", SKY, "mm"))
    k3 = p(t, 1.4, 2.2)
    fade_in(img, k3, lambda im: txt(ImageDraw.Draw(im), (960, 680), "İndir  ·  Çıkart  ·  Çift tıkla", 34, "r", MUTED, "mm"))
    return img


def s_download(t):
    img = DESKTOP.copy()
    B = (220, 80, 1700, 880)
    cx0, cy0, cx1, cy1 = window(img, B, "Releases · batuhankul61-hub/netcad-claude-bridge")
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((cx0 + 16, cy0 + 12, cx1 - 16, cy0 + 56), 22, fill=(241, 245, 249))
    d.ellipse((cx0 + 36, cy0 + 27, cx0 + 50, cy0 + 41), fill=GREEN)
    txt(d, (cx0 + 66, cy0 + 34), URL + "/releases", 21, "r", GRAY, "lm")
    x = 320
    txt(d, (x, 220), "Kul AI NETCAD 0.4.1 (beta)", 46, "b", INK)
    bx = x + tw("Kul AI NETCAD 0.4.1 (beta)", 46, "b") + 24
    d.rounded_rectangle((bx, 232, bx + 150, 270), 19, outline=(217, 119, 6), width=2)
    txt(d, (bx + 75, 251), "Pre-release", 19, "sb", (180, 83, 9), "mm")
    txt(d, (x, 296), "v0.4.1  ·  batuhankul61-hub", 22, "r", GRAY)
    txt(d, (x, 360), "İlk herkese açık beta.", 24, "r", INK)
    txt(d, (x, 400), "Zip'i indirip çıkartın; Kur.bat ya da Claude-Bagla.bat'a çift tıklayın.", 24, "r", INK)
    A = (320, 470, 1600, 780)
    d.rounded_rectangle(A, 10, outline=LINE, width=2)
    txt(d, (344, 506), "Assets", 26, "b", INK, "lm")
    d.rounded_rectangle((440, 492, 476, 520), 14, fill=(229, 231, 235))
    txt(d, (458, 506), "3", 18, "sb", GRAY, "mm")
    rows = (("KulAI-Netcad-Beta-0.4.1.zip", "3.6 MB"), ("Source code (zip)", ""), ("Source code (tar.gz)", ""))
    for i, (name, size) in enumerate(rows):
        y = 586 + i * 64
        d.line((320, y - 32, 1600, y - 32), fill=LINE)
        if i == 0 and t > 1.9:
            d.rectangle((322, y - 31, 1598, y + 31), fill=(239, 246, 255))
        icon(d, "zip", 350, y - 14, 28)
        txt(d, (396, y), name, 24, "sb" if i == 0 else "r", BLUE if i == 0 else INK, "lm")
        if i == 0 and t > 1.9:
            d.line((396, y + 16, 396 + tw(name, 24, "sb"), y + 16), fill=BLUE, width=2)
        txt(d, (1570, y), size, 21, "r", GRAY, "rm")

    k = p(t, 2.6, 3.0)

    def pill(im):
        dd = ImageDraw.Draw(im)
        P = (1220, 140 - (1 - k) * 20, 1670, 250 - (1 - k) * 20)
        shadow(im, P, 12, 90)
        dd.rounded_rectangle(P, 12, fill=WHITE, outline=LINE)
        icon(dd, "zip", P[0] + 22, P[1] + 22, 32)
        txt(dd, (P[0] + 70, P[1] + 34), "KulAI-Netcad-Beta-0.4.1.zip", 21, "sb", INK, "lm")
        pr = p(t, 2.8, 4.0)
        dd.rounded_rectangle((P[0] + 70, P[1] + 62, P[2] - 24, P[1] + 70), 4, fill=(226, 232, 240))
        dd.rounded_rectangle((P[0] + 70, P[1] + 62, lerp(P[0] + 78, P[2] - 24, pr), P[1] + 70), 4, fill=GREEN if pr >= 1 else BLUE)
        status = "3.6 MB  ·  Tamamlandı" if pr >= 1 else f"{3.6 * pr:.1f} / 3.6 MB"
        txt(dd, (P[0] + 70, P[1] + 90), status, 18, "r", GREEN if pr >= 1 else GRAY, "lm")
    fade_in(img, k, pill)
    cursor(img, t, [(0, (1300, 840)), (0.6, (1300, 840)), (1.9, (560, 590))], clicks=[2.4])
    caption(img, t, "1", "Releases sayfasından zip dosyasını indirin")
    return img


ZIP_ROW = ("zip", "KulAI-Netcad-Beta-0.4.1.zip", "30.10.2026 14:12", "Sıkıştırılmış klasör", "3,6 MB")
DIR_ROW = ("folder", "KulAI-Netcad-Beta-0.4.1", "30.10.2026 14:13", "Dosya klasörü", "")


def s_extract(t):
    img = DESKTOP.copy()
    E = (260, 90, 1660, 860)
    done = t > 5.4
    rows = [DIR_ROW, ZIP_ROW] if done else [ZIP_ROW]
    tint = {}
    if done:
        tint[DIR_ROW[1]] = (220, 252, 231) if t < 6.6 else (219, 234, 254)
    elif 1.5 < t < 5.4:
        tint[ZIP_ROW[1]] = (219, 234, 254)
    ys = explorer(img, E, "İndirilenler", "Bu bilgisayar  ›  İndirilenler", rows, tint)
    menu = (790, ys[0] + 10)
    items = ("Aç", "Tümünü ayıkla...", "Birlikte aç", None, "Kes", "Kopyala", "Sil", "Özellikler")
    if 1.75 < t < 3.15:
        def draw_menu(im):
            dd = ImageDraw.Draw(im)
            x, y = menu
            M = (x, y, x + 330, y + 16 + 46 * 7 + 12)
            shadow(im, M, 8, 90)
            dd.rounded_rectangle(M, 8, fill=WHITE, outline=LINE)
            yy = y + 8
            for it in items:
                if it is None:
                    dd.line((x + 12, yy + 6, x + 318, yy + 6), fill=LINE)
                    yy += 12
                    continue
                if it.startswith("Tümünü") and t > 2.45:
                    dd.rounded_rectangle((x + 6, yy, x + 324, yy + 44), 6, fill=(219, 234, 254))
                txt(dd, (x + 24, yy + 22), it, 21, "sb" if it.startswith("Tümünü") else "r", INK, "lm")
                yy += 46
        fade_in(img, p(t, 1.75, 1.95), draw_menu)
    if 3.25 < t < 5.15:
        def draw_dialog(im):
            dd = ImageDraw.Draw(im)
            D = (600, 290, 1320, 670)
            cx0, cy0, cx1, cy1 = window(im, D, "Sıkıştırılmış (Zip) Klasörleri Ayıkla")
            txt(dd, (cx0 + 32, cy0 + 40), "Hedef seçin ve dosyaları ayıklayın", 28, "sb", (30, 64, 175))
            txt(dd, (cx0 + 32, cy0 + 104), "Dosyalar bu klasöre ayıklanacak:", 20, "r", INK)
            dd.rectangle((cx0 + 32, cy0 + 136, cx1 - 32, cy0 + 176), fill=WHITE, outline=(148, 163, 184))
            txt(dd, (cx0 + 44, cy0 + 156), "C:\\Users\\Kullanıcı\\Downloads\\KulAI-Netcad-Beta-0.4.1", 18, "m", INK, "lm")
            dd.rectangle((cx0 + 32, cy0 + 204, cx0 + 52, cy0 + 224), fill=BLUE)
            dd.line((cx0 + 36, cy0 + 214, cx0 + 41, cy0 + 219, cx0 + 49, cy0 + 208), fill=WHITE, width=3)
            txt(dd, (cx0 + 64, cy0 + 214), "Tamamlandığında ayıklanan dosyaları göster", 19, "r", INK, "lm")
            dd.rounded_rectangle((1040, 600, 1170, 644), 6, fill=BLUE)
            txt(dd, (1105, 622), "Ayıkla", 21, "sb", WHITE, "mm")
            dd.rounded_rectangle((1184, 600, 1296, 644), 6, fill=(243, 244, 246), outline=LINE)
            txt(dd, (1240, 622), "İptal", 21, "r", INK, "mm")
        fade_in(img, min(p(t, 3.25, 3.45), 1 - p(t, 4.95, 5.15)), draw_dialog)
    hover_y = menu[1] + 8 + 46 + 22
    cursor(img, t, [(0, (1250, 720)), (0.4, (1250, 720)), (1.4, (700, ys[-1] if not done else ys[1])),
                    (2.1, (700, ys[0] if not done else ys[1])), (2.6, (900, hover_y)), (3.4, (900, hover_y)),
                    (4.5, (1105, 622)), (5.6, (1105, 622)), (6.4, (1300, 760))],
           clicks=[3.0, 4.8], right=[1.65])
    caption(img, t, "2", "Zip'e sağ tıklayıp “Tümünü ayıkla” deyin", "Zip'in içinden doğrudan çalıştırmayın")
    return img


def s_close(t):
    img = DESKTOP.copy()
    N = (260, 90, 1660, 860)
    k = p(t, 1.9, 2.5)
    if k < 1:
        base = img.copy()
        netcad(img, N, "NETCAD 8.5 - Proje1.ncz", close_hover=t > 1.15)
        if k > 0:
            img = Image.blend(img, base, k)
    cursor(img, t, [(0, (1100, 600)), (0.3, (1100, 600)), (1.3, (1637, 112)), (3.0, (1637, 112)), (3.8, (1400, 420))],
           clicks=[1.7])
    caption(img, t, "3", "NETCAD'i kapatın", "Kaydedilmemiş işiniz varsa önce kaydedin")
    return img


FOLDER_ROWS = [("folder", "KulAI", "30.10.2026 14:13", "Dosya klasörü", ""),
               ("folder", "mcp", "30.10.2026 14:13", "Dosya klasörü", ""),
               ("bat", "Claude-Bagla.bat", "30.10.2026 14:13", "Windows Toplu İş Dosyası", "1 KB"),
               ("bat", "Kaldir.bat", "30.10.2026 14:13", "Windows Toplu İş Dosyası", "1 KB"),
               ("bat", "Kur.bat", "30.10.2026 14:13", "Windows Toplu İş Dosyası", "1 KB"),
               ("txt", "KURULUM VE TEST REHBERI.txt", "30.10.2026 14:13", "Metin Belgesi", "6 KB")]
FOLDER_TITLE = "KulAI-Netcad-Beta-0.4.1"
FOLDER_PATH = "Bu bilgisayar  ›  İndirilenler  ›  KulAI-Netcad-Beta-0.4.1"


def choice_card(im, box, color, file, head, lines):
    d = ImageDraw.Draw(im)
    shadow(im, box, 16, 100)
    d.rounded_rectangle(box, 16, fill=WHITE, outline=color, width=4)
    x, y = box[0] + 32, box[1] + 30
    icon(d, "bat", x, y + 4, 34)
    txt(d, (x + 50, y + 22), file, 32, "b", color, "lm")
    txt(d, (x, y + 74), head, 28, "sb", INK, "lm")
    for i, s in enumerate(lines):
        txt(d, (x, y + 118 + i * 34), s, 22, "r", GRAY, "lm")


def s_choose(t):
    img = DESKTOP.copy()
    E = (260, 90, 1660, 860)
    tint = {}
    if t > 1.0:
        tint["Kur.bat"] = (219, 234, 254)
    if t > 1.7:
        tint["Claude-Bagla.bat"] = (255, 237, 228)
    explorer(img, E, FOLDER_TITLE, FOLDER_PATH, FOLDER_ROWS, tint)
    k1, k2 = p(t, 1.0, 1.5), p(t, 1.7, 2.2)
    fade_in(img, k1, lambda im: choice_card(im, (300, 560 + (1 - k1) * 30, 940, 820 + (1 - k1) * 30), BLUE, "Kur.bat",
                                            "Kul AI asistanı",
                                            ["NETCAD içinde Türkçe yapay zekâ paneli", "Ücretsiz Gemini anahtarı gerekir"]))
    fade_in(img, k2, lambda im: choice_card(im, (980, 560 + (1 - k2) * 30, 1620, 820 + (1 - k2) * 30), ORANGE,
                                            "Claude-Bagla.bat", "Claude Code bağlantısı",
                                            ["Claude, NETCAD'i doğrudan kullanır", "API anahtarı gerekmez"]))
    caption(img, t, "4", "İhtiyacınıza göre birine çift tıklayın")
    return img


def s_smartscreen(t):
    img = DESKTOP.copy()
    E = (260, 90, 1660, 860)
    tint = {"Kur.bat": (219, 234, 254)} if t > 0.9 else {}
    ys = explorer(img, E, FOLDER_TITLE, FOLDER_PATH, FOLDER_ROWS, tint)
    expanded = t > 3.2
    D = (540, 180, 1380, 780)

    def dialog(im):
        dd = ImageDraw.Draw(im)
        shadow(im, D, 4, 140)
        dd.rectangle(D, fill=(0, 103, 184))
        x = D[0] + 56
        dd.line((D[2] - 40, D[1] + 26, D[2] - 24, D[1] + 42), fill=WHITE, width=2)
        dd.line((D[2] - 40, D[1] + 42, D[2] - 24, D[1] + 26), fill=WHITE, width=2)
        txt(dd, (x, D[1] + 64), "Windows bilgisayarınızı korudu", 42, "sb", WHITE)
        body = ("Microsoft Defender SmartScreen tanınmayan bir uygulamanın",
                "başlatılmasını engelledi. Bu uygulamayı çalıştırmak",
                "bilgisayarınızı riske atabilir.")
        for i, s in enumerate(body):
            txt(dd, (x, D[1] + 160 + i * 34), s, 22, "r", WHITE)
        if expanded:
            txt(dd, (x, D[1] + 300), "Uygulama:   Kur.bat", 22, "r", WHITE)
            txt(dd, (x, D[1] + 336), "Yayımcı:      Bilinmeyen yayımcı", 22, "r", WHITE)
            b = (D[2] - 400, D[3] - 78, D[2] - 212, D[3] - 30)
            dd.rectangle(b, outline=WHITE, width=2, fill=(0, 90, 160) if t > 4.2 else None)
            txt(dd, ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2), "Yine de çalıştır", 21, "sb", WHITE, "mm")
        else:
            txt(dd, (x, D[1] + 300), "Ek bilgi", 22, "sb", WHITE)
            dd.line((x, D[1] + 328, x + tw("Ek bilgi", 22, "sb"), D[1] + 328), fill=WHITE, width=2)
        b = (D[2] - 196, D[3] - 78, D[2] - 40, D[3] - 30)
        dd.rectangle(b, fill=(225, 225, 225))
        txt(dd, ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2), "Çalıştırma", 21, "r", INK, "mm")
    fade_in(img, min(p(t, 1.6, 1.9), 1 - p(t, 4.75, 5.15)), dialog)
    kur_y = ys[4]
    run_btn = ((D[2] - 400 + D[2] - 212) / 2, D[3] - 54)
    cursor(img, t, [(0, (1200, 760)), (0.2, (1200, 760)), (0.8, (640, kur_y)), (2.0, (640, kur_y)),
                    (2.8, (D[0] + 100, D[1] + 316)), (3.5, (D[0] + 100, D[1] + 316)), (4.3, run_btn)],
           clicks=[1.0, 1.18, 3.1, 4.6])
    caption(img, t, "5", "Windows uyarırsa: “Ek bilgi” → “Yine de çalıştır”", "İmzasız dosyalarda bu uyarı normaldir")
    return img


C_BOX = (330, 140, 1590, 800)
INSTALL_LINES = [(" Kul AI Asistan - NETCAD Kurulum", (229, 231, 235)),
                 (" =================================", (229, 231, 235)),
                 ("", None),
                 (" Kuruldu: C:\\NetCAD853\\MODUL\\KulAI", TERM_GREEN)]


def s_console(t):
    img = DESKTOP.copy()
    k = p(t, 5.9, 6.5)
    base = img.copy()
    lines = INSTALL_LINES + [("", None),
                             (" Şimdi NETCAD'i açın. Sağ kenarda 'Kul AI' paneli belirecek.", (229, 231, 235)),
                             (" Ayrıntılar: 'KURULUM VE TEST REHBERI.txt'", (229, 231, 235)),
                             ("", None),
                             (" Kapatmak için Enter'a basın:", (229, 231, 235))]
    console(img, C_BOX, "C:\\WINDOWS\\system32\\cmd.exe", lines, t, 0.4, 0.35)
    if t > 4.4:
        keycap(img, 1440, 690, "Enter", p(t, 4.4, 5.4))
    if k > 0:
        img = Image.blend(img, base, k)
    caption(img, t, "6", "“Kuruldu” yazısını görünce Enter'a basın")
    return img


def path_card(letter, color, head, sub):
    def scene(t):
        img = TITLE_BG.copy()
        k = p(t, 0.1, 0.7)

        def draw(im):
            d = ImageDraw.Draw(im)
            y = (1 - k) * 30
            d.ellipse((960 - 70, 330 + y, 960 + 70, 470 + y), fill=color)
            txt(d, (960, 400 + y), letter, 84, "b", WHITE, "mm")
            txt(d, (960, 580 + y), head, 76, "b", WHITE, "mm")
            txt(d, (960, 670 + y), sub, 34, "r", MUTED, "mm")
        fade_in(img, k, draw)
        return img
    return scene


def bubble(d, x_right, x_left, y, lines, user):
    size = 20
    w = max(tw(s, size) for s in lines) + 36
    h = 20 + 30 * len(lines)
    x0 = x_right - w if user else x_left
    d.rounded_rectangle((x0, y, x0 + w, y + h), 14, fill=BLUE if user else WHITE, outline=None if user else LINE)
    for i, s in enumerate(lines):
        txt(d, (x0 + 18, y + 12 + i * 30), s, size, "r", WHITE if user else INK)
    return y + h


def s_assistant(t):
    img = DESKTOP.copy()
    N = (150, 60, 1770, 880)
    PW = 470
    cv, pn = netcad(img, N, "NETCAD 8.5 - Proje1.ncz", panel_w=PW)
    d = ImageDraw.Draw(img)
    pts = parcel_points(cv)
    divided = p(t, 7.6, 8.6)
    for a in (1 / 3, 2 / 3):
        top = (lerp(pts[0][0], pts[1][0], a), lerp(pts[0][1], pts[1][1], a))
        bot = (lerp(pts[3][0], pts[2][0], a), lerp(pts[3][1], pts[2][1], a))
        if divided > 0:
            end = (lerp(top[0], bot[0], divided), lerp(top[1], bot[1], divided))
            d.line((top, end), fill=(56, 189, 248), width=3)
    ctr = (sum(x for x, _ in pts) / 4, sum(y for _, y in pts) / 4)
    if t < 8.6:
        txt(d, ctr, "101", 30, "sb", WHITE, "mm")
    else:
        for i, a in enumerate((1 / 6, 1 / 2, 5 / 6)):
            mx = lerp(lerp(pts[0][0], pts[1][0], a), lerp(pts[3][0], pts[2][0], a), 0.5)
            my = lerp(lerp(pts[0][1], pts[1][1], a), lerp(pts[3][1], pts[2][1], a), 0.5)
            txt(d, (mx, my), f"101/{i + 1}", 26, "sb", WHITE, "mm")

    px0, py0, px1, py1 = pn
    d.rectangle(pn, fill=(248, 250, 252))
    d.line((px0, py0, px0, py1), fill=LINE, width=2)
    txt(d, (px0 + 22, py0 + 32), "Kul AI", 26, "b", INK, "lm")
    ay = (px1 - 118, py0 + 14, px1 - 18, py0 + 50)
    d.rounded_rectangle(ay, 8, fill=(219, 234, 254) if 1.0 < t < 1.8 else WHITE, outline=LINE)
    txt(d, ((ay[0] + ay[2]) / 2, py0 + 32), "Ayarlar", 19, "sb", INK, "mm")
    d.rounded_rectangle((px1 - 270, py0 + 14, px1 - 130, py0 + 50), 8, fill=WHITE, outline=LINE)
    txt(d, (px1 - 200, py0 + 32), "Yeni sohbet", 19, "r", INK, "mm")
    d.line((px0 + 1, py0 + 64, px1, py0 + 64), fill=LINE)

    msg = "101 nolu parseli 3 eşit parçaya ifraz et"
    ib = (px0 + 16, py1 - 76, px1 - 16, py1 - 16)
    d.rounded_rectangle(ib, 10, fill=WHITE, outline=BLUE if 4.9 < t < 6.8 else LINE, width=2)
    if 4.9 < t < 6.8:
        cur = typed(msg, t, 5.0, 6.5)
        txt(d, (ib[0] + 16, (ib[1] + ib[3]) / 2), cur, 19, "r", INK, "lm")
        if int(t * 2.5) % 2 == 0:
            x = ib[0] + 18 + tw(cur, 19)
            d.line((x, ib[1] + 18, x, ib[3] - 18), fill=INK, width=2)
    else:
        txt(d, (ib[0] + 16, (ib[1] + ib[3]) / 2), "Ne yapmak istiyorsunuz?", 19, "r", MUTED, "lm")
    y = py0 + 90
    if t < 6.8:
        txt(d, (px0 + 22, y), "Merhaba! NETCAD'de ne yapmamı istersiniz?", 19, "r", GRAY)
    if t > 6.8:
        y = bubble(d, px1 - 18, px0 + 18, y, ["101 nolu parseli 3 eşit", "parçaya ifraz et"], True) + 16
    if 6.9 < t < 8.4:
        for i in range(3):
            on = int(t * 4) % 3 == i
            d.ellipse((px0 + 30 + i * 22, y + 8, px0 + 42 + i * 22, y + 20), fill=GRAY if on else LINE)
    if t > 8.4:
        bubble(d, px1 - 18, px0 + 18, y, ["101 parseli 3 eşit parçaya bölündü.", "Her parça 412,35 m².",
                                          "Ctrl+Z ile geri alabilirsiniz."], False)

    def settings(im):
        dd = ImageDraw.Draw(im)
        S = (560, 220, 1160, 700)
        cx0, cy0, cx1, cy1 = window(im, S, "Kul AI Ayarlar")
        x = cx0 + 32
        txt(dd, (x, cy0 + 28), "Model", 20, "sb", INK)
        dd.rounded_rectangle((x, cy0 + 60, cx1 - 32, cy0 + 104), 6, fill=WHITE, outline=(148, 163, 184))
        txt(dd, (x + 14, cy0 + 82), "Google Gemini (önerilen)", 20, "r", INK, "lm")
        dd.polygon([(cx1 - 62, cy0 + 76), (cx1 - 46, cy0 + 76), (cx1 - 54, cy0 + 88)], fill=GRAY)
        txt(dd, (x, cy0 + 130), "Anahtar almak için:", 19, "r", GRAY)
        txt(dd, (x, cy0 + 160), "aistudio.google.com  →  Create API key", 20, "sb", BLUE)
        txt(dd, (x, cy0 + 214), "API anahtarı", 20, "sb", INK)
        dd.rounded_rectangle((x, cy0 + 246, cx1 - 32, cy0 + 290), 6, fill=WHITE,
                             outline=BLUE if t > 2.3 else (148, 163, 184), width=2)
        dots = "•" * int(26 * clamp((t - 2.5) / 1.1))
        txt(dd, (x + 14, cy0 + 268), dots, 22, "r", INK, "lm")
        txt(dd, (x, cy0 + 312), "Anahtar yalnızca bu bilgisayarda, şifreli saklanır.", 17, "r", GRAY)
        b = (cx1 - 152, cy1 - 64, cx1 - 32, cy1 - 20)
        dd.rounded_rectangle(b, 6, fill=BLUE)
        txt(dd, ((b[0] + b[2]) / 2, (b[1] + b[3]) / 2), "Kaydet", 21, "sb", WHITE, "mm")
    fade_in(img, min(p(t, 1.6, 1.85), 1 - p(t, 4.6, 4.85)), settings)
    save = (1160 - 92, 700 - 42)
    cursor(img, t, [(0, (900, 560)), (0.3, (900, 560)), (1.2, ((ay[0] + ay[2]) / 2, py0 + 34)),
                    (2.0, ((ay[0] + ay[2]) / 2, py0 + 34)), (2.3, (800, 488)), (3.7, (800, 488)), (4.3, save),
                    (5.0, save), (5.6, (1180, 650))],
           clicks=[1.45, 2.3, 4.45])
    caption(img, t, "A", "Ayarlar'dan ücretsiz Gemini anahtarını girin, sonra Türkçe yazın")
    return img


def s_claude(t):
    img = DESKTOP.copy()
    if t < 3.2:
        base = img.copy()
        lines = INSTALL_LINES + [("", None),
                                 (" Claude Code bağlantısı kuruluyor...", (229, 231, 235)),
                                 (" Claude Code'a eklendi: netcad", TERM_GREEN),
                                 ("", None),
                                 (" Şimdi NETCAD'i açıp bir proje açın,", (229, 231, 235)),
                                 (" ardından Claude Code'u yeniden başlatın.", (229, 231, 235))]
        console(img, C_BOX, "C:\\WINDOWS\\system32\\cmd.exe", lines, t, 0.2, 0.2)
        k = p(t, 2.7, 3.2)
        if k > 0:
            img = Image.blend(img, base, k)
        caption(img, t, "B", "Claude-Bagla.bat bağlantıyı Claude Code'a ekler")
        return img

    tt = t - 3.2
    T = (260, 90, 1660, 860)
    base = img.copy()
    cx0, cy0, cx1, cy1 = window(img, T, "Claude Code  —  C:\\Projeler", dark=True)
    d = ImageDraw.Draw(img)
    x = cx0 + 32
    d.rounded_rectangle((x, cy0 + 24, x + 620, cy0 + 104), 10, outline=ORANGE, width=2)
    txt(d, (x + 24, cy0 + 50), "Claude Code", 26, "mb", ORANGE, "lm")
    txt(d, (x + 24, cy0 + 82), "MCP: netcad  ·  bağlı", 22, "m", TERM_GREEN, "lm")
    y = cy0 + 150
    q = "NETCAD'de hangi katmanlar var?"
    txt(d, (x, y), ">", 26, "mb", (229, 231, 235))
    cur = typed(q, tt, 0.4, 1.8)
    txt(d, (x + 32, y), cur, 26, "m", WHITE)
    if tt < 2.0 and int(t * 2.5) % 2 == 0:
        cxp = x + 34 + tw(cur, 26, "m")
        d.rectangle((cxp, y + 2, cxp + 14, y + 28), fill=(229, 231, 235))
    y += 64
    if tt > 2.1:
        d.ellipse((x + 4, y + 8, x + 18, y + 22), fill=ORANGE)
        txt(d, (x + 32, y), "netcad · list_layers", 26, "mb", WHITE)
    if 2.3 < tt < 4.4:
        bx = (x, y + 50, x + 760, y + 270)
        d.rounded_rectangle(bx, 10, outline=(115, 115, 115), width=2)
        txt(d, (x + 24, y + 74), "Bu araç çalıştırılsın mı?", 24, "mb", WHITE)
        opts = ("> 1. Evet", "  2. Evet, bir daha sorma", "  3. Hayır")
        for i, s in enumerate(opts):
            txt(d, (x + 24, y + 126 + i * 42), s, 24, "m", ORANGE if i == 0 else (163, 163, 163))
        keycap(img, cx1 - 160, cy1 - 110, "Enter", p(tt, 3.3, 4.2))
    if tt > 4.4:
        txt(d, (x + 32, y + 44), "└ 4 katman bulundu", 24, "m", (163, 163, 163))
        y += 110
        k = p(tt, 4.6, 5.0)
        if k > 0:
            d.ellipse((x + 4, y + 8, x + 18, y + 22), fill=WHITE)
            ans = ["Projede 4 katman var:", "  - 0", "  - PARSEL  (12 nesne)", "  - YOL     (5 nesne)",
                   "  - BINA    (8 nesne)"]
            for i, s in enumerate(ans):
                if tt > 4.6 + i * 0.25:
                    txt(d, (x + 32, y + i * 40), s, 26, "m", WHITE)
    kin = p(t, 3.2, 3.6)
    if kin < 1:
        img = Image.blend(base, img, kin)
    caption(img, t, "B", "Claude Code'u yeniden başlatın, NETCAD'e ne yapacağını söyleyin",
            "Claude her işlemden önce onay ister  ·  Ctrl+Z ile geri alınır")
    return img


def s_end(t):
    img = TITLE_BG.copy()
    k = p(t, 0.2, 0.9)
    fade_in(img, k, lambda im: txt(ImageDraw.Draw(im), (960, 400 + (1 - k) * 30), "Hazır!", 128, "b", WHITE, "mm"))
    k2 = p(t, 0.8, 1.5)

    def rest(im):
        d = ImageDraw.Draw(im)
        txt(d, (960, 560), URL, 36, "m", SKY, "mm")
        txt(d, (960, 640), "Kaldırmak için: Kaldir.bat", 30, "r", MUTED, "mm")
        txt(d, (960, 800), "Kul AI · NETCAD 8.5", 24, "sb", (100, 116, 139), "mm")
    fade_in(img, k2, rest)
    return img


SCENES = [
    (4.0, s_title), (6.5, s_download), (8.0, s_extract), (3.6, s_close), (7.0, s_choose),
    (7.5, s_smartscreen), (7.0, s_console),
    (3.0, path_card("A", BLUE, "Kul AI asistanı", "Kur.bat ile kurduysanız")), (11.0, s_assistant),
    (3.0, path_card("B", ORANGE, "Claude Code bağlantısı", "Claude-Bagla.bat ile kurduysanız")), (12.0, s_claude),
    (5.0, s_end),
]
EDGE = 0.3


def frame(fn, dur, t):
    img = fn(t)
    k = min(1.0, t / EDGE, (dur - t) / EDGE)
    return Image.blend(FADE_BG, img, k) if k < 1 else img


def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--kareler":
        out = Path(sys.argv[2])
        out.mkdir(parents=True, exist_ok=True)
        for i, (dur, fn) in enumerate(SCENES):
            for j, f in enumerate((0.35, 0.6, 0.85)):
                frame(fn, dur, dur * f).save(out / f"s{i:02d}_{j}.png")
        print("Kareler:", out)
        return
    OUT.parent.mkdir(parents=True, exist_ok=True)
    ff = subprocess.Popen(["ffmpeg", "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24",
                           "-s", f"{W}x{H}", "-r", str(FPS), "-i", "-", "-c:v", "libx264", "-preset", "medium",
                           "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(OUT)],
                          stdin=subprocess.PIPE)
    total = sum(round(dur * FPS) for dur, _ in SCENES)
    n = 0
    for dur, fn in SCENES:
        for i in range(round(dur * FPS)):
            ff.stdin.write(frame(fn, dur, i / FPS).tobytes())
            n += 1
            if n % 300 == 0:
                print(f"{n}/{total}", flush=True)
    ff.stdin.close()
    ff.wait()
    print("Hazır:", OUT, f"({total / FPS:.0f} sn)")


if __name__ == "__main__":
    main()
