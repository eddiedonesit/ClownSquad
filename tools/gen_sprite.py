"""Generate the player clown sprite sheet, and the spinning mallet projectile.

Everything is drawn from one parameterised profile pose, so each animation is
the same artwork at different offsets. Profile only: fighters face left or right
and the sheet is mirrored for leftward travel (ADR 0004).

The canvas is taller than the character. Rows above give the raised mallet room
and rows below give the front flip room to rotate through; the frame map records
the feet row as "baseline" so the engine stands the sprite on the ground
regardless of that padding.

Run: bash tools/refresh_sprites.sh   (never this file alone - see tools/README)
"""
import os, sys, json, math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart import Canvas, shade, pack_rows, rotated, touches_border, laid_down, MATS
from pixelpng import write_png

BODY_H = 64              # the character is authored in a 48 x 64 space
TOP_PAD, BOTTOM_PAD = 44, 36
W, H = 104, TOP_PAD + BODY_H + BOTTOM_PAD
DRAW_X, DRAW_Y = 28, TOP_PAD
BASELINE = TOP_PAD + BODY_H      # canvas row the feet stand on

CARRY = (143, 29)        # mallet resting back over the shoulder


def draw_mallet(c, hx, hy, spec, head_len=24, head_rad=9):
    """A cylinder whose axis is perpendicular to the handle, with an end cap.
    A plain disc reads as a lollipop; the barrel and cap are what say mallet."""
    ang, length = spec
    r = math.radians(ang)
    mx = hx + math.cos(r) * length
    my = hy - math.sin(r) * length
    c.limb(hx, hy, int(round(mx)), int(round(my)), 14, half=2)

    pr = r + math.pi / 2.0
    ax, ay = math.cos(pr), -math.sin(pr)
    half = head_len / 2.0
    t = -half
    while t <= half:
        c.ellipse(int(round(mx + ax * t)), int(round(my + ay * t)), head_rad, head_rad, 1)
        t += 0.5
    c.ellipse(int(round(mx + ax * half)), int(round(my + ay * half)), head_rad - 1, head_rad - 1, 4)
    c.ellipse(int(round(mx - ax * half)), int(round(my - ay * half)), head_rad - 2, head_rad - 2, 13)


SIDE = dict(body_dy=0, lean=0, head_dy=0,
            near_leg=0, far_leg=0, near_arm=0, far_arm=0, arm_lift=0,
            mallet=None)


def spose(**over):
    p = dict(SIDE)
    p.update(over)
    return p


def draw_side(p):
    """Profile view facing RIGHT."""
    c = Canvas(W, H, DRAW_X, DRAW_Y)
    by, ln = p["body_dy"], p["lean"]
    nl, fl = p["near_leg"], p["far_leg"]
    na, fa, lift = p["near_arm"], p["far_arm"], p["arm_lift"]

    # far leg first, in darker tones so the near leg reads in front
    c.limb(23 + ln, 49 + by, 23 + fl, 57, 11, half=2)
    c.ellipse(26 + fl, 60, 9, 4, 13)
    c.limb(25 + ln, 49 + by, 25 + nl, 57, 5, half=2)
    for i in range(2):
        c.rect(23 + nl, 52 + i * 3, 27 + nl, 53 + i * 3, 1)
    c.ellipse(28 + nl, 60, 10, 4, 8)
    c.ellipse(29 + nl, 59, 8, 2, 1)

    # far arm behind the torso
    c.limb(22 + ln, 40 + by, 22 + fa, 53 - lift, 11, half=2)
    c.ellipse(22 + fa, 53 - lift, 3, 3, 12)

    # torso
    c.rect(19 + ln, 37 + by, 29 + ln, 50 + by, 5)
    c.ellipse(24 + ln, 50 + by, 5, 3, 5)
    for b in (43, 48):
        for dy in range(-2, 3):
            w = 2 - abs(dy)
            c.rect(24 + ln - w, b + dy + by, 24 + ln + w, b + dy + by, 10)

    # near arm in front
    c.limb(26 + ln, 40 + by, 26 + na, 53 - lift, 5, half=2)
    c.ellipse(26 + na, 53 - lift, 3, 3, 6)

    # ruff
    c.ellipse(24 + ln, 36 + by, 9, 4, 4)
    for cx in (17, 24, 31):
        c.ellipse(cx + ln, 38 + by, 3, 3, 4)

    hy = 24 + by + p["head_dy"]
    c.ellipse(15 + ln, hy + 4, 5, 4, 7)          # hair tuft trailing behind
    c.ellipse(24 + ln, hy, 9, 10, 2)             # head
    c.ellipse(28 + ln, hy, 1, 2, 9)              # eye
    c.put(28 + ln, hy - 1, 6)
    c.ellipse(33 + ln, hy + 3, 3, 3, 3)          # nose, the key profile read
    c.ellipse(30 + ln, hy + 5, 2, 1, 3)
    for x in range(26, 32):
        c.put(x + ln, hy + 8, 9)

    # hat, sat down on the brow, with diagonal stripes
    hx = 22 + ln
    top = hy - 19
    c.tri(hx - 2, top, hx - 10, hy - 6, hx + 11, hy - 6, 1)
    for y in range(top, hy - 5):
        for x in range(-DRAW_X, W - DRAW_X):
            if c.get(x, y) == 1 and (x + y) % 7 < 2:
                c.put(x, y, 4)
    c.ellipse(hx + 1, hy - 6, 11, 2, 1)
    c.ellipse(hx - 2, top, 3, 3, 6)

    if p.get("mallet") is not None:
        draw_mallet(c, 26 + na, 53 - lift, p["mallet"])
    return c


