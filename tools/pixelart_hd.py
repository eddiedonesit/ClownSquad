"""HD pixel-art pipeline: double-resolution characters with SNES-style rendering.

The first pipeline (pixelart.py) shades each flat region with three tones and a
black outline. That is what limits it: 16-bit art reads as detailed because of
what happens *inside* the silhouette. This one adds:

- Five-tone ramps per material, hue-shifted: shadows lean toward violet,
  highlights toward warm yellow, rather than being the same hue darker.
- Form shading from a lighting model. Every drawn part gets a height field from
  its distance to its own edge, so a limb shades like a cylinder and a head like
  a dome, lit from the upper left. Parts drawn later cast a short shadow onto
  the parts behind them (under the hat brim, under the ruff, behind an arm).
- Selective outlines: the silhouette edge takes a dark tone of the material it
  wraps, with an ink ring outside that for readability against the floor, and
  interior lines appear only where one part overlaps another.

Characters are still built from parameterised shapes, so animation stays cheap:
a pose is a set of offsets, and every frame is rendered by the same code.
"""
import colorsys
import math

# ------------------------------------------------------------------ ramps

TONES = 5          # 0 deepest shadow .. 2 base .. 4 highlight

_VAL = (0.46, 0.70, 1.0, 1.10, 1.20)
_SAT = (1.10, 1.06, 1.0, 0.84, 0.58)
_TO_COOL = (0.12, 0.06, 0.0, 0.0, 0.0)     # fraction of the way to violet
_TO_WARM = (0.0, 0.0, 0.0, 0.05, 0.10)     # fraction of the way to yellow
COOL, WARM = 0.72, 0.14


def _toward(h, target, amount):
    d = ((target - h + 0.5) % 1.0) - 0.5
    return (h + d * amount) % 1.0


def ramp(rgb, cool=COOL):
    """Five hue-shifted tones from one base colour (tone 2 is the base)."""
    h, s, v = colorsys.rgb_to_hsv(*(c / 255.0 for c in rgb))
    out = []
    for i in range(TONES):
        hh = _toward(_toward(h, cool, _TO_COOL[i]), WARM, _TO_WARM[i])
        vv = v * _VAL[i]
        ss = s * _SAT[i]
        if vv > 1.0:                       # past white: spend the excess desaturating
            ss *= max(0.0, 1.0 - (vv - 1.0) * 1.5)
            vv = 1.0
        r, g, b = colorsys.hsv_to_rgb(hh, min(1.0, ss), vv)
        out.append((int(r * 255 + 0.5), int(g * 255 + 0.5), int(b * 255 + 0.5)))
    return out


class Material:
    """A paint. Flat materials (ink, eye whites) skip shading entirely; glossy
    ones are allowed the top highlight tone and reach it more easily."""

    def __init__(self, rgb, flat=False, gloss=False, soft=False, cool=COOL):
        # Skin wants its shadows to lean rose rather than violet, or it turns grey.
        self.tones = [tuple(rgb)] * TONES if flat else ramp(rgb, cool)
        self.flat = flat
        self.gloss = gloss
        # Soft materials (skin) never shade into the deepest tone; it reads as dirt
        # on a face. The deepest tone is still used for their lines.
        self.soft = soft


INK = (26, 18, 34)


# ----------------------------------------------------------------- canvas

class Part:
    def __init__(self, far, line):
        self.far = far          # behind the body: everything one tone darker
        self.line = line        # draw a line where this overlaps an earlier part


