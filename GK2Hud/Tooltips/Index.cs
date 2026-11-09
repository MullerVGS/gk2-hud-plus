using System.Collections.Generic;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Índices reversos que o jogo não tem: item → crafts que o consomem; perk → itens que a dão.
    /// Montados uma vez, preguiçosos (só depois do GameBalance carregado). Leitura pura do balanço.
    /// </summary>
    internal sealed class Index
    {
        private Dictionary<string, List<CraftDef>> usedIn;
        private Dictionary<string, List<ItemDef>> perkSources;

        public bool EnsureBuilt()
        {
            if (usedIn != null) return true;
            GameBalance gb = GameBalance.Me;
            if (gb == null || gb.craftDefs == null || gb.craftDefs.Count == 0 || gb.itemDefs == null) return false;

            var used = new Dictionary<string, List<CraftDef>>();
            foreach (CraftDef cd in gb.craftDefs)
            {
                if (cd == null || cd.isHidden || cd.doNotShowInTooltips || string.IsNullOrEmpty(cd.id)) continue;
                if (cd.id.StartsWith("rem_") || cd.id.StartsWith("set_") || cd.id.StartsWith("town_building_craft:")) continue;
                foreach (NeedItemData need in cd.needItems)
                {
                    foreach (string itemId in Expand(gb, need)) Add(used, itemId, cd);
                }
            }

            var sources = new Dictionary<string, List<ItemDef>>();
            foreach (ItemDef item in gb.itemDefs)
            {
                if (item == null) continue;
                List<PerkDef> onUse = item.GetPerksOnUse();
                if (onUse != null)
                {
                    foreach (PerkDef p in onUse)
                        if (p != null) Add(sources, p.id, item);
                }
                if (!string.IsNullOrEmpty(item.bodyLinkedPerk)) Add(sources, item.bodyLinkedPerk, item);
            }

            usedIn = used;
            perkSources = sources;
            Plugin.Log.LogInfo($"índices: {usedIn.Count} itens usados em crafts, {perkSources.Count} perks com origem em item");
            return true;
        }

        public List<CraftDef> UsedIn(string itemId)
        {
            if (!EnsureBuilt() || itemId == null) return null;
            return usedIn.TryGetValue(itemId, out List<CraftDef> l) ? l : null;
        }

        public List<ItemDef> PerkSources(string perkId)
        {
            if (!EnsureBuilt() || perkId == null) return null;
            return perkSources.TryGetValue(perkId, out List<ItemDef> l) ? l : null;
        }

        private static IEnumerable<string> Expand(GameBalance gb, NeedItemData need)
        {
            if (need == null || string.IsNullOrEmpty(need.id)) yield break;
            List<ItemDef> group = null;
            switch (need.groupType)
            {
                case ItemGroup.None:
                    yield return need.id;
                    yield break;
                case ItemGroup.Common:
                    gb.groupItemsCache.TryGetValue(need.id, out group);
                    break;
                case ItemGroup.Star:
                    gb.starGroupItemsCache.TryGetValue(need.id, out group);
                    break;
            }
            if (group == null) yield break;
            foreach (ItemDef d in group)
                if (d != null) yield return d.id;
        }

        private static void Add<T>(Dictionary<string, List<T>> dict, string key, T value) where T : class
        {
            if (string.IsNullOrEmpty(key)) return;
            if (!dict.TryGetValue(key, out List<T> list)) dict[key] = list = new List<T>();
            if (!list.Contains(value)) list.Add(value);
        }
    }
}
