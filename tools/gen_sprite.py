"""Generate a 16-bit-style character sprite sheet with animation frames.

Draws the clown from a parameterised pose, so idle breathing, a walk cycle,
an attack swing and a hit recoil are all the same artwork at different offsets.
Emits a horizontal strip to assets/sprites/clown_16bit.png plus a frame map.

Run: python tools/gen_sprite.py
"""
import zlib, struct, os, json

W, H = 48, 64

MATS = {
    1:  ((214, 58, 66),   (150, 36, 48),   (242, 112, 104)),  # hat red
    2:  ((247, 214, 188), (198, 158, 140), (255, 240, 226)),  # skin
    3:  ((226, 70, 70),   (170, 42, 48),   (250, 120, 112)),  # nose
    4:  ((246, 203, 90),  (190, 142, 52),  (255, 234, 158)),  # yellow trim
    5:  ((74, 110, 206),  (42, 66, 146),   (126, 166, 242)),  # suit blue
    6:  ((240, 242, 248), (190, 196, 214), (255, 255, 255)),  # white
    7:  ((235, 140, 70),  (176, 92, 44),   (252, 186, 124)),  # hair orange
    8:  ((70, 56, 76),    (44, 34, 50),    (104, 88, 110)),   # shoe
    9:  ((40, 30, 48),    (40, 30, 48),    (40, 30, 48)),     # eye
    10: ((150, 60, 150),  (104, 38, 108),  (196, 108, 196)),  # accent purple
}
OUTLINE = (26, 18, 34)


class Canvas:
    def __init__(self):
        self.buf = [[0] * W for _ in range(H)]

    def put(self, x, y, m):
        if 0 <= x < W and 0 <= y < H:
            self.buf[y][x] = m

    def ellipse(self, cx, cy, rx, ry, m):
        for y in range(cy - ry, cy + ry + 1):
            for x in range(cx - rx, cx + rx + 1):
                dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
                if dx * dx + dy * dy <= 1.0:
                    self.put(x, y, m)

    def rect(self, x0, y0, x1, y1, m):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, m)

    def tri(self, ax, ay, bx, by, cx_, cy_, m):
        def sign(x1, y1, x2, y2, x3, y3):
            return (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3)
        for y in range(min(ay, by, cy_), max(ay, by, cy_) + 1):
            for x in range(min(ax, bx, cx_), max(ax, bx, cx_) + 1):
                d1 = sign(x, y, ax, ay, bx, by)
                d2 = sign(x, y, bx, by, cx_, cy_)
                d3 = sign(x, y, cx_, cy_, ax, ay)
                neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
                pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
                if not (neg and pos):
                    self.put(x, y, m)

    def limb(self, sx, sy, ex, ey, m, half=2):
        steps = max(abs(ex - sx), abs(ey - sy), 1)
        for t in range(steps + 1):
            f = t / steps
            x = int(round(sx + (ex - sx) * f))
            y = int(round(sy + (ey - sy) * f))
            self.rect(x - half, y - half, x + half, y + half, m)


NEUTRAL = dict(body_dy=0, lean=0, head_dy=0,
               larm=(10, 53), rarm=(37, 53),
               lleg_dy=0, rleg_dy=0, lleg_dx=0, rleg_dx=0,
               hat_dx=0, mallet=None)


def pose(**over):
    p = dict(NEUTRAL)
    p.update(over)
    return p


def draw(p):
    c = Canvas()
    by, ln, hd = p["body_dy"], p["lean"], p["head_dy"]
    lx, ly = p["larm"]
    rx, ry = p["rarm"]

    # shoes and legs, striding horizontally as well as lifting
    ldx, rdx = p["lleg_dx"], p["rleg_dx"]
    ldy, rdy = p["lleg_dy"], p["rleg_dy"]
    c.limb(20 + ln, 50 + by, 20 + ldx, 57 + ldy, 6, half=2)
    c.limb(28 + ln, 50 + by, 28 + rdx, 57 + rdy, 6, half=2)
    for i in range(3):
        c.rect(18 + ldx, 52 + ldy + i * 3, 22 + ldx, 53 + ldy + i * 3, 1)
        c.rect(26 + rdx, 52 + rdy + i * 3, 30 + rdx, 53 + rdy + i * 3, 1)
    c.ellipse(16 + ldx, 60 + ldy, 8, 4, 8)
    c.ellipse(32 + rdx, 60 + rdy, 8, 4, 8)
    c.ellipse(15 + ldx, 59 + ldy, 6, 2, 1)
    c.ellipse(33 + rdx, 59 + rdy, 6, 2, 1)

    # torso
    c.rect(16 + ln, 37 + by, 31 + ln, 50 + by, 5)
    c.ellipse(24 + ln, 50 + by, 8, 3, 5)
    c.ellipse(24 + ln, 37 + by, 8, 3, 5)
    for b in (43, 49):
        for dy in range(-3, 4):
            w = 3 - abs(dy)
            c.rect(24 + ln - w, b + dy + by, 24 + ln + w, b + dy + by, 10)

    # arms
    c.limb(17 + ln, 40 + by, lx, ly, 5)
    c.limb(30 + ln, 40 + by, rx, ry, 5)
    c.ellipse(lx, ly, 4, 4, 6)
    c.ellipse(rx, ry, 4, 4, 6)

    # ruff collar
    c.ellipse(24 + ln, 36 + by, 13, 4, 4)
    for cx in range(12, 37, 6):
        c.ellipse(cx + ln, 38 + by, 3, 3, 4)

    # hair and head
    hy = 24 + by + hd
    c.ellipse(13 + ln, hy + 5, 4, 4, 7)
    c.ellipse(35 + ln, hy + 5, 4, 4, 7)
    c.ellipse(24 + ln, hy, 9, 10, 2)
    c.ellipse(20 + ln, hy, 1, 2, 9)
    c.ellipse(28 + ln, hy, 1, 2, 9)
    c.put(20 + ln, hy - 1, 6)
    c.put(28 + ln, hy - 1, 6)
    c.ellipse(18 + ln, hy + 4, 2, 1, 3)
    c.ellipse(30 + ln, hy + 4, 2, 1, 3)
    c.ellipse(24 + ln, hy + 4, 3, 3, 3)
    for x in range(20, 29):
        c.put(x + ln, hy + 7 + (0 if 22 <= x <= 26 else -1), 9)

    # hat
    hx = 24 + ln + p["hat_dx"]
    top = hy - 22
    c.tri(hx, top, hx - 11, hy - 9, hx + 11, hy - 9, 1)
    for y in range(max(0, top), hy - 8):
        for x in range(W):
            if c.buf[y][x] == 1 and (x + y) % 7 < 2:
                c.buf[y][x] = 4
    c.ellipse(hx, hy - 9, 12, 2, 1)
    c.ellipse(hx, top, 3, 3, 6)

    # mallet, drawn last so the arc stays visible across the body
    if p["mallet"] is not None:
        mx, my = p["mallet"]
        c.limb(rx, ry, mx, my, 8, half=1)
        c.ellipse(mx, my, 5, 4, 1)
        c.rect(mx - 2, my - 3, mx + 2, my + 3, 4)
    return c.buf


