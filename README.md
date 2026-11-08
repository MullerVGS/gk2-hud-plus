# graveyard-keeper

Mods pessoais de qualidade de vida para **Graveyard Keeper 2** (Unity 6 Mono, BepInEx 5.4.23.5 que vem com o jogo).
Uso pessoal; não publicar no Workshop sem revisar.

## GK2 Insight (`GK2Insight/`)

Plugin só de leitura: não muda gameplay.

| Tecla | Ação |
| --- | --- |
| F7 | Mostra/esconde o painel (relógio, dia, energia/insanidade/stamina/felicidade com números, dinheiro, tech, sono, efeitos ativos) |
| Shift+F7 | Modo detalhado: todos os recursos internos do jogador (`PlayerData.res`) |
| Ctrl+F7 | Dump em JSON do `GameBalance` inteiro, textos do idioma atual e `PlayerData` para `<jogo>/BepInEx/GK2Insight-dump/` |

Configuração em `<jogo>/BepInEx/config/arthur.gk2.insight.cfg`.

## Build

```
./build.sh            # compila em container (dotnet sdk 8) contra as DLLs do jogo copiadas em refs/
./build.sh --install  # e instala como pacote local em %USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2\Mods\Code\GK2Insight
```

O jogo copia o pacote para `<jogo>/BepInEx/plugins/` ao abrir; mudanças valem na próxima abertura.
Contrato oficial de mods: `README_MODDING.txt` na pasta do jogo.

## Engenharia reversa

`decomp/` (gitignored) tem o C# descompilado de `Assembly-CSharp` e `LazyBearTechnology` (ilspycmd 8.2 em container,
`DOTNET_ROLL_FORWARD=Major`). Regerar quando o jogo atualizar.