FRAMES = []          # (clip name, Canvas)


def add(name, canvas):
    FRAMES.append((name, canvas))


# Poses shared by the armed set and the empty-handed set shown while the
# mallet is away on a throw.
BASE = [
    ("idle", dict()),
    ("idle", dict(body_dy=2, head_dy=1, arm_lift=-1)),
    ("walk", dict(near_leg=8, far_leg=-7, near_arm=-9, far_arm=10, body_dy=-2, lean=1, arm_lift=1)),
    ("walk", dict(near_leg=3, far_leg=-2, near_arm=-4, far_arm=4)),
    ("walk", dict(near_leg=-7, far_leg=8, near_arm=10, far_arm=-9, body_dy=-2, lean=1, arm_lift=1)),
    ("walk", dict(near_leg=-2, far_leg=3, near_arm=4, far_arm=-4)),
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

# The deep slide drops the hand so low that the shoulder carry ran off the
# canvas edge. Dodge frames hold the mallet steeper and closer instead.
DODGE_CARRY = (112, 24)

for name, kw in BASE:
    add(name, draw_side(spose(mallet=DODGE_CARRY if name == "dodge" else CARRY, **kw)))

# attack: mallet arcs from behind the head, out to 45 degrees, down to ground
add("attack", draw_side(spose(lean=-3, body_dy=1, near_arm=-6, far_arm=-4, arm_lift=5, mallet=(165, 26))))
add("attack", draw_side(spose(lean=0, body_dy=-1, near_arm=-2, far_arm=-1, arm_lift=9, mallet=(120, 30))))
add("attack", draw_side(spose(lean=3, body_dy=-1, near_arm=6, far_arm=3, arm_lift=5, mallet=(45, 30))))
add("attack", draw_side(spose(lean=4, body_dy=1, near_arm=9, far_arm=5, arm_lift=-2, mallet=(0, 26))))

# charge: coiled low with the mallet drawn right back, two frames to shudder
add("charge", draw_side(spose(body_dy=5, lean=-4, head_dy=1, near_leg=5, far_leg=-5,
                              near_arm=-9, far_arm=-6, arm_lift=7, mallet=(168, 28))))
add("charge", draw_side(spose(body_dy=6, lean=-5, head_dy=2, near_leg=6, far_leg=-5,
                              near_arm=-10, far_arm=-7, arm_lift=8, mallet=(160, 28))))

# spin: mallet held straight out. The engine mirrors these rapidly, which is
# what reads as whirling when there is only a profile view to work with.
add("spin", draw_side(spose(body_dy=2, lean=2, near_leg=4, far_leg=-4,
                            near_arm=10, far_arm=-8, arm_lift=10, mallet=(6, 28))))
add("spin", draw_side(spose(body_dy=3, lean=3, near_leg=-3, far_leg=4,
                            near_arm=10, far_arm=-8, arm_lift=9, mallet=(-6, 28))))

# throw: wind up, then released with the arm thrown forward and the hand empty
add("throw", draw_side(spose(lean=-5, body_dy=2, near_leg=-4, far_leg=5,
                             near_arm=-10, far_arm=-6, arm_lift=11, mallet=(150, 26))))
add("throw", draw_side(spose(lean=6, body_dy=1, near_leg=8, far_leg=-6,
                             near_arm=13, far_arm=7, arm_lift=9)))

# flip: one tucked pose with the mallet out, rotated through a full turn.
# Clockwise on screen is a forward flip when facing right.
FLIP_CX, FLIP_CY = 24 + DRAW_X, 38 + DRAW_Y
tuck = draw_side(spose(body_dy=3, lean=2, head_dy=2, near_leg=6, far_leg=4,
                       near_arm=6, far_arm=4, arm_lift=12, mallet=(70, 24)))
for step in range(8):
    add("flip", rotated(tuck, step * 45, FLIP_CX, FLIP_CY))

# the same body with empty hands, for while the mallet is in flight
for name, kw in BASE:
    add("empty_" + name, draw_side(spose(**kw)))

# floored: the recoil pose tipped onto its back. Drawn empty-handed in both
# sets, since a mallet rotated with the body runs off the canvas.
HIT_POSE = dict(lean=-4, head_dy=-2, body_dy=2, near_arm=-7, far_arm=-5, arm_lift=-2)
FLOORED = laid_down(draw_side(spose(**HIT_POSE)), 24 + DRAW_X, 40 + DRAW_Y, BASELINE)
add("down", FLOORED)
add("empty_down", FLOORED)

clipped = sorted({n for n, cv in FRAMES if touches_border(cv)})
if clipped:
    print("WARNING: art touches the canvas edge (clipped) in: " + ", ".join(clipped))

shaded = [shade(cv) for _, cv in FRAMES]
out = os.path.join("assets", "sprites", "clown_16bit.png")
write_png(out, W * len(FRAMES), H, pack_rows(shaded, W, H))

clips = {}
for i, (name, _) in enumerate(FRAMES):
    clips.setdefault(name, []).append(i)
meta = {"frame_width": W, "frame_height": H, "baseline": BASELINE,
        "count": len(FRAMES), "clips": clips}
with open(os.path.join("assets", "sprites", "clown_16bit.frames.json"), "w") as f:
    json.dump(meta, f, indent=2)
print("wrote {}  {}x{}  {} frames".format(out, W * len(FRAMES), H, len(FRAMES)))
print("clips: " + ", ".join("{}={}".format(k, len(v)) for k, v in clips.items()))

# ---- the thrown mallet: the same mallet, alone, at eight rotations ----
MS = 60                                   # square frame for the projectile
loose = Canvas(W, H, DRAW_X, DRAW_Y)
draw_mallet(loose, 12, 32, (0, 22))
mcx, mcy = 26 + DRAW_X, 32 + DRAW_Y       # spin about the middle of the mallet
spin_frames = []
for step in range(8):
    rot = rotated(loose, step * 45, mcx, mcy)
    crop = Canvas(MS, MS)
    for y in range(MS):
        for x in range(MS):
            sx, sy = mcx - MS // 2 + x, mcy - MS // 2 + y
            if 0 <= sx < W and 0 <= sy < H:
                crop.buf[y][x] = rot.buf[sy][sx]
    if touches_border(crop):
        print("WARNING: mallet projectile frame {} is clipped".format(step))
    spin_frames.append(shade(crop))

mout = os.path.join("assets", "sprites", "mallet_spin.png")
write_png(mout, MS * len(spin_frames), MS, pack_rows(spin_frames, MS, MS))
with open(os.path.join("assets", "sprites", "mallet_spin.frames.json"), "w") as f:
    json.dump({"frame_width": MS, "frame_height": MS, "count": len(spin_frames),
               "clips": {"mallet_spin": list(range(len(spin_frames)))}}, f, indent=2)
print("wrote {}  {} frames".format(mout, len(spin_frames)))

# ---- the grunt: the same body, bare-handed, in different colours ----
# Recolouring at the shading step keeps one set of artwork while making the
# rank-and-file instantly distinguishable from the player.
GRUNT_MATS = dict(MATS)
GRUNT_MATS[5] = ((86, 160, 92), (52, 112, 60), (134, 204, 138))     # suit: green
GRUNT_MATS[11] = ((58, 110, 66), (38, 78, 46), (92, 150, 100))      # far-side suit
GRUNT_MATS[1] = ((142, 70, 160), (98, 44, 114), (186, 118, 202))    # hat and shoes: purple
GRUNT_MATS[13] = ((104, 50, 120), (72, 32, 86), (140, 84, 158))     # far-side shoe
GRUNT_MATS[7] = ((120, 200, 210), (76, 148, 160), (170, 232, 238))  # hair: teal
GRUNT_MATS[10] = ((246, 203, 90), (190, 142, 52), (255, 234, 158))  # suit diamonds: yellow

grunt = []
for name, kw in BASE:
    if name in ("idle", "walk", "hit"):
        grunt.append((name, draw_side(spose(**kw))))
# a straight punch: draw back, throw, recover
grunt.append(("attack", draw_side(spose(lean=-4, body_dy=1, near_arm=-9, far_arm=5, arm_lift=9, near_leg=-3, far_leg=3))))
grunt.append(("attack", draw_side(spose(lean=5, body_dy=0, near_arm=15, far_arm=-6, arm_lift=11, near_leg=7, far_leg=-5))))
grunt.append(("attack", draw_side(spose(lean=3, body_dy=1, near_arm=9, far_arm=-3, arm_lift=6, near_leg=4, far_leg=-2))))
grunt.append(("down", FLOORED))

gout = os.path.join("assets", "sprites", "grunt_clown.png")
write_png(gout, W * len(grunt), H, pack_rows([shade(cv, GRUNT_MATS) for _, cv in grunt], W, H))
gclips = {}
for i, (name, _) in enumerate(grunt):
    gclips.setdefault(name, []).append(i)
with open(os.path.join("assets", "sprites", "grunt_clown.frames.json"), "w") as f:
    json.dump({"frame_width": W, "frame_height": H, "baseline": BASELINE,
               "count": len(grunt), "clips": gclips}, f, indent=2)
print("wrote {}  {} frames".format(gout, len(grunt)))
