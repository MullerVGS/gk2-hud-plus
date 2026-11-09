using BepInEx;
using BepInEx.Configuration;
using GK2Hud.Tooltips;
using HarmonyLib;
using UnityEngine;

namespace GK2Hud
{
    /// <summary>
    /// Mod de jogador com cara nativa. Hoje: tooltips enriquecidos, fixáveis e aninhados (pasta Tooltips/).
    /// A HUD (hora, números de energia/sanidade) entra como outra feature ao lado, com o mesmo ciclo Update/Dispose.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.hud";
        public const string Name = "GK2 Hud";
        public const string Version = "0.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony harmony;

        /// <summary>Acessado por reflexão nos cenários de teste (docs/cenarios/gk2hud-*.cs).</summary>
        internal TooltipFeature Tooltips { get; private set; }

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony(Guid);
            ConfigEntry<bool> tooltipsOn = Config.Bind("Tooltips", "Enabled", true, "Tooltips com informação extra, fixáveis e aninhados.");
            ConfigEntry<KeyboardShortcut> pinKey = Config.Bind("Tooltips", "PinKey", new KeyboardShortcut(KeyCode.Mouse2),
                "Fixa o tooltip visível (ou fecha a bolha fixada sob o mouse). Padrão: botão do meio. T é a Tech Tree do jogo.");
            ConfigEntry<float> hoverDelay = Config.Bind("Tooltips", "LinkHoverDelay", 0.25f, "Segundos com o mouse num termo destacado até abrir o tooltip aninhado.");
            ConfigEntry<int> maxDepth = Config.Bind("Tooltips", "MaxDepth", 6, "Máximo de bolhas fixadas empilhadas.");
            if (tooltipsOn.Value)
            {
                Tooltips = new TooltipFeature(harmony, pinKey, hoverDelay, maxDepth);
            }
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void Update()
        {
            Tooltips?.Update();
        }

        private void OnDestroy()
        {
            Tooltips?.Dispose();
            Tooltips = null;
            harmony?.UnpatchSelf();
        }
    }
}
