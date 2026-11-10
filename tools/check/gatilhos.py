#!/usr/bin/env python3
"""Índice de gatilhos do GK2: quem libera o quê, montado a partir do próprio jogo.

Entrada: o export do green (tools/check/exportar.cs → BepInEx/GK2Insight-dump/: balanço, textos e graphs.jsonl) e o
decomp/ (chamadas com literal no código C#, ex.: new UITutorialWindowData("tut_commerce_hdr")).
Saída: decomp/gatilhos.json com
  calls   chamadas normalizadas {fn, args, src}: expressões do balanço (quests, agendador, crafts...) e dos grafos, e os
          nós dos grafos que fazem a mesma coisa (AddGameRes, ShowTutorialWindow, AddPhraseToBlackList...).
  lines   fala → {graph, gate, wgo}: a conversa onde ela aparece e a resposta (blackListPhrases) que leva a ela.
  graphs  grafo → {events, phrases}; firedBy: grafo → fontes que o disparam (GSFireEvent/GSRun).
  quests, logics, lang: ids que existem.

Uso: gatilhos.py build <dump_dir> <decomp_dir> <saida.json> | gatilhos.py onde <termo> [indice.json]
"""
import json
import os
import re
import sys
from collections import defaultdict

CALL = re.compile(r"([A-Za-z_][A-Za-z0-9_]*)\(([^()]*)\)")
STR = re.compile(r'"([^"]*)"')


def calls_in(text):
    """Chamadas Nome("a", "b") de uma expressão do jogo, das mais internas para fora (os argumentos literais)."""
    out = []
    s = text
    for _ in range(12):
        found = False
        for m in CALL.finditer(s):
            out.append((m.group(1), STR.findall(m.group(2))))
            found = True
        if not found:
            break
        s = CALL.sub("0", s)
    return out


def walk_strings(obj, path=""):
    if isinstance(obj, dict):
        for k, v in obj.items():
            yield from walk_strings(v, path + "." + k if path else k)
    elif isinstance(obj, list):
        for v in obj:
            yield from walk_strings(v, path)
    elif isinstance(obj, str):
        yield path, obj


def port(node, name):
    v = node.get("_inputPortValues", {}).get(name)
    return v.get("$content") if isinstance(v, dict) else None


# Código C#: funções que liberam algo, chamadas com literal (o nome vira o da expressão equivalente).
CODE_FNS = {"UITutorialWindowData": "ShowTutorialWindow", "UnlockTutorial": "ShowTutorialWindow", "UnlockMapDayIcon": "UnlockMapDayIcon",
            "AddPhraseToBlackList": "AddPhraseToBlackList", "UnlockPhrase": "UnlockPhrase", "StartQuest": "StartQuest",
            "CompleteQuest": "CompleteQuest", "SetPPar": "SetPPar", "AddPPar": "AddPPar", "UnlockHudDaySprite": "UnlockHudDaySprite"}
CODE_CALL = re.compile(r"\b(" + "|".join(CODE_FNS) + r")\(\s*\"([^\"]+)\"")


