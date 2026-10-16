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
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier, rotated, laid_down, crop, touches_border, pack_frames
from pixelpng import write_png

BODY_W, BODY_H = 96, 128
TOP_PAD, BOTTOM_PAD = 88, 72
W, H = 208, TOP_PAD + BODY_H + BOTTOM_PAD
DRAW_X, DRAW_Y = 56, TOP_PAD
BASELINE = TOP_PAD + BODY_H

# ---------------------------------------------------------------- palette

SUIT, DIAMOND, RED, YELLOW, WHITE, SKIN, HAIR, NOSE = 1, 2, 3, 4, 5, 6, 7, 8
INK, EYE, LIP, BLUSH, MAKEUP, SOLE, WOOD, METAL, BELT, GOLD = 9, 10, 11, 12, 13, 14, 15, 16, 17, 18
IRON, CHICK, BEAK, COMB, DARKWOOD = 19, 20, 21, 22, 23
CRUST, BALLOON, CORK, CREAM = 24, 25, 26, 27

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
    IRON: Material((70, 66, 84), gloss=True),
    CHICK: Material((250, 214, 70), soft=True),
    BEAK: Material((240, 140, 40)),
    COMB: Material((220, 40, 50)),
    DARKWOOD: Material((110, 64, 40)),
    CRUST: Material((222, 164, 92)),
    BALLOON: Material((236, 70, 170), gloss=True),
    CORK: Material((196, 150, 100)),
    CREAM: Material((252, 246, 232), soft=True),
}

# Skin, by hand: the generated ramp's shadow tone came out a grey-brown that read
# as dirt on the faces. These shadows lean warm and rosy instead (darkest first;
# the darkest is only used for lines, skin being a soft material).
MATS[SKIN].tones = [(150, 84, 82), (226, 160, 138), (250, 218, 192), (255, 232, 211), (255, 243, 231)]

# --------------------------------------------------------- Aseprite parts
# The hair and PIP's face pieces are drawn in Aseprite and shared by every
# generator: see clown_parts.py (art/aseprite/clown_head.aseprite).
from clown_parts import HEAD, HAIR_BACK, HAIR_FRONT, stamp_hair


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
    """The squeaky mallet's barrel, drawn as a real cylinder: straight sides,
    a foreshortened oval end at the back, the yellow striking face as an oval
    at the front, and rubber bands that curve round the barrel the way a band
    on a drum does. The curvature is what makes it read as round, not flat.
    Drawn before the body when carried, so it rests behind the head."""
    r = math.radians(angle)
    mx, my = hx + math.cos(r) * length, hy - math.sin(r) * length
    ax, ay = math.cos(r + math.pi / 2), -math.sin(r + math.pi / 2)      # along the barrel
    nx, ny = math.cos(r), -math.sin(r)                                    # across it
    half = head_len / 2
    # The camera looks down on the ring, so the end face that shows is
    # whichever end of the barrel is higher on screen.
    if my + ay * half > my - ay * half:
        ax, ay = -ax, -ay
    e0 = (mx - ax * half, my - ay * half)
    e1 = (mx + ax * half, my + ay * half)
    axis_angle = math.atan2(ay, ax)
    cap = head_rad * 0.42                                                # how much of the end face shows

    c.part()
    c.poly([(e0[0] + nx * head_rad, e0[1] + ny * head_rad), (e1[0] + nx * head_rad, e1[1] + ny * head_rad),
            (e1[0] - nx * head_rad, e1[1] - ny * head_rad), (e0[0] - nx * head_rad, e0[1] - ny * head_rad)], RED)
    c.ellipse_rot(e0[0], e0[1], cap, head_rad, axis_angle, RED)          # the far end, rounding the back

    def bands(x, y, m):
        u = (x - e0[0]) * ax + (y - e0[1]) * ay                        # along the barrel
        v = (x - e0[0]) * nx + (y - e0[1]) * ny                        # across it
        bulge = cap * math.sqrt(max(0.0, 1.0 - (v / head_rad) ** 2))   # a band follows the end's curve
        for at in (head_len * 0.2, head_len * 0.78):
            if abs(u - (at + bulge)) < 2.4:
                return METAL
        return m
    c.recolor(bands)

    c.part()
    c.ellipse_rot(e1[0], e1[1], cap, head_rad, axis_angle, YELLOW)       # the striking face
    c.recolor(lambda x, y, m: GOLD if ((x - e1[0]) * ax + (y - e1[1]) * ay) ** 2 / (cap * 0.55) ** 2
              + ((x - e1[0]) * nx + (y - e1[1]) * ny) ** 2 / (head_rad * 0.6) ** 2 <= 1.0 else m)


def mallet_handle(c, hx, hy, angle, length):
    r = math.radians(angle)
    c.part()
    c.capsule(hx, hy, hx + math.cos(r) * length, hy - math.sin(r) * length, 2.6, 2.6, WOOD)


# ----------------------------------------------------------------- weapons
# The squad's three weapons share every pose; only what is in the hand
# changes. WEAPON is set per sheet before the frames are drawn.

WEAPON = "mallet"


