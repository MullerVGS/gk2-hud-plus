using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.Profiling;

namespace GK2Insight.Telemetry
{
    /// <summary>
    /// Métricas (1 amostra/s) e eventos do jogo para o VictoriaMetrics/VictoriaLogs da VPS.
    /// Desempenho: frames, travadas, GC e memória. Jogador: recursos crus, tempo, sono, zona. Eventos: sessão, save,
    /// dia novo, sono, troca de zona e de cena, travadas grandes. Só leitura; nada aqui chama método que grava no save.
    /// O custo do próprio Insight (SelfCost) sai daqui também, para comparar blue e green com e sem painel.
    /// </summary>
    internal sealed class Telemetry : IDisposable
    {
        private const float HitchSeconds = 0.1f;
        private const float BigHitchSeconds = 0.25f;

        private static readonly AccessTools.FieldRef<PlayerData, GameRes> PlayerRes =
            AccessTools.FieldRefAccess<PlayerData, GameRes>("res");

        public readonly SelfCost Cost = new SelfCost();
        public readonly string Instance;

        private readonly Shipper shipper;
        private readonly MetricBatch batch = new MetricBatch();
        private readonly string commonLabels, commonJson;
        private readonly List<float> frames = new List<float>(512);
        private float[] sortBuffer = new float[512];
        private int hitches, bigHitches, lastGc;
        private float nextSample, nextSlow;
        private bool wasInGame, wasSleeping, saving;
        private float stateSince, sleepSince, saveSince, loadSince = -1f;
        private float sleepEnergyStart;
        private int sleepDayStart;
        private string zone, scene;
        private float zoneQuality = float.NaN;
        private int wgoWorld, wgoLoaded, scenesLoaded;
        private long workingSet;
        private int saveStartFrame, saveEndedFrame = -10;

        public Func<(bool local, bool tech)> LayersVisible;

        /// <summary>Envio desligado quando não há token: a medição continua (para o painel técnico).</summary>
        public bool Sending => shipper != null;
        public Shipper Shipper => shipper;

        public Telemetry(string url, string token)
        {
            Instance = Application.productName == "GK2 Green Instance" ? "green" : "blue";
            commonLabels = MetricBatch.Labels("instance", Instance);
            StringBuilder sb = new StringBuilder();
            sb.Append(",\"app\":\"gk2\",\"instance\":\"").Append(Instance).Append("\",\"session\":");
            EventLine.Quote(sb, SessionId());
            sb.Append(",\"version\":\"").Append(Plugin.Version).Append('"');
            commonJson = sb.ToString();
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(url)) shipper = new Shipper(url, token.Trim());
            lastGc = GC.CollectionCount(0);

            SaveSystem.OnSaveWriteStarted += SaveStarted;
            SaveSystem.OnSaveWriteStartedInstant += SaveStarted;
            SaveSystem.OnSaveWriteEnded += SaveEnded;
            SaveSystem.OnSaveLoadingStarted += LoadStarted;
            SaveSystem.OnSaveLoadingEnded += LoadEnded;
            EnvironmentEngine.OnNewDayStarted += NewDay;

            Event("session", $"GK2 Insight {Plugin.Version} em {Instance}")
                .Num("game_uptime_s", Time.realtimeSinceStartup)
                .Str("lang", LLBase.CurrentLang)
                .Num("screen_w", Screen.width).Num("screen_h", Screen.height)
                .Bool("fullscreen", Screen.fullScreen)
                .Num("vsync", QualitySettings.vSyncCount)
                .Num("target_fps", Application.targetFrameRate)
                .Num("refresh_hz", Screen.currentResolution.refreshRateRatio.value)
                .Str("gpu", SystemInfo.graphicsDeviceName)
                .Str("cpu", SystemInfo.processorType)
                .Num("ram_mb", SystemInfo.systemMemorySize)
                .Bool("sending", Sending)
                .Emit(this);
        }

        public void Dispose()
        {
            SaveSystem.OnSaveWriteStarted -= SaveStarted;
            SaveSystem.OnSaveWriteStartedInstant -= SaveStarted;
            SaveSystem.OnSaveWriteEnded -= SaveEnded;
            SaveSystem.OnSaveLoadingStarted -= LoadStarted;
            SaveSystem.OnSaveLoadingEnded -= LoadEnded;
            EnvironmentEngine.OnNewDayStarted -= NewDay;
            Event("session_end", "plugin descarregado (reload ou saída)").Num("game_uptime_s", Time.realtimeSinceStartup).Emit(this);
            shipper?.Dispose();
        }

