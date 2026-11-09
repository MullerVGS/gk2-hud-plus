using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Textos próprios do mod (en e pt-br, segue LLBase.CurrentLang). Rótulos que o jogo já tem vêm de LLBase.L
    /// (ui_crafted_at, energy, insanity, ui_mastery...); aqui só o que não existe nativamente.
    /// </summary>
    internal static class L10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["used_in"] = "Used in",
            ["you_have"] = "You have",
            ["have_split"] = "inventory {0} · chests {1}",
            ["durability"] = "Durability",
            ["uses_left"] = "~{0} uses",
            ["duration"] = "Duration",
            ["ticks"] = "{0} ticks",
            ["per_tick"] = "{0} per tick",
            ["total"] = "~{0} total",
            ["left"] = "{0} left",
            ["crafts"] = "Crafts",
            ["granted_by"] = "Granted by",
            ["craft_energy"] = "Craft energy",
            ["craft_insanity"] = "Craft insanity",
            ["head_start"] = "Head start",
            ["craft_length"] = "Craft length",
            ["every"] = "Every {0}",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["used_in"] = "Usado em",
            ["you_have"] = "Você tem",
            ["have_split"] = "inventário {0} · baús {1}",
            ["durability"] = "Durabilidade",
            ["uses_left"] = "~{0} usos",
            ["duration"] = "Duração",
            ["ticks"] = "{0} ticks",
            ["per_tick"] = "{0} por tick",
            ["total"] = "~{0} no total",
            ["left"] = "restam {0}",
            ["crafts"] = "Fabrica",
            ["granted_by"] = "Vem de",
            ["craft_energy"] = "Energia no craft",
            ["craft_insanity"] = "Insanidade no craft",
            ["head_start"] = "Início adiantado",
            ["craft_length"] = "Duração do craft",
            ["every"] = "A cada {0}",
        };

        public static string T(string key)
        {
            Dictionary<string, string> table = LLBase.CurrentLang == "pt-br" ? PtBr : En;
            if (table.TryGetValue(key, out string s)) return s;
            return En.TryGetValue(key, out s) ? s : key;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);
    }
}