def hammer_head(c, hx, hy, angle, length):
    """BIG BERTHA: a sledgehammer. A long rectangular iron block across the end
    of the handle, square faces at both ends, brass bands, a bevelled edge:
    nothing like the round squeaky mallet."""
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)               # along the handle
    ax, ay = -uy, ux                                 # along the head (across the handle)
    L = length - 2
    mx, my = hx + ux * L, hy + uy * L                # the head's middle, at the handle's end
    half, thick = 24, 13                             # the block: 48 long, 26 deep
    corners = [(mx + ax * half + ux * thick, my + ay * half + uy * thick),
               (mx - ax * half + ux * thick, my - ay * half + uy * thick),
               (mx - ax * half - ux * thick, my - ay * half - uy * thick),
               (mx + ax * half - ux * thick, my + ay * half - uy * thick)]
    c.part()
    c.poly(corners, IRON)

    def detail(x, y, m):
        a = (x - mx) * ax + (y - my) * ay            # along the head
        b = (x - mx) * ux + (y - my) * uy            # across it
        if abs(abs(a) - half * 0.55) < 2.6:
            return GOLD                               # brass bands near each end
        if abs(a) > half - 4:
            return METAL                              # polished striking faces
        if abs(a) < half * 0.55 - 2.6:
            # the middle is painted in the clown's colour, like a fairground
            # strength tester's mallet, with a white star stamped on it
            ang = math.atan2(b, a)
            if math.hypot(a, b) < 7.5 * (0.55 + 0.45 * abs(math.cos(2.5 * ang))):
                return WHITE
            return RED
        return m
    c.recolor(detail)


def hammer_handle(c, hx, hy, angle, length):
    r = math.radians(angle)
    c.part()
    c.capsule(hx, hy, hx + math.cos(r) * (length - 2), hy - math.sin(r) * (length - 2), 3.4, 3.4, DARKWOOD)
    # a fairground handle: barber-pole stripes above the grip
    c.recolor(lambda x, y, m: (GOLD if abs(((x - hx) * math.cos(r) - (y - hy) * math.sin(r)) - 4) < 1.5
                               else WHITE if ((x - hx) * math.cos(r) - (y - hy) * math.sin(r)) > 10
                               and math.floor(((x - hx) * math.cos(r) - (y - hy) * math.sin(r)
                                               + ((x - hx) * math.sin(r) + (y - hy) * math.cos(r)) * 1.2) / 4) % 2 == 0
                               else m))


def chicken_body(c, hx, hy, angle, length):
    """The RUBBER CHICKEN, held by the feet: a long floppy yellow body, a head with
    a red comb and an orange beak, little wings."""
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)
    # a plump body, a thin rubbery neck, a big head
    bx, by = hx + ux * length * 0.55, hy + uy * length * 0.55
    c.part()
    c.ellipse_rot(bx, by, length * 0.36, 12, math.atan2(uy, ux), CHICK)
    px, py = -uy, ux                      # across the body, toward the top of the screen
    if py > 0:
        px, py = -px, -py
    c.part()
    c.ellipse_rot(bx + px * 6, by + py * 6, 9, 5, math.atan2(uy, ux), CHICK)          # a wing on top
    nx0, ny0 = bx + ux * length * 0.3, by + uy * length * 0.3
    hcx, hcy = hx + ux * (length + 8), hy + uy * (length + 8)
    c.part()
    c.capsule(nx0, ny0, hcx - ux * 6, hcy - uy * 6, 4.5, 3.5, CHICK)
    c.part()
    c.ellipse(hcx, hcy, 9, 9, CHICK)
    # comb on top of the head, beak sticking out ahead
    c.part()
    for k in (-4, 0, 4):
        c.ellipse(hcx + px * 9 + ux * k, hcy + py * 9 + uy * k, 3.6, 3.6, COMB)
    c.part()
    c.poly([(hcx + ux * 6 - px * 3, hcy + uy * 6 - py * 3), (hcx + ux * 16, hcy + uy * 16),
            (hcx + ux * 6 + px * 3, hcy + uy * 6 + py * 3)], BEAK)
    c.part(line=False)
    c.ellipse(hcx + ux * 2 + px * 3, hcy + uy * 2 + py * 3, 1.8, 1.8, INK)


def chicken_legs(c, hx, hy, angle, length):
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)
    for side in (-1.5, 1.5):
        c.part()
        c.capsule(hx - uy * side, hy + ux * side, hx + ux * length * 0.3 - uy * side, hy + uy * length * 0.3 + ux * side, 1.4, 1.4, BEAK)


def axis(angle):
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)
    px, py = -uy, ux                       # across, toward the top of the screen
    if py > 0:
        px, py = -px, -py
    return ux, uy, px, py


def pie_head(c, hx, hy, angle, length):
    """A CREAM PIE balanced on the palm: a tin, a crust, a dome of cream, a cherry.
    It stays level whatever the arm does, the way a waiter carries a plate."""
    ux, uy, _, _ = axis(angle)
    x, y = hx + ux * 10, hy + uy * 10 - 6
    c.part()
    c.ellipse(x, y + 3, 17, 4.5, METAL)                 # the tin
    c.part()
    c.ellipse(x, y, 16, 5.5, CRUST)                     # the crust rim
    c.recolor(lambda xx, yy, m: CRUST if abs(math.sin(xx * 0.9)) > 0.6 else m)
    c.part()
    c.ellipse(x, y - 5, 13, 8, CREAM)                   # the cream
    for k in (-8, 0, 8):
        c.ellipse(x + k, y - 4, 4.5, 4.5, CREAM)        # piped swirls round the edge
    c.part()
    c.ellipse(x + 1, y - 13, 3.6, 3.6, NOSE)            # the cherry


def pie_handle(c, hx, hy, angle, length):
    pass


def pin_head(c, hx, hy, angle, length):
    """A juggling PIN held by its neck: a white bottle-shaped club, red bands."""
    ux, uy, px, py = axis(angle)
    L = length * 1.05
    bx, by = hx + ux * L * 0.72, hy + uy * L * 0.72
    c.part()
    c.ellipse_rot(bx, by, L * 0.34, 9.5, math.atan2(uy, ux), WHITE)
    c.part()
    c.capsule(hx + ux * L * 0.2, hy + uy * L * 0.2, bx - ux * L * 0.15, by - uy * L * 0.15, 3.6, 6, WHITE)

    def bands(x, y, m):
        u = (x - hx) * ux + (y - hy) * uy
        return RED if abs(u - L * 0.42) < 2.2 or abs(u - L * 0.5) < 1.6 else m
    c.recolor(bands)


