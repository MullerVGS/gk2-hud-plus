# AGENTS.md — `graveyard-keeper`

Mods de qualidade de vida para **Graveyard Keeper 2** (Unity 6000.3 Mono, BepInEx 5.4.23.5 que vem com o jogo).
`CLAUDE.md` é symlink para este arquivo. Memória de agentes e regras do workspace ficam fora do repo.

**Repo público.** Tudo que é versionado (código, docs, mensagens de commit) usa voz genérica ("vamos fazer...", "foi
recusado o X", "o jogador", "o blue"), sem nome de pessoa. Infra privada fica fora: domínios e hosts aparecem como
`example.com` e `vps`, token e URL reais só no `.cfg` local, nada de caminho com usuário do Windows. Lógica de design,
decisões e o uso de infra e dados no desenvolvimento ficam, porque são o que interessa mostrar.

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
Desde 0.4.0: `gk2_thread_seconds{thread=main|render|gpu|present}` (p50 do segundo), `gk2_render_count{what=draws|setpass|
batches|shadow_casters|triangles|render_textures}` e `gk2_memory_bytes{kind=render_textures|gfx_buffers|unity_used|committed}`
vêm dos contadores do profiler que a build de release expõe (`ProfilerRecorder`; `FrameTimingManager` está desligado no
jogo). E `gk2_mod_seconds{mod,part}`/`gk2_mod_max_seconds`: custo do Update de cada funcionalidade do HUD+ (o `ModHost`
só mede, em `TakeCost`; o Insight lê por reflexão). Coleta de lixo que cai no Update de uma parte conta para ela: julgue
pela mediana, não pela média. No frame típico o gargalo é render thread/GPU (~3400 draw calls e ~3000 SetPass num
interior), não a main thread. Cenários: `perf-render.cs` (os mesmos contadores na hora), `perf-amostra.cs` (objetos da
Unity por tipo e raiz, para achar acúmulo), `perf-alocacao.cs` (KB/s alocados).

## GK2 HUD+

Pacote público (`GK2HudPlus/`, `mullervgs.gk2.hudplus`, nome `GK2 HUD+`): os mods de jogador num plugin só, com um
`.cfg` e uma lista de funcionalidades que ligam e desligam sem reabrir o jogo. Insight, DevBridge e HotLoader ficam de fora.

- **Módulos**: cada mod de jogador virou um módulo que não é plugin (`HudModule`, `MapModule`, `SavesModule`,
  `RefundModule`) e registra as funcionalidades num `ModHost` (`Shared/ModHost.cs`). O `Plugin.cs` de cada pasta é só o
  ponto de entrada do mod avulso (`[BepInIncompatibility(HudPlusId.Guid)]`: com o HUD+ instalado, o BepInEx ignora o
  avulso). O `GK2HudPlus.csproj` compila as quatro pastas inteiras menos esses `Plugin.cs`.
- **Funcionalidade** = `host.Feature(id, categoria, seção, chave, nome, descrição, create, update, dispose, gameplay,
  options)`: toggle em `[seção] chave`, nome e descrição em en/pt (`Loc`) para o menu, e a parte do ModGuard que roda
  quando ligada. **Opção** = `host.Option(pai, ...)`: booleano mostrado abaixo da funcionalidade; mudar uma opção (ou
  qualquer ajuste em `options`) reinicia a funcionalidade. Ids únicos no pacote inteiro.
- **Em tempo de execução**: mudar o toggle (menu, cenário ou o `.cfg` salvo no disco, relido a cada 1 s) liga ou
  desliga no frame seguinte; desligar solta os patches, religar refaz. Parte que desligou por erro volta quando o
  jogador desliga e liga de novo. As features conversam por `X.Current` com checagem de nulo, então cada uma liga
  sozinha.
- **Gameplay começa desligado** no pacote (Save/Load e Zombie Refund); no avulso, ligado.
- **Migração**: na primeira vez que o `.cfg` do HUD+ é criado, os valores de mesma seção/chave vêm dos `.cfg` dos mods
  antigos (`ImportLegacy`), e gameplay de um módulo com `.cfg` antigo fica ligado. `[Calendar] Enabled` e `[Hud]
  Enabled` antigos, que agrupavam funcionalidades, são traduzidos. Diário e histórico de saves continuam nas pastas
  antigas (`arthur.gk2.hud/`, `arthur.gk2.saves/history`) se existirem; senão, em `BepInEx/config/<guid do host>/`.
- **Categorias**: `hud` (números, giro do medalhão), `tooltips` (tooltips, mundo, árvore de quests), `calendar` (diário,
  medalhão, aba), `map`, `gameplay`. Os textos de `.cfg` estão em inglês (o pacote é público).
- No green roda o HUD+: `gk2 deploy GK2HudPlus` descarrega e tira os avulsos do green; `gk2 deploy GK2Hud` (avulso)
  recusa enquanto o HUD+ estiver lá. Cenários acessam os módulos por
  `Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance` → propriedade `Hud`/`Map`/`Saves`/`Refund`.
- **Menu** (`GK2HudPlus/Menu/`, desde 0.2.0): botão "GK2 HUD+" no menu de pausa, logo abaixo de Controls (clone do
  Settings), que fecha a pausa, abre a janela e volta à pausa ao fechar, como Settings e Controls. A janela
  (`HudPlusWindow`, uma `LazyWindow` criada por código) segue a receita da janela de Mods do próprio jogo
  (`UICodeModsWindow`): clona o `GenericWindowLayout` da janela de Settings (moldura, cabeçalho, X, botão OK), usa a
  linha nativa `UISwitchButton` do VSync ("◀ Off/On ▶", `ui_off`/`ui_on`; em pt o jogo diz Desativar/Ativar) e seções no
  estilo do título, como "Code mods". Lista rolável de 10 linhas inteiras (passo 16 + 6, scroll de uma linha), rótulo
  76 px de arte mais largo que o nativo, crescendo para a esquerda. Opções aparecem só com a dona ligada (a placa do dia
  tem as dela). Funcionalidade que falhou: rótulo e aviso em `#E5736A` (o vermelho da janela de Mods). Janela órfã de
  uma carga anterior é destruída quando o menu inicia. O menu é uma parte do ModGuard
  fora da lista (não se desliga). No hot-reload, `Teardown` fecha, tira da pilha e solta os eventos estáticos de
  `LazyWindowsStackController`. O sprite de fade das bordas que a janela de Mods pede (`45a023dc...`) não existe neste
  build (`InvalidKeyException` também na nativa): o menu não usa fade. Referências: `docs/referencia/settings.png`,
  `mods-nativo.png`.
- **Pré-visualização** (desde 0.3.0): o tooltip saiu; à direita da lista, um painel com o nome, um clipe do jogo com a
  funcionalidade em ação (moldura `comm-cell_dark_2`) e uma frase curta. Hover ou foco do controle trocam o assunto.
  Clipes em `GK2HudPlus/previews/<Id>.webm` (VP8, o Unity decodifica sozinho, inclusive no Proton), mostrados 1 px de
  textura por px de tela (`RenderTexture` sem filtro); opção sem clipe usa o da dona. O deploy copia `previews/` para
  `BepInEx/plugins/GK2HudPlus/previews` (a DLL carregada da memória no hot-reload não tem `Location`).
- **Gravação** (`gk2 rec-clip`, rotas `/rec/start|stop|cursor` da DevBridge 0.2.0): compila o cenário antes, captura no
  fim do frame na resolução da tela (região alinhada em pares, para o yuv420p não borrar o pixel art), e o ffmpeg gera o
  WebM do menu (VP8 crf 10), o MP4 e o GIF (metade, em pixels de arte) das páginas. O cursor do sistema não sai na
  captura: o gravador desenha o cursor do jogo (`CursorController.cursorConfigurations[0]`) onde o demo mandar.
- **Sem defasagem** (`gk2 rec status|todos|desatualizados|<Id>`, `tools/rec/rec.py`): cada clipe tem receita em
  `tools/rec/clips.txt` (preparo, demo, região, fontes) e o que foi gravado fica em `GK2HudPlus/previews/clips.lock.json`
  com o hash de cada fonte, do demo, do preparo e do `DemoKit`. Mudou uma fonte, o clipe fica desatualizado e o
  `gk2 check GK2HudPlus` (logo, o `promote`) falha até `gk2 rec desatualizados`. Funcionalidade nova sem receita também
  falha. Todo lote começa com `gk2 save reset` + `recarregar.cs` (o relógio do green anda; calendário e diário dependem
  do dia) e cada receita prepara a cena (`hud-exterior-dia.cs`: quintal ao meio-dia). Região: até 588×384 em tamanho
  real no painel, até 1176×768 em meio tamanho (pixel de arte = 1 px). Mudou o comportamento de propósito? Regrave e
  olhe a tira de quadros antes de commitar.
- **Save de gravação** (`Steam_2`, snapshot `GK2-green-saves/inicio`): jogo novo, ainda na masmorra da abertura, com
  "Certificate, Please" concluída antes do diário e "What the Fairy?" concluída ao vivo no dia 1 (o diário tem a
  anotação). Os clipes de árvore de quests, calendário, diário e medalhão saem dele, sem spoiler. Feito com
  `jogo-novo.cs` (botão New Game, slot novo), `intro-conduzir.cs` (escolhe a primeira resposta até o jogador ficar livre:
  `FreePlayerState`, controles liberados, sem cinemática) e `jogo-novo-quests.cs` (`StartQuest`/`CompleteQuest`, que
  disparam a cadeia natural do jogo, e salva); guardado com `gk2 save inicio`. O diário do HUD+ fica fora do save
  (`arthur.gk2.hud/journal-<slot>.json`): o snapshot e o `gk2 save reset` levam os dois juntos, senão o mod trata as
  anotações como "antes do diário". O "Continue" pegaria o save mais recente: `continuar.cs`, `recarregar.cs` e
  `carregar-slot.cs` carregam por nome (`tools/snippet/Slots.cs`; `continuar.cs` sempre `Steam_1`).
- **Demos** (`docs/cenarios/demo-*.cs`) usam `tools/snippet/DemoKit.cs` (compilado com todo cenário): `Demo.Mouse(p)`
  move o cursor desenhado e o mouse virtual de todas as funcionalidades (HUD, tooltips, mundo, medalhão, mapa) de uma
  vez, em px de tela com origem em cima; `Glide`, `Wait`, `HoverOn`, `Enter`/`Exit`/`Click` (eventos de ponteiro na UI
  nativa), `Center` (UI) e `World` (mundo → tela). Cada demo termina no estado em que começou (o clipe roda em loop) e
  aceita `FIM` para parar no meio e enquadrar com um print. Spoiler: só começo de jogo (o que mostra quests, calendário ou
  diário usa o save de gravação). As áreas de tela do calendário saem dos próprios títulos ("Journal"), porque ele
  encolhe com poucas faixas.
- Cenários: `hudplus-funcionalidades.cs` (`listar`; `ligar`/`desligar` com `IDS`; `ciclo` desliga e religa cada uma e
  confere os patches), `hudplus-menu.cs` (`abrir` pelo botão da pausa, `trocar` ID, `hover` ID, `rolar` POS,
  `fechar`), `settings.cs`, `mods-nativo.cs`, `guard-desligar.cs` (MOD = GUID do HUD+, PARTE = id da funcionalidade).

## GK2 Saves

Mod de jogador (`GK2Saves/`, `arthur.gk2.saves`): **Save** e **Load** no topo do menu de pausa, clonados do botão Settings.
O jogo só salva ao acordar (`EnergySystem.StopSleeping`). O Save chama o mesmo `SaveSystem.Save`, mas só com o jogo livre
(`SaveGuard`: sem diálogo, cena, luta, sono, troca de área, trabalho ou carga na cabeça). Quando está bloqueado, o botão
fica desabilitado e o tooltip nativo mostra o motivo. O Load abre a janela nativa `UISaveSlotsWindow` em modo "em jogo",
com o save atual, os 3 backups do jogo e o histórico do mod, rotulados pelo `sourceLabel` nativo. Carregar passa por
`GoToMenu` + o mesmo caminho do Continue, e o slot em jogo continua sendo o principal. O histórico fica em
`BepInEx/config/arthur.gk2.saves/history/<slot>/`, fora da pasta que o Steam Cloud sincroniza: os últimos N saves de cada
tipo (manual e ao dormir). Cenários: `gk2saves-clicar.cs`, `gk2saves-salvar-varios.cs`, `gk2saves-carregar.cs`,
`gk2saves-apagar.cs`, `gk2saves-bloqueio.cs`, `save-qualquer-lugar.cs`, `pausa.cs`, `menu-load.cs`.

## GK2 Zombie Refund

Mod de jogador (`GK2ZombieRefund/`, `arthur.gk2.zombierefund`): desfaz a compra de um perk de zumbi na árvore de perks
(janela do zumbi, aba Perks). Nada desenhado no nó: o tooltip nativo do perk comprado ganha uma linha pequena
("Right-click: refund (50)") e o clique direito abre a janela de diálogo nativa (`UIDialogWindow`, Yes/No) com o título do
tooltip. Devolve os pontos e a caveira pela lógica do `ZombieWgoData.RespecSpentTalents` (respec que o jogo tem e não usa),
um nó por vez e só sem perk comprado dependente; perks de início (`availableAtStart`) ficam de fora. Muda estado do jogo.
Um X desenhado no nó (0.1.0) foi recusado por não ser imersivo. Cenários: `zumbi-perks.cs` (abre a árvore),
`gk2zombierefund-clicar.cs`.

## GK2 Map

Mod de jogador (`GK2Map/`, `arthur.gk2.map`), só informação, nas duas janelas de mapa (aba Map do TAB e mapa dos marcos).
- **Zoom**: scroll do mouse, 4 níveis de razão inteira (1, 2, 3 e 4 px de tela por pixel de arte; o nativo é 2), ancorado
  no cursor. O conteúdo do `ScrollRect` cresce (`sizeDelta`) e o `Map` ganha `localScale`, então arrasto e pan nativos
  continuam limitando certo. Rótulos ficam do tamanho nativo; marcadores também, menos no nível afastado.
- **Setinhas amarelas**: são pintura na arte (`map-bg`), não UI. O mod as acha pela cor na textura (uma vez por sessão)
  e casa cada uma com a porta mais próxima (`WGODef.teleportDestinationWgoIds` + áreas internas do
  `TeleportPointGraph`). Só entra interior cuja zona está em `KnowledgeSystem.visitedWorldZones`. Hover = tooltip nativo
  com o nome da zona; clique abre a vista do interior (o scroll no zoom máximo desce ao mundo aberto, ver 0.2.0).
- **Vista do local**: o mundo inteiro (interiores incluídos) vive na cena `RuinedTemple` e é ligado por pedaços conforme
  a câmera do mundo. A vista desliga o `CinemachineBrain`, leva a câmera do mundo para o interior e chama o
  `ChunkManager` no modo de carga gradual; some com o pergaminho e o fundo `Back` da moldura e refaz o `BgShadow` em
  faixas (preto por baixo, ver 0.1.1), então o mundo aparece pelo vão da janela. Luz e som ambiente do interior vêm do preset da porta
  (`TeleportTo("destino", "preset")`), trocando só `EnvironmentEngine.timesOfDay` (o nome que vai para o save não muda).
  Abre no enquadramento em que a zona cabe (escala normal ou metade); scroll alterna, arrasto/WASD andam dentro das
  zonas visitadas do mesmo interior; scroll para trás, botão direito, trocar de aba ou fechar a janela restauram tudo.
  Custo medido: 90–220 ms na primeira abertura de um local na sessão, ~10 ms depois.
- **Inspeção** (só leitura): hover mostra o nome do objeto; clique fixa o tooltip nativo com qualidade (widget nativo),
  craft em andamento e conteúdo do inventário. Nomes vêm do `GK2Hud/Names.cs` (linkado no csproj).
- **Spoiler (0.1.1)**: a arte do mapa tem trechos sem nuvem e rótulos soltos fora de qualquer região (o nativo conta
  com o jogador não rolar até lá; o zoom afastado mostrava tudo de uma vez). A vista do mapa fica presa à área explorada
  (união das áreas de nuvens das regiões em `knownMapZones` + posição do jogador), em todos os níveis; rótulo solto e
  marco fora dela somem. Qualquer coisa nova que o mapa mostre passa por esse recorte.
- **Câmera sem Cinemachine (0.1.1)**: bolhas de objetos, HUD e `ChunkManager` só se atualizam no
  `Cinemachine.CinemachineCore.CameraUpdatedEvent`; a vista dispara o evento a cada frame (sem isso as bolhas ficam
  presas na tela ao arrastar). Fora da moldura: faixas do escurecimento nativo + um canvas preto na ordem 45 (acima das
  bolhas, 40; abaixo do HUD, 50). Personagens usam caixa fixa a partir dos pés (os limites do esqueleto são enormes).
- **Navegação (0.2.0)**: no zoom máximo, mais um scroll desce ao **mundo aberto** naquele ponto do mapa (base,
  vinhedo, floresta, cidade...), só dentro das regiões em `knownMapZones`; ali a vista inteira (não só o foco) fica
  dentro de uma região conhecida. Scroll sobre porta, escada ou alçapão (`WGODef.teleportDestinationWgoIds`) atravessa;
  clique também. O caminho fica numa pilha (mapa → base → casa → porão) e o scroll para trás desfaz um passo por vez;
  botão direito volta direto ao mapa. `Atlas` (em `Places.cs`) monta lugares e portas a cada redesenho do mapa. Clicar
  na setinha do mapa continua abrindo o interior direto.
- **Contorno (0.2.0)**: o jogo não tem destaque de contorno. `Outline.cs` monta a borda numa textura à parte por
  `CommandBuffer` (`AfterForwardOpaque`): `UI/Default` com estêncil marca a área do objeto e desenha a silhueta
  deslocada em 8 direções só fora dela; a textura vai para a tela pelo `GUI/Text Shader` (cor única × alfa). Não
  funcionam: teste de profundidade (o shader do jogo não grava profundidade geométrica), redesenhar o objeto com o
  material dele (sai branco) e qualquer shader pronto direto na tela (cor de vértice das malhas escurece). Sprites
  auxiliares de personagem (`-hed`, `-bdy`, `Shadowcaster`, `_SpriteInvisible`) ficam fora.
- **Título (0.2.0)**: rótulo clonado da placa de zona do HUD (`WorldZoneWidget.worldZoneLabel`) com o texto nativo
  ("Morgue 6/7") montado por `ZoneInfo.cs` só com leitura: o caminho nativo (`GetQualityString`, `WZQual`) grava no save.
- **Clima (0.2.0)**: ao trocar o preset de luz a vista também pausa/solta o clima como o teleporte faz
  (`WeatherSystem.SetPauseState(TimeOfDay, indoorPreset)`), senão chove dentro dos interiores; restaura ao sair.
- **Porão (0.2.1)**: porta cujo destino cai em corredor (fora de qualquer `WorldZone`, ex.: escada do cruzamento) vale
  se alguma zona da mesma área interna já foi visitada. Dentro de um interior a vista anda pelas zonas visitadas com
  folga de 5 unidades (corredores) e, se todas as zonas da área já foram visitadas, pela área interna inteira.
- **Limite do cenário (0.2.1)**: as regiões do mapa e as áreas internas são maiores que o terreno desenhado; fora dele a
  câmera mostra vazio. No mundo aberto a vista lê quatro linhas de 1 px nas próprias bordas (fim do frame, a cada
  0,1 s) e empurra o foco para dentro onde a borda é uma faixa lisa (129 px com variação ≤1 por canal). Não servem: cor
  do vazio (preto de dia, azulado à noite pelo pós-processamento; com faixa de 49 px/tolerância 2 o terreno escuro da
  noite dava falso positivo), ladrilhos `LazyTerrainMeshData` (existem em camadas, também onde nada aparece) e a malha
  de navegação (prende a vista em platôs e custa caro). Em interior basta o centro estar sobre um ladrilho (`TerrainMap`).
- **O que é inspecionável (0.2.1)**: contorno em árvore e arbusto foi considerado exagero. Só entram portas, placas de
  construção, personagens e objetos cujo `WGODef.interactionType` não é `None` (enfeite) nem `Work` (natureza colhível).
- **Custo de construção (0.2.1)**: placas de reparo da cidade (`repair_sign_*`, `TownBuildingPlace`) mostram no hover o
  que o jogo oferece erguer ali (`TownBuildingWgoComponent.GetAvailableBuildings`, que respeita o que está liberado) e o
  custo de cada opção no widget nativo `UITooltipNeedsItemWidgetData(needItems, WorkerMultiInventory)`.
- Cenários: `gk2map-zoom.cs`, `gk2map-local.cs` (hover/entrar/scroll/sair/lista), `gk2map-inspecionar.cs`,
  `gk2map-estado.cs` (lugar, nível, pilha, portas; scroll sobre um alvo), `gk2map-ir.cs` (vista externa num objeto).
- O overlay de FPS de terceiros nos prints do blue marcava 11–14; a telemetria do blue (`gk2_fps`) no mesmo período
  ficou em 84–238. Consultar: `ssh vps 'docker exec victoriametrics wget -qO- "http://127.0.0.1:8428/api/v1/query_range?..."'`.
- A placa "Портовый район" no canto do green é do jogo (aparece sem o mod ao carregar o save de teste na mina).

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
gk2 rec status|todos|desatualizados|<Id>         clipes do menu (receitas em tools/rec/clips.txt); status acusa clipe defasado
gk2 rec-clip <demo.cs> <x,y,w,h> <Id> [K=V]      grava um clipe avulso: previews/<Id>.webm + docs/media/<Id>.gif/.mp4
gk2 log [n] [grep] | state | windows | ping
gk2 save snapshot|reset|inicio                   save de teste a partir do blue (reset limpa backups e histórico do GK2Saves e
                                                 restaura o Steam_2 de gravação com o diário; inicio guarda o Steam_2)
gk2 promote <Mod> [--green]                      entrega com o jogo aberto (blue, com aprovação) ou ensaio no green
gk2 gatilhos [onde <termo>]                      índice do jogo: quem libera ícone/tutorial/resposta/recurso, onde está cada fala
gk2 check GK2HudPlus                             barreira do promote (ids na tela, calendário × índice, auditoria do save)
gk2 update [--forcar] | gk2 update conferir     depois de update do jogo (ver "Update do jogo")
gk2 bridge | green-sync | decomp                 manutenção (ver abaixo)
```

Peças:
- **GK2DevBridge** (`GK2DevBridge/`): plugin HTTP só do green em `127.0.0.1:27777` (porta no `.cfg` do green; fora da faixa efêmera do WSL, `/proc/sys/net/ipv4/ip_local_port_range`, que o modo mirrored reserva no Windows; a 7777 é de um container
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
  na pasta como aplicado, porque o pacote `Mods/Code` carregou a mesma versão. Desde a 0.2.0 compara
  `Application.version` com `[Game] VerifiedVersion` do `arthur.gk2.hotloader.cfg` e, se diferem, avisa ao entrar no
  jogo "Game updated to X: mods not yet checked on this version" (gravado por `gk2 update conferir`). Ele próprio e a DevBridge não se
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
7. Subir a `Version` (plugin e `modinfo.json`), `gk2 deploy`, `gk2 check GK2HudPlus` verde, `gk2 promote <Mod> --green`
   (print do aviso) e então `gk2 promote <Mod>` (que roda o check de novo e não promove se falhar).

## Barreira: índice de gatilhos e `gk2 check`

Regra: nada que o mod mostra ou esconde é deduzido pelo nome de um id. Condição de revelação, nome e descrição saem do
que o jogo faz, conferido no índice.

- **Índice** (`gk2 gatilhos` → `decomp/gatilhos.json`, regenerado sozinho quando o jogo muda): `tools/check/exportar.cs`
  exporta no green o balanço e os textos (Dumper do Insight) e os 271 grafos de script/diálogo (Addressables
  `VisualScripts/`, JSON do FlowCanvas); `tools/check/gatilhos.py` junta com as chamadas com literal do `decomp/`. Tem
  toda chamada que libera algo (`UnlockMapDayIcon`, `ShowTutorialWindow`, `AddPhraseToBlackList`/`BlackListPhrase`,
  `UnlockPhrase`, `SetPPar`, `AddGameRes`, `StartQuest`, `GSFireEvent`...) com a fonte, e cada fala com a conversa e a
  resposta que leva a ela. `gk2 gatilhos onde <termo>` responde "o que libera isso / onde está essa fala".
- **Evidência mais precisa do save**: `KnowledgeSystem.blackListPhrases` = respostas/opções de diálogo que o jogador já
  ouviu. A resposta P abre as falas `P_n`; a opção de menu `X_1` abre as irmãs `X_n`.
- **`gk2 check GK2HudPlus`** (código do `GK2Hud/`, estado do HUD+ no green: `tools/check/gk2hud-estado.cs` + `check.py`) falha se: `LLBase.L` com chave variável
  fora do `Names.cs` (para reproduzir texto nativo, `Names.NativeAsIs`) ou chave literal inexistente; algum nome ou
  tooltip do mod, para qualquer id do balanço, tem texto com cara de id; alguma chave de revelação do calendário não é
  liberada por nada no jogo (chave morta); a descrição é fala atrás de uma resposta e a revelação não passa por ela;
  no save carregado, um evento está escondido com evidência de que o jogador o conhece. Exceções só em
  `tools/check/permitidos.txt`, com o motivo. `tools/check/teste.py` reintroduz os bugs já vistos e exige que a barreira
  os pegue (roda dentro do check).
- **Auditoria do save do blue**: `gk2 save snapshot && gk2 save reset && gk2 run docs/cenarios/recarregar.cs && gk2 check GK2HudPlus`.
- **Spoiler**: o índice mostra o jogo inteiro aos agentes; relatório, print e mensagem só citam o que o save
  do blue já revelou. O check segue isso: evento sem evidência entra só na contagem.

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
  `README_MODDING.txt`), `[BepInPlugin("<autor>.gk2.<mod>", ...)]`. Mod de jogador novo nasce como módulo (ver GK2 HUD+)
  com um `Plugin.cs` fino de avulso, e entra no `GK2HudPlus.csproj`.
- **Recarregável**: no `OnDestroy`, `Host.Dispose()` (ou `Guard.Dispose()`) solta os patches; cada parte destrói todo
  GameObject/UI criado e desinscreve eventos estáticos (`EnvironmentEngine.OnNewDayStarted` etc.). Nada de estado
  estático que sobreviva ao reload.
- Configuração por `Config.Bind` (vai para `BepInEx/config/<guid>.cfg`), descrição em inglês. Nunca escrever em
  `BepInEx/plugins`.
- **ModGuard** (`Shared/ModGuard.cs`, linkado no csproj): toda feature é uma parte com o Harmony `h` dela
  (`Guard.Define` + `Start`/`Stop`, ou `host.Feature` nos módulos; `Guard.Feature` define e inicia, usado pelo Insight),
  e o `Update` do plugin chama só `Host.Update()`/`Guard.Update()`. Parte que não inicia, ou erra 5 vezes em 60 s
  (Update, patch, ou `Guard.Report` de MonoBehaviour/callback próprio), desliga sozinha: sai do Update, solta os
  patches, é descartada e mostra o aviso nativo "<Mod> · <parte>: turned off after an error (see log)". Exceção que
  nasce num patch da parte é engolida por um finalizer (a do jogo passa intacta). O construtor do módulo só registra
  partes; chamada ao jogo fora delas derruba o plugin inteiro se a API mudar. Teste: `docs/cenarios/guard-desligar.cs`
  (estado, finalizer, desligar).
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
- `LazySingleton<T>.Instance` cria um componente vazio quando o do jogo ainda não existe: tocar em
  `LazySingleton<UINotificator>.Instance` antes do `LazyUI.Init` quebra a UI inteira do jogo (UILoadingOverlay some,
  NRE por frame). Aviso nativo só depois de save carregado e `UILoadingOverlay` fechado (`ReadyForNotice` no HotLoader
  e no ModGuard).
- A ponte guarda só as últimas 4000 linhas de log; para a sessão inteira, leia o `Player.log` do green.

## Update do jogo

`gk2 update` (≈10 min) faz o roteiro inteiro e grava `.update/<data>.md`:
1. `green-sync`: copia só o que é do jogo (tudo fora de `BepInEx`, arquivos da raiz, `BepInEx/core` e `patchers`); o
   resto do `BepInEx` do green nunca é tocado.
2. `decomp` com o anterior guardado em `decomp.anterior/` e `tools/update/relatorio.py`: tipos usados pelos mods que
   mudaram ou sumiram e membros acessados por reflexão (string) que não existem mais.
3. Build de todos os projetos contra o jogo novo, instalados no green; `gk2 gatilhos` e `gk2 check GK2HudPlus`. Os avulsos só compilam; no green quem roda é o HUD+.
4. Os DLLs de `Mods/Code` do blue (o que vai rodar de verdade) no green: bateria `tools/update/smoke.txt` (cenários com
   constantes trocadas, regex esperada, prints `.shots/*upd-<data>*`) e `tools/update/excecoes.py` no `Player.log`
   (erros do próprio jogo, conferidos sem mods, ficam em `tools/update/excecoes-do-jogo.txt`).
5. Green volta aos builds do repo e ao save de teste.

Veredito ok + prints olhados → `gk2 update conferir` grava a versão em `[Game] VerifiedVersion` no
`arthur.gk2.hotloader.cfg` do blue (único arquivo do blue que ele escreve) e o aviso de versão some. Se algo falhar,
o ModGuard já protege o jogador enquanto isso: a parte quebrada desliga sozinha com aviso.

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
- [ ] GK2 HUD+: `gk2 check GK2HudPlus` verde (sem ele o `gk2 promote` para o blue recusa).
- [ ] Nada mudou no blue nem no save do blue além do que foi promovido com aprovação.
- [ ] Commit no repo local (mensagem sem trailers) e memória atualizada se algo novo foi aprendido.