class HDCanvas:
    """Material and part buffers. Each drawing call paints into the current
    part; parts are the units the shading treats as one solid shape."""

    def __init__(self, w, h, ox=0, oy=0):
        self.w, self.h, self.ox, self.oy = w, h, ox, oy
        self.mat = [[0] * w for _ in range(h)]
        self.part_of = [[0] * w for _ in range(h)]
        self.parts = {0: Part(False, False)}
        self.cur = 0

    def part(self, far=False, line=True):
        self.cur += 1
        self.parts[self.cur] = Part(far, line)
        return self.cur

    # pixel (X, Y) has its centre at local (X - ox + 0.5, Y - oy + 0.5)
    def _box(self, x0, y0, x1, y1):
        X0 = max(0, int(math.floor(x0)) + self.ox)
        Y0 = max(0, int(math.floor(y0)) + self.oy)
        X1 = min(self.w - 1, int(math.ceil(x1)) + self.ox)
        Y1 = min(self.h - 1, int(math.ceil(y1)) + self.oy)
        for Y in range(Y0, Y1 + 1):
            for X in range(X0, X1 + 1):
                yield X, Y, X - self.ox + 0.5, Y - self.oy + 0.5

    def _set(self, X, Y, m):
        self.mat[Y][X] = m
        self.part_of[Y][X] = self.cur

    def ellipse(self, cx, cy, rx, ry, m):
        for X, Y, x, y in self._box(cx - rx, cy - ry, cx + rx, cy + ry):
            if ((x - cx) / rx) ** 2 + ((y - cy) / ry) ** 2 <= 1.0:
                self._set(X, Y, m)

    def capsule(self, x0, y0, x1, y1, r0, r1, m):
        """A tapered limb from (x0, y0) radius r0 to (x1, y1) radius r1."""
        dx, dy = x1 - x0, y1 - y0
        ll = dx * dx + dy * dy or 1e-6
        r = max(r0, r1)
        for X, Y, x, y in self._box(min(x0, x1) - r, min(y0, y1) - r, max(x0, x1) + r, max(y0, y1) + r):
            t = max(0.0, min(1.0, ((x - x0) * dx + (y - y0) * dy) / ll))
            px, py = x0 + dx * t, y0 + dy * t
            if (x - px) ** 2 + (y - py) ** 2 <= (r0 + (r1 - r0) * t) ** 2:
                self._set(X, Y, m)

    def poly(self, pts, m):
        xs = [p[0] for p in pts]
        ys = [p[1] for p in pts]
        n = len(pts)
        for X, Y, x, y in self._box(min(xs), min(ys), max(xs), max(ys)):
            inside = False
            j = n - 1
            for i in range(n):
                xi, yi = pts[i]
                xj, yj = pts[j]
                if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
                    inside = not inside
                j = i
            if inside:
                self._set(X, Y, m)

    def stroke(self, pts, r, m):
        for a, b in zip(pts, pts[1:]):
            self.capsule(a[0], a[1], b[0], b[1], r, r, m)

    def recolor(self, fn):
        """Repaint pixels of the current part: fn(x, y, m) -> new material."""
        for Y in range(self.h):
            row, prow = self.mat[Y], self.part_of[Y]
            for X in range(self.w):
                if prow[X] == self.cur and row[X]:
                    row[X] = fn(X - self.ox + 0.5, Y - self.oy + 0.5, row[X])


def bezier(p0, p1, p2, n=12):
    pts = []
    for i in range(n + 1):
        t = i / n
        a, b, c = (1 - t) ** 2, 2 * (1 - t) * t, t * t
        pts.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))
    return pts


# ---------------------------------------------------------------- shading

LIGHT = (-0.52, -0.74, 0.64)
_LN = math.sqrt(sum(c * c for c in LIGHT))
LIGHT = tuple(c / _LN for c in LIGHT)