        /// <summary>Uma vez por frame, no começo do Update do plugin.</summary>
        public void OnFrame()
        {
            long t0 = SelfCost.Now();
            float dt = Time.unscaledDeltaTime;
            frames.Add(dt);
            if (dt >= HitchSeconds)
            {
                hitches++;
                if (dt >= BigHitchSeconds)
                {
                    bigHitches++;
                    Hitch(dt);
                }
            }
            float now = Time.unscaledTime;
            if (wasInGame) CheckSleep(now);
            if (now >= nextSample)
            {
                nextSample = now + 1f;
                if (now >= nextSlow)
                {
                    nextSlow = now + 5f;
                    SlowSample();
                }
                Sample(now);
            }
            Cost.Stop(SelfCost.Part.Telemetry, t0);
        }

        private void Sample(float now)
        {
            MainGame game = MainGame.Instance;
            PlayerData player = MainGame.PlayerData;
            bool inGame = game != null && player != null && game.gameState == MainGame.GameState.InGame && game.GameSave != null;
            if (inGame != wasInGame)
            {
                Event("game_state", inGame ? "entrou no jogo" : "voltou ao menu").Num("previous_s", now - stateSince).Emit(this);
                wasInGame = inGame;
                stateSince = now;
                zone = scene = null;
            }

            batch.Begin(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), commonLabels);
            SamplePerf();
            SampleSelf();
            if (inGame)
            {
                try
                {
                    SamplePlayer(game, player, now);
                }
                catch (Exception e)
                {
                    Plugin.WarnOnce("telemetria jogador", e);
                }
            }
            shipper?.EnqueueMetrics(batch.Text);
        }

        private void SamplePerf()
        {
            int n = frames.Count;
            if (n > 0)
            {
                if (sortBuffer.Length < n) sortBuffer = new float[n * 2];
                float sum = 0f;
                for (int i = 0; i < n; i++)
                {
                    sortBuffer[i] = frames[i];
                    sum += frames[i];
                }
                Array.Sort(sortBuffer, 0, n);
                batch.Add("fps", n / Mathf.Max(sum, 1e-4f));
                batch.Add("frame_seconds", sum / n, "stat", "avg");
                batch.Add("frame_seconds", sortBuffer[n / 2], "stat", "p50");
                batch.Add("frame_seconds", sortBuffer[Mathf.Min(n - 1, (int)(n * 0.99f))], "stat", "p99");
                batch.Add("frame_seconds", sortBuffer[n - 1], "stat", "max");
                frames.Clear();
            }
            batch.Add("hitches", hitches, "min", "0.1");
            batch.Add("hitches", bigHitches, "min", "0.25");
            hitches = bigHitches = 0;
            int gc = GC.CollectionCount(0);
            batch.Add("gc_collections", gc - lastGc);
            lastGc = gc;
            batch.Add("memory_bytes", Profiler.GetMonoUsedSizeLong(), "kind", "mono_used");
            batch.Add("memory_bytes", Profiler.GetMonoHeapSizeLong(), "kind", "mono_heap");
            batch.Add("memory_bytes", Profiler.GetTotalAllocatedMemoryLong(), "kind", "unity_allocated");
            if (workingSet > 0) batch.Add("memory_bytes", workingSet, "kind", "working_set");
            batch.Add("time_scale", Time.timeScale);
            batch.Add("vsync", QualitySettings.vSyncCount);
            batch.Add("target_fps", Application.targetFrameRate);
            batch.Add("scenes_loaded", scenesLoaded);
            batch.Add("wgo_objects", wgoWorld, "scope", "world");
            batch.Add("wgo_objects", wgoLoaded, "scope", "loaded_scenes");
            if (shipper != null)
            {
                batch.Add("telemetry_queue", shipper.QueuedMetrics);
                batch.Add("telemetry_failures_total", shipper.Failures);
                batch.Add("telemetry_post_seconds", shipper.LastPostMs / 1000.0);
            }
        }

        private void SampleSelf()
        {
            (bool local, bool tech) v = LayersVisible != null ? LayersVisible() : (false, false);
            batch.Add("insight_layer_visible", v.local ? 1 : 0, "layer", "local");
            batch.Add("insight_layer_visible", v.tech ? 1 : 0, "layer", "tech");
            for (int i = 0; i < (int)SelfCost.Part.Count; i++)
            {
                if (Cost.Calls(i) == 0) continue;
                batch.Add("insight_seconds", Cost.Seconds(i), "part", SelfCost.Names[i]);
                batch.Add("insight_max_seconds", Cost.MaxSeconds(i), "part", SelfCost.Names[i]);
                batch.Add("insight_calls", Cost.Calls(i), "part", SelfCost.Names[i]);
            }
            Cost.Reset();
        }

        private void SamplePlayer(MainGame game, PlayerData player, float now)
        {
            EnvironmentData env = game.GameSave.environmentData;
            batch.Add("game_day", env.Day);
            batch.Add("game_week_day", env.CurrentDayNumber);
            batch.Add("game_time_of_day", env.TimeOfDay);
            batch.Add("game_paused", MainGame.IsGamePaused ? 1 : 0);

            GameRes res = PlayerRes(player);
            if (res != null)
            {
                foreach (GameResAtom a in res.List)
                {
                    if (a == null || string.IsNullOrEmpty(a.type)) continue;
                    if (a.type.EndsWith("_REP", StringComparison.Ordinal)) batch.Add("npc_reputation", a.value, "npc", a.type.Substring(0, a.type.Length - 4));
                    else if (IsPlayerRes(a.type)) batch.Add("player_res", a.value, "res", a.type);
                }
            }
            Max(player, "energy");
            Max(player, "insanity");
            Max(player, "stamina");

            EnergySystem sleep = player.energySystem;
            bool lack = game.GameSave.perkSystemData != null && game.GameSave.perkSystemData.HasPerk("lack_of_sleep_debuff");
            if (sleep != null) batch.Add("player_awake_days", sleep.timeWithoutSleep);
            batch.Add("player_sleeping", wasSleeping ? 1 : 0);
            batch.Add("player_lack_of_sleep", lack ? 1 : 0);

            string sceneNow = MainGame.PlayerController != null && MainGame.PlayerController.TryGetCurrentGameScene(out GameScene gs) ? gs.Id : null;
            if (sceneNow != scene)
            {
                if (scene != null) Event("scene", $"{scene} → {sceneNow}").Str("from", scene).Str("to", sceneNow).Emit(this);
                scene = sceneNow;
            }
            string zoneNow = player.CurrentWorldZoneData?.id;
            if (zoneNow != zone)
            {
                if (zone != null || zoneNow != null)
                {
                    zoneQuality = player.CurrentWorldZoneData != null ? LocalSection.TotalQuality(player.CurrentWorldZoneData) : float.NaN;
                    Event("zone", $"{zone ?? "-"} → {zoneNow ?? "-"}").Str("from", zone).Str("to", zoneNow).Str("scene", sceneNow)
                        .Num("quality", zoneQuality).Emit(this);
                }
                zone = zoneNow;
            }
            if (zone != null) batch.Add("zone_quality", zoneQuality, "zone", zone);
        }

        /// <summary>A cada frame (leitura de uma propriedade): um sono com energia cheia dura ~2 s.</summary>
        private void CheckSleep(float now)
        {
            PlayerData player = MainGame.PlayerData;
            EnergySystem sleep = player?.energySystem;
            bool sleeping = sleep != null && sleep.IsSleeping;
            if (sleeping == wasSleeping) return;
            wasSleeping = sleeping;
            EnvironmentData env = MainGame.Instance?.GameSave?.environmentData;
            if (env == null) return;
            float energy = player.GetRes("energy");
            if (sleeping)
            {
                sleepSince = now;
                sleepEnergyStart = energy;
                sleepDayStart = env.Day;
                Event("sleep_start", "foi dormir").Num("energy", energy).Num("insanity", player.GetRes("insanity"))
                    .Num("awake_days", sleep.timeWithoutSleep).Num("time_of_day", env.TimeOfDay).Emit(this);
            }
            else
            {
                Event("sleep_end", $"acordou após {now - sleepSince:0.0} s").Num("real_s", now - sleepSince)
                    .Num("energy", energy).Num("energy_gain", energy - sleepEnergyStart)
                    .Num("days", env.Day - sleepDayStart).Num("time_of_day", env.TimeOfDay).Emit(this);
            }
        }

        /// <summary>A cada 5 s: varreduras que custam mais (objetos carregados, qualidade da zona, memória do processo).</summary>
        private void SlowSample()
        {
            workingSet = ProcessMemory.WorkingSet();
            PlayerData player = MainGame.PlayerData;
            if (MainGame.Instance == null || player == null || MainGame.Instance.gameState != MainGame.GameState.InGame) return;
            try
            {
                wgoWorld = MainGame.WorldData?.Cache?.wgoDataByUidCache?.Count ?? 0;
                wgoLoaded = scenesLoaded = 0;
                foreach (GameScene gs in LazySingleton<GameSceneManager>.Instance.LoadedGameScenes)
                {
                    if (gs?.GameSceneData == null) continue;
                    scenesLoaded++;
                    wgoLoaded += gs.GameSceneData.wgoDataList.Count;
                }
                if (player.CurrentWorldZoneData != null) zoneQuality = LocalSection.TotalQuality(player.CurrentWorldZoneData);
            }
            catch (Exception e)
            {
                Plugin.WarnOnce("telemetria lenta", e);
            }
        }

        private void Max(PlayerData player, string id)
        {
            if (player.GetResSystem(id) is GK2GameResSystem s) batch.Add("player_res_max", s.Max, "res", id);
        }

        private static bool IsPlayerRes(string type)
        {
            switch (type)
            {
                case "money":
                case "energy":
                case "insanity":
                case "stamina":
                case "happiness":
                case "tech_red":
                case "tech_green":
                case "tech_blue":
                case "cur_zombies_count":
                case "global_ppl":
                    return true;
                default:
                    return false;
            }
        }

        private void Hitch(float dt)
        {
            (bool local, bool tech) v = LayersVisible != null ? LayersVisible() : (false, false);
            Event("hitch", $"frame de {dt * 1000f:0} ms").Num("frame_ms", dt * 1000f)
                .Bool("gc", GC.CollectionCount(0) != lastGc).Bool("saving", saving || saveEndedFrame >= Time.frameCount - 1).Bool("loading", loadSince >= 0f)
                .Str("scene", scene).Str("zone", zone).Bool("in_game", wasInGame)
                .Bool("insight_local", v.local).Bool("insight_tech", v.tech).Emit(this);
        }

        private void SaveStarted()
        {
            saving = true;
            saveStartFrame = Time.frameCount;
            saveSince = Time.realtimeSinceStartup;
        }

        private void SaveEnded()
        {
            if (!saving) return;
            saving = false;
            saveEndedFrame = Time.frameCount;
            float ms = (Time.realtimeSinceStartup - saveSince) * 1000f;
            Event("save_write", $"save gravado em {ms:0} ms").Num("ms", ms).Bool("same_frame", saveStartFrame == Time.frameCount).Emit(this);
        }

        private void LoadStarted() => loadSince = Time.realtimeSinceStartup;

        private void LoadEnded()
        {
            if (loadSince < 0f) return;
            float ms = (Time.realtimeSinceStartup - loadSince) * 1000f;
            loadSince = -1f;
            Event("save_load", $"save carregado em {ms:0} ms").Num("ms", ms).Emit(this);
        }

        private void NewDay(int day)
        {
            PlayerData player = MainGame.PlayerData;
            if (player == null) return;
            Event("new_day", $"dia {day}").Num("day", day).Num("money", player.GetRes("money"))
                .Num("energy", player.GetRes("energy")).Num("insanity", player.GetRes("insanity"))
                .Bool("sleeping", wasSleeping).Emit(this);
        }

        private EventLine Event(string kind, string msg) => new EventLine(kind, msg, commonJson);

        internal void Emit(EventLine e) => shipper?.EnqueueEvent(e.Build());

        private static string SessionId()
        {
            try
            {
                using (System.Diagnostics.Process p = System.Diagnostics.Process.GetCurrentProcess())
                    return p.StartTime.ToString("yyyyMMdd-HHmmss") + "-" + p.Id;
            }
            catch
            {
                return DateTime.Now.ToString("yyyyMMdd-HHmmss");
            }
        }
    }

    /// <summary>Process.WorkingSet64 volta 0 no Mono do jogo: lê direto do Windows.</summary>
    internal static class ProcessMemory
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Counters
        {
            public uint cb, PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool K32GetProcessMemoryInfo(IntPtr process, out Counters counters, uint size);

        public static long WorkingSet()
        {
            try
            {
                uint size = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Counters));
                return K32GetProcessMemoryInfo(GetCurrentProcess(), out Counters c, size) ? (long)c.WorkingSetSize.ToUInt64() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal static class EventLineExtensions
    {
        public static void Emit(this EventLine e, Telemetry t) => t.Emit(e);
    }
}
