#!/usr/bin/env python3
"""Barreira de `gk2 check GK2Hud`: falha (código 1) se algo do mod pode mostrar id de dev ou errar a revelação do calendário.

1. Código: no GK2Hud, LLBase.L só com chave literal fora do Names.cs, e toda chave literal existe nos textos do jogo.
2. Nomes: nenhum texto com cara de id nos nomes e tooltips do mod (varredura do cenário gk2hud-estado.cs no green), salvo
   os listados em permitidos.txt com o motivo.
3. Calendário: toda chave de revelação (ícone, tutorial, quest, resposta, recurso, agendador) tem gatilho real no índice
   (decomp/gatilhos.json); descrição que é fala de diálogo só vale se a revelação passa pela conversa dessa fala.
4. Auditoria do save carregado: evento com evidência no save e escondido, ou mostrado sem evidência. Sem spoiler: só cita
   eventos que o save já revelou ou dos quais há evidência; os demais entram só na contagem.

Uso: check.py <repo> <gk2hud.json> <gatilhos.json>
"""
import json
import os
import re
import sys

SETTERS = re.compile(r"^(Set|Add|Smart|Inc)")


def load(path):
    return json.load(open(path, encoding="utf-8-sig"))


def main(repo, state_path, index_path):
    st, idx = load(state_path), load(index_path)
    fails, warns, notes = [], [], []
    lang = set(idx["lang"])
    calls = idx["calls"]

    def sources(fn_pred, arg):
        return [c["src"] for c in calls if fn_pred(c["fn"]) and arg in c["args"]]

    # ---------- 1. código
    src_dir = os.path.join(repo, "GK2Hud")
    version = None
    for root, _, files in os.walk(src_dir):
        if "/obj" in root or "/bin" in root:
            continue
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            rel = os.path.relpath(path, repo)
            for no, line in enumerate(open(path, encoding="utf-8"), 1):
                m = re.search(r'public const string Version = "([^"]+)"', line)
                if m and f == "Plugin.cs":
                    version = m.group(1)
                if f == "Names.cs" or line.strip().startswith("//"):
                    continue
                for m in re.finditer(r"LLBase\.L\(", line):
                    rest = line[m.end():]
                    lits = re.match(r'\s*"([^"]+)"\s*[,)]', rest)
                    cond = re.match(r'\s*[\w.!]+\s*\?\s*"([^"]+)"\s*:\s*"([^"]+)"\s*\)', rest)
                    keys = [lits.group(1)] if lits else list(cond.groups()) if cond else None
                    if keys is None:
                        fails.append(f"{rel}:{no}: LLBase.L com chave não literal (use Names.Text/Names.Wgo): {line.strip()[:110]}")
                        continue
                    for k in keys:
                        if k not in lang:
                            fails.append(f"{rel}:{no}: chave \"{k}\" não existe nos textos do jogo")
    if version and st.get("version") != version:
        fails.append(f"o green roda GK2Hud {st.get('version')}, o código está em {version}: rode `gk2 deploy GK2Hud` antes do check")

    # ---------- 2. nomes
    allowed = {}
    allow_path = os.path.join(os.path.dirname(__file__), "permitidos.txt")
    if os.path.exists(allow_path):
        for line in open(allow_path, encoding="utf-8"):
            line = line.strip()
            if line and not line.startswith("#"):
                tok, _, why = line.partition("#")
                allowed[tok.strip()] = why.strip()
    bad = [h for h in st["names"] if h["token"] not in allowed]
    for h in bad[:40]:
        fails.append(f"nome: {h['where']}: «{h['text'][:100]}» ({h['token']})")
    if len(bad) > 40:
        fails.append(f"nome: +{len(bad) - 40} textos com id")
    used_allow = {h["token"] for h in st["names"]} & set(allowed)
    for tok in sorted(t for t in set(allowed) - used_allow if not t.startswith("calendário:")):
        warns.append(f"permitidos.txt: {tok} não aparece mais (pode sair da lista)")

    # ---------- 3. calendário × índice
    fired_by = idx["firedBy"]

    def graph_reach(gname):
        """Fontes que levam a uma conversa: o próprio grafo e as quests/grafos que o disparam (um nível)."""
        out = {f"graph:{gname}"}
        for s in fired_by.get(gname, []):
            out.add(s.split("#")[0] if s.startswith("graph:") else ":".join(s.split(":")[:2]))
        return out

    def base_of(src):
        return src.split("#")[0] if src.startswith("graph:") else ":".join(src.split(":")[:2])

    touches = {}   # fonte (quest:x / graph:y) → respostas que ela libera ou marca como ouvidas
    for c in calls:
        if c["fn"] in ("UnlockPhrase", "AddPhraseToBlackList"):
            touches.setdefault(base_of(c["src"]), set()).update(c["args"])

    def key_sources(ev):
        """(chave, fontes) de cada chave de revelação; fontes vazias = chave morta."""
        out = []
        for icon in ev["mapIcons"] or []:
            out.append((f"ícone {icon}", sources(lambda fn: fn == "UnlockMapDayIcon", icon)))
        for t in ev["tutorials"] or []:
            out.append((f"tutorial {t}", sources(lambda fn: fn.startswith("ShowTutorialWindow"), t)))
        for q in ev["quests"] or []:
            out.append((f"quest {q}", [f"quest:{q}"] if q in idx["quests"] else []))
        for p in ev["phrases"] or []:
            out.append((f"resposta {p}", sources(lambda fn: fn == "AddPhraseToBlackList", p)))
        if ev["resFlag"]:
            out.append((f"recurso {ev['resFlag']}", sources(lambda fn: bool(SETTERS.match(fn)), ev["resFlag"])))
        return out

    for ev in st["events"]:
        eid = ev["id"]
        if ev["logicId"] and ev["logicId"] not in idx["logics"]:
            fails.append(f"calendário {eid}: agendador {ev['logicId']} não existe em gameLogicsDefs")
        ks = key_sources(ev)
        if not ks and not ev["logicId"]:
            fails.append(f"calendário {eid}: sem nenhuma chave de revelação")
        for key, srcs in ks:
            if not srcs:
                fails.append(f"calendário {eid}: {key} não é liberado por nada no jogo (chave morta: o evento pode nunca aparecer)")
        nd = ev["nativeDesc"]
        if nd:
            if nd not in lang:
                fails.append(f"calendário {eid}: descrição {nd} não existe nos textos")
            line = idx["lines"].get(nd)
            if line:
                reach = graph_reach(line["graph"])
                linked = line["gate"] in (ev["phrases"] or []) if line["gate"] else False
                for key, srcs in ks:
                    for s in srcs:
                        base = base_of(s)
                        # Fala atrás de uma resposta: só a resposta prova que foi ouvida (entrar na conversa não basta).
                        if line["gate"] and line["gate"] in touches.get(base, ()):
                            linked = True
                        elif not line["gate"] and base in reach:
                            linked = True
                if line["gate"] and not linked:
                    fails.append(f"calendário {eid}: a descrição {nd} só é ouvida depois da resposta {line['gate']} "
                                 f"(conversa {line['graph']}), mas a revelação não passa por ela (Phrases = [\"{line['gate']}\"])")
                elif not linked and f"calendário:{eid}" in allowed:
                    notes.append(f"{eid}: descrição de outra conversa aceita ({allowed[f'calendário:{eid}']})")
                elif not linked:
                    warns.append(f"calendário {eid}: a descrição {nd} vem da conversa {line['graph']}, que a revelação não alcança "
                                 f"(disparada por: {', '.join(sorted(reach - {'graph:' + line['graph']})) or 'nada no índice'})")

    # ---------- 4. auditoria do save (sem spoiler)
    save = st["save"]
    completed = set(save["questsCompleted"])
    phrases = set(save["phrases"] or [])
    hidden_ok = 0
    for ev in st["events"]:
        eid = ev["id"]
        evidence = []
        for icon in ev["mapIcons"] or []:
            if icon in (save["mapIcons"] or []):
                evidence.append(f"ícone {icon}")
        for t in ev["tutorials"] or []:
            if t in (save["tutorials"] or []):
                evidence.append(f"tutorial {t}")
        for q in ev["quests"] or []:
            if q in completed:
                evidence.append(f"quest {q}")
        for p in ev["phrases"] or []:
            if p in phrases:
                evidence.append(f"resposta {p}")
        if ev["resFlag"] and ev["resValue"] > 0:
            evidence.append(f"recurso {ev['resFlag']}")
        line = idx["lines"].get(ev["nativeDesc"] or "")
        if line:
            if line["gate"] and line["gate"] in phrases:
                evidence.append(f"ouviu {ev['nativeDesc']} (resposta {line['gate']})")
            elif not line["gate"]:
                for s in fired_by.get(line["graph"], []):
                    if s.startswith("quest:") and s.split(":")[1] in completed:
                        evidence.append(f"conversa {line['graph']} (quest {s.split(':')[1]})")
                        break
        if ev["known"] and not evidence:
            warns.append(f"auditoria: {eid} aparece, mas o save não tem evidência de que você o conhece")
        elif not ev["known"] and evidence:
            fails.append(f"auditoria: {eid} está escondido, mas o save mostra que você já o conhece ({'; '.join(evidence)})")
        elif not ev["known"]:
            hidden_ok += 1
        else:
            notes.append(f"{eid}: visível ({'; '.join(evidence[:2])})" + (f", desde o dia {ev['since']}" if ev["since"] else ""))
    notes.append(f"{hidden_ok} evento(s) ainda não revelado(s) neste save, sem evidência (ok; não listados)")

    print(f"gk2 check GK2Hud {st.get('version')} · save no dia {st.get('day')} · {len(st['names'])} textos suspeitos "
          f"({len(st['names']) - len(bad)} permitidos)")
    for n in notes:
        print("  ·", n)
    for w in warns:
        print("  ! ", w)
    for f in fails:
        print("  ✗ ", f)
    print("FALHOU" if fails else "OK", f"({len(fails)} falha(s), {len(warns)} aviso(s))")
    return 1 if fails else 0


if __name__ == "__main__":
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(*sys.argv[1:]))
