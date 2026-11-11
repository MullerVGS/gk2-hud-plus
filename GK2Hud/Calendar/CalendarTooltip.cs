using System;
using System.Collections.Generic;
using System.Linq;
using GK2Hud.Hud;
using GK2Hud.Journal;
using GK2Hud.Tooltips;
using LazyBearTechnology;
using TMPro;
using UnityEngine.UI;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Conteúdo dos popups do medalhão no <c>UITooltip</c> nativo (TextStyles do próprio tooltip) e os textos de
    /// horário/contagem usados também pela aba do TAB.
    /// </summary>
    internal static class CalendarTooltip
    {
        /// <summary>Mini calendário: hoje e os próximos 5 dias, um por linha, e o próximo acontecimento.</summary>
        public static List<LazyWidgetDataBase> Week()
        {
            Styles s = Styles.Read();
            if (s == null || !CalendarModel.Ready) return null;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            var list = new List<LazyWidgetDataBase>
            {
                Text(DayHeader(today), s.Header, TextAlignmentOptions.Center),
                Text(CalendarL10n.T("day_n", today), s.Small, TextAlignmentOptions.Center),
                new UITooltipSeparatorWidgetData(),
            };
            for (int d = today; d < today + CalendarModel.DaysInWeek; d++)
            {
                List<Occurrence> occ = CalendarModel.OnDay(d);
                string names = occ.Count == 0 ? CalendarL10n.T("nothing_short")
                    : string.Join(" · ", occ.Where(o => d > today || CalendarModel.State(o, now) != OccState.Past).Select(o => ShortName(o)).DefaultIfEmpty(CalendarL10n.T("nothing_short")));
                string line = DayIcon(d) + " " + s.SmallSelected.ApplyStyleToString(CalendarL10n.Relative(d - today) + ":") + " " + names;
                list.Add(Text(line, s.Small, TextAlignmentOptions.Left));
            }
            Occurrence? next = CalendarModel.NextUpcoming(now);
            string reset = ResetLine(today);
            if (next.HasValue || reset != null) list.Add(new UITooltipSeparatorWidgetData());
            if (next.HasValue)
            {
                Occurrence o = next.Value;
                list.Add(Text(s.SmallSelected.ApplyStyleToString(CalendarL10n.T("next") + ":") + " " + o.Def.Title + " · " + Num(s, When(o, now)), s.Small, TextAlignmentOptions.Center));
            }
            if (reset != null) list.Add(Text(reset, s.Small, TextAlignmentOptions.Center));
            return list;
        }

        /// <summary>Um dia da semana (o próximo com esse sigilo, hoje inclusive): eventos com horário e contagem.</summary>
        public static List<LazyWidgetDataBase> Day(string dayId)
        {
            Styles s = Styles.Read();
            if (s == null || !CalendarModel.Ready) return null;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            int day = CalendarModel.NextDayOf(dayId, today);
            var list = new List<LazyWidgetDataBase>
            {
                Text(DayHeader(day), s.Header, TextAlignmentOptions.Center),
                Text(CalendarL10n.Relative(day - today) + " · " + CalendarL10n.T("day_n", day), s.Small, TextAlignmentOptions.Center),
                new UITooltipSeparatorWidgetData(),
            };
            List<Occurrence> occ = CalendarModel.OnDay(day);
            if (occ.Count == 0)
            {
                list.Add(Text(CalendarL10n.T("nothing"), s.Small, TextAlignmentOptions.Center));
                return list;
            }
            bool firstEv = true;
            foreach (Occurrence o in occ)
            {
                if (!firstEv) list.Add(new UITooltipSeparatorWidgetData());
                firstEv = false;
                bool past = CalendarModel.State(o, now) == OccState.Past;
                list.Add(Text(o.Def.Title, past ? s.Small : s.Description, TextAlignmentOptions.Center));
                list.Add(Text(When(o, now), past ? s.Small : s.SmallSelected, TextAlignmentOptions.Center));
                string desc = o.Def.Description;
                if (desc != null && !past) list.Add(Text(desc, s.Small, TextAlignmentOptions.Center));
                string extra = Extra(o);
                if (extra != null) list.Add(Text(extra, s.Small, TextAlignmentOptions.Center));
            }
            return list;
        }

        /// <summary>Uma marca da linha do tempo: o evento, quando (com contagem) e a regra.</summary>
        public static List<LazyWidgetDataBase> Occurrence(Occurrence o)
        {
            Styles s = Styles.Read();
            if (s == null || !CalendarModel.Ready) return null;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            bool past = CalendarModel.State(o, now) == OccState.Past;
            int off = o.Day - today;
            string rel = off >= 0 ? CalendarL10n.Relative(off) : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("days_ago", -off);
            var list = new List<LazyWidgetDataBase>
            {
                Text(o.Def.Title, s.Header, TextAlignmentOptions.Center),
                Text(DayHeader(o.Day) + " · " + rel + " · " + CalendarL10n.T("day_n", o.Day), s.Small, TextAlignmentOptions.Center),
                new UITooltipSeparatorWidgetData(),
                Text(Window(o) + (off >= 0 ? " · " + Remaining(o, now) : ""), past ? s.Small : s.SmallSelected, TextAlignmentOptions.Center),
            };
            string desc = o.Def.Description;
            if (desc != null) list.Add(Text(desc, s.Description, TextAlignmentOptions.Center));
            string extra = off >= 0 ? Extra(o) : null;
            if (extra != null) list.Add(Text(extra, s.Small, TextAlignmentOptions.Center));
            return list;
        }

        /// <summary>A janela do evento, sem contagem: "13:12", "00:14–23:45", "a qualquer hora do dia".</summary>
        public static string Window(Occurrence o)
        {
            if (o.AllDay) return CalendarL10n.T("any_time");
            if (o.Instant) return o.Def.NoClock ? CalendarL10n.T("at_dawn") : CalendarModel.Clock(o.Start);
            return CalendarModel.Clock(o.Start) + " - " + CalendarModel.Clock(o.End);
        }

        /// <summary>Só a contagem (o horário vem de <see cref="Window"/>): "começa em 3:10", "acaba em 1:02", "passou".</summary>
        public static string Remaining(Occurrence o, double now)
        {
            OccState st = CalendarModel.State(o, now);
            if (st == OccState.Upcoming) return CalendarL10n.T(o.Instant ? "in" : "starts_in", CalendarModel.Countdown(o.StartAbs - now));
            if (st == OccState.Active) return o.Instant ? CalendarL10n.T("now") : CalendarL10n.T("ends_in", CalendarModel.Countdown(o.EndAbs - now));
            return CalendarL10n.T(o.Instant ? "passed" : "ended");
        }

        /// <summary>Várias anotações do diário no mesmo ponto da linha do tempo, uma por linha com o tipo e a hora.</summary>
        public static List<LazyWidgetDataBase> JournalCluster(List<JournalLine> lines)
        {
            Styles s = Styles.Read();
            if (s == null || lines.Count == 0) return null;
            int day = lines[0].Day;
            var list = new List<LazyWidgetDataBase>
            {
                Text(CalendarL10n.T("journal"), s.Header, TextAlignmentOptions.Center),
                Text(DayHeader(day) + " · " + CalendarL10n.T("day_n", day), s.Small, TextAlignmentOptions.Center),
                new UITooltipSeparatorWidgetData(),
            };
            const int Max = 8;
            foreach (JournalLine l in lines.Take(Max))
                list.Add(Text(l.Name + "  " + s.Small.ApplyStyleToString(JournalView.KindLabel(l.Kind) + " · " + (l.Approx ? "~" : "") + CalendarModel.Clock(l.Tod)),
                    s.Description, TextAlignmentOptions.Left));
            if (lines.Count > Max) list.Add(Text(CalendarL10n.T("and_more", lines.Count - Max), s.SmallSelected, TextAlignmentOptions.Center));
            list.Add(new UITooltipSeparatorWidgetData());
            list.Add(Text(CalendarL10n.T("click_day"), s.Small, TextAlignmentOptions.Center));
            return list;
        }

        /// <summary>A marca da coluna "antes do diário": o que já era conhecido quando ele começou, por tipo.</summary>
        public static List<LazyWidgetDataBase> Baseline(Dictionary<string, int> counts, IEnumerable<string> kinds, int startDay)
        {
            Styles s = Styles.Read();
            if (s == null) return null;
            var list = new List<LazyWidgetDataBase>
            {
                Text(CalendarL10n.T("before_journal"), s.Header, TextAlignmentOptions.Center),
                Text(CalendarL10n.T("j_before", startDay), s.Small, TextAlignmentOptions.Center),
                new UITooltipSeparatorWidgetData(),
            };
            foreach (string k in kinds)
                if (counts.TryGetValue(k, out int n)) list.Add(Text(CalendarL10n.Count(k, n), s.Description, TextAlignmentOptions.Center));
            list.Add(new UITooltipSeparatorWidgetData());
            list.Add(Text(CalendarL10n.T("click_day"), s.Small, TextAlignmentOptions.Center));
            return list;
        }

        /// <summary>Linha de estado extra (energia da ressurreição, corpos do burro).</summary>
        public static string Extra(Occurrence o)
        {
            if (o.Def.Id == "donkey") return DonkeyLine(o);
            if (!o.Def.PowerStatus) return null;
            return CalendarModel.ResurrectionPowered() ? CalendarL10n.T("power_ok") : CalendarL10n.T("power_used", HudL10n.Day("day_lust"));
        }

        /// <summary>
        /// Quantos corpos o burro traz. A próxima viagem com o estado de agora (chance, vagas); as seguintes, só o
        /// máximo por viagem. Sem spoiler: mais de 1 só depois que o jogador pediu "muitos corpos" à Freira.
        /// </summary>
        private static string DonkeyLine(Occurrence o)
        {
            if (CalendarModel.State(o, CalendarModel.Now) != OccState.Upcoming) return null;
            DonkeyTrip t = CalendarModel.Donkey();
            if (t.Locked) return CalendarL10n.T("donkey_locked");
            bool next = CalendarModel.IsNextRun(o);
            if (!next) return t.Busy ? null : CalendarL10n.T(t.MaxCount > 1 ? "donkey_up_to" : "donkey_one", t.MaxCount);
            if (t.Busy) return CalendarL10n.T("donkey_busy");
            if (t.Full) return CalendarL10n.T("donkey_full", t.Bodies, t.Capacity);
            string line = CalendarL10n.T("donkey_chance", t.ChancePct, CalendarL10n.T(t.Count == 1 ? "corpse_1" : "corpse_n", t.Count));
            if (t.Count < t.MaxCount) line += " " + CalendarL10n.T(t.Count == 1 ? "donkey_room_1" : "donkey_room_n", t.Count);
            return line;
        }

        /// <summary>Horário e contagem regressiva em tempo real (dia de jogo = 7,5 min acordado).</summary>
        public static string When(Occurrence o, double now)
        {
            OccState st = CalendarModel.State(o, now);
            if (o.AllDay)
            {
                if (st == OccState.Upcoming) return CalendarL10n.T("all_day") + " · " + CalendarL10n.T("in", CalendarModel.Countdown(o.StartAbs - now));
                if (st == OccState.Active) return CalendarL10n.T("all_day") + " · " + CalendarL10n.T("ends_in", CalendarModel.Countdown(o.EndAbs - now));
                return CalendarL10n.T("ended");
            }
            string start = CalendarModel.Clock(o.Start);
            if (o.Instant && o.Def.NoClock)
            {
                if (st == OccState.Upcoming) return CalendarL10n.T("at_dawn") + " · " + CalendarL10n.T("in", CalendarModel.Countdown(o.StartAbs - now));
                return CalendarL10n.T("done_today");
            }
            if (o.Instant)
            {
                if (st == OccState.Upcoming) return start + " · " + CalendarL10n.T("in", CalendarModel.Countdown(o.StartAbs - now));
                if (st == OccState.Active) return start + " · " + CalendarL10n.T("now");
                return start + " · " + CalendarL10n.T("passed");
            }
            if (st == OccState.Upcoming) return start + " · " + CalendarL10n.T("starts_in", CalendarModel.Countdown(o.StartAbs - now));
            if (st == OccState.Active) return CalendarL10n.T("until", CalendarModel.Clock(o.End)) + " · " + CalendarL10n.T("ends_in", CalendarModel.Countdown(o.EndAbs - now));
            return CalendarL10n.T("ended");
        }

        /// <summary>"A semana renova em [Orgulho] em 3 dias" — só se o jogador já conhece algo que renova no Orgulho.</summary>
        public static string ResetLine(int today)
        {
            bool known = CalendarModel.Defs.Any(e => e.DayId == "day_pride" && CalendarModel.Known(e));
            if (!known) return null;
            int pride = CalendarModel.NextDayOf("day_pride", today);
            if (pride == today) return CalendarL10n.T("reset_today");
            return CalendarL10n.T("reset_in", "day_pride".FontIcon(), CalendarL10n.Relative(pride - today).ToLowerInvariant());
        }

        public static string DayHeader(int absDay)
        {
            string id = CalendarModel.DayIdOf(absDay);
            return DayIcon(absDay) + " " + HudL10n.Day(id);
        }

        public static string DayIcon(int absDay) => CalendarModel.DayIdOf(absDay)?.FontIcon() ?? "";

        /// <summary>Nome curto para listas; &lt;nobr&gt; evita a hora sozinha na linha de baixo.</summary>
        public static string ShortName(Occurrence o) => "<nobr>" + (o.Instant && !o.Def.NoClock ? o.Def.Title + " " + CalendarModel.Clock(o.Start) : o.Def.Title) + "</nobr>";

        private static string Num(Styles s, string v) => s.Description.ApplyStyleToString(v);

        private static UITooltipTextWidgetData Text(string text, TextStyle style, TextAlignmentOptions align) => new UITooltipTextWidgetData(text, align, style);
    }
}