def pin_handle(c, hx, hy, angle, length):
    ux, uy, _, _ = axis(angle)
    c.part()
    c.capsule(hx - ux * 5, hy - uy * 5, hx + ux * length * 0.25, hy + uy * length * 0.25, 3.2, 3.2, WHITE)
    c.part()
    c.ellipse(hx - ux * 6, hy - uy * 6, 4.4, 4.4, WHITE)    # the knob


def slapstick_head(c, hx, hy, angle, length):
    """The SLAPSTICK: two long flat slats on one handle, a gap between them that
    claps shut on impact. One slat yellow, one red."""
    ux, uy, px, py = axis(angle)
    a0, a1 = length * 0.3, length * 1.05
    for side, mat in ((-3.2, YELLOW), (3.2, RED)):
        c.part()
        c.capsule(hx + ux * a0 + px * side, hy + uy * a0 + py * side,
                  hx + ux * a1 + px * side, hy + uy * a1 + py * side, 2.8, 3.0, mat)


def slapstick_handle(c, hx, hy, angle, length):
    ux, uy, _, _ = axis(angle)
    c.part()
    c.capsule(hx - ux * 4, hy - uy * 4, hx + ux * length * 0.32, hy + uy * length * 0.32, 3.4, 3.4, WOOD)
    c.recolor(lambda x, y, m: GOLD if abs((x - hx) * ux + (y - hy) * uy - length * 0.3) < 1.8 else m)


def popgun_head(c, hx, hy, angle, length):
    """A toy POP GUN, the classic kind: a slim tin barrel striped like a
    candy cane, a flared muzzle with a fat cork plugged in it, tied on by a
    string that droops back to the barrel, and a wooden forestock under the
    barrel where the front hand goes."""
    ux, uy, px, py = axis(angle)
    L = length * 0.95
    at = lambda a, b=0.0: (hx + ux * a + px * b, hy + uy * a + py * b)
    # the wooden forestock along the underside
    c.part()
    c.capsule(*at(1, -2.2), *at(L * 0.42, -2.2), 3.2, 3.0, DARKWOOD)
    # the barrel: thin tin, red and white stripes running round it
    c.part()
    c.capsule(*at(0, 1.2), *at(L - 4, 1.2), 2.6, 2.6, METAL)
    c.recolor(lambda x, y, m: RED if m == METAL and math.floor(((x - hx) * ux + (y - hy) * uy + ((x - hx) * px + (y - hy) * py) * 0.8) / 3.0) % 2 == 0 else m)
    # the muzzle: a red flare
    c.part()
    c.capsule(*at(L - 7, 1.2), *at(L, 1.2), 3.4, 6.2, RED)
    c.recolor(lambda x, y, m: YELLOW if abs((x - hx) * ux + (y - hy) * uy - (L - 4.5)) < 1.0 else m)
    # the cork, half out of the muzzle, ready to pop
    c.part()
    cx, cy = at(L + 5, 1.2)
    c.ellipse_rot(cx, cy, 6.0, 7.0, math.atan2(uy, ux), CORK)
    c.part(line=False)
    c.ellipse_rot(cx + ux * 2.4, cy + uy * 2.4, 2.6, 4.6, math.atan2(uy, ux), CREAM)       # the cork's lighter face
    # the string, from the cork back to the barrel, sagging below it
    c.part(line=False)
    c.stroke(bezier((cx, cy), at(L * 0.75, -11), at(L * 0.45, -1)), 0.9, INK)


def popgun_handle(c, hx, hy, angle, length):
    """The rifle half: a trigger and guard under the hand, and a painted
    wooden stock that drops down behind it to a broad butt."""
    ux, uy, px, py = axis(angle)
    at = lambda a, b=0.0: (hx + ux * a + px * b, hy + uy * a + py * b)
    # trigger guard and trigger
    c.part(line=False)
    c.stroke([at(2, -1), at(1, -6), at(-4, -6), at(-5, -2)], 0.8, INK)
    c.part(line=False)
    c.stroke([at(-1, -1.5), at(-2, -4.5)], 0.8, INK)
    # the stock: wrist behind the hand, widening and dropping to the butt
    c.part()
    c.poly([at(-3, 3.4), at(-22, 2.6), at(-24, -9.5), at(-20, -10), at(-6, -4), at(-3, -2.5)], DARKWOOD)
    c.recolor(lambda x, y, m: YELLOW if m == DARKWOOD and abs((x - hx) * ux + (y - hy) * uy + 16) < 1.1 else m)   # a painted band
    # the butt plate
    c.part()
    c.poly([at(-21.5, 2.8), at(-24.5, 2.2), at(-26.5, -9.8), at(-23.5, -10.2)], RED)


