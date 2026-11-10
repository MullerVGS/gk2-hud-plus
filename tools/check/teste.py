#!/usr/bin/env python3
"""Prova da barreira: reintroduz os bugs já vistos no estado exportado e exige que check.py os pegue.

Uso: teste.py <repo> <gk2hud.json> <gatilhos.json>  (rode depois de um `gk2 check GK2Hud` verde)
"""
import copy
import io
import json
import os
import sys
from contextlib import redirect_stdout

sys.path.insert(0, os.path.dirname(__file__))
import check  # noqa: E402


def run(repo, state, idx_path, tmp):
    json.dump(state, open(tmp, "w", encoding="utf-8"))
    buf = io.StringIO()
    with redirect_stdout(buf):
        code = check.main(repo, tmp, idx_path)
    return code, buf.getvalue()


def main(repo, state_path, idx_path):
    base = json.load(open(state_path, encoding="utf-8-sig"))
    tmp = os.path.join(os.path.dirname(state_path), "teste-estado.json")
    ev = lambda st, i: next(e for e in st["events"] if e["id"] == i)
    cases = []

    st = copy.deepcopy(base)          # 0.7.4: Deusa revelada por um ícone que o jogo nunca libera
    g = ev(st, "goddess"); g.update(mapIcons=["day_envy_goddess"], phrases=[], quests=[], known=False)
    cases.append(("ícone inexistente da Deusa", st, ["day_envy_goddess não é liberado", "goddess está escondido"]))

    st = copy.deepcopy(base)          # 0.7.4 (achado pelo check): freira só pelo ícone day_wrath
    n = ev(st, "nun"); n.update(mapIcons=["day_wrath"], phrases=[], quests=[], known=False)
    cases.append(("freira só por day_wrath", st, ["day_wrath não é liberado"]))

    st = copy.deepcopy(base)          # descrição de conversa que a revelação não alcança
    g = ev(st, "goddess"); g.update(phrases=[], quests=["63_swamp_goddess_speak"])
    cases.append(("descrição fora da conversa", st, ["só é ouvida depois da resposta 63_swamp_goddess_32_c"]))

    st = copy.deepcopy(base)          # 0.7.3: id cru no hover do mundo e na descrição de quest
    st["names"] = st["names"] + [{"where": "mundo tree_linden_clr1_m_v1", "token": "tree_linden_clr1_m_v1", "text": "tree_linden_clr1_m_v1"},
                                 {"where": "tooltip do diário quest:x", "token": "quest_closed_27_port_astrologer_meet_d", "text": "quest_closed_27_port_astrologer_meet_d"}]
    cases.append(("ids crus", st, ["tree_linden_clr1_m_v1", "quest_closed_27_port_astrologer_meet_d"]))

    ok = True
    code, out = run(repo, base, idx_path, tmp)
    print(("ok  " if code == 0 else "ERRO") + " estado atual passa")
    ok &= code == 0
    for name, st, expect in cases:
        code, out = run(repo, st, idx_path, tmp)
        caught = code == 1 and all(e in out for e in expect)
        ok &= caught
        print(("ok  " if caught else "ERRO") + f" pega: {name}")
        if not caught:
            print(out)
    os.remove(tmp)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:]))
