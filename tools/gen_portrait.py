"""Comic-book close-ups of the four clowns, for the title intro.

Head and shoulders in three-quarter view, mallet over the shoulder, each clown
with their own expression. Drawn with the same HD pipeline and palettes as the
squad sheets (tools/gen_sprite_hd.py), at portrait scale: 240 x 300 pixels,
shown at 2x or more in the intro panels.

Writes assets/sprites/portrait_<clown>.png.
"""
import math
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier
from pixelpng import write_png
import gen_sprite_hd as hd

W, H = 300, 320
MOUTH, TONGUE, TOOTH = 19, 20, 21


def mats_for(base):
    m = dict(base)
    m[MOUTH] = Material((120, 24, 44))
    m[TONGUE] = Material((236, 110, 130), soft=True)
    m[TOOTH] = Material((250, 250, 255), soft=True)
    return m


def mallet(c, gx, gy, angle, length, head_len=120, head_rad=40):
    """The squeaky mallet at portrait scale: a lit barrel with banded ends and
    a yellow striking face, on a turned wooden handle."""
    r = math.radians(angle)
    c.part()
    c.capsule(gx, gy, gx + math.cos(r) * (length - 10), gy - math.sin(r) * (length - 10), 7, 6, hd.WOOD)
    mx, my = gx + math.cos(r) * length, gy - math.sin(r) * length
    ax, ay = math.cos(r + math.pi / 2), -math.sin(r + math.pi / 2)
    nx, ny = math.cos(r), -math.sin(r)
    half = head_len / 2
    if my + ay * half > my - ay * half:
        ax, ay = -ax, -ay
    e0 = (mx - ax * half, my - ay * half)
    e1 = (mx + ax * half, my + ay * half)
    axis = math.atan2(ay, ax)
    cap = head_rad * 0.42
    c.part()
    c.poly([(e0[0] + nx * head_rad, e0[1] + ny * head_rad), (e1[0] + nx * head_rad, e1[1] + ny * head_rad),
            (e1[0] - nx * head_rad, e1[1] - ny * head_rad), (e0[0] - nx * head_rad, e0[1] - ny * head_rad)], hd.RED)
    c.ellipse_rot(e0[0], e0[1], cap, head_rad, axis, hd.RED)

    def bands(x, y, m):
        u = (x - e0[0]) * ax + (y - e0[1]) * ay
        v = (x - e0[0]) * nx + (y - e0[1]) * ny
        bulge = cap * math.sqrt(max(0.0, 1.0 - (v / head_rad) ** 2))
        for at in (head_len * 0.18, head_len * 0.8):
            if abs(u - (at + bulge)) < 5.5:
                return hd.METAL
        return m
    c.recolor(bands)
    c.part()
    c.ellipse_rot(e1[0], e1[1], cap, head_rad, axis, hd.YELLOW)
    c.recolor(lambda x, y, m: hd.GOLD if ((x - e1[0]) * ax + (y - e1[1]) * ay) ** 2 / (cap * 0.55) ** 2
              + ((x - e1[0]) * nx + (y - e1[1]) * ny) ** 2 / (head_rad * 0.6) ** 2 <= 1.0 else m)


def eye(c, x, y, look, mood):
    """One eye: white, a big pupil looking `look` px right, a catchlight, a
    painted star of greasepaint above and a teardrop below."""
    c.part(line=False)                                   # clown greasepaint round the eye
    c.poly([(x - 3, y - 18), (x + 3, y - 18), (x, y - 32)], hd.MAKEUP)
    c.poly([(x - 3, y + 17), (x + 3, y + 17), (x, y + 27)], hd.MAKEUP)
    if mood == "closed":                                  # a happy squint: an upturned arc
        c.part(line=False)
        c.stroke(bezier((x - 11, y + 3), (x, y - 9), (x + 11, y + 3)), 2.6, hd.INK)
        return
    c.part()
    c.ellipse(x, y, 11, 15.5, hd.EYE)
    c.part(line=False)
    c.ellipse(x + look, y + 2, 6.5, 10, hd.INK)
    c.recolor(lambda xx, yy, m: hd.EYE if (xx - x - look - 2.5) ** 2 + (yy - y + 3.5) ** 2 < 6.5 else m)