def balloon_head(c, hx, hy, angle, length):
    """A BALLOON SWORD, shaped like a sword: a long straight blade of twisted
    balloon tapering to a point, a crossguard of two round balloons, a short
    grip and a pommel bulb. Longer than any other weapon."""
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)
    px, py = -uy, ux
    # the blade's tip, from the grip: a touch shorter swung right back or held straight out, to stay in frame
    L = length * (1.5 if 15 < angle < 100 else 1.32)
    g = 7                                             # where the blade starts (above the guard)
    # the blade: overlapping capsules, each a twist, narrowing toward the point
    n = 6
    seg = (L - g) / n
    for k in range(n):
        a0, a1 = g + seg * k, g + seg * (k + 1)
        w0 = 6.4 - 2.8 * k / n
        w1 = 6.4 - 2.8 * (k + 1) / n
        c.part()
        c.capsule(hx + ux * a0, hy + uy * a0, hx + ux * a1, hy + uy * a1, w0, w1, BALLOON)
    # the point
    c.part()
    tip = L + 7
    c.poly([(hx + ux * L + px * 3.4, hy + uy * L + py * 3.4), (hx + ux * tip, hy + uy * tip),
            (hx + ux * L - px * 3.4, hy + uy * L - py * 3.4)], BALLOON)
    # the twists between segments, and a shine along the blade's lit edge
    def twist(x, y, m):
        a = (x - hx) * ux + (y - hy) * uy
        b = (x - hx) * px + (y - hy) * py
        if m != BALLOON or a < g:
            return m
        if abs(((a - g) % seg) - seg) < 1.2 or ((a - g) % seg) < 0.8:
            return RED                                # the knot where each twist pinches
        if 0.8 < b < 2.0 and a < L:
            return WHITE                              # the shine
        return m
    c.recolor(twist)


def balloon_handle(c, hx, hy, angle, length):
    r = math.radians(angle)
    ux, uy = math.cos(r), -math.sin(r)
    px, py = -uy, ux
    # crossguard: a balloon bar across the blade, round at both ends
    c.part()
    c.capsule(hx + ux * 6 + px * 13, hy + uy * 6 + py * 13, hx + ux * 6 - px * 13, hy + uy * 6 - py * 13, 4.4, 4.4, YELLOW)
    c.part()
    c.ellipse(hx + ux * 6 + px * 14, hy + uy * 6 + py * 14, 5.4, 5.4, YELLOW)
    c.part()
    c.ellipse(hx + ux * 6 - px * 14, hy + uy * 6 - py * 14, 5.4, 5.4, YELLOW)
    # grip and pommel behind the hand
    c.part()
    c.capsule(hx - ux * 2, hy - uy * 2, hx - ux * 8, hy - uy * 8, 2.6, 2.6, BALLOON)
    c.part()
    c.ellipse(hx - ux * 10, hy - uy * 10, 3.6, 3.6, YELLOW)


HEADS = {"hammer": hammer_head, "chicken": chicken_body, "pie": pie_head, "pins": pin_head,
         "slapstick": slapstick_head, "popgun": popgun_head, "balloon": balloon_head}
HANDLES = {"hammer": hammer_handle, "chicken": chicken_legs, "pie": pie_handle, "pins": pin_handle,
           "slapstick": slapstick_handle, "popgun": popgun_handle, "balloon": balloon_handle}


def weapon_head(c, hx, hy, angle, length):
    HEADS.get(WEAPON, mallet_head)(c, hx, hy, angle, length)


def weapon_handle(c, hx, hy, angle, length):
    HANDLES.get(WEAPON, mallet_handle)(c, hx, hy, angle, length)


# -------------------------------------------------------------------- pose

CARRY = (143, 29)          # resting back over the shoulder (original units)
DODGE_CARRY = (112, 24)    # steeper and closer, so the deep slide stays on canvas

# Some weapons are not carried on the shoulder: the pop gun is held at the
# ready, the pie on the palm, the balloon sword out in front.
CARRY_FOR = {"popgun": (12, 24), "pie": (20, 14), "balloon": (28, 26)}
DODGE_CARRY_FOR = {"popgun": (6, 22), "pie": (25, 14), "balloon": (15, 24)}


def carry():
    return CARRY_FOR.get(WEAPON, CARRY)


def dodge_carry():
    return DODGE_CARRY_FOR.get(WEAPON, DODGE_CARRY)

POSE = dict(by=0, ln=0, hd=0, nl=0, fl=0, nfoot=0, ffoot=0, na=0, fa=0, lift=0, mallet=None,
            hair=(0, 0), pom=(0, 0))


def pose(**over):
    p = dict(POSE)
    p.update(over)
    return p


def old(mallet=None, body_dy=0, lean=0, head_dy=0, near_leg=0, far_leg=0,
        near_arm=0, far_arm=0, arm_lift=0, nfoot=0, ffoot=0, hair=(0, 0), pom=(0, 0)):
    """A pose written in the original sheet's units, doubled. Every clip keeps
    the timing and staging that was already tuned against the game code; only
    the drawing changes.

    hair and pom are secondary motion: how far the puff of hair and the hat's
    pompom lag behind the head, (dx, dy), so they trail a run, float on the
    way up a jump, flop on landing and whip on a hit."""
    m = None if mallet is None else (mallet[0], mallet[1] * 2)
    return pose(by=body_dy * 2, ln=lean * 2, hd=head_dy * 2, nl=near_leg * 2, fl=far_leg * 2,
                na=near_arm * 2, fa=far_arm * 2, lift=arm_lift * 2, nfoot=nfoot, ffoot=ffoot, mallet=m,
                hair=(hair[0] * 2, hair[1] * 2), pom=(pom[0] * 1.2, pom[1] * 1.2))   # the pompom stays on its tip


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
    c.face = (int(round(DRAW_X + 50 + ln)), int(round(DRAW_Y + hy)))  # head anchor, for the hand-drawn face (hand_face)

    if behind:
        weapon_head(c, gx, gy, m[0], m[1])

    # clown hair, drawn in Aseprite (hair_back): a big puff of curls bursting
    # out from under the hat, each curl its own part so it shades as a bumpy cloud
    hdx, hdy = p["hair"]
    stamp_hair(c, HAIR_BACK, 50 + ln + hdx, hy + hdy, material=HAIR)

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
    stamp_hair(c, HAIR_FRONT, 50 + ln + hdx * 0.5, hy + hdy * 0.5, material=HAIR)          # tufts over the ear (Aseprite: hair_front)

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
    px_, py_ = hx - 5 + p["pom"][0], hy - 45 + p["pom"][1]
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
        weapon_handle(c, gx, gy, m[0], m[1])
    fist(c, gx, gy, False)
    if m is not None and not behind:
        weapon_head(c, gx, gy, m[0], m[1])
    return c


