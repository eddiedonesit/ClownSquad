"""The hand-drawn parts every generator shares, read from Aseprite.

Source: art/aseprite/clown_head.aseprite, one layer per part, exported to
art/aseprite/export/ by tools/aseprite/export_layers.lua (refresh_sprites.sh
runs it). Every layer shares one anchor: a head centre at pixel (56, 24), on a
head of radius 18.5 (the squad's), facing right.

  hair_back, hair_front  shapes only: each colour is one curl. stamp_hair()
                         paints them into a canvas in any hair material, as one
                         part per curl, so the generator shades each curl as a
                         puff of its own, at any head size, either way round.
  eye_open, eye_hit, mouth_hit, blush, nose_shine
                         finished colours, painted over a shaded frame
                         (gen_sprite_hd.hand_face).
"""
import math
import os

from aseprite_import import read_png

PARTS_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "art", "aseprite", "export")
HEAD_AX, HEAD_AY = 56, 24
HEAD_R = 18.5


def _layer(name):
    """The opaque pixels of one layer: [(x, y, rgba)] relative to the anchor."""
    w, h, rows = read_png(os.path.join(PARTS_DIR, "clown_head_" + name + ".png"))
    return [(x - HEAD_AX, y - HEAD_AY, rows[y][x]) for y in range(h) for x in range(w) if rows[y][x][3] > 0]


def _groups(pixels):
    """Pixels grouped by colour, in order of first appearance: one group per curl."""
    out = {}
    for x, y, c in pixels:
        out.setdefault(c, []).append((x, y))
    return list(out.values())


HEAD = {n: _layer(n) for n in ("hair_back", "hair_front", "eye_open", "eye_hit", "mouth_hit", "blush", "nose_shine")}
HAIR_BACK = _groups(HEAD["hair_back"])
HAIR_FRONT = _groups(HEAD["hair_front"])


def stamp_hair(c, groups, x, y, r=HEAD_R, material=None, mirror=False, scale=None):
    """Paint hair shapes into an HDCanvas as one part per curl.

    (x, y) is the head anchor in the canvas's local units (the head centre),
    r the head's radius: the drawing is scaled from the squad's 18.5 to fit.
    mirror flips it for a head facing left (or the far side of a face seen
    from the front). scale overrides the size outright."""
    s = scale if scale is not None else r / HEAD_R
    lookup = {}
    for gi, group in enumerate(groups):
        for px, py in group:
            lookup[(px, py)] = gi
    xs = [px for px, _ in lookup]
    ys = [py for _, py in lookup]
    lx0, lx1 = (min(xs), max(xs) + 1) if not mirror else (-max(xs) - 1, -min(xs))
    ly0, ly1 = min(ys), max(ys) + 1
    X0 = max(0, int(math.floor(x + lx0 * s)) + c.ox)
    X1 = min(c.w - 1, int(math.ceil(x + lx1 * s)) + c.ox)
    Y0 = max(0, int(math.floor(y + ly0 * s)) + c.oy)
    Y1 = min(c.h - 1, int(math.ceil(y + ly1 * s)) + c.oy)
    per = [[] for _ in groups]
    for Y in range(Y0, Y1 + 1):
        for X in range(X0, X1 + 1):
            rx = (X - c.ox + 0.5 - x) / s
            ry = (Y - c.oy + 0.5 - y) / s
            if mirror:
                rx = -rx
            gi = lookup.get((int(math.floor(rx)), int(math.floor(ry))))
            if gi is not None:
                per[gi].append((X, Y))
    for pixels in per:
        if not pixels:
            continue
        c.part()
        for X, Y in pixels:
            c._set(X, Y, material)
