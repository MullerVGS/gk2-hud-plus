# GK2Hud — mapa técnico (08/11/2026)

Consolidado da pesquisa no `decomp/` e das provas no green que antecederam o mod `GK2Hud` (tooltips ricos estilo
BG3 + informações na HUD). Caminhos relativos a `decomp/Assembly-CSharp/` salvo indicação. "Inferido" = não
confirmado lendo o corpo do método nem no jogo.

## Decisões de design

- Mod de jogador: visual indistinguível do nativo (regras de "Design nativo" do `AGENTS.md`).
- v1 só UI (inventário, baú, craft, vendedor, HUD). Hover em objetos do mundo fica para v2 (viável, ver fim).
- Tooltip com informação extra no hover; **tecla livre** fixa o popup (padrão: botão do meio do mouse, configurável;
  T é a Tech Tree do jogo). No fixado, passar o mouse num termo destacado abre o popup aninhado ao lado; a tecla
  fixa esse também, recursivamente (igual BG3).
- Info extra de item: **usado em** (receitas, como links), **quantos tenho** (inventário + baús da zona), **detalhes
  de craft** (tempo, energia, estação → o que ela fabrica). Sem preço. Extra pequeno: durabilidade/usos restantes.
- HUD: hora + dia da semana; números de energia e sanidade junto das barras.

## Anatomia do `UITooltip` (medida no green)

- `UI/UIRoot/UITooltip`, singleton (`private static UITooltip instance`, setado em `Init()` — clone nunca chama
  `Init`). Canvas próprio ScreenSpaceOverlay, `overrideSorting`, `sortingOrder` 700. **Sem `GraphicRaycaster`**;
  `CanvasGroup` alpha 1, `interactable` false, `blocksRaycasts` true; TMP com `raycastTarget` false.
- Moldura `hint-frame` 156×114, 9-slice 20/20/20/20, ppu 50; separador `hint-text_separator` 96×6; ponta
  `hint-frame_tail` 10×10 em 4 cantos (um ativo).
- `VerticalLayoutGroup` padding 14 (arte), spacing 2, MiddleCenter; `ContentSizeFitter` Preferred;
  `OverrideLayoutElement` `maxWidth` 200 / `minWidth` 60 (arte) → 400 px de tela máx., 344 px de texto.
- `offsetAdd` (3,4), `alternativeDownOffsetAdd` (0,−6); fade DOTween de duração 0; `Update` segue `target`
  (funciona nos clones). Cada `Show` toca `gui_hover_light`. Esconde em `LazyWindowsStackController.OnWindowOpened/Closed`.
- `widgetContainer` = o próprio GO; widgets em pool reaproveitado por tipo. Prefabs registrados no
  `LazyWidgetPrefabContainer` incluem `UITooltipTextWidgetData`, `UITooltipSeparatorWidgetData`,
  `UITooltipNeedsItemWidgetData`, `UIQualityTooltipWidgetData`, `UICraftStatusInfoWidgetData`. **Não** inclui
  `LinkedEntityWidgetData` (usar lança exceção).

| Campo (privado) | TextStyle | Cor |
| --- | --- | --- |
| headerTextStyle | `small_font-hint_header` | #5E2100 |
| headerBoldTextStyle | `small_font_bold-hint_header_1` | #4E3012 |
| descriptionTextStyle | `small_font-hint_regular_text` | #784F26 |
| smallDescriptionTextStyle | `tiny_font-hint_additional_text` | #946E44 |
| selectedSmallDescriptionTextStyle | `tiny_font-hint_additional_text_selected` | #784F26 |
| perkDescriptionTextStyle | `tiny_font-hint_regular_text_3` | #7B3D00 |
| greenEffectTextStyle / redEffectTextStyle | `small_font-hint_receive` / `small_font-hint_give_away` | #357200 / #E73B3B |
| headerBoldTextStyleNotEnough / Gold | `small_font_bold-hint-not_enough` / `-gold` | #BC3330 / #FFBD00 |

