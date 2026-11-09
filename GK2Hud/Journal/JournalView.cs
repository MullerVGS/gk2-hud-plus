using System.Collections.Generic;
using System.Linq;
using GK2Hud.Calendar;
using GK2Hud.Tooltips;
using LazyBearTechnology;
using TMPro;
using UnityEngine.UI;

namespace GK2Hud.Journal
{
    /// <summary>Uma linha pronta para mostrar (quests da mesma cadeia com o mesmo título viram uma só).</summary>
    internal sealed class JournalLine
    {
        public string Kind;
        public string Id;
        public string Name;
        public float Tod;
        public bool Approx;
    }

    /// <summary>Nomes, agrupamento e tooltips das linhas do diário; a linha "desbloqueado no dia N" dos tooltips.</summary>
    internal static class JournalView
    {
        public static string KindLabel(string kind) => CalendarL10n.T("jk_" + kind);

        /// <summary>Nome no idioma atual; null quando o jogo não tem nome para mostrar (não entra no diário).</summary>
        public static string Name(string kind, string id)
        {
            switch (kind)
            {
                case JournalKinds.Craft:
                {
                    CraftDef cd = GameBalance.Me.GetDataOrNull<CraftDef>(id);
                    if (cd == null || id.StartsWith("mix")) return null;
                    string n = TooltipContent.CraftName(cd);
                    return n == cd.id ? null : n;
                }
                case JournalKinds.DayIcon:
                {
                    List<string> titles = CalendarModel.Defs.Where(e => e.MapIcons.Contains(id)).Select(e => e.Title).ToList();
                    return titles.Count == 0 ? null : string.Join(", ", titles);
                }
                default:
                {
                    string n = LLBase.L(id);
                    return string.IsNullOrEmpty(n) || n == id ? null : n;
                }
            }
        }

        /// <summary>Linhas de um dia em ordem de tipo e hora; quests com o mesmo título juntas.</summary>
        public static List<JournalLine> Lines(IEnumerable<JournalEntry> entries)
        {
            var list = new List<JournalLine>();
            foreach (JournalEntry e in entries.OrderBy(x => System.Array.IndexOf(JournalKinds.Order, x.k)).ThenBy(x => x.t))
            {
                string n = Name(e.k, e.id);
                if (n == null) continue;
                if (e.k == JournalKinds.Quest && list.Any(l => l.Kind == e.k && l.Name == n)) continue;
                list.Add(new JournalLine { Kind = e.k, Id = e.id, Name = n, Tod = e.t, Approx = e.a });
            }
            return list;
        }

        /// <summary>Resumo curto para a lista da esquerda: "Oiled wood · Cook · +4".</summary>
        public static string Summary(List<JournalLine> lines, int max = 2)
        {
            if (lines.Count == 0) return CalendarL10n.T("nothing_short");
            string s = string.Join(" · ", lines.Take(max).Select(l => l.Name));
            return lines.Count > max ? s + " · +" + (lines.Count - max) : s;
        }

        /// <summary>Conteúdo do tooltip de uma linha: o tooltip completo do GK2Hud quando a coisa tem um.</summary>
        public static List<LazyWidgetDataBase> Tooltip(JournalLine l)
        {
            Styles s = Styles.Read();
            if (s == null) return null;
            Entity e = EntityFor(l.Kind, l.Id);
            if (e != null && TooltipFeature.Current != null)
            {
                List<LazyWidgetDataBase> full = TooltipFeature.Current.Content.Build(e);
                if (full != null && full.Count > 0) return full;
            }
            var list = new List<LazyWidgetDataBase> { new UITooltipTextWidgetData(l.Name, TextAlignmentOptions.Center, s.Header), new UITooltipSeparatorWidgetData() };
            string desc = Description(l.Kind, l.Id);
            if (desc != null) list.Add(new UITooltipTextWidgetData(desc, TextAlignmentOptions.Center, s.Description));
            if (l.Kind == JournalKinds.Tech)
            {
                // O que veio junto com a tech (mesmo momento): receitas e construções como links (fixe para abrir).
                JournalTracker j = JournalTracker.Current;
                JournalEntry me = j?.Find(l.Kind, l.Id, out _);
                if (me != null)
                {
                    List<string> with = j.Entries.Where(x => x.d == me.d && System.Math.Abs(x.t - me.t) < 0.0001f && (x.k == JournalKinds.Craft || x.k == JournalKinds.Building))
                        .Select(x => LinkFor(x.k, x.id)).Where(x => x != null).Distinct().ToList();
                    if (with.Count > 0)
                    {
                        if (!(list[list.Count - 1] is UITooltipSeparatorWidgetData)) list.Add(new UITooltipSeparatorWidgetData());
                        list.Add(new UITooltipTextWidgetData(s.SmallSelected.ApplyStyleToString(CalendarL10n.T("j_came_with") + ":") + " " + string.Join(", ", with),
                            TextAlignmentOptions.Center, s.Small));
                    }
                }
            }
            if (!(list[list.Count - 1] is UITooltipSeparatorWidgetData)) list.Add(new UITooltipSeparatorWidgetData());
            list.Add(new UITooltipTextWidgetData(KindLabel(l.Kind) + " · " + When(l.Kind, l.Id), TextAlignmentOptions.Center, s.Small));
            return list;
        }

