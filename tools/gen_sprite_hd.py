"""The squad, the grunts and the thrown mallet at double resolution.

The character is authored in a 96 x 128 space (the original was 48 x 64) and
rendered through pixelart_hd.py: five-tone hue-shifted ramps, form shading,
cast shadows between parts, and coloured outlines. Every clip of the original
sheet (tools/gen_sprite.py, kept as the classic art) is here with the same
poses doubled, so the game code drives either sheet unchanged.

Same canvas padding as the original, doubled: 208 x 288 frames, feet on row 216.

Run: bash tools/refresh_sprites.sh.  The classic art: add  -- classic  to the game.
"""
import json
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier, rotated, laid_down, crop, touches_border
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


def mallet_head(c, hx, hy, angle, length, head_len=44, head_rad=15):
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

CARRY = (143, 29)          # resting back over the shoulder (original units)
DODGE_CARRY = (112, 24)    # steeper and closer, so the deep slide stays on canvas

POSE = dict(by=0, ln=0, hd=0, nl=0, fl=0, nfoot=0, ffoot=0, na=0, fa=0, lift=0, mallet=None)


def pose(**over):
    p = dict(POSE)
    p.update(over)
    return p


def old(mallet=None, body_dy=0, lean=0, head_dy=0, near_leg=0, far_leg=0,
        near_arm=0, far_arm=0, arm_lift=0, nfoot=0, ffoot=0):
    """A pose written in the original sheet's units, doubled. Every clip keeps
    the timing and staging that was already tuned against the game code; only
    the drawing changes."""
    m = None if mallet is None else (mallet[0], mallet[1] * 2)
    return pose(by=body_dy * 2, ln=lean * 2, hd=head_dy * 2, nl=near_leg * 2, fl=far_leg * 2,
                na=near_arm * 2, fa=far_arm * 2, lift=arm_lift * 2, nfoot=nfoot, ffoot=ffoot, mallet=m)


