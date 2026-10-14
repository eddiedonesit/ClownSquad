"""Generate the first enemy: a big, fat, grinning clown.

Deliberately built from the same palette and shading passes as the player so
they read as the same game, but with inverted proportions: the player is small
and nimble, this thing is a wall of belly with a fixed grin.

Run: python tools/gen_enemy.py
"""
import os, sys, json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart import Canvas, shade, pack_rows
from pixelpng import write_png

W, H, OX = 88, 64, 12

NEUTRAL = dict(body_dy=0, lean=0, head_dy=0, belly=0,
               near_leg=0, far_leg=0, arm_dy=0, grin=0)


def pose(**over):
    p = dict(NEUTRAL)
    p.update(over)
    return p


def draw(p):
    c = Canvas(W, H, OX)
    by, ln, hd = p["body_dy"], p["lean"], p["head_dy"]
    bulge = p["belly"]

    # far arm and leg first, in darker tones
    c.limb(26 + p["far_leg"], 52 + by, 24 + p["far_leg"], 58, 16, half=3)
    c.ellipse(23 + p["far_leg"], 60, 8, 4, 13)
    c.limb(22 + ln, 34 + by, 15 + ln, 46 + p["arm_dy"], 16, half=3)
    c.ellipse(14 + ln, 47 + p["arm_dy"], 5, 5, 12)

    # near leg
    c.limb(34 + p["near_leg"], 52 + by, 36 + p["near_leg"], 58, 16, half=3)
    c.ellipse(37 + p["near_leg"], 60, 9, 4, 1)
    c.ellipse(39 + p["near_leg"], 59, 6, 2, 4)

    # the belly: the whole silhouette hangs off this
    c.ellipse(30 + ln, 42 + by, 19 + bulge, 15 + bulge, 16)
    # polka dots and a pair of straining suspenders
    for dx, dy, r in ((20, 36, 2), (16, 46, 2), (26, 50, 2), (30, 38, 1), (36, 47, 2), (23, 43, 1)):
        c.ellipse(dx + ln, dy + by, r, r, 10)
    for x0, x1 in ((30, 26), (40, 42)):
        c.limb(x0 + ln, 30 + by, x1 + ln + bulge, 55 + by, 1, half=1)
        c.rect(x0 + ln - 1, 31 + by, x0 + ln + 1, 32 + by, 4)
    # pompom buttons down the front
    for y in (34, 42, 50):
        c.ellipse(44 + ln, y + by, 3, 3, 4)

    # near arm, stubby
    c.limb(40 + ln, 34 + by, 48 + ln, 44 + p["arm_dy"], 16, half=3)
    c.ellipse(47 + ln, 43 + p["arm_dy"], 3, 2, 4)    # cuff
    c.ellipse(49 + ln, 45 + p["arm_dy"], 5, 5, 6)

    # ruff collar, wide enough to sit on the belly
    c.ellipse(34 + ln, 28 + by, 13, 5, 4)
    for cx, m in ((23, 6), (29, 4), (35, 6), (41, 4), (46, 6)):
        c.ellipse(cx + ln, 30 + by, 3, 3, m)

    hy = 18 + by + hd
    # wild green hair behind
    c.ellipse(23 + ln, hy + 3, 7, 6, 15)
    c.ellipse(25 + ln, hy - 6, 5, 4, 15)
    # head, large enough to carry the grin
    c.ellipse(34 + ln, hy, 12, 11, 2)
    # thin brow over a small eye: menace without reading as sunglasses
    c.rect(37 + ln, hy - 6, 44 + ln, hy - 5, 9)
    c.ellipse(41 + ln, hy - 2, 2, 3, 9)
    c.put(42 + ln, hy - 3, 6)
    # nose, set high and forward so it never crowds the mouth
    c.ellipse(46 + ln, hy, 4, 4, 3)
    # the grin: a wide toothy band with a hard outline, the character beat
    g = p["grin"]
    gx0, gx1 = 29 + ln, 45 + ln
    gy0, gy1 = hy + 4, hy + 8 + g
    c.rect(gx0, gy0, gx1, gy1, 6)
    c.rect(gx0 - 1, gy0 - 1, gx1 + 1, gy0 - 1, 9)
    c.rect(gx0 - 1, gy1 + 1, gx1 + 1, gy1 + 1, 9)
    c.rect(gx0 - 1, gy0, gx0 - 1, gy1, 9)
    c.rect(gx1 + 1, gy0, gx1 + 1, gy1, 9)
    for tx in range(gx0 + 2, gx1, 4):
        c.rect(tx, gy0, tx, gy1, 9)

    # tiny hat, comically small on a huge head
    c.tri(30 + ln, hy - 18, 25 + ln, hy - 10, 37 + ln, hy - 10, 1)
    c.ellipse(31 + ln, hy - 10, 7, 2, 1)
    c.ellipse(30 + ln, hy - 18, 2, 2, 6)
    return c


FRAMES = []


def add(name, p):
    FRAMES.append((name, draw(p)))


# idle: the belly breathes
add("idle", pose())
add("idle", pose(belly=1, body_dy=1, head_dy=1, arm_dy=1))

# walk: a heavy waddle, weight shifting side to side
add("walk", pose(near_leg=5, far_leg=-4, body_dy=-1, lean=1, arm_dy=-1))
add("walk", pose(near_leg=2, far_leg=-1, body_dy=1, belly=1, arm_dy=1))
add("walk", pose(near_leg=-4, far_leg=5, body_dy=-1, lean=-1, arm_dy=-1))
add("walk", pose(near_leg=-1, far_leg=2, body_dy=1, belly=1, arm_dy=1))

# attack: rear back, then throw the whole belly forward
add("attack", pose(lean=-4, body_dy=1, head_dy=1, arm_dy=-4, grin=1))
add("attack", pose(lean=3, body_dy=-1, belly=2, arm_dy=2, grin=2, near_leg=4))
add("attack", pose(lean=6, body_dy=1, belly=1, arm_dy=4, grin=1, near_leg=7))

# hit
add("hit", pose(lean=-5, head_dy=-2, body_dy=2, arm_dy=-3, grin=-1))

# airborne: arms up and legs tucked for the hang, belly-first for the drop
add("air", pose(body_dy=-2, belly=1, near_leg=-3, far_leg=3, arm_dy=-9, grin=2))
add("air", pose(lean=4, body_dy=1, belly=2, arm_dy=-6, grin=2))

shaded = [shade(cv) for _, cv in FRAMES]
rows = pack_rows(shaded, W, H)

out = os.path.join("assets", "sprites", "boss_clown.png")
write_png(out, W * len(FRAMES), H, rows)

clips = {}
for i, (name, _) in enumerate(FRAMES):
    clips.setdefault(name, []).append(i)
meta = {"frame_width": W, "frame_height": H, "count": len(FRAMES), "clips": clips}
with open(os.path.join("assets", "sprites", "boss_clown.frames.json"), "w") as fh:
    json.dump(meta, fh, indent=2)

print("wrote {}  {}x{}  {} frames".format(out, W * len(FRAMES), H, len(FRAMES)))
print("clips: " + ", ".join("{}={}".format(k, len(v)) for k, v in clips.items()))
