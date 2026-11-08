using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Insight
{
    internal class Hud
    {
        private const float RefreshInterval = 0.25f;
        private const float LackOfSleepDays = 2f;

        private static readonly Regex RichTag = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly AccessTools.FieldRef<PlayerData, GameRes> PlayerRes =
            AccessTools.FieldRefAccess<PlayerData, GameRes>("res");

        private readonly StringBuilder sb = new StringBuilder(1024);
        private string text = "";
        private float nextRefresh;
        private GUIStyle style;
        private int styleFontSize;
        private Texture2D background;

        public bool Detailed { get; set; }

        public void Refresh()
        {
            if (Time.unscaledTime < nextRefresh)
            {
                return;
            }
            nextRefresh = Time.unscaledTime + RefreshInterval;
            try
            {
                text = Build();
            }
            catch (Exception e)
            {
                text = "";
                Plugin.Log.LogDebug("HUD: " + e.Message);
            }
        }

        public void Draw(float x, float y, int fontSize)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            EnsureStyle(fontSize);
            GUIContent content = new GUIContent(text);
            Vector2 size = style.CalcSize(content);
            GUI.Box(new Rect(x, y, size.x, size.y), content, style);
        }

        private void EnsureStyle(int fontSize)
        {
            if (style != null && styleFontSize == fontSize)
            {
                return;
            }
            if (background == null)
            {
                background = new Texture2D(1, 1);
                background.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.72f));
                background.Apply();
            }
            style = new GUIStyle(GUI.skin.box)
            {
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                richText = true,
                wordWrap = false,
                padding = new RectOffset(10, 10, 8, 8)
            };
            style.normal.background = background;
            style.normal.textColor = new Color(0.92f, 0.9f, 0.84f);
            styleFontSize = fontSize;
        }

        private string Build()
        {
            MainGame game = MainGame.Instance;
            PlayerData player = MainGame.PlayerData;
            if (game == null || player == null || game.GameSave == null || game.GameSave.environmentData == null)
            {
                return "";
            }
            sb.Length = 0;
            AppendTime(game);
            AppendResources(player);
            AppendSleep(player, game);
            AppendPerks(game);
            if (Detailed)
            {
                AppendAllRes(player);
            }
            if (Time.unscaledTime - Dumper.LastStatusTime < 10f)
            {
                sb.Append("<color=#80d0ff>").Append(Dumper.LastStatus).Append("</color>\n");
            }
            return sb.ToString().TrimEnd();
        }

        private void AppendTime(MainGame game)
        {
            EnvironmentData env = game.GameSave.environmentData;
            float t = env.TimeOfDay;
            int minutes = (int)(t * 1440f);
            float dayMinutes = EnvironmentEngine.Instance != null ? EnvironmentEngine.Instance.gameplayDayInMinutes : 5f;
            float realSecondsLeft = (1f - t) * dayMinutes * 60f;
            sb.Append($"<b>Dia {env.Day}</b>  ·  semana {env.CurrentDayNumber}/{EnvironmentData.DAYS_IN_WEEK}  ·  {minutes / 60:00}:{minutes % 60:00}  ({t * 100f:0}%)\n");
            sb.Append($"Virada do dia em {FormatReal(realSecondsLeft)} reais  ·  1 dia = {dayMinutes:0.#} min\n");
        }

        private void AppendResources(PlayerData player)
        {
            sb.Append($"Energia  {ResLine("energy")}    Insanidade  {ResLine("insanity")}\n");
            sb.Append($"Stamina  {ResLine("stamina")}    Felicidade  {ResLine("happiness")}\n");
            int money = (int)player.GetRes("money");
            sb.Append($"Dinheiro  {FormatMoney(money)}    Tech  <color=#e06060>{player.GetRes("tech_red"):0}</color> / <color=#60c060>{player.GetRes("tech_green"):0}</color> / <color=#6090e0>{player.GetRes("tech_blue"):0}</color>\n");
        }

        private void AppendSleep(PlayerData player, MainGame game)
        {
            EnergySystem energy = player.energySystem;
            if (energy == null)
            {
                return;
            }
            bool debuff = game.GameSave.perkSystemData != null && game.GameSave.perkSystemData.HasPerk("lack_of_sleep_debuff");
            if (energy.IsSleeping)
            {
                sb.Append("Dormindo\n");
                return;
            }
            if (debuff)
            {
                sb.Append("<color=#ff7070>Falta de sono ATIVA</color>: cada ponto de energia gasto dá +0,5 de insanidade\n");
                return;
            }
            float dayMinutes = EnvironmentEngine.Instance != null ? EnvironmentEngine.Instance.gameplayDayInMinutes : 5f;
            float left = Mathf.Max(0f, LackOfSleepDays - energy.timeWithoutSleep);
            sb.Append($"Acordado há {energy.timeWithoutSleep:0.00} dia(s)  ·  falta de sono em {FormatReal(left * dayMinutes * 60f)} reais\n");
        }

        private void AppendPerks(MainGame game)
        {
            PerkSystemData perks = game.GameSave.perkSystemData;
            if (perks == null || perks.activePerks == null || perks.activePerks.Count == 0)
            {
                return;
            }
            sb.Append("<b>Efeitos ativos</b>\n");
            foreach (PerkData perk in perks.activePerks)
            {
                PerkDef def = perk.Definition;
                if (def == null)
                {
                    continue;
                }
                string name = Clean(LLBase.L(perk.id));
                sb.Append("  • ").Append(name);
                if (def.isHidden)
                {
                    sb.Append(" <color=#888888>(oculto)</color>");
                }
                if (!def.hiddenTimer && perk.currentDuration > 0f)
                {
                    sb.Append("  ").Append(PerkSystemData.GetFormattedDuration(perk.currentDuration));
                }
                string effects = PerkEffects(def);
                if (effects.Length > 0)
                {
                    sb.Append("  <color=#a0a0a0>[").Append(effects).Append("]</color>");
                }
                sb.Append('\n');
                string descKey = perk.id + "_d";
                string desc = LLBase.L(descKey);
                if (desc != descKey && !string.IsNullOrEmpty(desc))
                {
                    sb.Append("     <color=#b8b090>").Append(Clean(desc)).Append("</color>\n");
                }
            }
        }

        private static string PerkEffects(PerkDef def)
        {
            List<string> parts = new List<string>();
            if (def.energyAdd != 0f) parts.Add($"energia {def.energyAdd:+0.##;-0.##}");
            if (def.insanityAdd != 0f) parts.Add($"insanidade {def.insanityAdd:+0.##;-0.##}");
            if (def.craftStartTicks != 0) parts.Add($"ticks iniciais {def.craftStartTicks:+0;-0}");
            if (def.craftTotalProgressTicksBonus != 0) parts.Add($"progresso {def.craftTotalProgressTicksBonus:+0;-0}");
            if (def.craftMasteryBonus != 0) parts.Add($"maestria {def.craftMasteryBonus:+0;-0}");
            if (def.tickRate > 0f && def.addGameResPerTick != null && def.addGameResPerTick.List.Count > 0)
            {
                foreach (GameResAtom atom in def.addGameResPerTick.List)
                {
                    parts.Add($"{atom.type} {atom.value:+0.##;-0.##}/{def.tickRate:0.#}s");
                }
            }
            return string.Join(", ", parts);
        }

        private void AppendAllRes(PlayerData player)
        {
            GameRes res = PlayerRes(player);
            if (res == null)
            {
                return;
            }
            sb.Append("<b>Todos os recursos do jogador</b>\n");
            List<GameResAtom> atoms = new List<GameResAtom>(res.List);
            atoms.Sort((a, b) => string.CompareOrdinal(a.type, b.type));
            int column = 0;
            foreach (GameResAtom atom in atoms)
            {
                if (atom.value == 0f)
                {
                    continue;
                }
                sb.Append(column == 0 ? "  " : "    ").Append(atom.type).Append('=').Append(atom.value.ToString("0.##"));
                if (++column == 4)
                {
                    sb.Append('\n');
                    column = 0;
                }
            }
            if (column != 0)
            {
                sb.Append('\n');
            }
        }

        private static string ResLine(string id)
        {
            PlayerData player = MainGame.PlayerData;
            float value = player.GetRes(id);
            if (player.GetResSystem(id) is GK2GameResSystem system)
            {
                return $"<b>{value:0.#}</b>/{system.Max:0.#}";
            }
            return $"<b>{value:0.#}</b>";
        }

        private static string FormatMoney(int value)
        {
            int abs = Math.Abs(value);
            int gold = abs / 10000;
            int silver = abs / 100 % 100;
            int bronze = abs % 100;
            string sign = value < 0 ? "-" : "";
            return $"{sign}<color=#e8c040>{gold}o</color> <color=#c8c8d0>{silver}p</color> <color=#c08050>{bronze}b</color>";
        }

        private static string FormatReal(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int s = (int)seconds;
            return s >= 60 ? $"{s / 60}m{s % 60:00}s" : $"{s}s";
        }

        private static string Clean(string s)
        {
            return RichTag.Replace(s ?? "", "").Replace("\n", " ").Trim();
        }
    }
}