        private static string LinkFor(string kind, string id)
        {
            string n = Name(kind, id);
            if (n == null) return null;
            if (kind == JournalKinds.Craft) return Links.Wrap(Links.Craft, id, n);
            if (kind == JournalKinds.Building)
            {
                string wgo = WgoOfBuilding(id);
                return wgo != null ? Links.Wrap(Links.Station, wgo, n) : n;
            }
            return n;
        }

        private static string Description(string kind, string id)
        {
            if (kind == JournalKinds.Quest)
            {
                QuestData q = MainGame.Instance?.GameSave?.questSystemData?.questCollection?.quests?.Find(x => x != null && x.id == id);
                string d = q?.Description;
                return string.IsNullOrEmpty(d) ? null : d;
            }
            if (kind == JournalKinds.DayIcon)
            {
                EventDef ev = CalendarModel.Defs.FirstOrDefault(e => e.MapIcons.Contains(id));
                return ev?.Description;
            }
            string key = id + "_d";
            string s = LLBase.L(key);
            return string.IsNullOrEmpty(s) || s == key ? null : s;
        }

        public static Entity EntityFor(string kind, string id)
        {
            GameBalance gb = GameBalance.Me;
            switch (kind)
            {
                case JournalKinds.Craft: return id.StartsWith("mix") ? null : Entity.ForCraft(gb.GetDataOrNull<CraftDef>(id));
                case JournalKinds.Perk: return Entity.ForPerk(gb.GetDataOrNull<PerkDef>(id));
                case JournalKinds.Building:
                {
                    string wgo = WgoOfBuilding(id);
                    return wgo != null ? Entity.ForStation(wgo) : null;
                }
            }
            return null;
        }

        /// <summary>
        /// Construção desbloqueada (BuildingDef.id) → estação no mundo. O desbloqueio costuma ser o canteiro de obra
        /// (<c>..._place_p</c>, wgoId <c>..._place</c>) que vira a estação de <c>customWgoPlacePreview</c>.
        /// </summary>
        public static string WgoOfBuilding(string buildingId)
        {
            BuildingDef bd = GameBalance.Me.GetDataOrNull<BuildingDef>(buildingId);
            if (bd == null) return null;
            if (!string.IsNullOrEmpty(bd.customWgoPlacePreview)) return bd.customWgoPlacePreview;
            return string.IsNullOrEmpty(bd.wgoId) ? null : bd.wgoId;
        }

        private static Dictionary<string, List<string>> buildingsByWgo;

        /// <summary>Estação → construções que a põem no mundo (a própria e o canteiro).</summary>
        private static List<string> BuildingsOfWgo(string wgoId)
        {
            if (buildingsByWgo == null)
            {
                buildingsByWgo = new Dictionary<string, List<string>>();
                foreach (BuildingDef bd in GameBalance.Me.buildableWgos.Values)
                {
                    if (bd == null) continue;
                    string w = WgoOfBuilding(bd.id);
                    if (w == null) continue;
                    if (!buildingsByWgo.TryGetValue(w, out List<string> l)) buildingsByWgo[w] = l = new List<string>();
                    if (!l.Contains(bd.id)) l.Add(bd.id);
                }
            }
            return buildingsByWgo.TryGetValue(wgoId, out List<string> r) ? r : null;
        }

