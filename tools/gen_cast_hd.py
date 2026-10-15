"""The new cast: six enemy types and nine bosses, in the HD pipeline.

One flexible humanoid rig draws most of them: body build, outfit pattern,
face, hair, hat, legs (feet, stilts or a unicycle), and whatever they hold
(pins, torches, a whip and chair, a barbell, balloons, a cane), posed by
shoulder and elbow angles. A quadruped rig draws the tiger and the pony; the
balloon dog, the clown car and the ringmaster's carriage are drawn by hand.

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
from pixelpng import write_png

OUT = os.path.join("assets", "sprites", "cast")

# ------------------------------------------------------------- materials
(SUIT, ACCENT, TRIM, SKIN, HAIR, NOSE, INK, EYE, LIP, BLUSH, WHITE, SHOE, SOLE, WOOD, METAL, GOLD,
 FLAME, FLAME2, HATC, PANTS, BALLOON, MOUTH, TOOTH, FUR, STRIPE, BELLY, CART, CART2, BLACK) = range(1, 30)


def mats(spec):
    s = spec
    return {
        SUIT: Material(s.get("suit", (70, 108, 210))),
        ACCENT: Material(s.get("accent", (246, 200, 84))),
        TRIM: Material(s.get("trim", (236, 236, 246)), soft=True),
        SKIN: Material(s.get("skin", (250, 218, 192)), soft=True, cool=0.97),
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

        # near leg
        self.leg(c, hip_x + 4 * k, hip_y, p["nf"], p["nfl"], legs, p, far=False, leg_len=leg_len, lift=lift)

        # neck wear
        self.neck(c, sh_x, sh_y, k)

        # head
        self.head(c, hd_x, hd_y, head_r, p)

        # near arm on top
        self.arm(c, near_sh, p["na"], p["ne"], p["item_n"], p, far=False)

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
        elif pat == "spots":   # leopard
            c.recolor(lambda x, y, m: STRIPE if ((x / k + 3 * math.sin(y / k)) % 11 - 5) ** 2 + ((y / k) % 10 - 5) ** 2 < 5 else m)
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
        if s.get("legwear") == "stripes":
            c.recolor(lambda xx, yy, m: ACCENT if math.floor(yy / (5 * k)) % 2 == 0 else m)
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
            for dx, dy, rr in ((-14, -10, 6), (-19, -2, 7.5), (-22, 7, 7), (-18, 15, 6.5)):
                c.part()
                c.ellipse(x + dx * k * r / (18 * k), y + dy * k * r / (18 * k), rr * k, rr * 0.9 * k, HAIR)
        elif style == "bun":
            c.part()
            c.ellipse(x - r * 0.9, y - r * 0.4, r * 0.45, r * 0.45, HAIR)
        elif style == "long":
            c.part()
            c.ellipse(x - r * 0.5, y + r * 0.3, r * 0.75, r * 1.1, HAIR)

    def head(self, c, x, y, r, p):
        k, s = self.k, self.s
        face = s.get("face", "clown")
        c.part()
        c.ellipse(x, y + 1 * k, r, r * 1.02, SKIN if face != "mime" else WHITE)
        if face == "clown":
            c.recolor(lambda xx, yy, m: WHITE if ((xx - x - r * 0.55) / (r * 0.5)) ** 2 + ((yy - y - r * 0.6) / (r * 0.36)) ** 2 <= 1 else m)
        # ear
        c.part()
        c.ellipse(x - r * 0.7, y + r * 0.2, r * 0.2, r * 0.28, SKIN if face != "mime" else WHITE)
        style = s.get("hair_style", "curls")
        if style == "curls":
            c.part()
            c.ellipse(x - r * 0.95, y + r * 0.05, r * 0.25, r * 0.4, HAIR)
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
        c.part(line=False)
        brow = -r * 0.12 if p["eyes"] == "angry" else 0
        c.stroke(bezier((ex - r * 0.35, ey - r * 0.38 - brow), (ex, ey - r * 0.6), (ex + r * 0.3, ey - r * 0.4 + brow)), 1.1 * k, INK)
        if p["eyes"] == "closed":
            c.part(line=False)
            c.stroke(bezier((ex - r * 0.2, ey), (ex, ey - r * 0.15), (ex + r * 0.2, ey)), 1.0 * k, INK)
        else:
            c.part(line=False)
            c.ellipse(ex, ey, r * 0.18, r * 0.27, EYE)
            c.part(line=False)
            c.ellipse(ex + r * 0.07, ey + r * 0.04, r * 0.1, r * 0.17, INK)
        if face != "mime":
            c.part(line=False)
            c.ellipse(x + r * 0.05, y + r * 0.38, r * 0.17, r * 0.12, BLUSH)
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
        if s.get("mustache"):
            for side in (-1, 1):
                c.part()
                c.stroke(bezier((mx + r * 0.05, my - r * 0.25), (mx + r * 0.05 + side * r * 0.4, my - r * 0.15), (mx + r * 0.05 + side * r * 0.55, my - r * 0.4)), 2.2 * k, HAIR if s.get("stache_col") is None else BLACK)
        if s.get("nose", True):
            c.part()
            c.ellipse(x + r * 1.0, y + r * 0.2, r * 0.36, r * 0.34, NOSE)
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
        top = y - r * 0.6
        if kind == "cone":
            c.part()
            c.poly([(x - r * 0.95, top), (x + r * 0.95, top), (x + r * 0.45, top - r * 0.9), (x - r * 0.2, top - r * 1.7), (x - r * 0.65, top - r * 0.8)], HATC)
            c.recolor(lambda xx, yy, m: ACCENT if math.floor((xx + yy * 0.75) / (5 * k)) % 3 == 0 else m)
            c.part()
            c.ellipse(x, top, r * 1.05, r * 0.23, ACCENT)
            c.part()
            c.ellipse(x - r * 0.25, top - r * 1.75, r * 0.25, r * 0.25, WHITE)
        elif kind == "top":
            c.part()
            c.poly([(x - r * 0.62, top), (x + r * 0.62, top), (x + r * 0.58, top - r * 1.3), (x - r * 0.58, top - r * 1.3)], HATC)
            c.recolor(lambda xx, yy, m: ACCENT if abs(yy - (top - r * 0.25)) < r * 0.12 else m)
            c.part()
            c.ellipse(x, top, r * 1.0, r * 0.2, HATC)
        elif kind == "beret":
            c.part()
            c.ellipse(x - r * 0.1, top + r * 0.05, r * 1.0, r * 0.38, HATC)
            c.part()
            c.ellipse(x - r * 0.1, top - r * 0.32, r * 0.12, r * 0.12, HATC)
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


# ----------------------------------------------------------- quadrupeds

class Beast:
    """Tiger and pony: a body, four legs, a head and a tail, in profile."""

    def __init__(self, spec):
        self.s = spec
        self.k = spec.get("k", 1.0)
        self.W = int(spec.get("cw", 300) * self.k)
        self.H = int(spec.get("ch", 200) * self.k)
        self.base = self.H - int(26 * self.k)

    def canvas(self):
        return HDCanvas(self.W, self.H, self.W // 2, self.base)

    def draw(self, p):
        k, s = self.k, self.s
        c = self.canvas()
        tiger = s.get("kind") == "tiger"
        leg = 22 * k * s.get("leg", 1.0)
        body_len = 44 * k * s.get("body", 1.0)
        by = -leg - 14 * k + p.get("by", 0) * k
        crouch = p.get("crouch", 0) * k
        by += crouch
        stretch = p.get("stretch", 1.0)
        hx, hy = body_len * 0.5 * stretch, by - 4 * k + p.get("head_dy", 0) * k     # shoulders
        rx = -body_len * 0.5 * stretch                                            # haunch x
        # tail
        c.part(far=True)
        t = p.get("tail", 0)
        c.stroke(bezier((rx - 6 * k, by - 4 * k), (rx - 30 * k, by - 20 * k + t * k), (rx - 44 * k, by - 34 * k + t * k), 10), 3 * k, FUR)
        if tiger:
            c.recolor(lambda x, y, m: STRIPE if math.floor(x / (5 * k)) % 3 == 0 else m)
        # far legs
        for (lx, phase) in ((hx - 6 * k, p.get("ff", 0)), (rx + 6 * k, p.get("bf", 0))):
            self.limb(c, lx, by + 8 * k, phase, p.get("lift_f", 0), leg, True)
        # body
        c.part()
        c.ellipse(0, by, body_len * 0.62 * stretch, 16 * k, FUR)
        c.ellipse(hx - 4 * k, by - 2 * k, 17 * k, 17 * k, FUR)
        c.ellipse(rx + 6 * k, by - 1 * k, 15 * k, 15 * k, FUR)
        c.recolor(lambda x, y, m: BELLY if y > by + 7 * k else m)
        if tiger:
            c.recolor(lambda x, y, m: STRIPE if m == FUR and (math.floor((x + (y - by) * 0.4) / (7 * k)) % 2 == 0) and abs((x + (y - by) * 0.4) % (7 * k)) < 2.5 * k else m)
        if s.get("saddle"):
            c.part()
            c.ellipse(0, by - 13 * k, 14 * k, 5 * k, CART)
            c.recolor(lambda x, y, m: CART2 if abs(y - (by - 13 * k)) < 1.2 * k else m)
        # near legs
        for (lx, phase) in ((hx - 2 * k, p.get("nf", 0)), (rx + 10 * k, p.get("nb", 0))):
            self.limb(c, lx, by + 10 * k, phase, p.get("lift_n", 0), leg, False)
        # head
        ha = p.get("head_ang", 0)
        hcx, hcy = hx + 16 * k + math.sin(rad(ha)) * 4 * k, hy - 14 * k - p.get("head_up", 0) * k
        if not tiger:   # the pony's neck
            c.part()
            c.capsule(hx, hy, hcx - 2 * k, hcy + 4 * k, 9 * k, 7 * k, FUR)
            c.part()
            c.stroke(bezier((hx - 4 * k, hy - 8 * k), (hcx - 10 * k, hcy - 10 * k), (hcx - 4 * k, hcy - 12 * k)), 3.5 * k, HAIR)
        c.part()
        if tiger:
            c.ellipse(hcx, hcy, 14 * k, 13 * k, FUR)
            c.ellipse(hcx + 11 * k, hcy + 4 * k, 8 * k, 6.5 * k, BELLY)
            c.recolor(lambda x, y, m: STRIPE if m == FUR and abs((y - hcy + 10 * k) % (6 * k)) < 1.6 * k and x < hcx + 6 * k else m)
            for ex in (-6, 4):
                c.part()
                c.ellipse(hcx + ex * k, hcy - 12 * k, 4.5 * k, 5 * k, FUR)
        else:
            c.ellipse(hcx, hcy, 9 * k, 9 * k, FUR)
            c.capsule(hcx, hcy, hcx + 12 * k, hcy + 6 * k, 7 * k, 5.5 * k, FUR)
            c.part()
            c.ellipse(hcx - 2 * k, hcy - 10 * k, 3 * k, 5 * k, FUR)
            c.part()
            c.ellipse(hcx - 4 * k, hcy - 10 * k, 4 * k, 3.5 * k, ACCENT)   # a plume
        # face
        c.part(line=False)
        c.ellipse(hcx + 5 * k, hcy - 3 * k, 2.6 * k, 2.8 * k, EYE if not p.get("angry") else FLAME2)
        c.part(line=False)
        c.ellipse(hcx + 6 * k, hcy - 3 * k, 1.3 * k, 2.2 * k, INK)
        c.part(line=False)
        nose_x = hcx + (18 if tiger else 17) * k
        c.ellipse(nose_x, hcy + 2 * k, 2.5 * k, 2 * k, INK)
        if p.get("mouth") == "open":
            c.part()
            c.ellipse(hcx + 12 * k, hcy + 9 * k, 7 * k, 5 * k, MOUTH)
            if tiger:
                for fx in (8, 15):
                    c.part(line=False)
                    c.poly([(hcx + fx * k, hcy + 5 * k), (hcx + (fx + 2) * k, hcy + 5 * k), (hcx + (fx + 1) * k, hcy + 9 * k)], TOOTH)
        return c

    def limb(self, c, x, y, phase, lift, leg, far):
        k = self.k
        fx = x + math.sin(rad(phase)) * 10 * k
        fy = -max(0.0, math.cos(rad(phase))) * lift * k
        c.part(far=far)
        c.capsule(x, y, fx, fy - 4 * k, 6.5 * k, 5 * k, FUR)
        if self.s.get("kind") == "tiger":
            c.recolor(lambda xx, yy, m: STRIPE if math.floor(yy / (6 * k)) % 3 == 0 else m)
        c.part(far=far)
        if self.s.get("kind") == "tiger":
            c.ellipse(fx + 2 * k, fy - 3 * k, 6.5 * k, 3.5 * k, FUR)
        else:
            c.ellipse(fx + 1 * k, fy - 2.5 * k, 4.5 * k, 3 * k, SOLE)


# ------------------------------------------------------------ hand-drawn

def balloon_dog(p, k=1.0):
    W, H = int(110 * k), int(100 * k)
    c = HDCanvas(W, H, W // 2, H - int(14 * k))
    by = -22 * k + p.get("by", 0) * k
    leg = p.get("leg", 0)
    for lx, ph in ((-14, leg), (-8, -leg), (10, -leg), (16, leg)):
        c.part(far=lx in (-8, 16))
        c.capsule(lx * k, by + 4 * k, lx * k + math.sin(rad(ph)) * 4 * k, -3 * k, 3.6 * k, 3.4 * k, BALLOON)
    c.part()
    c.capsule(-16 * k, by, 14 * k, by, 8 * k, 8 * k, BALLOON)
    c.part()
    c.capsule(-20 * k, by - 2 * k, -28 * k, by - 12 * k + p.get("wag", 0) * k, 3.5 * k, 3 * k, BALLOON)
    c.part()
    c.capsule(18 * k, by - 6 * k, 22 * k, by - 22 * k, 6 * k, 6 * k, BALLOON)
    c.part()
    c.capsule(22 * k, by - 22 * k, 34 * k, by - 18 * k + (3 if p.get("bite") else 0) * k, 5.5 * k, 4.5 * k, BALLOON)
    for ex in (-1, 1):
        c.part()
        c.capsule(20 * k, by - 26 * k, 16 * k + ex * 3 * k, by - 38 * k, 3.5 * k, 3 * k, BALLOON)
    for x, y in ((-2, by), (16, by - 5)):
        c.part(line=False)
        c.ellipse(x * k if isinstance(x, int) else x, y, 2 * k, 3 * k, WHITE)
    c.part(line=False)
    c.ellipse(28 * k, by - 23 * k, 1.6 * k, 1.6 * k, INK)
    return c


def clown_car(p, k=1.0):
    W, H = int(370 * k), int(240 * k)
    c = HDCanvas(W, H, W // 2 + int(14 * k), H - int(16 * k))
    by = p.get("by", 0) * k
    sq = p.get("squash", 0)
    wheel_r = 26 * k
    spin = p.get("wheel", 0)
    # body
    c.part()
    c.poly([(-120 * k, -40 * k + by), (-110 * k, -84 * k + by), (-70 * k, -92 * k + by), (-56 * k, -130 * k + by),
            (40 * k, -132 * k + by), (66 * k, -92 * k + by), (118 * k, -86 * k + by), (126 * k, -44 * k + by),
            (116 * k, -28 * k + by), (-112 * k, -26 * k + by)], CART)
    c.recolor(lambda x, y, m: CART2 if abs(y - (-58 * k + by)) < 4 * k else m)
    # windows
    for (x0, x1) in ((-60, -10), (-2, 46)):
        c.part()
        c.poly([(x0 * k, -96 * k + by), (x1 * k, -96 * k + by), (x1 * k - 6 * k, -126 * k + by), (x0 * k + 6 * k, -126 * k + by)], BELLY)
    # a clown squeezed into every window, hats poking through the roof
    for i, wx in enumerate((-35, 22)):
        if not p.get("faces", True):
            break
        fx, fy = wx * k, -108 * k + by - (i * 2 + (p.get("door", 0) > 0) * 3) * k
        c.part()
        c.ellipse(fx, fy, 12 * k, 12 * k, SKIN)
        c.recolor(lambda x, y, m, fx=fx, fy=fy: WHITE if ((x - fx - 5 * k) / (6 * k)) ** 2 + ((y - fy - 7 * k) / (4 * k)) ** 2 <= 1 else m)
        c.part()
        c.ellipse(fx - 11 * k, fy - 1 * k, 5 * k, 7 * k, HAIR if i == 0 else ACCENT)
        c.part()
        c.ellipse(fx + 11 * k, fy + 2 * k, 4.5 * k, 4.5 * k, NOSE)
        c.part(line=False)
        c.ellipse(fx + 4 * k, fy - 3 * k, 2.2 * k, 3.2 * k, INK)
        c.part(line=False)
        c.stroke(bezier((fx + 1 * k, fy + 7 * k), (fx + 6 * k, fy + 11 * k), (fx + 11 * k, fy + 7 * k)), 1.2 * k, LIP)
        c.part()
        c.poly([(fx - 10 * k, fy - 9 * k), (fx + 10 * k, fy - 9 * k), (fx - 1 * k, fy - 40 * k)], SHOE if i == 0 else HATC)
        c.recolor(lambda x, y, m: ACCENT if math.floor(y / (5 * k)) % 2 == 0 else m)
    # door (opens)
    door = p.get("door", 0)
    c.part()
    if door <= 0:
        c.poly([(-10 * k, -34 * k + by), (40 * k, -34 * k + by), (40 * k, -92 * k + by), (-10 * k, -92 * k + by)], CART)
        c.recolor(lambda x, y, m: CART2 if abs(x - 34 * k) < 2 * k and abs(y - (-66 * k + by)) < 3 * k else m)
    else:
        c.poly([(40 * k, -34 * k + by), (40 * k + 40 * k * door, -40 * k + by), (40 * k + 40 * k * door, -96 * k + by), (40 * k, -92 * k + by)], CART)
        c.part()
        c.poly([(-10 * k, -34 * k + by), (40 * k, -34 * k + by), (40 * k, -92 * k + by), (-10 * k, -92 * k + by)], BLACK)
    # bumpers, lamp, horn
    c.part()
    c.ellipse(124 * k, -38 * k + by, 9 * k, 7 * k, METAL)
    c.part()
    c.ellipse(-120 * k, -36 * k + by, 9 * k, 7 * k, METAL)
    c.part()
    c.ellipse(116 * k, -70 * k + by, 7 * k, 8 * k, FLAME2)
    c.part()
    c.capsule(60 * k, -100 * k + by, 74 * k, -112 * k + by, 3 * k, 6 * k, GOLD)
    c.part()
    c.ellipse(58 * k, -98 * k + by, 6 * k, 6 * k, BLACK)
    # exhaust puff
    if p.get("puff"):
        for i in range(3):
            c.part(line=False)
            c.ellipse(-134 * k - i * 12 * k, -34 * k + by - i * 6 * k, (7 + i * 3) * k, (6 + i * 2) * k, WHITE)
    # wheels
    for wx in (-74, 74):
        c.part()
        c.ellipse(wx * k, -wheel_r * (1 - sq), wheel_r, wheel_r * (1 - sq), BLACK)
        c.part()
        c.ellipse(wx * k, -wheel_r * (1 - sq), wheel_r * 0.55, wheel_r * 0.55 * (1 - sq), ACCENT)
        c.recolor(lambda x, y, m, wx=wx: BLACK if ((math.degrees(math.atan2(y + wheel_r, x - wx * k)) - spin) % 60) < 10 else m)
        c.part()
        c.ellipse(wx * k, -wheel_r * (1 - sq), 5 * k, 5 * k, GOLD)
    if p.get("smoke"):
        for i in range(4):
            c.part(line=False)
            c.ellipse(-20 * k + i * 18 * k, -150 * k - i * 8 * k, (10 + i * 2) * k, (8 + i * 2) * k, BLACK)
    return c


def carriage(p, k=1.0):
    """The Ringmaster's little gilded carriage, pulled by a plumed pony."""
    W, H = int(380 * k), int(230 * k)
    c = HDCanvas(W, H, W // 2 - int(30 * k), H - int(18 * k))
    pony = Beast(dict(kind="pony", k=k * 0.95, fur=(250, 250, 255), hair=(220, 120, 200), accent=(250, 200, 80), saddle=True,
                      leg=1.1))
    by = p.get("by", 0) * k
    # the pony, in front
    pc = pony.draw(dict(ff=p.get("leg", 0), bf=-p.get("leg", 0), nf=-p.get("leg", 0), nb=p.get("leg", 0), lift_f=6, lift_n=6,
                        by=p.get("pony_by", 0)))
    paste(c, pc, 62 * k, 0)
    # shafts
    c.part()
    c.capsule(-20 * k, -54 * k + by, 36 * k, -48 * k, 2.4 * k, 2.4 * k, GOLD)
    # cart body: a scrolled tub
    if not p.get("broken"):
        c.part()
        c.poly([(-90 * k, -40 * k + by), (-20 * k, -40 * k + by), (-8 * k, -64 * k + by), (-14 * k, -96 * k + by),
                (-30 * k, -86 * k + by), (-86 * k, -86 * k + by), (-100 * k, -104 * k + by), (-104 * k, -64 * k + by)], CART)
        c.recolor(lambda x, y, m: CART2 if abs(y - (-84 * k + by)) < 2.5 * k or abs(y - (-44 * k + by)) < 2 * k else m)
        c.part()
        c.ellipse(-52 * k, -64 * k + by, 12 * k, 12 * k, CART2)
        c.recolor(lambda x, y, m: CART if ((x + 52 * k) ** 2 + (y + 64 * k - by) ** 2) < (7 * k) ** 2 else m)
    else:
        c.part()
        c.poly([(-90 * k, -30 * k), (-30 * k, -26 * k), (-40 * k, -56 * k), (-96 * k, -60 * k)], CART)
    wheel_r = 28 * k
    spin = p.get("wheel", 0)
    for wx in (-56,):
        c.part()
        c.ellipse(wx * k, -wheel_r, wheel_r, wheel_r, CART2)
        c.part()
        c.ellipse(wx * k, -wheel_r, wheel_r * 0.8, wheel_r * 0.8, CART)
        c.recolor(lambda x, y, m, wx=wx: CART2 if ((math.degrees(math.atan2(y + wheel_r, x - wx * k)) - spin) % 45) < 8 else m)
        c.part()
        c.ellipse(wx * k, -wheel_r, 5 * k, 5 * k, GOLD)
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
                    hat="cone", hat_col=(80, 190, 210), neck="ruff", shoe=(80, 190, 210), juggles="pin"),
    "unicyclist": dict(k=1.0, legs="unicycle", suit=(240, 150, 40), accent=(60, 130, 220), pattern="vstripes",
                       hair=(90, 60, 40), hat="fez", hat_col=(200, 40, 60), neck="bowtie", shoe=(60, 60, 80), pants=(60, 130, 220)),
    "weightlifter": dict(k=1.12, build="muscle", suit=(210, 50, 60), accent=(250, 240, 240), pattern="leotard", bare_arms=True,
                         gloves=False, hair_style="slick", hair=(40, 30, 30), hat="none", neck="none", mustache=True,
                         face="plain", nose=False, shoe=(60, 40, 50), shoes="boot", pants=(210, 50, 60), cw=240),
    "balloonist": dict(k=1.0, build="round", suit=(70, 180, 120), accent=(250, 250, 250), pattern="dots", hair=(250, 200, 80),
                       hat="bowler", hat_col=(60, 50, 70), neck="bowtie", balloon=(240, 80, 120)),
    "stiltwalker": dict(k=1.0, legs="stilts", suit=(60, 60, 120), accent=(250, 220, 90), pattern="plain", tails=True,
                        hair=(200, 200, 210), hat="top", hat_col=(40, 40, 60), neck="collar", pants=(220, 60, 80), ch=420),
    "mime": dict(k=1.0, face="mime", suit=(30, 30, 40), accent=(245, 245, 250), pattern="stripes", hair_style="none",
                 hat="beret", hat_col=(30, 30, 40), neck="scarf", nose=False, shoe=(30, 30, 40), pants=(30, 30, 40),
                 trim=(230, 60, 70)),
    "tot": dict(k=0.62, build="tiny", suit=(240, 90, 90), accent=(250, 230, 120), pattern="dots", hair=(120, 210, 250),
                hat="cone", hat_col=(120, 210, 250), neck="ruff", cw=260, ch=320),
    # ---- bosses
    "ballrider": dict(k=1.25, suit=(250, 120, 170), accent=(255, 255, 255), pattern="dots", hair=(120, 220, 210),
                      hat="cone", hat_col=(250, 200, 80), neck="ruff", juggles="pin"),
    "trainer": dict(k=1.3, suit=(200, 40, 50), accent=(250, 200, 80), pattern="tux", hair_style="bun", hair=(90, 50, 40),
                    hat="pith", hat_col=(240, 230, 210), neck="collar", face="plain", nose=False, shoes="boot",
                    shoe=(50, 40, 50), pants=(245, 240, 225), lip=(200, 40, 70), cw=300, shift=-40),
    "elastic": dict(k=1.3, suit=(120, 70, 210), accent=(250, 220, 80), pattern="star", leg=1.5, torso=1.25, arm=1.15,
                    hair_style="slick", hair=(250, 230, 120), hat="none", neck="collar", trim=(250, 220, 80),
                    shoe=(250, 220, 80), shoes="boot", ch=340),
    "trapeze_a": dict(k=1.2, suit=(40, 170, 200), accent=(250, 250, 255), pattern="leotard", hair_style="slick", hair=(50, 40, 40),
                      hat="none", neck="none", face="plain", nose=False, mustache=True, shoes="boot", shoe=(250, 250, 255),
                      pants=(40, 170, 200), cuffs=False),
    "trapeze_b": dict(k=1.2, suit=(240, 90, 170), accent=(250, 250, 255), pattern="leotard", hair_style="long", hair=(240, 200, 90),
                      hat="none", neck="none", face="plain", nose=False, shoes="boot", shoe=(250, 250, 255),
                      pants=(240, 90, 170), cuffs=False),
    "fireeater": dict(k=1.3, suit=(120, 40, 40), accent=(250, 170, 60), pattern="vest", hair_style="none", hat="bandana",
                      hat_col=(220, 60, 40), mustache=True, face="plain", nose=False, bare_arms=True, gloves=False,
                      neck="none", pants=(60, 40, 60), shoes="boot", shoe=(60, 40, 50), juggles="torch"),
    "strongman": dict(k=1.5, build="muscle", suit=(240, 200, 120), accent=(250, 240, 200), stripe=(70, 40, 30),
                      pattern="spots", bare_arms=True, gloves=False, hair_style="none", hat="none", neck="none",
                      mustache=True, stache_col=1, face="plain", nose=False, shoes="boot", shoe=(60, 40, 40),
                      pants=(240, 200, 120), cw=260),
    "ringmaster": dict(k=1.35, suit=(200, 30, 50), accent=(252, 204, 72), pattern="tux", tails=True, hair=(40, 30, 30),
                       hair_style="slick", hat="top", hat_col=(40, 30, 50), neck="bowtie", mustache=True, stache_col=1,
                       face="plain", nose=False, shoes="boot", shoe=(40, 30, 40), pants=(245, 240, 230), leg=1.15, torso=1.1,
                       cw=300, shift=-40),
}

