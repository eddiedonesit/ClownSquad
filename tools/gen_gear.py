"""The squad's gear, as worn: every hat, neckpiece, pair of shoes and weapon
charm, drawn to sit on the bare clown sheets (gen_sprite_hd writes those, and
the named points each frame carries: the hat brim, the neck, both feet, the
near hand).

Each model is drawn round its anchor, at the centre of its own canvas, in the
squad's materials, so it shades and lights like the clown it sits on. It comes
in a set of colourways (an item's id picks one, so two of the same hat seldom
match; legendary gear is gold), plus each clown's own colours for the hat and
shoes they wear with nothing equipped. Every one is drawn turned through eight
steps of 45 degrees, so it can tumble with the clown: clips r0..r7, and f0..f7
for the far shoe, drawn a tone darker.

Writes assets/sprites/gear/<slot>_<model>_<colourway>.png (+ _n.png, .frames.json).
Run: python tools/gen_gear.py   (bash tools/refresh_sprites.sh runs it)
"""
import json
import math
import os
import sys
from multiprocessing import Pool

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import gen_sprite_hd as hd
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier, rotated
from pixelpng import write_png

OUT = os.path.join("assets", "sprites", "gear")
MAIN, ACC = hd.RED, hd.YELLOW            # a model's two dyes
WHITE, GOLD, INK, METAL, SOLE = hd.WHITE, hd.GOLD, hd.INK, hd.METAL, hd.SOLE

# Colourways: main, accent. The last is gold, for legendary gear.
COLOURWAYS = [
    ((218, 56, 68), (246, 200, 84)),     # 0 circus red and gold
    ((70, 130, 230), (250, 250, 255)),   # 1 blue and white
    ((60, 170, 100), (250, 220, 80)),    # 2 green and yellow
    ((150, 70, 190), (120, 220, 210)),   # 3 purple and teal
    ((240, 140, 40), (60, 40, 60)),      # 4 orange and black
    ((44, 40, 54), (220, 60, 70)),       # 5 black and red
    ((252, 204, 72), (255, 250, 235)),   # 6 gold (legendary)
]
# Each clown's own colours, for what they wear with nothing equipped.
CLOWN_COLOURS = {
    "pip": ((218, 56, 68), (246, 200, 84)),
    "poppy": ((120, 70, 200), (250, 214, 96)),
    "bingo": ((40, 150, 110), (246, 200, 84)),
    "dot": ((60, 60, 160), (246, 200, 84)),
}


# ------------------------------------------------------------------ hats
# Drawn round the hat brim's centre, upward (y < 0 is up).

def hat_party(c, far=False):
    c.part()
    c.poly([(-17, 0), (17, 0), (8, -17), (-4, -32), (-12, -15)], MAIN)
    c.recolor(lambda x, y, m: ACC if math.floor((x + y * 0.75) / 5.0) % 3 == 0 else m)
    c.part()
    c.ellipse(0, 0, 19.5, 4.2, ACC)
    c.recolor(lambda x, y, m: WHITE if abs(y) < 0.8 and math.floor(x) % 5 == 0 else m)
    c.part()
    c.ellipse(-5, -34, 4.5, 4.5, WHITE)
    for a in range(0, 360, 60):
        c.ellipse(-5 + 3.5 * math.cos(math.radians(a)), -34 + 3.5 * math.sin(math.radians(a)), 2.4, 2.4, WHITE)


def hat_top(c, far=False):
    c.part()
    c.poly([(-12, -2), (12, -2), (13, -31), (-13, -31)], MAIN)
    c.recolor(lambda x, y, m: ACC if -9 < y < -4 else m)
    c.part()
    c.ellipse(0, -31, 13, 3.4, MAIN)
    c.part()
    c.ellipse(0, 0, 20, 4.2, MAIN)


