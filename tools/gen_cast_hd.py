"""The new cast: six enemy types and nine bosses, in the HD pipeline.

One flexible humanoid rig draws most of them: body build, outfit pattern,
face, hair, hat, legs (feet, stilts or a unicycle), and whatever they hold
(pins, torches, a whip and chair, a barbell, balloons, a cane), posed by
shoulder and elbow angles. The tiger has a rig of its own; the balloon dog,
the clown car and the ringmaster's carriage are drawn by hand.

Every sheet has the clips the game expects: idle, walk, attack (4 frames, in
step with the swing), hit, down, air, plus each character's specials. Same
pixel density as the squad (a normal-sized enemy is 128 px tall), shaded and
normal-mapped by pixelart_hd.py.

Writes assets/sprites/cast/<name>.png, _n.png and .frames.json.
Run: python tools/gen_cast_hd.py [names...]   (bash tools/refresh_sprites.sh runs it)
"""
import json
import math
import os
import sys
from multiprocessing import Pool

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pixelart_hd import HDCanvas, Material, shade, to_rows, bezier, rotated, laid_down, touches_border
from clown_parts import HAIR_BACK, HAIR_FRONT, stamp_hair
from pixelpng import write_png

OUT = os.path.join("assets", "sprites", "cast")

# ------------------------------------------------------------- materials
(SUIT, ACCENT, TRIM, SKIN, HAIR, NOSE, INK, EYE, LIP, BLUSH, WHITE, SHOE, SOLE, WOOD, METAL, GOLD,
 FLAME, FLAME2, HATC, PANTS, BALLOON, MOUTH, TOOTH, FUR, STRIPE, BELLY, CART, CART2, BLACK) = range(1, 30)
FRECK = 30          # freckles, stubble, a scar: per character (spec "freck")


def mats(spec):
    s = spec
    return {
        SUIT: Material(s.get("suit", (70, 108, 210))),
        ACCENT: Material(s.get("accent", (246, 200, 84))),
        TRIM: Material(s.get("trim", (236, 236, 246)), soft=True),
        SKIN: Material(s.get("skin", (250, 218, 192)), soft=True, cool=0.97),
        FRECK: Material(s.get("freck", (190, 110, 80))),
        HAIR: Material(s.get("hair", (238, 132, 58))),
        NOSE: Material(s.get("nose_col", (232, 52, 60)), gloss=True),
        INK: Material((34, 22, 40), flat=True),
        EYE: Material((252, 252, 255), flat=True),
        LIP: Material(s.get("lip", (196, 36, 58))),
        BLUSH: Material((244, 150, 160)),
        WHITE: Material((236, 236, 246), soft=True),
        SHOE: Material(s.get("shoe", (218, 56, 68))),
        SOLE: Material((74, 58, 82)),
        WOOD: Material((176, 102, 56)),
        METAL: Material((196, 202, 216), gloss=True),
        GOLD: Material((252, 204, 72), gloss=True),
        FLAME: Material((255, 150, 40), flat=True),
        FLAME2: Material((255, 236, 120), flat=True),
        HATC: Material(s.get("hat_col", (218, 56, 68))),
        PANTS: Material(s.get("pants", s.get("suit", (70, 108, 210)))),
        BALLOON: Material(s.get("balloon", (240, 80, 120)), gloss=True),
        MOUTH: Material((120, 24, 44)),
        TOOTH: Material((250, 250, 255), soft=True),
        FUR: Material(s.get("fur", (236, 140, 50))),
        STRIPE: Material(s.get("stripe", (40, 30, 40))),
        BELLY: Material(s.get("belly", (250, 240, 220)), soft=True),
        CART: Material(s.get("cart", (190, 40, 60))),
        CART2: Material(s.get("cart2", (252, 204, 72)), gloss=True),
        BLACK: Material((44, 36, 52)),
    }


def rad(d):
    return math.radians(d)


def limb_end(x, y, angle, length):
    """Angle 0 hangs straight down, 90 points forward (+x), 180 straight up."""
    return x + math.sin(rad(angle)) * length, y + math.cos(rad(angle)) * length


# ------------------------------------------------------------------ items

def pin(c, x, y, ang, k):
    """A juggling pin, white with red bands, its body along `ang` (degrees, 0 = down)."""
    ux, uy = math.sin(rad(ang)), math.cos(rad(ang))
    c.part()
    c.ellipse_rot(x + ux * 9 * k, y + uy * 9 * k, 7 * k, 4.2 * k, math.atan2(uy, ux), WHITE)
    c.capsule(x, y, x + ux * 6 * k, y + uy * 6 * k, 1.8 * k, 2.4 * k, WHITE)
    c.recolor(lambda xx, yy, m: SHOE if abs(((xx - x) * ux + (yy - y) * uy) - 9 * k) < 1.6 * k else m)


def torch(c, x, y, ang, k, lit=True, big=1.0):
    ux, uy = math.sin(rad(ang)), math.cos(rad(ang))
    c.part()
    c.capsule(x - ux * 4 * k, y - uy * 4 * k, x + ux * 14 * k, y + uy * 14 * k, 1.6 * k, 2.2 * k, WOOD)
    if lit:
        fx, fy = x + ux * 18 * k, y + uy * 18 * k
        c.part(line=False)
        c.ellipse(fx, fy - 5 * k * big, 5.5 * k * big, 9 * k * big, FLAME)
        c.part(line=False)
        c.ellipse(fx, fy - 3 * k * big, 3 * k * big, 5 * k * big, FLAME2)


def whip(c, x, y, mode, k, facing=1):
    """A whip from the hand: coiled at the hip, drawn back, or cracking out long."""
    c.part()
    c.capsule(x, y, x + 6 * k * facing, y - 5 * k, 2 * k, 1.6 * k, BLACK)
    hx, hy = x + 6 * k * facing, y - 5 * k
    if mode == "coil":
        for r in (6, 9):
            c.part(line=False)
            c.stroke([(hx + math.cos(t / 8 * math.tau) * r * k, hy + 10 * k + math.sin(t / 8 * math.tau) * r * 0.6 * k) for t in range(9)], 0.9 * k, WOOD)
    elif mode == "back":
        c.part(line=False)
        c.stroke(bezier((hx, hy), (hx - 30 * k, hy - 40 * k), (hx - 60 * k, hy - 10 * k), 16), 1.1 * k, WOOD)
    else:   # crack: out in front, long, with a snap at the tip
        c.part(line=False)
        pts = bezier((hx, hy), (hx + 40 * k, hy - 22 * k), (hx + 88 * k, hy + 6 * k), 18)
        c.stroke(pts, 1.1 * k, WOOD)
        tx, ty = pts[-1]
        c.part(line=False)
        for a in range(0, 360, 60):
            c.stroke([(tx, ty), (tx + math.cos(rad(a)) * 5 * k, ty + math.sin(rad(a)) * 5 * k)], 0.7 * k, FLAME2)


def barbell(c, x0, y0, x1, y1, k):
    c.part()
    c.capsule(x0, y0, x1, y1, 1.7 * k, 1.7 * k, METAL)
    for x, y in ((x0, y0), (x1, y1)):
        c.part()
        c.ellipse(x, y, 5 * k, 13 * k, BLACK)
        c.part()
        c.ellipse(x, y, 2 * k, 5 * k, METAL)


def dumbbell(c, x, y, k):
    c.part()
    c.capsule(x - 7 * k, y, x + 7 * k, y, 1.4 * k, 1.4 * k, METAL)
    for dx in (-7, 7):
        c.part()
        c.ellipse(x + dx * k, y, 3.2 * k, 6 * k, BLACK)


def balloon(c, x, y, k, col_off=0, n=3):
    for i in range(n):
        bx, by = x - 6 * k + i * 6 * k, y - 32 * k - (i % 2) * 7 * k
        c.part(line=False)
        c.stroke([(x, y), (bx, by + 9 * k)], 0.5 * k, WHITE)
        c.part()
        c.ellipse(bx, by, 6.5 * k, 8.5 * k, BALLOON if (i + col_off) % 2 == 0 else ACCENT)


def cane(c, x, y, ang, k):
    ux, uy = math.sin(rad(ang)), math.cos(rad(ang))
    c.part()
    c.capsule(x - ux * 12 * k, y - uy * 12 * k, x + ux * 22 * k, y + uy * 22 * k, 1.5 * k, 1.5 * k, BLACK)
    c.part()
    c.ellipse(x - ux * 13 * k, y - uy * 13 * k, 3 * k, 3 * k, GOLD)


def chair(c, x, y, k):
    """The lion tamer's chair, held out legs first."""
    c.part()
    c.poly([(x, y - 10 * k), (x + 4 * k, y - 10 * k), (x + 4 * k, y + 8 * k), (x, y + 8 * k)], WOOD)
    for dy in (-8, 0, 7):
        c.part()
        c.capsule(x + 2 * k, y + dy * k, x + 22 * k, y + (dy - 3) * k, 1.4 * k, 1.4 * k, WOOD)
    c.part()
    c.poly([(x - 2 * k, y - 4 * k), (x + 6 * k, y - 4 * k), (x + 6 * k, y + 2 * k), (x - 2 * k, y + 2 * k)], SHOE)


def hoop(c, x, y, k, lit=True):
    for a in range(0, 360, 20):
        px, py = x + math.cos(rad(a)) * 16 * k, y + math.sin(rad(a)) * 19 * k
        c.part(line=False)
        c.ellipse(px, py, 2.2 * k, 2.2 * k, GOLD)
        if lit and a % 40 == 0:
            c.part(line=False)
            c.ellipse(px, py - 3 * k, 2.6 * k, 4 * k, FLAME)


# ------------------------------------------------------------------ rig

BASE_POSE = dict(by=0, lean=0, nf=0, nfl=0, ff=0, ffl=0, na=12, ne=12, fa=-10, fe=10,
                 head_dy=0, mouth="grin", eyes="open", item_n=None, item_f=None, whip="coil",
                 flame=0, pedal=0, juggle=None, arms_up=False, crouch=0, stretch=1.0)


def P(**kw):
    p = dict(BASE_POSE)
    p.update(kw)
    return p


