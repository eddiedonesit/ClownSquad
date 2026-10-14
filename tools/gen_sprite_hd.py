"""PIP at double resolution: the sample for the SNES-quality art pass.

The character is authored in a 96 x 128 space (the original was 48 x 64) and
rendered through pixelart_hd.py: five-tone hue-shifted ramps, form shading,
cast shadows between parts, and coloured outlines. Only idle and walk exist so
far; the game falls back to idle for everything else while this is a sample.

Same canvas padding as the original, doubled, so the sheet is a drop-in
replacement once every clip is drawn: 208 x 288 frames, feet on row 216.

Run: python tools/gen_sprite_hd.py  (then Godot --import, as refresh_sprites.sh does)
Play it: add  -- hd  to the game's command line.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier
from pixelpng import write_png

BODY_W, BODY_H = 96, 128
TOP_PAD, BOTTOM_PAD = 88, 72
W, H = 208, TOP_PAD + BODY_H + BOTTOM_PAD
DRAW_X, DRAW_Y = 56, TOP_PAD
BASELINE = TOP_PAD + BODY_H

# ---------------------------------------------------------------- palette

SUIT, DIAMOND, RED, YELLOW, WHITE, SKIN, HAIR, NOSE = 1, 2, 3, 4, 5, 6, 7, 8
INK, EYE, LIP, BLUSH, MAKEUP, SOLE, WOOD, METAL, BELT, GOLD = 9, 10, 11, 12, 13, 14, 15, 16, 17, 18

MATS = {
    SUIT: Material((70, 108, 210)),
    DIAMOND: Material((156, 64, 160)),
    RED: Material((218, 56, 68)),
    YELLOW: Material((246, 200, 84)),
    WHITE: Material((236, 236, 246), soft=True),
    SKIN: Material((250, 218, 192), soft=True, cool=0.97),
    HAIR: Material((238, 132, 58)),
    NOSE: Material((232, 52, 60), gloss=True),
    INK: Material((34, 22, 40), flat=True),
    EYE: Material((252, 252, 255), flat=True),
    LIP: Material((196, 36, 58)),
    BLUSH: Material((244, 150, 160)),
    MAKEUP: Material((76, 150, 236)),
    SOLE: Material((74, 58, 82)),
    WOOD: Material((176, 102, 56)),
    METAL: Material((196, 202, 216), gloss=True),
    BELT: Material((84, 44, 104)),
    GOLD: Material((252, 204, 72), gloss=True),
}

# ------------------------------------------------------------------- parts


def shoe(c, x, base, far):
    """A big clown shoe facing right: heel, a round toe bulb, a dark sole, laces."""
    c.part(far=far)
    c.ellipse(x - 2, base - 7, 13, 6.5, RED)
    c.ellipse(x + 9, base - 8, 10, 8, RED)
    c.recolor(lambda xx, yy, m: SOLE if yy > base - 3 else m)
    if not far:
        c.part(line=False)
        c.ellipse(x + 12, base - 12, 3, 2, WHITE)       # toe shine, painted on
        c.part()
        c.ellipse(x - 1, base - 13, 3, 2.2, WHITE)      # lace bow
        c.ellipse(x + 3, base - 13, 3, 2.2, WHITE)


def frill(c, x, y, rx, far):
    """A scalloped white cuff."""
    c.part(far=far)
    c.ellipse(x, y, rx, 3.2, WHITE)
    for i in range(-2, 3):
        c.ellipse(x + i * rx * 0.42, y + 2.4, 2.4, 2.2, WHITE)


def fist(c, x, y, far):
    """A white-gloved hand gripping, facing right."""
    c.part(far=far)
    c.ellipse(x, y, 6, 5.6, WHITE)
    c.part(far=far)
    c.ellipse(x + 3, y - 3.5, 3, 2.5, WHITE)            # thumb over the grip


def mallet_head(c, hx, hy, angle, length, head_len=40, head_rad=14):
    """The squeaky mallet's barrel: red, with a yellow striking face and steel
    bands, its axis across the handle. Drawn before the body when carried, so
    it rests behind the head instead of over the face."""
    r = math.radians(angle)
    mx, my = hx + math.cos(r) * length, hy - math.sin(r) * length
    ax, ay = math.cos(r + math.pi / 2), -math.sin(r + math.pi / 2)
    half = head_len / 2
    e0 = (mx - ax * half, my - ay * half)
    e1 = (mx + ax * half, my + ay * half)
    c.part()
    c.capsule(e0[0], e0[1], e1[0], e1[1], head_rad, head_rad, RED)

    def band(x, y, m):
        t = ((x - e0[0]) * ax + (y - e0[1]) * ay) / head_len
        return METAL if 0.16 < t < 0.24 or 0.76 < t < 0.84 else m
    c.recolor(band)
    c.part()
    c.ellipse(e1[0], e1[1], head_rad - 3, head_rad - 3, YELLOW)    # the striking face


def mallet_handle(c, hx, hy, angle, length):
    r = math.radians(angle)
    c.part()
    c.capsule(hx, hy, hx + math.cos(r) * length, hy - math.sin(r) * length, 2.6, 2.6, WOOD)


# -------------------------------------------------------------------- pose

CARRY_ANGLE, CARRY_LENGTH = 150, 54     # resting back over the shoulder

POSE = dict(by=0, ln=0, hd=0, nl=0, fl=0, nfoot=0, ffoot=0, na=0, fa=0, lift=0)


def pose(**over):
    p = dict(POSE)
    p.update(over)
    return p


def draw(p):
    """Profile facing right, in the 96 x 128 authoring space."""
    c = HDCanvas(W, H, DRAW_X, DRAW_Y)
    by, ln, hd = p["by"], p["ln"], p["hd"]
    nl, fl = p["nl"], p["fl"]
    hipy = 93 + by
    hy = 46 + by + hd                       # head centre

    # the carried mallet's head rests behind everything, over the shoulder
    ex, ey = 52 + p["na"], 95 - p["lift"]
    mallet_head(c, ex, ey + 4, CARRY_ANGLE, CARRY_LENGTH)

    # hair, behind everything above the shoulders
    for x, y, r in ((36, hy - 10, 6), (31, hy - 3, 7.5), (27, hy + 5, 7), (30, hy + 13, 6.5)):
        c.part()                                  # each curl its own part, so they separate
        c.ellipse(x + ln, y, r, r * 0.9, HAIR)

    # far leg, shoe and arm, a tone darker
    c.part(far=True)
    c.capsule(42 + ln, hipy, 44 + fl, 112 - p["ffoot"], 8.5, 6.5, SUIT)
    c.recolor(lambda x, y, m: RED if 104 - p["ffoot"] < y < 108 - p["ffoot"] else m)
    frill(c, 44 + fl, 113 - p["ffoot"], 7, True)
    shoe(c, 46 + fl, 128 - p["ffoot"], True)
    c.part(far=True)
    c.capsule(41 + ln, 76 + by, 39 + p["fa"], 95 - p["lift"], 5.6, 4.8, SUIT)
    fist(c, 39 + p["fa"], 99 - p["lift"], True)

    # torso: a pear of harlequin diamonds, pompom buttons, a belt and buckle
    c.part()
    c.ellipse(46 + ln, 85 + by, 15, 13.5, SUIT)
    c.ellipse(47 + ln, 76 + by, 12.5, 9, SUIT)

    def harlequin(x, y, m):
        u, v = (x - ln + y - by) / 7.0, (x - ln - y + by) / 7.0
        if 93 + by <= y <= 97 + by:
            return GOLD if 52 + ln <= x <= 57 + ln else BELT
        return DIAMOND if (math.floor(u) + math.floor(v)) % 2 == 0 else m
    c.recolor(harlequin)
    for bx, byy in ((58, 78), (59.5, 87)):
        c.part()
        c.ellipse(bx + ln, byy + by, 3.4, 3.4, WHITE)

    # near leg: baggy trousers with a red band, a frill, the big shoe
    c.part()
    c.capsule(50 + ln, hipy - 1, 52 + nl, 112 - p["nfoot"], 9, 6.8, SUIT)
    c.recolor(lambda x, y, m: RED if 103 - p["nfoot"] < y < 108 - p["nfoot"] else m)
    frill(c, 52 + nl, 113 - p["nfoot"], 8, False)
    shoe(c, 56 + nl, 128 - p["nfoot"], False)

    # the ruff: alternating white and yellow scallops, each its own pleat
    for i, x in enumerate(range(30, 67, 6)):
        c.part()
        c.ellipse(x + ln, 68 + by + abs(x - 48) * 0.1, 5.6, 4.6, WHITE if i % 2 == 0 else YELLOW)

    # head: one rounded shape, white greasepaint round the mouth
    c.part()
    c.ellipse(50 + ln, hy + 1, 18.5, 18.5, SKIN)
    c.recolor(lambda x, y, m: WHITE if ((x - 60 - ln) / 9.5) ** 2 + ((y - hy - 11) / 6.5) ** 2 <= 1 else m)
    c.part()
    c.ellipse(37 + ln, hy + 4, 3.6, 5, SKIN)                     # ear, half under the hair
    c.recolor(lambda x, y, m: BLUSH if ((x - 37.6 - ln) / 1.6) ** 2 + ((y - hy - 4) / 2.8) ** 2 <= 1 else m)
    c.part()
    c.ellipse(33 + ln, hy + 1, 4.5, 7, HAIR)                     # a curl over its back edge

    # face
    c.part(line=False)
    c.stroke(bezier((53 + ln, hy - 11), (59 + ln, hy - 16), (65 + ln, hy - 11)), 1.1, INK)   # brow
    c.part(line=False)
    c.ellipse(60 + ln, hy - 4, 3.3, 5, EYE)
    c.part(line=False)
    c.ellipse(61.4 + ln, hy - 3, 1.9, 3.2, INK)
    c.recolor(lambda x, y, m: EYE if abs(x - 61.5 - ln) < 0.6 and abs(y - hy + 5) < 0.6 else m)
    c.part(line=False)
    c.ellipse(50.5 + ln, hy + 7, 3.2, 2.2, BLUSH)
    c.part(line=False)
    c.stroke(bezier((54 + ln, hy + 11), (61 + ln, hy + 17), (68 + ln, hy + 9)), 1.7, LIP)     # the grin
    c.part()
    c.ellipse(69 + ln, hy + 3, 6.6, 6.2, NOSE)

    # hat, low on the brow, with diagonal stripes, a dotted brim and a pompom
    hx = 46 + ln
    c.part()
    c.poly([(hx - 17, hy - 11), (hx + 17, hy - 11), (hx + 8, hy - 28), (hx - 4, hy - 43), (hx - 12, hy - 26)], RED)
    c.recolor(lambda x, y, m: YELLOW if math.floor((x - ln + (y - by) * 0.75) / 5.0) % 3 == 0 else m)
    c.part()
    c.ellipse(hx, hy - 11, 19.5, 4.2, YELLOW)
    c.recolor(lambda x, y, m: WHITE if abs(y - hy + 11) < 0.8 and math.floor(x) % 5 == 0 else m)
    c.part()
    px_, py_ = hx - 5, hy - 45
    c.ellipse(px_, py_, 4.5, 4.5, WHITE)
    for a in range(0, 360, 60):
        c.ellipse(px_ + 3.5 * math.cos(math.radians(a)), py_ + 3.5 * math.sin(math.radians(a)), 2.4, 2.4, WHITE)

    # near arm in front: sleeve with a red band, frilled cuff, gloved fist, mallet
    sx, sy = 50 + ln, 76 + by
    c.part()
    c.capsule(sx, sy, ex, ey, 6.2, 5.2, SUIT)

    def sleeve(x, y, m):
        dx, dy = ex - sx, ey - sy
        t = ((x - sx) * dx + (y - sy) * dy) / (dx * dx + dy * dy)
        return RED if 0.42 < t < 0.56 else m
    c.recolor(sleeve)
    frill(c, sx + (ex - sx) * 0.92, sy + (ey - sy) * 0.92, 5.5, False)
    mallet_handle(c, ex, ey + 4, CARRY_ANGLE, CARRY_LENGTH)
    fist(c, ex, ey + 4, False)
    return c


FRAMES = [
    ("idle", pose()),
    ("idle", pose(by=1, hd=1, lift=-1)),
    ("walk", pose(nl=12, fl=-10, na=-4, fa=6, by=-2, ln=1)),
    ("walk", pose(nl=3, fl=-2, ffoot=4, na=-1, fa=2)),
    ("walk", pose(nl=-10, fl=12, na=5, fa=-5, by=-2, ln=1)),
    ("walk", pose(nl=-2, fl=3, nfoot=4, na=1, fa=-2)),
]


def main():
    shaded = [shade(draw(p), MATS) for _, p in FRAMES]
    out = os.path.join("assets", "sprites", "pip_hd.png")
    write_png(out, W * len(shaded), H, to_rows(shaded, W, H))
    clips = {}
    for i, (name, _) in enumerate(FRAMES):
        clips.setdefault(name, []).append(i)
    with open(os.path.join("assets", "sprites", "pip_hd.frames.json"), "w") as f:
        json.dump({"frame_width": W, "frame_height": H, "baseline": BASELINE,
                   "count": len(shaded), "clips": clips}, f, indent=2)
    print("wrote {}  {}x{}  {} frames".format(out, W * len(shaded), H, len(shaded)))


if __name__ == "__main__":
    main()
