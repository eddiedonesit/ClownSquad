"""Shared pixel-art primitives: palette, canvas, and the 16-bit shading pass.

Used by every character generator. Keeping the palette in one place is what
makes separate sheets look like they belong to the same game.
"""

# material id -> (base, shadow, light)
MATS = {
    1:  ((214, 58, 66),   (150, 36, 48),   (242, 112, 104)),  # red
    2:  ((247, 214, 188), (198, 158, 140), (255, 240, 226)),  # skin
    3:  ((226, 70, 70),   (170, 42, 48),   (250, 120, 112)),  # nose / blush
    4:  ((246, 203, 90),  (190, 142, 52),  (255, 234, 158)),  # yellow trim
    5:  ((74, 110, 206),  (42, 66, 146),   (126, 166, 242)),  # blue suit
    6:  ((240, 242, 248), (190, 196, 214), (255, 255, 255)),  # white
    7:  ((235, 140, 70),  (176, 92, 44),   (252, 186, 124)),  # orange hair
    8:  ((70, 56, 76),    (44, 34, 50),    (104, 88, 110)),   # dark
    9:  ((40, 30, 48),    (40, 30, 48),    (40, 30, 48)),     # ink
    10: ((150, 60, 150),  (104, 38, 108),  (196, 108, 196)),  # purple
    11: ((48, 72, 140),   (30, 46, 96),    (80, 110, 180)),   # far-side blue
    12: ((186, 192, 208), (146, 152, 172), (220, 224, 236)),  # far-side white
    13: ((176, 40, 48),   (120, 26, 34),   (206, 86, 84)),    # far-side red
    14: ((170, 96, 54),   (118, 62, 32),   (206, 136, 86)),   # wood
    15: ((96, 176, 96),   (58, 122, 60),   (150, 214, 148)),  # sickly green
    16: ((92, 46, 112),   (58, 26, 74),    (134, 80, 158)),   # deep purple
}
OUTLINE = (26, 18, 34)


class Canvas:
    """Index buffer the generators draw into. Art is authored in a logical
    space and offset into a wider canvas, so props can overhang the body."""

    def __init__(self, w, h, offset_x=0):
        self.w = w
        self.h = h
        self.ox = offset_x
        self.buf = [[0] * w for _ in range(h)]

    def put(self, x, y, m):
        x += self.ox
        if 0 <= x < self.w and 0 <= y < self.h:
            self.buf[y][x] = m

    def ellipse(self, cx, cy, rx, ry, m):
        for y in range(int(cy - ry), int(cy + ry) + 1):
            for x in range(int(cx - rx), int(cx + rx) + 1):
                dx = (x - cx) / max(rx, .5)
                dy = (y - cy) / max(ry, .5)
                if dx * dx + dy * dy <= 1.0:
                    self.put(x, y, m)

    def rect(self, x0, y0, x1, y1, m):
        for y in range(int(y0), int(y1) + 1):
            for x in range(int(x0), int(x1) + 1):
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
        for t in range(int(steps) + 1):
            f = t / steps
            x = int(round(sx + (ex - sx) * f))
            y = int(round(sy + (ey - sy) * f))
            self.rect(x - half, y - half, x + half, y + half, m)


def shade(canvas, mats=MATS, outline=OUTLINE):
    """The passes that make flat regions read as 16-bit art: a dark silhouette
    outline, rim light on the upper-left, shadow on the lower-right."""
    buf, w, h = canvas.buf, canvas.w, canvas.h

    def op(x, y):
        return 0 <= x < w and 0 <= y < h and buf[y][x] != 0

    px = [[(0, 0, 0, 0)] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            m = buf[y][x]
            if m == 0:
                continue
            if not all(op(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = outline + (255,)
                continue
            base, dark, light = mats[m]
            if not op(x + 2, y + 2) or not op(x + 1, y + 2):
                col = dark
            elif not op(x - 2, y - 2) or not op(x - 1, y - 2):
                col = light
            else:
                col = base
            px[y][x] = col + (255,)
    return px


def pack_rows(frames, w, h):
    """Lay shaded frames side by side into PNG rows."""
    rows = []
    for y in range(h):
        row = bytearray()
        for f in frames:
            for x in range(w):
                row.extend(f[y][x])
        rows.append(row)
    return rows
