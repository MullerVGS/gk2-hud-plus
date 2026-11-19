#!/usr/bin/env python3
"""Mídias de divulgação a partir dos clipes do `gk2 rec` (docs/media/<Id>.mp4).

  webp     docs/showcase/<Id>.webp: os clipes do README (WebP animado; clipe largo sai com pixel de arte = 1 px)
  trailer  docs/media/reddit/trailer.mp4: os clipes em sequência, 1920x1080, com legenda (roteiro em TRAILER)
  gameplay docs/media/reddit/gameplay.mp4: clipe curto a 60 qps com as tomadas de tela inteira (roteiro em GAMEPLAY),
           gravadas antes com `gk2 rec-clip <demo> 0,0,1920,1080 <nome> --fps 60 --escala 2 --tomada`

Uso: showcase.py <repo> [webp [Id ...]|trailer|gameplay|tudo]
"""
import os
import subprocess
import sys
import tempfile
from concurrent.futures import ThreadPoolExecutor

from PIL import Image, ImageDraw, ImageFont

repo = sys.argv[1]
what = sys.argv[2] if len(sys.argv) > 2 else "tudo"
MEDIA = os.path.join(repo, "docs/media")
SHOWCASE = os.path.join(repo, "docs/showcase")
OUT = os.path.join(MEDIA, "reddit")
FONT = os.path.join(repo, "tools/rec/fonts/Jersey10-Regular.ttf")
W, H, FPS = 1920, 1080, 30
BOX_W, BOX_H, BOX_TOP = 1800, 840, 40          # área do clipe; a legenda fica abaixo
GOLD, GREY, DARK = (240, 217, 160), (190, 184, 200), (23, 21, 28)

# Ordem do trailer: (clipe, título, frase). Clipe terminado em .png é imagem parada.
TRAILER = [
    ("MapPlaces", "Look inside places from the map", "Zoom into a yellow arrow to peek inside a place you have visited"),
    ("Map", "Map zoom", "Mouse wheel zoom, all the way down to the world. Only what you have explored"),
    ("Tooltips", "Richer tooltips", "More numbers on items, stations and characters. Middle click pins one"),
    ("World", "Tooltips in the world", "Rest the mouse on a craft, a garden bed or a body to see how long is left"),
    ("Wheel", "Time and week on the day medallion", "Hover the medallion to see the time and what each day brings"),
    ("CalendarTab", "Calendar tab and journal", "The days, their events and a diary of what you unlocked and finished"),
    ("Quests", "Quest tree", "NPC names on the tree. Click a quest to read how you got there"),
    ("MapOrders", "Town orders on the map", "Hover the orders icon to see the item of each order"),
    ("HudDayPlate", "HUD numbers and day plate", "Energy and insanity as numbers, plus the day and time"),
    ("SaveLoad", "Save and Load in the pause menu", "Optional, off by default"),
    ("Refund", "Zombie perk refund", "Optional, off by default. Right-click a bought perk to get its points back"),
    ("../showcase/menu.png", "Everything toggles in the pause menu", "Each feature turns on and off without restarting the game"),
]
STILL_SECONDS = 4.0

# Clipe curto: (tomada em docs/media/tomadas, início s, fim s, rótulo). Emendas com fusão de FADE segundos.
GAMEPLAY = [
    ("world", 1.5, 7.5, "Tooltips in the world"),
    ("medalhao", 1.0, 7.0, "The week on the day medallion"),
    ("tooltips", 2.5, 8.5, "Richer tooltips, pinned and nested"),
    ("mapa", 0.5, 9.2, "Zoom the map into places you have visited"),
]
FADE = 0.35
SANS = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"


def run(cmd):
    subprocess.run(cmd, check=True)


def probe(path):
    out = subprocess.check_output(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
                                   "stream=width,height", "-of", "csv=p=0", path], text=True)
    return tuple(int(v) for v in out.strip().split(","))


def webp(cid):
    src = os.path.join(MEDIA, cid + ".mp4")
    w, _ = probe(src)
    vf = "scale=iw/2:ih/2:flags=neighbor" if w > 640 else "null"
    run(["ffmpeg", "-v", "error", "-y", "-i", src, "-vf", vf, "-c:v", "libwebp_anim", "-lossless", "0", "-q:v", "80",
         "-compression_level", "4", "-loop", "0", os.path.join(SHOWCASE, cid + ".webp")])
    return cid


