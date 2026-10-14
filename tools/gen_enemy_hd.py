"""Bellyflop Bertie and his floating fists at double resolution.

Same poses and clip layout as tools/gen_enemy.py (kept as the classic art),
doubled, and rendered through pixelart_hd.py like the squad. He has no arms:
his two gloved fists are a separate sheet, floated by BossClown.cs.

Run: bash tools/refresh_sprites.sh
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier
from pixelpng import write_png

# Headroom above the original's frame, which cut the hat's pompom off.
TOP = 16
W, H, OX = 176, 128 + TOP, 24

BELLY, DOTS, RED, YELLOW, WHITE, SKIN, HAIR, NOSE = 1, 2, 3, 4, 5, 6, 7, 8
INK, TOOTH, SOLE, LIP = 9, 10, 11, 12

MATS = {
    BELLY: Material((100, 50, 124)),
    DOTS: Material((170, 84, 176)),
    RED: Material((218, 56, 68)),
    YELLOW: Material((246, 200, 84)),
    WHITE: Material((236, 236, 246), soft=True),
    SKIN: Material((250, 222, 200), soft=True, cool=0.97),
    HAIR: Material((112, 196, 104)),
    NOSE: Material((232, 52, 60), gloss=True),
    INK: Material((34, 22, 40), flat=True),
    TOOTH: Material((250, 248, 236), flat=True),
    SOLE: Material((74, 58, 82)),
    LIP: Material((150, 30, 52)),
}

NEUTRAL = dict(body_dy=0, lean=0, head_dy=0, belly=0, near_leg=0, far_leg=0, arm_dy=0, grin=0)


def pose(**over):
    p = dict(NEUTRAL)
    p.update(over)
    # Doubled from the original's units; arm_dy is unused now he has no arms.
    return {k: v * 2 for k, v in p.items()}


def shoe(c, x, base, far):
    c.part(far=far)
    c.ellipse(x - 3, base - 8, 17, 7.5, RED)
    c.ellipse(x + 10, base - 9, 12, 9, RED)
    c.recolor(lambda xx, yy, m: SOLE if yy > base - 3 else m)
    if not far:
        c.part(line=False)
        c.ellipse(x + 13, base - 14, 4, 2.4, WHITE)     # shine
        c.part()
        c.ellipse(x + 2, base - 15, 9, 3, YELLOW)       # the yellow spat


def draw(p):
    c = HDCanvas(W, H, OX, TOP)
    by, ln, hd, bulge = p["body_dy"], p["lean"], p["head_dy"], p["belly"]
    nl, fl, g = p["near_leg"], p["far_leg"], p["grin"]

    # far leg first, a tone darker
    c.part(far=True)
    c.capsule(52 + fl, 102 + by, 48 + fl, 116, 9, 7, BELLY)
    shoe(c, 46 + fl, 128, True)

    # near leg
    c.part()
    c.capsule(68 + nl, 102 + by, 72 + nl, 116, 9.5, 7.5, BELLY)
    shoe(c, 74 + nl, 128, False)

    # the belly: the whole silhouette hangs off this, polka-dotted
    bx, bcy = 60 + ln, 84 + by
    rx, ry = 38 + bulge, 30 + bulge
    c.part()
    c.ellipse(bx, bcy, rx, ry, BELLY)
    dots = ((40, 72, 5), (32, 92, 5), (52, 100, 5), (60, 76, 3.5), (72, 94, 5), (46, 86, 3.5), (82, 78, 4))

    def polka(x, y, m):
        for dx, dy, r in dots:
            if (x - dx - ln) ** 2 + (y - dy - by) ** 2 <= r * r:
                return DOTS
        return m
    c.recolor(polka)

    # straining suspenders, each clipped to the belly's outline
    for x0, x1 in ((60, 52), (80, 84)):
        c.part()
        pts = bezier((x0 + ln, 60 + by), ((x0 + x1) / 2 + ln + 6, 84 + by), (x1 + ln + bulge, 110 + by))
        c.stroke(pts, 2.4, RED)
        c.recolor(lambda x, y, m: m if ((x - bx) / rx) ** 2 + ((y - bcy) / ry) ** 2 <= 1.0 else 0)
        c.part()
        c.ellipse(x0 + ln, 63 + by, 3, 2.4, YELLOW)        # the button it hangs from

    # pompom buttons down the front
    for y in (68, 84, 100):
        c.part()
        c.ellipse(88 + ln, y + by, 5.5, 5.5, YELLOW)
        for a in range(0, 360, 72):
            c.ellipse(88 + ln + 3.6 * math.cos(math.radians(a)), y + by + 3.6 * math.sin(math.radians(a)), 2.4, 2.4, YELLOW)

    # ruff collar, wide enough to sit on the belly
    for i, cx in enumerate((44, 54, 64, 74, 84, 92)):
        c.part()
        c.ellipse(cx + ln, 58 + by + abs(cx - 68) * 0.06, 7, 6, WHITE if i % 2 == 0 else YELLOW)

    hy = 36 + by + hd
    # wild green hair behind
    for x, y, rx_, ry_ in ((46, hy + 6, 14, 12), (50, hy - 12, 10, 8), (42, hy - 4, 9, 9)):
        c.part()
        c.ellipse(x + ln, y, rx_, ry_, HAIR)

    # head, large enough to carry the grin
    c.part()
    c.ellipse(68 + ln, hy, 24, 22, SKIN)
    c.part()
    c.ellipse(52 + ln, hy + 2, 4.5, 6.5, SKIN)                   # ear
    c.recolor(lambda x, y, m: LIP if ((x - 52.6 - ln) / 2) ** 2 + ((y - hy - 2) / 3.6) ** 2 <= 1 else m)

    # thin brow over a small eye: menace without reading as sunglasses
    c.part(line=False)
    c.stroke(bezier((72 + ln, hy - 11), (80 + ln, hy - 15), (89 + ln, hy - 11)), 1.6, INK)
    c.part(line=False)
    c.ellipse(82 + ln, hy - 4, 3.6, 5.4, INK)
    c.recolor(lambda x, y, m: TOOTH if abs(x - 83 - ln) < 0.8 and abs(y - hy + 6.5) < 0.8 else m)

    # the grin: a wide toothy band with a hard outline, the character beat
    gx0, gx1 = 58 + ln, 90 + ln
    gy0, gy1 = hy + 8, hy + 16 + g
    c.part()
    c.poly([(gx0 - 2, gy0 - 2), (gx1 + 2, gy0 - 3), (gx1 + 3, gy1 + 1), (gx0 + 3, gy1 + 3)], LIP)
    c.part(line=False)
    c.poly([(gx0, gy0), (gx1, gy0 - 1), (gx1 + 1, gy1 - 1), (gx0 + 3, gy1 + 1)], TOOTH)
    c.recolor(lambda x, y, m: INK if (int(x - gx0) % 6 == 5) or abs(y - (gy0 + gy1) / 2) < 0.5 else m)

    # nose, set high and forward so it never crowds the mouth
    c.part()
    c.ellipse(92 + ln, hy, 8, 8, NOSE)

    # tiny hat, comically small on a huge head
    c.part()
    c.poly([(48 + ln, hy - 19), (76 + ln, hy - 19), (62 + ln, hy - 38)], RED)
    c.recolor(lambda x, y, m: YELLOW if math.floor((x - ln + (y - by) * 0.7) / 4.0) % 3 == 0 else m)
    c.part()
    c.ellipse(62 + ln, hy - 19, 15, 3.6, YELLOW)
    c.part()
    c.ellipse(61 + ln, hy - 39, 4, 4, WHITE)
    return c


FRAMES = [
    ("idle", pose()),
    ("idle", pose(belly=1, body_dy=1, head_dy=1)),
    ("walk", pose(near_leg=5, far_leg=-4, body_dy=-1, lean=1)),
    ("walk", pose(near_leg=2, far_leg=-1, body_dy=1, belly=1)),
    ("walk", pose(near_leg=-4, far_leg=5, body_dy=-1, lean=-1)),
    ("walk", pose(near_leg=-1, far_leg=2, body_dy=1, belly=1)),
    ("attack", pose(lean=-4, body_dy=1, head_dy=1, grin=1)),
    ("attack", pose(lean=3, body_dy=-1, belly=2, grin=2, near_leg=4)),
    ("attack", pose(lean=6, body_dy=1, belly=1, grin=1, near_leg=7)),
    ("hit", pose(lean=-5, head_dy=-2, body_dy=2, grin=-1)),
    ("air", pose(body_dy=-2, belly=1, near_leg=-3, far_leg=3, grin=2)),
    ("air", pose(lean=4, body_dy=1, belly=2, grin=2)),
]


FW = 44


def fist():
    """A closed fist in a white cartoon glove, facing right."""
    c = HDCanvas(FW, FW)
    c.part()
    c.ellipse(10, 22, 6, 9, WHITE)                    # rolled cuff
    c.recolor(lambda x, y, m: YELLOW if abs(x - 9) < 1.2 else m)
    c.part()
    c.ellipse(24, 22, 13, 13, WHITE)                  # the fist
    for y in (13, 21, 29):
        c.part()
        c.ellipse(34, y, 4.5, 4.2, WHITE)             # knuckles, the punching face
    c.part()
    c.ellipse(22, 30, 8, 4.5, WHITE)                  # thumb folded under
    return c


def main():
    shaded = [shade(draw(p), MATS) for _, p in FRAMES]
    out = os.path.join("assets", "sprites", "boss_hd.png")
    write_png(out, W * len(shaded), H, to_rows(shaded, W, H))
    clips = {}
    for i, (name, _) in enumerate(FRAMES):
        clips.setdefault(name, []).append(i)
    with open(os.path.join("assets", "sprites", "boss_hd.frames.json"), "w") as f:
        json.dump({"frame_width": W, "frame_height": H, "baseline": H,
                   "count": len(shaded), "clips": clips}, f, indent=2)
    print("wrote {}  {}x{}  {} frames".format(out, W * len(shaded), H, len(shaded)))

    fout = os.path.join("assets", "sprites", "boss_fist_hd.png")
    write_png(fout, FW, FW, to_rows([shade(fist(), MATS)], FW, FW))
    print("wrote {}  {}x{}  1 frame".format(fout, FW, FW))


if __name__ == "__main__":
    main()
