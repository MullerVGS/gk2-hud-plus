using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace GK2Insight
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.insight";
        public const string Name = "GK2 Insight";
        public const string Version = "0.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        private const string BuildStamp = "D";

        private ConfigEntry<KeyboardShortcut> hudKey;
        private ConfigEntry<KeyboardShortcut> dumpKey;
        private ConfigEntry<KeyboardShortcut> detailKey;
        private ConfigEntry<bool> hudVisible;
        private ConfigEntry<float> hudX;
        private ConfigEntry<float> hudY;
        private ConfigEntry<int> fontSize;

        private Hud hud;

        private void Awake()
        {
            Log = Logger;
            hudKey = Config.Bind("HUD", "ToggleKey", new KeyboardShortcut(KeyCode.F7), "Mostra/esconde o painel de informações.");
            hudVisible = Config.Bind("HUD", "Visible", true, "Painel visível ao iniciar.");
            hudX = Config.Bind("HUD", "X", 12f, "Posição horizontal do painel (pixels).");
            hudY = Config.Bind("HUD", "Y", 190f, "Posição vertical do painel (pixels).");
            fontSize = Config.Bind("HUD", "FontSize", 15, "Tamanho da fonte do painel.");
            detailKey = Config.Bind("HUD", "DetailKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftShift), "Alterna o modo detalhado (todos os recursos internos do jogador).");
            dumpKey = Config.Bind("Dump", "DumpKey", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftControl), "Exporta dados do jogo (GameBalance e textos) em JSON.");
            hud = new Hud();
            Log.LogInfo($"{Name} {Version} carregado (build {BuildStamp})");
        }

        private void Update()
        {
            if (dumpKey.Value.IsDown())
            {
                Dumper.DumpAll();
            }
            else if (detailKey.Value.IsDown())
            {
                hud.Detailed = !hud.Detailed;
                hudVisible.Value = true;
            }
            else if (hudKey.Value.IsDown())
            {
                hudVisible.Value = !hudVisible.Value;
            }
            if (hudVisible.Value)
            {
                hud.Refresh();
            }
        }

        private void OnGUI()
        {
            if (hudVisible.Value)
            {
                hud.Draw(hudX.Value, hudY.Value, fontSize.Value);
            }
        }
    }
}
