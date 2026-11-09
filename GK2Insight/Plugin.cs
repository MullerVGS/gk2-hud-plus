using BepInEx;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2Insight.Telemetry;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>
    /// Ferramenta de debug e estudo (fora da regra de visual nativo). F7 = local e momento; Shift+F7 = técnico e
    /// overlays no mundo; Ctrl+F7 = dump de dados. Só leitura.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.insight";
        public const string Name = "GK2 Insight";
        public const string Version = "0.3.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        private ConfigEntry<KeyboardShortcut> localKey;
        private ConfigEntry<KeyboardShortcut> techKey;
        private ConfigEntry<KeyboardShortcut> dumpKey;
        private ConfigEntry<bool> localVisible;
        private ConfigEntry<bool> techVisible;
        private ConfigEntry<float> localX;
        private ConfigEntry<float> localY;
        private ConfigEntry<int> fontSize;
        private ConfigEntry<float> overlayRadius;
        private ConfigEntry<bool> showColliders, showPaths, showDocks, showHitboxes;

        private ConfigEntry<bool> telemetryEnabled;
        private ConfigEntry<string> telemetryUrl, telemetryToken;

        private Hud hud;
        private Telemetry.Telemetry telemetry;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        private void Awake()
        {
            Log = Logger;
            localKey = Config.Bind("Local", "ToggleKey", new KeyboardShortcut(KeyCode.F7), "Painel do local e do momento (zona, NPCs, zumbis, tempo).");
            localVisible = Config.Bind("Local", "Visible", true, "Painel local visível ao iniciar.");
            localX = Config.Bind("Local", "X", 12f, "Posição horizontal do painel local (pixels).");
            localY = Config.Bind("Local", "Y", 270f, "Posição vertical do painel local (pixels).");
            techKey = Config.Bind("Tech", "ToggleKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftShift), "Painel técnico e overlays no mundo.");
            techVisible = Config.Bind("Tech", "Visible", false, "Camada técnica visível ao iniciar.");
            overlayRadius = Config.Bind("Tech", "Radius", 9f, "Raio (m) em volta do jogador para colisores, dock points e hitboxes.");
            showColliders = Config.Bind("Tech", "Colliders", true, "Desenhar colisores.");
            showPaths = Config.Bind("Tech", "Paths", true, "Desenhar rotas de movimento.");
            showDocks = Config.Bind("Tech", "DockPoints", true, "Desenhar dock points.");
            showHitboxes = Config.Bind("Tech", "Hitboxes", true, "Desenhar hurtboxes e hitboxes de arma.");
            fontSize = Config.Bind("General", "FontSize", 14, "Tamanho da fonte dos painéis.");
            dumpKey = Config.Bind("Dump", "DumpKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl), "Exporta dados do jogo (GameBalance e textos) em JSON.");
            telemetryEnabled = Config.Bind("Telemetry", "Enabled", true, "Mede desempenho e estado do jogo; envia à VPS quando há token.");
            telemetryUrl = Config.Bind("Telemetry", "Url", "https://metrics-ingest.example.com", "Base do vmauth (import Prometheus e jsonline).");
            telemetryToken = Config.Bind("Telemetry", "Token", "", "Bearer do usuário gk2-client no vmauth. Vazio = só mede, não envia.");
            Warned.Clear();
            if (telemetryEnabled.Value)
            {
                telemetry = new Telemetry.Telemetry(telemetryUrl.Value, telemetryToken.Value);
                telemetry.LayersVisible = () => (localVisible.Value, techVisible.Value);
            }
            hud = new Hud(telemetry?.Cost, fontSize.Value);
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void OnDestroy()
        {
            hud?.Dispose();
            telemetry?.Dispose();
        }

        internal static void WarnOnce(string where, Exception e)
        {
            if (Warned.Add(where + e.Message)) Log.LogWarning($"{where}: {e}");
        }

        private void Update()
        {
            telemetry?.OnFrame();
            long t0 = SelfCost.Now();
            if (dumpKey.Value.IsDown()) Dumper.DumpAll();
            else if (techKey.Value.IsDown()) techVisible.Value = !techVisible.Value;
            else if (localKey.Value.IsDown()) localVisible.Value = !localVisible.Value;

            Overlay o = hud.Overlay;
            o.Radius = overlayRadius.Value;
            o.ShowColliders = showColliders.Value;
            o.ShowPaths = showPaths.Value;
            o.ShowDocks = showDocks.Value;
            o.ShowHitboxes = showHitboxes.Value;
            hud.Tick(localVisible.Value, techVisible.Value, localX.Value, localY.Value, fontSize.Value);
            telemetry?.Cost.Stop(SelfCost.Part.Update, t0);
        }
    }
}