def draw(p):
    """Profile facing right, in the 96 x 128 authoring space."""
    c = HDCanvas(W, H, DRAW_X, DRAW_Y)
    by, ln, hd = p["by"], p["ln"], p["hd"]
    nl, fl = p["nl"], p["fl"]
    hipy = 93 + by
    hy = 46 + by + hd                       # head centre
    # The grip sits where the original's did (doubled), so the mallet head lands
    # where the game's impact effects expect it.
    ex, ey = 52 + p["na"], 102 - p["lift"]
    gx, gy = ex, ey + 4
    m = p["mallet"]
    behind = m is not None and m[0] >= 100   # swung back over the shoulder

    if behind:
        mallet_head(c, gx, gy, m[0], m[1])

    # hair, behind everything above the shoulders
    for x, y, r in ((36, hy - 10, 6), (31, hy - 3, 7.5), (27, hy + 5, 7), (30, hy + 13, 6.5)):
        c.part()                                  # each curl its own part, so they separate
        c.ellipse(x + ln, y, r, r * 0.9, HAIR)

    # far leg, shoe and arm, a tone darker
    c.part(far=True)
    c.capsule(42 + ln, hipy, 44 + fl, 112 - p["ffoot"], 8.5, 6.5, SUIT)
    c.recolor(lambda x, y, mm: RED if 104 - p["ffoot"] < y < 108 - p["ffoot"] else mm)
    frill(c, 44 + fl, 113 - p["ffoot"], 7, True)
    shoe(c, 46 + fl, 128 - p["ffoot"], True)
    c.part(far=True)
    c.capsule(41 + ln, 76 + by, 39 + p["fa"], 102 - p["lift"], 5.6, 4.8, SUIT)
    fist(c, 39 + p["fa"], 106 - p["lift"], True)

    # torso: a pear of harlequin diamonds, pompom buttons, a belt and buckle
    c.part()
    c.ellipse(46 + ln, 85 + by, 15, 13.5, SUIT)
    c.ellipse(47 + ln, 76 + by, 12.5, 9, SUIT)

    def harlequin(x, y, mm):
        u, v = (x - ln + y - by) / 7.0, (x - ln - y + by) / 7.0
        if 93 + by <= y <= 97 + by:
            return GOLD if 52 + ln <= x <= 57 + ln else BELT
        return DIAMOND if (math.floor(u) + math.floor(v)) % 2 == 0 else mm
    c.recolor(harlequin)
    for bx, byy in ((58, 78), (59.5, 87)):
        c.part()
        c.ellipse(bx + ln, byy + by, 3.4, 3.4, WHITE)

    # near leg: baggy trousers with a red band, a frill, the big shoe
    c.part()
    c.capsule(50 + ln, hipy - 1, 52 + nl, 112 - p["nfoot"], 9, 6.8, SUIT)
    c.recolor(lambda x, y, mm: RED if 103 - p["nfoot"] < y < 108 - p["nfoot"] else mm)
    frill(c, 52 + nl, 113 - p["nfoot"], 8, False)
    shoe(c, 56 + nl, 128 - p["nfoot"], False)

    # the ruff: alternating white and yellow scallops, each its own pleat
    for i, x in enumerate(range(30, 67, 6)):
        c.part()
        c.ellipse(x + ln, 68 + by + abs(x - 48) * 0.1, 5.6, 4.6, WHITE if i % 2 == 0 else YELLOW)

    # head: one rounded shape, white greasepaint round the mouth
    c.part()
    c.ellipse(50 + ln, hy + 1, 18.5, 18.5, SKIN)
    c.recolor(lambda x, y, mm: WHITE if ((x - 60 - ln) / 9.5) ** 2 + ((y - hy - 11) / 6.5) ** 2 <= 1 else mm)
    c.part()
    c.ellipse(37 + ln, hy + 4, 3.6, 5, SKIN)                     # ear, half under the hair
    c.recolor(lambda x, y, mm: BLUSH if ((x - 37.6 - ln) / 1.6) ** 2 + ((y - hy - 4) / 2.8) ** 2 <= 1 else mm)
    c.part()
    c.ellipse(33 + ln, hy + 1, 4.5, 7, HAIR)                     # a curl over its back edge

    # face
    c.part(line=False)
    c.stroke(bezier((53 + ln, hy - 11), (59 + ln, hy - 16), (65 + ln, hy - 11)), 1.1, INK)   # brow
    c.part(line=False)
    c.ellipse(60 + ln, hy - 4, 3.3, 5, EYE)
    c.part(line=False)
    c.ellipse(61.4 + ln, hy - 3, 1.9, 3.2, INK)
    c.recolor(lambda x, y, mm: EYE if abs(x - 61.5 - ln) < 0.6 and abs(y - hy + 5) < 0.6 else mm)
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
    c.recolor(lambda x, y, mm: YELLOW if math.floor((x - ln + (y - by) * 0.75) / 5.0) % 3 == 0 else mm)
    c.part()
    c.ellipse(hx, hy - 11, 19.5, 4.2, YELLOW)
    c.recolor(lambda x, y, mm: WHITE if abs(y - hy + 11) < 0.8 and math.floor(x) % 5 == 0 else mm)
    c.part()
    px_, py_ = hx - 5, hy - 45
    c.ellipse(px_, py_, 4.5, 4.5, WHITE)
    for a in range(0, 360, 60):
        c.ellipse(px_ + 3.5 * math.cos(math.radians(a)), py_ + 3.5 * math.sin(math.radians(a)), 2.4, 2.4, WHITE)

    # near arm in front: sleeve with a red band, frilled cuff, gloved fist, mallet
    sx, sy = 50 + ln, 76 + by
    c.part()
    c.capsule(sx, sy, ex, ey, 6.2, 5.2, SUIT)

    def sleeve(x, y, mm):
        dx, dy = ex - sx, ey - sy
        t = ((x - sx) * dx + (y - sy) * dy) / max(1e-6, dx * dx + dy * dy)
        return RED if 0.42 < t < 0.56 else mm
    c.recolor(sleeve)
    frill(c, sx + (ex - sx) * 0.92, sy + (ey - sy) * 0.92, 5.5, False)
    if m is not None:
        mallet_handle(c, gx, gy, m[0], m[1])
    fist(c, gx, gy, False)
    if m is not None and not behind:
        mallet_head(c, gx, gy, m[0], m[1])
    return c


# ------------------------------------------------------------------ clips
# Mirrors tools/gen_sprite.py clip for clip, so either sheet drives the same code.

def idle_cycle(n=4):
    """A slow breath: settle down and back up, in n in-betweens."""
    out = []
    for i in range(n):
        b = 0.5 - 0.5 * math.cos(2 * math.pi * i / n)
        out.append(("idle", dict(body_dy=b, head_dy=b, arm_lift=-b)))
    return out


def walk_cycle(n=8):
    """A full stride as a continuous cycle, so it can be cut into as many
    in-betweens as smoothness wants. Both feet lift on their forward swing."""
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        stride = math.cos(t)
        out.append(("walk", dict(near_leg=6 * stride, far_leg=-6 * stride,
                                 near_arm=-2 * stride, far_arm=3 * stride,
                                 body_dy=-abs(stride), lean=abs(stride),
                                 nfoot=5 * max(0.0, -math.sin(t)), ffoot=5 * max(0.0, math.sin(t)))))
    return out


