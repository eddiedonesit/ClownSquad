"""Generate a 16-bit-style character sprite (48x64) with palette shading.

Draws flat material regions, then applies pixel-art passes that give the
SNES/Genesis look: a dark silhouette outline, rim light on the upper-left,
and shadow on the lower-right. No external dependencies.
"""
import zlib, struct, os

W, H = 48, 64
TRANSPARENT = 0

# material id -> (base, shadow, light)
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

buf = [[TRANSPARENT] * W for _ in range(H)]

def put(x, y, m):
    if 0 <= x < W and 0 <= y < H:
        buf[y][x] = m

def ellipse(cx, cy, rx, ry, m):
    for y in range(cy - ry, cy + ry + 1):
        for x in range(cx - rx, cx + rx + 1):
            dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
            if dx * dx + dy * dy <= 1.0:
                put(x, y, m)

def rect(x0, y0, x1, y1, m):
    for y in range(y0, y1 + 1):
        for x in range(x0, x1 + 1):
            put(x, y, m)

def tri(ax, ay, bx, by, cx_, cy_, m):
    minx, maxx = min(ax, bx, cx_), max(ax, bx, cx_)
    miny, maxy = min(ay, by, cy_), max(ay, by, cy_)
    def sign(x1, y1, x2, y2, x3, y3):
        return (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3)
    for y in range(miny, maxy + 1):
        for x in range(minx, maxx + 1):
            d1 = sign(x, y, ax, ay, bx, by)
            d2 = sign(x, y, bx, by, cx_, cy_)
            d3 = sign(x, y, cx_, cy_, ax, ay)
            neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
            pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
            if not (neg and pos):
                put(x, y, m)

# ---- shoes (oversized, clown-like) ----
ellipse(15, 60, 8, 4, 8)
ellipse(33, 60, 8, 4, 8)
ellipse(14, 59, 6, 2, 1)
ellipse(34, 59, 6, 2, 1)
# ---- legs with stripes ----
rect(18, 52, 23, 59, 6)
rect(25, 52, 30, 59, 6)
for y in range(52, 60, 3):
    rect(18, y, 23, y + 1, 1)
    rect(25, y, 30, y + 1, 1)
# ---- torso ----
rect(16, 37, 31, 50, 5)
ellipse(24, 50, 8, 3, 5)
ellipse(24, 37, 8, 3, 5)
# diamond accents down the front
for by in (43, 49):
    for dy in range(-3, 4):
        w = 3 - abs(dy)
        rect(24 - w, by + dy, 24 + w, by + dy, 10)
# ---- arms (after torso so they read on top) ----
for sx, ex in ((16, 10), (31, 37)):
    for t in range(13):
        f = t / 12.0
        x = int(sx + (ex - sx) * f)
        rect(x - 2, 39 + t, x + 2, 41 + t, 5)
ellipse(10, 53, 4, 4, 6)
ellipse(37, 53, 4, 4, 6)
# ---- ruff collar ----
ellipse(24, 36, 13, 4, 4)
for cx in range(12, 37, 6):
    ellipse(cx, 38, 3, 3, 4)
# ---- hair tufts, low at the sides ----
ellipse(13, 29, 4, 4, 7)
ellipse(35, 29, 4, 4, 7)
# ---- head ----
ellipse(24, 24, 9, 10, 2)
# eyes, clear of the hat brim
ellipse(20, 24, 1, 2, 9)
ellipse(28, 24, 1, 2, 9)
put(20, 23, 6)
put(28, 23, 6)
# cheeks
ellipse(18, 28, 2, 1, 3)
ellipse(30, 28, 2, 1, 3)
# nose
ellipse(24, 28, 3, 3, 3)
# smile
for x in range(20, 29):
    put(x, 31 + (0 if 22 <= x <= 26 else -1), 9)
# ---- hat, sitting high above the eyes ----
tri(24, 2, 13, 15, 35, 15, 1)
for y in range(2, 16):
    for x in range(W):
        if buf[y][x] == 1 and (x + y) % 7 < 2:
            buf[y][x] = 4
ellipse(24, 15, 12, 2, 1)
ellipse(24, 2, 3, 3, 6)

# ---------- pixel-art passes ----------
def opaque(x, y):
    return 0 <= x < W and 0 <= y < H and buf[y][x] != TRANSPARENT

outline = [[False] * W for _ in range(H)]
for y in range(H):
    for x in range(W):
        if buf[y][x] == TRANSPARENT:
            continue
        if not all(opaque(x + dx, y + dy) for dx, dy in ((1,0),(-1,0),(0,1),(0,-1))):
            outline[y][x] = True

px = [[(0, 0, 0, 0)] * W for _ in range(H)]
for y in range(H):
    for x in range(W):
        m = buf[y][x]
        if m == TRANSPARENT:
            continue
        if outline[y][x]:
            px[y][x] = OUTLINE + (255,)
            continue
        base, dark, light = MATS[m]
        c = base
        # shadow: near the lower-right silhouette edge
        if not opaque(x + 2, y + 2) or not opaque(x + 1, y + 2):
            c = dark
        # rim light: near the upper-left silhouette edge
        elif not opaque(x - 2, y - 2) or not opaque(x - 1, y - 2):
            c = light
        px[y][x] = c + (255,)

rows = []
for y in range(H):
    row = bytearray()
    for x in range(W):
        row.extend(px[y][x])
    rows.append(row)

def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n"
    data += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    data += chunk(b"IDAT", zlib.compress(raw, 9))
    data += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)

out = os.path.join("assets", "sprites", "clown_16bit.png")
write_png(out, W, H, rows)
cols = len({p for r in px for p in r if p[3]})
print(f"wrote {out}  {W}x{H}  {os.path.getsize(out)} bytes  {cols} distinct colors")
