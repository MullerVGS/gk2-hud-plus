#!/usr/bin/env python3
"""Clipes do menu do GK2 HUD+ sem defasagem.

Receita de cada clipe em tools/rec/clips.txt; o que foi gravado, com o hash de cada arquivo que o clipe mostra, em
GK2HudPlus/previews/clips.lock.json. Se o código de uma funcionalidade muda, o clipe dela fica desatualizado e o
`gk2 check GK2HudPlus` falha até ele ser regravado.

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
MODULES = ["GK2Hud/HudModule.cs", "GK2Map/MapModule.cs", "GK2Saves/SavesModule.cs", "GK2ZombieRefund/RefundModule.cs"]
KIT = "tools/snippet/DemoKit.cs"


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
    paths = set([KIT])
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
        if changed:
            print(f"- {c['id']}: DESATUALIZADO (mudou {', '.join(changed[:4])}{' …' if len(changed) > 4 else ''}) · gravado em {entry.get('date')}")
            problems += 1
        else:
            print(f"- {c['id']}: ok · gravado em {entry.get('date')} · jogo {entry.get('game', '?')}")
    for fid in feature_ids():
        if fid not in known:
            print(f"- {fid}: funcionalidade sem receita de clipe em tools/rec/clips.txt")
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
    run("docs/cenarios/recarregar.cs", "save de teste")
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
        lock[cid] = {"files": digest(c), "date": datetime.date.today().isoformat(), "game": version}
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
        if e is None or not os.path.exists(os.path.join(PREVIEWS, c["id"] + ".webm")) or e.get("files") != digest(c):
            stale.append(c["id"])
    print("desatualizados: " + (", ".join(stale) or "nenhum"))
    if stale:
        record(stale)
else:
    record(args)