`headerBoldTextStyle.ColorizeTags("x [Termo]")` e `ApplyStyleToString(nome)` (`TextStyle.cs:80,119`) dão o termo em
negrito marrom (visto em print). O "Crafted at:" nativo usa `tiny_font-hint_additional_text_selected` (#784F26) no
rótulo e #946E44 na lista.

## Provas no green (cenários em `docs/cenarios/`)

- `tooltip-duas-bolhas.cs`: três bolhas simultâneas (original + 2 clones) — print `docs/referencia/tooltip-aninhado-poc.png`.
  Clone: `Object.Instantiate(original.gameObject, original.transform.parent)`, `SetActive(false)`, canvas
  `sortingOrder` próprio, `AccessTools.Method(typeof(UITooltip), "ShowAtTargetAndCalculateOffsets").Invoke(clone, ...)`.
  Âncora no termo: RectTransform filho do TMP (`LayoutElement.ignoreLayout`) cobrindo o `characterInfo` do termo.
- Raycast no clone: `AddComponent<GraphicRaycaster>()` + TMP `raycastTarget = true` → `RaycastAll` acerta o clone;
  `TMP_TextUtilities.FindIntersectingLink(tmp, mouse, null)` acha `<link="kiln">`.
- Ajustes obrigatórios: clones não fecham com janela (assinar os eventos por conta própria); clonar de template limpo
  (filhos acrescentados, como âncoras, viajam no clone e ficam nos widgets do pool); no PoC as bolhas aninhadas
  **cobriam o conteúdo da mãe** — o mod deve abrir o aninhado ao lado.
- `tooltip-item.cs`, `tooltip-hud.cs`, `tooltip-perk.cs`: levam a cada tooltip nativo (referências em `docs/referencia/tooltip-*.png|txt`).
- `teclas.cs`: bindings ativos (do blue): WASD, Mouse0/1, Tab, I, P, **T (TechTree)**, M, Q, Esc, E, F, R,
  Space, 1–4, **LeftShift (AttackFocus)**. Livres: LeftAlt, RightAlt, LeftControl, Mouse2/3/4, G, Z, X, C, V.
  Fixos no código: Shift/Ctrl+clique (mover pilha), Shift+F5/F10/F11. Não adicionar bindings em `GameBindings`
  (regrava configurações, `GameSettings.cs:439`); usar `Config.Bind` com `KeyboardShortcut` + `UnityEngine.Input`.
- Cursor: `UIMouseTooltip.IsAvailable` true com mouse; `CursorController` esconde o cursor após 4 s parado em
  gameplay. No green o mouse nunca mexe (`Cursor.visible = false`): simular hover por código.

## Interceptação

- Todo tooltip passa por `UITooltip.ShowAtTargetAndCalculateOffsets` (privado, `:120`) → `ShowAtTarget` →
  `ShowAtPosition` (remove separadores finais, `ConstructWidgets`). Prefix ali recebe a lista por referência.
- Contexto da entidade: prefixes leves nos `Show*` públicos (`ShowItemCell :472`, `ShowCraftInfo :783`,
  `ShowCraftOutputCell :521`, `ShowPerk :354`, `ShowPerkWidget :584`, `ShowLinkedEntity :594`,
  `ShowGardenFertilizerSlot`, `ShowOrderWidget`, `ShowTalentLevelUpWidget`) e `UIMouseTooltip.TryShow` gravam
  tipo + objeto + target; o prefix do funil consome e limpa (finalizer). Vale o mais interno
  (`ShowItemCell` → `CustomTooltipShowAction` → `ShowCraftInfo`). Fallback: deduzir pelo target
  (`UIItemCell`, `LinkedEntityWidget`, `PerkWidget`, `UIBuffElement`).
- **Custo**: `ShowItemCell` é chamado a cada `Draw` da célula sob o mouse (`UIItemCell.CheckPreviousTooltip :681`).
  Conteúdo extra vem de cache; índices montados uma vez.
- Para tooltips aninhados, montar conteúdo com os `Add*Widgets` privados por reflection (`AddItemWidgets :1407`,
  `AddPerkWidgets :987`, `AddBuildingWidgets :946`...), não pelos `Show*` (som, `isHovered`).

| Tela | Disparo | Dado |
| --- | --- | --- |
| Inventário, baú, vendedor, hotbar, ingredientes | `UIItemCell.OnOver :717` → `ShowItemCell` | `DisplayingItem` (Item real), `.Definition`; inventário dono via `GetComponentInParent<InventoryWidget>().Data.Inventory` (inferido) |
| Craft (seleção / fila) | `UICraftPreviewItemCell.cs:75` / `UICraftQueueElementWidget.cs:129` → `ShowCraftInfo` | `CraftDef`, `WgoData` da estação (fila: target não é `UIItemCell`) |
| Craft: energia/insanidade, ticks | `UICraftRequirementWidget.cs:67`, `UIProgressCellsInfoWidget.cs:162` | `CraftDef`, perks, ferramenta |
| HUD: buffs | `UIBuffElement.cs:46,73` → `ShowPerk` | `PerkDef`; ativo em `perkSystemData.activePerks` |
| HUD: energia, insanidade, felicidade | `UIEnergySanityBar.cs:69,74`, `HUD.cs:155` → `ShowSimpleInfo` | só loc keys |
| Personagem, tech tree, quests | `PerkWidget`, `TalentLevelUpWidget`, `LinkedEntityWidget.cs:109` | `PerkData`, `TalentLevelUpDef`, `LinkedEntityType` + def |

## O que o nativo já mostra (não repetir)

- Item: cabeçalho, descrição `<id>_d`, efeitos (`AddItemEffectWidgets :1007`: qualidade, energia por uso de
  ferramenta, ataque, defesa, efeito ao usar com perks, tamanho de bolsa...), runas, estudo, "Crafted at"
  (`:1563-1580`), dica de uso. Nada da **instância** (durabilidade não aparece em UI nenhuma).
- Craft: nome + talento/nível, efeitos do resultado, extensão exigida, ingredientes **com possui/precisa**
  (`UITooltipNeedsItemWidgetData`), status da fila. Janela de craft mostra energia/insanidade por tick e ticks.
- Perk/buff: nome e descrição (tempo restante só na célula do HUD).

## Fontes de dados (todas só leitura, salvo nota)

| Info | API | Notas |
| --- | --- | --- |
| Usado em | varrer `GameBalance.Me.craftDefs` → `needItems` (+ `needItemsFromWgo`); grupos via `groupItemsCache` / `starGroupItemsCache` (`GameBalance.cs:121,124`), como `ScanNeedItems :338` | sem cache reverso nativo: montar uma vez. Filtrar como o jogo: `!isHidden`, prefixos `rem_`/`set_`, `doNotShowInTooltips`, conhecimento (`CraftComponent.cs:107,312-320`: `blackListCrafts`, `IsOneTimeCraftCompleted`, `isNeedsUnlock && !unlockedCrafts.Contains`) |
| Crafted at → estação → o que faz | `GameBalance.Me.craftInItemsCacheShownInTooltips[itemId]` (ids de WGO, `:142`); `craftsInCache[wgoId]` (`:136`); custo `buildableWgos[wgoId].needItems` (`:148`); extensões `workbenchParentsByExtensionCache` (`:178`) | nome da estação `LLBase.L(wgoId)`; widget nativo reconhecível por `Text.Contains(LLBase.L("ui_crafted_at"))` (técnica do próprio jogo, `:927`), ordem igual à do cache |
| Quantos tenho | `new MultiInventory(MainGame.PlayerData).GetTotalCount(id)` (jogador + baús da zona, `MultiInventory.cs:51-73`, igual a `UITooltip.cs:1656`); só jogador: `PlayerData.inventory.Data.GetTotalCountInInventory(id)` | `HasProperty` no sort tem escrita benigna de cache |
| Tempo/energia de craft | `craftDef.duration.EvaluateInt()` (ticks), `energyPerTick`, `insanityPerTick`, `talentLock`; `WGODef.autocraftTickDuration` | energia total = por tick × ticks é **inferido** (acertos/maestria); nunca `GetActionDamage` |
| Durabilidade | `item.TryGetProperty<DurabilitySerializedItemProperty>(out p)` → `p.Durability` (0..1); usos ≈ `Durability / Definition.durDecreaseOnUse` (`ToolComponent.cs:231`) | só no contexto `ShowItemCell` (há instância) |
| Perk | `PerkDef.duration`, `energyAdd`, `insanityAdd`, `craft*Bonus`, `tickRate`+`addGameResPerTick`; restante `PerkData.currentDuration`; `PerkSystemData.GetFormattedDuration :110` | origem: `ItemDef.GetPerksOnUse()` (`:465`), `bodyLinkedPerk`, `SermonDef.successRewardBuff` |
| Textos nativos úteis | `ui_crafted_at`, `ui_hint_extention_for`, `required_extension`, `tt_energy_spendings`, `tt_craft_tick_length`, `ui_effect_on_use`, `ui_perk` | `LLBase.L(id)` |

## Links (recursão)

Texto TMP com `<link="tipo:id">` dentro de `UITooltipTextWidgetData`, termo estilizado por um `TextStyle` nativo.
Tipos v1: **item** (`ItemDef.GetHeader()`), **estação** (`LLBase.L(wgoId)`), **craft** (`LLBase.L(craftDef.id)`,
item via `TryGetResultingItemDef()`), **perk** (`PerkDef.GetHeader()`). Ícone inline `id.FontIcon()` só para itens
cujo sprite está no atlas TMP (inferido; conferir). Ingredientes dentro do tooltip de craft já são `UIItemCell`
(`UITooltipCraftItemCell.cs:123`): num clone com raycast, o hover deve disparar o `ShowItemCell` nativo (inferido).

## Armadilhas novas

- `Wgo.GetWidgetData()` e o getter `Wgo.BubbleDrawableWidgets` **gravam estado** (`Wgo.cs:432,~463`). Nunca chamar.
- `LazyExpression.EvaluateChance` avança o `UnityEngine.Random`. Expressões `onUse*`, `onAdd*`, `expressionsOnSell/Buy`,
  `onCraft*`, `onPerTick*` podem conter `AddPerk`, `ReplaceWgoData`... — só ler a string crua. Só avaliar expressões
  que a UI nativa já avalia (`count`, `damage`, `gameResOnUse`, `duration`, `energy`, `autocraftTickDuration`).
- `Trading` tem setters privados de dinheiro/felicidade; não chamar `Vendor.TryAddMissingOrder` / `ForceLevelUp`.
- Escritas benignas de cache (ok): `Item.CheckPropertiesCreated`, getter `WgoData.WorldZoneData`,
  `CraftComponent.AvailableCrafts`, `CraftDef.TryGetResultingItemDef`, `ItemDef.GetPerksOnUse`.

## Hover no mundo (v2)

Alvo nativo é por proximidade (`PlayerInteractionComponent.cs:98`, máscara 65600 = layers 6 e 16). Do mouse:
`CameraSystem.ScreenPointToRay(Input.mousePosition)` → `Physics.RaycastAll(ray, ∞, 65600, Collide)` →
`GetComponentInParent<Wgo>().Data`; no green, primeiro acerto da layer 6 é o alvo em 7–9 de 12 (vizinho da frente
rouba; fogão sem collider próprio). Alternativa: projetar `wgo.BubbleDrawablePosition` e escolher o mais próximo do
cursor. Bolha ancorada: `IBubbleDrawable` próprio (SGuid novo) + `UIObjectBubbleManager.Instance.Display/Hide`.
Cenário: `docs/cenarios/hover-mundo.cs`.

## HUD (pasta `Hud/`, 08/11)

- Medalhão = `UIHUDWheel` (`UIHUDWheel.cs`): slot de baixo, sob `CurrentDay` (`hud-day_dial-curren_day_selector`), é o
  dia atual (`:500`, `CurrentDayNumber`); a seta acesa aponta o próximo (`NextDayNumber`, `:501`). Sol/lua giram com
  ângulo `timeOfDay*360-90` (`:188`): 0 = meia-noite embaixo, 0,5 = meio-dia em cima; casas/estrelas de dia em
  0,25–0,8 (`:201`), `GetTimeOfDayType` dia em 0,25–0,75 (`EnvironmentEngine.cs:303`). O jogo não tem relógio nem
  nome de dia: ids `day_*` (`LazyConsts.cs:356`) com números gluttony=1, sloth=2, lust=3, envy=4, pride=5, wrath=6
  (`ConstDef.Get(id).IntValue`); `LLBase.L("day_*")` devolve a chave. pt-br oficial dos pecados: `sin_orbe_*`.
- Energia/insanidade: barra dupla (`UIEnergySanityBar.cs:90`) — energia enche da esquerda, insanidade da direita, ambas
  `GetRes(id)/100` (`UIEnergySanityBarData.cs:26-27`). Máximo da energia = `100 - insanity`, da insanidade
  `80 - 10*perk_ironwill` (`gameResSystemDefs`): o vão escuro entre as duas é a energia perdida para a insanidade.
- Painel de tech points (`HUD.techPointsPanel`): moldura `hud-gameres_frame` (9-slice 6/0/6/0, 20 de altura, HLG pad
  7/7/2/0) + `tiny_font-perk_techpoint_cost`; aparece 5 s à direita da barra (Big) ou desce para (352,134) sob a
  felicidade no modo Small (`HUD.cs:830-846`). Texto sobre barra nativo: `tiny_font-inspiration-plate_bar_progress`.
- Dump do canto: `docs/referencia/hud-canto.txt`; cenários `hud-exterior-dia.cs`, `hud-interior-noite.cs`,
  `gk2hud-recursos.cs`, `gk2hud-hud-config.cs`.