class Rig:
    def __init__(self, spec):
        self.s = spec
        k = spec.get("k", 1.0)
        self.k = k
        self.W = int(spec.get("cw", 220) * k)
        self.H = int(spec.get("ch", 300) * k)
        self.base = self.H - int(40 * k)

    def canvas(self):
        return HDCanvas(self.W, self.H, self.W // 2 + int(self.s.get("shift", 0) * self.k), self.base)

    # -------------------------------------------------------- humanoid
    def draw(self, p):
        s, k = self.s, self.k
        c = self.canvas()
        legs = s.get("legs", "feet")
        leg_len = 35 * k * s.get("leg", 1.0)
        if legs == "stilts":
            leg_len = 35 * k * 2.6
        if legs == "unicycle":
            leg_len = 30 * k
        lift = 0
        wheel_r = 15 * k
        if legs == "unicycle":
            lift = wheel_r * 2 + 8 * k               # the seat sits over the wheel
        crouch = p["crouch"] * k
        hip_y = -leg_len - lift + p["by"] * k + crouch
        torso_h = 26 * k * s.get("torso", 1.0)
        lean = p["lean"]
        lx = math.sin(rad(lean)) * torso_h           # how far the shoulders lean forward
        hip_x = 0
        sh_x, sh_y = hip_x + lx, hip_y - torso_h
        head_r = 18 * k * s.get("head", 1.0)
        hd_x, hd_y = sh_x + 2 * k + lx * 0.3, sh_y - head_r * 0.85 + p["head_dy"] * k

        build = s.get("build", "slim")
        far_sh = (sh_x - 5 * k, sh_y + 4 * k)
        near_sh = (sh_x + 3 * k, sh_y + 5 * k)
        if build == "muscle":
            far_sh = (sh_x - 12 * k, sh_y + 3 * k)
            near_sh = (sh_x + 8 * k, sh_y + 4 * k)

        if s.get("cape"):
            c.part(far=True)
            c.poly([(sh_x - 8 * k, sh_y), (sh_x + 6 * k, sh_y), (hip_x - 18 * k, hip_y + 18 * k), (hip_x - 34 * k, hip_y + 14 * k)], ACCENT)
        if s.get("tails"):
            c.part(far=True)
            c.poly([(hip_x - 10 * k, hip_y - 8 * k), (hip_x + 2 * k, hip_y - 6 * k), (hip_x - 8 * k, hip_y + 22 * k), (hip_x - 20 * k, hip_y + 18 * k)], SUIT)

        # hair behind the head
        self.hair_back(c, hd_x, hd_y, head_r)

        # far arm, far leg (a tone darker)
        self.arm(c, far_sh, p["fa"], p["fe"], p["item_f"], p, far=True)
        if s.get("epaulettes"):
            self.epaulette(c, far_sh, k, far=True)
        self.leg(c, hip_x - 4 * k, hip_y, p["ff"], p["ffl"], legs, p, far=True, leg_len=leg_len, lift=lift)

        if legs == "unicycle":
            self.unicycle(c, hip_x, hip_y, wheel_r, p)

        # torso
        c.part()
        if build == "round":
            c.ellipse(hip_x + lx * 0.5, hip_y - torso_h * 0.42, 19 * k, torso_h * 0.62, SUIT)
        elif build == "muscle":
            c.ellipse(sh_x - 1 * k, sh_y + torso_h * 0.3, 22 * k, torso_h * 0.42, SUIT)
            c.ellipse(hip_x + lx * 0.4, hip_y - torso_h * 0.25, 13 * k, torso_h * 0.42, SUIT)
        elif build == "tiny":
            c.ellipse(hip_x + lx * 0.5, hip_y - torso_h * 0.45, 13 * k, torso_h * 0.6, SUIT)
        else:
            c.ellipse(hip_x + lx * 0.5, hip_y - torso_h * 0.45, 13.5 * k, torso_h * 0.6, SUIT)
        self.pattern(c, hip_x, hip_y, sh_y, k)
        self.costume(c, hip_x, hip_y, sh_x, sh_y, torso_h, lx, k)

        # near leg
        self.leg(c, hip_x + 4 * k, hip_y, p["nf"], p["nfl"], legs, p, far=False, leg_len=leg_len, lift=lift)

        # neck wear
        self.neck(c, sh_x, sh_y, k)

        # head
        self.head(c, hd_x, hd_y, head_r, p)

        # near arm on top
        self.arm(c, near_sh, p["na"], p["ne"], p["item_n"], p, far=False)
        if s.get("epaulettes"):
            self.epaulette(c, near_sh, k, far=False)

        # juggled pins / torches in the air
        if p["juggle"] is not None:
            for i in range(3):
                t = p["juggle"] + i / 3
                ax = hd_x + math.cos(t * math.tau) * 22 * k + 8 * k
                ay = hd_y - head_r - 30 * k - abs(math.sin(t * math.tau)) * 26 * k
                if s.get("juggles") == "torch":
                    torch(c, ax, ay, 180 + t * 360, k * 1.1)
                else:
                    pin(c, ax, ay, t * 360, k * 1.25)
        return c

    def costume(self, c, hip_x, hip_y, sh_x, sh_y, torso_h, lx, k):
        """The costume's details, on the torso (the current part) and over it.

        frogging     gold braid across a jacket front (the ringmaster, the tamer)
        sequins      a scatter of sparkles over the suit (the trapeze act)
        sash         a band from shoulder to hip
        strap        a one-shouldered leotard: bare chest above, one strap (the strongman)
        belt         a wide belt; "champ" a championship belt with a big gold plate"""
        s = self.s
        cx = hip_x + lx * 0.5
        if s.get("strap"):
            # the chest bare above a line, one strap over the far shoulder
            # (the cut runs from the far shoulder down to under the near arm)
            c.recolor(lambda x, y, m: SKIN if y < sh_y + torso_h * 0.12 + (x - cx) * 0.55 else m)
        if s.get("frogging"):
            rows = [sh_y + torso_h * f for f in (0.28, 0.42, 0.56, 0.7)]
            c.recolor(lambda x, y, m: GOLD if x > cx - 5 * k and any(abs(y - r) < 1.0 * k for r in rows) else m)
            c.recolor(lambda x, y, m: GOLD if any((x - cx + 5 * k) ** 2 + (y - r) ** 2 < (1.9 * k) ** 2 for r in rows) else m)
        if s.get("sequins"):
            c.recolor(lambda x, y, m: (TRIM if (math.floor(x / (3 * k)) * 7 + math.floor(y / (3 * k)) * 13) % 11 == 0
                                       else GOLD if (math.floor(x / (3 * k)) * 5 + math.floor(y / (3 * k)) * 3) % 17 == 0 else m)
                      if m == SUIT else m)
        if s.get("suspenders"):
            # a pair of braces down the front (the mime's)
            c.recolor(lambda x, y, m: TRIM if y > sh_y + 2 * k and (abs(x - cx + 3 * k) < 1.4 * k or abs(x - cx - 6 * k) < 1.4 * k) else m)
        if s.get("sash"):
            c.recolor(lambda x, y, m: ACCENT if abs((x - cx) + (y - (sh_y + hip_y) / 2) * 0.9) < 3.2 * k else m)
        belt = s.get("belt")
        if belt:
            by = hip_y - 3 * k
            c.recolor(lambda x, y, m: BLACK if abs(y - by) < (3.6 if belt == "champ" else 2.4) * k else m)
            c.part()
            if belt == "champ":
                c.ellipse(cx + 6 * k, by, 8 * k, 6.5 * k, GOLD)
                c.part(line=False)
                c.ellipse(cx + 6 * k, by, 4.5 * k, 3.6 * k, ACCENT)
            else:
                c.ellipse(cx + 8 * k, by, 3.4 * k, 3 * k, GOLD)

    def epaulette(self, c, sh, k, far):
        """A gold shoulder board with a fringe."""
        x, y = sh
        c.part(far=far)
        c.ellipse(x, y - 1 * k, 7.5 * k, 3.4 * k, GOLD)
        for i in range(5):
            c.part(far=far, line=False)
            c.capsule(x + (i - 2) * 2.8 * k, y + 1 * k, x + (i - 2) * 3.0 * k, y + 5.5 * k, 0.9 * k, 0.9 * k, GOLD)

    def pattern(self, c, hip_x, hip_y, sh_y, k):
        pat = self.s.get("pattern", "plain")
        if pat == "diamond":
            c.recolor(lambda x, y, m: ACCENT if (math.floor((x + y) / (7 * k)) + math.floor((x - y) / (7 * k))) % 2 == 0 else m)
        elif pat == "stripes":
            c.recolor(lambda x, y, m: ACCENT if math.floor(y / (5 * k)) % 2 == 0 else m)
        elif pat == "vstripes":
            c.recolor(lambda x, y, m: ACCENT if math.floor(x / (5 * k)) % 2 == 0 else m)
        elif pat == "dots":
            c.recolor(lambda x, y, m: ACCENT if ((x / k) % 9 - 4.5) ** 2 + ((y / k) % 9 - 4.5) ** 2 < 6 else m)
        elif pat == "spots":   # leopard: dark broken rings round a brown middle
            def rosette(x, y, m):
                u = (x / k + 3 * math.sin(y / (2 * k)) + 7 * (math.floor(y / (12 * k)) % 2)) % 14 - 7
                v = (y / k) % 12 - 6
                d = u * u / 1.4 + v * v
                return ACCENT if d < 4 else STRIPE if d < 10 and math.atan2(v, u) % 2.1 > 0.5 else m
            c.recolor(rosette)
        elif pat == "tux":
            # a white shirt front and gold buttons down the chest
            front = hip_x + 6 * k
            c.recolor(lambda x, y, m: (GOLD if abs(x - front) < 1.6 * k and math.floor(y / (6 * k)) % 2 == 0 else TRIM)
                      if x > front - 3 * k and y < hip_y - 4 * k else m)
        elif pat == "leotard":
            c.recolor(lambda x, y, m: ACCENT if abs(y - (hip_y - 3 * k)) < 2 * k else m)
        elif pat == "vest":
            c.recolor(lambda x, y, m: SKIN if x > hip_x + 3 * k and y < hip_y - 4 * k else m)
        elif pat == "star":
            cx, cy = hip_x + 4 * k, (hip_y + sh_y) / 2
            c.recolor(lambda x, y, m: ACCENT if star_in(x - cx, y - cy, 8 * k) else m)

    def leg(self, c, x, y, foot_dx, foot_lift, legs, p, far, leg_len, lift):
        k, s = self.k, self.s
        if legs == "unicycle":
            # pedalling feet go round the hub
            a = p["pedal"] + (180 if far else 0)
            hub_y = -15 * k
            fx, fy = math.cos(rad(a)) * 7 * k, hub_y + math.sin(rad(a)) * 7 * k
            c.part(far=far)
            c.capsule(x, y, fx, fy - 3 * k, 6 * k, 5 * k, PANTS)
            self.shoe(c, fx, fy, far, small=True)
            return
        fx, fy = x + foot_dx * k, -foot_lift * k
        if legs == "stilts":
            c.part(far=far)
            c.capsule(x, y, fx, fy - 4 * k, 6.5 * k, 4.5 * k, PANTS)
            c.recolor(lambda xx, yy, m: ACCENT if math.floor(yy / (8 * k)) % 2 == 0 else m)
            c.part(far=far)
            c.ellipse(fx + 2 * k, fy - 2 * k, 6 * k, 3 * k, SOLE)
            return
        c.part(far=far)
        width = 8.5 * k if s.get("build") != "muscle" else 10 * k
        c.capsule(x, y, fx, fy - 10 * k, width, width * 0.75, PANTS)
        kx, ky = (x + fx) / 2, (y + fy - 10 * k) / 2           # the knee
        if s.get("jodhpurs"):
            # riding breeches, flared out at the thigh
            c.ellipse(x + (kx - x) * 0.4 - 1.5 * k, y + (ky - y) * 0.45, width * 1.35, width * 1.2, PANTS)
        if s.get("legwear") == "stripes":
            c.recolor(lambda xx, yy, m: ACCENT if math.floor(yy / (5 * k)) % 2 == 0 else m)
        if s.get("flame_hem"):
            # flames licking up from the cuffs
            c.recolor(lambda xx, yy, m: (FLAME2 if yy > fy - 12 * k + 4 * k * math.sin(xx / (2.2 * k)) + 2 * k
                                         else FLAME if yy > fy - 16 * k + 5 * k * math.sin(xx / (2.2 * k)) else m))
        if s.get("riding_boots"):
            # tall polished boots to just under the knee, a gold cuff at the top
            c.recolor(lambda xx, yy, m: SHOE if yy > ky + 2 * k else (GOLD if yy > ky - 0.5 * k else m))
        self.shoe(c, fx + 2 * k, fy, far)

    def shoe(self, c, x, base, far, small=False):
        k = self.k
        style = self.s.get("shoes", "clown")
        c.part(far=far)
        if style == "boot" or small:
            c.ellipse(x + 1 * k, base - 5 * k, 7 * k, 5 * k, SHOE)
            c.capsule(x - 2 * k, base - 12 * k, x - 1 * k, base - 4 * k, 5 * k, 5 * k, SHOE)
        else:
            c.ellipse(x - 2 * k, base - 6 * k, 11 * k, 6 * k, SHOE)
            c.ellipse(x + 8 * k, base - 7 * k, 8.5 * k, 7 * k, SHOE)
        c.recolor(lambda xx, yy, m: SOLE if yy > base - 2.5 * k else m)

    def unicycle(self, c, hip_x, hip_y, r, p):
        k = self.k
        hub = (0, -r)
        c.part()
        c.capsule(hip_x, hip_y + 4 * k, hub[0], hub[1], 1.8 * k, 1.8 * k, METAL)
        c.part()
        c.ellipse(hip_x - 1 * k, hip_y + 2 * k, 9 * k, 3.5 * k, BLACK)
        c.part()
        c.ellipse(hub[0], hub[1], r, r, BLACK)
        c.part()
        c.ellipse(hub[0], hub[1], r * 0.72, r * 0.72, METAL)
        c.recolor(lambda x, y, m: BLACK if ((math.degrees(math.atan2(y - hub[1], x - hub[0])) - p["pedal"]) % 45) < 6 else m)
        c.part()
        c.ellipse(hub[0], hub[1], 3 * k, 3 * k, GOLD)

    def neck(self, c, x, y, k):
        style = self.s.get("neck", "ruff")
        if style == "ruff":
            for i, dx in enumerate(range(-16, 19, 6)):
                c.part()
                c.ellipse(x + dx * k, y + 1 * k + abs(dx) * 0.08 * k, 5.4 * k, 4.4 * k, WHITE if i % 2 == 0 else ACCENT)
        elif style == "bowtie":
            c.part()
            c.poly([(x + 2 * k, y + 2 * k), (x + 10 * k, y - 3 * k), (x + 10 * k, y + 7 * k)], ACCENT)
            c.poly([(x + 2 * k, y + 2 * k), (x - 6 * k, y - 3 * k), (x - 6 * k, y + 7 * k)], ACCENT)
        elif style == "collar":
            c.part()
            c.ellipse(x + 2 * k, y + 1 * k, 12 * k, 4 * k, TRIM)
        elif style == "scarf":
            c.part()
            c.ellipse(x + 1 * k, y + 1 * k, 12 * k, 4.5 * k, ACCENT)
            c.capsule(x - 6 * k, y + 2 * k, x - 14 * k, y + 16 * k, 3 * k, 2 * k, ACCENT)

    def hair_back(self, c, x, y, r):
        k, style = self.k, self.s.get("hair_style", "curls")
        if style == "curls":
            # the clown puff, drawn in Aseprite (clown_parts), scaled to this head
            stamp_hair(c, HAIR_BACK, x, y, r, material=HAIR)
        elif style == "bun":
            c.part()
            c.ellipse(x - r * 0.9, y - r * 0.4, r * 0.45, r * 0.45, HAIR)
        elif style == "long":
            c.part()
            c.ellipse(x - r * 0.5, y + r * 0.3, r * 0.75, r * 1.1, HAIR)

    def head(self, c, x, y, r, p):
        k, s = self.k, self.s
        face = s.get("face", "clown")
        feats = s.get("features", ())
        hw, hh = s.get("head_w", 1.0), s.get("head_h", 1.0)
        c.part()
        c.ellipse(x, y + 1 * k + r * (hh - 1) * 0.5, r * hw, r * 1.02 * hh, SKIN if face != "mime" else WHITE)
        if s.get("jaw"):
            # a square jaw: the lower face filled out to a block
            c.ellipse(x + r * 0.2, y + r * 0.5 * hh, r * 0.85 * hw, r * 0.6, SKIN)
            c.poly([(x - r * 0.3, y + r * 0.3), (x + r * 0.9, y + r * 0.3), (x + r * 0.85, y + r * 1.0 * hh), (x - r * 0.15, y + r * 0.95 * hh)], SKIN)
        if "stubble" in feats:
            c.recolor(lambda xx, yy, m: FRECK if m == SKIN and yy > y + r * 0.35 and xx > x - r * 0.3 and (int(xx / k) + int(yy / k)) % 2 == 0 else m)
        if face == "clown":
            c.recolor(lambda xx, yy, m: WHITE if ((xx - x - r * 0.55) / (r * 0.5)) ** 2 + ((yy - y - r * 0.6) / (r * 0.36)) ** 2 <= 1 else m)
        # ear
        c.part()
        c.ellipse(x - r * 0.7, y + r * 0.2, r * 0.2, r * 0.28, SKIN if face != "mime" else WHITE)
        style = s.get("hair_style", "curls")
        if style == "curls":
            stamp_hair(c, HAIR_FRONT, x, y, r, material=HAIR)
        elif style == "slick":
            c.part()
            c.ellipse(x - r * 0.2, y - r * 0.72, r * 0.95, r * 0.45, HAIR)
        elif style == "mohawk":
            c.part()
            c.poly([(x - r * 0.6, y - r * 0.6), (x + r * 0.4, y - r * 0.9), (x - r * 0.1, y - r * 1.55), (x - r * 0.7, y - r * 1.2)], HAIR)
        elif style == "long":
            c.part()
            c.ellipse(x - r * 0.25, y - r * 0.6, r * 0.9, r * 0.5, HAIR)

        # face
        ex, ey = x + r * 0.55, y - r * 0.2
        if face == "mime":
            c.part(line=False)
            c.poly([(ex, ey - r * 0.45), (ex + r * 0.12, ey), (ex, ey + r * 0.45), (ex - r * 0.12, ey)], INK)
        if "makeup_diamond" in feats:
            # a clown's painted diamond over the eye
            c.part(line=False)
            c.poly([(ex, ey - r * 0.62), (ex + r * 0.28, ey), (ex, ey + r * 0.55), (ex - r * 0.28, ey)], ACCENT)
        c.part(line=False)
        brow = -r * 0.12 if p["eyes"] == "angry" else 0
        bstyle = s.get("brow", "")
        lift = r * 0.14 if bstyle == "high" else 0
        if bstyle == "unibrow":
            c.stroke([(ex - r * 0.8, ey - r * 0.4 - brow * 0.5), (ex + r * 0.3, ey - r * 0.42 + brow)], 2.4 * k, HAIR if s.get("hair_style") != "none" else BLACK)
        elif bstyle != "none":
            c.stroke(bezier((ex - r * 0.35, ey - r * 0.38 - brow - lift), (ex, ey - r * 0.6 - lift * 1.4), (ex + r * 0.3, ey - r * 0.4 + brow - lift)),
                     (2.3 if bstyle == "thick" else 1.1) * k, INK)
        eye = s.get("eye", "")
        if p["eyes"] == "closed":
            c.part(line=False)
            c.stroke(bezier((ex - r * 0.2, ey), (ex, ey - r * 0.15), (ex + r * 0.2, ey)), 1.0 * k, INK)
        elif eye == "dot":
            c.part(line=False)
            c.ellipse(ex + r * 0.05, ey + r * 0.05, r * 0.12, r * 0.17, INK)
        elif eye == "narrow":
            c.part(line=False)
            c.ellipse(ex, ey + r * 0.03, r * 0.2, r * 0.12, EYE)
            c.part(line=False)
            c.ellipse(ex + r * 0.08, ey + r * 0.04, r * 0.08, r * 0.1, INK)
        elif eye == "big":
            c.part(line=False)
            c.ellipse(ex, ey, r * 0.24, r * 0.33, EYE)
            c.part(line=False)
            c.ellipse(ex + r * 0.08, ey + r * 0.05, r * 0.14, r * 0.22, INK)
            c.part(line=False)
            c.ellipse(ex + r * 0.12, ey - r * 0.06, r * 0.05, r * 0.06, EYE)          # the glint
        else:
            c.part(line=False)
            c.ellipse(ex, ey, r * 0.18, r * 0.27, EYE)
            c.part(line=False)
            c.ellipse(ex + r * 0.07, ey + r * 0.04, r * 0.1, r * 0.17, INK)
        if eye == "lids" and p["eyes"] != "closed":
            # heavy lids: cool, unimpressed
            c.part(line=False)
            c.poly([(ex - r * 0.22, ey - r * 0.3), (ex + r * 0.22, ey - r * 0.3), (ex + r * 0.22, ey - r * 0.02), (ex - r * 0.22, ey - r * 0.06)], SKIN)
            c.part(line=False)
            c.stroke([(ex - r * 0.22, ey - r * 0.05), (ex + r * 0.24, ey - r * 0.01)], 1.0 * k, INK)
        if "lashes" in feats and p["eyes"] != "closed":
            c.part(line=False)
            for dx, dy in ((0.1, -0.3), (0.22, -0.22), (0.28, -0.1)):
                c.stroke([(ex + r * dx * 0.8, ey + r * dy * 0.8), (ex + r * (dx + 0.12), ey + r * (dy - 0.1))], 0.9 * k, INK)
        if "monocle" in feats:
            c.part(line=False)
            c.stroke([(ex + math.cos(t / 16 * math.tau) * r * 0.32, ey + math.sin(t / 16 * math.tau) * r * 0.36) for t in range(17)], 0.9 * k, GOLD)
            c.stroke(bezier((ex - r * 0.1, ey + r * 0.36), (ex - r * 0.5, ey + r * 1.0), (x - r * 0.2, y + r * 1.25)), 0.5 * k, GOLD)
        if face != "mime":
            c.part(line=False)
            c.ellipse(x + r * 0.05, y + r * 0.38, r * 0.17, r * 0.12, BLUSH)
        if "freckles" in feats:
            for fx, fy in ((0.0, 0.28), (0.18, 0.22), (0.12, 0.42), (0.32, 0.3), (-0.12, 0.4)):
                c.part(line=False)
                c.ellipse(x + r * fx, y + r * fy, 0.8 * k, 0.8 * k, FRECK)
        if "scar" in feats:
            c.part(line=False)
            c.stroke([(x - r * 0.05, y - r * 0.05), (x + r * 0.35, y + r * 0.5)], 1.0 * k, FRECK)
            for t in (0.3, 0.6):
                px_, py_ = x - r * 0.05 + r * 0.4 * t, y - r * 0.05 + r * 0.55 * t
                c.stroke([(px_ - r * 0.08, py_ + r * 0.05), (px_ + r * 0.08, py_ - r * 0.05)], 0.7 * k, FRECK)
        if "sideburns" in feats:
            c.part()
            c.capsule(x - r * 0.4, y - r * 0.35, x - r * 0.3, y + r * 0.55, r * 0.16, r * 0.12, HAIR if s.get("stache_col") is None else BLACK)
        if "earring" in feats:
            c.part(line=False)
            c.stroke([(x - r * 0.7 + math.cos(t / 10 * math.tau) * r * 0.12, y + r * 0.58 + math.sin(t / 10 * math.tau) * r * 0.14) for t in range(11)], 0.8 * k, GOLD)
        if "beauty_mark" in feats:
            c.part(line=False)
            c.ellipse(x + r * 0.78, y + r * 0.42, 0.9 * k, 0.9 * k, INK)
        # mouth
        mx, my = x + r * 0.55, y + r * 0.62
        if p["mouth"] == "open" or p["mouth"] == "o":
            c.part()
            c.ellipse(mx + r * 0.1, my, r * 0.25, r * (0.25 if p["mouth"] == "o" else 0.3), MOUTH)
        elif p["mouth"] == "flat":
            c.part(line=False)
            c.stroke([(mx - r * 0.3, my), (mx + r * 0.35, my)], 1.4 * k, LIP if face != "mime" else INK)
        else:
            c.part(line=False)
            c.stroke(bezier((mx - r * 0.35, my - r * 0.05), (mx, my + r * 0.3), (mx + r * 0.45, my - r * 0.15)), 1.6 * k, LIP if face != "mime" else INK)
        if p["mouth"] not in ("open", "o", "flat"):
            if "gold_tooth" in feats:
                c.part(line=False)
                c.ellipse(mx + r * 0.08, my + r * 0.12, r * 0.07, r * 0.08, GOLD)
            if "buck_teeth" in feats:
                for dx in (-0.06, 0.08):
                    c.part()
                    c.poly([(mx + r * dx, my + r * 0.08), (mx + r * (dx + 0.12), my + r * 0.08), (mx + r * (dx + 0.11), my + r * 0.3), (mx + r * (dx + 0.01), my + r * 0.3)], TOOTH)
        if "goatee" in feats:
            c.part()
            c.ellipse(mx + r * 0.05, my + r * 0.42, r * 0.17, r * 0.22, HAIR if s.get("stache_col") is None else BLACK)
        if "cleft" in feats:
            c.part(line=False)
            c.stroke([(mx + r * 0.2, my + r * 0.4 * hh), (mx + r * 0.22, my + r * 0.55 * hh)], 0.8 * k, INK)
        if s.get("mustache"):
            col = HAIR if s.get("stache_col") is None else BLACK
            big = 1.2 if s.get("mustache") == "handlebar" else 1.0
            for side in (-1, 1):
                c.part()
                tip = (mx + r * 0.05 + side * r * 0.55 * big, my - r * 0.4 * big)
                c.stroke(bezier((mx + r * 0.05, my - r * 0.25), (mx + r * 0.05 + side * r * 0.4 * big, my - r * 0.12), tip), 2.0 * k, col)
                if big > 1:
                    # the waxed curl at each end
                    c.stroke([(tip[0] + math.cos(t / 6 * math.tau) * 1.6 * k * side, tip[1] - 1.6 * k + math.sin(t / 6 * math.tau) * 1.6 * k)
                              for t in range(5)], 0.8 * k, col)
        nose = s.get("nose_shape", "")
        if s.get("nose", True):
            c.part()
            c.ellipse(x + r * 1.0, y + r * 0.2, r * 0.36, r * 0.34, NOSE)
        elif nose == "hook":
            # long and hooked down at the tip: a schemer's nose
            c.part()
            c.poly([(x + r * 0.8, y - r * 0.2), (x + r * 1.25, y + r * 0.22), (x + r * 1.1, y + r * 0.36), (x + r * 0.85, y + r * 0.3)], SKIN)
        elif nose == "bent":
            # flattened and knocked sideways: a fighter's nose
            c.part()
            c.ellipse(x + r * 0.98, y + r * 0.2, r * 0.24, r * 0.17, SKIN)
            c.part()
            c.ellipse(x + r * 0.9, y + r * 0.02, r * 0.12, r * 0.1, SKIN)
        elif nose == "long":
            c.part()
            c.capsule(x + r * 0.85, y + r * 0.05, x + r * 1.35, y + r * 0.2, r * 0.12, r * 0.14, SKIN)
        elif nose == "bulb":
            c.part()
            c.ellipse(x + r * 1.0, y + r * 0.22, r * 0.27, r * 0.25, SKIN)
        else:
            c.part()
            c.ellipse(x + r * 0.95, y + r * 0.15, r * 0.16, r * 0.2, SKIN)
        # fire breath
        if p["flame"] > 0:
            f = p["flame"]
            for i in range(4):
                fx = mx + r * 0.6 + i * 14 * k * f
                c.part(line=False)
                c.ellipse(fx, my - i * 1.5 * k, (5 + i * 4) * k * f, (4 + i * 3) * k * f, FLAME if i % 2 else FLAME2)
        self.hat(c, x, y, r)

    def hat(self, c, x, y, r):
        k, kind = self.k, self.s.get("hat", "cone")
        # Hats sit on top of the head: the brim rides just above the brows,
        # so the forehead, brows and eyes all show below it.
        top = y - r * 0.84 if kind != "bandana" else y - r * 0.62
        if kind == "cone":
            c.part()
            c.poly([(x - r * 0.8, top), (x + r * 0.8, top), (x + r * 0.4, top - r * 0.8), (x - r * 0.15, top - r * 1.55), (x - r * 0.55, top - r * 0.75)], HATC)
            c.recolor(lambda xx, yy, m: ACCENT if math.floor((xx + yy * 0.75) / (5 * k)) % 3 == 0 else m)
            c.part()
            c.ellipse(x, top, r * 0.9, r * 0.2, ACCENT)
            c.part()
            c.ellipse(x - r * 0.2, top - r * 1.6, r * 0.24, r * 0.24, WHITE)
        elif kind == "top":
            c.part()
            c.poly([(x - r * 0.62, top), (x + r * 0.62, top), (x + r * 0.58, top - r * 1.3), (x - r * 0.58, top - r * 1.3)], HATC)
            c.recolor(lambda xx, yy, m: ACCENT if abs(yy - (top - r * 0.25)) < r * 0.12 else m)
            c.part()
            c.ellipse(x, top, r * 1.0, r * 0.2, HATC)
        elif kind == "beret":
            c.part()
            c.ellipse_rot(x - r * 0.15, top + r * 0.02, r * 0.95, r * 0.34, -0.12, HATC)
            c.part()
            c.ellipse(x - r * 0.15, top - r * 0.34, r * 0.12, r * 0.12, HATC)
        elif kind == "fez":
            c.part()
            c.poly([(x - r * 0.5, top + r * 0.05), (x + r * 0.5, top + r * 0.05), (x + r * 0.4, top - r * 0.75), (x - r * 0.4, top - r * 0.75)], HATC)
            c.part(line=False)
            c.stroke([(x, top - r * 0.75), (x - r * 0.5, top - r * 0.3)], 1.0 * k, ACCENT)
        elif kind == "bowler":
            c.part()
            c.ellipse(x, top - r * 0.2, r * 0.7, r * 0.55, HATC)
            c.part()
            c.ellipse(x, top + r * 0.05, r * 0.95, r * 0.16, HATC)
        elif kind == "bandana":
            c.part()
            c.ellipse(x - r * 0.05, top + r * 0.15, r * 0.98, r * 0.4, HATC)
            c.capsule(x - r * 0.9, top + r * 0.2, x - r * 1.35, top + r * 0.7, 2.5 * k, 1.5 * k, HATC)
        elif kind == "pith":
            c.part()
            c.ellipse(x, top - r * 0.15, r * 0.85, r * 0.6, HATC)
            c.part()
            c.ellipse(x, top + r * 0.1, r * 1.15, r * 0.2, HATC)
            c.recolor(lambda xx, yy, m: ACCENT if abs(yy - top) < r * 0.08 else m)

    def arm(self, c, sh, a, e, item, p, far):
        k, s = self.k, self.s
        sx, sy = sh
        ua, fa_ = 12 * k * s.get("arm", 1.0), 11 * k * s.get("arm", 1.0)
        ex, ey = limb_end(sx, sy, a, ua)
        hx, hy = limb_end(ex, ey, a + e, fa_)
        thick = 5.6 * k if s.get("build") != "muscle" else 8 * k
        c.part(far=far)
        c.capsule(sx, sy, ex, ey, thick, thick * 0.9, SKIN if s.get("bare_arms") else SUIT)
        c.part(far=far)
        c.capsule(ex, ey, hx, hy, thick * 0.9, thick * 0.8, SKIN if s.get("bare_arms") else SUIT)
        if s.get("limb_rings"):
            c.recolor(lambda xx, yy, m: ACCENT if math.floor((xx * math.sin(rad(a + e)) + yy * math.cos(rad(a + e))) / (4 * k)) % 2 == 0 else m)
        if s.get("wristbands"):
            c.part(far=far)
            c.ellipse(hx - math.sin(rad(a + e)) * 3 * k, hy - math.cos(rad(a + e)) * 3 * k, thick * 0.95, 2.4 * k, ACCENT)
        if s.get("cuffs", True) and not s.get("bare_arms"):
            c.part(far=far)
            c.ellipse(hx - math.sin(rad(a + e)) * 2 * k, hy - math.cos(rad(a + e)) * 2 * k, 5 * k, 2.6 * k, WHITE)
        c.part(far=far)
        c.ellipse(hx, hy, 5.5 * k, 5.2 * k, WHITE if s.get("gloves", True) else SKIN)
        fwd = a + e
        if item == "pin":
            pin(c, hx, hy, fwd + 180 if p.get("pin_up") else fwd, k)
        elif item == "torch":
            torch(c, hx, hy, 180 if not p.get("torch_fwd") else fwd, k)
        elif item == "whip":
            whip(c, hx, hy, p["whip"], k)
        elif item == "chair":
            chair(c, hx + 2 * k, hy, k)
        elif item == "dumbbell":
            dumbbell(c, hx, hy, k)
        elif item == "balloon":
            balloon(c, hx, hy, k)
        elif item == "cane":
            cane(c, hx, hy, fwd, k)
        elif item == "hoop":
            hoop(c, hx + 14 * k, hy - 12 * k, k)
        elif item == "barbell":
            # both hands hold one bar: drawn from the near hand, plates far and near
            barbell(c, hx - 34 * k, hy - 2 * k, hx + 10 * k, hy + 1 * k, k)
        elif item == "hat":
            c.part()
            c.poly([(hx - 7 * k, hy - 2 * k), (hx + 7 * k, hy - 2 * k), (hx + 6 * k, hy - 20 * k), (hx - 6 * k, hy - 20 * k)], HATC)
            c.ellipse(hx, hy - 2 * k, 11 * k, 2.5 * k, HATC)


def star_in(dx, dy, r):
    a = math.atan2(dy, dx)
    d = math.hypot(dx, dy)
    edge = r * (0.55 + 0.45 * abs(math.cos(2.5 * a)))
    return d < edge


# ----------------------------------------------------------- the tiger

class Tiger:
    """RAJAH, in profile, facing right: drawn the way a tiger is built.

    A long, low body (about twice as long as it is tall at the shoulder), a
    deep chest under a shoulder hump, the hips a touch higher than the back,
    heavy forelegs on big round paws, hind legs bent back at the hock, and a
    tail nearly as long as the body, ringed, with a black tip. A broad head:
    white cheek ruff, white muzzle and brows, small round ears, amber eyes.
    Stripes are dark wedges that start thick at the spine and taper down the
    flanks, wrapping the haunch and the legs; the belly is white."""

    def __init__(self, spec):
        self.s = spec
        self.k = spec.get("k", 1.0)
        self.W = int(spec.get("cw", 320) * self.k)
        self.H = int(spec.get("ch", 190) * self.k)
        self.base = self.H - int(22 * self.k)

    def canvas(self):
        return HDCanvas(self.W, self.H, self.W // 2 + int(6 * self.k), self.base)

    def draw(self, p):
        k = self.k
        c = self.canvas()
        st = p.get("stretch", 1.0)
        by = (-50 + p.get("by", 0) + p.get("crouch", 0)) * k        # the body's centre line
        sx, sy = 30 * st * k, by - 2 * k                              # the chest
        hx = -40 * st * k                                             # the hip
        top = by - 22 * k                                             # the line of the back

        def body_stripes(x, y, m):
            if m != FUR:
                return m
            v = (y - top) / (40 * k)                                  # 0 at the spine, 1 at the belly
            for i, x0 in enumerate(range(-62, 44, 9)):
                cx = (x0 * st + 3 * math.sin(i * 2.3) - 5 * v) * k
                w = (2.9 - 2.0 * v) * k * (0.8 if i % 3 == 1 else 1.0)
                stop = 0.62 + 0.18 * ((i * 7) % 3) / 2
                if v < stop and abs(x - cx - math.sin(v * 5 + i) * 1.5 * k) < w:
                    return STRIPE
            return m

        # tail: a long sweep from the rump, ringed, with a black tip
        t = p.get("tail", 0)
        pts = bezier((hx - 16 * k, by - 12 * k), (hx - 56 * k, by - 6 * k + t * k), (hx - 70 * k, by - 40 * k + t * 1.4 * k), 14)
        c.part(far=True)
        for i, (a, b) in enumerate(zip(pts, pts[1:])):
            r = (5.2 - 2.0 * i / len(pts)) * k
            c.capsule(a[0], a[1], b[0], b[1], r, r - 0.15 * k, STRIPE if i >= len(pts) - 3 or i % 3 == 2 else FUR)

        # far legs
        self.foreleg(c, sx - 8 * k, by, p.get("ff", 0), p.get("lift_f", 0), True)
        self.hindleg(c, hx - 4 * k, by, p.get("bf", 0), p.get("lift_f", 0), True)

        # body: haunch, belly, chest and the shoulder hump as one shape
        c.part()
        c.poly([(hx - 2 * k, top + 1 * k), (sx, top - 2 * k), (sx + 4 * k, by + 18 * k), (hx, by + 16 * k)], FUR)
        c.ellipse(0, by + 3 * k, 46 * st * k, 17 * k, FUR)
        c.ellipse(sx, sy, 24 * k, 22 * k, FUR)
        c.ellipse(sx - 4 * k, top + 6 * k, 16 * k, 9 * k, FUR)
        c.ellipse(hx, by - 2 * k, 22 * k, 21 * k, FUR)
        c.recolor(lambda x, y, m: BELLY if (y > by + 11 * k and x > hx + 6 * k) or (x > sx + 12 * k and y > sy + 2 * k) else m)
        c.recolor(body_stripes)
        # the haunch: stripes curve round it
        c.recolor(lambda x, y, m: STRIPE if m == FUR and ((x - hx) ** 2 + (y - by) ** 2) < (19 * k) ** 2
                  and abs(math.hypot(x - hx - 6 * k, y - by + 2 * k) % (8 * k) - 4 * k) < 1.3 * k and x < hx + 8 * k else m)

        # near legs
        self.hindleg(c, hx + 2 * k, by, p.get("nb", 0), p.get("lift_n", 0), False)
        self.foreleg(c, sx - 2 * k, by, p.get("nf", 0), p.get("lift_n", 0), False)

        # head
        ha = p.get("head_ang", 0)
        hcx = sx + 30 * k + math.sin(rad(ha)) * 3 * k
        hcy = by - 18 * k - p.get("head_up", 0) * k + p.get("head_dy", 0) * k
        angry = p.get("angry")
        k = self.k * 1.3                                              # a big cat's big head
        # ears: small and round, flattened back when angry
        ex, ey = (hcx - 10 * k, hcy - 11 * k) if angry else (hcx - 6 * k, hcy - 15 * k)
        c.part()
        c.ellipse(ex, ey, 6 * k, 5 * k if angry else 6.5 * k, FUR)
        c.recolor(lambda x, y, m: STRIPE if (x - ex) ** 2 + (y - ey) ** 2 > (4.6 * k) ** 2 else m)
        c.part(line=False)
        c.ellipse(ex + 1 * k, ey + 1 * k, 3 * k, 3.4 * k, BELLY)
        # the white cheek ruff, flaring back, with its own stripes
        c.part()
        c.poly([(hcx - 6 * k, hcy - 4 * k), (hcx - 22 * k, hcy + 4 * k), (hcx - 14 * k, hcy + 8 * k), (hcx - 20 * k, hcy + 14 * k),
                (hcx - 6 * k, hcy + 18 * k), (hcx + 6 * k, hcy + 12 * k)], BELLY)
        c.recolor(lambda x, y, m: STRIPE if abs(((x - hcx) * 0.8 + (y - hcy)) % (7 * k) - 3.5 * k) < 1.0 * k and x < hcx - 12 * k else m)
        # skull and nose bridge
        c.part()
        c.ellipse(hcx, hcy, 16 * k, 14.5 * k, FUR)
        c.capsule(hcx + 2 * k, hcy - 3 * k, hcx + 21 * k, hcy + 0 * k, 7 * k, 5 * k, FUR)
        c.recolor(lambda x, y, m: STRIPE if y < hcy - 6 * k and abs((y - hcy + 15 * k) % (4.5 * k) - 2.2 * k) < 0.9 * k
                  and hcx - 9 * k < x < hcx + 5 * k else m)                     # the forehead's bars
        c.recolor(lambda x, y, m: STRIPE if hcx - 14 * k < x < hcx - 4 * k and abs(((x - hcx) * 0.5 - (y - hcy)) % (6 * k) - 3 * k) < 0.9 * k
                  and y > hcy - 4 * k else m)                                   # down the side of the face
        # white muzzle and chin
        c.part()
        c.ellipse(hcx + 15 * k, hcy + 5 * k, 10 * k, 7.5 * k, BELLY)
        mouth_open = p.get("mouth") == "open"
        if mouth_open:
            c.part()
            c.ellipse(hcx + 15 * k, hcy + 12 * k, 9 * k, 6.5 * k, MOUTH)
            for fx in (10, 19):                                       # fangs, top and bottom
                c.part(line=False)
                c.poly([(hcx + fx * k, hcy + 7 * k), (hcx + (fx + 2.6) * k, hcy + 7 * k), (hcx + (fx + 1.3) * k, hcy + 12 * k)], TOOTH)
            c.part()
            c.ellipse(hcx + 12 * k, hcy + 19 * k, 8 * k, 3.6 * k, BELLY)       # the dropped jaw
            c.part(line=False)
            c.poly([(hcx + 15 * k, hcy + 17 * k), (hcx + 17.5 * k, hcy + 17 * k), (hcx + 16.2 * k, hcy + 13 * k)], TOOTH)
        else:
            c.part()
            c.ellipse(hcx + 11 * k, hcy + 12 * k, 8 * k, 4 * k, BELLY)
            c.part(line=False)
            c.stroke([(hcx + 9 * k, hcy + 9.5 * k), (hcx + 15 * k, hcy + 10.5 * k), (hcx + 22 * k, hcy + 8 * k)], 0.7 * k, INK)
        # nose, whisker spots
        c.part()
        c.poly([(hcx + 21 * k, hcy - 2 * k), (hcx + 26.5 * k, hcy - 1.5 * k), (hcx + 25 * k, hcy + 2.5 * k), (hcx + 22 * k, hcy + 2 * k)], NOSE)
        for wx, wy in ((13, 3), (16, 4), (19, 3), (14.5, 6), (17.5, 6.5)):
            c.part(line=False)
            c.ellipse(hcx + wx * k, hcy + wy * k, 0.7 * k, 0.7 * k, STRIPE)
        # white brow, amber eye, black liner
        c.part(line=False)
        c.ellipse(hcx + 6 * k, hcy - 9 * k, 4.5 * k, 2.2 * k, BELLY)
        c.part(line=False)
        c.ellipse(hcx + 8 * k, hcy - 4 * k, 3 * k, 2.4 * k if angry else 2.8 * k, ACCENT)
        c.part(line=False)
        c.ellipse(hcx + 9 * k, hcy - 4 * k, 1.4 * k, 1.6 * k, INK)
        c.part(line=False)
        c.stroke([(hcx + 11 * k, hcy - 3 * k), (hcx + 14 * k, hcy + 0 * k)], 0.6 * k, INK)
        if angry:
            c.part(line=False)
            c.stroke([(hcx + 3 * k, hcy - 8 * k), (hcx + 11 * k, hcy - 6.2 * k)], 0.9 * k, STRIPE)
        return c

    def foreleg(self, c, x, by, phase, lift, far):
        k = self.k
        fx = x + 6 * k + math.sin(rad(phase)) * 14 * k
        fy = -max(0.0, math.cos(rad(phase))) * lift * k
        ex, ey = x + 1 * k + math.sin(rad(phase)) * 5 * k, by + 18 * k
        c.part(far=far)
        c.capsule(x, by + 2 * k, ex, ey, 11 * k, 8.5 * k, FUR)
        c.capsule(ex, ey, fx, fy - 6 * k, 8.5 * k, 6.5 * k, FUR)
        c.recolor(lambda xx, yy, m: STRIPE if yy < -14 * k and abs((yy + 40 * k) % (8 * k) - 4 * k) < 1.1 * k and xx < ex + 2 * k else m)
        c.part(far=far)
        c.ellipse(fx + 3 * k, fy - 4 * k, 8.5 * k, 4.6 * k, BELLY if not far else FUR)

    def hindleg(self, c, x, by, phase, lift, far):
        k = self.k
        sw = math.sin(rad(phase))
        fx = x + sw * 14 * k
        fy = -max(0.0, math.cos(rad(phase))) * lift * k
        kx, ky = x + 10 * k + sw * 4 * k, by + 16 * k                  # knee, forward
        hx, hy = fx - 7 * k, fy - 15 * k                                # hock, back
        c.part(far=far)
        c.capsule(x, by, kx, ky, 15 * k, 9 * k, FUR)
        c.capsule(kx, ky, hx, hy, 8.5 * k, 6 * k, FUR)
        c.capsule(hx, hy, fx, fy - 5 * k, 6 * k, 5.5 * k, FUR)
        c.recolor(lambda xx, yy, m: STRIPE if by + 6 * k < yy < -10 * k and abs((yy - xx * 0.4) % (8 * k) - 4 * k) < 1.1 * k else m)
        c.part(far=far)
        c.ellipse(fx + 2 * k, fy - 3.6 * k, 7.5 * k, 4 * k, BELLY if not far else FUR)


# ------------------------------------------------------------ hand-drawn

def balloon_dog(p, k=1.0):
    """A BALLOON DOG, the kind twisted from one long balloon: every part a
    shiny sausage-shaped bubble of its own, pinched at the joins: a pointed
    snout, a round head, two long ears, a neck, a body, four stubby legs and a
    little tail, with a marker-pen eye. Each bubble is its own part, so it
    shades and shines as one round balloon.
    p: by (bob), leg (walk swing), wag (tail), bite, float (legs dangling, nose up)."""
    W, H = int(120 * k), int(110 * k)
    c = HDCanvas(W, H, W // 2, H - int(12 * k))
    floating = p.get("float", 0)
    by = -26 * k + p.get("by", 0) * k
    leg = p.get("leg", 0)
    tilt = -10 * floating                              # nose up a little while floating
    pinches = []

    def bubble(cx, cy, rx, ry, deg, far=False, shine=True):
        c.part(far=far)
        c.ellipse_rot(cx, cy, rx, ry, math.radians(deg), BALLOON)
        if shine:
            a = math.radians(deg)
            ux, uy = math.cos(a), math.sin(a)
            # the shine: a short bright streak along the bubble's upper side
            sx, sy = cx - uy * ry * 0.45 - ux * rx * 0.15, cy + ux * ry * 0.45 * -1 - uy * rx * 0.15
            c.part(line=False)
            c.ellipse_rot(sx, sy, max(1.2, rx * 0.4), max(0.8, ry * 0.22), a, WHITE)
        # where it pinches into the next bubble
        a = math.radians(deg)
        pinches.append((cx + math.cos(a) * rx * 0.95, cy + math.sin(a) * rx * 0.95))

    # far legs (a tone darker)
    for lx, ph in ((-12, -leg), (14, leg)):
        dangle = 18 * floating * (1 if lx > 0 else -1)
        bubble(lx * k + math.sin(math.radians(ph)) * 3 * k, by + 13 * k, 9.5 * k, 3.8 * k, 90 + ph * 0.3 + dangle, far=True, shine=False)
    # tail, sticking up and wagging
    wag = p.get("wag", 0)
    bubble(-22 * k, by - 7 * k, 7 * k, 3.4 * k, -125 + wag * 2 - 15 * floating)
    # body
    bubble(0, by + tilt * 0.1 * k, 17 * k, 7.5 * k, tilt * 0.3)
    # neck, up and forward from the chest
    bubble(16 * k, by - 9 * k, 8.5 * k, 5 * k, -65 + tilt)
    # far ear, then the head, the near ear and the snout
    hx, hy = 21 * k, by - 21 * k
    bubble(hx - 6 * k, hy - 9 * k, 9 * k, 3.6 * k, -105 + tilt, far=True, shine=False)
    bubble(hx, hy, 6.5 * k, 6 * k, tilt)
    bubble(hx - 3 * k, hy - 10 * k, 9.5 * k, 3.8 * k, -95 + tilt)
    bite = p.get("bite", 0)
    bubble(hx + 9 * k, hy + 1 * k + bite * 2 * k, 7.5 * k, 4.4 * k, 8 + bite * 18 + tilt)
    # the knot at the tip of the nose
    c.part()
    c.ellipse(hx + 16.5 * k, hy + 2 * k + bite * 4 * k, 1.8 * k, 1.8 * k, BALLOON)
    # near legs
    for lx, ph in ((-15, leg), (11, -leg)):
        dangle = 18 * floating * (1 if lx > 0 else -1)
        bubble(lx * k + math.sin(math.radians(ph)) * 3 * k, by + 14 * k, 10 * k, 4 * k, 90 + ph * 0.3 + dangle)
    # the pinches: little dark twists where bubbles meet
    for x, y in pinches[:-1]:
        c.part(line=False)
        c.ellipse(x, y, 1.1 * k, 1.1 * k, MOUTH)
    # a marker-pen eye
    c.part(line=False)
    c.ellipse(hx + 2 * k, hy - 1.5 * k, 1.5 * k, 1.9 * k, INK)
    return c


def car_clown(c, fx, fy, r, hair, hat, face=1, squish=1.0, o=False):
    """One clown head crammed into the car: the puff, white muzzle, red nose, a hat."""
    c.part(far=False)
    stamp_hair(c, HAIR_BACK, fx, fy, r, material=hair, mirror=face < 0)
    c.part()
    c.ellipse(fx, fy, r, r * squish, SKIN)
    c.recolor(lambda x, y, m: WHITE if ((x - fx - face * r * 0.4) / (r * 0.5)) ** 2 + ((y - fy - r * 0.55 * squish) / (r * 0.35)) ** 2 <= 1 else m)
    c.part()
    c.ellipse(fx + face * r * 0.92, fy + r * 0.15, r * 0.36, r * 0.36, NOSE)
    for ex in ((0.3, 0.62) if o else (0.35,)):
        c.part(line=False)
        c.ellipse(fx + face * r * ex, fy - r * 0.25, r * 0.16, r * 0.26, INK)
    c.part(line=False)
    if o:
        c.ellipse(fx + face * r * 0.45, fy + r * 0.62 * squish, r * 0.2, r * 0.22, MOUTH)
    else:
        c.stroke(bezier((fx + face * r * 0.1, fy + r * 0.5), (fx + face * r * 0.5, fy + r * 0.9), (fx + face * r * 0.9, fy + r * 0.5)), r * 0.1, LIP)
    if hat == "cone":
        c.part()
        c.poly([(fx - r * 0.8, fy - r * 0.7), (fx + r * 0.8, fy - r * 0.7), (fx - face * r * 0.3, fy - r * 3.0)], HATC)
        c.recolor(lambda x, y, m: ACCENT if math.floor((y - fy) / (r * 0.4)) % 2 == 0 else m)
        c.part()
        c.ellipse(fx - face * r * 0.3, fy - r * 3.0, r * 0.32, r * 0.32, WHITE)
    elif hat == "derby":
        c.part()
        c.ellipse(fx, fy - r * 0.75, r * 1.15, r * 0.25, BLACK)
        c.ellipse(fx, fy - r * 1.05, r * 0.7, r * 0.55, BLACK)
        c.recolor(lambda x, y, m: NOSE if abs(y - (fy - r * 0.85)) < r * 0.1 else m)


def clown_car(p, k=1.0):
    """THE JALOPY: a tiny round bubble car (the circus kind, an old Fiat or
    Isetta made smaller still) with clowns crammed into every opening: one
    at the wheel, one up through the sunroof, one squashed at the side window,
    a pair of big shoes kicking out of the back. Polka-dot paint, mismatched
    wheels, a bulb horn, a googly headlamp and a flower on a spring aerial."""
    W, H = int(370 * k), int(250 * k)
    c = HDCanvas(W, H, W // 2 + int(6 * k), H - int(16 * k))
    by = p.get("by", 0) * k
    sq = p.get("squash", 0)
    spin = p.get("wheel", 0)
    door = p.get("door", 0)
    faces = p.get("faces", True)
    yb = lambda y: (y * (1 - sq * 0.6)) * k + by                    # body heights squash down onto the wheels

    # the flower on its spring aerial, behind everything
    c.part(far=True)
    zz = [(-82 * k + (3 * k if i % 2 else -3 * k), yb(-78 - i * 9)) for i in range(7)]
    c.stroke(zz, 1.2 * k, METAL)
    flx, fly = zz[-1][0] - 2 * k, zz[-1][1] - 8 * k
    for a in range(5):
        c.part()
        ang = a / 5 * math.tau + spin * 0.02
        c.ellipse(flx + math.cos(ang) * 6 * k, fly + math.sin(ang) * 6 * k, 4.5 * k, 4.5 * k, NOSE)
    c.part()
    c.ellipse(flx, fly, 3.6 * k, 3.6 * k, GOLD)

    # a clown up through the sunroof: arms flung up, cone hat
    if faces:
        c.part()
        c.capsule(-10 * k, yb(-120), -10 * k, yb(-142), 11 * k, 10 * k, SUIT)
        c.recolor(lambda x, y, m: ACCENT if math.floor(x / (4 * k)) % 2 == 0 else m)
        for s in (-1, 1):
            c.part()
            c.capsule(-10 * k + s * 8 * k, yb(-140), -10 * k + s * 24 * k, yb(-168 + (4 if p.get("puff") else 0) * s), 4 * k, 3.6 * k, SUIT)
            c.part()
            c.ellipse(-10 * k + s * 25 * k, yb(-172 + (4 if p.get("puff") else 0) * s), 5.5 * k, 5.5 * k, WHITE)
        car_clown(c, -8 * k, yb(-154), 13 * k, HAIR, "cone", o=bool(p.get("puff")))

    # the cabin: a bubble, glass all round
    c.part()
    c.ellipse(-6 * k, yb(-96), 66 * k, 44 * k, TRIM)
    c.part()
    c.poly([(26 * k, yb(-90)), (54 * k, yb(-88)), (36 * k, yb(-128)), (14 * k, yb(-132))], BELLY)        # windscreen
    c.part()
    c.ellipse(-24 * k, yb(-106), 28 * k, 20 * k, BELLY)                                             # side window
    c.part()
    c.ellipse(-62 * k, yb(-100), 9 * k, 15 * k, BELLY)                                              # back window
    if faces:
        car_clown(c, 32 * k, yb(-108), 12 * k, ACCENT, "derby")                                     # the driver
        car_clown(c, -24 * k, yb(-103), 13 * k, HAIR, None, squish=0.82, o=True)                    # squashed at the glass
        c.part()
        c.ellipse(-42 * k, yb(-94), 6 * k, 6 * k, WHITE)                                            # a glove on the glass
        # big shoes kicking out of the back window
        for i, (lx, ly) in enumerate(((-82, -130), (-92, -116))):
            c.part()
            c.capsule(-62 * k, yb(-102 + i * 6), lx * k, yb(ly), 4 * k, 4 * k, PANTS)
            c.recolor(lambda x, y, m: ACCENT if math.floor((x + y) / (4 * k)) % 2 == 0 else m)
            c.part()
            c.ellipse(lx * k - 7 * k, yb(ly) - 2 * k, 11 * k, 5.5 * k, SHOE)

    # the body: a fat rounded tub, polka-dotted, with a gold belt line
    c.part()
    c.ellipse(0, yb(-62), 106 * k, 32 * k, CART)
    c.ellipse(76 * k, yb(-52), 42 * k, 24 * k, CART)
    c.ellipse(-78 * k, yb(-56), 34 * k, 28 * k, CART)
    c.recolor(lambda x, y, m: CART2 if abs(y - yb(-74)) < 2.5 * k else m)
    c.recolor(lambda x, y, m: WHITE if m == CART and ((x / k + 11 * (math.floor((y - by) / (22 * k)) % 2)) % 22 - 11) ** 2
              + (((y - by) / k) % 22 - 11) ** 2 < 24 else m)
    # the door, opening out to let more clowns out than could possibly fit
    if door <= 0:
        c.part(line=True)
        c.poly([(-6 * k, yb(-36)), (38 * k, yb(-36)), (38 * k, yb(-84)), (-6 * k, yb(-84))], CART)
        c.recolor(lambda x, y, m: WHITE if ((x / k + 11 * (math.floor((y - by) / (22 * k)) % 2)) % 22 - 11) ** 2
                  + (((y - by) / k) % 22 - 11) ** 2 < 24 else (CART2 if abs(y - yb(-74)) < 2.5 * k else m))
        c.part(line=False)
        c.ellipse(32 * k, yb(-66), 3 * k, 2 * k, GOLD)
    else:
        c.part()
        c.poly([(-6 * k, yb(-36)), (38 * k, yb(-36)), (38 * k, yb(-84)), (-6 * k, yb(-84))], BLACK)
        if faces:
            for i in range(3):                                                          # eyes in the dark
                c.part(line=False)
                c.ellipse((4 + i * 12) * k, yb(-70 + (i % 2) * 10), 2.5 * k, 3 * k, EYE)
        c.part()
        c.poly([(38 * k, yb(-36)), (38 * k + 44 * k * door, yb(-42)), (38 * k + 44 * k * door, yb(-90)), (38 * k, yb(-84))], CART)
        c.recolor(lambda x, y, m: CART2 if abs(y - yb(-74)) < 2.5 * k else m)

    # bumpers, red and white like a barber's pole
    for x0, x1 in ((96, 126), (-122, -96)):
        c.part()
        c.capsule(x0 * k, yb(-36), x1 * k, yb(-36), 5 * k, 5 * k, WHITE)
        c.recolor(lambda x, y, m: NOSE if math.floor((x + y) / (5 * k)) % 2 == 0 else m)
    # googly headlamp
    c.part()
    c.ellipse(104 * k, yb(-62), 10 * k, 10 * k, GOLD)
    c.part(line=False)
    c.ellipse(105 * k, yb(-62), 7.5 * k, 7.5 * k, EYE)
    c.part(line=False)
    look = 2 if p.get("puff") else 0
    c.ellipse(108 * k, yb(-60 - look), 3.6 * k, 3.6 * k, INK)
    # bulb horn on the bonnet
    c.part()
    c.capsule(70 * k, yb(-82), 90 * k, yb(-92), 2.5 * k, 7 * k, GOLD)
    c.part()
    c.ellipse(64 * k, yb(-80), 7 * k if not p.get("puff") else 5.5 * k, 6 * k, NOSE)
    # exhaust and its puff
    c.part()
    c.capsule(-108 * k, yb(-30), -122 * k, yb(-28), 3 * k, 3.5 * k, METAL)
    if p.get("puff"):
        for i in range(3):
            c.part(line=False)
            c.ellipse(-134 * k - i * 12 * k, yb(-30) - i * 6 * k, (7 + i * 3) * k, (6 + i * 2) * k, WHITE)
    # wheels: a big one behind, a little one in front, white walls and starry hubs
    for wx, wr in ((-70, 31), (74, 23)):
        wr *= k
        cy = -wr * (1 - sq)
        c.part()
        c.ellipse(wx * k, cy, wr, wr * (1 - sq), BLACK)
        c.part()
        c.ellipse(wx * k, cy, wr * 0.68, wr * 0.68 * (1 - sq), WHITE)
        c.part()
        c.ellipse(wx * k, cy, wr * 0.5, wr * 0.5 * (1 - sq), CART2)
        c.recolor(lambda x, y, m, wx=wx, cy=cy: NOSE if ((math.degrees(math.atan2(y - cy, x - wx * k)) - spin * 3) % 72) < 30 else m)
        c.part()
        c.ellipse(wx * k, cy, 4.5 * k, 4.5 * k, GOLD)
    if p.get("smoke"):
        for i in range(4):
            c.part(line=False)
            c.ellipse(-20 * k + i * 18 * k, yb(-110) - i * 8 * k, (10 + i * 2) * k, (8 + i * 2) * k, BLACK)
    return c


def carriage(p, k=1.0):
    """The Ringmaster's chariot: no horse, no dog, just the carriage, a little
    gilded circus parade wagon that rolls itself. A deep open box (he stands
    in it), a tall carved back panel with a sunburst crest and plumes, a swan
    neck scroll curling up at the front with a lantern, gold border and a
    medallion star on the side, a scalloped valance underneath, and sunburst
    wheels, the back pair bigger than the front."""
    W, H = int(380 * k), int(250 * k)
    c = HDCanvas(W, H, W // 2, H - int(18 * k))
    by = p.get("by", 0) * k
    spin = p.get("wheel", 0)
    broken = p.get("broken")
    tilt = 0.18 if broken else 0.0

    def P(x, y):
        """Body points, tipped forward onto the ground when wrecked."""
        x, y = x * k, y * k
        if broken:
            return (x * math.cos(tilt) - (y + 30 * k) * math.sin(tilt), x * math.sin(tilt) + (y + 30 * k) * math.cos(tilt) + 2 * k)
        return (x, y + by)

    # plumes on the crest, behind
    if not broken:
        for i, (dx, col) in enumerate(((-12, ACCENT), (0, HAIR), (12, ACCENT))):
            c.part(far=True)
            x0, y0 = P(-92 + dx * 0.4, -150)
            c.capsule(x0, y0, x0 + dx * k, y0 - (26 - abs(dx)) * k, 4 * k, 6.5 * k, col)

    # the far wheels peek out behind
    def wheel(wx, wr, far, flat=False):
        wr *= k
        if flat:
            c.part(far=far)
            c.ellipse(wx * k, -wr * 0.28, wr, wr * 0.28, CART2)
            c.part(far=far)
            c.ellipse(wx * k, -wr * 0.28 - 1 * k, wr * 0.75, wr * 0.2, CART)
            return
        c.part(far=far)
        c.ellipse(wx * k, -wr, wr, wr, CART2)
        c.part(far=far)
        c.ellipse(wx * k, -wr, wr * 0.8, wr * 0.8, CART)
        c.recolor(lambda x, y, m, wx=wx, wr=wr: CART2 if ((math.degrees(math.atan2(y + wr, x - wx * k)) - spin) % 30) < 9 else m)
        c.part(far=far)
        c.ellipse(wx * k, -wr, wr * 0.26, wr * 0.26, GOLD)
        c.recolor(lambda x, y, m, wx=wx, wr=wr: CART if (x - wx * k) ** 2 + (y + wr) ** 2 < (wr * 0.1) ** 2 else m)

    if not broken:
        wheel(-56 + 6, 36, True)
        wheel(70 + 6, 26, True)

    # the box with its carved back panel and swan-neck front, all one shape
    c.part()
    c.poly([P(-104, -40), P(84, -40), P(92, -60), P(88, -94), P(-80, -94), P(-86, -138), P(-98, -150), P(-110, -138), P(-104, -94)], CART)
    # the swan neck: a scroll rising from the front corner and curling over
    neck = bezier(P(84, -60), P(116, -84), P(102, -126), 10)
    c.stroke(neck, 7 * k, CART)
    cx_, cy_ = P(92, -128)
    c.ellipse(cx_, cy_, 11 * k, 11 * k, CART)
    # gold: rims along the top and bottom, a border inset on the side, the crest's edge
    top, bot = P(0, -94)[1], P(0, -40)[1]
    c.recolor(lambda x, y, m: CART2 if (not broken and (abs(y - top) < 3 * k or abs(y - bot) < 3 * k
                                                        or (abs(y - top - 9 * k) < 1.4 * k and -96 * k < x < 76 * k)
                                                        or (abs(y - bot + 9 * k) < 1.4 * k and -96 * k < x < 76 * k)
                                                        or (abs(x + 96 * k) < 1.4 * k and top + 9 * k < y < bot - 9 * k)
                                                        or (abs(x - 76 * k) < 1.4 * k and top + 9 * k < y < bot - 9 * k))) else m)
    # the scroll's curl: a gold spiral eye
    c.part()
    c.ellipse(cx_, cy_, 6 * k, 6 * k, CART2)
    c.recolor(lambda x, y, m: CART if (x - cx_ + 1 * k) ** 2 + (y - cy_ - 1 * k) ** 2 < (2.6 * k) ** 2 else m)
    # the crest: a gold sunburst at the top of the back panel
    sx, sy = P(-98, -134)
    c.part()
    c.ellipse(sx, sy, 10 * k, 10 * k, CART2)
    c.recolor(lambda x, y, m: ACCENT if ((math.degrees(math.atan2(y - sy, x - sx))) % 40) < 14 else m)
    c.part()
    c.ellipse(sx, sy, 4 * k, 4 * k, NOSE)
    # the side medallion: a gold oval with a star
    mx, my = P(-6, -67)
    c.part()
    c.ellipse(mx, my, 20 * k, 16 * k, CART2)
    c.part()
    c.ellipse(mx, my, 15.5 * k, 12 * k, HATC)
    c.recolor(lambda x, y, m: CART2 if star_in((x - mx) / (1.2 * k), (y - my) / k, 10) else m)
    # gold scrolls either side of the medallion
    for s in (-1, 1):
        c.part()
        c.stroke(bezier(P(-6 + s * 22, -67), P(-6 + s * 44, -82), P(-6 + s * 62, -66)), 2.2 * k, CART2)
        x1, y1 = P(-6 + s * 62, -66)
        c.ellipse(x1, y1 + 2 * k, 3.5 * k, 3.5 * k, CART2)
    # finials: gold balls on the corners
    for fx, fy in ((-104, -96), (84, -96)):
        x0, y0 = P(fx, fy)
        c.part()
        c.ellipse(x0, y0 - 4 * k, 4.5 * k, 4.5 * k, GOLD)
    # the scalloped valance under the box, between the wheels
    if not broken:
        for i in range(9):
            x0, y0 = P(-60 + i * 14, -38)
            c.part()
            c.ellipse(x0, y0, 7 * k, 6 * k, ACCENT if i % 2 else HAIR)
            c.part(line=False)
            c.ellipse(x0, y0 + 6 * k, 1.6 * k, 2.2 * k, GOLD)
    # the lantern hung from the swan neck
    lx, ly = P(108, -94)
    c.part()
    c.stroke([P(104, -110), (lx, ly - 6 * k)], 1 * k, GOLD)
    c.part()
    c.poly([(lx - 5 * k, ly - 6 * k), (lx + 5 * k, ly - 6 * k), (lx + 4 * k, ly + 7 * k), (lx - 4 * k, ly + 7 * k)], GOLD)
    c.part(line=False)
    c.ellipse(lx, ly + 0.5 * k, 2.8 * k, 4.4 * k, FLAME2 if not broken else BLACK)
    # the near wheels
    if broken:
        wheel(-70, 36, False, flat=True)
        c.part()
        c.ellipse(96 * k, -26 * k, 26 * k, 26 * k, CART2)
        c.part()
        c.ellipse(96 * k, -26 * k, 20 * k, 20 * k, CART)
        c.recolor(lambda x, y, m: CART2 if ((math.degrees(math.atan2(y + 26 * k, x - 96 * k)) + 17) % 30) < 9 else m)
        for i in range(4):
            c.part()
            c.capsule((-30 + i * 22) * k, -3 * k, (-18 + i * 22) * k, -8 * k + (i % 2) * 3 * k, 2.4 * k, 2.4 * k, CART2)
    else:
        # the chassis between the axles
        c.part()
        c.capsule(-56 * k, -36 * k + by, 70 * k, -26 * k + by, 2.6 * k, 2.6 * k, GOLD)
        wheel(-56, 36, False)
        wheel(70, 26, False)
    return c


def paste(dst, src, dx, dy):
    """Copy a drawn canvas onto another (parts renumbered so shading keeps them separate)."""
    base = dst.cur
    for pid, part in src.parts.items():
        if pid:
            dst.parts[base + pid] = part
    dst.cur = base + max(src.parts)
    for y in range(src.h):
        for x in range(src.w):
            m = src.mat[y][x]
            if not m:
                continue
            X = int(x - src.ox + dst.ox + dx)
            Y = int(y - src.oy + dst.oy + dy)
            if 0 <= X < dst.w and 0 <= Y < dst.h:
                dst.mat[Y][X] = m
                dst.part_of[Y][X] = base + src.part_of[y][x]


# ------------------------------------------------------------ the cast

CAST = {
    # ---- enemies
    "juggler": dict(k=1.0, suit=(150, 70, 190), accent=(250, 210, 90), pattern="stripes", hair=(250, 120, 160),
                    hat="cone", hat_col=(80, 190, 210), neck="ruff", shoe=(80, 190, 210), juggles="pin", belt="plain", legwear="stripes", wristbands=True),
    "unicyclist": dict(k=1.0, legs="unicycle", suit=(240, 150, 40), accent=(60, 130, 220), pattern="vstripes",
                       hair=(90, 60, 40), hat="fez", hat_col=(200, 40, 60), neck="bowtie", shoe=(60, 60, 80), pants=(60, 130, 220), belt="plain", legwear="stripes"),
    "weightlifter": dict(k=1.12, build="muscle", suit=(210, 50, 60), accent=(250, 240, 240), pattern="leotard", bare_arms=True,
                         gloves=False, hair_style="slick", hair=(40, 30, 30), hat="none", neck="none", mustache=True,
                         face="plain", nose=False, shoe=(60, 40, 50), shoes="boot", pants=(210, 50, 60), cw=240, strap=True, belt="plain", wristbands=True),
    "balloonist": dict(k=1.0, build="round", suit=(70, 180, 120), accent=(250, 250, 250), pattern="dots", hair=(250, 200, 80),
                       hat="bowler", hat_col=(60, 50, 70), neck="bowtie", balloon=(240, 80, 120), belt="plain", sash=True),
    "stiltwalker": dict(k=1.0, legs="stilts", suit=(60, 60, 120), accent=(250, 220, 90), pattern="plain", tails=True,
                        hair=(200, 200, 210), hat="top", hat_col=(40, 40, 60), neck="collar", pants=(220, 60, 80), ch=420, cw=330, sequins=True, frogging=True),
    "mime": dict(k=1.0, face="mime", suit=(30, 30, 40), accent=(245, 245, 250), pattern="stripes", hair_style="none",
                 hat="beret", hat_col=(30, 30, 40), neck="scarf", nose=False, shoe=(30, 30, 40), pants=(30, 30, 40),
                 trim=(230, 60, 70), suspenders=True),
    "tot": dict(k=0.62, build="tiny", suit=(240, 90, 90), accent=(250, 230, 120), pattern="dots", hair=(120, 210, 250),
                hat="cone", hat_col=(120, 210, 250), neck="ruff", cw=260, ch=320),
    # ---- bosses
    # Bobo: a clown in a spotted suit with a ruff and striped stockings, juggling up on his ball
    "ballrider": dict(k=1.25, suit=(250, 120, 170), accent=(255, 255, 255), pattern="dots", hair=(120, 220, 210),
                      hat="cone", hat_col=(250, 200, 80), neck="ruff", juggles="pin", belt="plain", legwear="stripes",
                    eye="big", brow="high", features=("makeup_diamond",)),
    # Madame Stripes, the lion tamer: a red hussar jacket with gold frogging and
    # epaulettes, white jodhpurs, tall black riding boots, pith helmet, whip and chair
    "trainer": dict(k=1.3, suit=(200, 40, 50), accent=(250, 200, 80), pattern="plain", hair_style="bun", hair=(90, 50, 40),
                    hat="pith", hat_col=(240, 230, 210), neck="collar", face="plain", nose=False, shoes="boot",
                    shoe=(40, 32, 44), pants=(245, 240, 225), lip=(200, 40, 70), cw=300, shift=-40,
                    frogging=True, epaulettes=True, jodhpurs=True, riding_boots=True, belt="plain",
                    eye="lids", features=("lashes", "beauty_mark"), skin=(238, 196, 164)),
    # Stretch, the india-rubber man: a banded rubber suit, a star on the chest, everything too long
    "elastic": dict(k=1.3, suit=(120, 70, 210), accent=(150, 100, 236), pattern="star", leg=1.5, torso=1.25, arm=1.15,
                    hair_style="slick", hair=(250, 230, 120), hat="none", neck="collar", trim=(250, 220, 80),
                    shoe=(250, 220, 80), shoes="boot", ch=340, legwear="stripes", limb_rings=True, belt="plain",
                    eye="dot", head_h=1.3, head_w=0.85, nose_shape="long", features=("buck_teeth",)),
    # the Flying Fratelli: sequinned leotards with a sash, taped wrists, soft boots
    "trapeze_a": dict(k=1.2, suit=(40, 170, 200), accent=(250, 250, 255), pattern="leotard", hair_style="slick", hair=(50, 40, 40),
                      hat="none", neck="none", face="plain", nose=False, mustache="handlebar", shoes="boot", shoe=(250, 250, 255),
                      pants=(40, 170, 200), cuffs=False, sequins=True, sash=True, wristbands=True,
                    eye="narrow", brow="thick", jaw=True, features=("sideburns", "cleft"), skin=(232, 188, 150)),
    "trapeze_b": dict(k=1.2, suit=(240, 90, 170), accent=(250, 250, 255), pattern="leotard", hair_style="long", hair=(240, 200, 90),
                      hat="none", neck="none", face="plain", nose=False, shoes="boot", shoe=(250, 250, 255),
                      pants=(240, 90, 170), cuffs=False, sequins=True, sash=True, wristbands=True,
                    eye="big", brow="high", features=("lashes", "freckles"), freck=(214, 120, 96)),
    # Pyro Pete: bandana, open vest, flames up his trousers, a torch in each hand
    "fireeater": dict(k=1.3, suit=(120, 40, 40), accent=(250, 170, 60), pattern="vest", hair_style="none", hat="bandana",
                      hat_col=(220, 60, 40), mustache="handlebar", face="plain", nose=False, bare_arms=True, gloves=False,
                      neck="none", pants=(60, 40, 60), shoes="boot", shoe=(60, 40, 50), juggles="torch",
                      flame_hem=True, belt="plain", wristbands=True,
                    eye="narrow", brow="thick", nose_shape="bulb", features=("scar", "earring", "stubble", "goatee"), skin=(196, 128, 96), freck=(120, 70, 60)),
    # Iron Ivan, the strongman: a one-strap leopard leotard, a championship belt,
    # leather wristbands, a waxed handlebar moustache, bald as a cannonball
    "strongman": dict(k=1.5, build="muscle", suit=(240, 200, 120), accent=(120, 70, 40), stripe=(70, 40, 30),
                      pattern="spots", bare_arms=True, gloves=False, hair_style="none", hat="none", neck="none",
                      mustache="handlebar", stache_col=1, face="plain", nose=False, shoes="boot", shoe=(60, 40, 40),
                      pants=(240, 200, 120), cw=260, strap=True, belt="champ", wristbands=True,
                    eye="dot", brow="unibrow", head_w=1.12, jaw=True, nose_shape="bent", features=("cleft",), skin=(244, 198, 170)),
    # the Ringmaster: red tailcoat with gold frogging and epaulettes, white
    # breeches, tall black riding boots, top hat, a waxed moustache
    "ringmaster": dict(k=1.35, suit=(200, 30, 50), accent=(252, 204, 72), pattern="tux", tails=True, hair=(40, 30, 30),
                       hair_style="slick", hat="top", hat_col=(40, 30, 50), neck="bowtie", mustache="handlebar", stache_col=1,
                       face="plain", nose=False, shoes="boot", shoe=(30, 24, 34), pants=(245, 240, 230), leg=1.15, torso=1.1,
                       cw=300, shift=-40, frogging=True, epaulettes=True, riding_boots=True,
                    eye="narrow", brow="thick", nose_shape="hook", features=("monocle", "gold_tooth", "sideburns")),
}

BEASTS = {
    "tiger": dict(kind="tiger", k=1.2, fur=(238, 132, 40), stripe=(34, 24, 30), belly=(252, 244, 228), nose_col=(214, 120, 118),
                  accent=(250, 196, 60)),
}


def humanoid_clips(name):
    """Every humanoid's standard clips, then its own specials."""
    s = CAST[name]
    held_n, held_f = None, None
    j = s.get("juggles")
    special = {}
    if name in ("juggler", "ballrider"):
        held_n, held_f = "pin", "pin"
    if name == "fireeater":
        held_n, held_f = "torch", "torch"
    if name == "weightlifter":
        held_n = "dumbbell"
    if name == "balloonist":
        held_f = "balloon"
    if name in ("trainer",):
        held_n, held_f = "whip", "chair"
    if name == "ringmaster":
        held_n, held_f = "whip", "cane"
    if name == "strongman":
        held_n = None

    def base(**kw):
        d = dict(item_n=held_n, item_f=held_f)
        d.update(kw)
        return P(**d)

    clips = []
    uni = s.get("legs") == "unicycle"
    # Bosses move bigger: more breath, more swagger, a heavier stride.
    amp = 1.7 if name in BOSSES else 1.0
    # idle: six frames of breathing, arm sway, a head bob and a blink
    # (and juggling, for those who juggle)
    for i in range(6):
        ph = i / 6 * math.tau
        b = (1 - math.cos(ph)) * 1.4 * amp                            # 0 .. 2.8 px down and back
        sway = math.sin(ph) * 7 * amp
        kw = dict(by=b, na=14 + sway, ne=12 + sway * 0.5, fa=-8 - sway, fe=10 + sway * 0.5,
                  head_dy=-math.sin(ph) * 0.8 * amp, lean=math.sin(ph) * 1.5 * amp,
                  eyes="closed" if i == 3 else "open")
        if j and name != "fireeater":
            kw.update(juggle=i / 6, na=60 + (i % 2) * 20, ne=60, fa=40 + ((i + 1) % 2) * 20, fe=60, item_n=None, item_f=None, eyes="open")
        if name == "fireeater":
            kw.update(juggle=i / 6, na=70 + (i % 2) * 25, ne=50, fa=45 + ((i + 1) % 2) * 25, fe=50, item_n=None, item_f=None, eyes="open")
        if name == "mime":
            kw.update(na=80 + sway, ne=70, fa=70 - sway, fe=80)       # palms feeling the invisible box
        if uni:
            kw.update(na=90 + sway * 2, ne=0, fa=-80 - sway * 2, fe=0, pedal=i * 15, nf=0, ff=0)   # wobbling for balance
        if name == "strongman":
            kw.update(na=20 + sway * 0.5, ne=90, fa=-20, fe=90, lean=-2 + math.sin(ph) * 2)   # fists on hips, chest out
        if name == "balloonist":
            kw.update(fa=150 + sway, fe=10)                            # the bunch held up high
        if name in ("trainer", "ringmaster"):
            kw.update(na=30 + sway * 2, ne=40 + sway, whip="coil")     # flicking the coiled whip
        clips.append(("idle", base(**kw)))
    # walk: eight frames of stride (or pedalling, or stilt steps), arms swinging, body bobbing
    for i in range(8):
        ph = i / 8 * math.tau
        stride = 11 * (1.15 if amp > 1 else 1.0)
        kw = dict(nf=math.sin(ph) * stride, nfl=max(0, math.cos(ph)) * 7, ff=-math.sin(ph) * stride, ffl=max(0, -math.cos(ph)) * 7,
                  by=abs(math.sin(ph)) * 2.2 * amp, na=12 - math.sin(ph) * 24, fa=-10 + math.sin(ph) * 24,
                  lean=3 + math.cos(ph * 2) * 1.5 * amp, head_dy=abs(math.sin(ph)) * 0.8)
        if j and name not in ("fireeater",):
            kw.update(juggle=i / 8, na=60 + (i % 2) * 20, ne=60, fa=40 + ((i + 1) % 2) * 20, fe=60, item_n=None, item_f=None)
        if name == "fireeater":
            kw.update(item_n="torch", item_f="torch")
        if uni:
            kw.update(pedal=i * 45, na=90 + math.sin(ph) * 15, ne=0, fa=-80 - math.sin(ph) * 15, fe=0, nf=0, ff=0)
        if s.get("legs") == "stilts":
            kw.update(nf=math.sin(ph) * 16, ff=-math.sin(ph) * 16)
        clips.append(("walk", base(**kw)))
    # attack: wind-up, strike, follow-through, recover (pinned to the swing in game)
    atk = [
        dict(lean=-6, na=-60, ne=40, fa=-30, by=1),
        dict(lean=4, na=70, ne=10, fa=-20, nf=6, ff=-4),
        dict(lean=8, na=95, ne=0, fa=-10, nf=8, ff=-6, by=1),
        dict(lean=2, na=40, ne=20, fa=-12),
    ]
    if name in ("trainer", "ringmaster"):
        atk = [dict(lean=-6, na=150, ne=20, whip="back"), dict(lean=2, na=110, ne=0, whip="back"),
               dict(lean=8, na=80, ne=0, whip="crack", nf=6), dict(lean=3, na=40, ne=20, whip="coil")]
    if name in ("juggler", "ballrider"):
        atk = [dict(lean=-6, na=160, ne=30, item_n="pin"), dict(lean=2, na=120, ne=10, item_n="pin"),
               dict(lean=8, na=80, ne=0, item_n=None), dict(lean=3, na=40, ne=20, item_n=None)]
    if name == "stiltwalker":
        atk = [dict(nf=10, nfl=24, lean=-4), dict(nf=16, nfl=30, lean=-2), dict(nf=14, nfl=0, lean=6, by=2), dict(nf=6, lean=2)]
    if name == "weightlifter":
        atk = [dict(lean=-6, na=-80, ne=60), dict(lean=2, na=60, ne=40), dict(lean=10, na=110, ne=10, by=2), dict(lean=4, na=40, ne=30)]
    if name == "strongman":
        atk = [dict(lean=-8, na=-70, ne=80, fa=20, fe=90), dict(lean=2, na=60, ne=40, fa=20, fe=90),
               dict(lean=10, na=100, ne=0, fa=10, fe=90, nf=8, by=2), dict(lean=4, na=40, ne=40, fa=20, fe=90)]
    for a in atk:
        clips.append(("attack", base(mouth="open", eyes="angry", **a)))
    clips.append(("hit", base(lean=-10, by=2, head_dy=2, na=-40, ne=60, fa=-60, fe=60, mouth="o", eyes="closed")))
    clips.append(("air", base(by=-2, na=140, ne=20, fa=130, fe=20, nf=6, nfl=8, ff=-6, ffl=10, mouth="o")))
    clips.append(("air", base(by=-1, na=60, ne=40, fa=50, fe=40, nf=4, nfl=4, ff=-4, ffl=6)))

    # specials: (clip name, pose) lists
    if name == "juggler":
        special["special"] = [dict(na=170, ne=10, fa=150, fe=10, item_n="pin", item_f="pin", pin_up=True),
                              dict(na=130, ne=0, fa=150, fe=10, item_n=None, item_f="pin", pin_up=True, lean=6)]
    if name == "unicyclist":
        special["special"] = [dict(lean=18, na=150, ne=0, fa=150, fe=0, pedal=a, mouth="open", eyes="angry") for a in (0, 90, 180, 270)]
    if name == "weightlifter":
        special["special"] = [dict(lean=8, na=95, ne=-10, fa=95, fe=-10, mouth="open", eyes="angry"),     # the grab
                              dict(lean=-4, na=170, ne=10, fa=170, fe=10, by=-1, mouth="open"),            # hoisted
                              dict(lean=14, na=90, ne=20, fa=90, fe=20, by=4, mouth="open", eyes="angry")]  # the slam
        special["guard"] = [dict(na=120, ne=110, fa=120, fe=110, lean=-2, mouth="flat", eyes="angry")]
    if name == "balloonist":
        special["special"] = [dict(na=80, ne=60, fa=60, fe=70, item_f=None), dict(na=90, ne=50, fa=70, fe=60, item_f=None, mouth="o"),
                              dict(na=150, ne=10, fa=60, fe=60, item_f=None, mouth="open")]
    if name == "stiltwalker":
        special["special"] = [dict(nf=8, nfl=40, lean=-8, na=150, fa=140, mouth="open", eyes="angry"),
                              dict(nf=14, nfl=0, lean=10, by=4, na=60, fa=50, mouth="open", eyes="angry")]
    if name == "mime":
        special["special"] = [dict(na=95, ne=-10, fa=80, fe=10, lean=-6), dict(na=60, ne=80, fa=70, fe=60, lean=-14),   # rope pull
                              dict(na=95, ne=-10, fa=70, fe=60, lean=-8)]
        special["wall"] = [dict(na=90, ne=0, fa=95, fe=-5, lean=4)]
    if name == "tot":
        special["special"] = [dict(lean=60, by=10, na=100, ne=0, fa=90, fe=0, nf=-10, ff=-16, mouth="open")]   # crawling
        special["stack"] = [dict(na=170, ne=10, fa=165, fe=10, mouth="grin")]                                   # holding up the next
    if name == "ballrider":
        special["special"] = [dict(na=170, ne=0, fa=170, fe=0, item_n="pin", item_f="pin", pin_up=True, mouth="open"),
                              dict(na=60, ne=30, fa=60, fe=30, crouch=8, mouth="o")]      # crouch for a bounce
    if name == "trainer":
        special["special"] = [dict(na=170, ne=0, whip="coil", fa=10, mouth="open", eyes="angry")]   # command the tiger
        special["hoop"] = [dict(na=150, ne=10, item_n="hoop", fa=-10, mouth="grin")]
    if name == "elastic":
        special["special"] = [dict(na=95, ne=0, lean=8, mouth="grin", eyes="angry")]            # the arm is drawn by the game
        special["coil"] = [dict(crouch=18, na=30, ne=60, fa=20, fe=60, mouth="o")]
    if name.startswith("trapeze"):
        special["hang"] = [dict(na=175, ne=0, fa=170, fe=0, nf=8, nfl=6, ff=4, ffl=8)]
        special["dive"] = [dict(na=100, ne=0, fa=95, fe=0, lean=10, nf=-6, nfl=10, ff=-10, ffl=12, mouth="open")]
        special["special"] = [dict(na=170, ne=10, fa=160, fe=10, crouch=10, mouth="open")]      # catch and throw
    if name == "fireeater":
        special["special"] = [dict(head_dy=-2, na=150, ne=40, fa=40, fe=60, item_n="torch", lean=-6, mouth="o"),
                              dict(head_dy=0, na=100, ne=20, fa=40, fe=60, item_n="torch", lean=6, mouth="open", flame=0.5),
                              dict(head_dy=0, na=100, ne=20, fa=40, fe=60, item_n="torch", lean=8, mouth="open", flame=1.0)]
        special["kick"] = [dict(nf=6, nfl=6, lean=-8, na=-40, ne=40, fa=60, fe=40, item_n="torch", mouth="flat", eyes="angry"),
                           dict(nf=22, nfl=26, lean=-14, na=-60, ne=30, fa=80, fe=30, item_n="torch", mouth="open", eyes="angry")]
        special["smash"] = [dict(na=170, ne=10, item_n=None, fa=40, fe=60, mouth="grin"),
                            dict(na=60, ne=10, item_n=None, fa=40, fe=60, lean=8, mouth="open", eyes="angry")]
    if name == "strongman":
        special["special"] = [dict(na=100, ne=60, fa=100, fe=60, crouch=10, item_n="barbell", mouth="flat", eyes="angry"),
                              dict(na=175, ne=0, fa=175, fe=0, item_n="barbell", mouth="open", eyes="angry", by=-2),
                              dict(na=100, ne=10, fa=100, fe=10, crouch=12, item_n="barbell", mouth="open", eyes="angry", lean=10)]
        special["grab"] = [dict(na=95, ne=-15, fa=95, fe=-15, lean=10, mouth="open", eyes="angry"),
                           dict(na=175, ne=0, fa=175, fe=0, mouth="open", by=-2)]
    if name == "ringmaster":
        special["special"] = [dict(na=170, ne=0, item_n="hat", fa=-10, mouth="open")]           # the hat trick
        special["point"] = [dict(na=95, ne=0, item_n=None, fa=-10, item_f="cane", mouth="open", eyes="angry", lean=4)]
        special["sit"] = [dict(na=60, ne=70, fa=30, fe=60, whip="coil")]
    for clip, poses in special.items():
        for kw in poses:
            clips.append((clip, base(**kw)))
    return clips


def beast_clips(name):
    clips = []
    for i in range(6):
        ph = i / 6 * math.tau
        clips.append(("idle", dict(by=(1 - math.cos(ph)) * 1.5, tail=math.sin(ph) * 12, head_up=math.sin(ph) * 2,
                                   head_ang=math.sin(ph) * 15, mouth="open" if i == 3 else None)))
    for i in range(8):
        ph = i / 8 * 360
        clips.append(("walk", dict(nf=ph, nb=ph + 180, ff=ph + 180, bf=ph, lift_n=10, lift_f=10, by=abs(math.sin(rad(ph))) * 3,
                                   tail=math.sin(rad(ph)) * 10, head_up=math.sin(rad(ph * 2)) * 1.5)))
    for a in (dict(head_up=4, mouth="open", angry=True), dict(head_up=-2, nf=60, lift_n=20, mouth="open", angry=True),
              dict(head_up=-4, nf=90, lift_n=10, mouth="open", angry=True), dict(head_up=0, nf=20)):
        clips.append(("attack", a))
    clips.append(("hit", dict(by=3, head_up=-4, mouth="open", tail=-6)))
    clips.append(("air", dict(stretch=1.25, nf=80, ff=70, nb=-80, bf=-70, lift_n=0, mouth="open", angry=True, head_up=2)))
    clips.append(("air", dict(stretch=1.1, nf=40, ff=30, nb=-40, bf=-30, mouth="open", angry=True)))
    clips.append(("special", dict(crouch=10, head_dy=4, angry=True, tail=10)))           # crouched to pounce
    clips.append(("special", dict(stretch=1.3, nf=90, ff=80, nb=-90, bf=-80, mouth="open", angry=True)))
    clips.append(("roar", dict(head_up=10, mouth="open", angry=True, by=-1)))
    return clips


# ------------------------------------------------------------ output

FRAME_FPS = {"idle": 7, "walk": 13}
BOSSES = {"ballrider", "trainer", "elastic", "trapeze_a", "trapeze_b", "fireeater", "strongman", "ringmaster", "tot"}


def render(name):
    if name in CAST:
        rig = Rig(CAST[name])
        frames = [(clip, rig.draw(p)) for clip, p in humanoid_clips(name)]
        down_src = rig.draw(P(lean=-10, by=2, na=-40, ne=60, fa=-60, fe=60, mouth="o", eyes="closed"))
        frames.append(("down", laid_down(down_src, rig.W // 2, rig.base - int(60 * rig.k), rig.base)))
        m = mats(CAST[name])
        W, H, base = rig.W, rig.H, rig.base
    elif name in BEASTS:
        b = Tiger(BEASTS[name])
        frames = [(clip, b.draw(p)) for clip, p in beast_clips(name)]
        flat = b.draw(dict(by=8, head_up=-8, tail=-10))
        frames.append(("down", flat))
        m = mats(BEASTS[name])
        W, H, base = b.W, b.H, b.base
    elif name == "balloondog":
        spec = dict(balloon=(250, 110, 170))
        frames = []
        for i in range(4):
            frames.append(("idle", balloon_dog(dict(by=[0, 1, 1, 0][i], wag=[0, 4, 8, 4][i]))))
        for i in range(4):
            frames.append(("walk", balloon_dog(dict(leg=[25, 0, -25, 0][i], by=[0, 2, 0, 2][i], wag=i * 3))))
        for b in (0, 1, 1, 0):
            frames.append(("attack", balloon_dog(dict(bite=b, by=-2 if b else 0))))
        frames.append(("hit", balloon_dog(dict(by=3))))
        # floating: legs dangling, nose up, bobbing gently (and the drop)
        for i in range(4):
            frames.append(("air", balloon_dog(dict(float=1, by=[0, -1, -2, -1][i], wag=[0, 3, 6, 3][i], leg=[6, 0, -6, 0][i]))))
        frames.append(("down", balloon_dog(dict(by=10))))
        m = mats(spec)
        W, H, base = frames[0][1].w, frames[0][1].h, frames[0][1].oy
    elif name == "clowncar":
        spec = dict(cart=(226, 52, 66), cart2=(252, 210, 80), accent=(252, 240, 120), hat_col=(120, 200, 90),
                    hair=(250, 140, 60), belly=(170, 220, 250), trim=(250, 246, 236), suit=(80, 160, 230), pants=(120, 200, 90),
                    shoe=(230, 60, 70))
        frames = []
        for i in range(4):
            frames.append(("idle", clown_car(dict(by=[0, -2, -3, -2][i], wheel=0))))
        for i in range(6):
            frames.append(("walk", clown_car(dict(by=[0, -2, 0, -2, 0, -2][i], wheel=i * 10, puff=i % 3 == 0))))
        for a in (dict(squash=0.05, by=2), dict(by=-4, wheel=10), dict(by=-2, wheel=20), dict(by=0)):
            frames.append(("attack", clown_car(a)))
        frames.append(("hit", clown_car(dict(by=4, squash=0.12, puff=True))))
        frames.append(("air", clown_car(dict(by=-6))))
        for d in (0.4, 1.0):
            frames.append(("special", clown_car(dict(door=d, by=-1))))
        frames.append(("honk", clown_car(dict(by=-4, puff=True))))
        frames.append(("down", clown_car(dict(by=6, squash=0.25, smoke=True, faces=False))))
        m = mats(spec)
        W, H, base = frames[0][1].w, frames[0][1].h, frames[0][1].oy
    elif name == "carriage":
        spec = dict(cart=(160, 28, 52), cart2=(252, 204, 72), hair=(250, 250, 255), accent=(230, 60, 80), hat_col=(40, 60, 140))
        frames = []
        for i in range(4):
            frames.append(("idle", carriage(dict(by=[0, -1, -1, 0][i], pony_by=[0, 1, 1, 0][i]))))
        for i in range(6):
            frames.append(("walk", carriage(dict(leg=math.sin(i / 6 * math.tau) * 60, wheel=i * 15, by=[0, -2, 0, -2, 0, -2][i]))))
        frames.append(("hit", carriage(dict(by=2))))
        frames.append(("down", carriage(dict(broken=True))))
        m = mats(spec)
        W, H, base = frames[0][1].w, frames[0][1].h, frames[0][1].oy
    else:
        raise SystemExit(f"unknown cast member {name}")
    return name, frames, m, W, H, base


def write(name, frames, m, W, H, base):
    os.makedirs(OUT, exist_ok=True)
    clipped = sorted({n for n, cv in frames if touches_border(cv)})
    both = [shade(cv, m, normals=True) for _, cv in frames]
    path = os.path.join(OUT, name + ".png")
    write_png(path, W * len(both), H, to_rows([px for px, _ in both], W, H))
    write_png(path.replace(".png", "_n.png"), W * len(both), H, to_rows([n for _, n in both], W, H))
    clips = {}
    for i, (n, _) in enumerate(frames):
        clips.setdefault(n, []).append(i)
    with open(path.replace(".png", ".frames.json"), "w") as f:
        json.dump({"frame_width": W, "frame_height": H, "baseline": base, "count": len(both), "clips": clips,
                   "fps": FRAME_FPS}, f, indent=1)
    return name, len(both), W, H, clipped


def job(name):
    return write(*render(name))


ALL = list(CAST) + list(BEASTS) + ["balloondog", "clowncar", "carriage"]

if __name__ == "__main__":
    names = sys.argv[1:] or ALL
    with Pool(min(len(names), os.cpu_count() or 4)) as pool:
        for name, n, W, H, clipped in pool.imap_unordered(job, names):
            print(f"{name:<12} {n:3d} frames {W}x{H}" + (f"   WARNING: touches edge in {', '.join(clipped)}" if clipped else ""))