def shade(buf):
    def op(x, y):
        return 0 <= x < W and 0 <= y < H and buf[y][x] != 0
    px = [[(0, 0, 0, 0)] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            m = buf[y][x]
            if m == 0:
                continue
            if not all(op(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = OUTLINE + (255,)
                continue
            base, dark, light = MATS[m]
            if not op(x + 2, y + 2) or not op(x + 1, y + 2):
                col = dark
            elif not op(x - 2, y - 2) or not op(x - 1, y - 2):
                col = light
            else:
                col = base
            px[y][x] = col + (255,)
    return px


FRAMES = []


def add(name, p):
    FRAMES.append((name, draw(p)))


# idle: a visible breath, shoulders and head rise
add("idle", pose())
add("idle", pose(body_dy=2, larm=(10, 55), rarm=(37, 55), head_dy=1))

# walk: a real stride. Legs swing through, body bobs, arms counter-swing.
add("walk", pose(lleg_dx=6, lleg_dy=-7, rleg_dx=-5, rleg_dy=1,
                 body_dy=-2, larm=(8, 47), rarm=(39, 58), lean=1))
add("walk", pose(lleg_dx=3, lleg_dy=-2, rleg_dx=-2,
                 body_dy=0, larm=(9, 50), rarm=(38, 55)))
add("walk", pose(lleg_dx=-5, lleg_dy=1, rleg_dx=6, rleg_dy=-7,
                 body_dy=-2, larm=(8, 58), rarm=(39, 47), lean=-1))
add("walk", pose(lleg_dx=-2, rleg_dx=3, rleg_dy=-2,
                 body_dy=0, larm=(9, 55), rarm=(38, 50)))

# attack: an overhead mallet arc, upper right down to lower left
add("attack", pose(lean=-4, body_dy=2, head_dy=1, hat_dx=-2,
                   larm=(9, 56), rarm=(41, 34), mallet=(44, 26)))
add("attack", pose(lean=1, body_dy=-1, hat_dx=1,
                   larm=(10, 52), rarm=(40, 38), mallet=(39, 5)))
add("attack", pose(lean=5, body_dy=0, head_dy=1, hat_dx=3,
                   larm=(13, 50), rarm=(34, 46), mallet=(11, 7)))
add("attack", pose(lean=3, body_dy=2, hat_dx=2,
                   larm=(12, 54), rarm=(31, 54), mallet=(4, 36)))

# hit recoil
add("hit", pose(lean=-4, head_dy=-3, larm=(5, 47), rarm=(34, 42),
                body_dy=2, hat_dx=-4, lleg_dx=-2, rleg_dx=2))

SHEET_W = W * len(FRAMES)
shaded = [shade(b) for _, b in FRAMES]
rows = []
for y in range(H):
    row = bytearray()
    for f in shaded:
        for x in range(W):
            row.extend(f[y][x])
    rows.append(row)


def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)

    def ch(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)

    data = (b"\x89PNG\r\n\x1a\n"
            + ch(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + ch(b"IDAT", zlib.compress(raw, 9)) + ch(b"IEND", b""))
    open(path, "wb").write(data)


out = os.path.join("assets", "sprites", "clown_16bit.png")
write_png(out, SHEET_W, H, rows)

clips = {}
for i, (name, _) in enumerate(FRAMES):
    clips.setdefault(name, []).append(i)
meta = {"frame_width": W, "frame_height": H, "count": len(FRAMES), "clips": clips}
with open(os.path.join("assets", "sprites", "clown_16bit.frames.json"), "w") as f:
    json.dump(meta, f, indent=2)

print("wrote {}  {}x{}  {} frames  {} bytes".format(out, SHEET_W, H, len(FRAMES), os.path.getsize(out)))
print("clips: " + ", ".join("{}={}".format(k, len(v)) for k, v in clips.items()))
