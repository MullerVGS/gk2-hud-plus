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
    /// Página do calendário no TAB: faixa de cenas em cima e o dia escolhido embaixo. Unidades = pixels de arte.
    /// Faixa: uma coluna por dia (do início do diário até o fim da semana), cada uma com a cena do medalhão
    /// (<see cref="WeekStrip.Scene"/>: céu, horizonte, arte e sigilo do dia), a legenda ("Hoje", "Dia 150"), um selo com a
    /// contagem do diário e os ícones dos eventos conhecidos, com a hora embaixo. Hoje usa a hora atual, os outros dias o meio-dia.
    /// O passado fica escurecido até o marcador de agora. Mouse num ícone abre o tooltip nativo do evento; clicar na coluna
    /// escolhe o dia; roda, arrasto e setas andam pelos dias, e escolher um dia fora da janela rola até ele.
    /// Embaixo, à esquerda, os eventos do dia (formato de Buffs/Perks) e, à direita, o diário dele: grupos por tipo, cada
    /// anotação com o nome inteiro, tipo e hora, e a descrição que o tooltip dela mostra. Grupos grandes ficam recolhidos
    /// e só os abertos criam linhas.
    /// Peças nativas: cabeçalho <c>comm-header_2</c>, seleção <c>hint-menu_selector</c>, setas <c>btn_i_grey-arrow_horizontal_small</c>,
    /// selo na <c>craft_window-header_1-value_cell</c>.
    /// </summary>
    internal sealed class CalendarPage
    {
        private const string Name = "GK2Hud.CalendarPage";
        private const int X0 = 4, ColW = 96, HeaderH = 26, RangeY = 28, RangeH = 12, StripY = 40;
        private const int SceneH = 56, LabelY = SceneH + 2, LabelH = 12, EventsY = LabelY + LabelH + 2;
        private const int IconCell = 24, IconPitch = 32, TimeH = 10, PerRow = 3, MaxRows = 3;
        private const int Pad = 10, LineH = 22, ColGap = 8, KindW = 104, OpenLimit = 8;
        private const float DimAlpha = 0.4f;            // UIHUDWheelElement.iconColorInactive
        private const float PastIcon = 0.35f;           // o mesmo dos cartões do medalhão
        private const int Baseline = int.MinValue;

        private static readonly Color PastShade = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color NowOutline = new Color(0.17f, 0.09f, 0.05f, 1f);
        private static readonly Color NowColor = new Color(1f, 0.91f, 0.66f, 1f);
        private static readonly Color BaselineBg = new Color(0.1f, 0.075f, 0.06f, 0.92f);

        private GameObject root;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, TextStyle> styles = new Dictionary<string, TextStyle>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private int pageW, pageH;

        // faixa de cenas
        private RectTransform stripVp, stripContent, detail, selFrame, pastShade, nowLine, nowOutline, todayScene;
        private TMP_Text header, rangeText;
        private Image arrowLeft, arrowRight;
        private readonly List<int> cols = new List<int>();
        private readonly List<RectTransform> colRoots = new List<RectTransform>();
        private readonly List<List<Occurrence>> colEvents = new List<List<Occurrence>>();
        private readonly Dictionary<int, List<JournalLine>> dayLines = new Dictionary<int, List<JournalLine>>();
        private readonly Dictionary<int, TMP_Text> dayLabels = new Dictionary<int, TMP_Text>();
        private readonly List<EventMark> eventMarks = new List<EventMark>();
        private readonly List<RectTransform> timelineTips = new List<RectTransform>();
        private int stripW, stripH, scrollX;
        private string timelineSig;
        private bool todayDaytime, revealPending;

        // detalhe
        private TMP_Text leftHeader, rightHeader;
        private RectTransform leftContent, rightContent;
        private ScrollRect leftScroll, rightScroll;
        private int leftW, rightW;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<RectTransform> tooltipTargets = new List<RectTransform>();
        private readonly Dictionary<string, bool> groupOpen = new Dictionary<string, bool>();
        private readonly Dictionary<string, string> descCache = new Dictionary<string, string>();
        private readonly List<string> groupInfo = new List<string>();
        private string detailSig;
        private int detailDay = int.MinValue + 1;
        private int journalLinesShown;

        private int selected = -1;        // dia absoluto escolhido (Baseline = antes do diário)
        private int shownToday = -1;

        private sealed class Entry
        {
            public Occurrence Occ;
            public TMP_Text When;
            public string WhenText;
        }

        /// <summary>Ícone de evento na faixa: a hora (ou contagem) embaixo e o esmaecido do que já passou.</summary>
        private sealed class EventMark
        {
            public int Col;
            public Occurrence Occ;
            public Image Icon;
            public TMP_Text Time;
            public OccState State;
        }

        /// <summary>Só pointer enter/exit/click: o scroll e o arrasto sobem para quem rola (EventTrigger os engoliria).</summary>
        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Action OnEnter, OnExit, OnClick;
            public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData e) => OnExit?.Invoke();
            public void OnPointerClick(PointerEventData e) { if (!e.dragging) OnClick?.Invoke(); }
        }

        /// <summary>Roda e arrasto da faixa de dias.</summary>
        internal sealed class Pan : MonoBehaviour, IScrollHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public Action<float> OnWheel;
            public Action<Vector2> OnDragBy;
            public Action OnDragEnd;
            public void OnScroll(PointerEventData e) => OnWheel?.Invoke(e.scrollDelta.y);
            public void OnBeginDrag(PointerEventData e) { }
            public void OnDrag(PointerEventData e) => OnDragBy?.Invoke(e.delta);
            public void OnEndDrag(PointerEventData e) => OnDragEnd?.Invoke();
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

        private static readonly string[] RequiredSprites =
        {
            "comm-header_2", "comm-header_2-dec_side_1", "widget_perks-separator_hor-drk_1", "hint-menu_selector",
            "btn_i_grey-arrow_horizontal_small", "btn_i_grey-arrow_horizontal_small-inactive", "btn_i_grey-arrow_vertical_small-d", "craft_window-header_1-value_cell",
        };

        private static readonly string[] RequiredStyles =
        {
            "small_font_bold-header_2-text_1", "small_font_bold-sub_header_2", "small_font_bold-effects_header", "small_font-effects_regular_text",
            "tiny_font-effects_timer", "small_font-no_effects", "tiny_font-hint_additional_text",
        };

        private bool Collect(CharacterWindow w)
        {
            foreach (Image img in w.GetComponentsInChildren<Image>(true)) Keep(img.sprite);
            UIHUDWheel wheel = Object.FindObjectsByType<UIHUDWheel>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (wheel != null)
                foreach (Sprite s in Traverse.Create(wheel).Field("dayIcons").GetValue<List<Sprite>>() ?? new List<Sprite>()) Keep(s);
            foreach (UIMapDayActivityIcon ic in w.GetComponentsInChildren<UIMapDayActivityIcon>(true))
                Keep(Traverse.Create(ic).Field("expandedSprite").GetValue<Sprite>());
            List<string> missing = RequiredSprites.Where(n => !sprites.ContainsKey(n)).ToList();
            if (missing.Count > 0)
                foreach (Sprite s in Resources.FindObjectsOfTypeAll<Sprite>()) if (missing.Contains(Clean(s.name))) Keep(s);

            foreach (TextStyle ts in Resources.FindObjectsOfTypeAll<TextStyle>())
                if (RequiredStyles.Contains(ts.name) && !styles.ContainsKey(ts.name)) styles[ts.name] = ts;
            List<string> miss = RequiredSprites.Where(n => !sprites.ContainsKey(n)).Concat(RequiredStyles.Where(n => !styles.ContainsKey(n))).ToList();
            if (miss.Count > 0)
            {
                HudModule.Log.LogWarning("calendário: recursos nativos não encontrados: " + string.Join(", ", miss));
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

        // ---------------- montagem fixa ----------------

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
            stripW = pageW - 2 * X0;

            header = Header(root.transform, X0, 0, stripW);

            stripVp = Viewport(root.transform, "Days", X0, StripY, stripW, EventsY, out stripContent);
            AddPan(stripVp);

            // A seta nativa aponta para a esquerda; a da direita é a mesma espelhada no lugar.
            arrowLeft = Img(root.transform, "Earlier", "btn_i_grey-arrow_horizontal_small", X0 + 2, RangeY, 8, RangeH);
            arrowRight = Img(root.transform, "Later", "btn_i_grey-arrow_horizontal_small", X0 + stripW - 2, RangeY, 8, RangeH);
            arrowRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            arrowRight.rectTransform.pivot = new Vector2(1f, 1f);
            foreach (Image a in new[] { arrowLeft, arrowRight })
            {
                Image arrow = a;
                arrow.raycastTarget = true;
                Hover h = arrow.gameObject.AddComponent<Hover>();
                h.OnClick = () => StepDays(arrow == arrowLeft ? -1 : 1);
            }
            rangeText = Label(root.transform, "Range", "tiny_font-effects_timer", X0 + 16, RangeY, stripW - 32, TextAlignmentOptions.Center, false);

            var d = new GameObject("Day", typeof(RectTransform));
            d.transform.SetParent(root.transform, false);
            detail = (RectTransform)d.transform;
            leftW = (stripW - ColGap) / 3;
            rightW = stripW - ColGap - leftW;
            TopLeft(detail, 0, StripY + EventsY + 4, pageW, 100);
            leftHeader = Header(detail, X0, 0, leftW);
            rightHeader = Header(detail, X0 + leftW + ColGap, 0, rightW);
            leftScroll = Scroll(detail, "Events", X0, HeaderH + 2, leftW, 60, out leftContent);
            rightScroll = Scroll(detail, "Journal", X0 + leftW + ColGap, HeaderH + 2, rightW, 60, out rightContent);
            leftScroll.scrollSensitivity = rightScroll.scrollSensitivity = LineH;
            Restyle();
        }

        /// <summary>Roda: dias inteiros. Arrasto: "pega" a faixa e encaixa no dia ao soltar.</summary>
        private void AddPan(RectTransform vp)
        {
            Pan p = vp.gameObject.AddComponent<Pan>();
            p.OnWheel = delta =>
            {
                if (Mathf.Abs(delta) < 0.01f) return;
                int steps = -Mathf.Clamp(Mathf.RoundToInt(delta), -3, 3);
                if (steps == 0) steps = delta > 0 ? -1 : 1;
                StepDays(steps);
            };
            p.OnDragEnd = () => StepDays(0);
            p.OnDragBy = delta => ScrollBy(-Mathf.RoundToInt(delta.x / Mathf.Max(1f, LazyUI.ScaleFactor)), 0);
        }

        /// <summary>Área recortada que recebe roda/arrasto; o conteúdo anda por <see cref="ApplyScroll"/>.</summary>
        private static RectTransform Viewport(Transform parent, string name, int x, int y, int w, int h, out RectTransform content)
        {
            var vp = new GameObject(name, typeof(RectTransform));
            vp.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)vp.transform;
            TopLeft(rt, x, y, w, h);
            Image hit = vp.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0f);
            hit.raycastTarget = true;
            vp.AddComponent<RectMask2D>();
            var c = new GameObject("Content", typeof(RectTransform));
            c.transform.SetParent(vp.transform, false);
            content = (RectTransform)c.transform;
            TopLeft(content, 0, 0, w, h);
            return rt;
        }

        /// <summary>Área rolável vertical: Image transparente (recebe o scroll), RectMask2D e conteúdo ancorado no topo.</summary>
        private static ScrollRect Scroll(Transform parent, string name, int x, int y, int w, int h, out RectTransform content)
        {
            RectTransform vp = Viewport(parent, name, x, y, w, h, out content);
            TopLeft(content, 0, 0, w, 0);
            ScrollRect sr = vp.gameObject.AddComponent<ScrollRect>();
            sr.content = content;
            sr.viewport = vp;
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
            if (img.sprite != null && img.sprite.border != Vector4.zero) img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            return img;
        }

        private static Image Rect(Transform parent, string name, Color color, int x, int y, int w, int h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TopLeft((RectTransform)go.transform, x, y, w, h);
            Image img = go.AddComponent<Image>();
            img.color = color;
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

        /// <summary>Altura de um texto com quebra na largura dada, arredondada para cima e par (grade de 2 px de tela).</summary>
        private static int TextHeight(TMP_Text t, int width)
        {
            int h = Mathf.CeilToInt(t.GetPreferredValues(t.text, width, 0f).y);
            return h % 2 != 0 ? h + 1 : h;
        }

        // ---------------- faixa de cenas ----------------

        /// <summary>Colunas: "antes do diário", do início do diário (ou ontem) até o fim da semana que começa hoje.</summary>
        private static List<int> Columns(int today)
        {
            var days = new List<int>();
            int first = today - 1;
            JournalTracker j = JournalTracker.Current;
            if (j != null && j.Active)
            {
                days.Add(Baseline);
                first = Math.Min(first, j.StartDay);
                List<int> jd = j.Days();
                if (jd.Count > 0) first = Math.Min(first, jd[0]);
            }
            for (int d = first; d < today + CalendarModel.DaysInWeek; d++) days.Add(d);
            return days;
        }

        /// <summary>Muda quando o conjunto de colunas, eventos ou marcas muda (não a cada quadro).</summary>
        private static string TimelineSig(int today)
        {
            JournalTracker j = JournalTracker.Current;
            KnowledgeSystem ks = MainGame.Instance.GameSave.knowledgeSystem;
            var parts = new List<string>
            {
                today.ToString(), LLBase.CurrentLang, (j != null && j.Active ? j.Version : -1).ToString(), CalendarModel.RevealAll.ToString(),
                (ks?.unlockedCustomHudDaySprites?.Count ?? 0).ToString(),
            };
            List<GameLogicData> logics = MainGame.Instance.GameSave.gameLogicSystemData?.gameLogics;
            foreach (EventDef e in CalendarModel.Defs)
            {
                if (!CalendarModel.Known(e)) continue;
                parts.Add(e.Id);
                if (e.LogicId == null) continue;
                GameLogicData g = logics?.Find(x => x.id == e.LogicId);
                if (g != null) parts.Add(g.execDay + ":" + g.execTime.ToString("0.000"));
            }
            return string.Join("|", parts);
        }

        private static int ColX(int idx) => idx * ColW;

        private static int TimeX(float tod) => Mathf.Clamp(Mathf.RoundToInt(tod * ColW), 0, ColW);

        private static float Frac(double now) => (float)(now - Math.Floor(now));

        private static int RowsOf(int count) => Mathf.Min(MaxRows, (count + PerRow - 1) / PerRow);

        /// <summary>Altura da área de eventos de uma coluna: cada linha de ícones, mais a folga da hora quando algum tem.</summary>
        private static int EventsHeight(List<Occurrence> occ)
        {
            int h = 0;
            for (int r = 0; r < RowsOf(occ.Count); r++)
                h += IconCell + (occ.Skip(r * PerRow).Take(PerRow).Any(HasClock) ? TimeH : 0) + 2;
            return h;
        }

        private static bool HasClock(Occurrence o) => o.Instant && !o.Def.NoClock;

        private static List<Occurrence> IconsOf(int day) =>
            CalendarModel.OnDay(day).Where(o => o.Def.Art != null).OrderBy(o => HasClock(o) ? 1 : 0).ToList();

        private void RebuildTimeline(int today, double now)
        {
            HideTimelineTips();
            timelineTips.Clear();
            eventMarks.Clear();
            dayLabels.Clear();
            Clear(stripContent);
            colRoots.Clear();
            colEvents.Clear();
            dayLines.Clear();
            List<int> oldCols = new List<int>(cols);
            cols.Clear();
            cols.AddRange(Columns(today));

            JournalTracker j = JournalTracker.Current;
            int eventsH = 0;
            foreach (int day in cols)
            {
                List<Occurrence> occ = day == Baseline ? new List<Occurrence>() : IconsOf(day);
                colEvents.Add(occ);
                eventsH = Mathf.Max(eventsH, EventsHeight(occ));
                if (day != Baseline && j != null && j.Active && day <= today) dayLines[day] = JournalView.Lines(j.OnDay(day));
            }
            stripH = EventsY + eventsH;
            stripVp.sizeDelta = new Vector2(stripW, stripH);
            TopLeft(stripContent, 0, 0, cols.Count * ColW, stripH);
            LayoutDetail();

            for (int i = 0; i < cols.Count; i++)
            {
                var go = new GameObject("Col_" + (cols[i] == Baseline ? "before" : cols[i].ToString()), typeof(RectTransform));
                go.transform.SetParent(stripContent, false);
                RectTransform crt = (RectTransform)go.transform;
                TopLeft(crt, ColX(i), 0, ColW, stripH);
                Hover h = AddHover(go);
                int day = cols[i];
                h.OnClick = () => Select(day);
                colRoots.Add(crt);
                BuildColumn(i, today, now);
            }

            // Por cima das colunas: o passado escurecido, a seleção e o marcador de agora.
            pastShade = Rect(stripContent, "Past", PastShade, 0, 0, 0, SceneH).rectTransform;
            selFrame = Img(stripContent, "Selected", "hint-menu_selector", 0, 0, ColW, stripH).rectTransform;
            // A moldura nativa é só uma barra discreta: contorno claro de 1 px em volta da cena do dia escolhido.
            var selColor = new Color(1f, 0.91f, 0.66f, 0.95f);
            Rect(selFrame, "Top", selColor, 0, 0, ColW, 1);
            Rect(selFrame, "Bottom", selColor, 0, SceneH - 1, ColW, 1);
            Rect(selFrame, "Left", selColor, 0, 0, 1, SceneH);
            Rect(selFrame, "Right", selColor, ColW - 1, 0, 1, SceneH);
            nowOutline = Rect(stripContent, "NowOutline", NowOutline, 0, 0, 3, SceneH).rectTransform;
            nowLine = Rect(stripContent, "Now", NowColor, 0, 0, 1, SceneH).rectTransform;

            if (!cols.SequenceEqual(oldCols)) DefaultScroll(today);
            else ApplyScroll();
        }

        private void BuildColumn(int i, int today, double now)
        {
            RectTransform c = colRoots[i];
            int day = cols[i];
            for (int k = eventMarks.Count - 1; k >= 0; k--) if (eventMarks[k].Col == i) eventMarks.RemoveAt(k);
            timelineTips.RemoveAll(t => t == null);
            Clear(c);
            if (day == Baseline) { BuildBaselineColumn(c); return; }

            bool isToday = day == today;
            float tod = isToday ? Frac(now) : 0.5f;
            if (WeekStrip.EnsureWheel())
            {
                RectTransform scene = WeekStrip.Scene(c, day, tod, true);
                scene.anchorMin = scene.anchorMax = scene.pivot = new Vector2(0f, 1f);
                scene.anchoredPosition = Vector2.zero;
                scene.sizeDelta = new Vector2(ColW, SceneH);
                if (isToday) { todayScene = scene; todayDaytime = WeekStrip.IsDaytime(tod); }
            }
            else
            {
                Rect(c, "Scene", BaselineBg, 0, 0, ColW, SceneH);
                if (isToday) todayScene = null;
            }

            int off = day - today;
            string rel = off == 0 ? CalendarL10n.T("today") : off == 1 ? CalendarL10n.T("tomorrow") : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("day_n", day);
            TMP_Text lbl = Label(c, "Label", day == selected ? "small_font_bold-effects_header" : "small_font-effects_regular_text", 4, LabelY, ColW - 8, TextAlignmentOptions.Left, false);
            lbl.text = rel;
            dayLabels[day] = lbl;

            if (dayLines.TryGetValue(day, out List<JournalLine> lines) && lines.Count > 0) JournalBadge(c, lines, day);

            List<Occurrence> occ = colEvents[i];
            int y = EventsY;
            for (int r = 0; r < RowsOf(occ.Count); r++)
            {
                List<Occurrence> row = occ.Skip(r * PerRow).Take(PerRow).ToList();
                int x = (ColW - row.Count * IconPitch) / 2 + (IconPitch - IconCell) / 2;
                foreach (Occurrence o in row)
                {
                    EventIcon(c, i, o, x, y, now);
                    x += IconPitch;
                }
                y += IconCell + (row.Any(HasClock) ? TimeH : 0) + 2;
            }
        }

        private void JournalBadge(RectTransform c, List<JournalLine> lines, int day)
        {
            var tmpText = lines.Count.ToString();
            Image pill = Img(c, "Journal", "craft_window-header_1-value_cell", 0, LabelY, 20, LabelH);
            TMP_Text t = Label(pill.transform, "Count", "tiny_font-effects_timer", 0, 0, 20, TextAlignmentOptions.Midline, false);
            t.text = tmpText;
            int w = Mathf.Max(18, Mathf.CeilToInt(t.GetPreferredValues(tmpText).x) + 8);
            if (w % 2 != 0) w++;
            TopLeft(pill.rectTransform, ColW - w - 2, LabelY, w, LabelH);
            t.rectTransform.sizeDelta = new Vector2(w, LabelH);
            RectTransform rt = pill.rectTransform;
            Hover h = AddHover(pill.gameObject);
            h.OnEnter = () => ShowTooltip(CalendarTooltip.JournalCluster(lines), rt);
            h.OnExit = () => HideTooltip(rt);
            h.OnClick = () => Select(day);
            timelineTips.Add(rt);
        }

        private void EventIcon(RectTransform c, int col, Occurrence o, int x, int y, double now)
        {
            var cell = new GameObject("Event_" + o.Def.Id, typeof(RectTransform));
            cell.transform.SetParent(c, false);
            RectTransform rt = (RectTransform)cell.transform;
            TopLeft(rt, x, y, IconCell, IconCell);
            Hover h = AddHover(cell);
            Occurrence occ = o;
            int day = o.Day;
            h.OnEnter = () => ShowTooltip(CalendarTooltip.Occurrence(occ), rt);
            h.OnExit = () => HideTooltip(rt);
            h.OnClick = () => Select(day);
            timelineTips.Add(rt);

            EasySpritesCollection sc = LazySingletonSO<EasySpritesCollection>.Instance;
            var mark = new EventMark { Col = col, Occ = o, State = CalendarModel.State(o, now) };
            if (sc != null && sc.HasSprite(o.Def.Art))
            {
                Sprite sp = sc.GetSprite(o.Def.Art);
                Image img = Img(cell.transform, "Icon", null, 0, 0, 0, 0);
                img.sprite = sp;
                img.type = Image.Type.Simple;
                Vector2 size = sp.rect.size * (25f / sp.pixelsPerUnit);   // metade do tamanho nativo: 1 texel = 1 px de tela
                RectTransform irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = size;
                irt.anchoredPosition = new Vector2(0f, -IconCell * 0.5f) + PackedOffset(sp) * 0.5f;
                img.color = new Color(1f, 1f, 1f, mark.State == OccState.Past ? PastIcon : 1f);
                Material key = Quests.QuestModel.PortraitMaterial();   // tira o contorno azul (cor-chave) dos ícones
                if (key != null) img.material = key;
                mark.Icon = img;
            }
            if (HasClock(o))
            {
                TMP_Text t = Label(cell.transform, "Time", mark.State == OccState.Past ? "tiny_font-hint_additional_text" : "tiny_font-effects_timer",
                    -(IconPitch - IconCell) / 2, IconCell, IconPitch, TextAlignmentOptions.Top, false);
                t.text = WeekStrip.TimeText(o, now);
                mark.Time = t;
            }
            eventMarks.Add(mark);
        }

        // O desenho não fica no meio do sprite (96×96 com a figura deslocada): devolve o quanto mover para centrar a figura.
        private static Vector2 PackedOffset(Sprite s)
        {
            if (!s.packed || s.packingMode != SpritePackingMode.Rectangle) return Vector2.zero;
            float k = 50f / s.pixelsPerUnit;
            Vector2 center = (s.textureRectOffset + s.textureRect.size * 0.5f) * k;
            Vector2 full = s.rect.size * k;
            return new Vector2(Mathf.Round(full.x * 0.5f - center.x), Mathf.Round(full.y * 0.5f - center.y));
        }

        /// <summary>A coluna de antes do diário: sem cena, só o nome, o dia em que o diário começou e quantas quests já estavam feitas.</summary>
        private void BuildBaselineColumn(RectTransform c)
        {
            Rect(c, "Bg", BaselineBg, 0, 0, ColW, stripH);
            JournalTracker j = JournalTracker.Current;
            TMP_Text name = Label(c, "Name", "small_font_bold-effects_header", 0, 12, ColW, TextAlignmentOptions.Center, false);
            name.text = CalendarL10n.T("before_short");
            if (j == null || !j.Active) return;
            TMP_Text sub = Label(c, "Since", "tiny_font-effects_timer", 4, 30, ColW - 8, TextAlignmentOptions.Center, true);
            sub.text = CalendarL10n.T("j_before", j.StartDay);
            Dictionary<string, int> counts = j.BaselineCounts();
            if (counts.TryGetValue(JournalKinds.Quest, out int q) && q > 0)
            {
                TMP_Text n = Label(c, "Quests", "small_font-effects_regular_text", 4, LabelY, ColW - 8, TextAlignmentOptions.Center, false);
                n.text = CalendarL10n.Count(JournalKinds.Quest, q);
            }
            List<string> kinds = JournalKinds.Order.Where(k => k != JournalKinds.Tutorial && k != JournalKinds.DayIcon).ToList();
            RectTransform rt = c;
            int start = j.StartDay;
            Hover h = c.GetComponent<Hover>();
            h.OnEnter = () => ShowTooltip(CalendarTooltip.Baseline(counts, kinds, start), rt);
            h.OnExit = () => HideTooltip(rt);
            timelineTips.Add(rt);
        }

        private void DefaultScroll(int today)
        {
            int idx = cols.IndexOf(today - 1);
            if (idx < 0) idx = Math.Max(0, cols.IndexOf(today));
            scrollX = ColX(idx);
            ApplyScroll();
        }

        /// <summary>Anda N dias inteiros (0 = só encaixa na coluna mais próxima).</summary>
        private void StepDays(int n)
        {
            int col = Mathf.RoundToInt(scrollX / (float)ColW) + n;
            ScrollBy(col * ColW - scrollX, 0);
        }

        internal void ScrollBy(int dx, int dy)
        {
            HideTimelineTips();
            scrollX += dx;
            ApplyScroll();
        }

        /// <summary>Rola a faixa até a coluna do dia ficar inteira na janela (não mexe se já está).</summary>
        private void Reveal(int day)
        {
            int idx = cols.IndexOf(day);
            if (idx < 0) return;
            int x = ColX(idx);
            if (x >= scrollX && x + ColW <= scrollX + stripW) return;
            int target = Mathf.RoundToInt((x - (stripW - ColW) / 2f) / ColW) * ColW;
            HideTimelineTips();
            scrollX = target;
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            if (cols.Count == 0) return;
            int maxX = Mathf.Max(0, cols.Count * ColW - stripW);
            scrollX = Mathf.Clamp(scrollX, 0, maxX);
            stripContent.anchoredPosition = new Vector2(-scrollX, 0f);
            arrowLeft.sprite = SpriteOrNull(scrollX > 0 ? "btn_i_grey-arrow_horizontal_small" : "btn_i_grey-arrow_horizontal_small-inactive");
            arrowRight.sprite = SpriteOrNull(scrollX < maxX ? "btn_i_grey-arrow_horizontal_small" : "btn_i_grey-arrow_horizontal_small-inactive");
            int a = Mathf.Clamp(scrollX / ColW, 0, cols.Count - 1), b = Mathf.Clamp((scrollX + stripW - 1) / ColW, 0, cols.Count - 1);
            int firstDay = cols[a] == Baseline && a < b ? cols[a + 1] : cols[a];
            SetText(rangeText, cols[a] == Baseline && a == b ? CalendarL10n.T("before_short") : CalendarL10n.T("days_range", firstDay, cols[b]));
        }

        private void UpdateNow(int today, double now)
        {
            int idx = cols.IndexOf(today);
            if (idx < 0 || nowLine == null) return;
            float tod = Frac(now);
            if (WeekStrip.IsDaytime(tod) != todayDaytime) BuildColumn(idx, today, now);   // virou dia/noite: a arte da cena troca
            else if (todayScene != null) WeekStrip.Retint(todayScene, tod);
            int x = ColX(idx) + TimeX(tod);
            nowLine.anchoredPosition = new Vector2(x, 0f);
            nowOutline.anchoredPosition = new Vector2(x - 1, 0f);
            pastShade.sizeDelta = new Vector2(x, SceneH);
            foreach (EventMark m in eventMarks)
            {
                OccState st = CalendarModel.State(m.Occ, now);
                if (m.Time != null) SetText(m.Time, WeekStrip.TimeText(m.Occ, now));
                if (st == m.State) continue;
                m.State = st;
                if (m.Icon != null) m.Icon.color = new Color(1f, 1f, 1f, st == OccState.Past ? PastIcon : 1f);
                if (m.Time != null) m.Time.GetComponent<TextStyleComponent>().SetTextStyle(styles[st == OccState.Past ? "tiny_font-hint_additional_text" : "tiny_font-effects_timer"]);
            }
        }

        private void UpdateSelection()
        {
            int idx = cols.IndexOf(selected);
            bool on = idx >= 0 && selFrame != null;
            if (selFrame == null) return;
            // O dia escolhido ganha a legenda em negrito (cor do cabeçalho); os outros, o texto comum.
            foreach (KeyValuePair<int, TMP_Text> kv in dayLabels)
            {
                if (kv.Value == null) continue;
                TextStyleComponent ts = kv.Value.GetComponent<TextStyleComponent>();
                TextStyle want = styles[kv.Key == selected ? "small_font_bold-effects_header" : "small_font-effects_regular_text"];
                if (ts.CurrentTextStyle != want) ts.SetTextStyle(want);
            }
            selFrame.gameObject.SetActive(on);
            if (on) selFrame.anchoredPosition = new Vector2(ColX(idx), 0f);
        }

        private void LayoutDetail()
        {
            int y = StripY + stripH + 4;
            detail.anchoredPosition = new Vector2(0f, -y);
            int h = Mathf.Max(LineH, pageH - y - HeaderH - 6);
            leftScroll.viewport.sizeDelta = new Vector2(leftScroll.viewport.sizeDelta.x, h);
            rightScroll.viewport.sizeDelta = new Vector2(rightScroll.viewport.sizeDelta.x, h);
        }

        private void HideTimelineTips()
        {
            foreach (RectTransform rt in timelineTips) HideTooltip(rt);
        }

        // ---------------- ciclo ----------------

        public void Show()
        {
            if (root == null) return;
            root.SetActive(true);
            int today = (int)Math.Floor(CalendarModel.Now);
            selected = today;
            shownToday = today;
            timelineSig = detailSig = null;
            revealPending = true;
            cols.Clear();
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
            timelineSig = detailSig = null;
            descCache.Clear();
            if (rightHeader != null) rightHeader.text = CalendarL10n.T("journal");
            if (root != null && root.activeSelf) Refresh();
        }

        public void Select(int day)
        {
            if (day == selected) { Reveal(day); return; }
            if (!cols.Contains(day)) return;
            selected = day;
            Reveal(day);
            Refresh();
        }

        /// <summary>Para os cenários: escolhe a coluna pelo índice a partir de hoje (negativo = dias passados).</summary>
        internal void SelectOffset(int offset)
        {
            int today = (int)Math.Floor(CalendarModel.Now);
            int idx = cols.IndexOf(today) + offset;
            if (idx < 0 || idx >= cols.Count) return;
            Select(cols[idx]);
        }

        public void Refresh()
        {
            if (root == null || !root.activeSelf || !CalendarModel.Ready) return;
            double now = CalendarModel.Now;
            int today = (int)Math.Floor(now);
            if (today != shownToday)
            {
                // Virou o dia com a página aberta: a escolha anda junto (mesma posição relativa).
                if (selected != Baseline && selected >= shownToday)
                    selected = Mathf.Clamp(selected + (today - shownToday), today, today + CalendarModel.DaysInWeek - 1);
                shownToday = today;
            }
            string sig = TimelineSig(today);
            if (sig != timelineSig) { timelineSig = sig; RebuildTimeline(today, now); }
            if (!cols.Contains(selected)) selected = today;
            if (revealPending) { revealPending = false; Reveal(selected); }
            SetText(header, CalendarTooltip.ResetLine(today) ?? CalendarL10n.T("tab"));
            UpdateNow(today, now);
            UpdateSelection();
            RefreshDetail(today, now);
        }

        // ---------------- dia escolhido ----------------

        private void RefreshDetail(int today, double now)
        {
            JournalTracker j = JournalTracker.Current;
            List<Occurrence> occ = selected != Baseline ? CalendarModel.OnDay(selected) : new List<Occurrence>();
            string sig = selected + "|" + LLBase.CurrentLang + "|" + (j?.Version ?? -1) + "|" + string.Join(",", occ.Select(o => o.Def.Id + "@" + o.Start.ToString("0.000") + ":" +
                (CalendarModel.State(o, now) == OccState.Past) + ":" + (CalendarTooltip.Extra(o) ?? "")));
            if (sig != detailSig)
            {
                bool sameDay = detailDay == selected;
                detailDay = selected;
                detailSig = sig;
                if (selected == Baseline)
                {
                    SetText(leftHeader, CalendarL10n.T("before_journal"));
                }
                else
                {
                    int off = selected - today;
                    string rel = off >= 0 ? CalendarL10n.Relative(off) : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("days_ago", -off);
                    string full = CalendarTooltip.DayIcon(selected) + " " + Hud.HudL10n.Day(CalendarModel.DayIdOf(selected)) + " · " + rel + " · " + CalendarL10n.T("day_n", selected);
                    // O cabeçalho tem a largura de um terço da página: se não cabe entre os enfeites, tira o nome do dia da semana (o ícone fica).
                    if (leftHeader.GetPreferredValues(full).x > leftW - 52)   // 22 px de enfeite de cada lado + folga
                        full = CalendarTooltip.DayIcon(selected) + " " + rel + " · " + CalendarL10n.T("day_n", selected);
                    SetText(leftHeader, full);
                }
                SetText(rightHeader, CalendarL10n.T("journal"));
                RebuildDetail(occ, today, now);
                if (!sameDay) leftContent.anchoredPosition = rightContent.anchoredPosition = Vector2.zero;
            }
            foreach (Entry e in entries)
            {
                string t = CalendarTooltip.When(e.Occ, now);
                if (t != e.WhenText) { e.WhenText = t; e.When.text = t; }
            }
        }

        private void RebuildDetail(List<Occurrence> occ, int today, double now)
        {
            HideTooltip();
            entries.Clear();
            tooltipTargets.Clear();
            groupInfo.Clear();
            journalLinesShown = 0;
            Clear(leftContent);
            Clear(rightContent);
            JournalTracker j = JournalTracker.Current;
            int ly = 4, ry = 4;
            if (selected == Baseline)
            {
                ly = BaselineText(leftContent, leftW, ly, j);
                ry = BaselineCounts(rightContent, rightW, ry, j);
            }
            else
            {
                ly = occ.Count > 0 ? EventsSection(leftContent, occ, leftW, ly, now) : Empty(leftContent, CalendarL10n.T("nothing"), leftW, ly);
                if (j == null || !j.Active) ry = Empty(rightContent, CalendarL10n.T("j_off"), rightW, ry);
                else if (selected > today) ry = Empty(rightContent, CalendarL10n.T("j_future"), rightW, ry);
                else
                {
                    List<JournalLine> lines = dayLines.TryGetValue(selected, out List<JournalLine> cached) ? cached : JournalView.Lines(j.OnDay(selected));
                    ry = lines.Count > 0 ? JournalSection(rightContent, lines, rightW, ry)
                        : Empty(rightContent, CalendarL10n.T(selected == today ? "j_today_empty" : "j_empty"), rightW, ry);
                }
            }
            leftContent.sizeDelta = new Vector2(leftW, ly + 4);
            rightContent.sizeDelta = new Vector2(rightW, ry + 4);
        }

        private int Empty(RectTransform content, string text, int w, int y)
        {
            TMP_Text none = Label(content, "Nothing", "small_font-no_effects", Pad, y + 10, w - 2 * Pad, TextAlignmentOptions.Center, true);
            none.text = text;
            return y + 40;
        }

        private int EventsSection(RectTransform content, List<Occurrence> occ, int w, int y, double now)
        {
            int textX = 44, textW = w - textX - Pad;
            for (int i = 0; i < occ.Count; i++)
            {
                Occurrence o = occ[i];
                var go = new GameObject("Event_" + o.Def.Id, typeof(RectTransform));
                go.transform.SetParent(content, false);
                bool past = CalendarModel.State(o, now) == OccState.Past;
                Sprite icon = SpriteOrNull(o.Def.Icon) ?? SpriteOrNull(CalendarModel.DayIdOf(o.Day));
                if (icon != null)
                {
                    int iw = Mathf.RoundToInt(icon.rect.width), ih = Mathf.RoundToInt(icon.rect.height);
                    Image img = Img(go.transform, "Icon", null, Pad + (26 - iw) / 2, 2 + (26 - ih) / 2, iw, ih);
                    img.sprite = icon;
                    img.type = Image.Type.Simple;
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
                    int dh = TextHeight(d, textW);
                    d.rectTransform.sizeDelta = new Vector2(textW, dh);
                    h += dh;
                }
                h += 6;
                TopLeft((RectTransform)go.transform, 0, y, w, h);
                y += h;
                if (i < occ.Count - 1) { Separator(content, Pad, y, w - 2 * Pad); y += 8; }
                entries.Add(new Entry { Occ = o, When = when });
            }
            return y;
        }

        /// <summary>Diário de um dia: um grupo por tipo (sermão, quests concluídas e cada tipo de desbloqueio), na ordem do diário.</summary>
        private int JournalSection(RectTransform content, List<JournalLine> lines, int w, int y)
        {
            bool first = true;
            foreach (IGrouping<string, JournalLine> g in lines.GroupBy(l => l.Kind))
            {
                if (!first) y += 8;
                first = false;
                y = JournalGroup(content, g.Key, g.ToList(), w, y);
            }
            return y;
        }

        private static bool AlwaysOpen(string kind) => kind == JournalKinds.Sermon || kind == JournalKinds.Quest;

        private bool IsOpen(string kind, int count) =>
            AlwaysOpen(kind) || (groupOpen.TryGetValue(selected + ":" + kind, out bool o) ? o : count <= OpenLimit);

        private string GroupTitle(string kind, int count)
        {
            if (kind == JournalKinds.Sermon) return CalendarL10n.T("j_sermon_h") + " · " + count;
            if (kind == JournalKinds.Quest) return CalendarL10n.T("j_completed_h") + " · " + count;
            return CalendarL10n.Count(kind, count);
        }

        private int JournalGroup(RectTransform content, string kind, List<JournalLine> lines, int w, int y)
        {
            bool open = IsOpen(kind, lines.Count);
            bool fixedOpen = AlwaysOpen(kind);
            var hg = new GameObject("Group_" + kind, typeof(RectTransform));
            hg.transform.SetParent(content, false);
            RectTransform hrt = (RectTransform)hg.transform;
            TopLeft(hrt, Pad, y, w - 2 * Pad, 18);
            int titleX = 0;
            if (!fixedOpen)
            {
                Hover hv = AddHover(hg);
                string k = kind;
                hv.OnClick = () => ToggleGroup(k);
                // Seta nativa: para baixo = aberto; a horizontal (espelhada, aponta para a direita) = recolhido.
                Image arrow = open ? Img(hg.transform, "Arrow", "btn_i_grey-arrow_vertical_small-d", 0, 5, 14, 8) : Img(hg.transform, "Arrow", "btn_i_grey-arrow_horizontal_small", 3, 3, 8, 12);
                if (!open) arrow.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                if (!open) arrow.rectTransform.pivot = new Vector2(1f, 1f);
                if (!open) arrow.rectTransform.anchoredPosition = new Vector2(3, -3);
                titleX = 18;
            }
            TMP_Text h = Label(hg.transform, "Title", "small_font_bold-sub_header_2", titleX, 0, w - 2 * Pad - titleX, TextAlignmentOptions.Left, false);
            h.text = GroupTitle(kind, lines.Count);
            y += 18;
            Separator(content, Pad, y, w - 2 * Pad);
            y += 6;
            groupInfo.Add(kind + "=" + lines.Count + (open ? "+" : "-"));
            if (!open) return y;
            for (int i = 0; i < lines.Count; i++)
            {
                y = JournalLineRow(content, lines[i], w, y);
                journalLinesShown++;
            }
            return y;
        }

        private string DescOf(JournalLine l)
        {
            string key = LLBase.CurrentLang + ":" + l.Kind + ":" + l.Id;
            if (!descCache.TryGetValue(key, out string d)) descCache[key] = d = JournalView.DescriptionOf(l);
            return d;
        }

        /// <summary>Uma anotação: nome inteiro (quebra de linha), tipo e hora à direita, e embaixo a descrição que o jogo tem para ela.</summary>
        private int JournalLineRow(RectTransform content, JournalLine line, int w, int y)
        {
            var go = new GameObject("Line_" + line.Kind + "_" + line.Id, typeof(RectTransform));
            go.transform.SetParent(content, false);
            RectTransform rt = (RectTransform)go.transform;
            int inner = w - 2 * Pad, nameW = inner - KindW - 6;
            TMP_Text name = Label(go.transform, "Name", "small_font_bold-effects_header", 0, 0, nameW, TextAlignmentOptions.TopLeft, true);
            name.text = line.Name;
            int nh = Mathf.Max(18, TextHeight(name, nameW));
            name.rectTransform.sizeDelta = new Vector2(nameW, nh);
            TMP_Text kind = Label(go.transform, "Kind", "tiny_font-effects_timer", inner - KindW, 2, KindW, TextAlignmentOptions.TopRight, false);
            kind.text = JournalView.KindLabel(line.Kind) + " · " + (line.Approx ? "~" : "") + CalendarModel.Clock(line.Tod);
            int h = nh;
            string desc = DescOf(line);
            if (desc != null)
            {
                TMP_Text d = Label(go.transform, "Description", "small_font-effects_regular_text", 0, h, inner, TextAlignmentOptions.TopLeft, true);
                d.text = desc;
                int dh = TextHeight(d, inner);
                d.rectTransform.sizeDelta = new Vector2(inner, dh);
                h += dh;
            }
            h += 6;
            TopLeft(rt, Pad, y, inner, h);
            Hover hv = AddHover(go);
            hv.OnEnter = () => ShowTooltip(JournalView.Tooltip(line), rt);
            hv.OnExit = () => HideTooltip(rt);
            tooltipTargets.Add(rt);
            return y + h + 4;
        }

        /// <summary>Abre/fecha um grupo do diário do dia escolhido (os fixos não fecham).</summary>
        internal void ToggleGroup(string kind)
        {
            if (AlwaysOpen(kind) || root == null) return;
            string key = selected + ":" + kind;
            int count = dayLines.TryGetValue(selected, out List<JournalLine> l) ? l.Count(x => x.Kind == kind) : 0;
            groupOpen[key] = !IsOpen(kind, count);
            detailSig = null;
            double now = CalendarModel.Now;
            RefreshDetail((int)Math.Floor(now), now);
        }

        /// <summary>Para os cenários: rola o diário (px de arte a partir do topo).</summary>
        internal void ScrollJournal(int y) => rightContent.anchoredPosition = new Vector2(0f, y);

        private int BaselineText(RectTransform content, int w, int y, JournalTracker j)
        {
            if (j == null || !j.Active) return Empty(content, CalendarL10n.T("j_off"), w, y);
            TMP_Text d = Label(content, "Text", "small_font-effects_regular_text", Pad, y, w - 2 * Pad, TextAlignmentOptions.TopLeft, true);
            d.text = CalendarL10n.T("j_baseline_d", j.StartDay);
            int dh = TextHeight(d, w - 2 * Pad);
            d.rectTransform.sizeDelta = new Vector2(w - 2 * Pad, dh);
            return y + dh + 8;
        }

        private int BaselineCounts(RectTransform content, int w, int y, JournalTracker j)
        {
            if (j == null || !j.Active) return y;
            Dictionary<string, int> counts = j.BaselineCounts();
            foreach (string k in JournalKinds.Order)
            {
                if (!counts.TryGetValue(k, out int n)) continue;
                TMP_Text name = Label(content, "Count_" + k, "small_font_bold-effects_header", Pad, y + 3, w - 2 * Pad, TextAlignmentOptions.Left, false);
                name.text = CalendarL10n.Count(k, n);
                y += LineH;
            }
            return y;
        }

        // ---------------- tooltips ----------------

        private void ShowTooltip(List<LazyWidgetDataBase> list, RectTransform target)
        {
            UITooltip native = Native.Instance;
            if (native == null || target == null || list == null || list.Count == 0) return;
            Native.ShowAt.Invoke(native, new object[] { list, target, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.TopRight });
        }

        private static void HideTooltip(RectTransform target)
        {
            if (target != null && UITooltip.IsTooltipShowingAtTarget(target)) UITooltip.Hide();
        }

        private void HideTooltip()
        {
            foreach (RectTransform rt in tooltipTargets) HideTooltip(rt);
            HideTimelineTips();
        }

        private static void SetText(TMP_Text t, string s)
        {
            if (t != null && t.text != s) t.text = s;
        }

        internal string Describe()
        {
            return $"selected={(selected == Baseline ? "antes" : selected.ToString())} cols={cols.Count} scroll={scrollX} strip={stripW}x{stripH} icones={eventMarks.Count} marcas={timelineTips.Count} " +
                   $"entries={entries.Count} linhas={tooltipTargets.Count} grupos=[{string.Join(" ", groupInfo)}] active={(root != null && root.activeSelf)} header='{header?.text}' range='{rangeText?.text}'";
        }
    }
}
