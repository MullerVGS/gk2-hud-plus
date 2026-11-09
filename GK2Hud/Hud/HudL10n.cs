using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2Hud.Hud
{
    /// <summary>
    /// Nomes dos dias da semana (o jogo só tem os ids <c>day_*</c> e os ícones, sem texto). Em pt-br seguem a tradução
    /// oficial dos pecados nos itens do jogo (<c>sin_orbe_*</c>: Orgulho, Luxúria, Gula, Inveja, Ira, Preguiça).
    /// </summary>
    internal static class HudL10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["day_pride"] = "Pride",
            ["day_lust"] = "Lust",
            ["day_gluttony"] = "Gluttony",
            ["day_envy"] = "Envy",
            ["day_wrath"] = "Wrath",
            ["day_sloth"] = "Sloth",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["day_pride"] = "Orgulho",
            ["day_lust"] = "Luxúria",
            ["day_gluttony"] = "Gula",
            ["day_envy"] = "Inveja",
            ["day_wrath"] = "Ira",
            ["day_sloth"] = "Preguiça",
        };

        public static string Day(string dayId)
        {
            Dictionary<string, string> table = LLBase.CurrentLang == "pt-br" ? PtBr : En;
            if (dayId != null && table.TryGetValue(dayId, out string s)) return s;
            return dayId != null && En.TryGetValue(dayId, out s) ? s : string.Empty;
        }
    }
}
