"""Generate a 16-bit-style character sprite sheet with animation frames.

Draws the clown from a parameterised pose, so idle breathing, a walk cycle,
an attack swing and a hit recoil are all the same artwork at different offsets.
Emits a horizontal strip to assets/sprites/clown_16bit.png plus a frame map.

Run: python tools/gen_sprite.py
"""
import zlib, struct, os, json, math

W, H = 104, 64
# Body art is authored in a 48-wide space; this offset centres it in the wider
# canvas so the oversized mallet head has room either side.
DRAW_X = 28

MATS = {
    1:  ((214, 58, 66),   (150, 36, 48),   (242, 112, 104)),  # hat red
    2:  ((247, 214, 188), (198, 158, 140), (255, 240, 226)),  # skin
    3:  ((226, 70, 70),   (170, 42, 48),   (250, 120, 112)),  # nose
    4:  ((246, 203, 90),  (190, 142, 52),  (255, 234, 158)),  # yellow trim
    5:  ((74, 110, 206),  (42, 66, 146),   (126, 166, 242)),  # suit blue
    6:  ((240, 242, 248), (190, 196, 214), (255, 255, 255)),  # white
    7:  ((235, 140, 70),  (176, 92, 44),   (252, 186, 124)),  # hair orange
    8:  ((70, 56, 76),    (44, 34, 50),    (104, 88, 110)),   # shoe
    9:  ((40, 30, 48),    (40, 30, 48),    (40, 30, 48)),     # eye
    10: ((150, 60, 150),  (104, 38, 108),  (196, 108, 196)),  # accent purple
    11: ((48, 72, 140),   (30, 46, 96),    (80, 110, 180)),   # far-side suit
    12: ((186, 192, 208), (146, 152, 172), (220, 224, 236)),  # far-side glove
    13: ((176, 40, 48),   (120, 26, 34),   (206, 86, 84)),    # far-side shoe
    14: ((170, 96, 54),   (118, 62, 32),   (206, 136, 86)),   # mallet handle
}
OUTLINE = (26, 18, 34)



def mallet_tip(hx, hy, spec):
    """Resolve a (angle_deg, length) mallet spec into a tip position."""
    ang, length = spec
    r = math.radians(ang)
    return int(round(hx + math.cos(r) * length)), int(round(hy - math.sin(r) * length))


class Canvas:
    def __init__(self):
        self.buf = [[0] * W for _ in range(H)]

    def put(self, x, y, m):
        x += DRAW_X
        if 0 <= x < W and 0 <= y < H:
            self.buf[y][x] = m

    def ellipse(self, cx, cy, rx, ry, m):
        for y in range(cy - ry, cy + ry + 1):
            for x in range(cx - rx, cx + rx + 1):
                dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
                if dx * dx + dy * dy <= 1.0:
                    self.put(x, y, m)

    def rect(self, x0, y0, x1, y1, m):
        for y in range(y0, y1 + 1):
            for x in range(x0, x1 + 1):
                self.put(x, y, m)

    def tri(self, ax, ay, bx, by, cx_, cy_, m):
        def sign(x1, y1, x2, y2, x3, y3):
            return (x1 - x3) * (y2 - y3) - (x2 - x3) * (y1 - y3)
        for y in range(min(ay, by, cy_), max(ay, by, cy_) + 1):
            for x in range(min(ax, bx, cx_), max(ax, bx, cx_) + 1):
                d1 = sign(x, y, ax, ay, bx, by)
                d2 = sign(x, y, bx, by, cx_, cy_)
                d3 = sign(x, y, cx_, cy_, ax, ay)
                neg = (d1 < 0) or (d2 < 0) or (d3 < 0)
                pos = (d1 > 0) or (d2 > 0) or (d3 > 0)
                if not (neg and pos):
                    self.put(x, y, m)

    def limb(self, sx, sy, ex, ey, m, half=2):
        steps = max(abs(ex - sx), abs(ey - sy), 1)
        for t in range(steps + 1):
            f = t / steps
            x = int(round(sx + (ex - sx) * f))
            y = int(round(sy + (ey - sy) * f))
            self.rect(x - half, y - half, x + half, y + half, m)


SIDE = dict(body_dy=0, lean=0, head_dy=0,
            near_leg=0, far_leg=0, near_arm=0, far_arm=0, arm_lift=0,
            mallet=None)


def spose(**over):
    p = dict(SIDE)
    p.update(over)
    return p



