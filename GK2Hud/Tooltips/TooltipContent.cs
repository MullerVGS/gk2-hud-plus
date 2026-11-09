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
        private const float BlockSeconds = 0.5f;

        private readonly Index index = new Index();
        private readonly Dictionary<string, KeyValuePair<float, object>> cache = new Dictionary<string, KeyValuePair<float, object>>();
        // "Crafted at" esperado e com links por lista de estações (listas estáveis do GameBalance, chave por referência).
        private readonly Dictionary<List<string>, string[]> craftedAtCache = new Dictionary<List<string>, string[]>();

        public Index Index => index;

        // Sprites de construção: SpriteAtlas.GetSprite devolve um clone novo a cada chamada; um por estação, destruídos no Dispose.
        private readonly Dictionary<string, Sprite> stationImages = new Dictionary<string, Sprite>();

        /// <summary>
        /// Imagem da construção como a Tech Tree mostra (LinkedEntityWidgetData.cs:146: EasySpritesCollection.GetSprite(
        /// BuildingDef.BuildResultIcon)). Cada variante (I/II/III) tem o seu BuildingDef. Sem BuildingDef ou com o ícone
        /// genérico "i_b_blueprint_placeholder": null (o popup fica sem imagem).
        /// </summary>
        public Sprite StationImage(string wgoId)
        {
            if (string.IsNullOrEmpty(wgoId)) return null;
            if (stationImages.TryGetValue(wgoId, out Sprite cached)) return cached;
            Sprite sprite = null;
            if (GameBalance.Me.buildableWgos.TryGetValue(wgoId, out BuildingDef bd) && bd != null)
            {
                string icon = bd.BuildResultIcon;
                EasySpritesCollection sprites = LazySingletonSO<EasySpritesCollection>.Instance;
                if (icon != "i_b_blueprint_placeholder" && sprites.HasSprite(icon)) sprite = sprites.GetSprite(icon);
            }
            stationImages[wgoId] = sprite;
            return sprite;
        }

        private string stationDescLang;
        private Dictionary<string, int> stationDescCounts;

        /// <summary>
        /// Descrição própria da estação (<c>&lt;wgoId&gt;_d</c>) ou null. Muitas estações não têm a chave: o idioma tem um
        /// alias (LLBase.L, aliases1→aliases2) que leva a <c>simple_workbench_d</c>, "Select an object to craft", o subtítulo
        /// genérico da janela de craft. Só vale a chave que está no dicionário do idioma por ela mesma (sem alias) e cujo
        /// texto não se repete em outra construção.
        /// </summary>
        private string StationDescription(string wgoId)
        {
            string key = wgoId + "_d";
            if (!OwnLocKey(key)) return null;
            string text = LLBase.L(key);
            if (stationDescCounts == null || stationDescLang != LLBase.CurrentLang)
            {
                stationDescLang = LLBase.CurrentLang;
                stationDescCounts = new Dictionary<string, int>();
                foreach (string id in GameBalance.Me.buildableWgos.Keys)
                {
                    string k = id + "_d", t = LLBase.L(k);
                    if (t == k) continue;
                    stationDescCounts.TryGetValue(t, out int n);
                    stationDescCounts[t] = n + 1;
                }
            }
            return stationDescCounts.TryGetValue(text, out int count) && count > 1 ? null : text;
        }

        private static readonly System.Reflection.FieldInfo CurrentLangField = HarmonyLib.AccessTools.Field(typeof(LLBase), "currentLang");

        /// <summary>A chave existe no dicionário do idioma atual por ela mesma (não por alias).</summary>
        private static bool OwnLocKey(string key)
        {
            object lang = CurrentLangField?.GetValue(null);
            if (lang == null) return false;
            var dict = HarmonyLib.AccessTools.Field(lang.GetType(), "dictionary")?.GetValue(lang) as System.Collections.IDictionary;
            return dict != null && dict.Contains(key);
        }

        public void Dispose()
        {
            foreach (Sprite sp in stationImages.Values)
                if (sp != null) Object.Destroy(sp);
            stationImages.Clear();
        }

        // ---------- hover normal: acrescenta ao que o jogo montou ----------

        public void Enrich(List<LazyWidgetDataBase> list, Entity e)
        {
            Styles s = Styles.Read();
            if (s == null || e == null) return;
            switch (e.Kind)
            {
                case EntityKind.Item:
                    ReplaceCraftedAt(list, s, ItemStations(e.Id));
                    AppendBlock(list, Block(BlockKey(e), () => ItemLines(e, s)));
                    break;
                case EntityKind.Craft:
                {
                    bool hadCraftedAt = HasCraftedAt(list, s);
                    if (hadCraftedAt)
                    {
                        ItemDef res = e.CraftDef.TryGetResultingItemDef();
                        ReplaceCraftedAt(list, s, e.CraftDef.craftsIn, res != null ? ItemStations(res.id) : null);
                    }
                    InsertBlock(list, Block(BlockKey(e) + "|" + hadCraftedAt, () => CraftLines(e, s, !hadCraftedAt)), BlockIndexBeforeNeeds(list));
                    break;
                }
                case EntityKind.Perk:
                    AppendBlock(list, Block(BlockKey(e), () => PerkLines(e, s)));
                    break;
                case EntityKind.Station:
                    AppendBlock(list, Block(BlockKey(e), () => StationLines(e, s, false)));
                    break;
            }
        }

        /// <summary>
        /// Chave do bloco pronto: tipo, id e o que muda a leitura (durabilidade da instância, estação/worker, tempo
        /// restante da perk). O idioma entra no Cached. O ShowItemCell roda a cada Draw da célula: o bloco vem daqui.
        /// </summary>
        private static string BlockKey(Entity e)
        {
            switch (e.Kind)
            {
                case EntityKind.Item:
                    int dur = -1;
                    if (e.Instance != null && e.ItemDef.hasDurability && e.Instance.TryGetProperty(out DurabilitySerializedItemProperty p))
                        dur = Mathf.RoundToInt(p.Durability * 1000f);
                    return "item:" + e.Id + "|" + dur;
                case EntityKind.Craft:
                    return "craft:" + e.Id + "|" + (e.Wgo != null ? e.Wgo.UniqueId.ToString() : "-");
                case EntityKind.Perk:
                    PerkData active = MainGame.Instance?.GameSave?.perkSystemData?.activePerks?.Find(x => x != null && x.id == e.Id);
                    return "perk:" + e.Id + "|" + (active != null ? (int)active.currentDuration : -1);
                default:
                    return e.Kind + ":" + e.Id;
            }
        }

        private List<LazyWidgetDataBase> Block(string key, System.Func<List<LazyWidgetDataBase>> make) => Cached("block:" + key, make, BlockSeconds);

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
                    string desc = StationDescription(e.Id);
                    if (desc != null) list.Add(new UITooltipTextWidgetData(desc, TextAlignmentOptions.Center, s.Description));
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
            // Mesmo cálculo da janela de craft (UIBaseCraftSelectionWindow(Data)): worker real da estação (zumbi ou
            // jogador), perks ligadas ao craft dele e ferramenta que ele usaria. Sem estação (aninhado): o jogador, sem ferramenta.
            IWorker worker = e.Wgo?.Worker ?? MainGame.PlayerController;
            int ticks = cd.duration != null && cd.duration.HasExpression ? cd.duration.EvaluateInt() : 0;
            if (ticks > 0 && worker != null) ticks = Mathf.Max(0, ticks + worker.GetPerksCraftAddTotalProgressTicksValue(cd));
            Item tool = e.Wgo != null && worker != null ? worker.GetToolForWorkOnCraft(e.Wgo, cd) : null;
            bool hasTool = tool != null && !tool.IsEmpty && tool.Definition != null;
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
            float energyAdd = (worker?.GetPerksEnergyBonusValue(cd) ?? 0f) - (hasTool ? tool.Definition.GetGameResOnUse("energy") : 0f);
            float insanityAdd = (worker?.GetPerksInsanityBonusValue(cd) ?? 0f) - (hasTool ? tool.Definition.GetGameResOnUse("insanity") : 0f);
            AddPerTick(lines, s, "energy", cd.energyPerTick, energyAdd, ticks);
            AddPerTick(lines, s, "insanity", cd.insanityPerTick, insanityAdd, ticks);
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
            float baseValue = expr.EvaluateFloat();
            float per = baseValue + perkAdd;
            if (Mathf.Approximately(baseValue, 0f) && Mathf.Approximately(per, 0f)) return;   // perk zerando o custo ainda aparece (0), como na janela
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
                List<string> links = crafts.OfType<CraftDef>().Where(IsListable).Where(Known).Where(c => CraftNameOrNull(c) != null)
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
                return all.Where(Known).Where(c => CraftNameOrNull(c) != null).GroupBy(CraftName).Select(g => Links.Wrap(Links.Craft, g.First().id, g.Key)).ToList();
            });
            return links.Count == 0 ? null : ListText(s, L10n.T("used_in"), links);
        }

        // ---------- "Crafted at" com links ----------

        private static List<string> ItemStations(string itemId) =>
            itemId != null && GameBalance.Me.craftInItemsCacheShownInTooltips.TryGetValue(itemId, out List<string> l) ? l : null;

        private static string CraftedAtText(Styles s, List<string> stations) =>
            s.CraftedAtLabel + string.Join(", ", stations.Select(id => Links.Wrap(Links.Station, id, LLBase.L(id))));

        private static bool HasCraftedAt(List<LazyWidgetDataBase> list, Styles s)
        {
            foreach (LazyWidgetDataBase d in list)
                if (d is UITooltipTextWidgetData t && t.Text != null && t.Text.Contains(s.CraftedAtText)) return true;
            return false;
        }

        /// <summary>
        /// Troca o "Crafted at: A, B" nativo por um texto idêntico com cada estação como link. Só troca quando o texto
        /// começa exatamente como o jogo o monta (UITooltip.AddItemWidgets / ShowLinkedEntity); o resto (ex.: "\nRequired
        /// extension") é preservado. Barato quando não há "Crafted at" (só um Contains por widget de texto).
        /// </summary>
        private bool ReplaceCraftedAt(List<LazyWidgetDataBase> list, Styles s, params List<string>[] candidates)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!(list[i] is UITooltipTextWidgetData t) || t.Text == null || !t.Text.Contains(s.CraftedAtText) || Links.HasLinks(t.Text)) continue;
                foreach (List<string> cand in candidates)
                {
                    if (cand == null || cand.Count == 0) continue;
                    if (!craftedAtCache.TryGetValue(cand, out string[] pair) || pair[0] != s.Lang)
                    {
                        pair = new[] { s.Lang, s.CraftedAtLabel + string.Join(", ", cand.Select(id => LLBase.L(id))), CraftedAtText(s, cand) };
                        craftedAtCache[cand] = pair;
                    }
                    if (!t.Text.StartsWith(pair[1])) continue;
                    list[i] = new UITooltipTextWidgetData(pair[2] + t.Text.Substring(pair[1].Length), t.TextAlignmentOptions, t.TextStyle);
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

        private T Cached<T>(string key, System.Func<T> make, float ttl = CacheSeconds)
        {
            key = LLBase.CurrentLang + "|" + key;
            float now = Time.unscaledTime;
            if (cache.TryGetValue(key, out KeyValuePair<float, object> hit) && now - hit.Key < ttl) return (T)hit.Value;
            T v = make();
            if (cache.Count > 512) cache.Clear();
            cache[key] = new KeyValuePair<float, object>(now, v);
            return v;
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
            cd != null && !cd.isHidden && !cd.doNotShowInTooltips && !cd.id.StartsWith("rem_") && !cd.id.StartsWith("set_") && !cd.id.StartsWith("town_building_craft:");

        public static string CraftName(CraftDef cd) => CraftNameOrNull(cd) ?? cd.id;

        /// <summary>Nome do craft (texto próprio ou o do item resultante); null quando o jogo não tem nome para ele.</summary>
        private static string CraftNameOrNull(CraftDef cd)
        {
            string n = LLBase.L(cd.id);
            if (n != cd.id) return n;
            ItemDef res = cd.TryGetResultingItemDef();
            return res != null ? res.GetHeader() : null;
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
