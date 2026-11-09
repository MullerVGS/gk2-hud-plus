using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Textos do calendário (en e pt-br). As descrições dos eventos vêm do próprio jogo sempre que ele explica a regra
    /// (tutoriais e falas: <c>tut_sermons_day</c>, <c>tut_commerce_day</c>...); aqui ficam títulos e o que o jogo não diz.
    /// </summary>
    internal static class CalendarL10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["tab"] = "Calendar",
            ["week"] = "This week",
            ["today"] = "Today",
            ["tomorrow"] = "Tomorrow",
            ["in_days"] = "In {0} days",
            ["day_n"] = "Day {0}",
            ["nothing"] = "Nothing you know of happens on this day.",
            ["nothing_short"] = "—",
            ["all_day"] = "All day",
            ["starts_in"] = "starts in {0}",
            ["ends_in"] = "ends in {0}",
            ["in"] = "in {0}",
            ["until"] = "until {0}",
            ["now"] = "now",
            ["ended"] = "over",
            ["passed"] = "passed",
            ["at_dawn"] = "Right after midnight",
            ["done_today"] = "Done for this week",
            ["next"] = "Next",
            ["reset_in"] = "Week resets on {0} {1}",
            ["reset_today"] = "Week reset: today",
            ["power_ok"] = "The room has power.",
            ["power_used"] = "No power yet: it comes back at the end of {0}.",
            ["hint"] = "Hover a sigil to see that day.",
            // eventos
            ["ev_orders"] = "Town orders",
            ["ev_shops"] = "Shops reset",
            ["ev_ship"] = "Merchant ship at the port",
            ["ev_linda"] = "Green Scrolls for Linda",
            ["ev_pr_machine"] = "PR Machine",
            ["ev_resurrection"] = "Resurrection storm",
            ["ev_goddess"] = "Goddess's tears",
            ["ev_goddess_d"] = "The Goddess weeps today: collect the blue crystals by her statue afterwards.",
            ["ev_sermon"] = "Sermon",
            ["ev_nun"] = "The Nun drops by",
            ["ev_nun_d"] = "She checks on your progress. Ask her how many corpses the week needs.",
            ["ev_battle"] = "Battle",
            ["ev_donkey"] = "Donkey",
            ["ev_donkey_d"] = "Brings a corpse delivery to the morgue every other day.",
            ["ev_nun_body"] = "Nun's delivery",
            ["ev_nun_body_d"] = "While the donkey is away, the Nun may bring a corpse.",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["tab"] = "Calendário",
            ["week"] = "Esta semana",
            ["today"] = "Hoje",
            ["tomorrow"] = "Amanhã",
            ["in_days"] = "Em {0} dias",
            ["day_n"] = "Dia {0}",
            ["nothing"] = "Nada que você conheça acontece neste dia.",
            ["nothing_short"] = "—",
            ["all_day"] = "O dia todo",
            ["starts_in"] = "começa em {0}",
            ["ends_in"] = "acaba em {0}",
            ["in"] = "em {0}",
            ["until"] = "até {0}",
            ["now"] = "agora",
            ["ended"] = "acabou",
            ["passed"] = "passou",
            ["at_dawn"] = "Logo após a meia-noite",
            ["done_today"] = "Feito nesta semana",
            ["next"] = "Próximo",
            ["reset_in"] = "A semana renova no {0} {1}",
            ["reset_today"] = "Semana renovada: hoje",
            ["power_ok"] = "A sala tem energia.",
            ["power_used"] = "Sem energia: ela volta no fim de {0}.",
            ["hint"] = "Pare o mouse num sigilo para ver o dia.",
            ["ev_orders"] = "Encomendas da cidade",
            ["ev_shops"] = "Lojas renovam",
            ["ev_ship"] = "Navio mercante no porto",
            ["ev_linda"] = "Pergaminhos Verdes para a Linda",
            ["ev_pr_machine"] = "Máquina de RP",
            ["ev_resurrection"] = "Tempestade da ressurreição",
            ["ev_goddess"] = "Lágrimas da Deusa",
            ["ev_goddess_d"] = "A Deusa chora hoje: depois, colete os cristais azuis junto à estátua dela.",
            ["ev_sermon"] = "Sermão",
            ["ev_nun"] = "A Freira passa",
            ["ev_nun_d"] = "Ela confere o seu progresso. Pergunte quantos corpos a semana pede.",
            ["ev_battle"] = "Batalha",
            ["ev_donkey"] = "Burro",
            ["ev_donkey_d"] = "Traz uma entrega de corpos ao necrotério a cada dois dias.",
            ["ev_nun_body"] = "Entrega da Freira",
            ["ev_nun_body_d"] = "Enquanto o burro está fora, a Freira pode trazer um corpo.",
        };

        private static Dictionary<string, string> Table => LLBase.CurrentLang == "pt-br" ? PtBr : En;

        public static string T(string key) => TOrNull(key) ?? key;

        public static string T(string key, params object[] args) => string.Format(T(key), args);

        public static string TOrNull(string key)
        {
            if (Table.TryGetValue(key, out string s)) return s;
            return En.TryGetValue(key, out s) ? s : null;
        }

        /// <summary>"Hoje" / "Amanhã" / "Em N dias".</summary>
        public static string Relative(int deltaDays)
        {
            if (deltaDays <= 0) return T("today");
            if (deltaDays == 1) return T("tomorrow");
            return T("in_days", deltaDays);
        }
    }
}