def draw_mallet(c, hx, hy, spec, head_len=24, head_rad=9):
    """Draw a mallet: a cylinder whose axis is perpendicular to the handle.

    A plain disc reads as a lollipop. What makes a mallet legible is the barrel
    running across the handle plus a contrasting end cap showing its circular
    face, which is what the reference art does.
    """
    ang, length = spec
    r = math.radians(ang)
    mx = hx + math.cos(r) * length
    my = hy - math.sin(r) * length

    # handle, from the hand to the centre of the head
    c.limb(hx, hy, int(round(mx)), int(round(my)), 14, half=2)

    # barrel, swept along the axis perpendicular to the handle
    pr = r + math.pi / 2.0
    ax, ay = math.cos(pr), -math.sin(pr)
    half = head_len / 2.0
    t = -half
    while t <= half:
        c.ellipse(int(round(mx + ax * t)), int(round(my + ay * t)), head_rad, head_rad, 1)
        t += 0.5

    # far face, lighter, so the cylinder reads as 3D rather than flat
    ex = mx + ax * half
    ey = my + ay * half
    c.ellipse(int(round(ex)), int(round(ey)), head_rad - 1, head_rad - 1, 4)

    # near rim, a darker lip that separates barrel from handle
    nx = mx - ax * half
    ny = my - ay * half
    c.ellipse(int(round(nx)), int(round(ny)), head_rad - 2, head_rad - 2, 13)


def draw_side(p):
    """Profile view facing RIGHT. Flipped horizontally for leftward movement."""
    c = Canvas()
    by, ln = p["body_dy"], p["lean"]
    nl, fl = p["near_leg"], p["far_leg"]
    na, fa, lift = p["near_arm"], p["far_arm"], p["arm_lift"]

    # far leg first, in darker tones so the near leg reads in front
    c.limb(23 + ln, 49 + by, 23 + fl, 57, 11, half=2)
    c.ellipse(26 + fl, 60, 9, 4, 13)
    # near leg
    c.limb(25 + ln, 49 + by, 25 + nl, 57, 5, half=2)
    for i in range(2):
        c.rect(23 + nl, 52 + i * 3, 27 + nl, 53 + i * 3, 1)
    c.ellipse(28 + nl, 60, 10, 4, 8)
    c.ellipse(29 + nl, 59, 8, 2, 1)

    # far arm behind the torso
    c.limb(22 + ln, 40 + by, 22 + fa, 53 - lift, 11, half=2)
    c.ellipse(22 + fa, 53 - lift, 3, 3, 12)

    # torso, narrower than the front view
    c.rect(19 + ln, 37 + by, 29 + ln, 50 + by, 5)
    c.ellipse(24 + ln, 50 + by, 5, 3, 5)
    for b in (43, 48):
        for dy in range(-2, 3):
            w = 2 - abs(dy)
            c.rect(24 + ln - w, b + dy + by, 24 + ln + w, b + dy + by, 10)

    # near arm in front
    c.limb(26 + ln, 40 + by, 26 + na, 53 - lift, 5, half=2)
    c.ellipse(26 + na, 53 - lift, 3, 3, 6)

    # ruff, narrower in profile
    c.ellipse(24 + ln, 36 + by, 9, 4, 4)
    for cx in (17, 24, 31):
        c.ellipse(cx + ln, 38 + by, 3, 3, 4)

    hy = 24 + by + p["head_dy"]
    # hair tuft trails at the back
    c.ellipse(15 + ln, hy + 4, 5, 4, 7)
    # head
    c.ellipse(24 + ln, hy, 9, 10, 2)
    # single visible eye, set forward
    c.ellipse(28 + ln, hy, 1, 2, 9)
    c.put(28 + ln, hy - 1, 6)
    # nose juts out to the right, the key profile read
    c.ellipse(33 + ln, hy + 3, 3, 3, 3)
    c.ellipse(30 + ln, hy + 5, 2, 1, 3)
    for x in range(26, 32):
        c.put(x + ln, hy + 8, 9)

    # hat, tipped back off the brow
    hx = 22 + ln
    top = hy - 19
    c.tri(hx - 2, top, hx - 10, hy - 6, hx + 11, hy - 6, 1)
    for y in range(max(0, top), hy - 5):
        for x in range(W):
            if c.buf[y][x] == 1 and (x + y) % 7 < 2:
                c.buf[y][x] = 4
    c.ellipse(hx + 1, hy - 6, 11, 2, 1)
    c.ellipse(hx - 2, top, 3, 3, 6)

    if p.get("mallet") is not None:
        draw_mallet(c, 26 + na, 53 - lift, p["mallet"])
    return c.buf



def shade(buf):
    """Pixel-art passes: dark silhouette outline, rim light upper-left,
    shadow lower-right. This is what gives the flat regions a 16-bit read."""
    def op(x, y):
        return 0 <= x < W and 0 <= y < H and buf[y][x] != 0
    px = [[(0, 0, 0, 0)] * W for _ in range(H)]
    for y in range(H):
        for x in range(W):
            m = buf[y][x]
            if m == 0:
                continue
            if not all(op(x + dx, y + dy) for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1))):
                px[y][x] = OUTLINE + (255,)
                continue
            base, dark, light = MATS[m]
            if not op(x + 2, y + 2) or not op(x + 1, y + 2):
                col = dark
            elif not op(x - 2, y - 2) or not op(x - 1, y - 2):
                col = light
            else:
                col = base
            px[y][x] = col + (255,)
    return px


