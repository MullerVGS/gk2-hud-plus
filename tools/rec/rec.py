#!/usr/bin/env python3
"""Clipes do menu do GK2 HUD+ sem defasagem.

Receita de cada clipe em tools/rec/clips.txt; o que foi gravado, com o hash de cada arquivo que o clipe mostra, em
GK2HudPlus/previews/clips.lock.json. Se o código de uma funcionalidade muda, o clipe dela fica desatualizado e o
`gk2 check GK2HudPlus` falha até ele ser regravado.

Uma gravação alimenta os dois lados: o preview do menu do jogo (GK2HudPlus/previews/<Id>.webm) e o clipe do README
(docs/showcase/<Id>.webp, gerado na mesma hora). O lock guarda o hash dos dois; o status acusa clipe do README que não
é o da última gravação, clipe com receita que o README não mostra e clipe que o README cita e não existe.

Uso: rec.py <gk2> <repo> status [--estrito] | todos | desatualizados | <Id> [<Id> ...]
"""
import datetime
import glob
import hashlib
import json
import os
import re
import subprocess
import sys

gk2, repo = sys.argv[1], sys.argv[2]
args = sys.argv[3:] or ["status"]
MANIFEST = os.path.join(repo, "tools/rec/clips.txt")
LOCK = os.path.join(repo, "GK2HudPlus/previews/clips.lock.json")
PREVIEWS = os.path.join(repo, "GK2HudPlus/previews")
SHOWCASE = os.path.join(repo, "docs/showcase")
README = os.path.join(repo, "README.md")
MODULES = ["GK2Hud/HudModule.cs", "GK2Map/MapModule.cs", "GK2Saves/SavesModule.cs", "GK2ZombieRefund/RefundModule.cs"]
KIT = "tools/snippet/DemoKit.cs"
SLOTS = "tools/snippet/Slots.cs"


