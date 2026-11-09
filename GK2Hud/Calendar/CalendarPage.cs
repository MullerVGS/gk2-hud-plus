using System;
using System.Collections.Generic;
using System.Linq;
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
    /// dia vazio com <c>small_font-no_effects</c>. Esquerda: a semana a partir de hoje (sigilo aceso no dia escolhido,
    /// apagado a 40% nos outros, como o medalhão). Direita: o que acontece no dia escolhido. Unidades = pixels de arte.
    /// </summary>
    internal sealed class CalendarPage
    {
        private const string Name = "GK2Hud.CalendarPage";
        private const int LeftX = 4, LeftW = 216, RightX = 228, HeaderH = 26, RowTop = 30, RowH = 50, Pad = 10;
        private const float DimAlpha = 0.4f;   // UIHUDWheelElement.iconColorInactive

        private GameObject root;
        private RectTransform rightContent;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, TextStyle> styles = new Dictionary<string, TextStyle>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private readonly List<Row> rows = new List<Row>();
        private TMP_Text weekHeader, rightHeader, rightSub, footer;
        private readonly List<Entry> entries = new List<Entry>();
        private int selected = -1;        // dia absoluto escolhido
        private int shownToday = -1;
        private string rightSig;
        private int pageW;

        private sealed class Row
        {
            public int Offset;
            public Image Sigil;
            public TMP_Text Day, Rel, Summary;
            public string DayText, RelText, SummaryText, SigilId;
        }

        private sealed class Entry
        {
            public Occurrence Occ;
            public TMP_Text When;
            public string WhenText;
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
            pageW = Mathf.RoundToInt(((RectTransform)pages).rect.width);
            if (pageW <= 0) pageW = 640;
            int rightW = pageW - RightX - LeftX;

            weekHeader = Header(root.transform, LeftX, 0, LeftW);
            for (int i = 0; i < CalendarModel.DaysInWeek; i++) rows.Add(BuildRow(i));
            int footY = RowTop + CalendarModel.DaysInWeek * RowH + 2;
            Footer(root.transform, LeftX, footY, LeftW);
            footer = Label(root.transform, "Reset", "small_font-effects_regular_text", LeftX, footY + 12, LeftW, TextAlignmentOptions.Center, false);

            rightHeader = Header(root.transform, RightX, 0, rightW);
            rightSub = Label(root.transform, "Sub", "small_font_bold-sub_header_2", RightX, HeaderH + 2, rightW, TextAlignmentOptions.Center, false);
            var content = new GameObject("Entries", typeof(RectTransform));
            content.transform.SetParent(root.transform, false);
            rightContent = (RectTransform)content.transform;
            TopLeft(rightContent, RightX, HeaderH + 24, rightW, 0);
            Restyle();
        }

        private Row BuildRow(int i)
        {
            int y = RowTop + i * RowH;
            var go = new GameObject("Day" + i, typeof(RectTransform));
            go.transform.SetParent(root.transform, false);
            TopLeft((RectTransform)go.transform, LeftX, y, LeftW, RowH);
            // Área de hover/clique invisível (o resto da página não recebe raycast).
            Image hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            var row = new Row { Offset = i };
            EventTrigger trig = go.AddComponent<EventTrigger>();
            AddTrigger(trig, EventTriggerType.PointerEnter, () => SelectOffset(row.Offset));
            AddTrigger(trig, EventTriggerType.PointerClick, () => SelectOffset(row.Offset));

            row.Sigil = Img(go.transform, "Sigil", null, Pad, 14, 22, 22);
            row.Day = Label(go.transform, "Day", "small_font_bold-effects_header", 40, 6, 120, TextAlignmentOptions.Left, false);
            row.Rel = Label(go.transform, "Rel", "tiny_font-effects_timer", 120, 8, LeftW - 120 - 6, TextAlignmentOptions.Right, false);
            row.Summary = Label(go.transform, "Summary", "small_font-effects_regular_text", 40, 24, LeftW - 40 - 6, TextAlignmentOptions.Left, false);
            row.Summary.overflowMode = TextOverflowModes.Ellipsis;
            if (i < CalendarModel.DaysInWeek - 1) Separator(go.transform, Pad, RowH - 3, LeftW - 2 * Pad);
            return row;
        }

        private static void AddTrigger(EventTrigger trig, EventTriggerType type, Action a)
        {
            var e = new EventTrigger.Entry { eventID = type };
            e.callback.AddListener(_ => a());
            trig.triggers.Add(e);
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

        // ---------------- ciclo ----------------

        public void Show()
        {
            if (root == null) return;
            root.SetActive(true);
            int today = (int)Math.Floor(CalendarModel.Now);
            selected = today;
            shownToday = today;
            rightSig = null;
            Refresh();
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        public void Destroy()
        {
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

        private void SelectOffset(int offset)
        {
            Select((int)Math.Floor(CalendarModel.Now) + offset);
        }

        public void Select(int day)
        {
            int today = (int)Math.Floor(CalendarModel.Now);
            if (day < today || day >= today + CalendarModel.DaysInWeek) day = today;
            if (day == selected) return;
            selected = day;
            Refresh();
        }

        public void Refresh()
        {
            if (root == null || !root.activeSelf || !CalendarModel.Ready) return;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            if (today != shownToday)
            {
                // Virou o dia com a página aberta: a escolha anda junto (mesma posição relativa).
                selected = Mathf.Clamp(selected + (today - shownToday), today, today + CalendarModel.DaysInWeek - 1);
                shownToday = today;
            }
            weekHeader.text = CalendarL10n.T("week");
            foreach (Row r in rows) RefreshRow(r, today, now);
            SetText(footer, CalendarTooltip.ResetLine(today) ?? "");
            RefreshRight(today, now);
        }

        private void RefreshRow(Row r, int today, double now)
        {
            int day = today + r.Offset;
            string id = CalendarModel.DayIdOf(day);
            if (id != r.SigilId)
            {
                r.SigilId = id;
                r.Sigil.sprite = SpriteOrNull(id);
            }
            Color c = r.Sigil.color;
            c.a = day == selected ? 1f : DimAlpha;
            r.Sigil.color = c;
            string dayText = Hud.HudL10n.Day(id);
            if (dayText != r.DayText) { r.DayText = dayText; r.Day.text = dayText; }
            string rel = r.Offset == 0 ? CalendarL10n.T("today") : r.Offset == 1 ? CalendarL10n.T("tomorrow") : CalendarL10n.T("day_n", day);
            if (rel != r.RelText) { r.RelText = rel; r.Rel.text = rel; }
            List<Occurrence> occ = CalendarModel.OnDay(day);
            var names = new List<string>();
            foreach (Occurrence o in occ)
                if (r.Offset > 0 || CalendarModel.State(o, now) != OccState.Past)
                    names.Add(CalendarTooltip.ShortName(o));
            string sum = names.Count == 0 ? CalendarL10n.T("nothing_short") : string.Join(" · ", names);
            if (sum != r.SummaryText) { r.SummaryText = sum; r.Summary.text = sum; }
        }

        private void RefreshRight(int today, double now)
        {
            int rightW = pageW - RightX - LeftX;
            List<Occurrence> occ = CalendarModel.OnDay(selected);
            string sig = selected + "|" + LLBase.CurrentLang + "|" + string.Join(",", occ.Select(o => o.Def.Id + "@" + o.Start.ToString("0.000") + ":" + (CalendarModel.State(o, now) == OccState.Past)
                                                                                         + ":" + (CalendarTooltip.Extra(o) ?? "")));
            if (sig != rightSig)
            {
                rightSig = sig;
                SetText(rightHeader, Hud.HudL10n.Day(CalendarModel.DayIdOf(selected)));
                SetText(rightSub, CalendarL10n.Relative(selected - today) + " · " + CalendarL10n.T("day_n", selected));
                RebuildEntries(occ, rightW, now);
            }
            foreach (Entry e in entries)
            {
                string w = CalendarTooltip.When(e.Occ, now);
                if (w != e.WhenText) { e.WhenText = w; e.When.text = w; }
            }
        }

        private void RebuildEntries(List<Occurrence> occ, int w, double now)
        {
            foreach (Entry e in entries) labels.Remove(e.When);
            entries.Clear();
            for (int i = rightContent.childCount - 1; i >= 0; i--)
            {
                Transform ch = rightContent.GetChild(i);
                foreach (TMP_Text t in ch.GetComponentsInChildren<TMP_Text>(true)) labels.Remove(t);
                Object.Destroy(ch.gameObject);
            }
            if (occ.Count == 0)
            {
                TMP_Text none = Label(rightContent, "Nothing", "small_font-no_effects", Pad, 18, w - 2 * Pad, TextAlignmentOptions.Center, true);
                none.text = CalendarL10n.T("nothing");
                return;
            }
            int textX = 44, textW = w - textX - Pad;
            int y = 4;
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
                if (i < occ.Count - 1)
                {
                    Separator(rightContent, Pad, y, w - 2 * Pad);
                    y += 8;
                }
                entries.Add(new Entry { Occ = o, When = when });
            }
        }

        private static void SetText(TMP_Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        internal string Describe()
        {
            return $"selected={selected} rows={rows.Count} entries={entries.Count} active={(root != null && root.activeSelf)} footer='{footer?.text}'";
        }
    }
}
