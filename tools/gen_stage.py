"""Scrolling-stage scenery for all ten worlds.

A stage is a long walk left to right. Its scenery is built from tiling pieces,
one set per world theme, laid out by scripts/World/StageScenery.cs:

  backdrop_W.png   a painted panorama, sky and layered silhouettes, tiles in x
  floor_W.png      the ground from far behind the walkway to the camera, tiles in x;
                   the walkway band is the middle (z -3.6 .. 3.6)
  fence_W.png      the barrier along the back of the walkway, cut out, tiles in x
  prop_W_N.png     four props per world (+ _n normal maps), drawn and lit like the cast

Shared by every world:
  crate.png        a breakable crate (+ _n)
  food.png         four snacks that heal, side by side: cotton candy, hot dog,
                   popcorn, candy apple

Pixel density matches the arena (0.025 world units per floor/backdrop pixel,
0.01125 per prop pixel, the characters' density).

Run: bash tools/refresh_sprites.sh
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, ramp, shade, to_rows
from pixelpng import write_png

OUT = os.path.join("assets", "stages")
PX = 0.025
BAYER = ((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))


def dither(v, x, y, n=5):
    t = v * (n - 1) + (BAYER[y % 4][x % 4] / 16.0 - 0.47) * 0.7
    return max(0, min(n - 1, int(round(t))))


class Noise:
    """Value noise that tiles every `period` pixels in x, so textures repeat seamlessly."""

    def __init__(self, seed, cell, period):
        rnd = random.Random(seed)
        self.cell = cell
        self.wrap = max(1, int(round(period / cell)))
        self.g = [[rnd.random() for _ in range(256)] for _ in range(256)]

    def __call__(self, x, y):
        total, amp, norm = 0.0, 1.0, 0.0
        cell, wrap = self.cell, self.wrap
        for _ in range(3):
            xx, yy = x / cell, y / cell
            xi, yi = int(math.floor(xx)), int(math.floor(yy))
            fx, fy = xx - xi, yy - yi
            fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
            g = self.g
            x0, x1 = xi % wrap, (xi + 1) % wrap
            a = g[yi & 255][x0]; b = g[yi & 255][x1]
            c = g[(yi + 1) & 255][x0]; d = g[(yi + 1) & 255][x1]
            total += ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) * amp
            norm += amp
            amp *= 0.5
            cell /= 2.0
            wrap *= 2
        return total / norm


def save(name, w, h, px):
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for y in range(h):
        r = bytearray()
        for p in px[y]:
            r.extend(p if len(p) == 4 else tuple(p) + (255,))
        rows.append(r)
    write_png(os.path.join(OUT, name), w, h, rows)


# ------------------------------------------------------------------ themes

THEMES = {
    1: dict(name="midway", sky=((90, 150, 230), (255, 214, 170)), far=(120, 90, 150), near=(84, 60, 104),
            shape="tents", path=("boards", (176, 112, 70)), outer=("grass", (98, 168, 82)), fence=("picket", (238, 232, 220)),
            props=["lamp", "popcorn", "balloons", "tent"], lamp=(1.0, 0.82, 0.5)),
    2: dict(name="coast", sky=((120, 200, 240), (255, 220, 236)), far=(236, 160, 200), near=(200, 120, 170),
            shape="clouds", path=("boards", (214, 170, 120)), outer=("sand", (240, 214, 160)), fence=("rope", (220, 200, 160)),
            props=["umbrella", "candycart", "candytree", "lamp"], lamp=(1.0, 0.75, 0.85)),
    3: dict(name="forest", sky=((70, 120, 110), (190, 220, 160)), far=(40, 90, 70), near=(26, 64, 50),
            shape="trees", path=("dirt", (150, 110, 74)), outer=("grass", (72, 134, 78)), fence=("hedge", (60, 120, 60)),
            props=["mushroom", "tree", "lantern", "mushroom"], lamp=(0.7, 1.0, 0.7)),
    4: dict(name="junction", sky=((240, 170, 110), (255, 230, 170)), far=(150, 100, 80), near=(110, 70, 60),
            shape="wheel", path=("cobble", (170, 150, 140)), outer=("dirt", (190, 150, 100)), fence=("rail", (120, 80, 60)),
            props=["pins", "booth", "lamp", "barrel"], lamp=(1.0, 0.85, 0.55)),
    5: dict(name="mirrors", sky=((60, 50, 110), (170, 140, 220)), far=(120, 100, 180), near=(80, 66, 140),
            shape="spires", path=("checker", (200, 190, 230)), outer=("checker", (110, 100, 160)), fence=("mirror", (170, 210, 240)),
            props=["mirror", "crystal", "lamp", "crystal"], lamp=(0.7, 0.8, 1.0)),
    6: dict(name="peaks", sky=((110, 160, 220), (230, 240, 250)), far=(150, 160, 190), near=(100, 110, 140),
            shape="mountains", path=("rock", (150, 146, 150)), outer=("snow", (232, 238, 246)), fence=("rope", (120, 90, 60)),
            props=["pine", "rock", "flagpole", "pine"], lamp=(1.0, 0.9, 0.75)),
    7: dict(name="desert", sky=((240, 150, 90), (255, 222, 150)), far=(220, 160, 100), near=(190, 120, 80),
            shape="dunes", path=("sand", (226, 192, 128)), outer=("sand", (236, 206, 140)), fence=("cactus", (90, 160, 90)),
            props=["cactus", "popmachine", "barrel", "cactus"], lamp=(1.0, 0.8, 0.5)),
    8: dict(name="caverns", sky=((30, 24, 40), (70, 50, 70)), far=(60, 44, 60), near=(40, 30, 44),
            shape="stalactites", path=("cave", (96, 80, 90)), outer=("cave", (70, 58, 70)), fence=("pipes", (210, 170, 80)),
            props=["crystal", "pipes", "lantern", "rock"], lamp=(1.0, 0.6, 0.35)),
    9: dict(name="fortress", sky=((40, 30, 60), (150, 70, 80)), far=(90, 50, 70), near=(60, 34, 50),
            shape="castle", path=("flagstone", (130, 110, 120)), outer=("flagstone", (90, 74, 84)), fence=("battlement", (120, 96, 104)),
            props=["banner", "torch", "statue", "torch"], lamp=(1.0, 0.55, 0.3)),
    10: dict(name="finale", sky=((20, 14, 50), (90, 50, 130)), far=(110, 60, 150), near=(70, 36, 100),
             shape="bigtop", path=("stage", (180, 110, 70)), outer=("carpet", (160, 40, 60)), fence=("lights", (240, 200, 90)),
             props=["spotlight", "trophy", "lamp", "starprop"], lamp=(1.0, 0.9, 0.6)),
}


# ---------------------------------------------------------------- backdrop

BW, BH = 1024, 320          # 25.6 x 8 units


def backdrop(w, th):
    top, horizon = ramp(th["sky"][0]), ramp(th["sky"][1])
    far, near = ramp(th["far"]), ramp(th["near"])
    n = Noise(w * 31, 64, BW)
    rnd = random.Random(w * 7)
    px = [[None] * BW for _ in range(BH)]
    for y in range(BH):
        k = min(1.0, y / BH * 1.35)
        c = tuple(int(top[2][i] * (1 - k) + horizon[3][i] * k) for i in range(3))
        for x in range(BW):
            px[y][x] = c

    tops = {}

    def skyline(fn, pal, base, stripes=None):
        """A silhouette band: shaded down its height, a lit rim along its top
        edge, optionally striped like canvas."""
        for x in range(BW):
            h = fn(x)
            top_y = max(0, int(BH - h))
            tops[x] = top_y
            for y in range(top_y, BH):
                v = 0.5 + 0.25 * (n(x, y) - 0.5) - 0.25 * (y - top_y) / max(1.0, h)
                use = pal
                if stripes and int((x + (y - top_y) * 0.3) / stripes[0]) % 2 == 0: use = stripes[1]
                px[y][x] = use[4] if y - top_y < 2 else use[dither(max(0.05, v * base), x, y)]

    s = th["shape"]
    tau = 2 * math.pi
    if s == "tents":
        def far_fn(x):
            h = 90 + 12 * math.sin(x / BW * tau * 3)
            for cx in (120, 400, 700, 900):
                d = abs((x - cx + BW / 2) % BW - BW / 2)
                if d < 70: h = max(h, 190 - d * 1.5)
            return h
        skyline(far_fn, far, 1.0, stripes=(14, ramp((200, 150, 190))))
        for cx in (120, 400, 700, 900):                      # pennants on the tent tops
            for y in range(BH - 205, BH - 188):
                for x in range(cx, cx + int((y - (BH - 205)) * 0.0 + 12 - abs(y - (BH - 197)) * 0.7)):
                    px[y][x % BW] = ramp((240, 200, 90))[3]
            for y in range(BH - 205, BH - 186):
                px[y][cx] = near[0]
        skyline(lambda x: 60 + 14 * math.sin(x / BW * tau * 5 + 1) + 8 * n(x, 3), near, 0.9)
        for _ in range(26):                                  # strings of fair lights
            x, y = rnd.randrange(BW), rnd.randrange(BH - 70, BH - 30)
            px[y][x] = (255, 230, 150)
    elif s == "clouds":
        cloud = ramp((255, 240, 248))
        for _ in range(22):
            cx, cy, r = rnd.randrange(BW), rnd.randrange(30, 140), rnd.randrange(18, 40)
            for y in range(max(0, cy - r), min(BH, cy + r)):
                for x in range(cx - r * 2, cx + r * 2):
                    if ((x - cx) / 2.0) ** 2 + (y - cy) ** 2 < r * r:
                        px[y][x % BW] = cloud[dither(0.75 - (y - cy) / (3 * r), x % BW, y)]
        skyline(lambda x: 70 + 6 * math.sin(x / BW * tau * 2), ramp((90, 190, 220)), 1.0)
        skyline(lambda x: 40 + 10 * math.sin(x / BW * tau * 6), far, 1.0)
    elif s == "trees":
        skyline(lambda x: 150 + 40 * abs(math.sin(x / BW * tau * 44)) + 20 * n(x, 1), far, 1.0)
        skyline(lambda x: 100 + 50 * abs(math.sin(x / BW * tau * 68 + 1)) + 20 * n(x, 9), near, 0.9)
    elif s == "wheel":
        cx, cy, R = 300, 150, 110
        for y in range(BH):
            for x in range(BW):
                d = math.hypot(x - cx, y - cy)
                a = math.atan2(y - cy, x - cx)
                if abs(d - R) < 4 or (8 < d < R and abs(((a / tau * 16) % 1) - 0.5) < 0.5 * 3 / max(d, 1) * 16 / tau):
                    px[y][x] = far[1]
        skyline(lambda x: 80 + 20 * n(x, 2), far, 1.0)
        skyline(lambda x: 50 + 30 * (1 if (x // 64) % 3 == 0 else 0) + 10 * n(x, 5), near, 0.9)
    elif s == "spires":
        def fn(x):
            h = 100
            for cx in range(64, BW, 128):
                d = abs((x - cx + BW / 2) % BW - BW / 2)
                if d < 26: h = max(h, 240 - d * 5)
            return h
        skyline(fn, far, 1.0)
        skyline(lambda x: 70 + 10 * math.sin(x / BW * tau * 112), near, 0.9)
    elif s == "mountains":
        skyline(lambda x: 160 + 70 * abs(math.sin(x / BW * tau * 2.5)) + 30 * n(x, 1), far, 1.1)
        snow = ramp((240, 246, 252))
        for x in range(BW):                                  # snow caps follow each peak
            depth = 26 + 10 * n(x, 7)
            for y in range(tops[x], min(BH, tops[x] + int(depth))):
                if y > BH - 120: break
                px[y][x] = snow[dither(0.85 - (y - tops[x]) / 80, x, y)]
        skyline(lambda x: 90 + 40 * abs(math.sin(x / BW * tau * 4 + 2)) + 20 * n(x, 4), near, 1.0)
    elif s == "dunes":
        skyline(lambda x: 110 + 30 * math.sin(x / BW * tau * 2) + 15 * n(x, 1), far, 1.0)
        skyline(lambda x: 70 + 25 * math.sin(x / BW * tau * 3 + 2), near, 1.0)
    elif s == "stalactites":
        for x in range(BW):
            h = 40 + 30 * abs(math.sin(x / BW * tau * 78)) + (50 if (x * 7) % 61 < 6 else 0)
            for y in range(0, int(h)):
                px[y][x] = near[dither(0.3, x, y)]
        skyline(lambda x: 90 + 30 * n(x, 1), far, 1.0)
        for _ in range(60):
            px[rnd.randrange(BH - 80, BH - 10)][rnd.randrange(BW)] = (180, 240, 255)
    elif s == "castle":
        def fn(x):
            h = 130 + (20 if (x // 16) % 2 == 0 else 0)
            for cx in (150, 520, 860):
                d = abs((x - cx + BW / 2) % BW - BW / 2)
                if d < 34: h = max(h, 250 + (14 if (x // 8) % 2 == 0 else 0))
            return h
        skyline(fn, far, 1.0)
        for _ in range(40):
            x, y = rnd.randrange(BW), rnd.randrange(BH - 200, BH - 40)
            if px[y][x] in far:
                for dy in range(5):
                    for dx in range(3):
                        px[min(BH - 1, y + dy)][(x + dx) % BW] = (255, 200, 110)
        skyline(lambda x: 60 + 10 * n(x, 3), near, 0.9)
    else:  # bigtop: night, stars, a giant tent
        for _ in range(140):
            px[rnd.randrange(0, 180)][rnd.randrange(BW)] = (255, 250, 220) if rnd.random() < 0.7 else (255, 210, 120)
        cx = 512
        red, cream = ramp((200, 50, 70)), ramp((240, 220, 200))
        for x in range(BW):
            d = abs(x - cx)
            h = 260 - d * 0.55 if d < 420 else 30
            for y in range(max(0, int(BH - h)), BH):
                pal = red if int((x - cx) / 22 + (BH - y) * 0.02) % 2 == 0 else cream
                px[y][x] = pal[dither(0.45 + 0.2 * (n(x, y) - 0.5), x, y)]
        skyline(lambda x: 40 + 8 * n(x, 2), near, 0.8)
    save(f"backdrop_{w}.png", BW, BH, px)


# ------------------------------------------------------------------- floor

FW, FH = 384, 600            # 9.6 x 15 units: z from -9 (top row) to 6 (bottom)
BAND_FAR, BAND_NEAR = -3.6, 3.6


def z_of(y):
    return -9 + (y + 0.5) * PX


def paint_ground(kind, base, x, y, n, rnd_v):
    pal = ramp(base)
    if kind == "boards":
        plank = 14
        if y % plank == 0 or (x + (y // plank) * 97) % 128 == 0:
            return pal[0]
        v = 0.55 + 0.12 * (n(x * 0.3, y * 3) - 0.5) + (0.12 if y % plank == 1 else 0) - (0.12 if y % plank == plank - 1 else 0)
        return pal[dither(v, x, y)]
    if kind == "cobble":
        cy = y // 16
        lx, ly = (x + cy * 8) % 16, y % 16
        cx = (x + cy * 8) // 16
        if lx < 1 or ly < 1: return pal[0]
        v = 0.5 + 0.2 * (((cx * 37 + cy * 91) % 7) / 7 - 0.5) + (0.15 if ly < 4 else 0)
        return pal[dither(v, x, y)]
    if kind == "checker":
        on = ((x // 24) + (y // 24)) % 2 == 0
        return ramp(base if on else tuple(int(c * 0.55) for c in base))[dither(0.55 + 0.1 * (n(x, y) - 0.5), x, y)]
    if kind == "flagstone":
        cy = y // 30
        lx, ly = (x + cy * 21) % 48, y % 30
        if lx < 2 or ly < 2: return pal[0]
        return pal[dither(0.5 + 0.18 * (n(x, y) - 0.5) + (0.12 if ly < 6 else 0), x, y)]
    if kind == "stage":
        if y % 18 == 0: return pal[0]
        return pal[dither(0.55 + 0.1 * (n(x * 0.2, y * 2) - 0.5), x, y)]
    if kind == "carpet":
        return pal[dither(0.45 + 0.1 * (n(x, y) - 0.5) + (0.2 if (x // 4 + y // 4) % 9 == 0 else 0), x, y)]
    v = 0.5 + 0.3 * (n(x, y) - 0.5)
    if kind == "grass" and rnd_v < 0.04: v += 0.25
    if kind == "rock" and rnd_v < 0.03: v -= 0.25
    if kind == "cave" and rnd_v < 0.006: return (120, 220, 255)
    return pal[dither(v, x, y)]


def floor(w, th):
    n = Noise(w * 13, 40, FW)
    rnd = random.Random(w)
    px = [[None] * FW for _ in range(FH)]
    for y in range(FH):
        z = z_of(y)
        for x in range(FW):
            rv = rnd.random()
            if BAND_FAR <= z <= BAND_NEAR:
                kind, base = th["path"]
                c = paint_ground(kind, base, x, y, n, rv)
                if min(z - BAND_FAR, BAND_NEAR - z) < 0.15: c = tuple(int(v * 0.7) for v in c)
            else:
                kind, base = th["outer"]
                c = paint_ground(kind, base, x, y, n, rv)
            px[y][x] = c
    save(f"floor_{w}.png", FW, FH, px)


# ------------------------------------------------------------------- fence

FEW, FEH = 384, 72          # 9.6 x 1.8 units, cut out


def fence(w, th):
    kind, base = th["fence"]
    pal = ramp(base)
    px = [[(0, 0, 0, 0)] * FEW for _ in range(FEH)]

    def put(x, y, c):
        if 0 <= x < FEW and 0 <= y < FEH: px[y][x] = tuple(c) + (255,)

    if kind == "picket":
        for x0 in range(0, FEW, 16):
            for x in range(x0 + 2, x0 + 13):
                d = abs(x - x0 - 7)
                for y in range(30 - (4 - d) if d < 4 else 30, FEH):
                    put(x, y, pal[dither(0.8 - d * 0.05 - (0.4 if x == x0 + 12 else 0), x, y)])
        for yr in (40, 60):
            for x in range(FEW):
                for y in range(yr, yr + 5): put(x, y, pal[dither(0.55 if y > yr + 1 else 0.85, x, y)])
    elif kind in ("rope", "lights", "pipes"):
        post = ramp((120, 80, 50)) if kind == "rope" else pal
        for x0 in range(0, FEW, 96):
            for x in range(x0 + 44, x0 + 52):
                for y in range(16, FEH): put(x, y, post[dither(0.7 - (x - x0 - 44) * 0.06, x, y)])
        if kind == "pipes":
            for i, x0 in enumerate(range(4, FEW, 12)):
                top = 10 + (i * 7) % 24
                for x in range(x0, x0 + 9):
                    for y in range(top, FEH): put(x, y, pal[dither(0.85 - (x - x0) * 0.08, x, y)])
        else:
            line = pal if kind == "rope" else ramp((60, 40, 50))
            for x in range(FEW):
                y = int(26 + 12 * math.sin(math.pi * ((x % 96) / 96.0)))
                for dy in range(3): put(x, y + dy, line[dither(0.7 - dy * 0.2, x, y + dy)])
                if kind == "lights" and x % 24 == 12:
                    for dy in range(3, 8):
                        for dx in range(-2, 3): put(x + dx, y + dy, (255, 230, 150))
    elif kind == "hedge":
        nn = Noise(w, 12, FEW)
        for x in range(FEW):
            h = 22 + 10 * nn(x, 0)
            for y in range(int(h), FEH): put(x, y, pal[dither(0.3 + 0.5 * nn(x, y), x, y)])
    elif kind == "rail":
        for yr in (30, 50):
            for x in range(FEW):
                for y in range(yr, yr + 5): put(x, y, pal[dither(0.7 if y < yr + 2 else 0.4, x, y)])
        for x0 in range(0, FEW, 48):
            for x in range(x0, x0 + 6):
                for y in range(22, FEH): put(x, y, pal[dither(0.6, x, y)])
    elif kind == "mirror":
        frame = ramp((220, 180, 90))
        for x0 in range(0, FEW, 64):
            for x in range(x0 + 4, x0 + 60):
                for y in range(8, FEH):
                    edge = x < x0 + 7 or x > x0 + 56 or y < 11
                    shine = 0.9 if ((x - x0) + (FEH - y)) % 40 < 6 else 0.6
                    put(x, y, frame[2] if edge else pal[dither(shine, x, y)])
    elif kind == "cactus":
        for x0 in range(10, FEW, 64):
            for x in range(x0, x0 + 12):
                for y in range(14, FEH): put(x, y, pal[dither(0.8 - abs(x - x0 - 6) * 0.08, x, y)])
            for x in range(x0 - 10, x0):
                for y in range(30, 36): put(x, y, pal[2])
            for y in range(20, 36):
                for x in range(x0 - 12, x0 - 7): put(x, y, pal[2])
    elif kind == "battlement":
        for x in range(FEW):
            top = 8 if (x // 16) % 2 == 0 else 20
            for y in range(top, FEH):
                mortar = y % 14 == 0 or (x + (y // 14) * 12) % 24 == 0
                put(x, y, pal[0] if mortar else pal[dither(0.5 + (0.1 if y < top + 3 else 0), x, y)])
    save(f"fence_{w}.png", FEW, FEH, px)


# ------------------------------------------------------------------- props

def M(rgb, **kw):
    return Material(rgb, **kw)


def prop(kind, th):
    """Draw one prop; returns (canvas, materials)."""
    lamp = tuple(int(255 * c) for c in th["lamp"])
    mats = {1: M((60, 54, 70), gloss=True), 2: M(lamp, flat=True), 3: M((214, 56, 66)), 4: M((246, 236, 220), soft=True),
            5: M((246, 200, 84)), 6: M((150, 96, 58)), 7: M((76, 146, 228)), 8: M((90, 170, 90)), 9: M((150, 150, 160)),
            10: M((240, 140, 190)), 11: M((150, 110, 220)), 12: M((120, 200, 230), gloss=True), 13: M((34, 22, 40), flat=True),
            14: M((200, 50, 70)), 15: M((60, 110, 60))}
    if kind == "lamp":
        c = HDCanvas(80, 240)
        c.part(); c.capsule(40, 236, 40, 40, 4, 3, 1)
        c.part(); c.ellipse(40, 236, 16, 5, 1)
        c.part(); c.capsule(40, 44, 58, 36, 2.4, 2.4, 1)
        c.part(); c.ellipse(60, 46, 10, 10, 2)
        c.part(); c.poly([(48, 36), (72, 36), (66, 28), (54, 28)], 1)
    elif kind == "tent":
        c = HDCanvas(200, 170)
        c.part(); c.poly([(6, 166), (194, 166), (150, 60), (100, 14), (50, 60)], 3)
        c.recolor(lambda x, y, m: 4 if int((x - 100) / 14 + 100) % 2 == 0 else m)
        c.part(); c.poly([(84, 166), (116, 166), (110, 110), (90, 110)], 13)
        c.part(); c.capsule(100, 16, 100, 2, 1.5, 1.5, 6); c.part(); c.poly([(100, 2), (100, 12), (114, 7)], 5)
    elif kind == "popcorn":
        c = HDCanvas(130, 196)
        c.part(); c.poly([(20, 186), (110, 186), (110, 96), (20, 96)], 3)
        c.recolor(lambda x, y, m: 4 if int(x / 12) % 2 == 0 else m)
        c.part(); c.poly([(28, 96), (102, 96), (102, 40), (28, 40)], 12)
        for i in range(16):
            c.part(); c.ellipse(36 + (i * 23) % 60, 84 - (i * 7) % 30, 6, 5, 4)
        c.part(); c.poly([(14, 42), (116, 42), (100, 18), (30, 18)], 5)
        for x in (32, 98):
            c.part(); c.ellipse(x, 186, 9, 9, 1)
    elif kind == "balloons":
        c = HDCanvas(120, 230)
        c.part(); c.poly([(30, 226), (90, 226), (84, 180), (36, 180)], 6)
        cols = [3, 7, 5, 10, 11, 8]
        for i, (bx, by) in enumerate([(40, 60), (70, 44), (90, 76), (56, 96), (30, 110), (82, 118)]):
            c.part(); c.stroke([(60, 180), (bx, by + 18)], 0.7, 13)
            c.part(); c.ellipse(bx, by, 14, 17, cols[i % len(cols)])
    elif kind == "umbrella":
        c = HDCanvas(180, 200)
        c.part(); c.capsule(90, 196, 90, 50, 2.5, 2.5, 4)
        c.part(); c.poly([(10, 64), (170, 64), (150, 36), (90, 14), (30, 36)], 3)
        c.recolor(lambda x, y, m: 4 if int((x + 6) / 26) % 2 == 0 else m)
    elif kind == "candycart":
        c = HDCanvas(150, 200)
        c.part(); c.poly([(16, 180), (134, 180), (134, 110), (16, 110)], 10)
        c.part(); c.poly([(10, 112), (140, 112), (124, 76), (26, 76)], 4)
        c.recolor(lambda x, y, m: 10 if int(x / 14) % 2 == 0 else m)
        for i, x in enumerate((44, 76, 108)):
            c.part(); c.capsule(x, 76, x, 46, 1.2, 1.2, 4); c.part(); c.ellipse(x, 36, 13, 12, 10 if i % 2 else 11)
        for x in (36, 114):
            c.part(); c.ellipse(x, 184, 12, 12, 1)
    elif kind == "candytree":
        c = HDCanvas(140, 230)
        c.part(); c.capsule(70, 226, 70, 110, 6, 4, 4)
        c.recolor(lambda x, y, m: 3 if int((y + x) / 10) % 2 == 0 else m)
        for bx, by, r in ((70, 70, 44), (40, 96, 30), (100, 92, 32)):
            c.part(); c.ellipse(bx, by, r, r * 0.9, 10)
    elif kind in ("tree", "pine"):
        c = HDCanvas(170, 260)
        c.part(); c.capsule(85, 256, 85, 150, 9, 6, 6)
        if kind == "tree":
            for bx, by, r in ((85, 90, 56), (50, 130, 40), (122, 126, 42), (85, 150, 40)):
                c.part(); c.ellipse(bx, by, r, r * 0.88, 15)
        else:
            for i in range(4):
                y0 = 40 + i * 40
                c.part(); c.poly([(85, y0 - 30), (85 + 36 + i * 12, y0 + 46), (85 - 36 - i * 12, y0 + 46)], 15)
                c.recolor(lambda x, y, m, y0=y0: 4 if y < y0 - 12 else m)
    elif kind == "mushroom":
        c = HDCanvas(150, 180)
        c.part(); c.capsule(75, 176, 75, 90, 16, 13, 4)
        c.part(); c.ellipse(75, 82, 68, 46, 3)
        c.recolor(lambda x, y, m: 4 if ((x - 75) ** 2 / 300 + (y - 70) ** 2 / 200) % 7 < 1 else m)
    elif kind == "lantern":
        c = HDCanvas(70, 200)
        c.part(); c.capsule(35, 196, 35, 60, 3, 3, 6)
        c.part(); c.stroke([(35, 62), (52, 52), (52, 70)], 1.5, 6)
        c.part(); c.ellipse(52, 84, 11, 15, 2)
        c.part(); c.poly([(42, 72), (62, 72), (52, 62)], 3)
    elif kind == "pins":
        c = HDCanvas(140, 172)
        for i, x in enumerate((30, 58, 86, 114, 44, 72, 100)):
            y0 = 140 if i < 4 else 98
            c.part(); c.ellipse(x, y0, 12, 28, 4)
            c.recolor(lambda xx, yy, m, y0=y0: 3 if abs(yy - y0 + 14) < 4 else m)
    elif kind == "booth":
        c = HDCanvas(170, 210)
        c.part(); c.poly([(14, 206), (156, 206), (156, 80), (14, 80)], 7)
        c.part(); c.poly([(40, 150), (130, 150), (130, 96), (40, 96)], 13)
        c.part(); c.poly([(4, 84), (166, 84), (146, 40), (24, 40)], 3)
        c.recolor(lambda x, y, m: 4 if int(x / 16) % 2 == 0 else m)
        c.part(); c.poly([(56, 40), (114, 40), (106, 16), (64, 16)], 5)
    elif kind == "barrel":
        c = HDCanvas(110, 130)
        c.part(); c.ellipse(55, 70, 46, 58, 6)
        c.recolor(lambda x, y, m: 1 if abs(y - 36) < 4 or abs(y - 104) < 4 else m)
    elif kind == "mirror":
        c = HDCanvas(120, 230)
        c.part(); c.ellipse(60, 110, 50, 96, 5)
        c.part(); c.ellipse(60, 110, 40, 86, 12)
        c.part(); c.poly([(40, 226), (80, 226), (70, 200), (50, 200)], 5)
    elif kind == "crystal":
        c = HDCanvas(140, 180)
        c.part(); c.poly([(70, 6), (100, 90), (70, 176), (40, 90)], 12)
        c.part(); c.poly([(108, 60), (126, 120), (108, 176), (90, 120)], 11)
        c.part(); c.poly([(30, 80), (46, 130), (30, 176), (14, 130)], 11)
    elif kind == "rock":
        c = HDCanvas(150, 110)
        c.part(); c.ellipse(70, 72, 62, 36, 9)
        c.part(); c.ellipse(112, 82, 30, 24, 9)
    elif kind == "flagpole":
        c = HDCanvas(110, 250)
        c.part(); c.capsule(30, 246, 30, 10, 3, 2.5, 6)
        c.part(); c.poly([(32, 14), (32, 70), (104, 42)], 3)
        c.recolor(lambda x, y, m: 5 if int((x + y) / 10) % 2 == 0 else m)
    elif kind == "cactus":
        c = HDCanvas(130, 200)
        c.part(); c.capsule(65, 196, 65, 30, 18, 16, 8)
        c.part(); c.capsule(65, 110, 108, 90, 10, 10, 8); c.part(); c.capsule(108, 90, 108, 56, 10, 9, 8)
        c.part(); c.capsule(65, 130, 24, 116, 9, 9, 8); c.part(); c.capsule(24, 116, 24, 86, 9, 8, 8)
    elif kind == "popmachine":
        c = HDCanvas(140, 220)
        c.part(); c.poly([(20, 216), (120, 216), (120, 130), (20, 130)], 3)
        c.part(); c.poly([(26, 130), (114, 130), (114, 40), (26, 40)], 12)
        for i in range(24):
            c.part(); c.ellipse(34 + (i * 17) % 72, 122 - (i * 11) % 60, 6, 5, 4)
        c.part(); c.poly([(16, 44), (124, 44), (104, 14), (36, 14)], 5)
    elif kind == "pipes":
        c = HDCanvas(150, 240)
        for i, x in enumerate((24, 48, 72, 96, 120)):
            top = 30 + (i * 47) % 90
            c.part(); c.capsule(x, 236, x, top, 10, 10, 5)
            c.part(); c.ellipse(x, top + 30, 5, 3, 13)
    elif kind == "banner":
        c = HDCanvas(100, 260)
        c.part(); c.capsule(50, 256, 50, 8, 3, 3, 6)
        c.part(); c.poly([(20, 20), (80, 20), (80, 150), (50, 130), (20, 150)], 14)
        c.part(); c.ellipse(50, 70, 16, 16, 5)
    elif kind == "torch":
        c = HDCanvas(70, 200)
        c.part(); c.capsule(35, 196, 35, 70, 4, 3, 6)
        c.part(); c.poly([(22, 72), (48, 72), (44, 56), (26, 56)], 1)
        c.part(); c.ellipse(35, 44, 10, 16, 2)
    elif kind == "statue":
        c = HDCanvas(130, 240)
        c.part(); c.poly([(20, 236), (110, 236), (110, 190), (20, 190)], 9)
        c.part(); c.ellipse(65, 140, 30, 50, 9)
        c.part(); c.ellipse(65, 76, 22, 22, 9)
        c.part(); c.poly([(45, 60), (85, 60), (65, 20)], 9)
    elif kind == "spotlight":
        c = HDCanvas(120, 230)
        for x1 in (30, 60, 90):
            c.part(); c.capsule(60, 226, x1, 120, 3, 3, 1)
        c.part(); c.capsule(42, 96, 82, 70, 20, 24, 1)
        c.part(); c.ellipse(86, 68, 14, 18, 2)
    elif kind == "trophy":
        c = HDCanvas(120, 190)
        c.part(); c.poly([(30, 186), (90, 186), (82, 160), (38, 160)], 6)
        c.part(); c.capsule(60, 160, 60, 110, 6, 6, 5)
        c.part(); c.ellipse(60, 70, 40, 46, 5)
        c.part(); c.stroke([(22, 50), (8, 70), (26, 96)], 4, 5); c.part(); c.stroke([(98, 50), (112, 70), (94, 96)], 4, 5)
    else:  # starprop
        c = HDCanvas(160, 220)
        c.part(); c.capsule(80, 216, 80, 120, 4, 4, 6)
        pts = []
        for i in range(10):
            a = -math.pi / 2 + i * math.pi / 5
            r = 70 if i % 2 == 0 else 28
            pts.append((80 + r * math.cos(a), 80 + r * math.sin(a)))
        c.part(); c.poly(pts, 5)
    return c, mats


def write_prop(name, canvas, mats):
    rgba, nm = shade(canvas, mats, normals=True)
    save(name + ".png", canvas.w, canvas.h, rgba)
    save(name + "_n.png", canvas.w, canvas.h, nm)


# ----------------------------------------------------------------- shared

def crate_and_food():
    mats = {1: M((160, 104, 60)), 2: M((110, 70, 44)), 3: M((70, 70, 84), gloss=True), 4: M((214, 56, 66)), 5: M((246, 200, 84)),
            6: M((240, 150, 200), soft=True), 7: M((236, 236, 246), soft=True), 8: M((200, 120, 60)), 9: M((90, 170, 90))}
    c = HDCanvas(90, 90)
    c.part(); c.poly([(6, 86), (84, 86), (84, 10), (6, 10)], 1)
    c.recolor(lambda x, y, m: 2 if int((y - 10) / 12) % 2 == 1 else m)
    c.part(); c.stroke([(12, 16), (78, 80)], 4, 2); c.part(); c.stroke([(78, 16), (12, 80)], 4, 2)
    for x, y in ((10, 14), (80, 14), (10, 82), (80, 82)):
        c.part(); c.ellipse(x, y, 5, 5, 3)
    c.part(); c.ellipse(45, 48, 12, 12, 4)
    write_prop("crate", c, mats)
    # The crate is a Fighter in the game (it takes hits, flashes and breaks),
    # so it needs a frame map like any fighter's sheet.
    import json
    with open(os.path.join(OUT, "crate.frames.json"), "w") as fh:
        json.dump({"frame_width": 90, "frame_height": 90, "baseline": 87, "count": 1,
                   "clips": {"idle": [0], "hit": [0], "down": [0]}}, fh, indent=2)

    S = 56
    foods = []
    f = HDCanvas(S, S); f.part(); f.capsule(28, 54, 28, 30, 2, 2, 7); f.part(); f.ellipse(28, 20, 18, 16, 6); foods.append(f)
    f = HDCanvas(S, S); f.part(); f.ellipse(28, 34, 24, 10, 5); f.part(); f.ellipse(28, 30, 22, 5, 8); f.part(); f.stroke([(8, 28), (48, 28)], 1.4, 4); foods.append(f)
    f = HDCanvas(S, S); f.part(); f.poly([(12, 54), (44, 54), (48, 22), (8, 22)], 4)
    f.recolor(lambda x, y, m: 7 if int(x / 8) % 2 == 0 else m)
    for i in range(9):
        f.part(); f.ellipse(14 + (i * 11) % 30, 20 - (i * 5) % 10, 6, 5, 7)
    foods.append(f)
    f = HDCanvas(S, S); f.part(); f.capsule(28, 20, 28, 4, 1.5, 1.5, 2); f.part(); f.ellipse(28, 34, 18, 18, 4); f.part(); f.ellipse(36, 8, 6, 3, 9); foods.append(f)
    shaded = [shade(x, mats, normals=True) for x in foods]
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, "food.png"), S * 4, S, to_rows([p for p, _ in shaded], S, S))
    write_png(os.path.join(OUT, "food_n.png"), S * 4, S, to_rows([n for _, n in shaded], S, S))


def main():
    only = [int(a) for a in sys.argv[1:]] or list(range(1, 11))
    for w in only:
        th = THEMES[w]
        backdrop(w, th)
        floor(w, th)
        fence(w, th)
        for i, kind in enumerate(th["props"]):
            canvas, mats = prop(kind, th)
            write_prop(f"prop_{w}_{i}", canvas, mats)
        print(f"wrote stage set {w} ({th['name']})")
    crate_and_food()
    print("wrote crate and food")


if __name__ == "__main__":
    main()
