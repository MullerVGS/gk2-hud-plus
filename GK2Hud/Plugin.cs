using BepInEx;
using BepInEx.Configuration;
using GK2Hud.Calendar;
using GK2Hud.Hud;
using GK2Hud.Journal;
using GK2Hud.Quests;
using GK2Hud.Tooltips;
using GK2Hud.World;
using HarmonyLib;
using UnityEngine;

namespace GK2Hud
{
    /// <summary>
    /// Mod de jogador com cara nativa: tooltips enriquecidos, fixáveis e aninhados (pasta Tooltips/) e informações
    /// extras no HUD (pasta Hud/: números de energia/insanidade, placa opcional de dia/hora), tooltip de coisas em andamento
    /// no mundo (pasta World/) e calendário (pasta Calendar/: hora e popup no medalhão, aba no TAB). Cada feature tem ciclo Update/Dispose.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.hud";
        public const string Name = "GK2 Hud";
        public const string Version = "0.8.0";

        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony harmony;

        /// <summary>Acessado por reflexão nos cenários de teste (docs/cenarios/gk2hud-*.cs).</summary>
        internal TooltipFeature Tooltips { get; private set; }

        internal HudFeature HudInfo { get; private set; }

        internal WorldHoverFeature WorldHover { get; private set; }

        internal WheelFeature Wheel { get; private set; }

        internal WheelSpinFeature WheelSpin { get; private set; }

        internal CalendarTab CalendarTab { get; private set; }

        internal JournalTracker Journal { get; private set; }

        internal QuestTreeFeature Quests { get; private set; }

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
            ConfigEntry<bool> plateOn = Config.Bind("Hud", "DayPlate", false,
                "Placa fixa com dia e hora sob a felicidade (desde a 0.3.0 a hora aparece no hover do medalhão; Clock/WeekDay escolhem o conteúdo da placa).");
            ConfigEntry<bool> clockOn = Config.Bind("Hud", "Clock", true, "Hora do dia (relógio de 24 h derivado do timeOfDay) na placa sob a felicidade.");
            ConfigEntry<bool> weekDayOn = Config.Bind("Hud", "WeekDay", true, "Nome do dia da semana na placa sob a felicidade.");
            ConfigEntry<int> clockStep = Config.Bind("Hud", "ClockStepMinutes", 10,
                new ConfigDescription("Passo do relógio em minutos de jogo (1 minuto de jogo ≈ 0,3 s real).", new AcceptableValueRange<int>(1, 60)));
            ConfigEntry<HudNumbersMode> barsMode = HudConfig.BindNumbersMode(Config);
            ConfigEntry<bool> barsMax = Config.Bind("Hud", "EnergyInsanityShowMax", false, "Mostra atual/máximo em vez de só o atual.");
            ConfigEntry<float> fade = Config.Bind("Hud", "FadeDuration", 0.25f, new ConfigDescription(
                "Segundos do fade ao aparecer/sumir (placa e números). 0,25 = o fade do HUD do jogo (UISaveOverlay); 0 = sem animação.",
                new AcceptableValueRange<float>(0f, 2f)));
            ConfigEntry<bool> wheelSpinOn = Config.Bind("Hud", "WheelRotation", true,
                "Medalhão dos dias gira com a hora: cada dia tem sua fatia (divisórias de bronze) e passa sob o seletor de baixo, que fica parado; ao meio-dia tudo fica na posição do jogo.");
            if (hudOn.Value)
            {
                HudInfo = new HudFeature(plateOn, clockOn, weekDayOn, barsMode, barsMax, clockStep, fade);
                WheelSpin = new WheelSpinFeature(wheelSpinOn);
            }
            ConfigEntry<bool> calendarOn = Config.Bind("Calendar", "Enabled", true, "Calendário: aba no TAB, hora e popup no medalhão dos dias.");
            ConfigEntry<HudNumbersMode> wheelClock = Config.Bind("Calendar", "WheelClockMode", HudNumbersMode.Hover,
                "Hora em cima do medalhão. Hover: com o mouse sobre o medalhão; Always: sempre; Off: nunca.");
            ConfigEntry<bool> wheelPopup = Config.Bind("Calendar", "WheelPopup", true,
                "Mouse parado sobre o medalhão mostra a semana; sobre um sigilo, o que acontece naquele dia.");
            ConfigEntry<float> wheelDelay = Config.Bind("Calendar", "HoverDelay", 0.35f,
                new ConfigDescription("Segundos com o mouse parado sobre o medalhão até o popup aparecer.", new AcceptableValueRange<float>(0f, 5f)));
            ConfigEntry<bool> tabOn = Config.Bind("Calendar", "Tab", true, "Aba Calendário na janela do TAB.");
            ConfigEntry<bool> journalOn = Config.Bind("Calendar", "Journal", true,
                "Diário por save (desbloqueios e quests de cada dia) no calendário e nos tooltips. Fica em BepInEx/config/" + Guid + "/; o save do jogo não é tocado.");
            if (calendarOn.Value)
            {
                if (journalOn.Value) Journal = new JournalTracker();
                Wheel = new WheelFeature(wheelClock, clockStep, wheelDelay, fade, wheelPopup);
                if (tabOn.Value) CalendarTab = new CalendarTab(harmony);
            }
            ConfigEntry<bool> questsOn = Config.Bind("Quests", "Enabled", true,
                "Árvore de quests: nome do NPC sob cada nó, tooltip com retrato, lugar atual do NPC, data e passos anteriores; o botão de fixar abre a ficha do NPC.");
            if (questsOn.Value) Quests = new QuestTreeFeature(harmony);
            leakSweeper.Sweep();   // vazamentos da 0.1.0 (o hotfix entra por hot-reload, com o jogo aberto)
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void Update()
        {
            leakSweeper.Tick();
            Tooltips?.Update();
            WorldHover?.Update();
            HudInfo?.Update();
            WheelSpin?.Update();
            Journal?.Update();
            Wheel?.Update();
            CalendarTab?.Update();
        }

        private void OnDestroy()
        {
            Quests?.Dispose();
            Quests = null;
            WorldHover?.Dispose();
            WorldHover = null;
            Tooltips?.Dispose();
            Tooltips = null;
            HudInfo?.Dispose();
            HudInfo = null;
            WheelSpin?.Dispose();
            WheelSpin = null;
            Wheel?.Dispose();
            Wheel = null;
            CalendarTab?.Dispose();
            CalendarTab = null;
            Journal?.Dispose();
            Journal = null;
            harmony?.UnpatchSelf();
        }
    }
}
