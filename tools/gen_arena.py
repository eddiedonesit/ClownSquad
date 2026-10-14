"""The circus ring: floor, big-top wall, and props.

The camera looks down at the arena, so the floor is most of every frame and
carries the look: a sawdust ring with a painted red-and-white curb and a worn
gold star, on a wooden board floor. The far wall only shows as a strip along
the top of the screen (about 1.2 units at that depth), so its detail is packed
low: canvas stripes, the performers' curtain, poles, bunting and a string of
lamp bulbs. Props are drawn with the HD character pipeline, normal maps and all.

Pixel art at a world density close to the characters', quantised to hue-shifted
ramps with ordered dithering rather than smooth gradients, so the ground reads
as the same hand-pixeled world as the sprites standing on it.

World layout (must match scripts/World/CircusArena.cs):
  floor: 28 x 18 units centred on (0, 0, -3); ring centred on (0, -1), radius 6.5
  back wall layers, 30 x 3 units each: tent_back z = -8.6, crowd z = -8.2,
  barrier z = -7.8; foreground strip 10 x 1.6 units at z = 4.6

Run: bash tools/refresh_sprites.sh
"""
import math
import os
import random
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, ramp, shade, to_rows
from pixelpng import write_png

OUT = os.path.join("assets", "arena")
PX = 0.025                                  # world units per floor/wall pixel

BAYER = ((0, 8, 2, 10), (12, 4, 14, 6), (3, 11, 1, 9), (15, 7, 13, 5))


def dither(v, x, y, n=5):
    """Value 0..1 to a tone index 0..n-1, ordered-dithered at the boundaries."""
    # A light touch: full-strength ordered dither reads as a screen-door grid on a floor this size.
    t = v * (n - 1) + (BAYER[y % 4][x % 4] / 16.0 - 0.47) * 0.6
    return max(0, min(n - 1, int(round(t))))


class Noise:
    """Smooth value noise on a seeded lattice, a few octaves summed."""

    def __init__(self, seed, cell, octaves=3):
        rnd = random.Random(seed)
        self.cell, self.octaves = cell, octaves
        self.grid = [[rnd.random() for _ in range(256)] for _ in range(256)]

    def _one(self, x, y):
        xi, yi = int(math.floor(x)), int(math.floor(y))
        fx, fy = x - xi, y - yi
        fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
        g = self.grid
        a = g[yi & 255][xi & 255]; b = g[yi & 255][(xi + 1) & 255]
        c = g[(yi + 1) & 255][xi & 255]; d = g[(yi + 1) & 255][(xi + 1) & 255]
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy

    def __call__(self, x, y):
        total, amp, norm, f = 0.0, 1.0, 0.0, 1.0 / self.cell
        for _ in range(self.octaves):
            total += self._one(x * f, y * f) * amp
            norm += amp
            amp *= 0.5
            f *= 2.0
        return total / norm


def write(name, w, h, px):
    os.makedirs(OUT, exist_ok=True)
    rows = []
    for y in range(h):
        r = bytearray()
        for p in px[y]:
            r.extend(p if len(p) == 4 else p + (255,))
        rows.append(r)
    write_png(os.path.join(OUT, name), w, h, rows)
    print("wrote {}/{}  {}x{}".format(OUT, name, w, h))


# --------------------------------------------------------------------- floor

SAWDUST = ramp((176, 136, 92))
SAWDUST_DARK = ramp((168, 124, 78))
BOARD = ramp((128, 80, 52))
CURB_RED = ramp((206, 48, 62))
CURB_WHITE = ramp((236, 228, 214))
STAR = ramp((222, 172, 70))
GAP = (52, 30, 34)


def _star_poly(r_out, r_in, points=5):
    pts = []
    for i in range(points * 2):
        a = -math.pi / 2 + i * math.pi / points
        r = r_out if i % 2 == 0 else r_in
        pts.append((r * math.cos(a), r * math.sin(a)))
    return pts


_STAR = _star_poly(2.3, 0.95)


