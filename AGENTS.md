# AGENTS.md — `graveyard-keeper`

Mods pessoais de qualidade de vida para **Graveyard Keeper 2** (Unity 6000.3 Mono, BepInEx 5.4.23.5 que vem com o jogo).
`CLAUDE.md` é symlink para este arquivo. Regras de `pessoal/AGENTS.md` valem; memória do projeto em
`pessoal/.agents/memory/graveyard-keeper-2-mods.md`.

## O que vamos fazer

- **Dois tipos de mod, duas regras de interface.**
  - **Mods de jogador** (o HUD que vamos fazer, dicas, janelas): parecem do jogo. Interface, fontes, molduras,
    cores e comportamento indistinguíveis dos nativos (ver "Design nativo"); IMGUI nunca chega à versão promovida.
  - **GK2 Insight é ferramenta de debug e estudo**, fora da regra de visual nativo. Vale legibilidade, densidade e
    informação que o jogo **não** entrega; IMGUI e desenho técnico (GL) são permitidos. Ver "GK2 Insight".
- **Por ora, informação e interface, não gameplay.** Mais números, dicas, contexto e entendimento das mecânicas.
  Mudança de regra do jogo (velocidade, custos, craft com todos os baús) só com pedido explícito.
- **Prints sempre.** Toda mudança visível termina com print do green e um julgamento escrito (contra a referência
  nativa nos mods de jogador; legibilidade, sobreposição e utilidade no Insight). Sem print, a tarefa não acabou.

## GK2 Insight

| Tecla | Camada | Conteúdo |
| --- | --- | --- |
| F7 | Local e momento | o que o jogo não mostra sobre onde o jogador está: zona (qualidade e de onde vem), NPCs e zumbis carregados (o que fazem, onde), tempo/dia/sono, clima, crafts em andamento. **Sem buffs** (o inventário já mostra) |
| Shift+F7 | Técnico | recursos internos do jogador e overlays desenhados no mundo: colisores, hitboxes, pathing, dock points |
| Ctrl+F7 | Dump | `GameBalance`, textos e `PlayerData` em JSON |

Cenários úteis para ver as camadas no green: `insight-camadas.cs` (liga F7/Shift+F7 sem teclado), `hora.cs` (muda a
hora; 0,445 = NPCs saindo), `teleporte-npc-andando.cs`, `teleporte-zumbi.cs`, `perk.cs`. Hitboxes de combate ainda
não foram vistas em print (precisa de luta).

Critérios: não repetir o que a UI nativa já mostra; texto curto, colunas alinhadas, sem cobrir o centro da tela;
custo baixo por frame (atualizar listas a cada 0,25–1 s, nunca varrer a cena inteira por frame).

**Telemetria** (`GK2Insight/Telemetry/`, desde 0.3.0): 1 amostra/s para o VictoriaMetrics e eventos para o
VictoriaLogs da VPS, por `metrics-ingest.example.com` (vmauth, usuário `gk2-client`, só import). Token em
`[Telemetry] Token` do `BepInEx/config/arthur.gk2.insight.cfg` de cada instância (vazio = mede e não envia); rótulo
`instance=blue|green` vem do *productName*. Dashboard `vps · Graveyard Keeper 2` (uid `vps-gk2`,
pasta Jogos, versionado em `pessoal-tools`). Métricas `gk2_*`: frames (fps, p50/p99/max, travadas), GC, memória,
custo do próprio Insight por parte (`gk2_insight_seconds{part}`), recursos do jogador, reputação, zona. Eventos
(`app:gk2`): session, game_state, save_load, save_write, new_day, sleep_start/end, scene, zone, hitch (≥250 ms, com
GC/save/painel). Envio em thread própria; nada de rede na thread do jogo. `dormir.cs` gera sono + save no green.
Para comparar custo, ligue as camadas com `insight-camadas.cs` e leia `gk2_insight_seconds` por janela.

## Blue-green

