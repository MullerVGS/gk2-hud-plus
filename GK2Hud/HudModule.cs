using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Hud.Calendar;
using GK2Hud.Hud;
using GK2Hud.Journal;
using GK2Hud.Quests;
using GK2Hud.Tooltips;
using GK2Hud.World;
using GK2Shared;
using UnityEngine;

namespace GK2Hud
{
    /// <summary>
    /// Interface com cara nativa: tooltips enriquecidos, fixáveis e aninhados (Tooltips/), números no HUD e medalhão
    /// (Hud/), tooltip de coisas em andamento no mundo (World/), calendário e diário (Calendar/, Journal/) e árvore de
    /// quests (Quests/). Cada funcionalidade liga e desliga sozinha pelo <see cref="ModHost"/>.
    /// </summary>
    internal sealed class HudModule
    {
        /// <summary>GUID do mod avulso: nome do .cfg antigo e da pasta do diário em BepInEx/config.</summary>
        public const string LegacyGuid = "arthur.gk2.hud";

        internal static ManualLogSource Log;
        internal static HudModule Instance;

        internal readonly ModHost Host;
        internal ConfigFile Config => Host.Config;
        internal ModGuard Guard => Host.Guard;

        /// <summary>Acessado por reflexão nos cenários de teste (docs/cenarios/gk2hud-*.cs).</summary>
        internal TooltipFeature Tooltips { get; private set; }
        internal HudFeature HudInfo { get; private set; }
        internal WorldHoverFeature WorldHover { get; private set; }
        internal WheelFeature Wheel { get; private set; }
        internal WheelSpinFeature WheelSpin { get; private set; }
        internal CalendarTab CalendarTab { get; private set; }
        internal JournalTracker Journal { get; private set; }
        internal QuestTreeFeature Quests { get; private set; }

        /// <summary>Pasta do diário (por save) em BepInEx/config.</summary>
        internal string DataDir => Host.DataDir(LegacyGuid, null);

        private readonly LeakSweeper leakSweeper = new LeakSweeper();

