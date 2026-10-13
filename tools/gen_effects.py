"""Generate the ground-impact effect sheet.

A burst that reads as a mallet striking earth: a bright core, an expanding
elliptical shock ring (squashed, so it reads as lying on the ground under a
tilted camera), and dust that rises and thins. Five frames, played once.

Run: python tools/gen_effects.py
"""
import os, json, math, sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelpng import write_png

W, H = 64, 40
CX, CY = 32, 28          # impact point sits low, so dust has room to rise

CORE = (255, 255, 255)
FLASH = (255, 244, 190)
DUST = (246, 232, 206)
DUST_DARK = (206, 178, 142)


def blank():
    return [[None] * W for _ in range(H)]


def put(buf, x, y, c):
    if 0 <= x < W and 0 <= y < H:
        buf[y][x] = c


def disc(buf, cx, cy, rx, ry, c):
    for y in range(int(cy - ry), int(cy + ry) + 1):
        for x in range(int(cx - rx), int(cx + rx) + 1):
            dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
            if dx * dx + dy * dy <= 1.0:
                put(buf, x, y, c)


def ring(buf, cx, cy, rx, ry, thickness, c):
    inner = 1.0 - thickness / max(rx, 1.0)
    for y in range(int(cy - ry) - 1, int(cy + ry) + 2):
        for x in range(int(cx - rx) - 1, int(cx + rx) + 2):
            dx, dy = (x - cx) / max(rx, .5), (y - cy) / max(ry, .5)
            d = dx * dx + dy * dy
            if inner * inner <= d <= 1.0:
                put(buf, x, y, c)


def puffs(buf, count, radius, rise, size, c, seed=0):
    """Dust blobs thrown outward along the ground, drifting upward as they age."""
    for i in range(count):
        a = (i / count) * math.tau + seed
        x = CX + math.cos(a) * radius
        y = CY - rise + math.sin(a) * radius * 0.35
        disc(buf, x, y, size, max(1, size - 1), c)


FRAMES = []

# 0: flash of contact
f = blank()
ring(f, CX, CY, 13, 6, 4, FLASH)
disc(f, CX, CY, 8, 4, CORE)
FRAMES.append(("burst", f, 1.0))

# 1: shock ring expands, first dust
f = blank()
puffs(f, 6, 15, 2, 4, DUST, 0.3)
ring(f, CX, CY, 19, 9, 4, FLASH)
disc(f, CX, CY, 5, 3, CORE)
FRAMES.append(("burst", f, 1.0))

# 2: ring thins and widens, dust lifts
f = blank()
puffs(f, 7, 21, 5, 5, DUST, 0.6)
puffs(f, 5, 13, 8, 3, DUST_DARK, 1.1)
ring(f, CX, CY, 26, 12, 3, DUST)
FRAMES.append(("burst", f, 0.85))

# 3: ring almost gone, dust highest
f = blank()
puffs(f, 6, 25, 9, 4, DUST_DARK, 0.9)
puffs(f, 4, 15, 13, 3, DUST_DARK, 1.6)
ring(f, CX, CY, 31, 14, 2, DUST_DARK)
FRAMES.append(("burst", f, 0.75))

# 4: residue
f = blank()
puffs(f, 5, 27, 13, 3, DUST_DARK, 1.2)
FRAMES.append(("burst", f, 0.45))

rows = []
for y in range(H):
    row = bytearray()
    for _, buf, alpha in FRAMES:
        for x in range(W):
            c = buf[y][x]
            if c is None:
                row.extend((0, 0, 0, 0))
            else:
                row.extend((c[0], c[1], c[2], int(255 * alpha)))
    rows.append(row)

os.makedirs(os.path.join("assets", "effects"), exist_ok=True)
out = os.path.join("assets", "effects", "impact.png")
write_png(out, W * len(FRAMES), H, rows)

clips = {}
for i, (name, _, _) in enumerate(FRAMES):
    clips.setdefault(name, []).append(i)
meta = {"frame_width": W, "frame_height": H, "count": len(FRAMES), "clips": clips}
with open(os.path.join("assets", "effects", "impact.frames.json"), "w") as fh:
    json.dump(meta, fh, indent=2)

print("wrote {}  {}x{}  {} frames".format(out, W * len(FRAMES), H, len(FRAMES)))
