"""Steam achievement icons (docs/STEAM.md §2.2), made from the game's own art.

Each achievement gets a 256x256 rosette badge (a scalloped circus rosette in its
group's colour) with a picture in the middle: the boss's face for WORLD n
TAMED, the weapon for (weapon) CHAMP, a clown, a number or a prop for the
rest. Steam also wants a greyed icon for one not yet earned: the same badge,
desaturated and darkened.

The list is read from scripts/Platform/Achievements.cs, so a new achievement
gets an icon (a plain star until it is given a picture here).
Writes store/achievements/<id>.png and <id>_locked.png.
Run: python tools/make_achievement_icons.py
"""
import json
import math
import os
import re
import sys

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageFont, ImageOps

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "store", "achievements")
FONT = os.path.join(ROOT, "assets", "fonts", "LuckiestGuy-Regular.ttf")
S = 256
INK = (26, 12, 32, 255)

WEAPONS = ["mallet", "hammer", "seltzer", "pie", "pins", "slapstick", "popgun", "balloonsword"]
SPIN = {"balloonsword": "balloon"}
BOSSES = ["bertie", "ballrider", "trainer", "clowncar", "elastic", "trapeze_a", "fireeater", "tot", "strongman", "ringmaster"]


def achievement_ids():
    src = open(os.path.join(ROOT, "scripts", "Platform", "Achievements.cs"), encoding="utf-8").read()
    ids = re.findall(r'new\("([a-z0-9_]+)", "', src)
    ids += [f"boss_{w}" for w in range(1, 11)]
    ids += [f"win_{w}" for w in WEAPONS]
    return ids


# ------------------------------------------------------------- pictures

def sheet_frame(path, clip, i=0, frames_json=None):
    meta = json.load(open(frames_json or path.replace(".png", ".frames.json")))
    im = Image.open(path).convert("RGBA")
    idx = meta["clips"][clip][i]
    fw, fh = meta["frame_width"], meta["frame_height"]
    if "rects" in meta:
        x, y, w, h, ox, oy = meta["rects"][idx]
        f = Image.new("RGBA", (fw, fh))
        f.alpha_composite(im.crop((x, y, x + w, y + h)), (ox, oy))
    else:
        f = im.crop((idx * fw, 0, idx * fw + fw, fh))
    return f.crop(f.getbbox())


def boss_face(kind):
    """A boss's head (or the whole thing, for the car)."""
    if kind == "bertie":
        f = sheet_frame(os.path.join(ROOT, "assets", "sprites", "boss_hd.png"), "idle")
        return f.crop((0, 0, f.width, int(f.height * 0.62)))
    if kind == "clowncar":
        return sheet_frame(os.path.join(ROOT, "assets", "sprites", "cast", "clowncar.png"), "idle")
    import gen_cast_hd as g
    spec = g.CAST[kind]
    rig = g.Rig(spec)
    cv = rig.draw(g.P(juggle=None, item_n=None, item_f=None))
    px, _ = g.shade(cv, g.mats(spec), normals=True)
    im = Image.new("RGBA", (rig.W, rig.H))
    im.putdata([tuple(c) for row in px for c in row])
    bb = im.getbbox()
    head = im.crop((bb[0], bb[1], bb[2], bb[1] + int((bb[3] - bb[1]) * (0.5 if kind in ("tot", "ballrider") else 0.36))))
    return head.crop(head.getbbox())


def weapon(kind):
    name = SPIN.get(kind, kind)
    return sheet_frame(os.path.join(ROOT, "assets", "sprites", f"{name}_spin_hd.png"), "mallet_spin", 1)


def clown(name, clip="victory"):
    return sheet_frame(os.path.join(ROOT, "assets", "sprites", f"{name}_hd.png"), clip)


def portrait(name):
    im = Image.open(os.path.join(ROOT, "assets", "sprites", f"portrait_{name}.png")).convert("RGBA")
    return im.crop(im.getbbox())


def file_art(rel, crop=None):
    im = Image.open(os.path.join(ROOT, rel)).convert("RGBA")
    if crop:
        im = im.crop(crop)
    return im.crop(im.getbbox())


def number(text, color=(255, 214, 72, 255)):
    font = ImageFont.truetype(FONT, 150)
    l, t, r, b = font.getbbox(text)
    im = Image.new("RGBA", (r - l + 40, b - t + 40))
    ImageDraw.Draw(im).text((20 - l, 20 - t), text, font=font, fill=color, stroke_width=10, stroke_fill=INK)
    return im.crop(im.getbbox())


def star(color=(255, 214, 72, 255)):
    im = Image.new("RGBA", (200, 200))
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        r = 92 if i % 2 == 0 else 40
        pts.append((100 + math.cos(a) * r, 100 + math.sin(a) * r))
    d = ImageDraw.Draw(im)
    d.polygon(pts, fill=color, outline=INK, width=8)
    return im


def pair(a, b):
    w = a.width + b.width - int(min(a.width, b.width) * 0.2)
    h = max(a.height, b.height)
    im = Image.new("RGBA", (w, h))
    im.alpha_composite(a, (0, h - a.height))
    im.alpha_composite(b, (w - b.width, h - b.height))
    return im