def in_star(x, y, scale=1.0):
    """Inside test for the five-point star (straight edges), point toward the back wall."""
    x, y = x * scale, y * scale
    inside, j = False, len(_STAR) - 1
    for i in range(len(_STAR)):
        xi, yi = _STAR[i]
        xj, yj = _STAR[j]
        if (yi > y) != (yj > y) and x < (xj - xi) * (y - yi) / (yj - yi) + xi:
            inside = not inside
        j = i
    return inside


def floor():
    W, H = int(28 / PX), int(18 / PX)
    n_big, n_fine, n_wear = Noise(1, 90), Noise(2, 7, 2), Noise(3, 24)
    rnd = random.Random(7)
    px = [[None] * W for _ in range(H)]

    # Boards run left-right; each has a tint and staggered butt joints.
    board_h = 12
    joints, tints = {}, {}
    for b in range(H // board_h + 2):
        tints[b] = rnd.uniform(-0.12, 0.1)
        xs, x = [], rnd.randint(0, 120)
        while x < W:
            xs.append(x)
            x += rnd.randint(90, 170)
        joints[b] = xs

    cx, cz = 0.0, -1.0
    for y in range(H):
        wz = -12 + (y + 0.5) * PX
        for x in range(W):
            wx = -14 + (x + 0.5) * PX
            r = math.hypot(wx - cx, wz - cz)
            if r < 6.2:
                # Sawdust: broad tonal drifts, fine grain, darker trodden patches.
                # Kept low in contrast: the floor is the stage, the fighters are the show.
                v = 0.45 + 0.34 * (n_big(x, y) - 0.5) + 0.16 * (n_fine(x, y) - 0.5)
                v -= 0.10 * max(0.0, 1.0 - r / 4.0)            # scuffed where the action is
                speck = rnd.random()
                if speck < 0.004:
                    col = SAWDUST_DARK[1]
                elif speck < 0.007:
                    col = SAWDUST[4]
                else:
                    col = SAWDUST[dither(v, x, y)]
                # The worn star in the middle of the ring.
                sx, sz = wx - cx, wz - cz
                if in_star(sx, sz) and n_wear(x, y) >= 0.36:
                    edge = not in_star(sx, sz, 1.07)
                    col = STAR[0] if edge else STAR[dither(0.55 + 0.25 * (n_fine(x, y) - 0.5), x, y)]
                px[y][x] = col
            elif r < 6.85:
                # The painted ring curb: red and white blocks, lit along the
                # inner lip and falling into shadow at the outer edge.
                ang = math.degrees(math.atan2(wz - cz, wx - cx)) % 360
                pal = CURB_RED if int(ang / 7.5) % 2 == 0 else CURB_WHITE
                k = (r - 6.2) / 0.65
                if k < 0.12 or k > 0.9:
                    col = pal[0]
                else:
                    col = pal[dither(0.85 - 0.6 * k + 0.12 * (n_fine(x, y) - 0.5), x, y)]
                px[y][x] = col
            else:
                b = y // board_h
                if y % board_h == 0 or any(abs(x - j) < 1 for j in joints[b]):
                    px[y][x] = GAP
                    continue
                v = 0.45 + tints[b] + 0.25 * (n_fine(x * 0.35, y * 2.5) - 0.5)
                # top edge of each board catches the light, bottom edge shades
                yy = y % board_h
                if yy == 1: v += 0.22
                if yy == board_h - 1: v -= 0.2
                # a little sawdust kicked out over the boards near the ring
                if r < 7.6 and rnd.random() < (7.6 - r) * 0.25:
                    px[y][x] = SAWDUST[dither(0.5, x, y)]
                    continue
                col = BOARD[dither(v, x, y)]
                if (x - joints[b][0]) % 61 in (4, 5) and yy in (5, 6):
                    col = (60, 50, 58)                      # nail heads
                px[y][x] = col
    write("floor.png", W, H, px)


# ---------------------------------------------------------------------- wall
# The back of the ring is three layers, back to front, each its own quad in
# the scene so lights and haze separate them:
#   tent_back  the dark tent interior                      z = -8.6
#   crowd      two tiers of audience, three frames         z = -8.2
#   barrier    the ring barrier, poles, bunting, bulbs,    z = -7.8
#              and the performers' curtain
# Only about the bottom 1.2 units of any of them is on screen.

VELVET = ramp((92, 36, 98))
GOLD = ramp((238, 190, 72))
POLE = ramp((150, 96, 58))
PANEL = ramp((176, 40, 56))
FLAGS = [ramp(c) for c in ((214, 56, 66), (246, 200, 84), (76, 146, 228), (96, 182, 96))]
ROPE = (60, 40, 46)
POLES = (-12.5, -7.5, -2.5, 2.5, 7.5, 12.5)
CLEAR = (0, 0, 0, 0)
BARRIER_H = 0.55


def tent_back():
    """Deep in the tent, past the crowd: dim canvas falling away into shadow."""
    W, H = int(30 / PX), int(3 / PX)
    n = Noise(21, 40, 2)
    deep = ramp((70, 30, 62))
    px = [[None] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            wx = -15 + (x + 0.5) * PX
            v = 0.32 + 0.12 * math.cos((wx / 0.9) * 2 * math.pi) + 0.08 * (n(x, y) - 0.5)
            px[y][x] = deep[dither(v, x, y)]
    write("tent_back.png", W, H, px)


CROWD_W, CROWD_H = int(30 / PX), 64
SKINS = [(250, 214, 186), (222, 170, 128), (176, 120, 84), (120, 78, 54)]
HAIRS = [(60, 40, 30), (150, 90, 50), (230, 190, 100), (40, 30, 40), (200, 80, 60), (120, 120, 130)]
CLOTHES = [(170, 60, 70), (60, 100, 170), (70, 140, 90), (190, 150, 60), (120, 70, 150), (90, 90, 100), (200, 110, 60)]


def crowd():
    """Two tiers of the audience, in three frames: settled, bobbing, cheering.
    Pushed darker and duller than the ring, so it reads as further away."""
    def dim(rgb):
        return tuple(int(c * 0.72) for c in rgb)

    mats, m_id = {}, [1]

    def mat(rgb, **kw):
        key = (rgb, tuple(sorted(kw.items())))
        if key not in mats:
            mats[key] = m_id[0]
            m_id[0] += 1
        return mats[key]

    rnd = random.Random(31)
    people = []
    for row, (y_head, spacing, offset) in enumerate(((19, 20, 0), (33, 22, 11))):
        x = offset + rnd.randint(0, 6)
        while x < CROWD_W - 6:
            people.append(dict(
                x=x + rnd.uniform(-2, 2), y=y_head + rnd.uniform(-1, 1), row=row,
                skin=mat(dim(rnd.choice(SKINS)), soft=True),
                hair=mat(dim(rnd.choice(HAIRS))),
                cloth=mat(dim(rnd.choice(CLOTHES))),
                hat=rnd.random() < 0.22,
                hat_col=mat(dim(rnd.choice(CLOTHES))),
                cheers=rnd.random() < 0.65,
                phase=rnd.randint(0, 1),
            ))
            x += spacing + rnd.randint(-3, 4)

    def draw(frame):
        c = HDCanvas(CROWD_W, CROWD_H)
        for p in sorted(people, key=lambda q: q["row"]):
            far = p["row"] == 0
            bob = 0
            if frame == 1 and p["phase"]: bob = -1
            if frame == 2 and p["cheers"]: bob = -2
            x, y = p["x"], p["y"] + bob
            c.part(far=far)
            c.ellipse(x, y + 12, 9, 8, p["cloth"])                     # shoulders
            if frame == 2 and p["cheers"]:
                for side in (-1, 1):                                     # arms up
                    c.part(far=far)
                    c.capsule(x + side * 6, y + 9, x + side * 9, y - 7, 2.2, 1.8, p["cloth"])
                    c.part(far=far)
                    c.ellipse(x + side * 9, y - 8, 2.4, 2.4, p["skin"])
            c.part(far=far)
            c.ellipse(x, y, 5.5, 6, p["skin"])                          # head
            c.part(far=far)
            if p["hat"]:
                c.poly([(x - 6, y - 3), (x + 6, y - 3), (x, y - 13)], p["hat_col"])   # party hat
            else:
                c.ellipse(x, y - 3.5, 6, 3.5, p["hair"])
        return c

    material_table = {i: Material(rgb, **dict(kw)) for (rgb, kw), i in mats.items()}
    frames = [shade(draw(f), material_table) for f in range(3)]
    rows = to_rows(frames, CROWD_W, CROWD_H)
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, "crowd.png"), CROWD_W * 3, CROWD_H, rows)
    print("wrote {}/crowd.png  {}x{}  3 frames".format(OUT, CROWD_W * 3, CROWD_H))


