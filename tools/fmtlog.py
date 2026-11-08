"""Formata respostas JSON da DevBridge: log legível (repetições agrupadas, últimas N linhas) + o restante como JSON.

GK2_LOG_LINES=N limita as linhas (padrão 40); GK2_LOG_ALL=1 mostra tudo sem agrupar.
"""
import json
import os
import sys

data = json.load(sys.stdin)
entries = data.get("logs") or data.get("entries") or []
show_all = os.environ.get("GK2_LOG_ALL") == "1"
limit = int(os.environ.get("GK2_LOG_LINES", "40"))
lines = []
for e in entries:
    msg = str(e.get("message"))
    head = msg.splitlines()[0] if msg else ""
    key = (e.get("level"), e.get("source"), head[:60])
    if not show_all and lines and lines[-1][0] == key:
        lines[-1][2] += 1
        continue
    lines.append([key, f"{e.get('time')} {str(e.get('level'))[:4]:4} [{e.get('source')}] {msg if show_all else head}", 1])
if not show_all and len(lines) > limit:
    print(f"... {len(lines) - limit} grupos anteriores omitidos (GK2_LOG_ALL=1 mostra tudo)")
    lines = lines[-limit:]
for _, text, count in lines:
    print(text + (f"  (×{count})" if count > 1 else ""))
rest = {k: v for k, v in data.items() if k not in ("logs", "entries")}
print(json.dumps(rest, indent=2, ensure_ascii=False))