def hat_jester(c, far=False):
    tips = [(-24, -22), (-2, -36), (20, -26)]
    for i, (tx, ty) in enumerate(tips):
        c.part()
        pts = bezier((-10 + i * 10, -4), ((-10 + i * 10 + tx) / 2, ty - 6), (tx, ty), 10)
        for k, (a, b) in enumerate(zip(pts, pts[1:])):
            r = 7.0 - 5.0 * k / len(pts)
            c.capsule(a[0], a[1], b[0], b[1], r, r - 0.5, MAIN if i % 2 == 0 else ACC)
        c.part()
        c.ellipse(tx, ty + 3, 3.4, 3.4, GOLD)
    c.part()
    c.ellipse(0, -1, 18, 5, ACC)
    c.recolor(lambda x, y, m: MAIN if math.floor(x / 4) % 2 == 0 else m)


def hat_bowler(c, far=False):
    c.part()
    c.ellipse(0, -11, 13.5, 12, MAIN)
    c.recolor(lambda x, y, m: ACC if -5.5 < y < -2.5 else m)
    c.part()
    c.ellipse(0, -1, 19, 3.8, MAIN)


def hat_propeller(c, far=False):
    c.part()
    c.ellipse(0, -6, 15.5, 11, MAIN)
    c.recolor(lambda x, y, m: ACC if math.floor(math.degrees(math.atan2(y + 6, x)) / 45) % 2 == 0 else m)
    c.recolor(lambda x, y, m: m if y < -0.5 else WHITE)
    c.part()
    c.capsule(0, -16, 0, -21, 1.4, 1.4, METAL)
    c.part()
    c.ellipse(-8, -22, 8, 2.4, ACC)
    c.ellipse(8, -22, 8, 2.4, MAIN)
    c.part()
    c.ellipse(0, -22, 2.4, 2.4, GOLD)


def hat_crown(c, far=False):
    c.part()
    pts = [(-15, 0), (15, 0), (15, -10)]
    for i, x in enumerate(range(15, -16, -6)):
        pts += [(x, -10), (x - 3, -23 if i % 2 == 0 else -19)]
    pts += [(-15, -10)]
    c.poly(pts, GOLD)
    for x in (-9, 0, 9):
        c.part(line=False)
        c.ellipse(x, -5, 2.6, 2.6, MAIN if x else ACC)


def hat_fez(c, far=False):
    c.part()
    c.poly([(-11, 0), (11, 0), (8, -19), (-8, -19)], MAIN)
    c.part()
    c.ellipse(0, -19, 8, 2.4, MAIN)
    c.part(line=False)
    c.stroke(bezier((0, -20), (-8, -22), (-11, -9)), 1.0, INK)
    c.part()
    c.ellipse(-11, -7, 2.4, 4, ACC)


# --------------------------------------------------------------- neckwear
# Drawn round the middle of the ruff (the neck), over it.

def neck_ruff(c, far=False):
    for i, x in enumerate(range(-22, 23, 6)):
        c.part()
        c.ellipse(x, 2 + abs(x) * 0.1, 6.6, 5.6, MAIN if i % 2 == 0 else WHITE)


def neck_bowtie(c, far=False):
    c.part()
    c.poly([(9, 3), (21, -6), (22, 12)], MAIN)
    c.poly([(9, 3), (-3, -6), (-4, 12)], MAIN)
    c.recolor(lambda x, y, m: WHITE if ((x % 6) - 3) ** 2 + ((y % 6) - 3) ** 2 < 2.2 else m)
    c.part()
    c.ellipse(9, 3, 3.6, 4.4, ACC)


def neck_scarf(c, far=False):
    c.part()
    c.ellipse(2, 2, 18, 5.5, MAIN)
    c.capsule(10, 4, 16, 24, 4.6, 3.6, MAIN)
    c.recolor(lambda x, y, m: ACC if math.floor((x + y) / 4) % 2 == 0 else m)
    c.part()
    c.ellipse(16, 26, 4, 2, ACC)


def neck_necktie(c, far=False):
    c.part()
    c.poly([(8, 1), (14, 1), (13, 6), (9, 6)], ACC)
    c.part()
    c.poly([(9, 6), (13, 6), (17, 26), (11, 31), (6, 26)], MAIN)
    c.recolor(lambda x, y, m: ACC if math.floor((x - y * 0.8) / 3) % 3 == 0 else m)


