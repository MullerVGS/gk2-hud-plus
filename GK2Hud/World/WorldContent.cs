using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GK2Hud.Tooltips;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.World
{
    /// <summary>
    /// Conteúdo do tooltip de mundo no estilo dos tooltips do GK2Hud (TextStyles do próprio UITooltip): cabeçalho com o
    /// objeto, depois "o quê", progresso, tempo restante, quem trabalha, bloqueio e fila. Termos como links.
    /// Só leitura: nada de GetActionDamage, Wgo.GetWidgetData/BubbleDrawableWidgets, EvaluateChance ou Roll*/Set*.
    /// </summary>
    internal sealed class WorldContent
    {
        private const int MaxQueue = 4;

        /// <summary>
        /// <paramref name="detail"/> falso = hover: o básico (o quê, barra de células, quanto falta, o que trava) e a dica
        /// da tecla no canto. Verdadeiro = bolha fixada: entra o resto (maestria, fila, quem trabalha, ingredientes...).
        /// </summary>
        public List<LazyWidgetDataBase> Build(WorldTarget t, bool detail = false)
        {
            Styles s = Styles.Read();
            if (s == null || t == null) return null;
            var list = new List<LazyWidgetDataBase>();
            if (t.Drop != null)
            {
                if (WorldProcesses.IsRotting(t.Drop)) DropSection(list, s, t.Drop, detail);
                Names.Scrub(list);
                return list.Count == 0 ? null : list;
            }
            if (t.Data != null && t.Data.CraftComponent != null && WorldProcesses.HasCraftProcess(t.Data.CraftComponent))
            {
                CraftElementBase el = t.Data.CraftComponent.CurrentCraftElement;
                if (el.ParamsData != null && el.ParamsData.craftParamsType == CraftParamsData.CraftParamsType.GardenGrowing) GardenSection(list, s, t.Data, el, detail);
                else if (el.Def.isHidden && el.Def.isAuto) RenewSection(list, s, t.Data, el, detail);
                else CraftSection(list, s, t.Data, detail);
            }
            Names.Scrub(list);
            if (list.Count == 0) return null;
            if (!detail) AddHint(list);
            return list;
        }

        private static void AddHint(List<LazyWidgetDataBase> list)
        {
            string hint = TooltipFeature.Current?.PinHint;
            if (hint != null) list.Add(new PinHintData { Text = hint });
        }

        /// <summary>Linha "ícone + células". A janela nativa de craft corta a barra em 18 células.</summary>
        private const int MaxCells = 18;

        private static string Time(Styles s, float seconds, bool approx = false) => "cell_time".FontIcon() + Clock(s, seconds, approx);

        /// <summary>
        /// Conteúdo da bolha fixada: o normal enquanto o processo segue; senão uma linha curta no lugar do tempo congelado.
        /// "Concluído" quando o processo acabou ou o objeto foi trocado/removido; "Fora de alcance" quando o objeto ainda
        /// existe no save mas a cena/visual dele não está carregada.
        /// </summary>
        public List<LazyWidgetDataBase> BuildLive(WorldTarget t)
        {
            if (t == null) return null;
            if (t.Alive)
            {
                List<LazyWidgetDataBase> list = Build(t, true);
                if (list != null) return list;
                return StatusOnly(t, L10n.T(t.Drop != null ? "w_gone" : "w_done"));
            }
            bool exists = t.Drop != null
                ? !t.Drop.IsRemoving && (MainGame.WorldData?.GetGameSceneDataById(t.Drop.WorldId)?.droppedItems?.Contains(t.Drop) ?? false)
                : t.Data != null && MainGame.WorldData?.GetWgoData(t.Data.UniqueId) == t.Data && WorldProcesses.HasCraftProcess(t.Data.CraftComponent);
            if (exists) return StatusOnly(t, L10n.T("w_out_of_range"));
            return StatusOnly(t, L10n.T(t.Drop != null ? "w_gone" : "w_done"));
        }

        private static List<LazyWidgetDataBase> StatusOnly(WorldTarget t, string line)
        {
            Styles s = Styles.Read();
            if (s == null) return null;
            string header;
            if (t.Drop != null)
            {
                ItemDef def = t.Drop.Item?.Definition;
                header = def?.GetHeader();
            }
            else header = t.Data != null ? WgoName(t.Data) : null;
            var list = new List<LazyWidgetDataBase>();
            AddHeader(list, header, s);
            list.Add(Text(line, s.Description));
            Names.Scrub(list);
            return list;
        }

        // ---------------- corpo no chão ----------------

        /// <summary>Corpo largado no chão: some quando o timer zera (DropSystem.cs:137-160) e vira body_drop_grave_object.</summary>
        private static void DropSection(List<LazyWidgetDataBase> list, Styles s, DropData d, bool detail)
        {
            ItemDef def = d.Item.Definition;
            AddHeader(list, def != null ? Links.Wrap(Links.Item, def.id, def.GetHeader()) : null, s);
            var lines = new List<string> { Label(s, L10n.T("w_rots_in")) + Clock(s, d.AutoDestroyTimer) };
            // O DropView liga WhenVisibleOnScreen quando o chunk está visível (DropView.cs:1807): o timer não corre na tela.
            MultiFlagOR<CanNotBeAutoDestroyedReason> f = d.CanNotBeAutoDestroyed;
            if (detail)
            {
                if (f.HasFlag(CanNotBeAutoDestroyedReason.WhenVisibleOnScreen) && f.GetFlag(CanNotBeAutoDestroyedReason.WhenVisibleOnScreen))
                    lines.Add(L10n.T("w_rot_paused"));
                string after = Names.Wgo("body_drop_grave_object");
                if (after != null) lines.Add(Label(s, L10n.T("w_then")) + after);
                else lines.Add(L10n.T("w_rots_then"));
            }
            list.AddRange(Widgets(lines, s));
        }

        // ---------------- canteiro ----------------

        /// <summary>
        /// Crescimento (autocraft GardenGrowing, isHidden, sem bolha nativa): por tick avança 1 célula (boa ou falhada,
        /// sorteio com chance maestria/trava) ou clamp(maestria/trava, 1..3) boas quando a maestria alcança a trava
        /// (CraftComponent.UpdateGardenGrowingCraft :1387). Cada célula boa soma gameresPerSuccessfulProgress no gameRes do
        /// canteiro (WgoData.OnSuccessfulTicksChange); a colheita sai de addItemsToWgoOnFinish (ex.: carrot = @crop + perks).
        /// </summary>
        private static void GardenSection(List<LazyWidgetDataBase> list, Styles s, WgoData w, CraftElementBase el, bool detail)
        {
            CraftComponent cc = w.CraftComponent;
            var cd = el.Def as CraftDef;
            List<ChanceOutputItem> outs = el.Def.addItemsToWgoOnFinish?.chanceOutputItems;
            ChanceOutputItem main = outs != null && outs.Count > 0 ? outs[0] : null;
            ItemDef crop = main != null ? GameBalance.Me.GetDataOrNull<ItemDef>(main.id) : null;
            AddHeader(list, WgoName(w), s);

            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            int good = Mathf.Max(0, el.SucceededProgressTicks), bad = Mathf.Max(0, el.FailedProgressTicks);

            // A mesma barra da janela do canteiro (UIGardenBedWindow.UpdateProgressBar): boas à esquerda, falhadas no
            // fim, estrela/+1 nas células que rendem colheita.
            if (crop != null) list.Add(Text(ItemLink(crop), s.Description));
            var row = new ProgressRowData { IconId = crop?.iconId };
            int shown = Mathf.Min(total, MaxCells);
            for (int i = 0; i < shown; i++)
                row.Cells.Add(new TipCell
                {
                    State = i < good ? TipCell.Good : i >= total - bad ? TipCell.Failed : TipCell.Empty,
                    Star = -1,
                    Garden = cd != null ? cd.GetQualityForGardenProgressTick(i + 1) : -1,
                });
            row.More = total - shown;
            list.Add(row);

            int mastery = 0, lockv = 1;
            try { mastery = el.ParamsData.MasteryValue; lockv = Mathf.Max(1, el.ParamsData.MasteryLock); }
            catch { }
            bool zombie = w.Worker is ZombieWgoData;
            // Como o jogo (CraftComponent.cs:1397-1408): M ≥ L → clamp(clamp(M/L, 0, max_cells_per_one_hit), 0, 3) células
            // boas; senão sorteio de 1 boa com chance M/L. Sem zumbi, um tick de 0 células ainda avança 1 (falha,
            // CraftElementBase.Update :506); com zumbi, 0 não avança.
            float cells;
            if (mastery >= lockv)
            {
                int maxCells = ConstDef.Get("max_cells_per_one_hit").IntValue;
                cells = Mathf.Clamp(Mathf.Clamp(mastery / lockv, 0, maxCells), 0, 3);
            }
            else cells = zombie ? Mathf.Clamp01(mastery / (float)lockv) : 0f;
            if (!zombie && cells < 1f) cells = 1f;
            if (done >= total) list.Add(Text(L10n.T("w_finishing"), s.Small));
            else if (cc.IsWorkPausedByMove()) list.Add(Text(L10n.T("w_paused_move"), s.Small));
            else if (cells > 0f)
            {
                float per = WorldProcesses.SecondsPerTick(cc);
                int ticks = Mathf.CeilToInt((total - done) / cells);
                list.Add(Text(Time(s, Mathf.Max(0f, ticks * per - WorldProcesses.TickElapsed(cc)), approx: mastery < lockv && zombie), s.Small));
            }
            if (!detail) return;

            // Chance de célula boa: o widget que o jogo mostra ao passar o mouse na chance, na janela do canteiro.
            TalentDef talent = w.Definition != null && !string.IsNullOrEmpty(w.Definition.talent) ? GameBalance.Me.GetDataOrNull<TalentDef>(w.Definition.talent) : null;
            if (talent != null)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(new UITooltipProgressTicksWidgetData(mastery, lockv, true, talent));
            }

            // Adubo: perks perk_fertilize_* do canteiro (sem duração, WgoData.cs:2490). Sem adubo, sem linha.
            var fert = new List<string>();
            if (w.ActivePerks != null)
                foreach (PerkData p in w.ActivePerks)
                    if (p?.Definition != null && p.Definition.IsFertilizerPerk) fert.Add(Links.Wrap(Links.Perk, p.Definition.id, p.Definition.GetHeader()));
            if (fert.Count > 0) AddBlock(list, new List<LazyWidgetDataBase> { Text(Label(s, L10n.T("w_fertilizer")) + string.Join(", ", fert), s.Small) });

            // Colheita acumulada (gameRes do canteiro) em células de item nativas; o que ainda está em zero não entra.
            if (outs != null && cd != null)
            {
                var harvest = new List<NeedItemData>();
                foreach (ChanceOutputItem o in outs)
                {
                    ItemDef idef = GameBalance.Me.GetDataOrNull<ItemDef>(o.id);
                    if (idef == null || o.count == null || !o.count.HasExpression) continue;
                    string expr = o.count.ToString();
                    int now = 0;
                    System.Text.RegularExpressions.MatchCollection ms = ResRx.Matches(expr);
                    foreach (System.Text.RegularExpressions.Match m in ms) now += w.GetGameResInt(m.Groups[1].Value);
                    if (ms.Count == 0 && !int.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out now)) continue;
                    if (now > 0) harvest.Add(new NeedItemData(idef.id, now));
                }
                if (harvest.Count > 0)
                {
                    list.Add(new UITooltipSeparatorWidgetData());
                    list.Add(Text(L10n.T("w_harvest"), s.SmallSelected));
                    list.Add(new UITooltipNeedsItemWidgetData(harvest, new MultiInventory(new Inventory("inventory", 1)), w));
                }
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ResRx = new System.Text.RegularExpressions.Regex("WGOPar\\(\"([^\"]+)\"\\)");

        // ---------------- renovação (autocraft escondido) ----------------

        /// <summary>
        /// Autocraft isHidden que o jogo não mostra: árvores/arbustos/cogumelos que voltam (grupo spawner, Spawning() troca
        /// o objeto pelo próximo estágio), colmeia, poço, latrina. Spawner pronto espera o jogador se afastar
        /// (WgoData.OnCraftEnd :1136, wgoDelayedSpawnSystemData).
        /// </summary>
        private static void RenewSection(List<LazyWidgetDataBase> list, Styles s, WgoData w, CraftElementBase el, bool detail)
        {
            CraftComponent cc = w.CraftComponent;
            AddHeader(list, WgoName(w), s);
            var lines = new List<string>();
            List<SpawnStage> stages = w.SpawnWGOComponent?.SpawnStages;
            int stage = stages == null ? -1 : stages.FindIndex(x => x != null && x.craftId == el.CraftId);
            if (detail && stage >= 0 && stages.Count > 1) lines.Add(Label(s, L10n.T("w_stage")) + Num(s, (stage + 1) + "/" + stages.Count));
            ItemDef res = ResultItem(el.Def);
            if (res != null) lines.Add(Label(s, L10n.T("w_brings")) + ItemLink(res));
            else if (TryAddedToWgo(el.Def, out string addId, out int addCount) && GameBalance.Me.GetDataOrNull<ItemDef>(addId) is ItemDef add)
            {
                // Poço: onCraftEndExpressions = AddItemToWgoInv("water",1) + StartCraft de novo (só a string crua é lida).
                int stock = w.Inventory?.Data?.GetTotalCountInInventory(addId) ?? 0;
                lines.Add(Label(s, L10n.T("w_brings")) + ItemLink(add) + " " + Num(s, "×" + addCount) + " · " + L10n.T("w_stock", Num(s, stock.ToString())));
            }
            else if (el.Def is CraftDef rd && !string.IsNullOrEmpty(rd.replaceWgoId) && rd.replaceWgoId != "0")
            {
                // Colmeia, cogumelo, arbusto: o craft troca o objeto pelo "cheio" (replaceWgoId).
                string n = Names.Wgo(rd.replaceWgoId);
                if (n != null && n != WgoName(w)) lines.Add(Label(s, L10n.T("w_becomes")) + n);
            }
            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            bool delayed = MainGame.Instance?.GameSave?.wgoDelayedSpawnSystemData?.Contains(w) ?? false;
            if (delayed) lines.Add(L10n.T("w_wait_away"));
            else if (cc.Status == CraftComponentStatus.ReadyToFinishAutoCraft || (el.IsStarted && done >= total)) lines.Add(L10n.T("w_finishing"));
            else if (el.IsStarted)
                lines.Add(Time(s, AutoSecondsLeft(cc, el)));
            else lines.Add(L10n.T("w_not_started"));
            if (detail && el.IsStarted && total > 0) lines.Add(Label(s, L10n.T("w_progress")) + Num(s, done + "/" + total) + " " + L10n.T("w_ticks_word"));
            list.AddRange(Widgets(lines, s));
        }

        /// <summary>Item que o craft entrega (saída comum ou o que vai para o objeto ao terminar), sem avaliar expressões.</summary>
        private static ItemDef ResultItem(CraftDefBase def)
        {
            string id = null;
            if (def.outputItems?.chanceOutputItems != null && def.outputItems.chanceOutputItems.Count > 0) id = def.outputItems.chanceOutputItems[0].id;
            else if (def.addItemsToWgoOnFinish?.chanceOutputItems != null && def.addItemsToWgoOnFinish.chanceOutputItems.Count > 0) id = def.addItemsToWgoOnFinish.chanceOutputItems[0].id;
            return string.IsNullOrEmpty(id) ? null : GameBalance.Me.GetDataOrNull<ItemDef>(id);
        }

        private static readonly System.Text.RegularExpressions.Regex AddToWgoRx = new System.Text.RegularExpressions.Regex("AddItemToWgoInv\\(\"([^\"]+)\"\\s*,\\s*(\\d+)\\)");

        private static bool TryAddedToWgo(CraftDefBase def, out string id, out int count)
        {
            id = null; count = 0;
            if (!(def is CraftDef cd) || cd.onCraftEndExpressions == null) return false;
            foreach (LazyExpression e in cd.onCraftEndExpressions)
            {
                if (e == null || !e.HasExpression) continue;
                var m = AddToWgoRx.Match(e.ToString());
                if (!m.Success) continue;
                id = m.Groups[1].Value;
                count = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                return true;
            }
            return false;
        }

        private static string ItemLink(ItemDef d) => Links.Wrap(Links.Item, d.id, d.GetHeader());

        // ---------------- craft ----------------

        private void CraftSection(List<LazyWidgetDataBase> list, Styles s, WgoData w, bool detail)
        {
            CraftComponent cc = w.CraftComponent;
            CraftElementBase el = cc.CurrentCraftElement;
            CraftDefBase def = el.Def;
            string name = WgoName(w);
            AddHeader(list, name != null ? Links.Wrap(Links.Station, w.id, name) : null, s);

            // O quê: nome do craft (link) e quantidade que sai (pré-rolada no início do craft, CraftElementBase.cs:486).
            string what = CraftLink(def);
            int outCount = OutputCount(el, w);
            if (what != null && outCount > 1) what += " " + Num(s, "×" + outCount);
            if (what != null) list.Add(Text(what, s.Description));

            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            bool started = el.IsStarted;
            bool auto = def.isAuto;
            IWorker worker = w.Worker;
            var zombie = worker as ZombieWgoData;
            CraftComponentStatus st = cc.Status;

            // Barra de células como a da janela de craft (UIBaseCraftSelectionWindow.UpdateProgressBar), com as estrelas
            // do craft estrela. Antes de começar, a duração do balanço em células vazias.
            int span = started ? total : DurationTicks(el, w);
            var row = new ProgressRowData { IconId = ResultItem(def)?.iconId };
            int shown = Mathf.Min(span, MaxCells);
            var sc = def as CraftDef;
            for (int i = 0; i < shown; i++)
            {
                int star = -1;
                if (sc != null && sc.isStarCraft) star = i + 1 == sc.goldLevel ? 3 : i + 1 == sc.silverLevel ? 2 : i + 1 == sc.bronzeLevel ? 1 : -1;
                row.Cells.Add(new TipCell { State = started && i < done * shown / Mathf.Max(1, span) ? TipCell.Good : TipCell.Empty, Star = star, Garden = -1 });
            }
            row.More = span - shown;
            if (row.Cells.Count > 0 || row.IconId != null) list.Add(row);

            // Estado e tempo.
            string state = null, time = null;
            bool ready = started && total > 0 && done >= total;
            if (st == CraftComponentStatus.ReadyToFinishAutoCraft) state = L10n.T(cc.IsServicedByZombieOperator ? "w_ready_zombie" : "w_ready_collect");
            else if (st == CraftComponentStatus.WaitingForWorkerPickUp) state = L10n.T("w_wait_pickup");
            else if (st == CraftComponentStatus.WaitingForOutputDrop) state = L10n.T("w_wait_drop");
            else if (st == CraftComponentStatus.FinishDelayed) state = L10n.T("w_finish_blocked");
            else if (ready) state = L10n.T("w_finishing");
            else if (!started) state = L10n.T(cc.NeedsZombieOperatorToStartCraft ? "w_wait_zombie_start" : "w_not_started");
            else if (cc.IsWorkPausedByMove()) state = L10n.T("w_paused_move");
            // Zumbi artesão com ordem pendente para o craft: CraftComponent.Update sai cedo no autocraft (:721) e a
            // ZombieCraftActivity não bate no manual (ZombieCraftActivity.cs:99).
            else if (zombie != null && zombie.CrafterOrders.Count > 0) state = L10n.T("w_zombie_busy");
            else if (auto) time = Time(s, AutoSecondsLeft(cc, el));
            else if (zombie != null)
            {
                float per = ZombieSecondsPerTick(zombie, w, el);
                time = per > 0f ? Time(s, (total - done) * per, approx: true) : null;
            }
            else state = L10n.T("w_needs_you", Num(s, (total - done).ToString()));

            // Bloqueio: CraftStatus ≠ OK traduzido. No hover ele toma o lugar do estado (é o que o jogador precisa saber).
            CraftStatus cs = el.CraftStatus;
            bool blocked = cs != CraftStatus.OK && !ready;
            if (time != null) list.Add(Text(time, s.Small));
            if (state != null && (detail || !blocked)) list.Add(Text(state, s.Small));
            if (blocked) list.Add(Text(StatusText(cs), Native.Style("redEffectTextStyle") ?? s.Small));

            // Ingredientes que faltam para o próximo craft (widget nativo com possui/precisa): é a resposta ao bloqueio.
            bool needsShown = false;
            if (!started && cs == CraftStatus.NotEnoughResources && def is CraftDef cdef)
            {
                var needs = new UITooltipNeedsItemWidgetData(cdef, cc);
                if (needs.CraftItemCellsData.Count > 0) { list.Add(needs); needsShown = true; }
            }
            if (!detail) return;

            // Tempo da fila inteira (só autocraft: o resto depende de golpes): vai ao lado do rótulo "Next".
            List<CraftElementBase> queue = cc.CraftElementsQueue;
            string queueTime = null;
            if (auto && queue.Count > 0)
            {
                float all = QueueSecondsLeft(cc, w, el, out bool infinite, out _);
                if (!infinite && all > 0f && (queue.Count > 1 || el.Count > 1)) queueTime = Time(s, all, approx: true);
            }
            // Quem trabalha só entra quando não é a própria máquina.
            var lines = new List<string>();
            if (zombie != null || (auto && cc.IsServicedByZombieOperator)) lines.Add(Label(s, L10n.T("w_worker")) + L10n.T("w_worker_zombie"));
            else if (!auto) lines.Add(Label(s, L10n.T("w_worker")) + L10n.T("w_worker_you"));

            // Fila: o que vem depois, em células de item nativas com a quantidade; sem item para mostrar, em texto.
            var nextCells = new List<NeedItemData>();
            var next = new List<string>();
            int extra = 0;
            bool allItems = true;
            void Queue(CraftDefBase d, int count, bool infinite)
            {
                if (next.Count >= MaxQueue) { extra++; return; }
                ItemDef item = ResultItem(d);
                if (item == null || infinite) allItems = false;
                else nextCells.Add(new NeedItemData(item.id, count));
                next.Add(CraftLink(d) + (infinite ? " " + L10n.T("w_repeat") : count > 1 ? " " + Num(s, "×" + count) : ""));
            }
            if ((el.Count > 1 || el.IsInfinite) && CraftLink(def) != null) Queue(def, el.Count - 1, el.IsInfinite);
            foreach (CraftElementBase q in queue)
            {
                if (q == null || q == el || q.Def == null || q.Count <= 0 || CraftLink(q.Def) == null) continue;
                Queue(q.Def, q.Count, q.IsInfinite);
            }
            if (next.Count > 0)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                if (allItems && extra == 0)
                {
                    list.Add(Text((s.SmallSelected.ApplyStyleToString(L10n.T("w_next")) ?? "") + (queueTime != null ? "  " + queueTime : ""), s.Small));
                    list.Add(new UITooltipNeedsItemWidgetData(nextCells, new MultiInventory(new Inventory("inventory", 1)), w));
                }
                else list.Add(Text(Label(s, L10n.T("w_next")) + string.Join(", ", next) + (extra > 0 ? " +" + extra : "") + (queueTime != null ? "  " + queueTime : ""), s.Small));
            }
            else if (queueTime != null) lines.Add(Label(s, L10n.T("w_queue_time")) + queueTime);

            // Combustível guardado na estação (o mesmo número da bolha nativa, Wgo.TryGetFuelContainerStoredItem). Com as
            // células de ingredientes na bolha, o combustível que falta já está nelas.
            string fuel = FuelLine(s, w) ?? (needsShown ? null : FuelNeedLine(s, w, def));
            if (fuel != null) lines.Add(fuel);
            AddBlock(list, Widgets(lines, s));
        }

        private static int OutputCount(CraftElementBase el, WgoData w)
        {
            if (el.IsStarted && el.PreOutputItems != null && el.PreOutputItems.Count > 0) return el.PreOutputItems[0].count;
            return 1;
        }

        /// <summary>Tempo real do item atual: ticks que faltam × s/tick − fração do tick em curso (CraftComponent.cs:756-768).</summary>
        public static float AutoSecondsLeft(CraftComponent cc, CraftElementBase el)
        {
            float per = WorldProcesses.SecondsPerTick(cc);
            int left = Mathf.Max(0, el.TotalProgressTicks - el.ProgressTicks);
            return Mathf.Max(0f, left * per - WorldProcesses.TickElapsed(cc));
        }

        /// <summary>Espera fixa entre itens da fila: segura 0,6 s antes de concluir (FINISH_HELD_TIME, CraftComponent.cs:23,825) e tenta reiniciar a cada 1 s (QueueDelayed, :725-744).</summary>
        private const float BetweenItemsSeconds = 1.6f;

        /// <summary>
        /// Estimativa (sempre "~"): atual (com repetições) + seguintes da fila, cada um com duração do balanço + perks
        /// (CraftElementBase.Start :482) e a espera entre itens. Autocraft visível sem autoFinishAutoCraft para em
        /// ReadyToFinishAutoCraft até alguém retirar (CraftComponent.cs:871): <paramref name="collect"/> avisa isso.
        /// </summary>
        private static float QueueSecondsLeft(CraftComponent cc, WgoData w, CraftElementBase cur, out bool infinite, out bool collect)
        {
            infinite = false;
            collect = false;
            float per = WorldProcesses.SecondsPerTick(cc);
            float sum = 0f;
            if (cur.IsStarted) sum += AutoSecondsLeft(cc, cur);
            foreach (CraftElementBase q in cc.CraftElementsQueue)
            {
                if (q == null || q.Def == null || q.Count <= 0) continue;
                if (q.IsInfinite) { infinite = true; return 0f; }
                if (q.Def.isAuto && !q.Def.isHidden && !q.Def.autoFinishAutoCraft) collect = true;
                int reps = q == cur && cur.IsStarted ? q.Count - 1 : q.Count;
                if (reps <= 0) continue;
                if (!q.Def.isAuto) continue;
                sum += reps * (DurationTicks(q, w) * per + BetweenItemsSeconds);
            }
            return sum;
        }

        /// <summary>
        /// Ticks que o item ainda vai andar ao começar: duração + perks, menos o adiantamento com que craft estrela e de
        /// autópsia começam (CraftStartTicks, CraftElementBase.Start :478).
        /// </summary>
        private static int DurationTicks(CraftElementBase q, WgoData w)
        {
            if (q.Def.duration == null || !q.Def.duration.HasExpression) return 0;
            int add = 0, head = 0;
            try
            {
                if (q.ParamsData != null)
                {
                    add = q.ParamsData.PerksCraftAddTotalProgressTicksValue;
                    if (q.Def.isStarCraft || q.Def.isAutopsyCraft) head = q.ParamsData.CraftStartTicks;
                }
            }
            catch { add = head = 0; }
            return Mathf.Max(0, q.Def.duration.EvaluateInt(w) + add - head);
        }

        /// <summary>
        /// Segundos por tick com zumbi (craft comum): golpes por segundo simulados (ZombieHitsPerSecond), a cada
        /// zombie_craft_sub_ticks_count golpes soma clamp(max(maestria, trava)/trava, 0, max_cells_per_one_hit)
        /// (ZombieCraftActivity.cs:83-150 e GetActionDamage :160). Craft estrela é aleatório: estimativa pela média.
        /// </summary>
        private static float ZombieSecondsPerTick(ZombieWgoData z, WgoData w, CraftElementBase el)
        {
            int sub = Mathf.Max(1, ConstDef.Get("zombie_craft_sub_ticks_count").IntValue);
            int maxCells = Mathf.Max(1, ConstDef.Get("max_cells_per_one_hit").IntValue);
            int mastery = z.GetMasteryLevelForTalentBranch(w.Definition.talent, el.Def);
            int lockv = el.Def.talentLock;
            float cells;
            if (!el.Def.isStarCraft)
            {
                if (lockv == 0) { lockv = 1; if (mastery == 0) mastery = 1; }
                cells = Mathf.Clamp(Mathf.Max(mastery, lockv) / lockv, 0, maxCells);
            }
            else
            {
                cells = mastery < lockv ? Mathf.Clamp01(mastery / (float)Mathf.Max(1, lockv)) : Mathf.Clamp(mastery / Mathf.Max(1, lockv), 0, maxCells);
            }
            if (cells <= 0f) return -1f;
            float mod = 0f;
            Item hand = z.Hand;
            if (hand != null && !hand.IsEmpty && hand.Definition != null && hand.Definition.isTool && !el.Def.isAuto
                && el.Def is CraftDef cd && cd.zombieSpeedItemModificators != null)
                foreach (GameResAtom a in cd.zombieSpeedItemModificators.List)
                    if (a.type == hand.id) { mod = a.value; break; }
            float hitsPerSecond = ZombieHitsPerSecond(mod);
            return hitsPerSecond > 0f ? sub / (hitsPerSecond * cells) : -1f;
        }

        private static readonly Dictionary<float, float> HitsPerSecondCache = new Dictionary<float, float>();

        /// <summary>
        /// Golpes (sub-ticks) por segundo de jogo simulando a recorrência da ZombieCraftActivity.Update (:114-145) no passo
        /// do CraftSystem (0,2 s, MainGame.cs:204): p += dt; p += mod·p; se p ≥ 1, p -= floor(p) e conta UM golpe
        /// (UseTool soma 1 sub-tick por chamada, :231-246). Em float, como o jogo.
        /// </summary>
        private static float ZombieHitsPerSecond(float mod)
        {
            if (HitsPerSecondCache.TryGetValue(mod, out float v)) return v;
            const float dt = 0.2f;
            const int steps = 3000;   // 600 s de jogo
            float p = 0f;
            int hits = 0;
            for (int i = 0; i < steps; i++)
            {
                p += dt;
                p += mod * p;
                if (!(p >= 1f)) continue;
                p -= Mathf.FloorToInt(p);
                hits++;
            }
            v = hits / (steps * dt);
            HitsPerSecondCache[mod] = v;
            return v;
        }

        public static string StatusText(CraftStatus cs)
        {
            switch (cs)
            {
                case CraftStatus.NotEnoughResources: return L10n.T("cs_resources");
                case CraftStatus.NotEnoughEnergy: return L10n.T("cs_energy");
                case CraftStatus.NotEnoughInsanity: return L10n.T("cs_insanity");
                case CraftStatus.NotEnoughTalent: return L10n.T("cs_talent");
                case CraftStatus.DoesntHaveRequiredTool: return L10n.T("cs_tool");
                case CraftStatus.NotEnoughFuel: return L10n.T("cs_fuel");
                case CraftStatus.NotEnoughSpaceInWgo: return L10n.T("cs_space_wgo");
                case CraftStatus.DoesntHaveItemWithEnoughDurability: return L10n.T("cs_durability");
                case CraftStatus.NotEnoughMastery: return L10n.T("cs_mastery");
                case CraftStatus.NotEnoughSpaceInMultiInventory: return L10n.T("cs_space");
                case CraftStatus.NoExtension: return L10n.T("cs_extension");
                default: return L10n.T("cs_other");
            }
        }

        private static string FuelLine(Styles s, WgoData w)
        {
            WGODef d = w.Definition;
            if (d == null || !d.isFuelContainer) return null;
            string itemId = d.fuelItemId;
            if (string.IsNullOrEmpty(itemId) && GameBalance.Me.craftsInCache.TryGetValue(w.id, out List<CraftDefBase> crafts) && crafts != null)
            {
                List<string> black = MainGame.Instance?.GameSave?.knowledgeSystem?.blackListCrafts;
                foreach (CraftDefBase c in crafts)
                    if (c != null && c.isFuelCraft && c.FuelItemDef != null && (black == null || !black.Contains(c.id))) { itemId = c.FuelItemDef.id; break; }
            }
            if (string.IsNullOrEmpty(itemId)) return null;
            int count = w.Inventory?.Data?.GetTotalCountInInventory(itemId) ?? 0;
            ItemDef fd = GameBalance.Me.GetDataOrNull<ItemDef>(itemId);
            if (fd == null) return null;
            return Links.Wrap(Links.Item, itemId, itemId.FontIcon() + Num(s, count.ToString()));
        }

        /// <summary>
        /// Craft que consome combustível como ingrediente (forno: "fire", tirado da lenha da zona): quanto há no
        /// inventário múltiplo da estação (WgoData.GetCraftableMultiInventory, o mesmo que o craft usa) e para quantos
        /// crafts dá. Só leitura (MultiInventory novo, contagem de itens).
        /// </summary>
        private static string FuelNeedLine(Styles s, WgoData w, CraftDefBase def)
        {
            if (def?.needItems == null) return null;
            foreach (NeedItemData n in def.needItems)
            {
                if (n == null || n.IsGroup || string.IsNullOrEmpty(n.id)) continue;
                ItemDef d = GameBalance.Me.GetDataOrNull<ItemDef>(n.id);
                if (d == null || !d.isFuel) continue;
                int have = w.GetCraftableMultiInventory().GetTotalCount(n.id);
                int per = Mathf.Max(1, n.GetCount(w));
                // O item "fire" se chama "Fuel"/"Combustível": aí o número vira o link, sem repetir o nome.
                // O ícone e o número como a bolha nativa do depósito de lenha; ao lado, para quantos crafts dá.
                return Links.Wrap(Links.Item, d.id, d.id.FontIcon() + Num(s, have.ToString())) + "  " + Num(s, "×" + (have / per));
            }
            return null;
        }

        // ---------------- utilidades ----------------

        /// <summary>Nome do objeto (<see cref="Names.Wgo"/>) ou null quando o jogo e o glossário não têm nome para ele.</summary>
        public static string WgoName(WgoData w) => Names.Wgo(w?.id);

        /// <summary>Craft como link; sem nome próprio, o item que ele entrega; null quando não há nome mostrável.</summary>
        private static string CraftLink(CraftDefBase def)
        {
            if (def is CraftDef cd && !cd.id.StartsWith("mix") && TooltipContent.CraftName(cd) is string n) return Links.Wrap(Links.Craft, cd.id, n);
            string t = Names.Text(def.id);
            if (t != null) return t;
            ItemDef res = ResultItem(def);
            return res != null ? ItemLink(res) : null;
        }

        private static void AddHeader(List<LazyWidgetDataBase> list, string header, Styles s)
        {
            if (string.IsNullOrEmpty(header)) return;
            list.Add(Text(header, s.Header));
            list.Add(new UITooltipSeparatorWidgetData());
        }

        private static string Label(Styles s, string label) => s.SmallSelected.ApplyStyleToString(label + ": ") ?? "";

        private static string Num(Styles s, string v) => s.Description.ApplyStyleToString(v);

        private static string Clock(Styles s, float seconds, bool approx = false) =>
            Num(s, (approx ? "~" : "") + PerkSystemData.GetFormattedDuration(Mathf.Ceil(Mathf.Max(0f, seconds))));

        private static UITooltipTextWidgetData Text(string text, TextStyle style) => new UITooltipTextWidgetData(text, TextAlignmentOptions.Center, style);

        private static List<LazyWidgetDataBase> Widgets(List<string> lines, Styles s) =>
            lines.Select(l => (LazyWidgetDataBase)Text(l, s.Small)).ToList();

        private static void AddBlock(List<LazyWidgetDataBase> list, List<LazyWidgetDataBase> lines)
        {
            if (lines == null || lines.Count == 0) return;
            if (list.Count > 0 && !(list[list.Count - 1] is UITooltipSeparatorWidgetData)) list.Add(new UITooltipSeparatorWidgetData());
            list.AddRange(lines);
        }

        /// <summary>Assinatura do conteúdo (textos e tipos) para só redesenhar quando algo muda.</summary>
        public static string Signature(List<LazyWidgetDataBase> list)
        {
            var sb = new StringBuilder();
            foreach (LazyWidgetDataBase d in list) AppendKey(sb, d).Append('\n');
            return sb.ToString();
        }

        /// <summary>Chave de um item só (o redesenho no lugar compara item a item, ver <see cref="Tooltips.TipPatch"/>).</summary>
        public static string ItemKey(LazyWidgetDataBase d)
        {
            if (d is UITooltipTextWidgetData t) return t.Text ?? string.Empty;
            return AppendKey(new StringBuilder(), d).ToString();
        }

        private static StringBuilder AppendKey(StringBuilder sb, LazyWidgetDataBase d)
        {
            if (d is UITooltipTextWidgetData t) sb.Append(t.Text);
            else if (d is TipData td) sb.Append(td.Sig);
            else if (d is UITooltipProgressTicksWidgetData pt) sb.Append("mastery:").Append(pt.MasteryValue).Append('/').Append(pt.MasteryLock);
            else if (d is UITooltipNeedsItemWidgetData ni)
            {
                sb.Append("cells:");
                foreach (UICraftItemCellData c in ni.CraftItemCellsData) sb.Append(c.currentItem?.id).Append('×').Append(c.currentItem?.GetCount()).Append(',');
            }
            else sb.Append(d.GetType().Name);
            return sb;
        }
    }
}
