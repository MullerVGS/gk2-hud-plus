using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Seções extras no estilo do tooltip nativo (mesmos widgets e TextStyles) e o conteúdo completo dos tooltips
    /// aninhados. Só leitura: nada de GetActionDamage, GetTotalQuality, Wgo.GetWidgetData ou avaliar expressões de efeito.
    /// </summary>
    internal sealed class TooltipContent
    {
        private const int MaxListed = 6;
        private const float CacheSeconds = 1f;

        private readonly Index index = new Index();
        private readonly Dictionary<string, KeyValuePair<float, object>> cache = new Dictionary<string, KeyValuePair<float, object>>();

        public Index Index => index;

        // ---------- hover normal: acrescenta ao que o jogo montou ----------

        public void Enrich(List<LazyWidgetDataBase> list, Entity e)
        {
            Styles s = Styles.Read();
            if (s == null || e == null) return;
            switch (e.Kind)
            {
                case EntityKind.Item:
                    ReplaceCraftedAt(list, s, ItemStations(e.Id));
                    AppendBlock(list, ItemLines(e, s));
                    break;
                case EntityKind.Craft:
                {
                    ItemDef res = e.CraftDef.TryGetResultingItemDef();
                    bool hadCraftedAt = ReplaceCraftedAt(list, s, e.CraftDef.craftsIn, res != null ? ItemStations(res.id) : null) || HasCraftedAt(list);
                    InsertBlock(list, CraftLines(e, s, !hadCraftedAt), BlockIndexBeforeNeeds(list));
                    break;
                }
                case EntityKind.Perk:
                    AppendBlock(list, PerkLines(e, s));
                    break;
                case EntityKind.Station:
                    AppendBlock(list, StationLines(e, s, false));
                    break;
            }
        }

        // ---------- tooltip aninhado: conteúdo completo da entidade ----------

        public List<LazyWidgetDataBase> Build(Entity e)
        {
            Styles s = Styles.Read();
            var list = new List<LazyWidgetDataBase>();
            if (s == null || e == null) return list;
            GameBalance gb = GameBalance.Me;
            switch (e.Kind)
            {
                case EntityKind.Item:
                    Native.AddItemWidgets(list, e.ItemDef);
                    ReplaceCraftedAt(list, s, ItemStations(e.Id));
                    AppendBlock(list, ItemLines(e, s));
                    break;
                case EntityKind.Craft:
                {
                    CraftDef cd = e.CraftDef;
                    list.Add(new UITooltipTextWidgetData(CraftName(cd), TextAlignmentOptions.Center, s.Header));
                    list.Add(new UITooltipSeparatorWidgetData());
                    ItemDef res = cd.TryGetResultingItemDef();
                    if (res != null && res.type != ItemType.Preach)
                        Native.AddItemWidgets(list, res, addHeader: false, notShowStudy: true, noCraftedAt: true, variants: cd.GetPossibleResultingItemDefs());
                    else
                        AddDescription(list, cd.id, s.Small);
                    Native.TryAddRequiredExtensionWidget(list, cd);
                    AppendBlock(list, CraftLines(e, s, true));
                    Native.TryAddNeedItemsWidget(list, cd.needItems);
                    break;
                }
                case EntityKind.Perk:
                    Native.AddPerkWidgets(list, e.PerkDef);
                    AppendBlock(list, PerkLines(e, s));
                    break;
                case EntityKind.Station:
                    list.Add(new UITooltipTextWidgetData(LLBase.L(e.Id), TextAlignmentOptions.Center, s.Header));
                    list.Add(new UITooltipSeparatorWidgetData());
                    AddDescription(list, e.Id, s.Description);
                    AppendBlock(list, StationLines(e, s, true));
                    if (gb.buildableWgos.TryGetValue(e.Id, out BuildingDef bd) && bd != null)
                        Native.TryAddNeedItemsWidget(list, bd.needItems);
                    break;
            }
            Tidy(list);
            return list;
        }

        // ---------- seções ----------

        private List<LazyWidgetDataBase> ItemLines(Entity e, Styles s)
        {
            var lines = new List<string>();
            ItemDef def = e.ItemDef;
            if (e.Instance != null && def.hasDurability && e.Instance.TryGetProperty(out DurabilitySerializedItemProperty dur))
            {
                string t = L10n.T("durability") + ": " + Num(s, Mathf.RoundToInt(dur.Durability * 100f) + "%");
                if (def.durDecreaseOnUse > 0f)
                    t += " (" + L10n.T("uses_left", Num(s, Mathf.FloorToInt(dur.Durability / def.durDecreaseOnUse + 0.0001f).ToString())) + ")";
                lines.Add(t);
            }
            KeyValuePair<int, int> c = Counts(def.id);
            string have = L10n.T("you_have") + ": " + Num(s, (c.Key + c.Value).ToString());
            if (c.Value > 0) have += " (" + L10n.T("have_split", c.Key, c.Value) + ")";
            lines.Add(have);
            string used = UsedInLine(def.id, s);
            if (used != null) lines.Add(used);
            return Widgets(lines, s);
        }

        private List<LazyWidgetDataBase> CraftLines(Entity e, Styles s, bool withCraftedAt)
        {
            var lines = new List<string>();
            CraftDef cd = e.CraftDef;
            int ticks = cd.duration != null && cd.duration.HasExpression ? cd.duration.EvaluateInt() : 0;
            if (ticks > 0)
            {
                string t = L10n.T("duration") + ": " + string.Format(L10n.T("ticks"), Num(s, ticks.ToString()));
                if (cd.isAuto && e.Wgo != null)
                {
                    float tick = e.Wgo.AutoCraftTickDuration;
                    if (tick > 0f) t += " (" + "cell_time".FontIcon() + Num(s, PerkSystemData.GetFormattedDuration(tick * ticks)) + ")";
                }
                lines.Add(t);
            }
            List<PerkData> perks = LinkedActivePerks(cd);
            AddPerTick(lines, s, "energy", cd.energyPerTick, perks.Sum(p => p.Definition.energyAdd), ticks);
            AddPerTick(lines, s, "insanity", cd.insanityPerTick, perks.Sum(p => p.Definition.insanityAdd), ticks);
            List<LazyWidgetDataBase> w = Widgets(lines, s);
            // Na janela da própria estação, "Crafted at: <ela mesma>" não diz nada.
            bool onlyHere = e.Wgo != null && cd.craftsIn != null && cd.craftsIn.Count == 1 && cd.craftsIn[0] == e.Wgo.id;
            if (withCraftedAt && !onlyHere && cd.craftsIn != null && cd.craftsIn.Count > 0)
                w.Add(new UITooltipTextWidgetData(CraftedAtText(s, cd.craftsIn), TextAlignmentOptions.Center, s.Small));
            return w;
        }

        private static void AddPerTick(List<string> lines, Styles s, string res, LazyExpression expr, float perkAdd, int ticks)
        {
            if (expr == null || !expr.HasExpression) return;
            float per = expr.EvaluateFloat() + perkAdd;
            if (Mathf.Approximately(per, 0f)) return;
            string t = LLBase.L(res) + ": " + res.FontIcon() + L10n.T("per_tick", Num(s, F(per)));
            if (ticks > 0) t += ", " + L10n.T("total", Num(s, F(per * ticks)));
            lines.Add(t);
        }

        private List<LazyWidgetDataBase> PerkLines(Entity e, Styles s)
        {
            var lines = new List<string>();
            PerkDef p = e.PerkDef;
            PerkData active = MainGame.Instance?.GameSave?.perkSystemData?.activePerks?.Find(x => x != null && x.id == p.id);
            if (p.duration > 0f)
            {
                string t = L10n.T("duration") + ": " + Num(s, PerkSystemData.GetFormattedDuration(p.duration));
                if (active != null && !p.hiddenTimer)
                    t += " (" + L10n.T("left", Num(s, PerkSystemData.GetFormattedDuration(Mathf.Max(0f, active.currentDuration)))) + ")";
                lines.Add(t);
            }
            if (!Mathf.Approximately(p.energyAdd, 0f))
                lines.Add(L10n.T("craft_energy") + ": " + "energy".FontIcon() + L10n.T("per_tick", Num(s, Signed(p.energyAdd))));
            if (!Mathf.Approximately(p.insanityAdd, 0f))
                lines.Add(L10n.T("craft_insanity") + ": " + "insanity".FontIcon() + L10n.T("per_tick", Num(s, Signed(p.insanityAdd))));
            if (p.craftStartTicks != 0)
                lines.Add(L10n.T("head_start") + ": " + string.Format(L10n.T("ticks"), Num(s, Signed(p.craftStartTicks))));
            if (p.craftTotalProgressTicksBonus != 0)
                lines.Add(L10n.T("craft_length") + ": " + string.Format(L10n.T("ticks"), Num(s, Signed(p.craftTotalProgressTicksBonus))));
            if (p.craftMasteryBonus != 0)
                lines.Add(LLBase.L("ui_mastery") + ": " + Num(s, Signed(p.craftMasteryBonus)));
            if (p.tickRate > 0f && p.addGameResPerTick != null && p.addGameResPerTick.List.Count > 0)
            {
                IEnumerable<string> parts = p.addGameResPerTick.List.Select(a => ResIcon(a.type) + Num(s, Signed(a.value)));
                lines.Add(L10n.T("every", PerkSystemData.GetFormattedDuration(p.tickRate)) + ": " + string.Join(" ", parts));
            }
            List<LazyWidgetDataBase> w = Widgets(lines, s);
            List<ItemDef> sources = index.PerkSources(p.id);
            if (sources != null && sources.Count > 0)
            {
                List<string> links = sources.GroupBy(i => i.GetHeader()).Select(g => Links.Wrap(Links.Item, g.First().id, g.Key)).ToList();
                w.Add(new UITooltipTextWidgetData(ListText(s, L10n.T("granted_by"), links), TextAlignmentOptions.Center, s.Small));
            }
            return w;
        }

        private List<LazyWidgetDataBase> StationLines(Entity e, Styles s, bool withExtension)
        {
            var w = new List<LazyWidgetDataBase>();
            GameBalance gb = GameBalance.Me;
            if (gb.craftsInCache.TryGetValue(e.Id, out List<CraftDefBase> crafts) && crafts != null)
            {
                List<string> links = crafts.OfType<CraftDef>().Where(IsListable).Where(Known)
                    .GroupBy(CraftName).Select(g => Links.Wrap(Links.Craft, g.First().id, g.Key)).ToList();
                if (links.Count > 0)
                    w.Add(new UITooltipTextWidgetData(ListText(s, L10n.T("crafts"), links), TextAlignmentOptions.Center, s.Small));
            }
            if (withExtension && gb.IsWorkbenchExtensionId(e.Id) && gb.workbenchParentsByExtensionCache.TryGetValue(e.Id, out List<WGODef> parents) && parents.Count > 0)
            {
                string t = (s.SmallSelected.ApplyStyleToString(LLBase.L("ui_hint_extention_for") + " ") ?? "")
                    + string.Join(", ", parents.Select(p => Links.Wrap(Links.Station, p.id, LLBase.L(p.id))));
                w.Add(new UITooltipTextWidgetData(t, TextAlignmentOptions.Center, s.Small));
            }
            return w;
        }

        private string UsedInLine(string itemId, Styles s)
        {
            List<string> links = Cached("usedin:" + itemId, () =>
            {
                List<CraftDef> all = index.UsedIn(itemId);
                if (all == null) return new List<string>();
                return all.Where(Known).GroupBy(CraftName).Select(g => Links.Wrap(Links.Craft, g.First().id, g.Key)).ToList();
            });
            return links.Count == 0 ? null : ListText(s, L10n.T("used_in"), links);
        }

        // ---------- "Crafted at" com links ----------

        private static List<string> ItemStations(string itemId) =>
            itemId != null && GameBalance.Me.craftInItemsCacheShownInTooltips.TryGetValue(itemId, out List<string> l) ? l : null;

        private static string CraftedAtLabel(Styles s) => s.SmallSelected.ApplyStyleToString(LLBase.L("ui_crafted_at") + ": ") ?? "";

        private static string CraftedAtText(Styles s, List<string> stations) =>
            CraftedAtLabel(s) + string.Join(", ", stations.Select(id => Links.Wrap(Links.Station, id, LLBase.L(id))));

        private static bool HasCraftedAt(List<LazyWidgetDataBase> list)
        {
            string label = LLBase.L("ui_crafted_at");
            return list.Any(d => d is UITooltipTextWidgetData t && t.Text != null && t.Text.Contains(label));
        }

        /// <summary>
        /// Troca o "Crafted at: A, B" nativo por um texto idêntico com cada estação como link. Só troca quando o texto
        /// começa exatamente como o jogo o monta (UITooltip.AddItemWidgets / ShowLinkedEntity); o resto (ex.: "\nRequired
        /// extension") é preservado.
        /// </summary>
        private static bool ReplaceCraftedAt(List<LazyWidgetDataBase> list, Styles s, params List<string>[] candidates)
        {
            string labelText = LLBase.L("ui_crafted_at");
            string label = CraftedAtLabel(s);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!(list[i] is UITooltipTextWidgetData t) || t.Text == null || !t.Text.Contains(labelText) || Links.HasLinks(t.Text)) continue;
                foreach (List<string> cand in candidates)
                {
                    if (cand == null || cand.Count == 0) continue;
                    string expected = label + string.Join(", ", cand.Select(id => LLBase.L(id)));
                    if (!t.Text.StartsWith(expected)) continue;
                    list[i] = new UITooltipTextWidgetData(CraftedAtText(s, cand) + t.Text.Substring(expected.Length), t.TextAlignmentOptions, t.TextStyle);
                    return true;
                }
            }
            return false;
        }

        // ---------- utilidades ----------

        private static string ListText(Styles s, string label, List<string> items)
        {
            string t = (s.SmallSelected.ApplyStyleToString(label + ": ") ?? "") + string.Join(", ", items.Take(MaxListed));
            if (items.Count > MaxListed) t += " +" + (items.Count - MaxListed);
            return t;
        }

        private static List<LazyWidgetDataBase> Widgets(List<string> lines, Styles s) =>
            lines.Select(l => (LazyWidgetDataBase)new UITooltipTextWidgetData(l, TextAlignmentOptions.Center, s.Small)).ToList();

        private static void AddDescription(List<LazyWidgetDataBase> list, string id, TextStyle style)
        {
            string key = id + "_d", text = LLBase.L(key);
            if (text != key) list.Add(new UITooltipTextWidgetData(text, TextAlignmentOptions.Center, style));
        }

        private static void AppendBlock(List<LazyWidgetDataBase> list, List<LazyWidgetDataBase> lines)
        {
            if (lines == null || lines.Count == 0) return;
            if (list.Count > 0 && !(list[list.Count - 1] is UITooltipSeparatorWidgetData)) list.Add(new UITooltipSeparatorWidgetData());
            list.AddRange(lines);
        }

        private static void InsertBlock(List<LazyWidgetDataBase> list, List<LazyWidgetDataBase> lines, int at)
        {
            if (lines == null || lines.Count == 0) return;
            if (at < 0 || at >= list.Count) { AppendBlock(list, lines); return; }
            var block = new List<LazyWidgetDataBase>();
            if (at > 0 && !(list[at - 1] is UITooltipSeparatorWidgetData)) block.Add(new UITooltipSeparatorWidgetData());
            block.AddRange(lines);
            if (!(list[at] is UITooltipSeparatorWidgetData)) block.Add(new UITooltipSeparatorWidgetData());
            list.InsertRange(at, block);
        }

        /// <summary>Posição antes do separador que precede os ingredientes (UITooltipNeedsItemWidgetData), ou -1.</summary>
        private static int BlockIndexBeforeNeeds(List<LazyWidgetDataBase> list)
        {
            int i = list.FindIndex(d => d is UITooltipNeedsItemWidgetData);
            if (i > 0 && list[i - 1] is UITooltipSeparatorWidgetData) i--;
            return i;
        }

        private static void Tidy(List<LazyWidgetDataBase> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i] is UITooltipSeparatorWidgetData && list[i - 1] is UITooltipSeparatorWidgetData) list.RemoveAt(i);
            while (list.Count > 0 && list[list.Count - 1] is UITooltipSeparatorWidgetData) list.RemoveAt(list.Count - 1);
        }

        private KeyValuePair<int, int> Counts(string itemId)
        {
            return Cached("count:" + itemId, () =>
            {
                PlayerData pd = MainGame.PlayerData;
                if (pd == null || pd.inventory?.Data == null) return new KeyValuePair<int, int>(0, 0);
                int bag = pd.inventory.Data.GetTotalCountInInventory(itemId);
                int belt = pd.toolBeltInventory?.Data != null ? pd.toolBeltInventory.Data.GetTotalCountInInventory(itemId) : 0;
                int multi = new MultiInventory(pd).GetTotalCount(itemId);
                return new KeyValuePair<int, int>(bag + belt, Mathf.Max(0, multi - bag));
            });
        }

        private T Cached<T>(string key, System.Func<T> make)
        {
            key = LLBase.CurrentLang + "|" + key;
            float now = Time.unscaledTime;
            if (cache.TryGetValue(key, out KeyValuePair<float, object> hit) && now - hit.Key < CacheSeconds) return (T)hit.Value;
            T v = make();
            if (cache.Count > 512) cache.Clear();
            cache[key] = new KeyValuePair<float, object>(now, v);
            return v;
        }

        private static List<PerkData> LinkedActivePerks(CraftDef cd)
        {
            var res = new List<PerkData>();
            List<PerkData> active = MainGame.Instance?.GameSave?.perkSystemData?.activePerks;
            if (active == null || cd.linkedPerks == null) return res;
            foreach (string id in cd.linkedPerks)
            {
                PerkData p = active.Find(x => x != null && x.id == id);
                if (p?.Definition != null) res.Add(p);
            }
            return res;
        }

        /// <summary>Mesmo filtro de conhecimento do jogo (CraftComponent.AvailableCrafts / IsCraftHiddenByKnowledge).</summary>
        private static bool Known(CraftDef cd)
        {
            KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (ks == null) return true;
            if (ks.blackListCrafts.Contains(cd.id) || ks.IsOneTimeCraftCompleted(cd)) return false;
            return !cd.isNeedsUnlock || ks.unlockedCrafts.Contains(cd.id);
        }

        private static bool IsListable(CraftDef cd) =>
            cd != null && !cd.isHidden && !cd.id.StartsWith("rem_") && !cd.id.StartsWith("set_") && !cd.id.StartsWith("town_building_craft:");

        public static string CraftName(CraftDef cd)
        {
            string n = LLBase.L(cd.id);
            if (n != cd.id) return n;
            ItemDef res = cd.TryGetResultingItemDef();
            return res != null ? res.GetHeader() : cd.id;
        }

        private static string ResIcon(string res)
        {
            GameResIconConfig cfg = GameResDisplayConfig.GetConfigForRes(res, GameResIconType.Common);
            return cfg != null ? cfg.iconName.FontIcon() : res.FontIcon();
        }

        private static string Num(Styles s, string v) => s.Description.ApplyStyleToString(v);

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string Signed(float v) => (v > 0 ? "+" : "") + F(v);
    }
}