def neck_medal(c, far=False):
    c.part()
    c.poly([(2, 0), (6, 0), (11, 14), (8, 15)], MAIN)
    c.poly([(14, 0), (10, 0), (9, 15), (12, 14)], ACC)
    c.part()
    c.ellipse(10, 19, 5.5, 5.5, GOLD)
    c.part(line=False)
    c.ellipse(10, 19, 2, 2, WHITE)


# ------------------------------------------------------------------ shoes
# Drawn round the ground point under the shoe, as the clown's shoe is.

def shoe_clown(c, far=False):
    c.part(far=far)
    c.ellipse(-2, -7, 13, 6.5, MAIN)
    c.ellipse(9, -8, 10, 8, MAIN)
    c.recolor(lambda x, y, m: SOLE if y > -3 else m)
    if not far:
        c.part(line=False)
        c.ellipse(12, -12, 3, 2, WHITE)
        c.part()
        c.ellipse(-1, -13, 3, 2.2, WHITE)
        c.ellipse(3, -13, 3, 2.2, WHITE)


def shoe_floppy(c, far=False):
    c.part(far=far)
    c.ellipse(-3, -7, 12, 6.5, MAIN)
    c.ellipse(13, -7, 14, 7, MAIN)
    c.ellipse(25, -11, 4, 4, MAIN)                       # the toe curls up
    c.recolor(lambda x, y, m: SOLE if y > -2.5 else (ACC if math.floor(x / 6) % 2 == 0 and y < -8 else m))


def shoe_sneaker(c, far=False):
    c.part(far=far)
    c.poly([(-8, -4), (-8, -20), (4, -20), (6, -10), (16, -8), (17, -4)], MAIN)
    c.recolor(lambda x, y, m: WHITE if x > 9 else (ACC if -16 < y < -14 or (x < -2 and y < -12) else m))
    c.part(far=far)
    c.poly([(-9, -4), (18, -4), (18, 0), (-9, 0)], WHITE)
    c.recolor(lambda x, y, m: SOLE if y > -1.5 else m)


def shoe_skate(c, far=False):
    c.part(far=far)
    c.poly([(-7, -8), (-7, -22), (5, -22), (6, -14), (14, -12), (15, -8)], MAIN)
    c.recolor(lambda x, y, m: ACC if -18 < y < -16 else m)
    c.part(far=far)
    c.poly([(-8, -8), (16, -8), (16, -6), (-8, -6)], METAL)
    for x in (-4, 4, 12):
        c.part(far=far)
        c.ellipse(x, -3, 3, 3, ACC if not far else MAIN)


def shoe_boot(c, far=False):
    c.part(far=far)
    c.poly([(-7, -2), (-7, -27), (5, -27), (5, -10), (16, -8), (17, -2)], MAIN)
    c.recolor(lambda x, y, m: ACC if math.floor(y / 4) % 2 == 0 and y < -10 else m)
    c.part(far=far)
    c.poly([(-8, -2), (18, -2), (18, 0), (-8, 0)], SOLE)


def shoe_bunny(c, far=False):
    c.part(far=far)
    c.ellipse(3, -6, 14, 7, WHITE)
    c.recolor(lambda x, y, m: MAIN if y > -2 else m)
    for dx in (-1, 3):
        c.part(far=far)
        c.ellipse(10 + dx, -17, 2.4, 7, WHITE)
        c.recolor(lambda x, y, m: MAIN if abs(x - (10 + dx)) < 1 and -21 < y < -13 else m)
    if not far:
        c.part(line=False)
        c.ellipse(14, -9, 1.2, 1.2, INK)
        c.part(line=False)
        c.ellipse(17.5, -6, 1.6, 1.3, ACC)


# --------------------------------------------------------------- charms
# Drawn round the near hand: tied to the weapon's grip.

def charm_ribbon(c, far=False):
    c.part()
    c.poly([(0, 0), (-9, -6), (-9, 6)], MAIN)
    c.poly([(0, 0), (9, -6), (9, 6)], MAIN)
    c.capsule(0, 0, -4, 12, 1.6, 1.2, MAIN)
    c.capsule(0, 0, 4, 13, 1.6, 1.2, MAIN)
    c.part()
    c.ellipse(0, 0, 2.6, 2.6, ACC)


