using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Um acontecimento da semana. Os horários vêm do agendador do jogo (<c>gameLogicsDefs</c>): eventos de dia rodam no
    /// <c>dayNumber</c> a partir de <c>dayTime</c>; os periódicos (burro, freira) guardam a próxima execução no save
    /// (<c>GameLogicData.execDay/execTime</c>) e repetem a cada <c>periodTime</c> dias.
    /// </summary>
    internal sealed class EventDef
    {
        public string Id;
        public string DayId;          // dia fixo (day_*); null = periódico
        public string LogicId;        // periódico: id no agendador
        public float Start = -1f;     // timeOfDay de início; < 0 = dia todo sem hora conhecida
        public float End = -1f;       // timeOfDay de fim; < 0 = instantâneo (só Start)
        public string[] MapIcons = new string[0];   // ícones de dia do mapa que revelam o evento
        public string[] Tutorials = new string[0];  // tutoriais (cabeçalho) que revelam o evento
        public string[] Quests = new string[0];     // quests concluídas que revelam o evento (a partir do dia em que concluíram)
        public string[] Phrases = new string[0];    // respostas de diálogo já ouvidas (blackListPhrases) que revelam o evento
        public string ResFlag;        // PlayerData.res que precisa ser > 0 (lido cru)
        public string Icon;           // sprite do ícone no TAB (os do mapa: day_*_expanded)
        public string NativeDesc;     // descrição com texto do próprio jogo (LLBase.L)
        public bool PowerStatus;      // mostra se a ressurreição tem energia
        public bool NoClock;          // a hora não interessa (renovação logo após a meia-noite)

        public string Title => CalendarL10n.T("ev_" + Id);

        public string Description
        {
            get
            {
                string own = CalendarL10n.TOrNull("ev_" + Id + "_d");
                if (own != null) return own;
                if (NativeDesc == null) return null;
                return Names.Text(NativeDesc);
            }
        }
    }

    /// <summary>Uma ocorrência concreta: dia absoluto e janela em timeOfDay.</summary>
    internal struct Occurrence
    {
        public EventDef Def;
        public int Day;
        public float Start;   // < 0 = dia todo
        public float End;     // < 0 = instantâneo

        public bool AllDay => Start < 0f;
        public bool Instant => !AllDay && End < 0f;
        public double StartAbs => Day + (AllDay ? 0f : Start);
        public double EndAbs => Day + (AllDay ? 1f : Instant ? Start : End);
    }

    internal enum OccState { Upcoming, Active, Past }

    /// <summary>Previsão da viagem do burro (ver <see cref="CalendarModel.Donkey"/>).</summary>
    internal struct DonkeyTrip
    {
        public bool Locked;     // a Freira ouviu "nenhum corpo"
        public bool Busy;       // o burro vai ao necrotério sem entrega (missão em andamento)
        public bool Full;       // necrotério cheio: o jogo pula a viagem
        public int Bodies, Capacity;
        public int Count;       // corpos se a chance passar
        public int MaxCount;    // corpos por viagem com vaga sobrando
        public int ChancePct;
    }

    /// <summary>
    /// Calendário da semana, só leitura. Sem spoiler: cada evento só aparece quando o jogo já o revelou ao jogador
    /// (ícone de dia liberado no mapa, tutorial aberto ou o recurso do jogador que liga o evento).
    /// </summary>
    internal static class CalendarModel
    {
        public const int DaysInWeek = 6;

        /// <summary>Teste: mostra tudo, ignorando o conhecimento do jogador (cenário gk2hud-calendario-*.cs).</summary>
        internal static bool RevealAll = false;

        private static readonly AccessTools.FieldRef<PlayerData, GameRes> PlayerRes = AccessTools.FieldRefAccess<PlayerData, GameRes>("res");

        // Ordem dentro do dia: a do horário; empate pela ordem desta lista.
        internal static readonly EventDef[] Defs =
        {
            new EventDef { Id = "orders", DayId = "day_pride", Start = 0.01f, End = 0.99f, MapIcons = new[] { "day_pride" },
                Tutorials = new[] { "tut_commerce_hdr" }, Icon = "day_pride_expanded", NativeDesc = "tut_commerce_day" },
            new EventDef { Id = "shops", DayId = "day_pride", Start = 0.01f, NoClock = true, Tutorials = new[] { "tut_trading_hdr", "tut_commerce_hdr" },
                Icon = "day_pride_expanded", NativeDesc = "tt_trade_hap_likes" },
            new EventDef { Id = "ship", DayId = "day_lust", MapIcons = new[] { "day_lust" }, Icon = "day_lust_expanded",
                NativeDesc = "35_port_pop_new_people_4" },
            new EventDef { Id = "linda", DayId = "day_lust", Tutorials = new[] { "tut_town_hdr" }, Icon = "day_lust_expanded",
                NativeDesc = "tut_town_houses" },
            new EventDef { Id = "pr_machine", DayId = "day_gluttony", MapIcons = new[] { "day_gluttony" }, Tutorials = new[] { "tut_pr_machine_hdr" },
                Icon = "day_gluttony_expanded", NativeDesc = "tut_pr_machine_charge" },
            new EventDef { Id = "resurrection", DayId = "day_envy", Start = 0.01f, End = 0.99f, MapIcons = new[] { "day_envy_morgue" },
                Tutorials = new[] { "tut_resurrection_hdr" }, Icon = "day_envy_expanded", NativeDesc = "tut_resurrection_day", PowerStatus = true },
            // Nenhum ícone do mapa marca a Deusa: quem revela é ouvir a resposta 63_swamp_goddess_32_c da estátua, a conversa
            // que tem a fala das lágrimas (63_swamp_goddess_32_c_3).
            new EventDef { Id = "goddess", DayId = "day_envy", Phrases = new[] { "63_swamp_goddess_32_c" },
                Icon = "day_envy_expanded", NativeDesc = "63_swamp_goddess_32_c_3" },
            new EventDef { Id = "sermon", DayId = "day_wrath", Start = 0.01f, End = 0.99f, MapIcons = new[] { "day_wrath_church" },
                Tutorials = new[] { "tutorial_hdr_seremons" }, Icon = "day_wrath_expanded", NativeDesc = "tut_sermons_day" },
            // Nenhum ícone do mapa marca a freira (day_wrath nunca é liberado): revela a conversa "Corpse Delivery"
            // (opção 51_crossroad_fixed_delivery_1, marcada como ouvida ao concluir 51_crossroad_fixed_corpse_delivery).
            new EventDef { Id = "nun", DayId = "day_wrath", Phrases = new[] { "51_crossroad_fixed_delivery_1" },
                Quests = new[] { "51_crossroad_fixed_corpse_delivery" }, Icon = "day_wrath_expanded",
                NativeDesc = "51_crossroad_fixed_delivery_6" },
            new EventDef { Id = "battle", DayId = "day_sloth", Start = 0.01f, End = 0.99f,
                Tutorials = new[] { "tut_battle_1_hdr", "tut_battle_2_hdr" }, Icon = "day_sloth_expanded", NativeDesc = "battle_planer_other_day" },
            new EventDef { Id = "donkey", LogicId = "donkey", ResFlag = "donkey_ready", Icon = null },
            new EventDef { Id = "nun_body", LogicId = "nun_body_drop", ResFlag = "nun_body_drop_ready", Icon = null },
        };

        private static readonly Dictionary<int, string> dayIdByNumber = new Dictionary<int, string>();
        private static readonly Dictionary<string, int> numberByDayId = new Dictionary<string, int>();

        public static bool Ready => MainGame.Instance != null && MainGame.Instance.GameSave != null && MainGame.PlayerData != null
                                    && EnvironmentEngine.Instance != null;

        public static EnvironmentData Env => MainGame.Instance.GameSave.environmentData;

        /// <summary>Agora em dias absolutos (dia + fração).</summary>
        public static double Now => Env.Day + Env.TimeOfDay;

        /// <summary>Segundos reais por dia de jogo acordado (o sono acelera 50×).</summary>
        public static float DaySeconds => Mathf.Max(1f, EnvironmentEngine.Instance.gameplayDayInMinutes * 60f);

        public static string DayIdOf(int absDay)
        {
            EnsureDays();
            dayIdByNumber.TryGetValue(Env.GetDayNumberFromDay(absDay), out string id);
            return id;
        }

        private static void EnsureDays()
        {
            if (dayIdByNumber.Count == DaysInWeek) return;
            dayIdByNumber.Clear();
            numberByDayId.Clear();
            foreach (string id in LazyConsts.ConstDefs.AllDays)
            {
                int n = ConstDef.Get(id).IntValue;
                dayIdByNumber[n] = id;
                numberByDayId[id] = n;
            }
        }

        /// <summary>Próximo dia absoluto (a partir de hoje, inclusive) que cai no dia da semana pedido.</summary>
        public static int NextDayOf(string dayId, int fromDay)
        {
            for (int d = fromDay; d < fromDay + DaysInWeek; d++)
                if (DayIdOf(d) == dayId) return d;
            return fromDay;
        }

        public static bool Known(EventDef e)
        {
            if (RevealAll) return true;
            PlayerData pd = MainGame.PlayerData;
            if (e.ResFlag != null)
            {
                GameRes res = pd != null ? PlayerRes(pd) : null;
                return res != null && res.GetWithoutSystemsCheck(e.ResFlag) > 0f;
            }
            KnowledgeSystem ks = MainGame.Instance.GameSave.knowledgeSystem;
            if (ks == null) return false;
            foreach (string icon in e.MapIcons) if (ks.IsMapDayIconUnlocked(icon)) return true;
            if (ks.unlockedTutorials != null)
                foreach (string t in e.Tutorials) if (ks.unlockedTutorials.Contains(t)) return true;
            if (ks.blackListPhrases != null)
                foreach (string p in e.Phrases) if (ks.blackListPhrases.Contains(p)) return true;
            return QuestRevealed(e);
        }

        private static bool QuestRevealed(EventDef e)
        {
            if (e.Quests.Length == 0) return false;
            List<QuestData> qs = MainGame.Instance.GameSave.questSystemData?.questCollection?.quests;
            return qs != null && qs.Exists(q => q != null && q.status == QuestStatus.Completed && Array.IndexOf(e.Quests, q.id) >= 0);
        }

        /// <summary>
        /// Primeiro dia em que o evento conta. Revelado só por quest ou resposta de diálogo: o dia em que o diário anotou a conclusão (antes do
        /// diário ou sem diário = sempre). Revelado pelo jogo (ícone/tutorial/res): sempre.
        /// </summary>
        public static int KnownSince(EventDef e)
        {
            if (RevealAll || (e.Quests.Length == 0 && e.Phrases.Length == 0)) return int.MinValue;
            KnowledgeSystem ks = MainGame.Instance.GameSave.knowledgeSystem;
            if (ks != null)
            {
                foreach (string icon in e.MapIcons) if (ks.IsMapDayIconUnlocked(icon)) return int.MinValue;
                if (ks.unlockedTutorials != null) foreach (string t in e.Tutorials) if (ks.unlockedTutorials.Contains(t)) return int.MinValue;
            }
            Journal.JournalTracker j = Journal.JournalTracker.Current;
            if (j == null || !j.Active) return int.MinValue;
            int since = int.MaxValue;
            foreach (var key in e.Quests.Select(q => (Journal.JournalKinds.Quest, q)).Concat(e.Phrases.Select(p => (Journal.JournalKinds.Phrase, p))))
            {
                Journal.JournalEntry en = j.Find(key.Item1, key.Item2, out bool baseline);
                if (baseline) return int.MinValue;
                if (en != null) since = Math.Min(since, en.d);
            }
            return since == int.MaxValue ? int.MinValue : since;
        }

        /// <summary>A ressurreição tem energia? (res cru <c>resurrection_has_power</c>; recarrega no fim de Luxúria.)</summary>
        public static bool ResurrectionPowered()
        {
            GameRes res = PlayerRes(MainGame.PlayerData);
            return res != null && res.GetWithoutSystemsCheck("resurrection_has_power") > 0f;
        }

        /// <summary>
        /// Viagem do burro com o estado de agora, pela regra do grafo <c>npc_donkey</c> (<c>full_cycle</c>): nada com
        /// <c>global_lock_on_drop_bodies</c> (a Freira ouviu "nenhum corpo") ou durante <c>33_base_complain_go</c>;
        /// <c>Flow_Body</c> pula com o necrotério cheio e rola <c>donkey_body_drop_chance</c> contra <c>Random.Range(0, 100)</c>;
        /// <c>Flow_GetCorpseDropAmount</c> traz 1 (+<c>donkey_drop_additional</c> se a Freira ouviu "muitos corpos"),
        /// limitado às vagas. A capacidade é somada à mão: <c>GetTotalQuality</c> grava no save.
        /// </summary>
        public static DonkeyTrip Donkey()
        {
            var t = new DonkeyTrip();
            GameRes res = PlayerRes(MainGame.PlayerData);
            WorldZoneData morgue = MainGame.WorldData?.GetWorldZoneDataById("morgue");
            if (res == null || morgue == null) return t;
            t.Locked = res.GetWithoutSystemsCheck("global_lock_on_drop_bodies") > 0f;
            List<QuestData> qs = MainGame.Instance.GameSave.questSystemData?.questCollection?.quests;
            t.Busy = qs != null && qs.Exists(q => q != null && q.id == "33_base_complain_go" && q.status == QuestStatus.InProgress);
            t.Bodies = Mathf.RoundToInt(res.GetWithoutSystemsCheck("cur_bodies_count"));
            t.Capacity = morgue.IsContainer ? (int)ZoneQuality(morgue) : 0;
            t.Full = t.Bodies >= t.Capacity;
            int extra = 0;
            ConstDef add = ConstDef.Get("donkey_drop_additional");
            if (res.GetWithoutSystemsCheck("nun_many_body_drop") > 0f && add != null)
                extra = add.IntValue != 0 ? add.IntValue : Mathf.RoundToInt(add.FloatValue);
            t.MaxCount = 1 + extra;
            t.Count = Mathf.Min(t.MaxCount, Mathf.Max(1, t.Capacity - t.Bodies));
            t.ChancePct = Mathf.Clamp(Mathf.FloorToInt(res.GetWithoutSystemsCheck("donkey_body_drop_chance")) + 1, 0, 100);
            return t;
        }

        /// <summary>A ocorrência é a próxima viagem agendada no save (<c>GameLogicData.execDay/execTime</c>) e ainda não saiu?</summary>
        public static bool IsNextRun(Occurrence o)
        {
            if (o.Def.LogicId == null || State(o, Now) != OccState.Upcoming) return false;
            GameLogicData g = MainGame.Instance.GameSave.gameLogicSystemData?.gameLogics?.Find(x => x.id == o.Def.LogicId);
            return g != null && Math.Abs(o.StartAbs - (g.execDay + (double)g.execTime)) < 1e-3;
        }

        private static float ZoneQuality(WorldZoneData wz)
        {
            List<SGuid> ids = wz.Definition != null && wz.Definition.hasCustomQualityZones ? wz.customQualityWgoDataList : wz.wgoDataList;
            float total = wz.additionalQuality;
            if (ids == null) return total;
            foreach (SGuid id in ids)
            {
                WgoData w = MainGame.WorldData.GetWgoData(id);
                if (w != null) total += w.Quality;
            }
            return total;
        }

        /// <summary>Ocorrências conhecidas num dia absoluto, em ordem de horário.</summary>
        public static List<Occurrence> OnDay(int absDay)
        {
            var list = new List<Occurrence>();
            string dayId = DayIdOf(absDay);
            List<GameLogicData> logics = MainGame.Instance.GameSave.gameLogicSystemData?.gameLogics;
            foreach (EventDef e in Defs)
            {
                if (!Known(e) || absDay < KnownSince(e)) continue;
                if (e.DayId != null)
                {
                    if (e.DayId == dayId) list.Add(new Occurrence { Def = e, Day = absDay, Start = e.Start, End = e.End });
                    continue;
                }
                GameLogicData g = logics?.Find(x => x.id == e.LogicId);
                if (g == null || g.Definition == null) continue;
                float period = g.Definition.periodTime;
                double next = g.execDay + (double)g.execTime;
                if (period <= 0f) continue;
                // A execução que já passou hoje também conta (mostra "passou").
                while (next - period >= absDay) next -= period;
                for (double t = next; t < absDay + 1; t += period)
                {
                    if (t < absDay) continue;
                    list.Add(new Occurrence { Def = e, Day = absDay, Start = (float)(t - absDay), End = -1f });
                }
            }
            list.Sort((a, b) =>
            {
                int c = a.StartAbs.CompareTo(b.StartAbs);
                return c != 0 ? c : Array.IndexOf(Defs, a.Def).CompareTo(Array.IndexOf(Defs, b.Def));
            });
            return list;
        }

        public static OccState State(Occurrence o, double now)
        {
            if (now < o.StartAbs) return OccState.Upcoming;
            if (o.Instant) return now - o.StartAbs < 0.02 ? OccState.Active : OccState.Past;
            return now < o.EndAbs ? OccState.Active : OccState.Past;
        }

        /// <summary>Próxima ocorrência conhecida ainda por vir (ou em andamento), dentro de uma semana.</summary>
        public static Occurrence? NextUpcoming(double now)
        {
            int today = (int)Math.Floor(now);
            for (int d = today; d < today + DaysInWeek; d++)
                foreach (Occurrence o in OnDay(d))
                    if (State(o, now) == OccState.Upcoming) return o;
            return null;
        }

        /// <summary>
        /// timeOfDay → relógio 24 h (o medalhão põe 0 = meia-noite, 0,5 = meio-dia). Horários de evento arredondam
        /// (0,55 em float dá 791,99 min = 13:12); a hora atual usa o passo do HUD (HudFeature.Clock).
        /// </summary>
        public static string Clock(float tod)
        {
            int minutes = Mathf.RoundToInt(Mathf.Repeat(tod, 1f) * 1440f) % 1440;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        /// <summary>Duração real em m:ss (ou h:mm:ss), o formato dos tempos do jogo (PerkSystemData.GetFormattedDuration).</summary>
        public static string Countdown(double days)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt((float)(days * DaySeconds)));
            if (s >= 3600) return $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}";
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
