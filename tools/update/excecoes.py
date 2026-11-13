"""Lê o Player.log do green e lista erros que não são conhecidos do jogo, mais tudo que o ModGuard registrou.

Uso: excecoes.py <Player.log> <excecoes-do-jogo.txt>
Sai com 1 se houver erro novo ou parte desligada pelo guard. A ponte guarda só as últimas linhas do log; o Player.log
tem a sessão inteira (o primeiro erro costuma explicar os outros).
"""
import re
import sys

log_path, allowed_path = sys.argv[1:3]
allowed = [l.strip() for l in open(allowed_path, encoding="utf-8") if l.strip() and not l.startswith("#")]
lines = open(log_path, encoding="utf-8", errors="replace").read().splitlines()

START = re.compile(r"^(\[(Error|Fatal)\s*:|[\w.]*(Exception|Error)\b[^\n]*:)")
problems, guard, known = {}, [], 0
i = 0
while i < len(lines):
    line = lines[i]
    if "[guard]" in line:
        # Registros do ModGuard vão para a seção dele; os do cenário guard-desligar.cs são esperados.
        if "teste do ModGuard" not in line and "erro de teste" not in line:
            guard.append(line.strip())
        i += 1
        continue
    if not START.match(line) or "BoxCollider" in line:
        i += 1
        continue
    block = [line]
    i += 1
    while i < len(lines) and lines[i].strip() and (lines[i].startswith((" ", "\t")) or lines[i].startswith(("Stack trace", "UnityEngine.", "Rethrow"))):
        block.append(lines[i])
        i += 1
    text = "\n".join(block)
    if any(a in text for a in allowed):
        known += 1
        continue
    stack = [b.strip() for b in block[1:5]]
    head = block[0].strip()[:160] + (" @ " + stack[0][:100] if stack else "")
    entry = problems.setdefault(head, [0, stack])
    entry[0] += 1

print(f"- erros conhecidos do jogo: {known}")
print(f"- erros novos: {len(problems)}")
for head, (n, stack) in problems.items():
    print(f"  - {head} (×{n})")
    for l in stack:
        print(f"      {l}")
print(f"- registros do guard: {len(guard)}")
for g in guard[:20]:
    print(f"  - {g[:200]}")
off = [g for g in guard if "desligada" in g or "não iniciou" in g]
sys.exit(1 if problems or off else 0)
