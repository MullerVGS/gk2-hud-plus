using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2Hud.Quests
{
    /// <summary>Textos próprios da árvore de quests (en e pt-br, segue LLBase.CurrentLang).</summary>
    internal static class QuestL10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["in_progress"] = "In progress",
            ["available"] = "Not started",
            ["done"] = "Done",
            ["since_day"] = "since day {0}",
            ["on_day"] = "day {0}, {1}",
            ["before_journal"] = "before day {0}",
            ["earlier"] = "Earlier",
            ["more"] = "+{0} earlier",
            ["needs"] = "Needs",
            ["have"] = "have {0}",
            ["about"] = "Middle click: about {0}",
            ["now_at"] = "Now at",
            ["whereabouts"] = "Whereabouts unknown",
            ["wants"] = "Waiting on me",
            ["notes"] = "What I learned",
            ["day_short"] = "Day {0}",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["in_progress"] = "Em andamento",
            ["available"] = "Não iniciada",
            ["done"] = "Concluída",
            ["since_day"] = "desde o dia {0}",
            ["on_day"] = "dia {0}, {1}",
            ["before_journal"] = "antes do dia {0}",
            ["earlier"] = "Antes",
            ["more"] = "+{0} anteriores",
            ["needs"] = "Precisa",
            ["have"] = "tenho {0}",
            ["about"] = "Botão do meio: sobre {0}",
            ["now_at"] = "Agora em",
            ["whereabouts"] = "Paradeiro desconhecido",
            ["wants"] = "Esperando por mim",
            ["notes"] = "O que eu soube",
            ["day_short"] = "Dia {0}",
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
