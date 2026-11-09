using System;
using System.Collections.Generic;
using System.Linq;
using GK2Hud.Hud;
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

        /// <summary>Linha de estado extra (energia da ressurreição).</summary>
        public static string Extra(Occurrence o)
        {
            if (!o.Def.PowerStatus) return null;
            return CalendarModel.ResurrectionPowered() ? CalendarL10n.T("power_ok") : CalendarL10n.T("power_used", HudL10n.Day("day_lust"));
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