# ------------------------------------------------------------------ clips
# Mirrors tools/gen_sprite.py clip for clip, so either sheet drives the same code.

def idle_cycle(n=8):
    """A slow breath: settle down and back up, in n in-betweens."""
    out = []
    for i in range(n):
        b = 0.5 - 0.5 * math.cos(2 * math.pi * i / n)
        lag = 0.5 - 0.5 * math.cos(2 * math.pi * i / n - 0.9) - b     # hair and pompom follow a beat late
        out.append(("idle", dict(body_dy=b, head_dy=b, arm_lift=-b, hair=(0, lag * 1.2), pom=(0, lag * 1.6))))
    return out


def walk_cycle(n=8):
    """A full stride as a continuous cycle, so it can be cut into as many
    in-betweens as smoothness wants. Both feet lift on their forward swing."""
    out = []
    for i in range(n):
        t = 2 * math.pi * i / n
        stride = math.cos(t)
        bounce = abs(math.cos(t - 0.8)) - abs(stride)                  # the bob, a beat late
        out.append(("walk", dict(near_leg=6 * stride, far_leg=-6 * stride,
                                 near_arm=-2 * stride, far_arm=3 * stride,
                                 body_dy=-abs(stride), lean=abs(stride),
                                 nfoot=5 * max(0.0, -math.sin(t)), ffoot=5 * max(0.0, math.sin(t)),
                                 hair=(-1, -bounce * 1.3), pom=(-1.5 + 0.8 * math.sin(t), -bounce * 1.8))))
    return out


# Hand-keyed brawlers live on in-betweens; these two loops play all the time.
FPS = {"idle": 10, "walk": 22, "empty_idle": 10, "empty_walk": 22, "hit": 8, "empty_hit": 8, "victory": 4}

BASE = idle_cycle() + walk_cycle() + [
    # the hit whips the hair and pompom forward as the head snaps back
    ("hit", dict(lean=-4, head_dy=-2, body_dy=2, near_arm=-7, far_arm=-5, arm_lift=-2, hair=(2, -2), pom=(3, -2))),
    ("hit", dict(lean=-2, head_dy=-1, body_dy=1, near_arm=-4, far_arm=-3, arm_lift=-1, hair=(-1, 1), pom=(-1, 1.5))),   # catching himself
    ("dodge", dict(body_dy=8, lean=6, head_dy=3, near_leg=12, far_leg=-6,
                   near_arm=-10, far_arm=-7, arm_lift=-6, hair=(-1.5, -1.5), pom=(-2, -2))),
    ("dodge", dict(body_dy=15, lean=10, head_dy=6, near_leg=20, far_leg=-10,
                   near_arm=-15, far_arm=-11, arm_lift=-11, hair=(-2, -2.5), pom=(-3, -3))),
    ("dodge", dict(body_dy=8, lean=5, head_dy=3, near_leg=11, far_leg=-5,
                   near_arm=-9, far_arm=-6, arm_lift=-6, hair=(-1, 1), pom=(-1, 1.5))),
    # rising: the puff drags behind and below; at the top it floats up
    ("jump", dict(body_dy=-2, lean=2, near_leg=5, far_leg=-3, near_arm=-6, far_arm=-4, arm_lift=6, hair=(-1, 2), pom=(-1, 3))),
    ("jump", dict(lean=-1, near_leg=-4, far_leg=6, near_arm=4, far_arm=-3, arm_lift=2, hair=(0, -1.5), pom=(0, -2.5))),
]

HIT_POSE = dict(lean=-4, head_dy=-2, body_dy=2, near_arm=-7, far_arm=-5, arm_lift=-2)


