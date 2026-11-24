"""Generates the built-in MapleStory-style glyphs for MapleCombatText.

usage: python make_skin.py <out_dir>

Every glyph (0-9, miss, plus) is written as three same-sized layers so the plugin can recolour it:
  <g>_line.png   white outline + black drop shadow   (tinted with the outline colour)
  <g>_face.png   light grey shaded face               (tinted with a top->bottom colour gradient)
  <g>_gloss.png  white rim highlight + dark edge       (drawn untinted on top)
"""
import sys, os
from PIL import Image, ImageDraw, ImageFont, ImageFilter, ImageChops

FONT = "C:/Windows/Fonts/comicbd.ttf"
SIZE = 120   # glyph size in px
FAT = 5      # rounded fattening of the face
LINE = 6     # outline thickness

GLYPHS = {str(d): str(d) for d in range(10)}
GLYPHS["miss"] = "MISS"
GLYPHS["plus"] = "+"

def solid(mask, rgb, alpha_scale=1.0):
    im = Image.new("RGBA", mask.size, rgb + (255,))
    im.putalpha(mask if alpha_scale == 1.0 else mask.point(lambda a: int(a * alpha_scale)))
    return im

def glyph(text, font_size):
    f = ImageFont.truetype(FONT, font_size)
    pad = FAT + LINE + 14
    l, t, r, b = f.getbbox(text, stroke_width=FAT)
    w, h = r - l + pad * 2, b - t + pad * 2
    org = (pad - l, pad - t)

    def mask(stroke):
        m = Image.new("L", (w, h))
        ImageDraw.Draw(m).text(org, text, font=f, fill=255, stroke_width=stroke, stroke_fill=255)
        return m

    body, outer = mask(FAT), mask(FAT + LINE)
    bb = body.getbbox()

    # line layer: soft black shadow, then white outline
    line = Image.new("RGBA", (w, h))
    sh = solid(outer, (0, 0, 0), 0.5).filter(ImageFilter.GaussianBlur(2.5))
    line.alpha_composite(ImageChops.offset(sh, 3, 4))
    line.alpha_composite(solid(outer, (255, 255, 255)))

    # face layer: light grey shading, brighter at the top
    shade = Image.new("L", (w, h))
    sd = ImageDraw.Draw(shade)
    for y in range(h):
        v = min(1, max(0, (y - bb[1]) / max(1, bb[3] - bb[1])))
        sd.line([(0, y), (w, y)], fill=int(255 - 40 * v))
    face = Image.merge("RGBA", (shade, shade, shade, body))

    # gloss layer: white top-left inner rim, dark bottom-right inner edge
    gloss = Image.new("RGBA", (w, h))
    rim = ImageChops.subtract(body, ImageChops.offset(body, 4, 5)).filter(ImageFilter.GaussianBlur(1.2))
    edge = ImageChops.subtract(body, ImageChops.offset(body, -3, -4)).filter(ImageFilter.GaussianBlur(1.2))
    gloss.alpha_composite(solid(edge, (40, 10, 0), 0.35))
    gloss.alpha_composite(solid(rim, (255, 255, 250), 0.8))

    box = line.getbbox()
    return [im.crop(box) for im in (line, face, gloss)]

def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    for name, text in GLYPHS.items():
        size = int(SIZE * 0.8) if name == "miss" else SIZE
        for layer, im in zip(("line", "face", "gloss"), glyph(text, size)):
            im.save(os.path.join(out_dir, f"{name}_{layer}.png"))
    print("wrote glyphs to", out_dir)

if __name__ == "__main__":
    main()
