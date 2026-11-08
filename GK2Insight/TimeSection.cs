using UnityEngine;

namespace GK2Insight
{
    /// <summary>Tempo, sono e números exatos que o HUD só mostra como barra.</summary>
    internal static class TimeSection
    {
        private const float LackOfSleepDays = 2f;

        public static void Write(Panel p, MainGame game, PlayerData player)
        {
            EnvironmentData env = game.GameSave.environmentData;
            float dayMinutes = EnvironmentEngine.Instance != null ? EnvironmentEngine.Instance.gameplayDayInMinutes : 7.5f;
            float t = env.TimeOfDay;
            int minutes = (int)(t * 1440f);
            p.Section($"Dia {env.Day}", $"semana {env.CurrentDayNumber}/{EnvironmentData.DAYS_IN_WEEK} · 1 dia = {dayMinutes:0.#} min reais");
            string clock = $"{minutes / 60:00}:{minutes % 60:00}";
            p.Line($" {Panel.Hi(clock)}  vira o dia em {Real((1f - t) * dayMinutes * 60f)}");

            EnergySystem sleep = player.energySystem;
            PerkSystemData perks = game.GameSave.perkSystemData;
            if (sleep != null)
            {
                if (sleep.IsSleeping)
                    p.Line(" sono  dormindo");
                else if (perks != null && perks.HasPerk("lack_of_sleep_debuff"))
                    p.Line(" sono  " + Panel.Warn("falta de sono ATIVA") + Panel.Dim(" · energia gasta vira +0,5 insanidade/ponto"));
                else
                    p.Line($" sono  acordado {sleep.timeWithoutSleep:0.00} d · falta de sono em {Real(Mathf.Max(0f, LackOfSleepDays - sleep.timeWithoutSleep) * dayMinutes * 60f)}");
            }
            p.Line($" energia {Res(player, "energy")}  insanidade {Res(player, "insanity")}  stamina {Res(player, "stamina")}");
        }

        private static string Res(PlayerData player, string id)
        {
            float v = player.GetRes(id);
            return player.GetResSystem(id) is GK2GameResSystem s ? $"{v:0.#}/{s.Max:0.#}" : $"{v:0.#}";
        }

        public static string Real(float seconds)
        {
            int s = Mathf.Max(0, (int)seconds);
            return s >= 60 ? $"{s / 60}m{s % 60:00}s" : $"{s}s";
        }
    }
}
