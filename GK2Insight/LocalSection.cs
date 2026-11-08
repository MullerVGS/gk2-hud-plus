using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>
    /// Zona atual (qualidade somada sem efeito colateral e de onde vem), crafts em andamento, corpos e clima.
    /// Nunca chamar WorldZoneData.GetTotalQuality() nem PlayerData.GetRes("wz_*"): gravam no save e podem dar conquista.
    /// </summary>
    internal static class LocalSection
    {
        private static readonly AccessTools.FieldRef<CraftComponent, float> TickDuration =
            AccessTools.FieldRefAccess<CraftComponent, float>("autoCraftTickDuration");
        private static readonly AccessTools.FieldRef<CraftComponent, float> TickTime =
            AccessTools.FieldRefAccess<CraftComponent, float>("currentAutoCraftTickTime");

        public static void WriteZone(Panel p, MainGame game, PlayerData player)
        {
            WorldZoneData wz = player.CurrentWorldZoneData;
            string scene = MainGame.PlayerController != null && MainGame.PlayerController.TryGetCurrentGameScene(out GameScene gs) ? gs.Id : "?";
            Vector3 pos = MainGame.PlayerController != null ? MainGame.PlayerController.transform.position : Vector3.zero;
            if (wz == null)
            {
                p.Section("Fora de zona", $"cena {scene} · pos {pos.x:0.0}, {pos.z:0.0}");
            }
            else
            {
                string name = Names.Get("wz_" + wz.id);
                p.Section(name, $"{wz.id} · cena {scene} · pos {pos.x:0.0}, {pos.z:0.0}");
                WriteQuality(p, wz);
            }
            if (player.insideTownSubZones != null && player.insideTownSubZones.Count > 0)
            {
                string subs = string.Join(", ", player.insideTownSubZones.Where(s => s != null).Select(s => Names.Get(s.id)));
                p.Line($" cidade {subs} · qualidade da cidade {game.GameSave.townSystem.Quality}");
            }
        }

        private static void WriteQuality(Panel p, WorldZoneData wz)
        {
            List<SGuid> ids = wz.Definition != null && wz.Definition.hasCustomQualityZones ? wz.customQualityWgoDataList : wz.wgoDataList;
            Dictionary<string, (int count, float sum)> byId = new Dictionary<string, (int, float)>();
            float total = 0f;
            foreach (SGuid g in ids)
            {
                WgoData w = MainGame.WorldData.GetWgoData(g);
                if (w == null) continue;
                float q = w.Quality;
                if (Mathf.Approximately(q, 0f)) continue;
                total += q;
                byId.TryGetValue(w.id, out (int count, float sum) acc);
                byId[w.id] = (acc.count + 1, acc.sum + q);
            }
            total += wz.additionalQuality;
            if (!wz.IsContainer && byId.Count == 0)
            {
                return;
            }
            string extra = wz.additionalQuality != 0 ? Panel.Dim($" (inclui {wz.additionalQuality:+0;-0} fixo)") : "";
            p.Line($" qualidade {Panel.Hi(total.ToString("0.#"))}{extra} · {wz.wgoDataList.Count} objetos na zona");
            List<KeyValuePair<string, (int count, float sum)>> sorted = byId.OrderByDescending(kv => kv.Value.sum).ToList();
            List<string[]> rows = new List<string[]>();
            foreach (var kv in sorted.Where(kv => kv.Value.sum > 0).Take(4))
                rows.Add(new[] { Panel.Ok($"{kv.Value.sum:+0.#}"), $"{kv.Value.count}× {Names.Get(kv.Key)}" });
            foreach (var kv in sorted.Where(kv => kv.Value.sum < 0).Reverse().Take(4))
                rows.Add(new[] { Panel.Warn($"{kv.Value.sum:0.#}"), $"{kv.Value.count}× {Names.Get(kv.Key)}" });
            p.Table(rows, 2);
        }

        public static void WriteCrafts(Panel p, MainGame game, PlayerData player)
        {
            WorldZoneData wz = player.CurrentWorldZoneData;
            if (wz == null || game.GameSave.craftSystemData == null) return;
            List<string[]> rows = new List<string[]>();
            foreach (CraftComponent cc in game.GameSave.craftSystemData.activeCrafts)
            {
                if (cc == null || !(cc.CraftableObject is WgoData w) || w.WorldZoneData != wz) continue;
                CraftElementBase el = cc.CurrentCraftElement;
                if (el == null) continue;
                int total = Mathf.Max(1, el.TotalProgressTicks);
                string left = "";
                if (el.ProgressTicks >= total) left = Panel.Ok("pronto, aguardando retirada");
                else if (el.Def != null && el.Def.isAuto)
                {
                    float tick = TickDuration(cc) <= 0f ? 5f : TickDuration(cc);
                    left = TimeSection.Real((total - el.ProgressTicks) * tick - TickTime(cc));
                }
                string status = el.CraftStatus == CraftStatus.OK ? "" : Panel.Warn(el.CraftStatus.ToString());
                rows.Add(new[] { Names.Get(w.id), Names.Get(el.CraftId), $"{el.ProgressTicks}/{total}", left, status });
                if (rows.Count >= 6) break;
            }
            if (rows.Count == 0) return;
            p.Section("Crafts na zona", "progresso em ticks · tempo real restante (auto)");
            p.Table(rows);
        }

        public static void WriteDrops(Panel p, MainGame game)
        {
            if (MainGame.PlayerController == null || !MainGame.PlayerController.TryGetCurrentGameScene(out GameScene gs) || gs.GameSceneData == null) return;
            int day = game.GameSave.environmentData.Day;
            List<string[]> corpses = new List<string[]>();
            int aged = 0, total = 0;
            foreach (DropData d in gs.GameSceneData.droppedItems)
            {
                if (d == null) continue;
                total++;
                if (day - d.SpawnedAtDay >= 2) aged++;
                if (d.AutoDestroyTimer > 0f && corpses.Count < 4)
                    corpses.Add(new[] { Names.Get(d.Id), "apodrece em ~" + TimeSection.Real(d.AutoDestroyTimer) });
            }
            if (total == 0) return;
            p.Section("Itens no chão", $"{total} nesta cena" + (aged > 0 ? $" · {aged} com 2+ dias (corvo leva, se houver ninho)" : ""));
            p.Table(corpses);
        }

        public static void WriteWeather(Panel p, MainGame game)
        {
            WeatherData wd = game.GameSave.weatherData;
            if (wd == null) return;
            float dayMinutes = EnvironmentEngine.Instance != null ? EnvironmentEngine.Instance.gameplayDayInMinutes : 7.5f;
            float phase = WeatherSystem.Instance != null ? WeatherSystem.Instance.phaseLength : 0.25f;
            string state = string.IsNullOrEmpty(wd.stateName) || wd.stateName == "CleanWeather" ? "limpo" : wd.stateName;
            string next;
            if (wd.hasForceState) next = "fixo (forçado por evento)";
            else if (wd.isWeatherPausedByCinematics) next = "pausado (cena)";
            else if (wd.isWeatherPausedByTimeOfDay) next = "pausado à noite";
            else next = "pode mudar em " + TimeSection.Real(Mathf.Max(0f, phase - wd.currentPhaseLen) * dayMinutes * 60f);
            p.Line($" clima {state} · {next}");
        }
    }

    /// <summary>Cache de textos localizados: LLBase.L faz busca linear em aliases a cada chamada.</summary>
    internal static class Names
    {
        private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
        private static string lang;

        public static string Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            if (lang != LazyBearTechnology.LLBase.CurrentLang)
            {
                Cache.Clear();
                lang = LazyBearTechnology.LLBase.CurrentLang;
            }
            if (!Cache.TryGetValue(id, out string name))
            {
                name = Clean(LazyBearTechnology.LLBase.L(id));
                if (name == id)
                {
                    // Variantes numeradas (pot_trash_4) costumam não ter texto próprio: tenta a base.
                    string trimmed = System.Text.RegularExpressions.Regex.Replace(id, "_\\d+$", "");
                    string baseName = trimmed != id ? Clean(LazyBearTechnology.LLBase.L(trimmed)) : trimmed;
                    if (baseName != trimmed) name = baseName;
                }
                Cache[id] = name;
            }
            return name;
        }

        private static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", "<[^>]+>", "").Trim();
    }
}