| | Blue | Green |
| --- | --- | --- |
| Para quê | jogar | os agentes desenvolvem e testam |
| Jogo | `D:\SteamLibrary\steamapps\common\Graveyard Keeper 2` | cópia em `D:\GK2-green` (`/mnt/d/GK2-green`) |
| Dados (saves, `Player.log`, `Mods/`) | `%USERPROFILE%\AppData\LocalLow\Lazy Bear Games\Graveyard Keeper 2` | `...\Lazy Bear Games\GK2 Green Instance` |
| Configurações (registro) | `HKCU\Software\Lazy Bear Games\Graveyard Keeper 2` | `...\GK2 Green Instance` (sincronizado do blue, janela, sem som) |
| Plugins | GK2 HotLoader + mods promovidos (`Mods/Code/<Mod>/` e `BepInEx/hot/`) | `BepInEx/plugins/` direto + DevBridge (+ HotLoader para ensaio) |
| Janela | a de quem joga | 1920×1080 em modo janela, em `x=4000` (fora dos dois monitores) |

O isolamento vem de trocar o *productName* `Graveyard Keeper 2` por `GK2 Green Instance` (mesmo tamanho, 18 bytes)
em `GraveyardKeeper2_Data/globalgamemanagers` e `app.info` da cópia: o Unity deriva disso o `persistentDataPath` e a
chave do registro. Na cópia, o `steam_appid.txt` contém **480** (Spacewar, app de teste do Steamworks): o Steam
registra o green como outro app (`HKCU\Software\Valve\Steam\RunningAppID=480`), e o GK2 (4358690) continua livre
para abrir o blue pela biblioteca. Com 4358690 o Steam mostra o GK2 "Rodando" e bloqueia o blue (aconteceu em
08/11). Efeitos do 480 no green: Workshop e conquistas são do Spacewar, o DLC de pré-venda fica desligado (o jogo
remove a recompensa do save de teste) e os amigos na Steam veem "jogando Spacewar" enquanto o green roda.

**Regras invioláveis**
- Nunca fechar, matar ou enviar entrada ao processo do **blue**. No blue entram só o GK2 HotLoader e DLLs de mods
  promovidos; DevBridge, cenários e qualquer código avulso são exclusivos do green. Nunca trocar o `steam_appid.txt` do green
  para 4358690. O `green.ps1` só toca em processos cujo
  caminho começa por `D:\GK2-green`.
- No blue, só **leitura**: `Steam_1.dat` vira snapshot (`gk2 save snapshot`), nunca é escrito.
- Mudança de estado (itens, tempo, teleporte, salvar) **só no green**. O save do green é descartável
  (`gk2 save reset` volta ao snapshot).
- O blue só recebe mod por `gk2 promote <Mod>`, e só depois de os prints serem aprovados. Ensaie antes com
  `gk2 promote <Mod> --green` e confira o aviso no print.
- Mod de gameplay (que muda estado) é marcado "só ao reabrir": `gk2 promote <Mod> --sem-hot` e avisar que só vale ao reabrir o jogo.

## Ambiente

`tools/gk2` (em `/usr/local/bin/gk2`) é a única interface. `gk2 help` lista tudo.

```
gk2 green start|stop|restart|status|show|hide   instância green
gk2 run docs/cenarios/continuar.cs               menu → save de teste carregado (≈20 s; green sobe em ≈10 s)
gk2 deploy <Mod>                                 compila, instala no green e faz hot-reload (≈2 s)
gk2 shot <nome> [--hideui] [--crop x,y,w,h]      print pelo próprio Unity → .shots/HHMMSS-nome.png
gk2 ui <filtro> [prof]                           árvore da UI nativa: rects em px de tela, sprites, fontes, TextStyle
gk2 run <cenario.cs>                             C# dentro do jogo (≈1,5 s): abrir janela, ler valor, preparar cena
gk2 log [n] [grep] | state | windows | ping
gk2 save snapshot|reset                          save de teste a partir do blue
gk2 promote <Mod> [--green]                      entrega com o jogo aberto (blue, com aprovação) ou ensaio no green
gk2 bridge | green-sync | decomp                 manutenção (ver abaixo)
```

Peças:
- **GK2DevBridge** (`GK2DevBridge/`): plugin HTTP só do green em `127.0.0.1:47777` (a 7777 é de um container
  docker do WSL). O WSL está em `networkingMode=mirrored`, então `curl 127.0.0.1` alcança o Windows. Cada pedido
  roda na thread principal do Unity. Rotas: `/ping /state /log /shot /ui /windows /reload /unload /run /quit`.
  Ela própria não recarrega a quente: `gk2 bridge` reinstala e reinicia o green.
