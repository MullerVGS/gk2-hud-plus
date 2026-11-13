"""Roda a bateria de tools/update/smoke.txt no green e imprime o resultado em markdown.

Uso: smoke.py <gk2> <smoke.txt> <pasta de trabalho> <prefixo dos prints>
Sai com 1 se algum cenário falhar.
"""
import json
import re
import subprocess
import sys
from pathlib import Path

gk2, listing, work, prefix = sys.argv[1:5]
work = Path(work)
work.mkdir(parents=True, exist_ok=True)
fails = 0
for n, raw in enumerate(Path(listing).read_text(encoding="utf-8").splitlines()):
    if not raw.strip() or raw.lstrip().startswith("#"):
        continue
    spec, expect, shot = [p.strip() for p in (raw.split("|") + ["", ""])[:3]]
    path, *consts = spec.split()
    src = Path(path).read_text(encoding="utf-8")
    for c in consts:
        key, val = c.split("=", 1)
        rx = re.compile(r"((?:const|static)\s+(string|bool|int|float)\s+" + re.escape(key) + r"\s*=\s*)[^;]*;")
        m = rx.search(src)
        if not m:
            print(f"- FALHOU `{spec}`: constante {key} não existe no cenário")
            fails += 1
            break
        lit = json.dumps(val, ensure_ascii=False) if m.group(2) == "string" else val
        src = rx.sub(lambda mm: mm.group(1) + lit + ";", src, count=1)
    else:
        f = work / f"{n:03d}-{Path(path).stem}.cs"
        f.write_text(src, encoding="utf-8")
        out = subprocess.run([gk2, "run", str(f)], capture_output=True, text=True).stdout
        i = out.find("\n{")
        try:
            data = json.loads(out[i + 1:] if i >= 0 else out[out.index("{"):])
        except ValueError:
            data = {"ok": False, "error": out.strip()[-300:]}
        result = str(data.get("result", data.get("error", "")))
        if expect.startswith("log:"):
            log = subprocess.run([gk2, "log", "300"], capture_output=True, text=True).stdout
            ok = data.get("ok") is True and re.search(expect[4:], log) is not None
        else:
            ok = data.get("ok") is True and re.search(expect, result) is not None
        pic = ""
        if shot:
            pic = subprocess.run([gk2, "shot", f"{prefix}-{shot}"], capture_output=True, text=True).stdout.strip().splitlines()[-1:]
            pic = f" — print `{pic[0]}`" if pic else ""
        if not ok:
            fails += 1
        short = " ".join(result.split())[:160]
        print(f"- {'ok' if ok else 'FALHOU'} `{spec}` → {short}{pic}")
sys.exit(1 if fails else 0)
