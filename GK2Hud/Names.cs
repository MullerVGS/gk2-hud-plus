using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LazyBearTechnology;

namespace GK2Hud
{
    /// <summary>
    /// Nomes mostráveis. <c>LLBase.L</c> devolve a própria chave quando falta o texto e, num alias sem texto, o id do alvo;
    /// tudo que sai daqui passou por <see cref="IsDevId"/>, e null quer dizer "o jogo não tem nome para isso" (o chamador
    /// omite a linha em vez de mostrar o id).
    /// </summary>
    internal static class Names
    {
        private static readonly Regex DevLike = new Regex(@"^[a-z0-9_:.\-]+$");
        private static readonly Regex Noise = new Regex(@"^(clr\d+|xxs|xs|s|m|l|xl|v\d+|[ab]\d+|\d+|onetime|empty|autospawn|dev)$");

        /// <summary>Texto que é um id de dev: só minúsculas/dígitos/_/:, com _ ou :, sem espaço. Sem letra não é id (um relógio "8:56").</summary>
        public static bool IsDevId(string s) => !string.IsNullOrEmpty(s) && DevLike.IsMatch(s) && (s.IndexOf('_') >= 0 || s.IndexOf(':') >= 0) && s.Any(char.IsLetter);

        /// <summary>Texto do jogo para a chave, ou null quando não existe (chave devolvida, alias quebrado, vazio).</summary>
        public static string Text(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            string t = LLBase.L(key);
            return string.IsNullOrEmpty(t) || t == key || IsDevId(t) ? null : t;
        }

        private static readonly Regex Tags = new Regex("<[^>]+>");

        /// <summary>
        /// Rede de segurança do tooltip montado: tira widgets de texto que são só um id (mesmo que venham do jogo) e
        /// separadores sobrando (dois seguidos, no começo ou no fim).
        /// </summary>
        public static void Scrub(List<LazyWidgetDataBase> list)
        {
            if (list == null) return;
            list.RemoveAll(d => d is UITooltipTextWidgetData t && IsDevId(Tags.Replace(t.Text ?? "", "").Trim()));
            for (int i = list.Count - 1; i > 0; i--)
                if (list[i] is UITooltipSeparatorWidgetData && list[i - 1] is UITooltipSeparatorWidgetData) list.RemoveAt(i);
            while (list.Count > 0 && list[0] is UITooltipSeparatorWidgetData) list.RemoveAt(0);
            while (list.Count > 0 && list[list.Count - 1] is UITooltipSeparatorWidgetData) list.RemoveAt(list.Count - 1);
        }

        /// <summary>Texto visível (sem tags) é um id: serve para filtrar itens de lista já embrulhados em link.</summary>
        public static bool IsDevText(string s) => IsDevId(Tags.Replace(s ?? "", "").Trim());

        /// <summary>
        /// <c>LLBase.L</c> cru, só para reproduzir um texto nativo exatamente como o jogo o monta (ex.: casar o "Crafted at"
        /// nativo antes de trocá-lo); nunca para mostrar. É o único caminho para LLBase.L com chave variável fora daqui.
        /// </summary>
        public static string NativeAsIs(string key) => LLBase.L(key);

        /// <summary>Junta nomes sem nulos nem repetidos.</summary>
        public static string Join(IEnumerable<string> names, string sep = ", ") =>
            string.Join(sep, names.Where(n => !string.IsNullOrEmpty(n)).Distinct());

        /// <summary>
        /// Nome de um objeto do mundo (WGODef.id). Ordem: texto próprio (sem sufixo numérico, sem "_empty"); nome da
        /// construção que o põe no mundo; glossário do mod por espécie (árvores, tocos, pedras: o jogo não tem nome para
        /// eles porque nunca os mostra); primeiro pedaço do id que é um item ("bush_blueberry_..." → Blueberry); tipo
        /// genérico do glossário; null.
        /// </summary>
        public static string Wgo(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            string n = Text(id) ?? Text(Regex.Replace(id, "_\\d+$", "")) ?? (id.EndsWith("_empty") ? Text(id.Substring(0, id.Length - 6)) : null);
            if (n != null) return n;
            n = BuildingName(id);
            if (n != null) return n;
            List<string> tokens = id.Split('_').Where(t => t.Length > 0 && !Noise.IsMatch(t)).ToList();
            for (int len = tokens.Count; len >= 2; len--)
                if (Glossary(string.Join("_", tokens.Take(len)), out n)) return n;
            foreach (string token in tokens)
            {
                if (token.Length < 3) continue;
                string h = GameBalance.Me.GetDataOrNull<ItemDef>(token)?.GetHeader();
                if (!string.IsNullOrEmpty(h) && h != token && !IsDevId(h)) return h;
            }
            if (tokens.Count > 0 && Glossary(tokens[0], out n)) return n;
            return null;
        }

        private static Dictionary<string, string> buildingByWgo;

