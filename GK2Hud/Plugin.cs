using BepInEx;
using BepInEx.Configuration;
using GK2Hud.Hud;
using GK2Hud.Tooltips;
using GK2Hud.World;
using HarmonyLib;
using UnityEngine;

namespace GK2Hud
{
    /// <summary>
    /// Mod de jogador com cara nativa: tooltips enriquecidos, fixáveis e aninhados (pasta Tooltips/) e informações
    /// extras no HUD (pasta Hud/: dia da semana, hora, números de energia/insanidade) e tooltip de coisas em andamento no
    /// mundo (pasta World/). Cada feature tem ciclo Update/Dispose.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.hud";
        public const string Name = "GK2 Hud";
        public const string Version = "0.2.2";

        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony harmony;

        /// <summary>Acessado por reflexão nos cenários de teste (docs/cenarios/gk2hud-*.cs).</summary>
        internal TooltipFeature Tooltips { get; private set; }

        internal HudFeature HudInfo { get; private set; }

        internal WorldHoverFeature WorldHover { get; private set; }

        private readonly LeakSweeper leakSweeper = new LeakSweeper();

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
            ConfigEntry<bool> worldOn = Config.Bind("World", "Enabled", true,
                "Tooltip ao parar o mouse sobre algo em andamento no mundo (craft, plantação, corpo...). O botão de fixar dos tooltips também fixa este.");
            ConfigEntry<float> worldDelay = Config.Bind("World", "HoverDelay", 0.5f,
                new ConfigDescription("Segundos com o mouse parado sobre o objeto até o tooltip aparecer.", new AcceptableValueRange<float>(0f, 5f)));
            if (worldOn.Value)
            {
                WorldHover = new WorldHoverFeature(worldDelay);
            }
            ConfigEntry<bool> hudOn = Config.Bind("Hud", "Enabled", true, "Informações extras no HUD (dia da semana, hora, números de energia e insanidade).");
            ConfigEntry<bool> clockOn = Config.Bind("Hud", "Clock", true, "Hora do dia (relógio de 24 h derivado do timeOfDay) na placa sob a felicidade.");
            ConfigEntry<bool> weekDayOn = Config.Bind("Hud", "WeekDay", true, "Nome do dia da semana na placa sob a felicidade.");
            ConfigEntry<int> clockStep = Config.Bind("Hud", "ClockStepMinutes", 10,
                new ConfigDescription("Passo do relógio em minutos de jogo (1 minuto de jogo ≈ 0,3 s real).", new AcceptableValueRange<int>(1, 60)));
            ConfigEntry<HudNumbersMode> barsMode = HudConfig.BindNumbersMode(Config);
            ConfigEntry<bool> barsMax = Config.Bind("Hud", "EnergyInsanityShowMax", false, "Mostra atual/máximo em vez de só o atual.");
            ConfigEntry<float> fade = Config.Bind("Hud", "FadeDuration", 0.25f, new ConfigDescription(
                "Segundos do fade ao aparecer/sumir (placa e números). 0,25 = o fade do HUD do jogo (UISaveOverlay); 0 = sem animação.",
                new AcceptableValueRange<float>(0f, 2f)));
            if (hudOn.Value)
            {
                HudInfo = new HudFeature(clockOn, weekDayOn, barsMode, barsMax, clockStep, fade);
            }
            leakSweeper.Sweep();   // vazamentos da 0.1.0 (o hotfix entra por hot-reload, com o jogo aberto)
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void Update()
        {
            leakSweeper.Tick();
            Tooltips?.Update();
            WorldHover?.Update();
            HudInfo?.Update();
        }

        private void OnDestroy()
        {
            WorldHover?.Dispose();
            WorldHover = null;
            Tooltips?.Dispose();
            Tooltips = null;
            HudInfo?.Dispose();
            HudInfo = null;
            harmony?.UnpatchSelf();
        }
    }
}