def player_frames():
    frames = []
    add = lambda name, cv: frames.append((name, cv))
    for name, kw in BASE:
        add(name, draw(old(mallet=dodge_carry() if name == "dodge" else carry(), **kw)))

    if WEAPON == "popgun":
        # attack: shouldered and aimed down the lane, a kick of recoil on the shot
        add("attack", draw(old(lean=-1, near_arm=6, far_arm=7, arm_lift=8, mallet=(6, 26))))
        add("attack", draw(old(lean=0, near_arm=8, far_arm=8, arm_lift=8, mallet=(2, 26))))
        add("attack", draw(old(lean=-3, near_arm=5, far_arm=6, arm_lift=10, mallet=(16, 24))))
        add("attack", draw(old(lean=-1, near_arm=7, far_arm=7, arm_lift=8, mallet=(4, 26))))
    elif WEAPON == "pie":
        # attack: the pie held back, then shoved forward into a face
        add("attack", draw(old(lean=-3, body_dy=1, near_arm=-8, far_arm=-4, arm_lift=6, mallet=(140, 14))))
        add("attack", draw(old(lean=0, near_arm=-2, far_arm=-1, arm_lift=8, mallet=(90, 14))))
        add("attack", draw(old(lean=4, near_arm=10, far_arm=4, arm_lift=8, mallet=(10, 14))))
        add("attack", draw(old(lean=5, body_dy=1, near_arm=13, far_arm=5, arm_lift=6, mallet=(0, 14))))
    else:
        # attack: mallet arcs from behind the head, out to 45 degrees, down to ground
        add("attack", draw(old(lean=-3, body_dy=1, near_arm=-6, far_arm=-4, arm_lift=5, mallet=(165, 26))))
        add("attack", draw(old(lean=0, body_dy=-1, near_arm=-2, far_arm=-1, arm_lift=9, mallet=(120, 30))))
        add("attack", draw(old(lean=4, body_dy=1, near_arm=8, far_arm=4, arm_lift=3, mallet=(22, 30))))
        add("attack", draw(old(lean=5, body_dy=3, near_arm=10, far_arm=5, arm_lift=-3, mallet=(-14, 27), near_leg=3, far_leg=-2)))

    # The combo's hits get swings of their own (the overhead "attack" above is
    # kept for the moves that borrow it: the ground pound, the slam).
    if WEAPON == "popgun":
        # from the hip, then from the shoulder
        add("attack1", draw(old(lean=0, body_dy=1, near_arm=8, far_arm=6, arm_lift=3, mallet=(-4, 26))))
        add("attack1", draw(old(lean=1, body_dy=1, near_arm=9, far_arm=7, arm_lift=3, mallet=(-6, 26))))
        add("attack1", draw(old(lean=-2, body_dy=1, near_arm=7, far_arm=6, arm_lift=5, mallet=(6, 24))))
        add("attack1", draw(old(lean=0, body_dy=1, near_arm=8, far_arm=6, arm_lift=3, mallet=(-4, 26))))
        add("attack2", draw(old(lean=-1, near_arm=7, far_arm=7, arm_lift=11, mallet=(10, 26))))
        add("attack2", draw(old(lean=0, near_arm=8, far_arm=8, arm_lift=12, mallet=(8, 26))))
        add("attack2", draw(old(lean=-3, near_arm=6, far_arm=6, arm_lift=13, mallet=(22, 24))))
        add("attack2", draw(old(lean=-1, near_arm=7, far_arm=7, arm_lift=12, mallet=(10, 26))))
    elif WEAPON == "pie":
        # an underhand lob, then a sidearm fling
        add("attack1", draw(old(lean=-2, body_dy=2, near_arm=-6, far_arm=-3, arm_lift=-2, mallet=(-60, 14))))
        add("attack1", draw(old(lean=1, body_dy=1, near_arm=2, far_arm=0, arm_lift=0, mallet=(-20, 14))))
        add("attack1", draw(old(lean=4, body_dy=2, near_arm=13, far_arm=4, arm_lift=-4, mallet=(20, 14))))
        add("attack1", draw(old(lean=5, body_dy=1, near_arm=14, far_arm=5, arm_lift=-1, mallet=(40, 14))))
        add("attack2", draw(old(lean=-3, near_arm=-10, far_arm=-5, arm_lift=8, mallet=(170, 14))))
        add("attack2", draw(old(lean=0, near_arm=0, far_arm=-2, arm_lift=9, mallet=(120, 14))))
        add("attack2", draw(old(lean=2, body_dy=-1, near_arm=7, far_arm=4, arm_lift=15, mallet=(5, 14))))
        add("attack2", draw(old(lean=4, near_arm=10, far_arm=5, arm_lift=13, mallet=(-5, 14))))
    else:
        # Melee: swipe across, backhand swipe back, then swipe across again, hard.
        # Seen side-on a flat swing runs through depth: the weapon is drawn
        # back at shoulder height, comes round toward the camera (foreshortened,
        # short), lands out in front and carries on past.
        # hit 1, the swipe: from behind the shoulder, round the front
        add("attack1", draw(old(lean=-3, body_dy=1, near_arm=-8, far_arm=-3, arm_lift=6, near_leg=-2, far_leg=2, mallet=(176, 26))))
        add("attack1", draw(old(lean=0, body_dy=1, near_arm=-1, far_arm=0, arm_lift=8, mallet=(78, 13))))
        add("attack1", draw(old(lean=4, body_dy=1, near_arm=10, far_arm=3, arm_lift=7, near_leg=3, far_leg=-2, mallet=(4, 29))))
        add("attack1", draw(old(lean=5, body_dy=1, near_arm=12, far_arm=4, arm_lift=5, near_leg=4, far_leg=-2, mallet=(-14, 22))))
        # hit 2, the backhand: wound across the body to the far side, back the other way
        add("attack2", draw(old(lean=3, body_dy=2, near_arm=-11, far_arm=6, arm_lift=2, near_leg=3, far_leg=-2, mallet=(198, 18))))
        add("attack2", draw(old(lean=1, body_dy=1, near_arm=-2, far_arm=2, arm_lift=6, mallet=(92, 12))))
        add("attack2", draw(old(lean=3, body_dy=0, near_arm=9, far_arm=-6, arm_lift=10, near_leg=2, far_leg=-3, mallet=(16, 28))))
        add("attack2", draw(old(lean=1, body_dy=-1, near_arm=10, far_arm=-8, arm_lift=13, mallet=(42, 24))))
    # hit 3, the power hit: the swipe again, wound right back and put everything into it
    if WEAPON not in ("popgun", "pie"):
        add("attack3", draw(old(lean=-5, body_dy=3, near_arm=-10, far_arm=-4, arm_lift=7, near_leg=-3, far_leg=4, mallet=(182, 28))))
        add("attack3", draw(old(lean=0, body_dy=2, near_arm=-1, far_arm=0, arm_lift=8, near_leg=1, far_leg=1, mallet=(76, 14))))
        add("attack3", draw(old(lean=6, body_dy=2, near_arm=12, far_arm=3, arm_lift=7, near_leg=6, far_leg=-4, mallet=(2, 31))))
        add("attack3", draw(old(lean=7, body_dy=3, near_arm=13, far_arm=4, arm_lift=5, near_leg=6, far_leg=-4, mallet=(-18, 24))))

    # charge: coiled low with the mallet drawn right back, two frames to shudder
    add("charge", draw(old(body_dy=5, lean=-4, head_dy=1, near_leg=5, far_leg=-5,
                           near_arm=-9, far_arm=-6, arm_lift=7, mallet=(168, 28))))
    add("charge", draw(old(body_dy=6, lean=-5, head_dy=2, near_leg=6, far_leg=-5,
                           near_arm=-10, far_arm=-7, arm_lift=8, mallet=(160, 28))))

    # spin: mallet straight out; the engine mirrors these rapidly into a whirl
    add("spin", draw(old(body_dy=2, lean=2, near_leg=4, far_leg=-4,
                         near_arm=10, far_arm=-8, arm_lift=10, mallet=(6, 22 if WEAPON == "chicken" else 28))))
    add("spin", draw(old(body_dy=3, lean=3, near_leg=-3, far_leg=4,
                         near_arm=10, far_arm=-8, arm_lift=9, mallet=(-6, 22 if WEAPON == "chicken" else 28))))

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

    # landing: a squash on touching down
    add("land", draw(old(mallet=carry(), body_dy=6, head_dy=3, near_leg=6, far_leg=-6,
                         near_arm=-4, far_arm=-3, arm_lift=-3, hair=(0, 2.5), pom=(1, 3.5))))

    # air dash: stretched out, leaning into it, legs trailing
    add("dash", draw(old(mallet=(5, 26), lean=9, body_dy=3, head_dy=1, near_leg=-12, far_leg=-16,
                         near_arm=9, far_arm=-9, arm_lift=6, hair=(-3.5, -0.5), pom=(-4, 0.5))))

    # slam: weapon raised high for the dive, then smashed into the floor
    add("slam", draw(old(mallet=(78, 28), lean=1, body_dy=-2, head_dy=-1, near_leg=4, far_leg=-3,
                         near_arm=12, far_arm=2, arm_lift=15, hair=(0, -2.5), pom=(0, -3.5))))
    add("slam", draw(old(mallet=(-25, 26), lean=5, body_dy=7, head_dy=3, near_leg=9, far_leg=-7,
                         near_arm=10, far_arm=6, arm_lift=-4, hair=(0.5, 2.5), pom=(1, 3.5))))

    # victory: weapon thrown up in the air, bouncing on the spot
    add("victory", draw(old(mallet=(80, 29), lean=-2, body_dy=-1, head_dy=-2, near_leg=2, far_leg=-2,
                            near_arm=12, far_arm=-6, arm_lift=15, hair=(0, 1.5), pom=(0, 2))))
    add("victory", draw(old(mallet=(86, 29), lean=-1, body_dy=1, head_dy=0, near_leg=-2, far_leg=2,
                            near_arm=11, far_arm=-5, arm_lift=13, hair=(0, -1.5), pom=(0, -2))))

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
    weapon_handle(loose, 24, 64, 0, 44)
    weapon_head(loose, 24, 64, 0, 44)
    cx, cy = 52 + DRAW_X, 64 + DRAW_Y
    out = []
    for step in range(8):
        cv = crop(rotated(loose, step * 45, cx, cy), cx - MS // 2, cy - MS // 2, MS, MS)
        if touches_border(cv):
            print("WARNING: mallet projectile frame {} is clipped".format(step))
        out.append(shade(cv, MATS, normals=True))
    return out, MS


# ------------------------------------------------------------- the face
# Hand-drawn, pixel by pixel, over the shaded frame: the generator's face is
# fine in shape but muddy in detail. Coordinates are relative to the head
# anchor (draw(): x = 50 + lean, y = head centre). Rotated and floored frames
# have no anchor and keep the generated face.

FACE_CLOWNS = ("pip",)          # who has the hand-drawn face so far

INK_C = (34, 22, 40)
EYE_W = (252, 252, 255)
SKIN_T = MATS[SKIN].tones       # darkest first
BLUSH_C = (244, 150, 160)
BLUSH_L = (255, 183, 188)
NOSE_HI = (255, 160, 156)
NOSE_GLINT = (255, 236, 232)
MOUTH_D = (90, 9, 35)
TONGUE = (216, 68, 81)
LIP_C = (196, 36, 58)

def _is_skin(px):
    return px[3] > 0 and tuple(px[:3]) in SKIN_T


def hand_face(rows, anchor, expression):
    """Paint the hand-drawn face onto shaded RGBA rows (a list of lists of tuples)."""
    ax, ay = anchor
    h, w = len(rows), len(rows[0])

    def put(x, y, rgb):
        if 0 <= x < w and 0 <= y < h:
            rows[y][x] = tuple(rgb) + (255,)

    # 1. Clean cel shading on the skin: one base tone, a small soft light up
    #    front, and a thin shadow just inside the jaw and against the hair.
    #    No more mottling. Decided on a copy so the edges do not creep.
    skin = {(x, y) for y in range(ay - 14, ay + 20) for x in range(ax - 18, ax + 20)
            if 0 <= x < w and 0 <= y < h and _is_skin(rows[y][x])}
    for (x, y) in skin:
        dx, dy = x - ax, y - ay
        tone = SKIN_T[2]
        r = ((dx - 2) / 5.0) ** 2 + ((dy + 3) / 3.2) ** 2
        if r <= 1.0 or (r <= 1.5 and (x + y) % 2 == 0):    # the light, its edge dithered
            tone = SKIN_T[3]
        if dy > 3 and (x, y + 2) not in skin and (x, y + 1) in skin:
            tone = SKIN_T[1]                              # just above the jaw line
        if dx < -6 and ((x - 2, y) not in skin or (x - 1, y) not in skin):
            tone = SKIN_T[1]                              # against the hair
        put(x, y, tone)

    # 2. The pieces drawn in Aseprite: the blush (on skin only), the nose shine,
    #    and the eye (open, or squeezed shut and an "ouch" mouth when hit).
    for x, y, c in HEAD["blush"]:
        if 0 <= ax + x < w and 0 <= ay + y < h and _is_skin(rows[ay + y][ax + x]):
            put(ax + x, ay + y, c[:3])
    for x, y, c in HEAD["nose_shine"]:
        put(ax + x, ay + y, c[:3])
    if expression == "hit":
        # skin over the whole eye area (the generator's eye too), then the squeezed lines
        ex0 = min(x for x, _, _ in HEAD["eye_open"]) - 1
        ex1 = max(x for x, _, _ in HEAD["eye_open"]) + 1
        ey0 = min(y for _, y, _ in HEAD["eye_open"])
        ey1 = max(y for _, y, _ in HEAD["eye_open"]) + 2
        for y in range(ay + ey0, ay + ey1 + 1):
            for x in range(ax + ex0, ax + ex1 + 1):
                if 0 <= x < w and 0 <= y < h and rows[y][x][3] > 0:
                    c = tuple(rows[y][x][:3])
                    if c in (EYE_W, INK_C) or c in SKIN_T:
                        put(x, y, SKIN_T[2])
        for x, y, c in HEAD["eye_hit"] + HEAD["mouth_hit"]:
            put(ax + x, ay + y, c[:3])
    else:
        for x, y, c in HEAD["eye_open"]:
            put(ax + x, ay + y, c[:3])


def write_sheet(name, frames, mats):
    clipped = sorted({n for n, cv in frames if touches_border(cv)})
    if clipped:
        print("WARNING: {} art touches the canvas edge in: {}".format(name, ", ".join(clipped)))
    both = [shade(cv, mats, normals=True) for _, cv in frames]
    if name.split("_")[0] in FACE_CLOWNS:
        for (clip, cv), (px, _) in zip(frames, both):
            if getattr(cv, "face", None):
                hand_face(px, cv.face, "hit" if clip.endswith("hit") else "")
    shaded = [p for p, _ in both]
    out = os.path.join("assets", "sprites", name + ".png")
    # Trimmed and packed into rows (pixelart_hd.pack_frames): a one-row strip of
    # this many 208-wide frames would pass the 16384-pixel texture limit, and
    # most of every frame is empty padding. rects puts each frame back in place.
    sw, sh, rows, nrows, rects = pack_frames(shaded, [n for _, n in both], W, H)
    write_png(out, sw, sh, rows)
    write_png(os.path.join("assets", "sprites", name + "_n.png"), sw, sh, nrows)
    clips = {}
    for i, (n, _) in enumerate(frames):
        clips.setdefault(n, []).append(i)
    with open(os.path.join("assets", "sprites", name + ".frames.json"), "w", newline=chr(10)) as f:
        json.dump({"frame_width": W, "frame_height": H, "baseline": BASELINE,
                   "count": len(shaded), "clips": clips, "fps": FPS, "rects": rects}, f, indent=1)
    print("wrote {} (+ normal map)  {}x{} packed  {} frames ({:.0%} of a strip)".format(out, sw, sh, len(shaded), sw * sh / (W * len(shaded) * H)))


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
    "bingo": squad_mats((236, 140, 56), (70, 130, 230), (40, 150, 110), (232, 52, 52)),     # classic clown red
    "dot": squad_mats((244, 204, 70), (214, 56, 66), (60, 60, 160), (246, 96, 176)),        # hot pink
}


