using System;
using System.Collections.Generic;
using System.Linq;
using GK2Hud.Journal;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Página do calendário no TAB, montada com as peças da página Character: cabeçalho <c>comm-header_2</c> com as
    /// pontas <c>comm-header_2-dec_side_1</c>, lista no formato de Buffs/Perks (ícone, nome em
    /// <c>small_font_bold-effects_header</c>, texto em <c>small_font-effects_regular_text</c>, separador
    /// <c>widget_perks-separator_hor-drk_1</c>), contagem com o estilo do tempo de buff (<c>tiny_font-effects_timer</c>) e
    /// dia vazio com <c>small_font-no_effects</c>. Unidades = pixels de arte.
    /// Esquerda (rola): "antes do diário", os dias passados com anotação no diário e a semana a partir de hoje; sigilo aceso
    /// no dia escolhido e apagado a 40% nos outros, como o medalhão. Direita (rola): eventos do dia escolhido e o diário
    /// dele (desbloqueios e quests); cada linha do diário abre o tooltip da coisa (item, receita, perk, construção).
    /// </summary>
    internal sealed class CalendarPage
    {
        private const string Name = "GK2Hud.CalendarPage";
        private const int LeftX = 4, LeftW = 216, RightX = 228, HeaderH = 26, RowTop = 30, RowH = 50, Pad = 10, ListRows = 6, LineH = 22;
        private const float DimAlpha = 0.4f;   // UIHUDWheelElement.iconColorInactive
        private const int Baseline = int.MinValue;

        private GameObject root;
        private RectTransform leftContent, rightContent;
        private ScrollRect leftScroll, rightScroll;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, TextStyle> styles = new Dictionary<string, TextStyle>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<RectTransform> tooltipTargets = new List<RectTransform>();
        private TMP_Text weekHeader, rightHeader, rightSub, footer;
        private int selected = -1;        // dia absoluto escolhido (Baseline = antes do diário)
        private int shownToday = -1;
        private string rowsSig, rightSig;
        private int pageW, pageH;

        private sealed class Row
        {
            public int Day;               // dia absoluto ou Baseline
            public RectTransform Rt;
            public Image Sigil;
            public TMP_Text Day_, Rel, Summary;
            public string DayText, RelText, SummaryText, SigilId;
        }

        private sealed class Entry
        {
            public Occurrence Occ;
            public TMP_Text When;
            public string WhenText;
        }

        /// <summary>Só pointer enter/exit/click: o scroll do mouse sobe para o ScrollRect (EventTrigger o engoliria).</summary>
        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Action OnEnter, OnExit, OnClick;
            public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData e) => OnExit?.Invoke();
            public void OnPointerClick(PointerEventData e) => OnClick?.Invoke();
        }

        public static void DestroyOrphans(Transform pages)
        {
            for (int i = pages.childCount - 1; i >= 0; i--)
                if (pages.GetChild(i).name == Name) Object.DestroyImmediate(pages.GetChild(i).gameObject);
        }

        public static CalendarPage Create(CharacterWindow w, Transform pages)
        {
            var p = new CalendarPage();
            if (!p.Collect(w)) return null;
            p.Build(pages);
            return p;
        }

        // ---------------- recursos nativos ----------------

        private bool Collect(CharacterWindow w)
        {
            string[] spriteNames =
            {
                "comm-header_2", "comm-header_2-dec_side_1", "widget_perks-separator_hor-drk_1", "widget_perks-footer-center-drk_1", "widget_perks-edge-drk_1",
            };
            foreach (Image img in w.GetComponentsInChildren<Image>(true)) Keep(img.sprite);
            UIHUDWheel wheel = Object.FindObjectsByType<UIHUDWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (wheel != null)
                foreach (Sprite s in Traverse.Create(wheel).Field("dayIcons").GetValue<List<Sprite>>() ?? new List<Sprite>()) Keep(s);
            foreach (UIMapDayActivityIcon ic in w.GetComponentsInChildren<UIMapDayActivityIcon>(true))
                Keep(Traverse.Create(ic).Field("expandedSprite").GetValue<Sprite>());
            List<string> missing = spriteNames.Where(n => !sprites.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>()) if (missing.Contains(Clean(s.name))) Keep(s);

            string[] styleNames =
            {
                "small_font_bold-header_2-text_1", "small_font_bold-sub_header_2", "small_font_bold-effects_header", "small_font-effects_regular_text",
                "tiny_font-effects_timer", "small_font-no_effects",
            };
            foreach (TextStyle ts in Resources.FindObjectsOfTypeAll<TextStyle>())
                if (styleNames.Contains(ts.name) && !styles.ContainsKey(ts.name)) styles[ts.name] = ts;
            List<string> miss = spriteNames.Where(n => !sprites.ContainsKey(n)).Concat(styleNames.Where(n => !styles.ContainsKey(n))).ToList();
            if (miss.Count > 0)
            {
                Plugin.Log.LogWarning("calendário: recursos nativos não encontrados: " + string.Join(", ", miss));
                return false;
            }
            return true;
        }

        private static string Clean(string n) => n?.Replace("(Clone)", "").Trim();

        private void Keep(Sprite s)
        {
            if (s == null) return;
            string n = Clean(s.name);
            if (!sprites.ContainsKey(n)) sprites[n] = s;
        }

        private Sprite SpriteOrNull(string n) => n != null && sprites.TryGetValue(n, out Sprite s) ? s : null;

        // ---------------- montagem ----------------

        private void Build(Transform pages)
        {
            root = new GameObject(Name, typeof(RectTransform));
            root.transform.SetParent(pages, false);
            RectTransform rt = (RectTransform)root.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            root.SetActive(false);
            Rect pr = ((RectTransform)pages).rect;
            pageW = pr.width > 0 ? Mathf.RoundToInt(pr.width) : 640;
            pageH = pr.height > 0 ? Mathf.RoundToInt(pr.height) : 385;
            int rightW = pageW - RightX - LeftX;

            weekHeader = Header(root.transform, LeftX, 0, LeftW);
            leftScroll = Scroll(root.transform, "Days", LeftX, RowTop, LeftW, ListRows * RowH, out leftContent);
            leftScroll.scrollSensitivity = RowH / 2f;
            int footY = RowTop + ListRows * RowH + 2;
            Footer(root.transform, LeftX, footY, LeftW);
            footer = Label(root.transform, "Reset", "small_font-effects_regular_text", LeftX, footY + 12, LeftW, TextAlignmentOptions.Center, false);

            rightHeader = Header(root.transform, RightX, 0, rightW);
            rightSub = Label(root.transform, "Sub", "small_font_bold-sub_header_2", RightX, HeaderH + 2, rightW, TextAlignmentOptions.Center, false);
            int top = HeaderH + 24;
            rightScroll = Scroll(root.transform, "Entries", RightX, top, rightW, pageH - top - 4, out rightContent);
            rightScroll.scrollSensitivity = LineH;
            Restyle();
        }

        /// <summary>Área rolável: Image transparente (recebe o scroll), RectMask2D e conteúdo ancorado no topo.</summary>
        private static ScrollRect Scroll(Transform parent, string name, int x, int y, int w, int h, out RectTransform content)
        {
            var vp = new GameObject(name, typeof(RectTransform));
            vp.transform.SetParent(parent, false);
            TopLeft((RectTransform)vp.transform, x, y, w, h);
            Image hit = vp.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            vp.AddComponent<RectMask2D>();
            var c = new GameObject("Content", typeof(RectTransform));
            c.transform.SetParent(vp.transform, false);
            content = (RectTransform)c.transform;
            TopLeft(content, 0, 0, w, 0);
            ScrollRect sr = vp.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = (RectTransform)vp.transform;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.inertia = false;
            return sr;
        }

        private TMP_Text Header(Transform parent, int x, int y, int w)
        {
            Image bg = Img(parent, "Header", "comm-header_2", x, y, w, HeaderH);
            bg.type = Image.Type.Sliced;
            Sprite dec = SpriteOrNull("comm-header_2-dec_side_1");
            int decW = dec != null ? Mathf.RoundToInt(dec.rect.width) : 22;
            Img(bg.transform, "DecorLeft", "comm-header_2-dec_side_1", 0, 0, decW, HeaderH);
            Image right = Img(bg.transform, "DecorRight", "comm-header_2-dec_side_1", w, 0, decW, HeaderH);
            right.rectTransform.localScale = new Vector3(-1f, 1f, 1f);   // como o DecotRight nativo (largura negativa)
            return Label(bg.transform, "Text", "small_font_bold-header_2-text_1", 0, 5, w, TextAlignmentOptions.Center, false);
        }

        /// <summary>Rodapé da lista de perks: centro + duas pontas (a da direita espelhada).</summary>
        private void Footer(Transform parent, int x, int y, int w)
        {
            var go = new GameObject("Footer", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TopLeft((RectTransform)go.transform, x, y, w, 10);
            const int centerW = 50, edgeW = 75;
            int cx = (w - centerW) / 2;
            Image c = Img(go.transform, "Center", "widget_perks-footer-center-drk_1", cx, 0, centerW, 10);
            c.type = Image.Type.Sliced;
            Image l = Img(go.transform, "Left", "widget_perks-edge-drk_1", cx - edgeW, 0, edgeW, 10);
            l.type = Image.Type.Sliced;
            Image r = Img(go.transform, "Right", "widget_perks-edge-drk_1", cx + centerW + edgeW, 0, edgeW, 10);
            r.type = Image.Type.Sliced;
            r.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
        }

        private void Separator(Transform parent, int x, int y, int w)
        {
            Image s = Img(parent, "Separator", "widget_perks-separator_hor-drk_1", x, y, w, 4);
            s.type = Image.Type.Sliced;
        }

        private Image Img(Transform parent, string name, string sprite, int x, int y, int w, int h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TopLeft((RectTransform)go.transform, x, y, w, h);
            Image img = go.AddComponent<Image>();
            img.sprite = SpriteOrNull(sprite);
            img.raycastTarget = false;
            return img;
        }

        private TMP_Text Label(Transform parent, string name, string style, int x, int y, int w, TextAlignmentOptions align, bool wrap)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TopLeft((RectTransform)go.transform, x, y, w, 16);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            go.AddComponent<TextStyleComponent>().SetTextStyle(styles[style]);
            labels.Add(tmp);
            return tmp;
        }

        private static void TopLeft(RectTransform rt, int x, int y, int w, int h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static Hover AddHover(GameObject go)
        {
            if (!go.TryGetComponent(out Image hit))
            {
                hit = go.AddComponent<Image>();
                hit.color = new Color(1f, 1f, 1f, 0f);
            }
            hit.raycastTarget = true;
            return go.AddComponent<Hover>();
        }

        private void Clear(RectTransform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                Transform ch = content.GetChild(i);
                foreach (TMP_Text t in ch.GetComponentsInChildren<TMP_Text>(true)) labels.Remove(t);
                Object.Destroy(ch.gameObject);
            }
        }

        // ---------------- lista de dias ----------------

        /// <summary>Quais linhas a lista tem: antes do diário, dias passados com anotação e a semana a partir de hoje.</summary>
        private List<int> RowDays(int today)
        {
            var days = new List<int>();
            JournalTracker j = JournalTracker.Current;
            if (j != null && j.Active)
            {
                days.Add(Baseline);
                foreach (int d in j.Days()) if (d < today && JournalView.Lines(j.OnDay(d)).Count > 0) days.Add(d);
            }
            for (int d = today; d < today + CalendarModel.DaysInWeek; d++) days.Add(d);
            return days;
        }

        private void RebuildRows(List<int> days, int today)
        {
            Clear(leftContent);
            rows.Clear();
            for (int i = 0; i < days.Count; i++)
            {
                int y = i * RowH;
                var go = new GameObject("Day" + i, typeof(RectTransform));
                go.transform.SetParent(leftContent, false);
                TopLeft((RectTransform)go.transform, 0, y, LeftW, RowH);
                var row = new Row { Day = days[i], Rt = (RectTransform)go.transform };
                Hover h = AddHover(go);
                h.OnEnter = () => Select(row.Day);
                h.OnClick = () => Select(row.Day);
                row.Sigil = Img(go.transform, "Sigil", null, Pad, 14, 22, 22);
                row.Day_ = Label(go.transform, "Day", "small_font_bold-effects_header", 40, 6, 120, TextAlignmentOptions.Left, false);
                row.Rel = Label(go.transform, "Rel", "tiny_font-effects_timer", 120, 8, LeftW - 120 - 6, TextAlignmentOptions.Right, false);
                row.Summary = Label(go.transform, "Summary", "small_font-effects_regular_text", 40, 24, LeftW - 40 - 6, TextAlignmentOptions.Left, false);
                row.Summary.overflowMode = TextOverflowModes.Ellipsis;
                if (row.Day == Baseline) { row.Day_.rectTransform.anchoredPosition = new Vector2(Pad, -6); row.Summary.rectTransform.anchoredPosition = new Vector2(Pad, -24); row.Summary.rectTransform.sizeDelta = new Vector2(LeftW - 2 * Pad, 16); }
                if (i < days.Count - 1) Separator(go.transform, Pad, RowH - 3, LeftW - 2 * Pad);
                rows.Add(row);
            }
            leftContent.sizeDelta = new Vector2(LeftW, days.Count * RowH);
            // Hoje na segunda linha (o dia anterior aparece em cima, para lembrar que dá para rolar para trás).
            int todayIdx = days.IndexOf(today);
            float max = Mathf.Max(0f, days.Count * RowH - ListRows * RowH);
            leftContent.anchoredPosition = new Vector2(0f, Mathf.Clamp((todayIdx - 1) * RowH, 0f, max));
        }

        private void RefreshRow(Row r, int today, double now)
        {
            JournalTracker j = JournalTracker.Current;
            string dayText, rel, sum, sigilId;
            if (r.Day == Baseline)
            {
                sigilId = null;
                dayText = CalendarL10n.T("before_journal");
                rel = "";
                Dictionary<string, int> counts = j != null ? j.BaselineCounts() : new Dictionary<string, int>();
                List<string> parts = JournalKinds.Order.Where(k => k != JournalKinds.Tutorial && k != JournalKinds.DayIcon && counts.ContainsKey(k))
                    .Select(k => CalendarL10n.Count(k, counts[k])).ToList();
                sum = parts.Count == 0 ? CalendarL10n.T("nothing_short") : string.Join(" · ", parts);
            }
            else
            {
                sigilId = CalendarModel.DayIdOf(r.Day);
                dayText = Hud.HudL10n.Day(sigilId);
                int off = r.Day - today;
                rel = off == 0 ? CalendarL10n.T("today") : off == 1 ? CalendarL10n.T("tomorrow") : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("day_n", r.Day);
                if (r.Day < today)
                {
                    sum = JournalView.Summary(JournalView.Lines(j != null ? j.OnDay(r.Day) : new List<JournalEntry>()));
                }
                else
                {
                    var names = new List<string>();
                    foreach (Occurrence o in CalendarModel.OnDay(r.Day))
                        if (off > 0 || CalendarModel.State(o, now) != OccState.Past) names.Add(CalendarTooltip.ShortName(o));
                    if (off == 0 && j != null && j.Active)
                    {
                        int fresh = JournalView.Lines(j.OnDay(today)).Count;
                        if (fresh > 0) names.Add(CalendarL10n.T("j_new", fresh));
                    }
                    sum = names.Count == 0 ? CalendarL10n.T("nothing_short") : string.Join(" · ", names);
                }
            }
            if (sigilId != r.SigilId || r.Sigil.sprite == null)
            {
                r.SigilId = sigilId;
                r.Sigil.sprite = SpriteOrNull(sigilId);
                r.Sigil.enabled = r.Sigil.sprite != null;
            }
            Color c = r.Sigil.color;
            c.a = r.Day == selected ? 1f : DimAlpha;
            r.Sigil.color = c;
            if (dayText != r.DayText) { r.DayText = dayText; r.Day_.text = dayText; }
            if (rel != r.RelText) { r.RelText = rel; r.Rel.text = rel; }
            if (sum != r.SummaryText) { r.SummaryText = sum; r.Summary.text = sum; }
        }

        // ---------------- ciclo ----------------

        public void Show()
        {
            if (root == null) return;
            root.SetActive(true);
            int today = (int)Math.Floor(CalendarModel.Now);
            selected = today;
            shownToday = today;
            rowsSig = rightSig = null;
            Refresh();
        }

        public void Hide()
        {
            HideTooltip();
            if (root != null) root.SetActive(false);
        }

        public void Destroy()
        {
            HideTooltip();
            if (root != null) Object.Destroy(root);
            root = null;
        }

        public void Restyle()
        {
            foreach (TMP_Text l in labels) l?.GetComponent<TextStyleComponent>()?.ApplyStyle();
            foreach (Row r in rows) r.DayText = r.RelText = r.SummaryText = null;
            rightSig = null;
            if (weekHeader != null) weekHeader.text = CalendarL10n.T("week");
            if (root != null && root.activeSelf) Refresh();
        }

        public void Select(int day)
        {
            if (day == selected) return;
            if (day != Baseline && rows.All(r => r.Day != day)) return;
            selected = day;
            Refresh();
        }

        /// <summary>Para os cenários: escolhe a linha pelo índice a partir de hoje (negativo = dias passados com anotação).</summary>
        internal void SelectOffset(int offset)
        {
            int today = (int)Math.Floor(CalendarModel.Now);
            int idx = rows.FindIndex(r => r.Day == today) + offset;
            if (idx >= 0 && idx < rows.Count)
            {
                Select(rows[idx].Day);
                float max = Mathf.Max(0f, rows.Count * RowH - ListRows * RowH);
                leftContent.anchoredPosition = new Vector2(0f, Mathf.Clamp((idx - 1) * RowH, 0f, max));
            }
        }

        public void Refresh()
        {
            if (root == null || !root.activeSelf || !CalendarModel.Ready) return;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            if (today != shownToday)
            {
                // Virou o dia com a página aberta: a escolha da semana anda junto (mesma posição relativa).
                if (selected != Baseline && selected >= shownToday)
                    selected = Mathf.Clamp(selected + (today - shownToday), today, today + CalendarModel.DaysInWeek - 1);
                shownToday = today;
            }
            List<int> days = RowDays(today);
            string sig = string.Join(",", days);
            if (sig != rowsSig) { rowsSig = sig; RebuildRows(days, today); }
            if (!days.Contains(selected)) selected = today;
            weekHeader.text = CalendarL10n.T("week");
            foreach (Row r in rows) RefreshRow(r, today, now);
            SetText(footer, CalendarTooltip.ResetLine(today) ?? "");
            RefreshRight(today, now);
        }

        private void RefreshRight(int today, double now)
        {
            int rightW = pageW - RightX - LeftX;
            JournalTracker j = JournalTracker.Current;
            List<Occurrence> occ = selected != Baseline && selected >= today ? CalendarModel.OnDay(selected) : new List<Occurrence>();
            string sig = selected + "|" + LLBase.CurrentLang + "|" + (j?.Version ?? -1) + "|" + string.Join(",", occ.Select(o => o.Def.Id + "@" + o.Start.ToString("0.000") + ":" +
                (CalendarModel.State(o, now) == OccState.Past) + ":" + (CalendarTooltip.Extra(o) ?? "")));
            if (sig != rightSig)
            {
                bool sameDay = rightSig != null && rightSig.StartsWith(selected + "|");
                rightSig = sig;
                if (selected == Baseline)
                {
                    SetText(rightHeader, CalendarL10n.T("before_journal"));
                    SetText(rightSub, j != null && j.Active ? CalendarL10n.T("j_before", j.StartDay) : "");
                }
                else
                {
                    SetText(rightHeader, Hud.HudL10n.Day(CalendarModel.DayIdOf(selected)));
                    int off = selected - today;
                    string rel = off >= 0 ? CalendarL10n.Relative(off) : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("days_ago", -off);
                    SetText(rightSub, rel + " · " + CalendarL10n.T("day_n", selected));
                }
                RebuildRight(occ, rightW, today, now);
                if (!sameDay) rightContent.anchoredPosition = Vector2.zero;
            }
            foreach (Entry e in entries)
            {
                string w = CalendarTooltip.When(e.Occ, now);
                if (w != e.WhenText) { e.WhenText = w; e.When.text = w; }
            }
        }

        private void RebuildRight(List<Occurrence> occ, int w, int today, double now)
        {
            HideTooltip();
            entries.Clear();
            tooltipTargets.Clear();
            Clear(rightContent);
            int y = 4;
            JournalTracker j = JournalTracker.Current;
            if (selected == Baseline)
            {
                y = BaselineSection(w, y, j);
            }
            else
            {
                if (selected >= today) y = EventsSection(occ, w, y, now);
                List<JournalLine> lines = j != null && j.Active && selected <= today ? JournalView.Lines(j.OnDay(selected)) : new List<JournalLine>();
                if (lines.Count > 0)
                {
                    if (selected == today)
                    {
                        if (occ.Count > 0) { Separator(rightContent, Pad, y, w - 2 * Pad); y += 8; }
                        TMP_Text h = Label(rightContent, "TodaySoFar", "small_font_bold-sub_header_2", 0, y, w, TextAlignmentOptions.Center, false);
                        h.text = CalendarL10n.T("today_so_far");
                        y += 22;
                    }
                    y = JournalSection(lines, w, y);
                }
                else if (selected < today)
                {
                    y = Empty(CalendarL10n.T("j_empty"), w, y);
                }
                else if (occ.Count == 0)
                {
                    y = Empty(CalendarL10n.T("nothing"), w, y);
                }
            }
            rightContent.sizeDelta = new Vector2(w, y + 4);
        }

        private int Empty(string text, int w, int y)
        {
            TMP_Text none = Label(rightContent, "Nothing", "small_font-no_effects", Pad, y + 14, w - 2 * Pad, TextAlignmentOptions.Center, true);
            none.text = text;
            return y + 40;
        }

        private int EventsSection(List<Occurrence> occ, int w, int y, double now)
        {
            int textX = 44, textW = w - textX - Pad;
            for (int i = 0; i < occ.Count; i++)
            {
                Occurrence o = occ[i];
                var go = new GameObject("Event_" + o.Def.Id, typeof(RectTransform));
                go.transform.SetParent(rightContent, false);
                bool past = CalendarModel.State(o, now) == OccState.Past;
                Sprite icon = SpriteOrNull(o.Def.Icon) ?? SpriteOrNull(CalendarModel.DayIdOf(o.Day));
                if (icon != null)
                {
                    int iw = Mathf.RoundToInt(icon.rect.width), ih = Mathf.RoundToInt(icon.rect.height);
                    Image img = Img(go.transform, "Icon", null, Pad + (26 - iw) / 2, 2 + (26 - ih) / 2, iw, ih);
                    img.sprite = icon;
                    if (past) img.color = new Color(1f, 1f, 1f, DimAlpha);
                }
                TMP_Text name = Label(go.transform, "Name", past ? "small_font-no_effects" : "small_font_bold-effects_header", textX, 0, textW, TextAlignmentOptions.Left, false);
                name.text = o.Def.Title;
                TMP_Text when = Label(go.transform, "When", "tiny_font-effects_timer", textX, 18, textW, TextAlignmentOptions.Left, false);
                int h = 34;
                var parts = new List<string>();
                string desc = o.Def.Description;
                if (desc != null) parts.Add(desc);
                string extra = CalendarTooltip.Extra(o);
                if (extra != null) parts.Add(extra);
                if (parts.Count > 0)
                {
                    TMP_Text d = Label(go.transform, "Description", "small_font-effects_regular_text", textX, h, textW, TextAlignmentOptions.TopLeft, true);
                    d.text = string.Join("\n", parts);
                    int dh = Mathf.CeilToInt(d.GetPreferredValues(d.text, textW, 0f).y);
                    if (dh % 2 != 0) dh++;
                    d.rectTransform.sizeDelta = new Vector2(textW, dh);
                    h += dh;
                }
                h += 6;
                TopLeft((RectTransform)go.transform, 0, y, w, h);
                y += h;
                if (i < occ.Count - 1) { Separator(rightContent, Pad, y, w - 2 * Pad); y += 8; }
                entries.Add(new Entry { Occ = o, When = when });
            }
            return y;
        }

        /// <summary>Diário de um dia: "Concluído" (quests) e "Desbloqueado" (o resto), uma linha por coisa, com tooltip.</summary>
        private int JournalSection(List<JournalLine> lines, int w, int y)
        {
            List<JournalLine> quests = lines.Where(l => l.Kind == JournalKinds.Quest).ToList();
            List<JournalLine> unlocks = lines.Where(l => l.Kind != JournalKinds.Quest).ToList();
            if (quests.Count > 0) y = JournalGroup(CalendarL10n.T("j_completed_h"), quests, w, y);
            if (unlocks.Count > 0)
            {
                if (quests.Count > 0) y += 6;
                y = JournalGroup(CalendarL10n.T("j_unlocked_h"), unlocks, w, y);
            }
            return y;
        }

        private int JournalGroup(string title, List<JournalLine> lines, int w, int y)
        {
            TMP_Text h = Label(rightContent, "Group", "small_font_bold-sub_header_2", Pad, y, w - 2 * Pad, TextAlignmentOptions.Left, false);
            h.text = title;
            y += 18;
            Separator(rightContent, Pad, y, w - 2 * Pad);
            y += 6;
            foreach (JournalLine l in lines)
            {
                JournalLine line = l;
                var go = new GameObject("Line_" + l.Kind + "_" + l.Id, typeof(RectTransform));
                go.transform.SetParent(rightContent, false);
                RectTransform rt = (RectTransform)go.transform;
                TopLeft(rt, Pad, y, w - 2 * Pad, LineH);
                Hover hv = AddHover(go);
                hv.OnEnter = () => ShowTooltip(line, rt);
                hv.OnExit = () => HideTooltip(rt);
                tooltipTargets.Add(rt);
                int rightLabelW = 130;
                TMP_Text name = Label(go.transform, "Name", "small_font_bold-effects_header", 0, 3, w - 2 * Pad - rightLabelW - 6, TextAlignmentOptions.Left, false);
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.text = l.Name;
                TMP_Text kind = Label(go.transform, "Kind", "tiny_font-effects_timer", w - 2 * Pad - rightLabelW, 5, rightLabelW, TextAlignmentOptions.Right, false);
                kind.text = JournalView.KindLabel(l.Kind) + " · " + (l.Approx ? "~" : "") + CalendarModel.Clock(l.Tod);
                y += LineH;
            }
            return y;
        }

        private int BaselineSection(int w, int y, JournalTracker j)
        {
            if (j == null || !j.Active) return Empty(CalendarL10n.T("nothing"), w, y);
            TMP_Text d = Label(rightContent, "Text", "small_font-effects_regular_text", Pad, y, w - 2 * Pad, TextAlignmentOptions.TopLeft, true);
            d.text = CalendarL10n.T("j_baseline_d", j.StartDay);
            int dh = Mathf.CeilToInt(d.GetPreferredValues(d.text, w - 2 * Pad, 0f).y);
            if (dh % 2 != 0) dh++;
            d.rectTransform.sizeDelta = new Vector2(w - 2 * Pad, dh);
            y += dh + 8;
            Separator(rightContent, Pad, y, w - 2 * Pad);
            y += 8;
            Dictionary<string, int> counts = j.BaselineCounts();
            foreach (string k in JournalKinds.Order)
            {
                if (!counts.TryGetValue(k, out int n)) continue;
                TMP_Text name = Label(rightContent, "Count_" + k, "small_font_bold-effects_header", Pad, y + 3, w - 2 * Pad, TextAlignmentOptions.Left, false);
                name.text = CalendarL10n.Count(k, n);
                y += LineH;
            }
            return y;
        }

        // ---------------- tooltip das linhas do diário ----------------

        private void ShowTooltip(JournalLine line, RectTransform target)
        {
            UITooltip native = Native.Instance;
            if (native == null || target == null) return;
            List<LazyWidgetDataBase> list = JournalView.Tooltip(line);
            if (list == null || list.Count == 0) return;
            Native.ShowAt.Invoke(native, new object[] { list, target, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.TopRight });
        }

        private static void HideTooltip(RectTransform target)
        {
            if (target != null && UITooltip.IsTooltipShowingAtTarget(target)) UITooltip.Hide();
        }

        private void HideTooltip()
        {
            foreach (RectTransform rt in tooltipTargets) HideTooltip(rt);
        }

        private static void SetText(TMP_Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        internal string Describe()
        {
            return $"selected={(selected == Baseline ? "antes" : selected.ToString())} rows={rows.Count} entries={entries.Count} linhas={tooltipTargets.Count} " +
                   $"active={(root != null && root.activeSelf)} footer='{footer?.text}'";
        }
    }
}
