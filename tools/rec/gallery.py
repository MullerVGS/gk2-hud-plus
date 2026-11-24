#!/usr/bin/env python3
"""Galeria da página da Steam Workshop: cartões 1920x1080 com legenda, a partir de prints do green, das tomadas de
tela inteira (docs/media/tomadas) e dos clipes do `gk2 rec` (docs/media/<Id>.mp4).

Saída: docs/media/workshop/NN-nome.png (ou .jpg quando o PNG passa de 1,9 MB) (não versionada; sobe pela página do item, "Add/edit images & videos").
Uso: gallery.py <repo>
"""
import glob
import os
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFilter, ImageFont

repo = sys.argv[1]
FONT = os.path.join(repo, "tools/rec/fonts/Jersey10-Regular.ttf")
GOLD, CREAM = (240, 217, 160), (245, 238, 220)
MEDIA = os.path.join(repo, "docs/media")
OUT = os.path.join(MEDIA, "workshop")
W, H = 1920, 1080

# (nome, fonte, título, legenda). Fonte: "shot:<sufixo em .shots>", "take:<tomada>@<s>" (tela inteira) ou
# "clip:<Id>@<s>" (região do clipe, posta em tamanho real sobre o quintal desfocado); "clip:<Id>@<s>#x,y,w,h" recorta a
# região (px do clipe, pares) para tirar o escuro em volta da janela.
CARDS = [
    ("world-garden", "shot:capa-mundo", "Tooltips in the world", "Hover for the essentials. Pin it for the chance per cell and the harvest so far"),
    ("world-station", "shot:segue-a", "What every station is doing", "Progress, time left, the queue and the fuel, in the game's own widgets"),
    ("tooltips", "take:tooltips@7.2", "Richer tooltips, pinned and nested", "The recipe in item cells. Highlighted terms open another tooltip inside"),
    ("wheel", "take:medalhao@3.2", "The week on the day medallion", "What is coming and when, without opening anything"),
    ("map", "take:zoom@5.2", "Zoom the map down to the world", "Mouse wheel zoom, only over what you have explored"),
    ("places", "take:mapa@5.2", "Look inside places you have visited", "Hover an object for its name, click to see what it holds"),
    ("calendar", "clip:CalendarTab@5.0", "A calendar tab", "The days, their events and what you did on each one"),
    ("quests", "clip:Quests@5.0", "Quest tree with names", "Click a quest to read how you got there"),
    ("item-picker", "clip:ItemFinder@7.0#40,100,660,650", "Available items first", "Picking from your chests: what fits comes first, added up and sorted for that station"),
    ("folio", "clip:FolioButton@3.0#40,10,950,660", "The Folio at the alchemy table", "Once it is built, its formulas are one click away from the ingredients"),
]


def frame(path, at):
    tmp = os.path.join(OUT, "_frame.png")
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", str(at), "-i", path, "-frames:v", "1", tmp], check=True)
    im = Image.open(tmp).convert("RGB")
    os.remove(tmp)
    return im


def source(spec):
    kind, _, rest = spec.partition(":")
    if kind == "shot":
        hits = sorted(glob.glob(os.path.join(repo, ".shots", f"*-{rest}.png")))
        if not hits:
            sys.exit(f"falta o print {rest} em .shots/")
        return Image.open(hits[-1]).convert("RGB"), True
    rest, _, crop = rest.partition("#")
    name, _, at = rest.partition("@")
    if kind == "take":
        im = frame(os.path.join(MEDIA, "tomadas", name + ".mp4"), float(at))
        # A tomada guarda 1 pixel de arte por pixel e volta ao tamanho de tela: refaz a dobra sem o borrão do vídeo.
        return im.resize((W // 2, H // 2), Image.BOX).resize((W, H), Image.NEAREST), True
    im = frame(os.path.join(MEDIA, name + ".mp4"), float(at))
    if crop:
        x, y, w, h = (int(v) for v in crop.split(","))
        im = im.crop((x, y, x + w, y + h))
    return im, False


def backdrop():
    hits = sorted(glob.glob(os.path.join(repo, ".shots", "*-capa-limpo.png")))
    bg = Image.open(hits[-1]).convert("RGB").resize((W, H), Image.NEAREST).filter(ImageFilter.GaussianBlur(6)).convert("RGBA")
    return Image.alpha_composite(bg, Image.new("RGBA", (W, H), (14, 12, 20, 150)))


def caption(card, title, sub):
    band = Image.new("RGBA", (W, 150), (0, 0, 0, 0))
    d = ImageDraw.Draw(band)
    for y in range(150):   # degradê: a legenda lê sobre qualquer cena sem tapar o jogo com uma faixa dura
        d.line([(0, y), (W, y)], fill=(10, 8, 14, int(215 * min(1, y / 70))))
    card.alpha_composite(band, (0, H - 150))
    d = ImageDraw.Draw(card)
    for s, size, color, y in ((title, 72, GOLD, H - 104), (sub, 44, CREAM, H - 44)):
        font = ImageFont.truetype(FONT, size)
        d.text((W // 2 + 3, y + 3), s, font=font, fill=(0, 0, 0, 230), anchor="mm")
        d.text((W // 2, y), s, font=font, fill=color, anchor="mm")


os.makedirs(OUT, exist_ok=True)
for old in glob.glob(os.path.join(OUT, "*.png")) + glob.glob(os.path.join(OUT, "*.jpg")):
    os.remove(old)
for n, (name, spec, title, sub) in enumerate(CARDS, 1):
    im, full = source(spec)
    if full:
        card = im.resize((W, H), Image.NEAREST).convert("RGBA")
    else:
        card = backdrop()
        x, y = (W - im.width) // 2 // 2 * 2, (H - 130 - im.height) // 2 // 2 * 2
        sh = Image.new("RGBA", (im.width + 120, im.height + 120), (0, 0, 0, 0))
        ImageDraw.Draw(sh).rectangle((60, 74, 60 + im.width, 74 + im.height), fill=(0, 0, 0, 210))
        card.alpha_composite(sh.filter(ImageFilter.GaussianBlur(18)), (x - 60, y - 60))
        card.paste(im, (x, y))
    caption(card, title, sub)
    path = os.path.join(OUT, f"{n:02d}-{name}.png")
    card.convert("RGB").save(path, optimize=True)
    if os.path.getsize(path) > 1_900_000:   # folga para o limite de upload da Steam: cena cheia vai em JPEG sem subamostragem
        os.remove(path)
        path = path[:-4] + ".jpg"
        card.convert("RGB").save(path, quality=95, subsampling=0, optimize=True)
    print(os.path.getsize(path) // 1024, "KB", path)
