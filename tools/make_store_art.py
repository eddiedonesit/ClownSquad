"""The Steam store's images (docs/STEAM.md §2.7), made from the game's own art.

Background: the circus ring with no menus ("-- keyart window 1920x1080"
renders it to debug/keyart.png). In front: the squad, each clown in their
victory pose with a different weapon, drawn big (whole pixels, no smoothing).
On top: the logo, in the game's display font (Luckiest Guy) with its outline.

Writes store/: capsule_header 920x430, capsule_small 462x174, capsule_main
1232x706, capsule_vertical 748x896, library_capsule 600x900, library_hero
3840x1240 (no logo: Steam lays the logo over it), library_logo 1280x720
(the logo alone, transparent).
Run: python tools/make_store_art.py   (after the keyart render)
"""
import json
import os

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
OUT = os.path.join(ROOT, "store")
FONT = os.path.join(ROOT, "assets", "fonts", "LuckiestGuy-Regular.ttf")
SQUAD = (("pip", ""), ("poppy", "_pie"), ("bingo", "_seltzer"), ("dot", "_balloon"))
GOLD, INK = (255, 214, 72), (26, 12, 32)


def frame(sheet, clip, i=0):
    """One frame of a packed sheet, trimmed to the figure."""
    base = os.path.join(ROOT, "assets", "sprites", sheet)
    meta = json.load(open(base + ".frames.json"))
    im = Image.open(base + ".png").convert("RGBA")
    idx = meta["clips"][clip][i]
    x, y, w, h, ox, oy = meta["rects"][idx]
    f = Image.new("RGBA", (meta["frame_width"], meta["frame_height"]))
    f.alpha_composite(im.crop((x, y, x + w, y + h)), (ox, oy))
    return f.crop(f.getbbox())


def squad(height, rows=1):
    """The four clowns, each `height` px tall: side by side, or two by two (rows=2) for tall images."""
    if rows == 2:
        top, bottom = squad_of(SQUAD[:2], height), squad_of(SQUAD[2:], height)
        w = max(top.width, bottom.width)
        im = Image.new("RGBA", (w, int(top.height * 0.82) + bottom.height))
        im.alpha_composite(top, ((w - top.width) // 2, 0))
        im.alpha_composite(bottom, ((w - bottom.width) // 2, int(top.height * 0.82)))
        return im
    return squad_of(SQUAD, height)


def squad_of(members, height):
    figs = []
    for name, weapon in members:
        f = frame(f"{name}{weapon}_hd", "victory", 0)
        k = max(1, round(height / f.height))
        figs.append(f.resize((f.width * k, f.height * k), Image.NEAREST))
    gap = -int(figs[0].width * 0.12)
    w = sum(f.width for f in figs) + gap * (len(figs) - 1)
    h = max(f.height for f in figs)
    row = Image.new("RGBA", (w, h))
    x = 0
    for f in figs:
        row.alpha_composite(f, (x, h - f.height))
        x += f.width + gap
    return row


def logo(width):
    """CLOWN SQUAD in gold with a thick dark outline and a drop shadow, `width` px wide."""
    size = 200
    font = ImageFont.truetype(FONT, size)
    text = "CLOWN SQUAD"
    l, t, r, b = font.getbbox(text)
    pad = 40
    im = Image.new("RGBA", (r - l + pad * 2, b - t + pad * 2))
    d = ImageDraw.Draw(im)
    d.text((pad - l + 10, pad - t + 14), text, font=font, fill=(0, 0, 0, 150), stroke_width=18, stroke_fill=(0, 0, 0, 150))
    d.text((pad - l, pad - t), text, font=font, fill=GOLD, stroke_width=14, stroke_fill=INK)
    im = im.crop(im.getbbox())
    k = width / im.width
    return im.resize((int(im.width * k), int(im.height * k)), Image.LANCZOS)


def background(w, h, keyart):
    """The ring, cropped to fill w x h around its centre, softened so the squad stands out.
    Only the middle of the key art is used: the title's own clowns stand at its edges."""
    keyart = keyart.crop((int(keyart.width * 0.27), 0, int(keyart.width * 0.73), keyart.height))
    k = max(w / keyart.width, h / keyart.height)
    bg = keyart.resize((int(keyart.width * k) + 1, int(keyart.height * k) + 1), Image.LANCZOS)
    x = (bg.width - w) // 2
    y = int((bg.height - h) * 0.55)
    bg = bg.crop((x, y, x + w, y + h)).filter(ImageFilter.GaussianBlur(max(1, w // 400)))
    shade = Image.new("RGBA", (w, h), (20, 8, 30, 70))
    bg.alpha_composite(shade)
    return bg


def capsule(w, h, keyart, with_logo=True, squad_h=0.55, logo_w=0.8, logo_y=0.05, rows=1):
    im = background(w, h, keyart)
    if squad_h > 0:
        sq = squad(int(h * squad_h), rows)
        if sq.width > w * 0.95:
            k = w * 0.95 / sq.width
            sq = sq.resize((int(sq.width * k), int(sq.height * k)), Image.NEAREST)
        im.alpha_composite(sq, ((w - sq.width) // 2, h - sq.height - int(h * 0.03)))
    if with_logo:
        lg = logo(int(w * logo_w))
        if lg.height > h * 0.38:
            k = h * 0.38 / lg.height
            lg = lg.resize((int(lg.width * k), int(lg.height * k)), Image.LANCZOS)
        im.alpha_composite(lg, ((w - lg.width) // 2, int(h * logo_y)))
    return im.convert("RGB")


def main():
    os.makedirs(OUT, exist_ok=True)
    keyart = Image.open(os.path.join(ROOT, "debug", "keyart.png")).convert("RGBA")
    capsule(920, 430, keyart, squad_h=0.52).save(os.path.join(OUT, "capsule_header_920x430.png"))
    capsule(462, 174, keyart, squad_h=0, logo_w=0.86, logo_y=0.18).save(os.path.join(OUT, "capsule_small_462x174.png"))
    capsule(1232, 706, keyart, squad_h=0.55).save(os.path.join(OUT, "capsule_main_1232x706.png"))
    capsule(748, 896, keyart, squad_h=0.36, logo_w=0.9, logo_y=0.06, rows=2).save(os.path.join(OUT, "capsule_vertical_748x896.png"))
    capsule(600, 900, keyart, squad_h=0.34, logo_w=0.9, logo_y=0.06, rows=2).save(os.path.join(OUT, "library_capsule_600x900.png"))
    capsule(3840, 1240, keyart, with_logo=False, squad_h=0.55).save(os.path.join(OUT, "library_hero_3840x1240.png"))
    lg = logo(1180)
    canvas = Image.new("RGBA", (1280, 720))
    canvas.alpha_composite(lg, ((1280 - lg.width) // 2, (720 - lg.height) // 2))
    canvas.save(os.path.join(OUT, "library_logo_1280x720.png"))
    for f in sorted(os.listdir(OUT)):
        print("wrote store/" + f)


if __name__ == "__main__":
    main()