def charm_bell(c, far=False):
    c.part(line=False)
    c.stroke([(0, 0), (-2, 6)], 0.8, MAIN)
    c.part()
    c.ellipse(-2, 11, 5, 5.5, GOLD)
    c.part()
    c.ellipse(-2, 16, 1.6, 1.6, INK)
    c.part()
    c.ellipse(1, 2, 2.4, 2.4, ACC)


def charm_star(c, far=False):
    c.part(line=False)
    c.stroke([(0, 0), (2, 6)], 0.8, MAIN)
    c.part()
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = 7 if i % 2 == 0 else 3
        pts.append((2 + math.cos(a) * r, 13 + math.sin(a) * r))
    c.poly(pts, ACC)
    c.part(line=False)
    c.ellipse(2, 13, 1.6, 1.6, MAIN)


MODELS = {
    ("hat", "party"): hat_party, ("hat", "top"): hat_top, ("hat", "jester"): hat_jester, ("hat", "bowler"): hat_bowler,
    ("hat", "propeller"): hat_propeller, ("hat", "crown"): hat_crown, ("hat", "fez"): hat_fez,
    ("neck", "ruff"): neck_ruff, ("neck", "bowtie"): neck_bowtie, ("neck", "scarf"): neck_scarf,
    ("neck", "necktie"): neck_necktie, ("neck", "medal"): neck_medal,
    ("shoes", "clown"): shoe_clown, ("shoes", "floppy"): shoe_floppy, ("shoes", "sneaker"): shoe_sneaker,
    ("shoes", "skate"): shoe_skate, ("shoes", "boot"): shoe_boot, ("shoes", "bunny"): shoe_bunny,
    ("charm", "ribbon"): charm_ribbon, ("charm", "bell"): charm_bell, ("charm", "star"): charm_star,
}
SIZE = {"hat": 112, "neck": 80, "shoes": 72, "charm": 48}


def mats(colours):
    m = dict(hd.MATS)
    m[MAIN] = Material(colours[0])
    m[ACC] = Material(colours[1])
    return m


def render(job):
    slot, model, tag, colours = job
    S = SIZE[slot]
    c0 = S // 2
    frames = []
    sides = (False, True) if slot == "shoes" else (False,)
    for far in sides:
        base = HDCanvas(S, S, c0, c0)
        MODELS[(slot, model)](base, far)
        for step in range(8):
            frames.append(rotated(base, step * 45, c0, c0) if step else base)
    both = [shade(cv, mats(colours), normals=True) for cv in frames]
    name = f"{slot}_{model}_{tag}"
    path = os.path.join(OUT, name + ".png")
    write_png(path, S * len(both), S, to_rows([px for px, _ in both], S, S))
    write_png(path.replace(".png", "_n.png"), S * len(both), S, to_rows([n for _, n in both], S, S))
    clips = {f"r{i}": [i] for i in range(8)}
    if len(sides) == 2:
        clips.update({f"f{i}": [8 + i] for i in range(8)})
    with open(path.replace(".png", ".frames.json"), "w", newline=chr(10)) as f:
        json.dump({"frame_width": S, "frame_height": S, "baseline": S // 2, "count": len(both), "clips": clips}, f, indent=1)
    return name


def jobs():
    out = []
    for (slot, model) in MODELS:
        for i, colours in enumerate(COLOURWAYS):
            out.append((slot, model, str(i), colours))
    # what each clown wears with nothing equipped: their party hat and clown shoes, in their colours
    for clown, colours in CLOWN_COLOURS.items():
        out.append(("hat", "party", clown, colours))
        out.append(("shoes", "clown", clown, (colours[0], colours[1])))
    return out


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    js = jobs()
    with Pool(os.cpu_count() or 4) as pool:
        names = pool.map(render, js)
    print(f"wrote {len(names)} gear sheets to {OUT}")
