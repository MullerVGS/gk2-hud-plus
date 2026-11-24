"""Relatório do que mudou no código do jogo e do que isso toca nos mods.

Uso: relatorio.py <decomp anterior> <decomp novo> <raiz do repo>
Imprime markdown. A última linha é "ALERTAS <n>": tipo usado pelos mods que sumiu, ou membro acessado por reflexão
(string) que não existe mais no tipo.
"""
import os
import re
import sys
from pathlib import Path

MODS = ["GK2HudPlus", "GK2Hud", "GK2Insight", "GK2Map", "GK2Saves", "GK2ZombieRefund", "GK2Crafting", "GK2HotLoader", "GK2DevBridge", "Shared"]
DECL = re.compile(r"\b(?:class|struct|interface|enum)\s+([A-Za-z_]\w*)")
TYPEOF_MEMBER = re.compile(r'typeof\(([A-Za-z_][\w.]*)\),\s*"([A-Za-z_]\w*)"')
REFLECT_NAME = re.compile(r'(?:AccessTools\.[A-Za-z<>]+|\.GetField|\.GetMethod|\.GetProperty|\.Field|\.Property|\.Method)\s*\([^;"]*"([A-Za-z_]\w*)"')


def cs_files(root):
    return {p.relative_to(root).as_posix(): p for p in Path(root).rglob("*.cs")}


def declared(files):
    """nome do tipo → arquivos que o declaram."""
    out = {}
    for rel, p in files.items():
        for name in DECL.findall(p.read_text(encoding="utf-8", errors="replace")):
            out.setdefault(name, set()).add(rel)
    return out


def mod_sources(repo):
    out = {}
    for mod in MODS:
        d = Path(repo) / mod
        if not d.is_dir():
            continue
        for p in d.rglob("*.cs"):
            if "/bin/" in p.as_posix() or "/obj/" in p.as_posix():
                continue
            out[p.relative_to(repo).as_posix()] = p.read_text(encoding="utf-8", errors="replace")
    return out


def changed_lines(a, b):
    import difflib
    la = a.read_text(encoding="utf-8", errors="replace").splitlines()
    lb = b.read_text(encoding="utf-8", errors="replace").splitlines()
    return sum(1 for l in difflib.unified_diff(la, lb, lineterm="", n=0) if l[:1] in "+-" and not l.startswith(("+++", "---")))


def main():
    old_root, new_root, repo = sys.argv[1:4]
    old, new = cs_files(old_root), cs_files(new_root)
    removed = sorted(set(old) - set(new))
    added = sorted(set(new) - set(old))
    changed = sorted(r for r in set(old) & set(new) if old[r].read_bytes() != new[r].read_bytes())
    src = mod_sources(repo)
    new_decl = declared(new)
    alerts = 0

    def users(type_name):
        rx = re.compile(r"\b" + re.escape(type_name) + r"\b")
        return sorted({path.split("/")[0] for path, text in src.items() if rx.search(text)})

    print(f"- arquivos do jogo: {len(changed)} alterados, {len(added)} novos, {len(removed)} removidos")

    print("\n### Tipos removidos que os mods usam")
    hits = []
    for rel in removed:
        t = Path(rel).stem
        u = users(t)
        if u and t not in new_decl:
            hits.append(f"- **{t}** ({rel}) — usado por {', '.join(u)}")
    alerts += len(hits)
    print("\n".join(hits) if hits else "- nenhum")

    print("\n### Tipos alterados que os mods usam")
    rows = []
    for rel in changed:
        t = Path(rel).stem
        u = users(t)
        if u:
            rows.append((changed_lines(old[rel], new[rel]), t, rel, u))
    for n, t, rel, u in sorted(rows, reverse=True):
        print(f"- {t}: {n} linhas ({', '.join(u)}) — `diff decomp.anterior/{rel} decomp/{rel}`")
    if not rows:
        print("- nenhum")

    print("\n### Reflexão por string")
    missing = []
    checked = 0
    for path, text in src.items():
        for t, m in TYPEOF_MEMBER.findall(text):
            short = t.split(".")[-1]
            files = new_decl.get(short)
            if not files:
                continue   # tipo de fora do jogo (BepInEx, Unity)
            checked += 1
            body = "".join(new[f].read_text(encoding="utf-8", errors="replace") for f in files)
            if not re.search(r"\b" + re.escape(m) + r"\b", body):
                missing.append(f"- **{short}.{m}** não existe mais ({path})")
    all_new = None
    for path, text in src.items():
        for m in REFLECT_NAME.findall(text):
            if all_new is None:
                all_new = "\n".join(p.read_text(encoding="utf-8", errors="replace") for p in new.values())
                all_old = "\n".join(p.read_text(encoding="utf-8", errors="replace") for p in old.values())
            rx = re.compile(r"\b" + re.escape(m) + r"\b")
            if rx.search(all_old) and not rx.search(all_new):
                missing.append(f"- **{m}** sumiu do jogo ({path})")
    missing = sorted(set(missing))
    alerts += len(missing)
    print(f"- {checked} acessos `typeof(T), \"membro\"` conferidos")
    print("\n".join(missing) if missing else "- nenhum membro sumiu")
    print(f"\nALERTAS {alerts}")


if __name__ == "__main__":
    main()
