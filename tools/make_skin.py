"""Generates MapleStory-style digit PNGs for the MapleCombatText image-skin system.

usage: python make_skin.py <out_dir> [--black]
Writes normal_/crit_/taken_ 0-9 + _miss PNGs. --black uses a black outline.
"""
import sys, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

FONT = "C:/Windows/Fonts/comicbd.ttf"
SIZE = 120          # glyph size in px
FAT = 5             # rounded fattening (in face colour)
LINE = 6            # outline thickness

PALETTES = {  # top, middle, bottom, highlight, outline
    "normal": ((255, 247, 140), (255, 172, 28), (238, 72, 18), (255, 255, 215), (70, 26, 6)),
    "crit":   ((255, 196, 228), (246, 82, 168), (214, 18, 112), (255, 232, 246), (74, 6, 40)),
    "taken":  ((236, 210, 255), (172, 104, 250), (110, 40, 214), (248, 236, 255), (36, 8, 78)),
}

def lerp(a, b, t): return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))

def glyph(text, pal, black, font_size=SIZE):
    top, mid, bot, hi, ol = pal
    if black: ol = (0, 0, 0)
    f = ImageFont.truetype(FONT, font_size)
    pad = FAT + LINE + 12
    l, t, r, b = f.getbbox(text, stroke_width=FAT)
    w, h = r - l + pad * 2, b - t + pad * 2
    org = (pad - l, pad - t)

    def mask(stroke):
        m = Image.new("L", (w, h)); ImageDraw.Draw(m).text(org, text, font=f, fill=255, stroke_width=stroke, stroke_fill=255)
        return m

    body = mask(FAT)
    outer = mask(FAT + LINE)
    bb = body.getbbox()

    grad = Image.new("RGB", (w, h)); gd = ImageDraw.Draw(grad)
    for y in range(h):
        v = min(1, max(0, (y - bb[1]) / max(1, bb[3] - bb[1])))
        c = lerp(top, mid, v / 0.5) if v < 0.5 else lerp(mid, bot, (v - 0.5) / 0.5)
        gd.line([(0, y), (w, y)], fill=c)

    out = Image.new("RGBA", (w, h))
    # soft drop shadow
    sh = Image.new("RGBA", (w, h), (0, 0, 0, 0)); sh.putalpha(outer.point(lambda a: a * 0.5))
    sh = sh.filter(ImageFilter.GaussianBlur(2.5))
    out.alpha_composite(ImageChops.offset(sh, 3, 4))
    # outline
    o = Image.new("RGBA", (w, h), ol + (255,)); o.putalpha(outer); out.alpha_composite(o)
    # face
    face = grad.convert("RGBA"); face.putalpha(body); out.alpha_composite(face)
    # glossy top-left inner rim
    shifted = ImageChops.offset(body, 4, 5)
    rim = ImageChops.subtract(body, shifted).filter(ImageFilter.GaussianBlur(1.2))
    hl = Image.new("RGBA", (w, h), hi + (255,)); hl.putalpha(rim.point(lambda a: a * 0.85)); out.alpha_composite(hl)
    # darker bottom-right inner edge for depth
    shifted2 = ImageChops.offset(body, -3, -4)
    edge = ImageChops.subtract(body, shifted2).filter(ImageFilter.GaussianBlur(1.2))
    dk = Image.new("RGBA", (w, h), lerp(bot, ol, 0.35) + (255,)); dk.putalpha(edge.point(lambda a: a * 0.6)); out.alpha_composite(dk)
    return out.crop(out.getbbox())

def main():
    out_dir = sys.argv[1]; black = "--black" in sys.argv
    os.makedirs(out_dir, exist_ok=True)
    for name, pal in PALETTES.items():
        for d in range(10):
            glyph(str(d), pal, black).save(os.path.join(out_dir, f"{name}_{d}.png"))
        glyph("MISS", pal, black, int(SIZE * 0.8)).save(os.path.join(out_dir, f"{name}_miss.png"))
    print("wrote skin to", out_dir)

if __name__ == "__main__":
    main()
