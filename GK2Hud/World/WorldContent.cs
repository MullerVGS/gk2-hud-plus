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

        public List<LazyWidgetDataBase> Build(WorldTarget t)
        {
            Styles s = Styles.Read();
            if (s == null || t == null) return null;
            var list = new List<LazyWidgetDataBase>();
            if (t.Drop != null)
            {
                if (WorldProcesses.IsRotting(t.Drop)) DropSection(list, s, t.Drop);
                return list.Count == 0 ? null : list;
            }
            if (t.Data != null && t.Data.CraftComponent != null && WorldProcesses.HasCraftProcess(t.Data.CraftComponent))
            {
                CraftElementBase el = t.Data.CraftComponent.CurrentCraftElement;
                if (el.ParamsData != null && el.ParamsData.craftParamsType == CraftParamsData.CraftParamsType.GardenGrowing) GardenSection(list, s, t.Data, el);
                else if (el.Def.isHidden && el.Def.isAuto) RenewSection(list, s, t.Data, el);
                else CraftSection(list, s, t.Data);
            }
            return list.Count == 0 ? null : list;
        }

        // ---------------- corpo no chão ----------------

        /// <summary>Corpo largado no chão: some quando o timer zera (DropSystem.cs:137-160) e vira body_drop_grave_object.</summary>
        private static void DropSection(List<LazyWidgetDataBase> list, Styles s, DropData d)
        {
            ItemDef def = d.Item.Definition;
            string name = def != null ? def.GetHeader() : d.Id;
            list.Add(Text(def != null ? Links.Wrap(Links.Item, def.id, name) : name, s.Header));
            list.Add(new UITooltipSeparatorWidgetData());
            var lines = new List<string> { Label(s, L10n.T("w_rots_in")) + Clock(s, d.AutoDestroyTimer) };
            // O DropView liga WhenVisibleOnScreen quando o chunk está visível (DropView.cs:1807): o timer não corre na tela.
            MultiFlagOR<CanNotBeAutoDestroyedReason> f = d.CanNotBeAutoDestroyed;
            if (f.HasFlag(CanNotBeAutoDestroyedReason.WhenVisibleOnScreen) && f.GetFlag(CanNotBeAutoDestroyedReason.WhenVisibleOnScreen))
                lines.Add(L10n.T("w_rot_paused"));
            string after = LLBase.L("body_drop_grave_object");
            if (after != "body_drop_grave_object") lines.Add(Label(s, L10n.T("w_then")) + after);
            else lines.Add(L10n.T("w_rots_then"));
            list.AddRange(Widgets(lines, s));
        }

        // ---------------- canteiro ----------------

        /// <summary>
        /// Crescimento (autocraft GardenGrowing, isHidden, sem bolha nativa): por tick avança 1 célula (boa ou falhada,
        /// sorteio com chance maestria/trava) ou clamp(maestria/trava, 1..3) boas quando a maestria alcança a trava
        /// (CraftComponent.UpdateGardenGrowingCraft :1387). Cada célula boa soma gameresPerSuccessfulProgress no gameRes do
        /// canteiro (WgoData.OnSuccessfulTicksChange); a colheita sai de addItemsToWgoOnFinish (ex.: carrot = @crop + perks).
        /// </summary>
        private static void GardenSection(List<LazyWidgetDataBase> list, Styles s, WgoData w, CraftElementBase el)
        {
            CraftComponent cc = w.CraftComponent;
            var cd = el.Def as CraftDef;
            List<ChanceOutputItem> outs = el.Def.addItemsToWgoOnFinish?.chanceOutputItems;
            ChanceOutputItem main = outs != null && outs.Count > 0 ? outs[0] : null;
            ItemDef crop = main != null ? GameBalance.Me.GetDataOrNull<ItemDef>(main.id) : null;
            list.Add(Text(WgoName(w), s.Header));
            list.Add(new UITooltipSeparatorWidgetData());

            var lines = new List<string>();
            if (crop != null) lines.Add(Label(s, L10n.T("w_growing")) + ItemLink(crop));
            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            int good = el.SucceededProgressTicks, bad = el.FailedProgressTicks;
            string prog = Label(s, L10n.T("w_progress")) + Num(s, done + "/" + total);
            if (bad > 0) prog += " (" + L10n.T("w_cells_split", Num(s, good.ToString()), Num(s, bad.ToString())) + ")";
            lines.Add(prog);

            int mastery = 0, lockv = 1;
            try { mastery = el.ParamsData.MasteryValue; lockv = Mathf.Max(1, el.ParamsData.MasteryLock); }
            catch { }
            bool zombie = w.Worker is ZombieWgoData;
            float cells = mastery >= lockv ? Mathf.Clamp(mastery / lockv, 1, 3) : zombie ? Mathf.Clamp01(mastery / (float)lockv) : 1f;
            if (done >= total) lines.Add(L10n.T("w_finishing"));
            else if (cc.IsWorkPausedByMove()) lines.Add(L10n.T("w_paused_move"));
            else if (cells > 0f)
            {
                float per = WorldProcesses.SecondsPerTick(cc);
                int ticks = Mathf.CeilToInt((total - done) / cells);
                lines.Add(Label(s, L10n.T("w_time_left")) + Clock(s, Mathf.Max(0f, ticks * per - WorldProcesses.TickElapsed(cc)), approx: mastery < lockv && zombie));
            }
            if (mastery < lockv)
                lines.Add(Label(s, LLBase.L("ui_mastery")) + Num(s, mastery + "/" + lockv) + " · " + L10n.T("w_good_chance", Num(s, Mathf.RoundToInt(100f * mastery / lockv) + "%")));
            else if (cells > 1f)
                lines.Add(Label(s, LLBase.L("ui_mastery")) + Num(s, mastery + "/" + lockv) + " · " + L10n.T("w_cells_per_tick", Num(s, ((int)cells).ToString())));

            // Adubo: perks perk_fertilize_* do canteiro (sem duração, WgoData.cs:2490).
            var fert = new List<string>();
            if (w.ActivePerks != null)
                foreach (PerkData p in w.ActivePerks)
                    if (p?.Definition != null && p.Definition.IsFertilizerPerk) fert.Add(Links.Wrap(Links.Perk, p.Definition.id, p.Definition.GetHeader()));
            lines.Add(Label(s, L10n.T("w_fertilizer")) + (fert.Count > 0 ? string.Join(", ", fert) : L10n.T("w_none")));
            list.AddRange(Widgets(lines, s));

            // Colheita: o que já acumulou (gameRes do canteiro) e o máximo se todas as células restantes derem certo.
            if (outs != null && cd != null)
            {
                var harvest = new List<string>();
                foreach (ChanceOutputItem o in outs)
                {
                    ItemDef idef = GameBalance.Me.GetDataOrNull<ItemDef>(o.id);
                    if (idef == null || o.count == null || !o.count.HasExpression) continue;
                    string expr = o.count.ToString();
                    int now = 0, max = 0;
                    bool fromRes = false;
                    foreach (System.Text.RegularExpressions.Match m in ResRx.Matches(expr))
                    {
                        string res = m.Groups[1].Value;
                        fromRes = true;
                        now += w.GetGameResInt(res);
                        max += PotentialRes(cd, res, good + 1, good + Mathf.Max(0, total - done));
                    }
                    if (!fromRes)
                    {
                        if (!int.TryParse(expr, NumberStyles.Integer, CultureInfo.InvariantCulture, out now)) continue;
                        max = 0;
                    }
                    string t = ItemLink(idef) + " " + Num(s, now.ToString());
                    if (max > 0) t += " " + L10n.T("w_up_to", Num(s, (now + max).ToString()));
                    harvest.Add(t);
                }
                if (harvest.Count > 0)
                    AddBlock(list, new List<LazyWidgetDataBase> { Text(Label(s, L10n.T("w_harvest")) + string.Join(", ", harvest), s.Small) });
            }
        }

        private static readonly System.Text.RegularExpressions.Regex ResRx = new System.Text.RegularExpressions.Regex("WGOPar\\(\"([^\"]+)\"\\)");

        /// <summary>Quanto o recurso ainda pode somar nas células from..total se todas forem boas (gameresPerSuccessfulProgress).</summary>
        private static int PotentialRes(CraftDef cd, string res, int from, int total)
        {
            if (cd.gameresPerSuccessfulProgress == null) return 0;
            int sum = 0;
            foreach (GameResPerProgress g in cd.gameresPerSuccessfulProgress)
                if (g != null && g.gameRes != null && g.sucessfulProgressTick >= from && g.sucessfulProgressTick <= total) sum += g.gameRes.GetInt(res);
            return sum;
        }

        // ---------------- renovação (autocraft escondido) ----------------

        /// <summary>
        /// Autocraft isHidden que o jogo não mostra: árvores/arbustos/cogumelos que voltam (grupo spawner, Spawning() troca
        /// o objeto pelo próximo estágio), colmeia, poço, latrina. Spawner pronto espera o jogador se afastar
        /// (WgoData.OnCraftEnd :1136, wgoDelayedSpawnSystemData).
        /// </summary>
        private static void RenewSection(List<LazyWidgetDataBase> list, Styles s, WgoData w, CraftElementBase el)
        {
            CraftComponent cc = w.CraftComponent;
            list.Add(Text(WgoName(w), s.Header));
            list.Add(new UITooltipSeparatorWidgetData());
            var lines = new List<string>();
            List<SpawnStage> stages = w.SpawnWGOComponent?.SpawnStages;
            int stage = stages == null ? -1 : stages.FindIndex(x => x != null && x.craftId == el.CraftId);
            if (stage >= 0 && stages.Count > 1) lines.Add(Label(s, L10n.T("w_stage")) + Num(s, (stage + 1) + "/" + stages.Count));
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
                string n = LLBase.L(rd.replaceWgoId);
                if (n != rd.replaceWgoId && n != WgoName(w)) lines.Add(Label(s, L10n.T("w_becomes")) + n);
            }
            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            bool delayed = MainGame.Instance?.GameSave?.wgoDelayedSpawnSystemData?.Contains(w) ?? false;
            if (delayed) lines.Add(L10n.T("w_wait_away"));
            else if (cc.Status == CraftComponentStatus.ReadyToFinishAutoCraft || (el.IsStarted && done >= total)) lines.Add(L10n.T("w_finishing"));
            else if (el.IsStarted)
                lines.Add(Label(s, L10n.T("w_ready_in")) + Clock(s, AutoSecondsLeft(cc, el)));
            else lines.Add(L10n.T("w_not_started"));
            if (el.IsStarted && total > 0) lines.Add(Label(s, L10n.T("w_progress")) + Num(s, done + "/" + total) + " " + L10n.T("w_ticks_word"));
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

        private void CraftSection(List<LazyWidgetDataBase> list, Styles s, WgoData w)
        {
            CraftComponent cc = w.CraftComponent;
            CraftElementBase el = cc.CurrentCraftElement;
            CraftDefBase def = el.Def;
            list.Add(Text(Links.Wrap(Links.Station, w.id, WgoName(w)), s.Header));
            list.Add(new UITooltipSeparatorWidgetData());

            var lines = new List<string>();
            // O quê: nome do craft (link) e quantidade que sai (pré-rolada no início do craft, CraftElementBase.cs:486).
            string what = CraftLink(def);
            int outCount = OutputCount(el, w);
            if (outCount > 1) what += " " + Num(s, "×" + outCount);
            lines.Add(Label(s, L10n.T(def.isHidden ? "w_renewing" : "w_making")) + what);

            int total = Mathf.Max(0, el.TotalProgressTicks), done = Mathf.Max(0, el.ProgressTicks);
            bool started = el.IsStarted;
            bool auto = def.isAuto;
            IWorker worker = w.Worker;
            var zombie = worker as ZombieWgoData;
            CraftComponentStatus st = cc.Status;

            if (started && total > 0)
                lines.Add(Label(s, L10n.T("w_progress")) + Num(s, done + "/" + total) + " " + L10n.T("w_ticks_word"));

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
            else if (auto)
            {
                float left = AutoSecondsLeft(cc, el);
                time = Label(s, L10n.T("w_time_left")) + Clock(s, left);
            }
            else if (zombie != null)
            {
                float per = ZombieSecondsPerTick(zombie, w, el);
                time = Label(s, L10n.T("w_time_left")) + (per > 0f ? Clock(s, (total - done) * per, approx: true) : L10n.T("w_unknown"));
            }
            else
            {
                state = L10n.T("w_needs_you", Num(s, (total - done).ToString()));
            }
            if (time != null) lines.Add(time);
            if (state != null) lines.Add(state);

            // Fila inteira (só autocraft: o resto depende de golpes).
            List<CraftElementBase> queue = cc.CraftElementsQueue;
            if (auto && queue.Count > 0)
            {
                float all = QueueSecondsLeft(cc, w, el, out bool infinite);
                if (infinite) lines.Add(Label(s, L10n.T("w_queue_time")) + L10n.T("w_infinite"));
                else if (all > 0f && (queue.Count > 1 || el.Count > 1)) lines.Add(Label(s, L10n.T("w_queue_time")) + Clock(s, all));
            }

            // Quem trabalha.
            lines.Add(Label(s, L10n.T("w_worker")) + WorkerText(cc, w, zombie, auto));

            var widgets = Widgets(lines, s);

            // Bloqueio: CraftStatus ≠ OK traduzido.
            CraftStatus cs = el.CraftStatus;
            if (cs != CraftStatus.OK && !ready)
                widgets.Add(Text(L10n.T("w_blocked") + ": " + StatusText(cs), Native.Style("redEffectTextStyle") ?? s.Small));

            AddBlock(list, widgets);

            // Fila: próximos itens (links).
            var next = new List<string>();
            int extra = 0;
            if (el.Count > 1 || el.IsInfinite) next.Add(CraftLink(def) + " " + (el.IsInfinite ? L10n.T("w_repeat") : Num(s, "×" + (el.Count - 1))));
            foreach (CraftElementBase q in queue)
            {
                if (q == null || q == el || q.Def == null || q.Count <= 0) continue;
                if (next.Count >= MaxQueue) { extra++; continue; }
                next.Add(CraftLink(q.Def) + (q.IsInfinite ? " " + L10n.T("w_repeat") : q.Count > 1 ? " " + Num(s, "×" + q.Count) : ""));
            }
            if (next.Count > 0)
            {
                string t = Label(s, L10n.T("w_next")) + string.Join(", ", next) + (extra > 0 ? " +" + extra : "");
                AddBlock(list, new List<LazyWidgetDataBase> { Text(t, s.Small) });
            }

            // Ingredientes que faltam para o próximo craft (widget nativo com possui/precisa).
            if (!started && cs == CraftStatus.NotEnoughResources && def is CraftDef cdef)
            {
                var needs = new UITooltipNeedsItemWidgetData(cdef, cc);
                if (needs.CraftItemCellsData.Count > 0) { list.Add(new UITooltipSeparatorWidgetData()); list.Add(needs); }
            }

            // Combustível guardado na estação (o mesmo número da bolha nativa, Wgo.TryGetFuelContainerStoredItem).
            string fuel = FuelLine(s, w) ?? FuelNeedLine(s, w, def);
            if (fuel != null) AddBlock(list, new List<LazyWidgetDataBase> { Text(fuel, s.Small) });
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

        /// <summary>Atual (com repetições) + elementos seguintes da fila, cada um com duração do balanço + perks (CraftElementBase.Start :482).</summary>
        private static float QueueSecondsLeft(CraftComponent cc, WgoData w, CraftElementBase cur, out bool infinite)
        {
            infinite = false;
            float per = WorldProcesses.SecondsPerTick(cc);
            float sum = 0f;
            if (cur.IsStarted) sum += AutoSecondsLeft(cc, cur);
            foreach (CraftElementBase q in cc.CraftElementsQueue)
            {
                if (q == null || q.Def == null || q.Count <= 0) continue;
                if (q.IsInfinite) { infinite = true; return 0f; }
                int reps = q == cur && cur.IsStarted ? q.Count - 1 : q.Count;
                if (reps <= 0) continue;
                if (!q.Def.isAuto) continue;
                sum += reps * DurationTicks(q, w) * per;
            }
            return sum;
        }

        private static int DurationTicks(CraftElementBase q, WgoData w)
        {
            if (q.Def.duration == null || !q.Def.duration.HasExpression) return 0;
            int add = 0;
            try { add = q.ParamsData != null ? q.ParamsData.PerksCraftAddTotalProgressTicksValue : 0; }
            catch { add = 0; }
            return Mathf.Max(0, q.Def.duration.EvaluateInt(w) + add);
        }

        /// <summary>
        /// Segundos por tick com zumbi (craft comum): golpe a cada 1 s × (1 + modificador da ferramenta), a cada
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
            if (hand != null && !hand.IsEmpty && el.Def is CraftDef cd && cd.zombieSpeedItemModificators != null)
                foreach (GameResAtom a in cd.zombieSpeedItemModificators.List)
                    if (a.type == hand.id) { mod = a.value; break; }
            return sub / ((1f + mod) * cells);
        }

        private static string WorkerText(CraftComponent cc, WgoData w, ZombieWgoData zombie, bool auto)
        {
            if (zombie != null) return L10n.T("w_worker_zombie");
            if (auto) return L10n.T(cc.IsServicedByZombieOperator ? "w_worker_auto_zombie" : "w_worker_auto");
            return L10n.T("w_worker_you");
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
            string name = fd != null ? Links.Wrap(Links.Item, itemId, fd.GetHeader()) : itemId;
            return Label(s, L10n.T("w_fuel")) + name + " " + Num(s, count.ToString());
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
                string label = L10n.T("w_fuel");
                string amount = string.Equals(d.GetHeader(), label, System.StringComparison.OrdinalIgnoreCase)
                    ? Links.Wrap(Links.Item, d.id, Num(s, have.ToString()))
                    : ItemLink(d) + " " + Num(s, have.ToString());
                return Label(s, label) + amount + " · " + L10n.T("w_fuel_crafts", Num(s, (have / per).ToString()));
            }
            return null;
        }

        // ---------------- utilidades ----------------

        /// <summary>
        /// Nome do objeto: texto próprio, sem sufixo numérico, sem "_empty"; senão o primeiro pedaço do id que é um item
        /// (arbustos "bush_blueberry_clr1_l_empty" → Blueberry, "honey_wild_empty" → Honey); senão o id cru.
        /// </summary>
        public static string WgoName(WgoData w)
        {
            string id = w.id;
            foreach (string cand in new[] { id, System.Text.RegularExpressions.Regex.Replace(id, "_\\d+$", ""), id.EndsWith("_empty") ? id.Substring(0, id.Length - 6) : null })
            {
                if (string.IsNullOrEmpty(cand)) continue;
                string n = LLBase.L(cand);
                if (n != cand) return n;
            }
            foreach (string token in id.Split('_'))
            {
                if (token.Length < 3) continue;
                ItemDef d = GameBalance.Me.GetDataOrNull<ItemDef>(token);
                if (d != null) return d.GetHeader();
            }
            return id;
        }

        private static string CraftLink(CraftDefBase def)
        {
            if (def is CraftDef cd && !cd.id.StartsWith("mix")) return Links.Wrap(Links.Craft, cd.id, TooltipContent.CraftName(cd));
            string n = LLBase.L(def.id);
            return n;
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
            foreach (LazyWidgetDataBase d in list)
            {
                if (d is UITooltipTextWidgetData t) sb.Append(t.Text);
                else sb.Append(d.GetType().Name);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
