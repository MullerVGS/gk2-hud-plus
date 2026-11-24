"""Recorta o livro (sem o atril nem os cantos de metal soltos da linha 34) do render do Folio (docs/cenarios/folio-render.cs) para GK2Crafting/art/folio_book.png.

Só pixels originais: x 19-43 e y 16-33 do render de 64x64 (x 18 e 44-45 são as laterais do atril); da linha 32 para baixo
sai a madeira do atril (nem azul da capa nem claro dos cantos de metal).
Uso: python3 tools/art/folio-livro.py <folio-render.png>
"""
import colorsys
import sys
from pathlib import Path

from PIL import Image

im = Image.open(sys.argv[1]).convert("RGBA")
x0, y0, x1, y1 = 19, 16, 44, 34
c = im.crop((x0, y0, x1, y1)).copy()
px = c.load()
for y in range(c.height):
    for x in range(c.width):
        r, g, b, a = px[x, y]
        if a and y + y0 >= 32:
            _, l, _ = colorsys.rgb_to_hls(r / 255, g / 255, b / 255)
            if not (b > r + 8) and not (l > 0.5):
                px[x, y] = (0, 0, 0, 0)
c = c.crop(c.getbbox())
out = Path(__file__).resolve().parents[2] / "GK2Crafting/art/folio_book.png"
c.save(out)
print(out, c.size)
