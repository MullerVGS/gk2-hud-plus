using BepInEx;
using BepInEx.Configuration;
using GK2Hud.Hud;
using GK2Hud.Tooltips;
using HarmonyLib;
using UnityEngine;

namespace GK2Hud
{
    /// <summary>
    /// Mod de jogador com cara nativa: tooltips enriquecidos, fixáveis e aninhados (pasta Tooltips/) e informações
    /// extras no HUD (pasta Hud/: dia da semana, hora, números de energia/insanidade). Cada feature tem ciclo Update/Dispose.
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

        internal HudFeature HudInfo { get; private set; }

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
            ConfigEntry<bool> hudOn = Config.Bind("Hud", "Enabled", true, "Informações extras no HUD (dia da semana, hora, números de energia e insanidade).");
            ConfigEntry<bool> clockOn = Config.Bind("Hud", "Clock", true, "Hora do dia (relógio de 24 h derivado do timeOfDay) na placa sob a felicidade.");
            ConfigEntry<bool> weekDayOn = Config.Bind("Hud", "WeekDay", true, "Nome do dia da semana na placa sob a felicidade.");
            ConfigEntry<int> clockStep = Config.Bind("Hud", "ClockStepMinutes", 10,
                new ConfigDescription("Passo do relógio em minutos de jogo (1 minuto de jogo ≈ 0,3 s real).", new AcceptableValueRange<int>(1, 60)));
            ConfigEntry<bool> barsOn = Config.Bind("Hud", "EnergyInsanityNumbers", true, "Números de energia e insanidade dentro da barra.");
            ConfigEntry<bool> barsMax = Config.Bind("Hud", "EnergyInsanityShowMax", false, "Mostra atual/máximo em vez de só o atual.");
            if (hudOn.Value)
            {
                HudInfo = new HudFeature(clockOn, weekDayOn, barsOn, barsMax, clockStep);
            }
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void Update()
        {
            Tooltips?.Update();
            HudInfo?.Update();
        }

        private void OnDestroy()
        {
            Tooltips?.Dispose();
            Tooltips = null;
            HudInfo?.Dispose();
            HudInfo = null;
            harmony?.UnpatchSelf();
        }
    }
}