        /// <summary>"dia 42 (sigilo)" ou "antes do dia 66"; null se o diário não sabe.</summary>
        public static string When(string kind, string id)
        {
            JournalTracker j = JournalTracker.Current;
            if (j == null || !j.Active) return null;
            JournalEntry e = j.Find(kind, id, out bool baseline);
            if (e != null) return CalendarL10n.T("j_day", e.d, CalendarModel.DayIdOf(e.d)?.FontIcon() ?? "");
            return baseline ? CalendarL10n.T("j_before", j.StartDay) : null;
        }

        // ---------------- linhas para os tooltips do GK2Hud ----------------

        private static int itemIndexVersion = -1;
        private static readonly Dictionary<string, JournalEntry> recipeByItem = new Dictionary<string, JournalEntry>();
        private static readonly HashSet<string> recipeBaselineItems = new HashSet<string>();

        /// <summary>Item: quando a primeira receita que o produz foi liberada.</summary>
        public static string ItemLine(string itemId)
        {
            JournalTracker j = JournalTracker.Current;
            if (j == null || !j.Active) return null;
            if (itemIndexVersion != j.Version) BuildItemIndex(j);
            if (recipeBaselineItems.Contains(itemId)) return CalendarL10n.T("j_recipe") + ": " + CalendarL10n.T("j_before", j.StartDay);
            if (recipeByItem.TryGetValue(itemId, out JournalEntry e))
                return CalendarL10n.T("j_recipe") + ": " + CalendarL10n.T("j_day", e.d, CalendarModel.DayIdOf(e.d)?.FontIcon() ?? "");
            return null;
        }

        private static readonly Dictionary<string, string> resultOfCraft = new Dictionary<string, string>();

        /// <summary>Item que a receita produz, cacheado: TryGetResultingItemDef loga aviso nas receitas sem item.</summary>
        private static string ResultOf(CraftDef cd)
        {
            if (cd == null) return null;
            if (!resultOfCraft.TryGetValue(cd.id, out string r)) resultOfCraft[cd.id] = r = cd.TryGetResultingItemDef()?.id;
            return r;
        }

        private static void BuildItemIndex(JournalTracker j)
        {
            itemIndexVersion = j.Version;
            recipeByItem.Clear();
            recipeBaselineItems.Clear();
            GameBalance gb = GameBalance.Me;
            foreach (JournalEntry e in j.Entries.Where(x => x.k == JournalKinds.Craft).OrderBy(x => x.When))
            {
                string item = ResultOf(gb.GetDataOrNull<CraftDef>(e.id));
                if (item != null && !recipeByItem.ContainsKey(item)) recipeByItem[item] = e;
            }
            foreach (CraftDef cd in BaselineCrafts(j))
            {
                string item = ResultOf(cd);
                if (item != null) recipeBaselineItems.Add(item);
            }
        }

        private static IEnumerable<CraftDef> BaselineCrafts(JournalTracker j)
        {
            KnowledgeSystem ks = MainGame.Instance?.GameSave?.knowledgeSystem;
            if (ks?.unlockedCrafts == null) yield break;
            foreach (string id in ks.unlockedCrafts)
                if (j.InBaseline(JournalKinds.Craft, id))
                {
                    CraftDef cd = GameBalance.Me.GetDataOrNull<CraftDef>(id);
                    if (cd != null) yield return cd;
                }
        }

        public static string CraftLine(string craftId) => Line("j_unlocked", JournalKinds.Craft, craftId);

        public static string PerkLine(string perkId) => Line("j_gained", JournalKinds.Perk, perkId);

        public static string StationLine(string wgoId)
        {
            List<string> ids = BuildingsOfWgo(wgoId);
            if (ids == null) return null;
            foreach (string id in ids)
            {
                string l = Line("j_unlocked", JournalKinds.Building, id);
                if (l != null) return l;
            }
            return null;
        }

        private static string Line(string label, string kind, string id)
        {
            string w = When(kind, id);
            return w == null ? null : CalendarL10n.T(label) + ": " + w;
        }
    }
}