BEASTS = {
    "tiger": dict(kind="tiger", k=1.35, fur=(236, 140, 50), stripe=(40, 28, 36), belly=(252, 240, 220)),
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
    # idle: breathing (and juggling, for those who juggle)
    for i in range(4):
        b = [0, 1, 2, 1][i]
        kw = dict(by=b, na=14 + b, fa=-8 - b)
        if j and name != "fireeater":
            kw.update(juggle=i / 4, na=60 + (i % 2) * 20, ne=60, fa=40 + ((i + 1) % 2) * 20, fe=60, item_n=None, item_f=None)
        if name == "fireeater":
            kw.update(juggle=i / 4, na=70 + (i % 2) * 25, ne=50, fa=45 + ((i + 1) % 2) * 25, fe=50, item_n=None, item_f=None)
        if name == "mime":
            kw.update(na=80, ne=70, fa=70, fe=80)                     # palms on the invisible box
        if uni:
            kw.update(na=90, ne=0, fa=-80, fe=0, pedal=i * 20)          # arms out for balance
        if name == "strongman":
            kw.update(na=20, ne=90, fa=-20, fe=90)                    # fists on hips
        if name == "balloonist":
            kw.update(fa=150 + b * 3, fe=10)                           # the bunch held up high
        clips.append(("idle", base(**kw)))
    # walk: a stride (or pedalling, or a stilt step)
    for i in range(6):
        ph = i / 6 * math.tau
        kw = dict(nf=math.sin(ph) * 9, nfl=max(0, math.cos(ph)) * 5, ff=-math.sin(ph) * 9, ffl=max(0, -math.cos(ph)) * 5,
                  by=abs(math.sin(ph)) * 1.5, na=12 - math.sin(ph) * 18, fa=-10 + math.sin(ph) * 18)
        if j and name not in ("fireeater",):
            kw.update(juggle=i / 6, na=60 + (i % 2) * 20, ne=60, fa=40 + ((i + 1) % 2) * 20, fe=60, item_n=None, item_f=None)
        if name == "fireeater":
            kw.update(item_n="torch", item_f="torch")
        if uni:
            kw.update(pedal=i * 60, na=90 + math.sin(ph) * 15, ne=0, fa=-80 - math.sin(ph) * 15, fe=0, nf=0, ff=0)
        if s.get("legs") == "stilts":
            kw.update(nf=math.sin(ph) * 14, ff=-math.sin(ph) * 14)
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
    for i in range(4):
        clips.append(("idle", dict(by=[0, 1, 1, 0][i], tail=[0, 4, 8, 4][i])))
    for i in range(6):
        ph = i / 6 * 360
        clips.append(("walk", dict(nf=ph, nb=ph + 180, ff=ph + 180, bf=ph, lift_n=8, lift_f=8, by=abs(math.sin(rad(ph))) * 2, tail=i * 2)))
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

FRAME_FPS = {"idle": 5, "walk": 10}


def render(name):
    if name in CAST:
        rig = Rig(CAST[name])
        frames = [(clip, rig.draw(p)) for clip, p in humanoid_clips(name)]
        down_src = rig.draw(P(lean=-10, by=2, na=-40, ne=60, fa=-60, fe=60, mouth="o", eyes="closed"))
        frames.append(("down", laid_down(down_src, rig.W // 2, rig.base - int(60 * rig.k), rig.base)))
        m = mats(CAST[name])
        W, H, base = rig.W, rig.H, rig.base
    elif name in BEASTS:
        b = Beast(BEASTS[name])
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
        frames.append(("air", balloon_dog(dict(leg=30))))
        frames.append(("down", balloon_dog(dict(by=10))))
        m = mats(spec)
        W, H, base = frames[0][1].w, frames[0][1].h, frames[0][1].oy
    elif name == "clowncar":
        spec = dict(cart=(220, 40, 60), cart2=(252, 210, 80), accent=(80, 200, 230), hat_col=(120, 200, 90),
                    hair=(250, 140, 60), belly=(170, 220, 250))
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
        spec = dict(cart=(150, 30, 60), cart2=(252, 204, 72), fur=(250, 250, 255), hair=(220, 120, 200), accent=(250, 200, 80))
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