# Hand-keyed brawlers live on in-betweens; these two loops play all the time.
FPS = {"idle": 6, "walk": 22, "empty_idle": 6, "empty_walk": 22}

BASE = idle_cycle() + walk_cycle() + [
    ("hit", dict(lean=-4, head_dy=-2, body_dy=2, near_arm=-7, far_arm=-5, arm_lift=-2)),
    ("dodge", dict(body_dy=8, lean=6, head_dy=3, near_leg=12, far_leg=-6,
                   near_arm=-10, far_arm=-7, arm_lift=-6)),
    ("dodge", dict(body_dy=15, lean=10, head_dy=6, near_leg=20, far_leg=-10,
                   near_arm=-15, far_arm=-11, arm_lift=-11)),
    ("dodge", dict(body_dy=8, lean=5, head_dy=3, near_leg=11, far_leg=-5,
                   near_arm=-9, far_arm=-6, arm_lift=-6)),
    ("jump", dict(body_dy=-2, lean=2, near_leg=5, far_leg=-3, near_arm=-6, far_arm=-4, arm_lift=6)),
    ("jump", dict(lean=-1, near_leg=-4, far_leg=6, near_arm=4, far_arm=-3, arm_lift=2)),
]

HIT_POSE = dict(lean=-4, head_dy=-2, body_dy=2, near_arm=-7, far_arm=-5, arm_lift=-2)


def player_frames():
    frames = []
    add = lambda name, cv: frames.append((name, cv))
    for name, kw in BASE:
        add(name, draw(old(mallet=DODGE_CARRY if name == "dodge" else CARRY, **kw)))

    # attack: mallet arcs from behind the head, out to 45 degrees, down to ground
    add("attack", draw(old(lean=-3, body_dy=1, near_arm=-6, far_arm=-4, arm_lift=5, mallet=(165, 26))))
    add("attack", draw(old(lean=0, body_dy=-1, near_arm=-2, far_arm=-1, arm_lift=9, mallet=(120, 30))))
    add("attack", draw(old(lean=3, body_dy=-1, near_arm=6, far_arm=3, arm_lift=5, mallet=(45, 30))))
    add("attack", draw(old(lean=4, body_dy=1, near_arm=9, far_arm=5, arm_lift=-2, mallet=(0, 26))))

    # charge: coiled low with the mallet drawn right back, two frames to shudder
    add("charge", draw(old(body_dy=5, lean=-4, head_dy=1, near_leg=5, far_leg=-5,
                           near_arm=-9, far_arm=-6, arm_lift=7, mallet=(168, 28))))
    add("charge", draw(old(body_dy=6, lean=-5, head_dy=2, near_leg=6, far_leg=-5,
                           near_arm=-10, far_arm=-7, arm_lift=8, mallet=(160, 28))))

    # spin: mallet straight out; the engine mirrors these rapidly into a whirl
    add("spin", draw(old(body_dy=2, lean=2, near_leg=4, far_leg=-4,
                         near_arm=10, far_arm=-8, arm_lift=10, mallet=(6, 28))))
    add("spin", draw(old(body_dy=3, lean=3, near_leg=-3, far_leg=4,
                         near_arm=10, far_arm=-8, arm_lift=9, mallet=(-6, 28))))

    # throw: wind up, then released with the arm thrown forward and the hand empty
    add("throw", draw(old(lean=-5, body_dy=2, near_leg=-4, far_leg=5,
                          near_arm=-10, far_arm=-6, arm_lift=11, mallet=(150, 26))))
    add("throw", draw(old(lean=6, body_dy=1, near_leg=8, far_leg=-6,
                          near_arm=13, far_arm=7, arm_lift=9)))

    # flip: one tucked pose, rotated through a full turn, re-lit at every step
    tuck = draw(old(body_dy=3, lean=2, head_dy=2, near_leg=6, far_leg=4,
                    near_arm=6, far_arm=4, arm_lift=12, mallet=(70, 24)))
    for step in range(8):
        add("flip", rotated(tuck, step * 45, 48 + DRAW_X, 76 + DRAW_Y))

    # the same body with empty hands, for while the mallet is in flight
    for name, kw in BASE:
        add("empty_" + name, draw(old(**kw)))

    floored = laid_down(draw(old(**HIT_POSE)), 48 + DRAW_X, 80 + DRAW_Y, BASELINE)
    add("down", floored)
    add("empty_down", floored)
    return frames