def text_layer(path, lines, background=None):
    """lines: (texto, tamanho, cor, y do centro)."""
    im = Image.new("RGBA", (W, H), background + (255,) if background else (0, 0, 0, 0))
    draw = ImageDraw.Draw(im)
    for text, size, color, y in lines:
        font = ImageFont.truetype(FONT, size)
        draw.text((W // 2 + 3, y + 3), text, font=font, fill=(0, 0, 0, 200), anchor="mm")
        draw.text((W // 2, y), text, font=font, fill=color + (255,), anchor="mm")
    im.save(path)


def card(tmp, name, lines, seconds):
    png, out = os.path.join(tmp, name + ".png"), os.path.join(tmp, name + ".mp4")
    text_layer(png, lines, DARK)
    run(["ffmpeg", "-v", "error", "-y", "-loop", "1", "-t", str(seconds), "-i", png, "-r", str(FPS),
         "-c:v", "libx264", "-crf", "16", "-preset", "slow", "-pix_fmt", "yuv420p", out])
    return out


def segment(tmp, index, clip, title, line):
    src = os.path.join(MEDIA, clip if clip.endswith(".png") else clip + ".mp4")
    w, h = probe(src)
    scale = max(1, min(BOX_W // w, BOX_H // h))
    if w > BOX_W or h > BOX_H:                    # imagem maior que a área: reduz pela metade (pixel de arte = 1 px)
        w, h, scale = w // 2, h // 2, 1
        pre = "scale=iw/2:ih/2:flags=neighbor,"
    else:
        pre = ""
    cw, ch = w * scale, h * scale
    x, y = (W - cw) // 2, BOX_TOP + (BOX_H - ch) // 2
    caption = os.path.join(tmp, f"cap{index}.png")
    text_layer(caption, [(title, 76, GOLD, 940), (line, 44, GREY, 1012)])
    graph = (
        f"[0:v]{pre}fps={FPS},split[a][b];"
        f"[a]scale={W}:{H}:force_original_aspect_ratio=increase,crop={W}:{H},boxblur=30:2,eq=brightness=-0.32:saturation=0.7[bg];"
        f"[b]scale={cw}:{ch}:flags=neighbor,pad={cw + 8}:{ch + 8}:4:4:color=0x0c0b0f[fg];"
        f"[bg][fg]overlay={x - 4}:{y - 4}[v];[v][1:v]overlay=0:0,format=yuv420p[out]"
    )
    out = os.path.join(tmp, f"seg{index}.mp4")
    source = ["-loop", "1", "-t", str(STILL_SECONDS), "-i", src] if clip.endswith(".png") else ["-i", src]
    run(["ffmpeg", "-v", "error", "-y", *source, "-i", caption, "-filter_complex", graph, "-map", "[out]",
         "-r", str(FPS), "-c:v", "libx264", "-crf", "16", "-preset", "slow", out])
    return out


def trailer():
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        intro = card(tmp, "intro", [("GK2 HUD+", 220, GOLD, 470),
                                    ("Quality-of-life mods for Graveyard Keeper 2", 64, GREY, 640)], 2.5)
        with ThreadPoolExecutor(4) as pool:
            parts = list(pool.map(lambda a: segment(tmp, *a), [(i, *t) for i, t in enumerate(TRAILER)]))
        outro = card(tmp, "outro", [("GK2 HUD+", 160, GOLD, 440),
                                    ("github.com/MullerVGS/gk2-hud-plus", 72, GREY, 600),
                                    ("Looks native. Shows only what you have already discovered.", 48, GREY, 700)], 3.5)
        listing = os.path.join(tmp, "list.txt")
        with open(listing, "w") as f:
            for p in [intro, *parts, outro]:
                f.write(f"file '{p}'\n")
        target = os.path.join(OUT, "trailer.mp4")
        run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy",
             "-movflags", "+faststart", target])
        print("trailer:", target)


def gameplay():
    os.makedirs(OUT, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        inputs, graph, offset, last = [], [], 0.0, None
        for i, (take, start, end, label) in enumerate(GAMEPLAY):
            png = os.path.join(tmp, f"label{i}.png")
            im = Image.new("RGBA", (W, H), (0, 0, 0, 0))
            draw = ImageDraw.Draw(im)
            font = ImageFont.truetype(SANS, 34)
            box = draw.textbbox((0, 0), label, font=font)
            tw, th = box[2] - box[0], box[3] - box[1]
            x, y = (W - tw) // 2, H - 96
            draw.rounded_rectangle((x - 26, y - 16, x + tw + 26, y + th + 26), 14, fill=(12, 11, 15, 190))
            draw.text((x, y), label, font=font, fill=(245, 240, 228, 255))
            im.save(png)
            inputs += ["-ss", str(start), "-t", str(end - start), "-i", os.path.join(MEDIA, "tomadas", take + ".mp4"),
                       "-loop", "1", "-t", str(end - start), "-i", png]
            graph.append(f"[{2 * i}:v]fps=60,setpts=PTS-STARTPTS[c{i}];[c{i}][{2 * i + 1}:v]overlay=0:0:shortest=1,format=yuv420p[s{i}]")
            if last is None:
                last, offset = f"s{i}", end - start
            else:
                graph.append(f"[{last}][s{i}]xfade=transition=fade:duration={FADE}:offset={offset - FADE:.3f}[x{i}]")
                last, offset = f"x{i}", offset + (end - start) - FADE
        target = os.path.join(OUT, "gameplay.mp4")
        run(["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(graph), "-map", f"[{last}]", "-r", "60",
             "-c:v", "libx264", "-crf", "17", "-preset", "slow", "-pix_fmt", "yuv420p", "-movflags", "+faststart", target])
        print("gameplay:", target)


if what == "gameplay":
    gameplay()
if what in ("webp", "tudo"):
    os.makedirs(SHOWCASE, exist_ok=True)
    ids = sys.argv[3:] or sorted(f[:-4] for f in os.listdir(MEDIA) if f.endswith(".mp4"))
    with ThreadPoolExecutor(6) as pool:
        print("webp:", " ".join(pool.map(webp, ids)))
if what in ("trailer", "tudo"):
    trailer()
