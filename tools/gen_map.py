"""World map data: the ten islands of the 3D world map.

Each world is one island in its own theme, Super Mario World style: sea all
round, a dirt road winding left to right through the stages, and the land
dotted with that world's scenery. Written for scripts/Map/WorldMap3D.cs:
the painted ground (with the road), a heightmap, and the spots where scenery
stands; the game builds the 3D island, its props, nodes and paths from them.

Node positions must match scripts/Map/Campaign.cs (BuildWorld's Road and
offsets). The map is 576 x 324 and shown at twice that size.

Run: bash tools/refresh_sprites.sh
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, ramp, shade, to_rows
from pixelpng import write_png

OUT = os.path.join("assets", "map")
MW, MH = 576, 324
BAYER = ((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))


def dither(v, x, y, n=5):
    t = v * (n - 1) + (BAYER[y % 4][x % 4] / 16.0 - 0.47) * 0.8
    return max(0, min(n - 1, int(round(t))))


class Noise:
    def __init__(self, seed, cell):
        rnd = random.Random(seed)
        self.cell = cell
        self.g = [[rnd.random() for _ in range(128)] for _ in range(128)]

    def __call__(self, x, y):
        total, amp, norm, f = 0.0, 1.0, 0.0, 1.0 / self.cell
        for _ in range(3):
            xx, yy = x * f, y * f
            xi, yi = int(math.floor(xx)), int(math.floor(yy))
            fx, fy = xx - xi, yy - yi
            fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
            g = self.g
            a = g[yi & 127][xi & 127]; b = g[yi & 127][(xi + 1) & 127]
            c = g[(yi + 1) & 127][xi & 127]; d = g[(yi + 1) & 127][(xi + 1) & 127]
            total += ((a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy) * amp
            norm += amp
            amp *= 0.5
            f *= 2.0
        return total / norm


# ------------------------------------------------------------- the road

def road(w, t):
    return (70 + t * 436, 170 + 70 * math.sin(t * 5.2 + w * 1.3) * (0.6 + 0.4 * math.cos(t * 2)))


def nodes(w):
    n = {}
    for s in range(1, 6):
        n[f"{w}-{s}"] = road(w, (s - 1) / 6)
    sx, sy = road(w, 1.5 / 6); n[f"{w}-S"] = (sx, sy - 62)
    bx, by = road(w, 2.5 / 6); n[f"{w}-*"] = (bx, by + 64)
    n[f"{w}-B"] = road(w, 5 / 6)
    n[f"{w}-G"] = road(w, 1.0)
    return n


def edges(w):
    e = [(f"{w}-{s}", f"{w}-{s + 1}") for s in range(1, 5)]
    e += [(f"{w}-2", f"{w}-S"), (f"{w}-3", f"{w}-*"), (f"{w}-5", f"{w}-B"), (f"{w}-B", f"{w}-G")]
    return e


def seg_dist(px, py, a, b):
    ax, ay = a; bx, by = b
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy or 1)))
    return math.hypot(px - ax - dx * t, py - ay - dy * t)


# ------------------------------------------------------------- themes

THEMES = [
    dict(land=(104, 176, 84), water=(58, 128, 212), deco=["tree", "tent", "tree"]),
    dict(land=(236, 160, 196), water=(70, 176, 206), deco=["candy", "lolly", "candy"]),
    dict(land=(64, 130, 76), water=(52, 96, 160), deco=["shroom", "tree", "tree"]),
    dict(land=(206, 168, 98), water=(64, 138, 200), deco=["pins", "tent", "tree"]),
    dict(land=(160, 150, 210), water=(80, 90, 170), deco=["crystal", "crystal", "tree"]),
    dict(land=(150, 150, 162), water=(70, 120, 190), deco=["peak", "peak", "pole"]),
    dict(land=(232, 202, 124), water=(68, 150, 196), deco=["popcorn", "cactus", "popcorn"]),
    dict(land=(104, 80, 92), water=(40, 50, 90), deco=["crystal", "pipe", "rock"]),
    dict(land=(132, 70, 78), water=(50, 40, 70), deco=["tower", "rock", "tower"]),
    dict(land=(84, 62, 128), water=(28, 22, 60), deco=["star", "tent", "star"]),
]

DECO_MATS = {
    1: Material((70, 150, 70)), 2: Material((130, 84, 50)), 3: Material((214, 56, 66)), 4: Material((246, 236, 220), soft=True),
    5: Material((246, 200, 84)), 6: Material((240, 140, 190)), 7: Material((150, 110, 220)), 8: Material((120, 200, 230), gloss=True),
    9: Material((150, 150, 160)), 10: Material((240, 240, 250), soft=True), 11: Material((90, 170, 90)), 12: Material((120, 60, 70)),
}


def deco_canvas(kind):
    c = HDCanvas(28, 30)
    if kind == "tree":
        c.part(); c.capsule(14, 28, 14, 18, 2, 2, 2)
        c.part(); c.ellipse(14, 13, 9, 9, 1)
    elif kind == "tent":
        c.part(); c.poly([(3, 28), (25, 28), (14, 6)], 3)
        c.recolor(lambda x, y, m: 4 if int(x / 4) % 2 == 0 else m)
        c.part(); c.ellipse(14, 6, 2, 2, 5)
    elif kind == "candy":
        c.part(); c.capsule(14, 28, 14, 16, 1.5, 1.5, 4)
        c.part(); c.ellipse(14, 11, 9, 8, 6)
    elif kind == "lolly":
        c.part(); c.capsule(14, 28, 14, 14, 1.5, 1.5, 4)
        c.part(); c.ellipse(14, 10, 7, 7, 7)
        c.recolor(lambda x, y, m: 4 if int(math.hypot(x - 14, y - 10) / 2) % 2 == 0 else m)
    elif kind == "shroom":
        c.part(); c.capsule(14, 28, 14, 17, 3, 3, 4)
        c.part(); c.ellipse(14, 15, 11, 7, 3)
        c.recolor(lambda x, y, m: 4 if (int(x / 5) + int(y / 4)) % 3 == 0 else m)
    elif kind == "pins":
        for i, x in enumerate((8, 14, 20)):
            y0 = 20 - (i % 2) * 4
            c.part(); c.ellipse(x, y0, 3, 7, 4)
            c.recolor(lambda xx, yy, m, y0=y0: 3 if abs(yy - y0 + 2) < 1 else m)
    elif kind == "crystal":
        c.part(); c.poly([(14, 3), (21, 16), (14, 28), (7, 16)], 8)
        c.part(); c.poly([(22, 12), (26, 20), (22, 28), (18, 20)], 7)
    elif kind == "peak":
        c.part(); c.poly([(1, 29), (27, 29), (14, 3)], 9)
        c.recolor(lambda x, y, m: 10 if y < 12 else m)
    elif kind == "pole":
        c.part(); c.capsule(6, 29, 6, 4, 1.5, 1.5, 2)
        c.part(); c.capsule(22, 29, 22, 6, 1.5, 1.5, 2)
        c.part(); c.stroke([(6, 6), (14, 9), (22, 7)], 0.8, 12)
    elif kind == "popcorn":
        for x, y in ((9, 20), (17, 19), (13, 13), (20, 12), (7, 12)):
            c.part(); c.ellipse(x, y, 5, 4.5, 10)
    elif kind == "cactus":
        c.part(); c.capsule(14, 29, 14, 6, 3.5, 3.5, 11)
        c.part(); c.capsule(14, 16, 22, 12, 2.2, 2.2, 11)
        c.part(); c.capsule(14, 19, 7, 14, 2.2, 2.2, 11)
    elif kind == "pipe":
        for i, x in enumerate((7, 12, 17, 22)):
            c.part(); c.capsule(x, 29, x, 10 + (i * 3) % 9, 2.2, 2.2, 5)
    elif kind == "rock":
        c.part(); c.ellipse(14, 22, 11, 7, 9)
    elif kind == "tower":
        c.part(); c.poly([(7, 29), (21, 29), (21, 9), (7, 9)], 12)
        c.recolor(lambda x, y, m: 9 if y < 12 and int(x / 3) % 2 == 0 else m)
        c.part(); c.poly([(5, 10), (23, 10), (14, 1)], 3)
    else:  # star
        pts = []
        for i in range(10):
            a = -math.pi / 2 + i * math.pi / 5
            r = 12 if i % 2 == 0 else 5
            pts.append((14 + r * math.cos(a), 15 + r * math.sin(a)))
        c.part(); c.poly(pts, 5)
    return c


_DECO_CACHE = {}


def deco_pixels(kind):
    if kind not in _DECO_CACHE:
        _DECO_CACHE[kind] = shade(deco_canvas(kind), DECO_MATS)
    return _DECO_CACHE[kind]


def world_map(w):
    th = THEMES[w - 1]
    land, water = ramp(th["land"]), ramp(th["water"])
    sand = ramp((232, 212, 156))
    dirt = ramp((198, 150, 98))
    n = Noise(100 + w, 46)
    nn = nodes(w)
    es = [(nn[a], nn[b]) for a, b in edges(w)]
    px = [[None] * MW for _ in range(MH)]
    is_land = [[False] * MW for _ in range(MH)]

    for y in range(MH):
        for x in range(MW):
            # An island: noise, pulled down toward the edges, and always land under the road.
            fx, fy = (x - MW / 2) / (MW / 2), (y - MH / 2) / (MH / 2)
            v = n(x, y) - 0.55 * (fx * fx * 0.8 + fy * fy) + 0.18
            near = min(seg_dist(x, y, a, b) for a, b in es)
            if near < 34: v = max(v, 0.5 - near / 120)
            if v > 0.25:
                is_land[y][x] = True
                h = (v - 0.25) * 2.4 + 0.15 * (n(x * 3, y * 3) - 0.5)
                px[y][x] = sand[dither(0.6, x, y)] if v < 0.29 else land[dither(min(0.95, 0.35 + h), x, y)]
            else:
                depth = max(0.0, 0.25 - v) * 3
                wave = 0.08 if (y + int(6 * math.sin(x / 9.0))) % 11 == 0 else 0.0
                px[y][x] = water[dither(max(0.05, 0.62 - depth + wave), x, y)]

    # The road: a dirt path, darker at its edges.
    for y in range(MH):
        for x in range(MW):
            d = min(seg_dist(x, y, a, b) for a, b in es)
            if d < 4.5:
                px[y][x] = dirt[0] if d > 3.4 else dirt[dither(0.65 - d * 0.05, x, y)]

    # For the 3D map: the ground with no scenery stamped on it, and a
    # heightmap (land rises with the noise, flattened along the road so the
    # path lies level, sinking under the sea at the shore).
    write_rgba(f"ground_{w}.png", px)
    HS = 3                                         # heightmap is a third the map's resolution
    hw, hh = MW // HS, MH // HS
    hrows = []
    for hy in range(hh):
        r = bytearray()
        for hx in range(hw):
            x, y = hx * HS + 1, hy * HS + 1
            fx, fy = (x - MW / 2) / (MW / 2), (y - MH / 2) / (MH / 2)
            v = n(x, y) - 0.55 * (fx * fx * 0.8 + fy * fy) + 0.18
            near = min(seg_dist(x, y, a, b) for a, b in es)
            if near < 34: v = max(v, 0.5 - near / 120)
            h = max(0.0, min(1.0, (v - 0.15) / 0.55))
            if near < 10: h = min(h, 0.32 + near * 0.004)          # the road runs level
            g = int(h * 255)
            r.extend((g, g, g, 255))
        hrows.append(r)
    write_png(os.path.join(OUT, f"height_{w}.png"), hw, hh, hrows)

    # Scenery on the land, kept clear of the road and the nodes.
    rnd = random.Random(500 + w)
    placed = []
    for _ in range(420):
        x, y = rnd.randint(14, MW - 28), rnd.randint(16, MH - 30)
        if not is_land[min(y + 28, MH - 1)][x + 14]: continue
        if min(seg_dist(x + 14, y + 26, a, b) for a, b in es) < 16: continue
        if any(math.hypot(x - qx, y - qy) < 20 for qx, qy in placed): continue
        if any(math.hypot(x + 14 - nx, y + 20 - ny) < 22 for nx, ny in nn.values()): continue
        placed.append((x, y))
        spr = deco_pixels(rnd.choice(th["deco"]))
        for yy in range(30):
            for xx in range(28):
                p = spr[yy][xx]
                if p[3] and 0 <= y + yy < MH and 0 <= x + xx < MW:
                    px[y + yy][x + xx] = p[:3]
        if len(placed) > 46: break

    import json
    with open(os.path.join(OUT, f"decos_{w}.json"), "w") as fh:
        json.dump([[x + 14, y + 28] for x, y in placed], fh)



def write_rgba(name, px):
    rows = []
    for y in range(MH):
        r = bytearray()
        for p in px[y]:
            r.extend(tuple(p[:3]) + (255,))
        rows.append(r)
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, name), MW, MH, rows)


def main():
    for w in range(1, 11):
        world_map(w)
        print("wrote {}/ground_{w}.png, height_{w}.png, decos_{w}.json".format(OUT, w=w))


if __name__ == "__main__":
    main()