# Each clown with each weapon. The mallet sheets keep their original names;
# the others carry the weapon: pip_hammer_hd.png, pip_chicken_hd.png.
WEAPONS = ("mallet", "hammer", "chicken", "pie", "pins", "slapstick", "popgun", "balloon")


def sheet_name(clown, weapon):
    return clown + ("_hd" if weapon == "mallet" else "_" + weapon + "_hd")


def spin_name(weapon):
    return weapon + "_spin_hd"


def render_weapon(weapon):
    global WEAPON
    WEAPON = weapon
    frames = player_frames()
    write_sheet(sheet_name("pip", weapon), frames, MATS)
    for name, mats in SQUAD.items():
        write_sheet(sheet_name(name, weapon), frames, mats)
    spin, ms = mallet_frames()
    out = os.path.join("assets", "sprites", spin_name(weapon) + ".png")
    write_png(out, ms * len(spin), ms, to_rows([p for p, _ in spin], ms, ms))
    write_png(out.replace(".png", "_n.png"), ms * len(spin), ms, to_rows([n for _, n in spin], ms, ms))
    with open(out.replace(".png", ".frames.json"), "w") as f:
        json.dump({"frame_width": ms, "frame_height": ms, "count": len(spin),
                   "clips": {"mallet_spin": list(range(len(spin)))}}, f, indent=2)
    print("wrote {}  {} frames".format(out, len(spin)))
    return weapon


def main():
    from multiprocessing import Pool
    weapons = [w for w in sys.argv[1:] if w in WEAPONS] or list(WEAPONS)
    with Pool(min(len(weapons), os.cpu_count() or 4)) as pool:
        pool.map(render_weapon, weapons)
    if "mallet" in weapons:
        write_sheet("grunt_hd", grunt_frames(), GRUNT_MATS)


if __name__ == "__main__":
    main()