def portrait(mats, mood):
    c = HDCanvas(W, H, 0, 18)
    hx, hy = 122, 132                                     # head centre

    mallet(c, 214, 276, 96, 192)                          # over the right shoulder, face out

    # hair, behind the head: big springy curls either side
    for x, y, r in ((62, 102, 24), (48, 132, 26), (56, 164, 22), (74, 188, 16),
                    (182, 100, 22), (196, 128, 24), (190, 160, 20), (174, 184, 15)):
        c.part()
        c.ellipse(x, y, r, r * 0.92, hd.HAIR)

    # shoulders: the harlequin suit, a pompom button
    c.part()
    c.ellipse(122, 298, 120, 86, hd.SUIT)

    def harlequin(x, y, m):
        u, v = (x + y) / 18.0, (x - y) / 18.0
        return hd.DIAMOND if (math.floor(u) + math.floor(v)) % 2 == 0 else m
    c.recolor(harlequin)

    # the ruff: a collar of white and yellow pleats
    for i, x in enumerate(range(46, 206, 16)):
        c.part()
        c.ellipse(x, 199 + abs(x - 124) * 0.14, 17, 13, hd.WHITE if i % 2 == 0 else hd.YELLOW)

    # head
    c.part()
    c.ellipse(hx, hy, 64, 68, hd.SKIN)
    c.recolor(lambda x, y, m: hd.WHITE if ((x - hx - 4) / 44) ** 2 + ((y - hy - 34) / 26) ** 2 <= 1 else m)
    for ex in (58, 186):                                  # ears, half under the hair
        c.part()
        c.ellipse(ex, hy + 8, 10, 15, hd.SKIN)

    # front curls over the ears
    for x, y, r in ((52, 112, 14), (192, 110, 13)):
        c.part()
        c.ellipse(x, y, r, r, hd.HAIR)

    # brows
    lift = -6 if mood in ("shout", "beam") else 0
    for bx, tilt in ((100, -1), (148, 1)):
        c.part(line=False)
        c.stroke(bezier((bx - 15, hy - 34 + lift + tilt * 2), (bx, hy - 48 + lift), (bx + 15, hy - 34 + lift - tilt * 2)), 2.8, hd.INK)

    # eyes
    if mood == "beam":
        eye(c, 100, hy - 14, 0, "closed")
        eye(c, 148, hy - 14, 0, "closed")
    elif mood == "wink":
        eye(c, 100, hy - 14, 4, "open")
        eye(c, 148, hy - 14, 0, "closed")
    else:
        eye(c, 100, hy - 14, 4, "open")
        eye(c, 148, hy - 14, 4, "open")

    # cheeks
    for cx in (78, 168):
        c.part(line=False)
        c.ellipse(cx, hy + 16, 12, 8, hd.BLUSH)

    # mouth
    my = hy + 34
    if mood == "shout":                                   # a big round holler
        c.part()
        c.ellipse(hx + 4, my + 8, 22, 24, MOUTH)
        c.part(line=False)
        c.ellipse(hx + 4, my + 22, 14, 8, TONGUE)
        c.part(line=False)
        c.poly([(hx - 12, my - 10), (hx + 20, my - 10), (hx + 18, my - 3), (hx - 10, my - 3)], TOOTH)
    else:
        wide = 40 if mood == "beam" else 34
        c.part()
        c.poly([(hx - wide + 4, my - 4)] + bezier((hx - wide + 4, my - 4), (hx + 4, my + 46), (hx + wide + 4, my - 6), 16)[1:]
               + [(hx + 4, my + 2)], MOUTH)
        c.part(line=False)
        c.ellipse(hx + 6, my + 18, 16, 8, TONGUE)
        c.part(line=False)
        c.poly([(hx - wide + 10, my - 3), (hx + wide - 2, my - 5), (hx + wide - 6, my + 4), (hx - wide + 14, my + 5)], TOOTH)
    c.part(line=False)                                    # painted lips round the whole grin
    c.stroke(bezier((hx - 44, my - 8), (hx + 4, my - 2), (hx + 50, my - 10)), 2.4, hd.LIP)

    # nose
    c.part()
    c.ellipse(hx + 6, hy + 8, 19, 18, hd.NOSE)
    c.part(line=False)
    c.ellipse(hx - 1, hy + 1, 5, 4, hd.WHITE)

    # hat: a striped cone tipped back, a dotted brim, a pompom
    c.part()
    c.poly([(58, 72), (186, 66), (160, 36), (124, 4), (96, 26)], hd.RED)
    c.recolor(lambda x, y, m: hd.YELLOW if math.floor((x + y * 0.75) / 12.0) % 3 == 0 else m)
    c.part()
    c.ellipse_rot(122, 69, 68, 12, -0.05, hd.YELLOW)
    c.recolor(lambda x, y, m: hd.WHITE if abs(y - 69 + (x - 122) * 0.05) < 2.2 and math.floor(x) % 12 < 3 else m)
    c.part()
    c.ellipse(124, 10, 10, 10, hd.WHITE)
    for a in range(0, 360, 60):
        c.ellipse(124 + 8 * math.cos(math.radians(a)), 10 + 8 * math.sin(math.radians(a)), 5.5, 5.5, hd.WHITE)

    # the gloved fist on the mallet handle, a frilled cuff
    c.part()
    c.capsule(250, 330, 228, 294, 19, 16, hd.SUIT)
    hd.frill(c, 226, 290, 15, False)
    c.part()
    c.ellipse(214, 274, 20, 18, hd.WHITE)
    c.part()
    c.ellipse(204, 262, 8, 6.5, hd.WHITE)                 # thumb over the grip
    return c


MOODS = {"pip": "grin", "poppy": "wink", "bingo": "shout", "dot": "beam"}


def main():
    sets = {"pip": hd.MATS}
    sets.update(hd.SQUAD)
    for name, base in sets.items():
        c = portrait(mats_for(base), MOODS[name])
        px = shade(c, mats_for(base))
        out = os.path.join("assets", "sprites", f"portrait_{name}.png")
        write_png(out, W, H, to_rows([px], W, H))
        print("wrote", out)


if __name__ == "__main__":
    main()
