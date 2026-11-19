#!/usr/bin/env python3
"""Capa do projeto a partir de prints reais do green (.shots/*-capa-<nome>.png, o mais recente de cada).

Prints: capa-limpo (quintal), capa-mundo (demo-world FIM=primeiro), capa-medalhao (demo-calendar MODO=medalhao FIM=meio),
capa-mapa (demo-map MODO=zoom FIM=meio). Saídas: docs/showcase/cover.png (1280x640: topo do README e prévia social do
GitHub) e docs/media/capa/ (matriz 2560x1280 e quadrada 1024 para a miniatura da Workshop).

Uso: cover.py <repo>
"""
import glob
import os
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

repo = sys.argv[1]
FONT = os.path.join(repo, "tools/rec/fonts/Jersey10-Regular.ttf")
GOLD, CREAM, GREY = (240, 217, 160), (245, 238, 220), (196, 190, 204)


def shot(name):
    hits = sorted(glob.glob(os.path.join(repo, ".shots", f"*-capa-{name}.png")))
    if not hits:
        sys.exit(f"falta o print capa-{name} em .shots/")
    return Image.open(hits[-1]).convert("RGB")


def parchment_box(im, around):
    """Retângulo do tooltip (pergaminho) dentro da área aproximada `around`."""
    x0, y0, x1, y1 = around
    px = im.load()
    is_p = lambda c: c[0] > 150 and c[1] > 120 and c[0] - c[2] > 45 and c[0] - c[1] < 70
    cols = [x for x in range(x0, x1) if sum(is_p(px[x, y]) for y in range(y0, y1, 2)) > (y1 - y0) / 2 * 0.35]
    rows = [y for y in range(y0, y1) if sum(is_p(px[x, y]) for x in range(x0, x1, 2)) > (x1 - x0) / 2 * 0.35]
    return (cols[0] - 4, rows[0] - 4, cols[-1] + 5, rows[-1] + 5)


def paste(canvas, piece, xy, shadow=28):
    sh = Image.new("RGBA", (piece.width + shadow * 4, piece.height + shadow * 4), (0, 0, 0, 0))
    ImageDraw.Draw(sh).rectangle((shadow * 2, shadow * 2 + shadow // 2, shadow * 2 + piece.width, shadow * 2 + piece.height + shadow // 2), fill=(0, 0, 0, 200))
    sh = sh.filter(ImageFilter.GaussianBlur(shadow / 1.6))
    canvas.alpha_composite(sh, (xy[0] - shadow * 2, xy[1] - shadow * 2))
    canvas.paste(piece, xy)


def backdrop(clean, size):
    w, h = size
    scale = max(w / clean.width, h / clean.height)
    bg = clean.resize((round(clean.width * scale), round(clean.height * scale)), Image.NEAREST)
    bg = bg.crop(((bg.width - w) // 2, (bg.height - h) // 2, (bg.width - w) // 2 + w, (bg.height - h) // 2 + h))
    bg = bg.filter(ImageFilter.GaussianBlur(5)).convert("RGBA")
    return Image.alpha_composite(bg, Image.new("RGBA", size, (14, 12, 20, 150)))


def text(draw, xy, s, size, color, anchor="la"):
    font = ImageFont.truetype(FONT, size)
    draw.text((xy[0] + size // 16, xy[1] + size // 16), s, font=font, fill=(0, 0, 0, 230), anchor=anchor)
    draw.text(xy, s, font=font, fill=color, anchor=anchor)


clean, world, wheel, gmap = shot("limpo"), shot("mundo"), shot("medalhao"), shot("mapa")
tip = world.crop(parchment_box(world, (380, 140, 820, 520)))
week = wheel.crop(parchment_box(wheel, (80, 220, 930, 620)))
window = gmap.crop((308, 118, 1612, 962))
print("recortes:", tip.size, week.size, window.size)

# Matriz 2560x1280 na escala do jogo (2 px por pixel de arte); a de 1280x640 é a metade exata.
wide = backdrop(clean, (2560, 1280))
paste(wide, window, (1176, 318))
paste(wide, week, (1070, 84))
paste(wide, tip, (836, 838))
d = ImageDraw.Draw(wide)
text(d, (112, 268), "GK2 HUD+", 262, GOLD)
text(d, (118, 560), "Quality-of-life mods", 92, CREAM)
text(d, (118, 650), "for Graveyard Keeper 2", 92, CREAM)
text(d, (118, 1090), "Looks native. No spoilers.", 64, GREY)
text(d, (118, 1156), "Every feature toggles in game.", 64, GREY)

square = backdrop(clean, (2048, 2048))
paste(square, window.crop((0, 0, 1304, 844)), (372, 1010))
paste(square, week, (120, 640))
paste(square, tip, (1560, 560))
d = ImageDraw.Draw(square)
text(d, (1024, 250), "GK2 HUD+", 420, GOLD, "mm")
text(d, (1024, 480), "Quality-of-life mods for Graveyard Keeper 2", 96, CREAM, "mm")

out = os.path.join(repo, "docs/media/capa")
os.makedirs(out, exist_ok=True)
wide.convert("RGB").save(os.path.join(out, "capa-2560x1280.png"), optimize=True)
half = wide.convert("RGB").resize((1280, 640), Image.NEAREST)
half.save(os.path.join(repo, "docs/showcase/cover.png"), optimize=True)
half.save(os.path.join(out, "capa-github-1280x640.png"), optimize=True)
square.convert("RGB").resize((1024, 1024), Image.NEAREST).save(os.path.join(out, "capa-workshop-1024.png"), optimize=True)
print("capas:", os.path.join(repo, "docs/showcase/cover.png"), out)