def barrier():
    """The ring barrier, the curtain, poles, bunting and bulbs. Transparent
    elsewhere, so the crowd shows over the top of the barrier."""
    W, H = int(30 / PX), int(3 / PX)
    px = [[CLEAR] * W for _ in range(H)]
    emit = [[(0, 0, 0, 255)] * W for _ in range(H)]
    n = Noise(12, 30, 2)

    def height(y):
        return (H - y - 0.5) * PX

    for y in range(H):
        h = height(y)
        for x in range(W):
            wx = -15 + (x + 0.5) * PX
            if h < BARRIER_H:
                # Panels between gold posts, a gold rail on top, a star on each.
                cell = (wx + 15) % 1.5
                if h > BARRIER_H - 0.07:
                    px[y][x] = GOLD[dither(0.8 if h > BARRIER_H - 0.035 else 0.4, x, y)]
                elif cell < 0.06:
                    px[y][x] = GOLD[dither(0.5, x, y)]
                else:
                    v = 0.55 + 0.1 * (n(x, y) - 0.5) - 0.4 * max(0.0, 1.0 - h / 0.18)
                    px[y][x] = PANEL[dither(v, x, y)]
                    sx, sy = cell - 0.78, h - 0.27
                    if in_star(sx / 0.075, -sy / 0.075):
                        px[y][x] = GOLD[dither(0.7, x, y)]
            # the performers' entrance: velvet curtains and a dark gap between
            if abs(wx) < 1.5 and h < 1.75:
                if abs(wx) < 0.18:
                    px[y][x] = (24, 12, 30)
                else:
                    fold = math.cos(wx * 14.0) * 0.5 + 0.5
                    px[y][x] = VELVET[dither(0.25 + 0.5 * fold - 0.35 * max(0.0, 1.0 - h / 0.4), x, y)]
                if 1.55 < h < 1.75:
                    px[y][x] = GOLD[0] if (h < 1.6 and x % 3 == 0) else GOLD[dither(0.75 if (x // 3) % 2 else 0.45, x, y)]

    for pole_x in POLES:
        x0 = int((pole_x + 15) / PX)
        for y in range(H):
            for dx in range(-3, 4):
                px[y][x0 + dx] = POLE[dither(0.85 - abs(dx + 1) * 0.18, x0 + dx, y)] if abs(dx) < 3 else POLE[0]
        for y in range(H - int(0.62 / PX), H - int(0.5 / PX)):
            for dx in range(-4, 5):
                px[y][x0 + dx] = GOLD[dither(0.8 - abs(dx) * 0.1, x0 + dx, y)]

    for a, b in zip(POLES, POLES[1:]):
        flag_i = 0
        steps = int((b - a) / PX)
        for i in range(steps + 1):
            t = i / steps
            wx = a + (b - a) * t
            h = 1.2 - 0.22 * math.sin(math.pi * t)
            x, y = int((wx + 15) / PX), int(H - h / PX)
            for dy in (0, 1):
                px[y + dy][x] = ROPE + (255,)
            if i % 16 == 8:
                pal = FLAGS[flag_i % len(FLAGS)]
                flag_i += 1
                for fy in range(1, 11):
                    half = int((10 - fy) * 0.55)
                    for fx in range(-half, half + 1):
                        px[y + fy][x + fx] = pal[0] if abs(fx) == half else pal[dither(0.75 - fx * 0.05, x + fx, y + fy)]
            if i % 16 == 0 and 0 < i < steps:
                for by in range(1, 5):
                    for bx in range(-2, 3):
                        if bx * bx + (by - 2.5) ** 2 <= 5:
                            px[y + by][x + bx] = (255, 236, 160)
                            emit[y + by][x + bx] = (255, 210, 120, 255)
    write("barrier.png", W, H, px)
    write("barrier_emit.png", W, H, emit)


# ---------------------------------------------------------------- foreground
# Dark shapes right at the camera along the bottom corners, rim-lit by the
# lamps: the layer that says the ring is a real place you are looking into.

FG_MATS = {
    1: Material((70, 34, 70)), 2: Material((96, 34, 46)), 3: Material((150, 110, 50), gloss=True),
    4: Material((54, 38, 60)), 5: Material((120, 40, 50)), 6: Material((180, 170, 150)),
}


def foreground():
    W, H = 960, 150
    c = HDCanvas(W, H)
    # brass-topped rope posts at both corners, the velvet rope sagging off-screen
    for x0, out in ((150, -1), (W - 150, 1)):
        c.part()
        c.capsule(x0, H, x0, 40, 7, 6, 4)
        c.part()
        c.ellipse(x0, 36, 10, 10, 3)
        c.part()
        pts = [(x0 + out * t, 42 + 26 * math.sin(math.pi * min(1.0, t / 300.0) * 0.5)) for t in range(0, 320, 8)]
        c.stroke(pts, 4, 2)
    # a spilled popcorn bucket, bottom left
    c.part()
    c.poly([(20, H), (110, H), (100, 78), (30, 78)], 5)
    c.recolor(lambda x, y, m: 6 if int(x / 14) % 2 == 0 else m)
    for i in range(14):
        c.part()
        c.ellipse(30 + (i * 37) % 75, 72 - (i * 13) % 14, 6, 5, 6)
    # the ringmaster's stool, bottom right
    c.part()
    c.ellipse(W - 70, 88, 52, 12, 1)
    c.part()
    c.poly([(W - 112, 92), (W - 28, 92), (W - 40, H), (W - 100, H)], 1)
    c.recolor(lambda x, y, m: 3 if 104 < y < 110 else m)
    rgba, nm = shade(c, FG_MATS, normals=True)
    write_png(os.path.join(OUT, "foreground.png"), W, H, to_rows([rgba], W, H))
    write_png(os.path.join(OUT, "foreground_n.png"), W, H, to_rows([nm], W, H))
    print("wrote {}/foreground.png (+ normal map)".format(OUT))


# --------------------------------------------------------------------- props
# Drawn and shaded exactly like the characters, at their pixel density.

PROP_MATS = {
    1: Material((214, 56, 66)), 2: Material((246, 200, 84)), 3: Material((236, 236, 246), soft=True),
    4: Material((150, 96, 58)), 5: Material((96, 60, 40)), 6: Material((76, 146, 228)),
    7: Material((70, 70, 84), gloss=True), 8: Material((34, 22, 40), flat=True), 9: Material((120, 76, 50)),
}
RED, YEL, WHT, WOOD, WOOD_D, BLUE, IRON, INK, WOOD_M = 1, 2, 3, 4, 5, 6, 7, 8, 9


def prop_tub():
    """The elephant tub: a striped circus pedestal with a star on the front."""
    c = HDCanvas(140, 124)
    c.part()
    c.poly([(22, 112), (118, 112), (110, 30), (30, 30)], RED)
    c.recolor(lambda x, y, m: YEL if int(x / 12) % 2 == 0 else m)
    c.part()
    c.ellipse(70, 30, 42, 9, BLUE)                       # top
    c.part()
    c.ellipse(70, 112, 49, 7, BLUE)                      # base rim
    c.part()
    c.poly([(70, 52), (77, 66), (92, 67), (80, 76), (85, 91), (70, 82), (55, 91), (60, 76), (48, 67), (63, 66)], WHT)
    return c


def prop_crates():
    """Three stacked wooden crates with iron corners."""
    c = HDCanvas(164, 150)

    def crate(x0, y0, s):
        c.part()
        c.poly([(x0, y0), (x0 + s, y0), (x0 + s, y0 + s), (x0, y0 + s)], WOOD)
        c.recolor(lambda x, y, m: WOOD_M if int((y - y0) / 9) % 2 == 0 else m)
        c.part()
        c.stroke([(x0 + 6, y0 + 6), (x0 + s - 6, y0 + s - 6)], 3.2, WOOD_D)
        c.part()
        c.stroke([(x0 + s - 6, y0 + 6), (x0 + 6, y0 + s - 6)], 3.2, WOOD_D)
        for cx, cy in ((x0 + 5, y0 + 5), (x0 + s - 5, y0 + 5), (x0 + 5, y0 + s - 5), (x0 + s - 5, y0 + s - 5)):
            c.part()
            c.ellipse(cx, cy, 5, 5, IRON)
    crate(14, 72, 72)
    crate(80, 76, 68)
    crate(46, 10, 62)
    return c


def prop_cannon():
    """The human cannonball's cannon: a striped barrel on a cart, aimed up."""
    c = HDCanvas(200, 152)
    c.part()
    c.capsule(40, 108, 160, 40, 22, 28, RED)
    c.recolor(lambda x, y, m: YEL if int((x * 0.8 - y * 0.6) / 14) % 2 == 0 else m)
    c.part()
    c.ellipse(162, 38, 20, 26, IRON)                      # the muzzle
    c.part()
    c.ellipse(166, 38, 12, 17, INK)
    c.part()
    c.poly([(30, 112), (130, 112), (120, 128), (40, 128)], WOOD)
    for wx in (52, 112):
        c.part()
        c.ellipse(wx, 126, 20, 20, BLUE)
        c.part()
        c.ellipse(wx, 126, 6, 6, YEL)
    return c


def write_prop(name, canvas):
    rgba, nm = shade(canvas, PROP_MATS, normals=True)
    os.makedirs(OUT, exist_ok=True)
    write_png(os.path.join(OUT, name + ".png"), canvas.w, canvas.h, to_rows([rgba], canvas.w, canvas.h))
    write_png(os.path.join(OUT, name + "_n.png"), canvas.w, canvas.h, to_rows([nm], canvas.w, canvas.h))
    print("wrote {}/{}.png (+ normal map)".format(OUT, name))


def main():
    floor()
    tent_back()
    crowd()
    barrier()
    foreground()
    write_prop("prop_tub", prop_tub())
    write_prop("prop_crates", prop_crates())
    write_prop("prop_cannon", prop_cannon())
    loot_icons()



# ---------------------------------------------------------------- loot icons
# What a drop looks like lying in the sawdust: one icon per slot, side by side
# in a single sheet in ItemSlot order (Mallet, Hat, Ruff, Shoes).

def loot_icons():
    S = 64
    sheets = []
    def icon(kind):
        c = HDCanvas(S, S)
        if kind == "mallet":
            c.part()
            c.capsule(14, 52, 40, 22, 3, 3, WOOD)
            c.part()
            c.capsule(30, 12, 52, 34, 11, 11, RED)
            c.part()
            c.ellipse(52, 34, 8, 8, YEL)
        elif kind == "hat":
            c.part()
            c.poly([(14, 50), (50, 50), (30, 8)], RED)
            c.recolor(lambda x, y, m: YEL if int((x + y * 0.7) / 6) % 3 == 0 else m)
            c.part()
            c.ellipse(32, 50, 20, 5, YEL)
            c.part()
            c.ellipse(30, 8, 5, 5, WHT)
        elif kind == "ruff":
            for i, x in enumerate(range(10, 56, 9)):
                c.part()
                c.ellipse(x, 34 + abs(x - 32) * 0.12, 7, 9, WHT if i % 2 == 0 else YEL)
        else:
            c.part()
            c.ellipse(28, 44, 20, 9, RED)
            c.ellipse(44, 42, 13, 11, RED)
            c.recolor(lambda x, y, m: WOOD_D if y > 50 else m)
            c.part()
            c.ellipse(26, 33, 6, 3, WHT)
        return c
    for k in ("mallet", "hat", "ruff", "shoes"):
        sheets.append(shade(icon(k), PROP_MATS))
    write_png(os.path.join(OUT, "loot_icons.png"), S * 4, S, to_rows(sheets, S, S))
    print("wrote {}/loot_icons.png  4 icons".format(OUT))



if __name__ == "__main__":
    main()