        public HudModule(ModHost host)
        {
            Host = host;
            Log = host.Log;
            Instance = this;
            host.Module("hud", leakSweeper.Tick, () => { if (Instance == this) Instance = null; });
            ConfigFile cfg = host.Config;

            // ---- Tooltips
            ConfigEntry<KeyboardShortcut> pinKey = cfg.Bind("Tooltips", "PinKey", new KeyboardShortcut(KeyCode.Mouse2),
                "Pins the visible tooltip (or closes the pinned bubble under the mouse). Default: middle mouse button (T is the game's Tech Tree).");
            ConfigEntry<float> linkDelay = cfg.Bind("Tooltips", "LinkHoverDelay", 0.25f, "Seconds on a highlighted term before its nested tooltip opens.");
            ConfigEntry<int> maxDepth = cfg.Bind("Tooltips", "MaxDepth", 6, "Most pinned bubbles stacked at once.");
            host.Feature("Tooltips", "tooltips", "Tooltips", "Enabled",
                new Loc("Richer tooltips", "Tooltips com mais informação"),
                new Loc("Item, station and character tooltips with more numbers. The middle mouse button pins a tooltip; highlighted terms open nested ones.",
                    "Tooltips de itens, estações e personagens com mais números. O botão do meio fixa um tooltip; termos destacados abrem outros dentro dele."),
                h => Tooltips = new TooltipFeature(h, pinKey, linkDelay, maxDepth),
                f => f.Update(), f => { f.Dispose(); Tooltips = null; }, options: new ConfigEntryBase[] { pinKey, linkDelay, maxDepth });

            ConfigEntry<float> worldDelay = cfg.Bind("World", "HoverDelay", 0.5f,
                new ConfigDescription("Seconds with the mouse still on an object before its tooltip shows.", new AcceptableValueRange<float>(0f, 5f)));
            host.Feature("World", "tooltips", "World", "Enabled",
                new Loc("Tooltips in the world", "Tooltips no mundo"),
                new Loc("Resting the mouse on something in progress (craft, garden bed, body...) shows how it is going and how long is left. The pin button pins it too.",
                    "Parar o mouse sobre algo em andamento (craft, canteiro, corpo...) mostra como vai e quanto falta. O botão de fixar também fixa este."),
                h => WorldHover = new WorldHoverFeature(worldDelay),
                f => f.Update(), f => { f.Dispose(); WorldHover = null; }, options: worldDelay);

            // ---- HUD
            ConfigEntry<int> clockStep = cfg.Bind("Hud", "ClockStepMinutes", 10,
                new ConfigDescription("Clock step in game minutes (1 game minute is about 0.3 s).", new AcceptableValueRange<int>(1, 60)));
            ConfigEntry<HudNumbersMode> barsMode = HudConfig.BindNumbersMode(cfg);
            ConfigEntry<float> fade = cfg.Bind("Hud", "FadeDuration", 0.25f, new ConfigDescription(
                "Fade time in seconds when the plate and numbers appear or hide. 0.25 is the game's own HUD fade; 0 turns the animation off.",
                new AcceptableValueRange<float>(0f, 2f)));
            FeatureInfo plate = null, clock = null, weekDay = null, showMax = null;
            FeatureInfo hud = host.Feature("Hud", "hud", "Hud", "Enabled",
                new Loc("HUD numbers", "Números no HUD"),
                new Loc("Energy and insanity numbers on the bars, and an optional plate with the day and time.",
                    "Números de energia e insanidade nas barras e uma placa opcional com o dia e a hora."),
                h => HudInfo = new HudFeature(plate.Toggle, clock.Toggle, weekDay.Toggle, barsMode, showMax.Toggle, clockStep, fade),
                f => f.Update(), f => { f.Dispose(); HudInfo = null; }, options: new ConfigEntryBase[] { barsMode, clockStep, fade });
            showMax = host.Option(hud, "HudShowMax", "Hud", "EnergyInsanityShowMax", false,
                new Loc("Show the maximum", "Mostrar o máximo"),
                new Loc("Energy and insanity as current/max instead of only the current value.", "Energia e insanidade como atual/máximo em vez de só o atual."));
            plate = host.Option(hud, "HudDayPlate", "Hud", "DayPlate", false,
                new Loc("Day plate", "Placa do dia"),
                new Loc("A fixed plate with the day and time under happiness (the time also shows when hovering the medallion).",
                    "Uma placa fixa com o dia e a hora sob a felicidade (a hora também aparece no mouse sobre o medalhão)."));
            clock = host.Option(plate, "HudClock", "Hud", "Clock", true,
                new Loc("Time on the plate", "Hora na placa"), new Loc("24-hour clock on the day plate.", "Relógio de 24 h na placa do dia."));
            weekDay = host.Option(plate, "HudWeekDay", "Hud", "WeekDay", true,
                new Loc("Week day on the plate", "Dia da semana na placa"), new Loc("Name of the week day on the day plate.", "Nome do dia da semana na placa do dia."));

            FeatureInfo spin = null;
            spin = host.Feature("WheelSpin", "hud", "Hud", "WheelRotation",
                new Loc("Turning day medallion", "Medalhão que gira"),
                new Loc("The day medallion turns with the time: each day has its slice and passes under the selector. At noon it sits where the game puts it.",
                    "O medalhão dos dias gira com a hora: cada dia tem sua fatia e passa sob o seletor. Ao meio-dia fica onde o jogo o põe."),
                h => WheelSpin = new WheelSpinFeature(spin.Toggle),
                f => f.Update(), f => { f.Dispose(); WheelSpin = null; });

            // ---- Calendário
            host.Feature("Journal", "calendar", "Calendar", "Journal",
                new Loc("Journal", "Diário"),
                new Loc("Notes what you unlock and finish each day, shown in the calendar and tooltips. Kept per save in BepInEx/config; the game save is not touched.",
                    "Anota o que você libera e conclui a cada dia, mostrado no calendário e nos tooltips. Fica por save em BepInEx/config; o save do jogo não é tocado."),
                h => Journal = new JournalTracker(),
                f => f.Update(), f => { f.Dispose(); Journal = null; });

            ConfigEntry<HudNumbersMode> wheelClock = cfg.Bind("Calendar", "WheelClockMode", HudNumbersMode.Hover,
                "Time above the medallion. Hover: with the mouse on the medallion; Always; Off.");
            ConfigEntry<float> wheelDelay = cfg.Bind("Calendar", "HoverDelay", 0.35f,
                new ConfigDescription("Seconds with the mouse still on the medallion before the popup shows.", new AcceptableValueRange<float>(0f, 5f)));
            FeatureInfo popup = null;
            FeatureInfo wheel = host.Feature("Wheel", "calendar", "Calendar", "Wheel",
                new Loc("Time and week on the medallion", "Hora e semana no medalhão"),
                new Loc("The time shows above the day medallion; resting the mouse on it shows the week, and on a sigil, what happens that day.",
                    "A hora aparece sobre o medalhão dos dias; parar o mouse nele mostra a semana e, num sigilo, o que acontece naquele dia."),
                h => Wheel = new WheelFeature(wheelClock, clockStep, wheelDelay, fade, popup.Toggle),
                f => f.Update(), f => { f.Dispose(); Wheel = null; }, options: new ConfigEntryBase[] { wheelClock, wheelDelay, clockStep, fade });
            popup = host.Option(wheel, "WheelPopup", "Calendar", "WheelPopup", true,
                new Loc("Week popup", "Popup da semana"),
                new Loc("Resting the mouse on the medallion shows the week; on a sigil, that day.", "Parar o mouse no medalhão mostra a semana; num sigilo, aquele dia."));

            host.Feature("CalendarTab", "calendar", "Calendar", "Tab",
                new Loc("Calendar tab", "Aba Calendário"),
                new Loc("A Calendar tab in the TAB window: a timeline of each day with the events you already know and, with the journal, what you did.",
                    "Uma aba Calendário na janela do TAB: a linha do tempo de cada dia com os eventos que você já conhece e, com o diário, o que você fez."),
                h => CalendarTab = new CalendarTab(h),
                f => f.Update(), f => { f.Dispose(); CalendarTab = null; });

            // ---- Quests
            ConfigEntry<float> questsDelay = cfg.Bind("Quests", "HoverDelay", 0.8f,
                new ConfigDescription("Seconds with the mouse still on a quest node before the short tooltip shows.", new AcceptableValueRange<float>(0f, 5f)));
            host.Feature("Quests", "tooltips", "Quests", "Enabled",
                new Loc("Quest tree", "Árvore de quests"),
                new Loc("NPC names under the nodes, a short tooltip on hover, and a click opens the journal page of the quest with how you got there.",
                    "Nome do NPC sob os nós, um tooltip curto no mouse e o clique abre a página do diário da quest com o caminho até ela."),
                h => Quests = new QuestTreeFeature(h, questsDelay),
                f => f.Update(), f => { f.Dispose(); Quests = null; }, options: questsDelay);

            host.ImportLegacy(LegacyGuid, old =>
            {
                // No mod avulso, [Calendar] Enabled ligava diário, medalhão e aba juntos, e [Hud] Enabled incluía o giro.
                if (old.TryGetValue("Calendar\nEnabled", out string cal) && cal.Trim().ToLowerInvariant() == "false")
                    foreach (string id in new[] { "Journal", "Wheel", "CalendarTab" }) host.Find(id).Toggle.Value = false;
                if (old.TryGetValue("Hud\nEnabled", out string h) && h.Trim().ToLowerInvariant() == "false") host.Find("WheelSpin").Toggle.Value = false;
            });
            leakSweeper.Sweep();   // vazamentos da 0.1.0 (o hotfix entra por hot-reload, com o jogo aberto)
        }
    }
}