FRAMES = []


def add(name, p):
    FRAMES.append((name, draw_side(p)))


# Profile only. Characters face left or right and never toward or away from the
# camera, so there is one set of frames, mirrored for leftward travel (ADR 0004).

# idle: breathing
add("idle", spose(mallet=(143, 29)))
add("idle", spose(body_dy=2, head_dy=1, arm_lift=-1, mallet=(143, 29)))

# walk: legs scissor hard, arms counter-swing
add("walk", spose(near_leg=8, far_leg=-7, near_arm=-9, far_arm=10,
                  body_dy=-2, lean=1, arm_lift=1, mallet=(143, 29)))
add("walk", spose(near_leg=3, far_leg=-2, near_arm=-4, far_arm=4,
                  body_dy=0, mallet=(143, 29)))
add("walk", spose(near_leg=-7, far_leg=8, near_arm=10, far_arm=-9,
                  body_dy=-2, lean=1, arm_lift=1, mallet=(143, 29)))
add("walk", spose(near_leg=-2, far_leg=3, near_arm=4, far_arm=-4,
                  body_dy=0, mallet=(143, 29)))

# attack: mallet arcs from behind the head, out to 45 degrees on impact
add("attack", spose(lean=-3, body_dy=1, near_arm=-6, far_arm=-4,
                    arm_lift=5, mallet=(165, 26)))
add("attack", spose(lean=0, body_dy=-1, near_arm=-2, far_arm=-1,
                    arm_lift=9, mallet=(120, 30)))
add("attack", spose(lean=3, body_dy=-1, near_arm=6, far_arm=3,
                    arm_lift=5, mallet=(45, 30)))
add("attack", spose(lean=4, body_dy=1, near_arm=9, far_arm=5,
                    arm_lift=-2, mallet=(0, 26)))

# hit recoil
add("hit", spose(lean=-4, head_dy=-2, body_dy=2,
                 near_arm=-7, far_arm=-5, arm_lift=-2, mallet=(143, 29)))

# dodge slide: body drops, leading leg extends, arms trail behind
add("dodge", spose(body_dy=8, lean=6, head_dy=3,
                   near_leg=12, far_leg=-6,
                   near_arm=-10, far_arm=-7, arm_lift=-6, mallet=(143, 29)))
add("dodge", spose(body_dy=15, lean=10, head_dy=6,
                   near_leg=20, far_leg=-10,
                   near_arm=-15, far_arm=-11, arm_lift=-11, mallet=(143, 29)))
add("dodge", spose(body_dy=8, lean=5, head_dy=3,
                   near_leg=11, far_leg=-5,
                   near_arm=-9, far_arm=-6, arm_lift=-6, mallet=(143, 29)))

# jump: tuck on the rise, reach on the fall
add("jump", spose(body_dy=-2, lean=2, near_leg=5, far_leg=-3,
                  near_arm=-6, far_arm=-4, arm_lift=6, mallet=(143, 29)))
add("jump", spose(body_dy=0, lean=-1, near_leg=-4, far_leg=6,
                  near_arm=4, far_arm=-3, arm_lift=2, mallet=(143, 29)))

SHEET_W = W * len(FRAMES)
shaded = [shade(b) for _, b in FRAMES]
rows = []
for y in range(H):
    row = bytearray()
    for f in shaded:
        for x in range(W):
            row.extend(f[y][x])
    rows.append(row)


def write_png(path, w, h, rows):
    raw = b"".join(b"\x00" + bytes(r) for r in rows)

    def ch(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)

    data = (b"\x89PNG\r\n\x1a\n"
            + ch(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + ch(b"IDAT", zlib.compress(raw, 9)) + ch(b"IEND", b""))
    open(path, "wb").write(data)


out = os.path.join("assets", "sprites", "clown_16bit.png")
write_png(out, SHEET_W, H, rows)

clips = {}
for i, (name, _) in enumerate(FRAMES):
    clips.setdefault(name, []).append(i)
meta = {"frame_width": W, "frame_height": H, "count": len(FRAMES), "clips": clips}
with open(os.path.join("assets", "sprites", "clown_16bit.frames.json"), "w") as f:
    json.dump(meta, f, indent=2)

print("wrote {}  {}x{}  {} frames  {} bytes".format(out, SHEET_W, H, len(FRAMES), os.path.getsize(out)))
print("clips: " + ", ".join("{}={}".format(k, len(v)) for k, v in clips.items()))