- **Build**: container `gk2-builder` persistente (dotnet sdk 8.0.425), `Directory.Build.props` comum (net472,
  referências do jogo em `refs/`, recopiadas quando o `Assembly-CSharp.dll` do jogo muda).
- **Cenários** (`docs/cenarios/*.cs`): `public static class Snippet { public static object Run() }`. Pode retornar
  `IEnumerator` (vários frames; o último valor que não é yield vira o resultado). Logue com `Debug.Log("[cenario] ...")`.
  Cenário útil e reutilizável vai para `docs/cenarios/` com um comentário de uma linha dizendo o que prepara.
- **GK2 HotLoader** (`GK2HotLoader/`): entrega sem reabrir. Observa só `<jogo>/BepInEx/hot/*.dll` a cada 2 s; quando
  o hash muda, troca o plugin em execução (mesmo `Shared/PluginReloader.cs` da DevBridge) e mostra a notificação
  nativa do jogo ("GK2 Insight updated to 0.1.1"; em pt se o jogo estiver em pt). Ao abrir o jogo, marca o que já está
  na pasta como aplicado, porque o pacote `Mods/Code` carregou a mesma versão. Ele próprio e a DevBridge não se
  recarregam a quente. Falha vira aviso "update failed (see log)" e o jogo segue.
- **Fallback sem ponte**: `gk2 winshot` (PrintWindow da janela green).

## Ciclo de trabalho

1. Ler o código do jogo em `decomp/` (grep). REA serve para metadados e membros (`inspect_managed_members`
   com `type_names`); ele não tem decompilador C# neste ambiente.
2. `gk2 green status`; se parado, `gk2 green start && gk2 wait && gk2 run docs/cenarios/continuar.cs`.
3. Antes de criar UI: print e `gk2 ui` da tela nativa equivalente, salvos em `docs/referencia/` quando forem novos.
4. Editar → `gk2 deploy <Mod>` → cenário que leva à tela → `gk2 shot` → **olhar o print** (Read) → julgar → repetir.
5. `gk2 log 40 <Mod>` sem exceções.
6. Mostrar o print final, de preferência ao lado da referência, e pedir aprovação.
7. Subir a `Version` (plugin e `modinfo.json`), `gk2 promote <Mod> --green` (print do aviso) e então `gk2 promote <Mod>`.

## Design nativo

Vale para mods de jogador (não para o GK2 Insight).


Fatos medidos (HUD, 1920×1080):
- Canvas raiz `UI` em `ScreenSpaceOverlay`, `scaleFactor` 2 (`LazyUI.ScaleFactor`). **1 pixel de arte = 2 px de tela.**
  Posições e tamanhos em múltiplos de 2 px de tela; nada de meio pixel de arte.
- Sprites com `ppu` 50, molduras em 9-slice (`Image.type = Sliced`, `border` em pixels de arte). Ex.: barra
  `hud-bar-energy` 20×12 com borda 4/0/4/0; ícone `energy` 11×12.
- Fontes TMP do jogo: `small_font`, `small_font_bold`, `tiny_font` (16 pt), sempre aplicadas por um `TextStyle`
  (`TextStyleComponent`, ex.: `small_font-fuel_value`, `small_font_bold-world_zone-location`) que define material,
  cor e contorno por idioma. Não setar fonte/cor na mão: aplicar o `TextStyle` (`TextStyle.ApplyStyle`).
- Ícones dentro de texto via `<sprite name="...">` (ex.: `happiness`) e `id.FontIcon()`.

Como construir:
- Reusar a UI do jogo: widgets e janelas do LazyUI (`LazyWidgetBase`, `LazyWindow<>`), o `UITooltip` nativo
  (`UITooltipTextWidgetData`, estilos `headerTextStyle`, `descriptionTextStyle`...), sprites do
  `EasySpritesCollection`. Clonar um prefab/widget nativo parecido é melhor que montar do zero.
- Textos do jogo via `LLBase.L(id)`. Texto próprio do mod segue o idioma atual (`LLBase.CurrentLang`), com pelo
  menos `en` e `pt-br`. O idioma de referência nos testes é o inglês.