def picture(aid):
    if aid.startswith("boss_"):
        return boss_face(BOSSES[int(aid[5:]) - 1]), (196, 40, 58)
    if aid.startswith("win_"):
        return weapon(aid[4:]), (60, 120, 210)
    table = {
        "first_clear": (lambda: clown("pip"), (240, 150, 40)),
        "bonus_clear": (lambda: file_art("assets/arena/loot_icons.png", (0, 0, 64, 64)), (220, 80, 160)),
        "flawless": (lambda: star((250, 250, 255, 255)), (90, 180, 230)),
        "secret_shop": (lambda: file_art("assets/arena/secret_curtain.png"), (130, 60, 190)),
        "legendary": (lambda: file_art("assets/arena/loot_icons.png", (192, 0, 256, 64)), (230, 170, 30)),
        "combo_25": (lambda: number("25"), (230, 70, 150)),
        "combo_50": (lambda: number("50"), (230, 70, 150)),
        "level_10": (lambda: number("10", (150, 230, 255, 255)), (60, 160, 120)),
        "level_25": (lambda: number("25", (150, 230, 255, 255)), (60, 160, 120)),
        "level_50": (lambda: number("50", (150, 230, 255, 255)), (60, 160, 120)),
        "full_tree": (lambda: star(), (60, 160, 120)),
        "all_acts": (lambda: boss_face("ballrider") if False else clown("dot"), (130, 60, 190)),
        "hard_boss": (lambda: boss_face("strongman"), (30, 30, 40)),
        "rush_clear": (lambda: pair(boss_face("ringmaster"), boss_face("fireeater")), (196, 40, 58)),
        "rush_fast": (lambda: number("20"), (196, 40, 58)),
        "together": (lambda: pair(clown("pip"), clown("poppy")), (240, 150, 40)),
        "ring_bell": (lambda: file_art("assets/sprites/fx/high_striker.png", (56, 0, 112, 208)), (230, 170, 30)),
        "daily_clear": (lambda: clown("bingo"), (90, 180, 230)),
    }
    make, col = table.get(aid, (lambda: star(), (120, 110, 140)))
    return make(), col


# ---------------------------------------------------------------- badge

def badge(art, color):
    im = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(im)
    c = S / 2
    # the rosette: scallops round the edge, a ring, a darker centre
    pts = []
    for i in range(96):
        a = i / 96 * math.tau
        r = 124 - 7 * (0.5 + 0.5 * math.cos(a * 16))
        pts.append((c + math.cos(a) * r, c + math.sin(a) * r))
    d.polygon(pts, fill=color + (255,), outline=INK, width=5)
    light = tuple(min(255, int(v * 1.3 + 40)) for v in color)
    d.ellipse((c - 100, c - 100, c + 100, c + 100), fill=light + (255,), outline=INK, width=5)
    dark = tuple(int(v * 0.35) for v in color)
    d.ellipse((c - 88, c - 88, c + 88, c + 88), fill=dark + (255,))
    # the picture, whole-pixel scaled to fit the centre, with an outline and a shadow
    box = 150
    k = box / max(art.width, art.height)
    if k >= 1:
        # whole pixels: the nearest whole size that still fits the centre (176 px)
        k = max(1, round(k))
        while k > 1 and max(art.width, art.height) * k > 176:
            k -= 1
        art = art.resize((art.width * k, art.height * k), Image.NEAREST)
    else:
        art = art.resize((max(1, int(art.width * k)), max(1, int(art.height * k))), Image.LANCZOS)
    shadow = Image.new("RGBA", art.size, (0, 0, 0, 0))
    shadow.putalpha(art.getchannel("A").point(lambda a: 110 if a > 0 else 0))
    pos = (int(c - art.width / 2), int(c - art.height / 2))
    im.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(3)), (pos[0] + 5, pos[1] + 6))
    im.alpha_composite(art, pos)
    return im


def locked(im):
    rgb = ImageOps.grayscale(im.convert("RGB")).convert("RGB")
    rgb = ImageEnhance.Brightness(rgb).enhance(0.55)
    out = rgb.convert("RGBA")
    out.putalpha(im.getchannel("A"))
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    ids = achievement_ids()
    for aid in ids:
        art, col = picture(aid)
        b = badge(art, col)
        b.save(os.path.join(OUT, f"{aid}.png"))
        locked(b).save(os.path.join(OUT, f"{aid}_locked.png"))
    # a contact sheet, to look them over
    cols = 9
    rows = (len(ids) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * 132, rows * 132), (40, 30, 55, 255))
    for i, aid in enumerate(ids):
        sheet.alpha_composite(Image.open(os.path.join(OUT, f"{aid}.png")).resize((128, 128), Image.LANCZOS), ((i % cols) * 132 + 2, (i // cols) * 132 + 2))
    sheet.save(os.path.join(ROOT, "debug", "achievement_icons.png"))
    print(f"wrote {len(ids)} icons (and locked versions) to store/achievements/")


if __name__ == "__main__":
    main()