def build(dump, decomp, out_path):
    calls = []

    def add(fn, args, src):
        calls.append({"fn": fn, "args": [a for a in args if a is not None], "src": src})

    # ---- balanço: toda expressão (expressionString) com a def de onde veio
    quests, logics = {}, []
    for fname in sorted(os.listdir(dump)):
        if not fname.endswith("Defs.json") and fname != "fightDefinitions.json":
            continue
        kind = fname.replace("Defs.json", "").replace(".json", "")
        try:
            data = json.load(open(os.path.join(dump, fname), encoding="utf-8"))
        except Exception as e:  # dump interrompido
            print(f"aviso: {fname}: {e}", file=sys.stderr)
            continue
        if not isinstance(data, list):
            continue
        for item in data:
            if not isinstance(item, dict):
                continue
            iid = item.get("id") or item.get("_name") or "?"
            if kind == "quest":
                quests[iid] = {"tree": bool(item.get("hasPosInBalance"))}
            if kind == "gameLogics":
                logics.append(iid)
            for path, s in walk_strings(item):
                if not path.endswith("expressionString") or "(" not in s:
                    continue
                field = path.split(".")[0]
                for fn, args in calls_in(s):
                    add(fn, args, f"{kind}:{iid}:{field}")

    # ---- código C# do jogo
    for root, _, files in os.walk(decomp):
        for f in files:
            if not f.endswith(".cs"):
                continue
            path = os.path.join(root, f)
            for no, text in enumerate(open(path, encoding="utf-8", errors="replace"), 1):
                for m in CODE_CALL.finditer(text):
                    add(CODE_FNS[m.group(1)], [m.group(2)], f"code:{os.path.relpath(path, decomp)}:{no}")

    # ---- grafos
    lines, graphs, fired_by = {}, {}, defaultdict(list)
    for raw in open(os.path.join(dump, "graphs.jsonl"), encoding="utf-8"):
        d = json.loads(raw)
        gname = os.path.basename(d["path"]).replace(".asset", "")
        g = {"events": [], "phrases": [], "path": d["path"]}
        glines = []
        for n in d["graph"].get("nodes", []):
            t = n.get("$type", "").split("[")[0].split(".")[-1]
            src = f"graph:{gname}#{n.get('$id', '?')}"
            if t == "CustomEvent":
                ev = (n.get("eventName") or {}).get("_value")
                if ev:
                    g["events"].append(ev)
            elif t == "Flow_MultiTalk":
                for el in port(n, "Elements") or []:
                    if isinstance(el, dict) and el.get("text"):
                        glines.append((el["text"], el.get("wgoId")))
            elif t in ("Flow_Talk", "Flow_TalkWisp"):
                if port(n, "text"):
                    glines.append((port(n, "text"), "[Player]" if n.get("isPlayer") else None))
            elif t == "Flow_MultiAnswer":
                for o in n.get("answerOptions", []):
                    if o.get("id"):
                        glines.append((o["id"], "[Player]"))
            elif t == "Flow_AddPhraseToBlackList":
                p = port(n, "Phrase")
                if p:
                    remove = n.get("operationType") == 1
                    add("RemovePhraseFromBlackList" if remove else "AddPhraseToBlackList", [p], src)
                    if not remove:
                        g["phrases"].append(p)
            elif t == "Flow_UnlockePhrase":
                add("UnlockPhrase", [port(n, "Phrase")], src)
            elif t == "Flow_AddGameRes":
                add("AddGameRes", [port(n, "GameresName")], src)
            elif t == "Flow_SmartRes":
                for o in n.get("options", []):
                    add("SmartRes", [o.get("id")], src)
            elif t == "Flow_ShowTutorialWindow":
                add("ShowTutorialWindow", [port(n, "PageId")], src)
            elif t == "Flow_FireEventOnGlobalScript":
                add("GSFireEvent", [port(n, "scriptName"), port(n, "eventName")], src)
            elif t == "Flow_AddInteractionEvent":
                add("AddInteractionEvent", [port(n, "eventId")], src)
            elif t == "Flow_QuestStatusEquals":
                add("QuestStatusEquals", [port(n, "QuestId")], src)
            for path, s in walk_strings(n.get("_inputPortValues", {})):
                if "(" in s and t != "Flow_MultiTalk":
                    for fn, args in calls_in(s):
                        add(fn, args, src)
        for text, wgo in glines:
            lines.setdefault(text, {"graph": gname, "gate": None, "wgo": wgo})
        g["phrases"] = sorted(set(g["phrases"]))
        g["events"] = sorted(set(g["events"]))
        graphs[gname] = g

    for c in calls:
        if c["fn"] == "BlackListPhrase":   # expressão do balanço: mesma coisa que o nó AddPhraseToBlackList
            c["fn"] = "AddPhraseToBlackList"
        if c["fn"] in ("GSFireEvent", "GSRun") and c["args"]:
            fired_by[c["args"][0]].append(c["src"])

    # Resposta/opção que leva a cada fala (o que fica em blackListPhrases quando o jogador a ouviu): a resposta P abre as
    # falas P_n ("63_swamp_goddess_32_c" → "..._32_c_3"); a opção de menu X_1 abre as irmãs X_n ("..._delivery_1" → "..._delivery_6").
    phrases = sorted({a for c in calls if c["fn"] == "AddPhraseToBlackList" for a in c["args"]}, key=len, reverse=True)
    stems = {}
    for p in phrases:
        stems.setdefault(p, p)
        if p.endswith("_1"):
            stems.setdefault(p[:-2], p)
    by_len = sorted(stems, key=len, reverse=True)
    for text, info in lines.items():
        info["gate"] = next((stems[k] for k in by_len if text == k or text.startswith(k + "_")), None)

    lang = sorted(json.load(open(os.path.join(dump, "lang_en.json"), encoding="utf-8")).keys())
    build_id = open(os.path.join(dump, "build.txt"), encoding="utf-8").read().split()
    index = {"build": build_id, "calls": calls, "lines": lines, "graphs": graphs, "firedBy": fired_by,
             "quests": quests, "logics": logics, "lang": lang}
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    json.dump(index, open(out_path, "w", encoding="utf-8"), ensure_ascii=False)
    fns = defaultdict(int)
    for c in calls:
        fns[c["fn"]] += 1
    top = ", ".join(f"{k} {v}" for k, v in sorted(fns.items(), key=lambda kv: -kv[1])[:8])
    print(f"gatilhos: {len(calls)} chamadas ({top}), {len(lines)} falas em {len(graphs)} grafos, "
          f"{len(quests)} quests, {len(lang)} textos → {out_path}")


def where(term, path):
    idx = json.load(open(path, encoding="utf-8"))
    hits = [c for c in idx["calls"] if term in c["args"] or term == c["fn"]]
    for c in hits[:60]:
        print(f"{c['fn']}({', '.join(c['args'])})  ← {c['src']}")
    if len(hits) > 60:
        print(f"... +{len(hits) - 60}")
    if term in idx["lines"]:
        print("fala:", json.dumps(idx["lines"][term], ensure_ascii=False))
    if term in idx["graphs"]:
        g = idx["graphs"][term]
        print(f"grafo: {len(g['events'])} eventos, respostas {g['phrases'][:10]}; disparado por {idx['firedBy'].get(term, [])[:10]}")
    if term in idx["quests"]:
        print("quest:", idx["quests"][term])
    if not hits and term not in idx["lines"] and term not in idx["graphs"] and term not in idx["quests"]:
        print("nada no índice" + (" (existe como texto)" if term in set(idx["lang"]) else ""))


if __name__ == "__main__":
    if len(sys.argv) >= 5 and sys.argv[1] == "build":
        build(sys.argv[2], sys.argv[3], sys.argv[4])
    elif len(sys.argv) >= 3 and sys.argv[1] == "onde":
        where(sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.path.dirname(__file__), "../../decomp/gatilhos.json"))
    else:
        print(__doc__)
        sys.exit(2)