def _distance(c):
    """Chamfer distance from each pixel to the edge of its own part."""
    w, h, P = c.w, c.h, c.part_of
    INF = 1 << 20
    d = [[0 if not c.mat[y][x] else INF for x in range(w)] for y in range(h)]

    def same(x, y, p):
        return 0 <= x < w and 0 <= y < h and c.mat[y][x] and P[y][x] == p

    for y in range(h):
        for x in range(w):
            if c.mat[y][x]:
                p = P[y][x]
                if not all(same(x + dx, y + dy, p) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                    d[y][x] = 3
    fwd = ((-1, 0, 3), (-1, -1, 4), (0, -1, 3), (1, -1, 4))
    for y in range(h):
        for x in range(w):
            if d[y][x] > 3:
                p = P[y][x]
                for dx, dy, cost in fwd:
                    if same(x + dx, y + dy, p):
                        d[y][x] = min(d[y][x], d[y + dy][x + dx] + cost)
    for y in range(h - 1, -1, -1):
        for x in range(w - 1, -1, -1):
            if d[y][x] > 3:
                p = P[y][x]
                for dx, dy, cost in fwd:
                    if same(x - dx, y - dy, p):
                        d[y][x] = min(d[y][x], d[y - dy][x - dx] + cost)
    return [[v / 3.0 for v in row] for row in d]


def shade(c, mats):
    """Render the canvas to RGBA rows (list of lists of 4-tuples)."""
    w, h, M, P = c.w, c.h, c.mat, c.part_of
    dist = _distance(c)

    # Each part rounds over its own thickness: a limb is a cylinder, a head a dome.
    reach = {}
    for y in range(h):
        for x in range(w):
            if M[y][x]:
                p = P[y][x]
                reach[p] = max(reach.get(p, 0.0), dist[y][x])
    height = [[0.0] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            if M[y][x]:
                R = max(2.0, reach[P[y][x]] * 0.95)
                t = min(1.0, dist[y][x] / R)
                height[y][x] = math.sqrt(1.0 - (1.0 - t) ** 2) * R

    # Smooth the height field so a part made of several overlapping shapes
    # shades as one surface instead of showing creases where they meet.
    for _ in range(3):
        nxt = [row[:] for row in height]
        for y in range(h):
            for x in range(w):
                if not M[y][x]:
                    continue
                p = P[y][x]
                total, n = 0.0, 0
                for dy in (-1, 0, 1):
                    for dx in (-1, 0, 1):
                        qx, qy = x + dx, y + dy
                        if 0 <= qx < w and 0 <= qy < h and M[qy][qx] and P[qy][qx] == p:
                            total += height[qy][qx]
                            n += 1
                nxt[y][x] = total / n
        height = nxt

    def hgt(x, y, p):
        if 0 <= x < w and 0 <= y < h and M[y][x] and P[y][x] == p:
            return height[y][x]
        return None

    tone = [[None] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            m = M[y][x]
            if not m:
                continue
            p = P[y][x]
            mat = mats[m]
            if mat.flat:
                tone[y][x] = 2
                continue
            hl = hgt(x - 1, y, p); hr = hgt(x + 1, y, p)
            hu = hgt(x, y - 1, p); hd = hgt(x, y + 1, p)
            sx = ((hr if hr is not None else 0.0) - (hl if hl is not None else 0.0)) * 0.5
            sy = ((hd if hd is not None else 0.0) - (hu if hu is not None else 0.0)) * 0.5
            nx, ny, nz = -sx, -sy, 1.0
            nl = math.sqrt(nx * nx + ny * ny + nz * nz)
            lam = max(0.0, (nx * LIGHT[0] + ny * LIGHT[1] + nz * LIGHT[2]) / nl)
            v = 0.26 + 0.84 * lam

            # A part drawn in front casts a short shadow down and to the right.
            for k in (1, 2, 3):
                qx, qy = x - (k + 1) // 2, y - k
                # Painted-on details (line=False) are flat to the surface and cast nothing.
                if 0 <= qx < w and 0 <= qy < h and M[qy][qx] and P[qy][qx] > p and c.parts[P[qy][qx]].line:
                    v -= 0.30
                    break
            if c.parts[p].far:
                v -= 0.20

            # A surface facing the viewer (v about 0.80) sits well inside the base
            # tone, so domes do not band into rings; only slopes turned toward
            # the light reach the highlights.
            if v < 0.36: t = 0
            elif v < 0.58: t = 1
            elif v < 0.88: t = 2
            elif v < (0.97 if mat.gloss else 1.04): t = 3
            else: t = 4
            if mat.soft: t = max(t, 1)
            tone[y][x] = t

    px = [[(0, 0, 0, 0)] * w for _ in range(h)]
    for y in range(h):
        for x in range(w):
            m = M[y][x]
            if not m:
                continue
            p = P[y][x]
            t = tone[y][x]
            mat = mats[m]
            # Where this part overlaps one drawn before it, a dark line separates them.
            if c.parts[p].line and not mat.flat:
                for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    qx, qy = x + dx, y + dy
                    if 0 <= qx < w and 0 <= qy < h and M[qy][qx] and P[qy][qx] < p and M[qy][qx] != m:
                        t = 0
                        break
            px[y][x] = mat.tones[t] + (255,)

    # Outline: the first ring outside takes a deep tone of what it wraps (lighter on
    # the lit upper-left side), the second is ink, so the figure holds against any floor.
    def solid(x, y):
        return 0 <= x < w and 0 <= y < h and M[y][x] != 0

    ring1 = []
    for y in range(h):
        for x in range(w):
            if M[y][x]:
                continue
            best = None
            for dx, dy in ((1, 0), (0, 1), (-1, 0), (0, -1)):
                if solid(x + dx, y + dy):
                    best = (dx, dy, M[y + dy][x + dx])
                    break
            if best is None:
                continue
            dx, dy, m = best
            lit_side = dx > 0 or dy > 0           # the shape is right of / below this pixel
            base = mats[m].tones[0]
            k = 0.62 if lit_side else 0.42
            ring1.append((x, y, tuple(int(cc * k + INK[i] * (1 - k)) for i, cc in enumerate(base))))
    for x, y, col in ring1:
        px[y][x] = col + (255,)
    filled = {(x, y) for x, y, _ in ring1}
    for y in range(h):
        for x in range(w):
            if M[y][x] or (x, y) in filled:
                continue
            if any(0 <= x + dx < w and 0 <= y + dy < h and (M[y + dy][x + dx] or (x + dx, y + dy) in filled)
                   for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = INK + (255,)
    return px


def to_rows(frames_px, fw, fh):
    """Pack frames left to right into bytearray rows for write_png."""
    rows = []
    for y in range(fh):
        r = bytearray()
        for f in frames_px:
            for p in f[y]:
                r.extend(p)
        rows.append(r)
    return rows