def grunt_frames():
    """The grunt: the same body bare-handed, recoloured at shading time."""
    frames = []
    for name, kw in BASE:
        if name in ("idle", "walk", "hit"):
            frames.append((name, draw(old(**kw))))
    frames.append(("attack", draw(old(lean=-4, body_dy=1, near_arm=-9, far_arm=5, arm_lift=9, near_leg=-3, far_leg=3))))
    frames.append(("attack", draw(old(lean=5, body_dy=0, near_arm=15, far_arm=-6, arm_lift=11, near_leg=7, far_leg=-5))))
    frames.append(("attack", draw(old(lean=3, body_dy=1, near_arm=9, far_arm=-3, arm_lift=6, near_leg=4, far_leg=-2))))
    frames.append(("down", laid_down(draw(old(**HIT_POSE)), 48 + DRAW_X, 80 + DRAW_Y, BASELINE)))
    return frames


GRUNT_MATS = dict(MATS)
GRUNT_MATS[SUIT] = Material((84, 162, 92))
GRUNT_MATS[RED] = Material((146, 70, 166))
GRUNT_MATS[HAIR] = Material((110, 200, 214))
GRUNT_MATS[DIAMOND] = Material((246, 200, 84))


def mallet_frames():
    """The thrown mallet alone at eight rotations, in a 120px square."""
    MS = 120
    loose = HDCanvas(W, H, DRAW_X, DRAW_Y)
    mallet_handle(loose, 24, 64, 0, 44)
    mallet_head(loose, 24, 64, 0, 44)
    cx, cy = 52 + DRAW_X, 64 + DRAW_Y
    out = []
    for step in range(8):
        cv = crop(rotated(loose, step * 45, cx, cy), cx - MS // 2, cy - MS // 2, MS, MS)
        if touches_border(cv):
            print("WARNING: mallet projectile frame {} is clipped".format(step))
        out.append(shade(cv, MATS, normals=True))
    return out, MS


def write_sheet(name, frames, mats):
    clipped = sorted({n for n, cv in frames if touches_border(cv)})
    if clipped:
        print("WARNING: {} art touches the canvas edge in: {}".format(name, ", ".join(clipped)))
    both = [shade(cv, mats, normals=True) for _, cv in frames]
    shaded = [p for p, _ in both]
    out = os.path.join("assets", "sprites", name + ".png")
    write_png(out, W * len(shaded), H, to_rows(shaded, W, H))
    write_png(os.path.join("assets", "sprites", name + "_n.png"), W * len(both), H, to_rows([n for _, n in both], W, H))
    clips = {}
    for i, (n, _) in enumerate(frames):
        clips.setdefault(n, []).append(i)
    with open(os.path.join("assets", "sprites", name + ".frames.json"), "w") as f:
        json.dump({"frame_width": W, "frame_height": H, "baseline": BASELINE,
                   "count": len(shaded), "clips": clips, "fps": FPS}, f, indent=2)
    print("wrote {} (+ normal map)  {}x{}  {} frames".format(out, W * len(shaded), H, len(shaded)))


# The squad: four clowns, one body, each in their own colours. PIP is MATS as
# drawn; the others recolour suit, diamonds, hat-and-shoes and hair. Kept clear
# of the grunts' green, purple and teal so friend and foe never blur.
def squad_mats(suit, diamond, red, hair):
    m = dict(MATS)
    m[SUIT] = Material(suit)
    m[DIAMOND] = Material(diamond)
    m[RED] = Material(red)
    m[HAIR] = Material(hair)
    return m


SQUAD = {
    "poppy": squad_mats((236, 112, 170), (250, 214, 96), (120, 70, 200), (90, 210, 220)),
    "bingo": squad_mats((236, 140, 56), (70, 130, 230), (40, 150, 110), (110, 70, 40)),
    "dot": squad_mats((244, 204, 70), (214, 56, 66), (60, 60, 160), (40, 30, 50)),
}


def main():
    frames = player_frames()
    write_sheet("pip_hd", frames, MATS)
    for name, mats in SQUAD.items():
        write_sheet(name + "_hd", frames, mats)
    write_sheet("grunt_hd", grunt_frames(), GRUNT_MATS)
    spin, ms = mallet_frames()
    out = os.path.join("assets", "sprites", "mallet_spin_hd.png")
    write_png(out, ms * len(spin), ms, to_rows([p for p, _ in spin], ms, ms))
    write_png(out.replace(".png", "_n.png"), ms * len(spin), ms, to_rows([n for _, n in spin], ms, ms))
    with open(os.path.join("assets", "sprites", "mallet_spin_hd.frames.json"), "w") as f:
        json.dump({"frame_width": ms, "frame_height": ms, "count": len(spin),
                   "clips": {"mallet_spin": list(range(len(spin)))}}, f, indent=2)
    print("wrote {}  {} frames".format(out, len(spin)))


if __name__ == "__main__":
    main()