def manifest():
    clips = []
    for no, raw in enumerate(open(MANIFEST, encoding="utf-8"), 1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = [p.strip() for p in line.split("|")]
        if len(parts) != 5:
            sys.exit(f"clips.txt:{no}: esperado 'Id | preparo | demo | região | fontes'")
        cid, prep, demo, region, sources = parts
        clips.append({
            "id": cid, "line": line,
            "prep": [p.strip() for p in prep.split(";") if p.strip()],
            "demo": demo, "region": region, "sources": sources.split(),
        })
    return clips


def files_of(clip):
    """Arquivos cujo conteúdo o clipe mostra: fontes, demo, preparo e o kit."""
    paths = set([KIT, SLOTS])
    for spec in [clip["demo"]] + clip["prep"]:
        paths.add(spec.split()[0])
    for pattern in clip["sources"]:
        hits = glob.glob(os.path.join(repo, pattern), recursive=True)
        if not hits:
            sys.exit(f"{clip['id']}: a fonte {pattern} não casa com nenhum arquivo")
        paths.update(os.path.relpath(h, repo) for h in hits)
    return sorted(p for p in paths if "/bin/" not in p and "/obj/" not in p)


def digest(clip):
    d = {p: hashlib.sha256(open(os.path.join(repo, p), "rb").read()).hexdigest()[:16] for p in files_of(clip)}
    d["clips.txt"] = hashlib.sha256(clip["line"].encode()).hexdigest()[:16]
    return d


def file_hash(path):
    return hashlib.sha256(open(path, "rb").read()).hexdigest()[:16] if os.path.exists(path) else None


def media(cid):
    """Os dois arquivos que saem de uma gravação: preview do jogo e clipe do README."""
    return {"webm": file_hash(os.path.join(PREVIEWS, cid + ".webm")), "webp": file_hash(os.path.join(SHOWCASE, cid + ".webp"))}


def load_lock():
    return json.load(open(LOCK, encoding="utf-8")) if os.path.exists(LOCK) else {}


def feature_ids():
    ids = []
    for m in MODULES:
        ids += re.findall(r'host\.Feature\("(\w+)"', open(os.path.join(repo, m), encoding="utf-8").read())
    return ids


def status(strict):
    clips, lock = manifest(), load_lock()
    known = {c["id"] for c in clips}
    problems = 0
    for c in clips:
        webm = os.path.join(PREVIEWS, c["id"] + ".webm")
        entry = lock.get(c["id"])
        if not os.path.exists(webm) or entry is None:
            print(f"- {c['id']}: sem clipe gravado")
            problems += 1
            continue
        now, then = digest(c), entry.get("files", {})
        changed = sorted(p for p in set(now) | set(then) if now.get(p) != then.get(p))
        now_media, readme_text = media(c["id"]), open(README, encoding="utf-8").read()
        if changed:
            print(f"- {c['id']}: DESATUALIZADO (mudou {', '.join(changed[:4])}{' …' if len(changed) > 4 else ''}) · gravado em {entry.get('date')}")
            problems += 1
        elif now_media != entry.get("media"):
            which = "do README (docs/showcase)" if now_media["webm"] == (entry.get("media") or {}).get("webm") else "do jogo (previews)"
            print(f"- {c['id']}: o clipe {which} não é o da última gravação: gk2 rec {c['id']}")
            problems += 1
        elif f"docs/showcase/{c['id']}.webp" not in readme_text:
            print(f"- {c['id']}: gravado, mas o README.md não mostra docs/showcase/{c['id']}.webp")
            problems += 1
        else:
            print(f"- {c['id']}: ok · gravado em {entry.get('date')} · jogo {entry.get('game', '?')}")
    for fid in feature_ids():
        if fid not in known:
            print(f"- {fid}: funcionalidade sem receita de clipe em tools/rec/clips.txt")
            problems += 1
    for cited in sorted(set(re.findall(r"docs/showcase/(\w+)\.webp", open(README, encoding="utf-8").read())) - known):
        print(f"- {cited}: o README.md cita docs/showcase/{cited}.webp, que não tem receita em tools/rec/clips.txt")
        problems += 1
    print(f"{len(clips)} clipes, {problems} problema(s)")
    if strict and problems:
        sys.exit(1)


def run(spec, label):
    path, *consts = spec.split()
    tmp = os.path.join(repo, ".shots", "rec-preparo.cs")
    r = subprocess.run(["python3", os.path.join(repo, "tools/update/smoke.py"), "--subst", os.path.join(repo, path), tmp] + consts)
    if r.returncode:
        sys.exit(f"{label}: constantes de {spec}")
    out = subprocess.run([gk2, "run", tmp], capture_output=True, text=True).stdout
    i = out.rfind('{\n  "ok"')
    data = json.loads(out[i:]) if i >= 0 else {"ok": False, "error": out[-400:]}
    if data.get("ok") is not True:
        sys.exit(f"{label}: preparo {spec} falhou: {str(data.get('error', ''))[:400]}")
    return data.get("result")


def game_version():
    out = subprocess.run([gk2, "run", os.path.join(repo, "tools/update/versao.cs")], capture_output=True, text=True).stdout
    m = re.search(r'"result": "([^"]*)"', out)
    return m.group(1) if m else "?"


def record(ids):
    clips = {c["id"]: c for c in manifest()}
    lock = load_lock()
    # Todo lote parte do save de teste limpo (o relógio do green anda; o diário e o calendário dependem do dia).
    subprocess.run([gk2, "save", "reset"], capture_output=True)
    run("docs/cenarios/carregar-slot.cs SLOT=Steam_1", "save de teste")
    version = game_version()
    for cid in ids:
        c = clips.get(cid) or sys.exit(f"{cid}: não está em tools/rec/clips.txt")
        print(f"== {cid}")
        for spec in c["prep"]:
            res = run(spec, cid)
            print(f"   preparo {spec.split()[0].split('/')[-1]}: {str(res)[:90]}")
        demo, *consts = c["demo"].split()
        r = subprocess.run([gk2, "rec-clip", os.path.join(repo, demo), c["region"], cid] + consts)
        if r.returncode:
            sys.exit(f"{cid}: gravação falhou")
        # O clipe do README sai da mesma gravação (docs/media/<Id>.mp4) que o preview do jogo.
        if subprocess.run(["python3", os.path.join(repo, "tools/rec/showcase.py"), repo, "webp", cid]).returncode:
            sys.exit(f"{cid}: clipe do README falhou")
        lock[cid] = {"files": digest(c), "media": media(cid), "date": datetime.date.today().isoformat(), "game": version}
        json.dump(lock, open(LOCK, "w", encoding="utf-8"), indent=2, ensure_ascii=False, sort_keys=True)
        open(LOCK, "a").write("\n")
    run("docs/cenarios/fechar-janelas.cs", "fim")


if args[0] == "status":
    status("--estrito" in args)
elif args[0] == "todos":
    record([c["id"] for c in manifest()])
elif args[0] == "desatualizados":
    lock = load_lock()
    stale = []
    for c in manifest():
        e = lock.get(c["id"])
        if e is None or e.get("files") != digest(c) or e.get("media") != media(c["id"]):
            stale.append(c["id"])
    print("desatualizados: " + (", ".join(stale) or "nenhum"))
    if stale:
        record(stale)
else:
    record(args)