Julgamento do print (escrever no relatório, item por item): fonte e estilo iguais aos nativos; grade de 2 px;
moldura/9-slice sem borrão nem esticado; paleta da referência; espaçamentos iguais aos de telas parecidas;
alinhamento com o HUD existente; não cobre informação nativa; legível com fundo claro e escuro (dia/noite, interiores).

## Convenções de plugin

- Um projeto por mod: `<Mod>/<Mod>.csproj` (só `AssemblyName`), `<Mod>/modinfo.json` (contrato do jogo,
  `README_MODDING.txt`), `[BepInPlugin("arthur.gk2.<mod>", ...)]`.
- **Recarregável**: `Harmony` com id = GUID, aplicado no `Awake`; no `OnDestroy`, `UnpatchSelf()`, destruir todo
  GameObject/UI criado e desinscrever eventos estáticos (`EnvironmentEngine.OnNewDayStarted` etc.). Nada de estado
  estático que sobreviva ao reload.
- Configuração por `Config.Bind` (vai para `BepInEx/config/<guid>.cfg`). Nunca escrever em `BepInEx/plugins`.
- Só leitura do estado do jogo, salvo pedido de gameplay.

## Armadilhas conhecidas

- O Mono devolve o assembly em cache quando o nome se repete: a DevBridge renomeia (Mono.Cecil) a cada carga.
- A DLL carregada pelo Chainloader fica travada pelo Windows: o deploy grava `<Mod>.dll.next`, aplicado no
  próximo `gk2 green start`.
- **Leituras que gravam no save**: `WorldZoneData.GetTotalQuality()`, `GetQualityString()` e `PlayerData.GetRes("wz_*")`
  escrevem `wz_<zona>` e podem desbloquear conquista. Somar `WgoData.Quality` dos `wgoDataList` à mão (ver
  `GK2Insight/LocalSection.cs`). Também não chamar `GetActionDamage` (avança o RNG) nem métodos `Roll*/Send*/Set*`
  de NPC, zumbi ou clima. Ler campos crus de `PlayerData.res` com `AccessTools`.
- Overlays: projetar com `CameraSystem.WorldCamera.WorldToScreenPoint` + `MainCamera.RenderTargetToScreen` e desenhar
  com GL no `OnGUI` (Repaint). O debug do dev (`MovementPathDebugManager`, gizmos) foi removido do build.
- `ScreenCapture` vem com alfa 0 em áreas pretas: o `gk2 shot` salva em RGB para o recorte não parecer cinza.
- O log do Unity é ruidoso (`BoxCollider does not support negative scale` aos milhares): o `gk2` agrupa repetições.
- `SteamApps.GetAppInstallDir` aponta para a pasta do blue mesmo no green; hoje só é usado em log e na importação de
  save da demo.
- Green não pode usar o app id do jogo (ver Blue-green); conferir com `RunningAppID` no registro do Steam.
- Depois de update do jogo: `gk2 green stop && gk2 green-sync && gk2 decomp`, depois recompilar tudo.

## Mecânicas já mapeadas

Dia = 7,5 min reais (`EnvironmentEngine.gameplayDayInMinutes`), semana de 6 dias, dia vira quando `timeOfDay` passa
de 1. Falta de sono após 2 dias acordado (`lack_of_sleep_debuff`: energia gasta vira +0,5 de insanidade por ponto).
Dormir acelera o tempo 50× e recupera 400 de energia por dia de jogo. Insanidade máxima 80; felicidade tem máximo
interno 9999 e a UI mostra 0/10. Dinheiro em bronze (100 b = 1 p, 100 p = 1 o). O craft já puxa itens dos baús da
mesma zona (`WgoData.GetCraftableMultiInventory`). Dados completos: `Ctrl+F7` do GK2 Insight gera o dump do
`GameBalance` e dos textos.

## Pronto quando

- [ ] Compila sem aviso; `gk2 deploy` e um segundo `gk2 deploy` funcionam (recarregável de verdade).
- [ ] Print do green na tela relevante, julgado contra a referência, com os itens acima.
- [ ] `gk2 log` sem exceção do mod.
- [ ] Nada mudou no blue nem no save do blue além do que foi promovido com aprovação.
- [ ] Commit no repo local (mensagem sem trailers) e memória atualizada se algo novo foi aprendido.