        /// <summary>Nome da construção cujo objeto no mundo é este (melhorias como bed_upgrade só têm texto na construção).</summary>
        private static string BuildingName(string wgoId)
        {
            GameBalance gb = GameBalance.Me;
            if (buildingByWgo == null)
            {
                var map = new Dictionary<string, string>();
                foreach (BuildingDef bd in gb.buildingDefs)
                {
                    if (bd == null || Text(bd.id) == null) continue;
                    foreach (string w in new[] { bd.customWgoPlacePreview, bd.wgoId })
                        if (!string.IsNullOrEmpty(w) && !map.ContainsKey(w)) map[w] = bd.id;
                }
                buildingByWgo = map;
            }
            return buildingByWgo.TryGetValue(wgoId, out string b) ? Text(b) : null;
        }

        private static bool Glossary(string key, out string text)
        {
            Dictionary<string, string> table = LLBase.CurrentLang == "pt-br" ? PtBr : En;
            return table.TryGetValue(key, out text) || En.TryGetValue(key, out text);
        }

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["tree"] = "Tree",
            ["tree_linden"] = "Linden tree",
            ["tree_fir"] = "Fir tree",
            ["tree_birch"] = "Birch tree",
            ["tree_pine"] = "Pine tree",
            ["tree_apple"] = "Apple tree",
            ["tree_dead"] = "Dead tree",
            ["tree_driftwood"] = "Driftwood",
            ["tree_creepy"] = "Creepy tree",
            ["tree_young"] = "Young tree",
            ["tree_common"] = "Tree",
            ["common_tree"] = "Tree",
            ["big_tree"] = "Big tree",
            ["stump"] = "Stump",
            ["stump_linden"] = "Linden stump",
            ["stump_fir"] = "Fir stump",
            ["stump_birch"] = "Birch stump",
            ["bush"] = "Bush",
            ["bush_ordinary"] = "Bush",
            ["bush_dry"] = "Dry bush",
            ["bush_dry_ivy"] = "Dry ivy",
            ["bush_rhodod"] = "Rhododendron",
            ["bush_blueberry"] = "Blueberry bush",
            ["bush_ashberry"] = "Ashberry bush",
            ["bush_barberry"] = "Barberry bush",
            ["common_stones"] = "Rocks",
            ["common_ores"] = "Ore vein",
            ["copper_vein"] = "Copper vein",
            ["broken_barrel"] = "Broken barrel",
            ["broken_trash"] = "Rubble",
            ["broken_shelf"] = "Broken shelf",
            ["pot_trash"] = "Broken pots",
            ["crates_small"] = "Crates",
            ["crates_big"] = "Crates",
            ["fishing"] = "Fishing spot",
            ["fishing_place"] = "Fishing spot",
            ["mushroom"] = "Mushroom",
            ["loot_bag"] = "Loot bag",
            ["looters_chest"] = "Looters' chest",
            ["looters_chestbooks"] = "Looters' chest",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["tree"] = "Árvore",
            ["tree_linden"] = "Tília",
            ["tree_fir"] = "Abeto",
            ["tree_birch"] = "Bétula",
            ["tree_pine"] = "Pinheiro",
            ["tree_apple"] = "Macieira",
            ["tree_dead"] = "Árvore morta",
            ["tree_driftwood"] = "Madeira à deriva",
            ["tree_creepy"] = "Árvore sinistra",
            ["tree_young"] = "Árvore jovem",
            ["tree_common"] = "Árvore",
            ["common_tree"] = "Árvore",
            ["big_tree"] = "Árvore grande",
            ["stump"] = "Toco",
            ["stump_linden"] = "Toco de tília",
            ["stump_fir"] = "Toco de abeto",
            ["stump_birch"] = "Toco de bétula",
            ["bush"] = "Arbusto",
            ["bush_ordinary"] = "Arbusto",
            ["bush_dry"] = "Arbusto seco",
            ["bush_dry_ivy"] = "Hera seca",
            ["bush_rhodod"] = "Rododendro",
            ["bush_blueberry"] = "Arbusto de mirtilo",
            ["bush_ashberry"] = "Arbusto de sorveira",
            ["bush_barberry"] = "Arbusto de bérberis",
            ["common_stones"] = "Pedras",
            ["common_ores"] = "Veio de minério",
            ["copper_vein"] = "Veio de cobre",
            ["broken_barrel"] = "Barril quebrado",
            ["broken_trash"] = "Entulho",
            ["broken_shelf"] = "Prateleira quebrada",
            ["pot_trash"] = "Potes quebrados",
            ["crates_small"] = "Caixotes",
            ["crates_big"] = "Caixotes",
            ["fishing"] = "Ponto de pesca",
            ["fishing_place"] = "Ponto de pesca",
            ["mushroom"] = "Cogumelo",
            ["loot_bag"] = "Saco de saque",
            ["looters_chest"] = "Baú de saqueadores",
            ["looters_chestbooks"] = "Baú de saqueadores",
        };
    }
}
