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
    /// Página do calendário no TAB: linha do tempo horizontal em cima e o dia escolhido embaixo. Unidades = pixels de arte.
    /// Linha do tempo: uma coluna por dia (do início do diário até o fim da semana, ontem na borda esquerda) com eixo de
    /// horas e a noite do jogo (18:00–06:00, <c>EnvironmentEngine.GetTimeOfDayType</c>) mais escura; uma faixa por evento
    /// já revelado e duas do diário (quests e desbloqueios). Janela com hora = barra na cor do sigilo do dia; dia sem hora =
    /// trilho escuro com o sigilo; instante = marca com a hora. O que já passou fica sombreado até a linha do agora.
    /// Peças nativas: cabeçalho <c>comm-header_2</c>, trilho <c>comm-progress_bar_bg</c>, barras
    /// <c>inspiration-bar_cell_fill-*</c>/<c>comm-progress-yellow</c>, marcas do diário <c>craft_window-progress_cell-*</c>,
    /// seleção <c>hint-menu_selector</c>, setas <c>btn_i_grey-arrow_horizontal_small</c>, relógio na
    /// <c>craft_window-header_1-value_cell</c>. Passar o mouse numa marca abre o tooltip nativo; clicar escolhe o dia;
    /// a roda e o arrasto andam pelos dias (a roda sobre os nomes das faixas rola as faixas).
    /// Embaixo: eventos do dia (formato de Buffs/Perks) e o diário dele, cada linha com o tooltip da coisa.
    /// </summary>
    internal sealed class CalendarPage
    {
        private const string Name = "GK2Hud.CalendarPage";
        private const int X0 = 4, LabelW = 114, GridX = X0 + LabelW, ColW = 74, VisibleCols = 7, GridW = ColW * VisibleCols;
        private const int HeaderH = 26, StripY = 30, StripH = 30, AxisH = 14, LanesY = StripY + StripH + AxisH + 2, LaneH = 18, MaxLanes = 9;
        private const int Pad = 10, LineH = 22, SermonDetailH = 18, ColGap = 8, ClusterPx = 10;
        private const float DimAlpha = 0.4f;            // UIHUDWheelElement.iconColorInactive
        private const float DayFrom = 0.25f, DayTo = 0.75f;
        private const int Baseline = int.MinValue;

        private static readonly Color Night = new Color(0f, 0f, 0f, 0.22f);
        private static readonly Color PastShade = new Color(0.05f, 0.04f, 0.04f, 0.5f);
        private static readonly Color Band = new Color(0f, 0f, 0f, 0.14f);
        private static readonly Color DayLine = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color Tick = new Color(0.62f, 0.52f, 0.40f, 0.6f);
        private static readonly Color NowColor = new Color(1f, 0.78f, 0.25f, 0.95f);
        private static readonly Color SelectedCol = new Color(1f, 0.9f, 0.6f, 0.08f);

        private GameObject root;
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, TextStyle> styles = new Dictionary<string, TextStyle>();
        private readonly List<TMP_Text> labels = new List<TMP_Text>();
        private int pageW, pageH;

        // linha do tempo
        private RectTransform stripVp, stripContent, labelsVp, labelsContent, gridVp, gridContent, detail;
        private TMP_Text header, rangeText;
        private Image arrowLeft, arrowRight, arrowUp, arrowDown;
        private TMP_Text moreLanes;
        private RectTransform nowLine, nowLineStrip, nowPill, pastShade, pastShadeStrip, selStrip, selGrid;
        private TMP_Text nowText;
        private readonly List<int> cols = new List<int>();
        private readonly List<Lane> lanes = new List<Lane>();
        private readonly List<RectTransform> timelineTips = new List<RectTransform>();
        private int lanesH, scrollX, scrollY;
        private string timelineSig;

        // detalhe
        private TMP_Text leftHeader, rightHeader;
        private RectTransform leftContent, rightContent;
        private ScrollRect leftScroll, rightScroll;
        private readonly List<Entry> entries = new List<Entry>();
        private readonly List<RectTransform> tooltipTargets = new List<RectTransform>();
        private string detailSig;

        private int selected = -1;        // dia absoluto escolhido (Baseline = antes do diário)
        private int shownToday = -1;

        private sealed class Lane
        {
            public EventDef Def;          // faixa de evento
            public string Journal;        // faixa do diário: "quest" ou "unlock"
            public string Key => Def != null ? Def.Id : "j_" + Journal;
        }

        private sealed class Entry
        {
            public Occurrence Occ;
            public TMP_Text When;
            public string WhenText;
        }

        /// <summary>Só pointer enter/exit/click: o scroll e o arrasto sobem para quem rola (EventTrigger os engoliria).</summary>
        internal sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Action OnEnter, OnExit, OnClick;
            public void OnPointerEnter(PointerEventData e) => OnEnter?.Invoke();
            public void OnPointerExit(PointerEventData e) => OnExit?.Invoke();
            public void OnPointerClick(PointerEventData e) { if (!e.dragging) OnClick?.Invoke(); }
        }

        /// <summary>Roda e arrasto da linha do tempo (sem ScrollRect: dias e faixas rolam em eixos e áreas diferentes).</summary>
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
            "comm-header_2", "comm-header_2-dec_side_1", "widget_perks-separator_hor-drk_1", "comm-progress_bar_bg", "comm-progress-yellow",
            "inspiration-bar_cell_fill-blue", "inspiration-bar_cell_fill-green", "inspiration-bar_cell_fill-orange", "inspiration-bar_cell_fill-red",
            "inspiration-bar_cell_fill-yellow", "craft_window-progress_cell-green", "craft_window-progress_cell-red", "hint-menu_selector",
            "btn_i_grey-arrow_horizontal_small", "btn_i_grey-arrow_horizontal_small-inactive", "btn_i_grey-arrow_vertical_small-u", "btn_i_grey-arrow_vertical_small-d", "craft_window-header_1-value_cell", "hud-world_zone-reputation_bar",
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

        /// <summary>Barra de cada dia na cor do sigilo dele (as células coloridas da janela de Inspirações).</summary>
        private static string BarSprite(string dayId)
        {
            switch (dayId)
            {
                case "day_pride": return "inspiration-bar_cell_fill-blue";
                case "day_lust": return "inspiration-bar_cell_fill-red";
                case "day_gluttony": return "comm-progress-yellow";
                case "day_envy": return "inspiration-bar_cell_fill-green";
                case "day_wrath": return "inspiration-bar_cell_fill-orange";
                case "day_sloth": return "inspiration-bar_cell_fill-yellow";
                default: return "hud-world_zone-reputation_bar";
            }
        }

        private static string JournalSprite(string lane) => lane == "quest" ? "comm-progress-yellow" : "craft_window-progress_cell-green";

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

            header = Header(root.transform, X0, 0, pageW - 2 * X0);

            stripVp = Viewport(root.transform, "Days", GridX, StripY, GridW, StripH + AxisH, out stripContent);
            labelsVp = Viewport(root.transform, "Lanes", X0, LanesY, LabelW, LaneH, out labelsContent);
            gridVp = Viewport(root.transform, "Grid", GridX, LanesY, GridW, LaneH, out gridContent);
            AddPan(stripVp, wheelDays: true, dragX: true, dragY: false);
            AddPan(gridVp, wheelDays: true, dragX: true, dragY: true);
            AddPan(labelsVp, wheelDays: false, dragX: false, dragY: true);

            // A seta nativa aponta para a esquerda; a da direita é a mesma espelhada no lugar.
            arrowLeft = Img(root.transform, "Earlier", "btn_i_grey-arrow_horizontal_small", X0 + 6, StripY + 4, 8, 12);
            arrowRight = Img(root.transform, "Later", "btn_i_grey-arrow_horizontal_small", X0 + LabelW - 16, StripY + 4, 8, 12);
            arrowRight.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
            arrowRight.rectTransform.pivot = new Vector2(1f, 1f);
            foreach (Image a in new[] { arrowLeft, arrowRight })
            {
                Image arrow = a;
                arrow.raycastTarget = true;
                Hover h = arrow.gameObject.AddComponent<Hover>();
                h.OnClick = () => StepDays(arrow == arrowLeft ? -1 : 1);
            }
            rangeText = Label(root.transform, "Range", "tiny_font-effects_timer", X0 + 16, StripY + 4, LabelW - 32, TextAlignmentOptions.Center, false);

            // Faixas que não cabem: setas na linha do eixo, ao lado dos nomes (a roda sobre os nomes também rola).
            arrowUp = Img(root.transform, "LanesUp", "btn_i_grey-arrow_vertical_small-u", X0 + 4, StripY + StripH + 3, 14, 8);
            arrowDown = Img(root.transform, "LanesDown", "btn_i_grey-arrow_vertical_small-d", X0 + 20, StripY + StripH + 3, 14, 8);
            foreach (Image a in new[] { arrowUp, arrowDown })
            {
                Image arrow = a;
                arrow.raycastTarget = true;
                Hover h = arrow.gameObject.AddComponent<Hover>();
                h.OnClick = () => ScrollBy(0, arrow == arrowUp ? -LaneH : LaneH);
            }
            moreLanes = Label(root.transform, "MoreLanes", "tiny_font-effects_timer", X0 + 38, StripY + StripH - 1, LabelW - 40, TextAlignmentOptions.Left, false);

            var d = new GameObject("Day", typeof(RectTransform));
            d.transform.SetParent(root.transform, false);
            detail = (RectTransform)d.transform;
            int detW = (pageW - 2 * X0 - ColGap) / 2;
            TopLeft(detail, 0, LanesY + LaneH + 6, pageW, 100);
            leftHeader = Header(detail, X0, 0, detW);
            rightHeader = Header(detail, X0 + detW + ColGap, 0, detW);
            leftScroll = Scroll(detail, "Events", X0, HeaderH + 2, detW, 60, out leftContent);
            rightScroll = Scroll(detail, "Journal", X0 + detW + ColGap, HeaderH + 2, detW, 60, out rightContent);
            leftScroll.scrollSensitivity = rightScroll.scrollSensitivity = LineH;
            Restyle();
        }

        /// <summary>Roda: dias inteiros (ou faixas, sobre os nomes). Arrasto: "pega" a linha do tempo e encaixa no dia ao soltar.</summary>
        private void AddPan(RectTransform vp, bool wheelDays, bool dragX, bool dragY)
        {
            Pan p = vp.gameObject.AddComponent<Pan>();
            p.OnWheel = delta =>
            {
                if (Mathf.Abs(delta) < 0.01f) return;
                int steps = -Mathf.Clamp(Mathf.RoundToInt(delta), -3, 3);
                if (steps == 0) steps = delta > 0 ? -1 : 1;
                if (wheelDays) StepDays(steps);
                else ScrollBy(0, steps * LaneH);
            };
            p.OnDragEnd = () => { if (dragX) StepDays(0); };
            p.OnDragBy = delta =>
            {
                float scale = Mathf.Max(1f, LazyUI.ScaleFactor);
                ScrollBy(dragX ? -Mathf.RoundToInt(delta.x / scale) : 0, dragY ? Mathf.RoundToInt(delta.y / scale) : 0);
            };
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

        // ---------------- linha do tempo ----------------

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

        private static List<Lane> Lanes()
        {
            var list = new List<Lane>();
            foreach (EventDef e in CalendarModel.Defs) if (CalendarModel.Known(e)) list.Add(new Lane { Def = e });
            JournalTracker j = JournalTracker.Current;
            if (j != null && j.Active)
            {
                list.Add(new Lane { Journal = "quest" });
                list.Add(new Lane { Journal = "unlock" });
            }
            return list;
        }

        /// <summary>Muda quando o conjunto de colunas, faixas ou marcas muda (não a cada quadro).</summary>
        private static string TimelineSig(int today)
        {
            JournalTracker j = JournalTracker.Current;
            var parts = new List<string> { today.ToString(), LLBase.CurrentLang, (j != null && j.Active ? j.Version : -1).ToString(), CalendarModel.RevealAll.ToString() };
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

        private int ColX(int idx) => idx * ColW;

        private static int TimeX(float tod) => Mathf.Clamp(Mathf.RoundToInt(tod * ColW), 0, ColW);

        private void RebuildTimeline(int today, double now)
        {
            HideTimelineTips();
            timelineTips.Clear();
            Clear(stripContent);
            Clear(gridContent);
            Clear(labelsContent);
            List<int> oldCols = new List<int>(cols);
            cols.Clear();
            cols.AddRange(Columns(today));
            lanes.Clear();
            lanes.AddRange(Lanes());

            lanesH = Mathf.Max(1, Mathf.Min(lanes.Count, MaxLanes)) * LaneH;
            int contentW = cols.Count * ColW, contentH = Mathf.Max(1, lanes.Count) * LaneH;
            labelsVp.sizeDelta = new Vector2(LabelW, lanesH);
            gridVp.sizeDelta = new Vector2(GridW, lanesH);
            TopLeft(stripContent, 0, 0, contentW, StripH + AxisH);
            TopLeft(gridContent, 0, 0, contentW, contentH);
            TopLeft(labelsContent, 0, 0, LabelW, contentH);
            LayoutDetail();

            // Fundo: faixas alternadas, noite, dia escolhido, divisas dos dias.
            for (int l = 0; l < lanes.Count; l++)
            {
                if (l % 2 == 1)
                {
                    Rect(gridContent, "Band", Band, 0, l * LaneH, contentW, LaneH);
                    Rect(labelsContent, "Band", Band, 0, l * LaneH, LabelW, LaneH);
                }
            }
            for (int i = 0; i < cols.Count; i++)
            {
                if (cols[i] == Baseline) continue;
                int x = ColX(i);
                Rect(gridContent, "Night", Night, x, 0, TimeX(DayFrom), contentH);
                Rect(gridContent, "Night", Night, x + TimeX(DayTo), 0, ColW - TimeX(DayTo), contentH);
                Rect(stripContent, "Night", Night, x, StripH, TimeX(DayFrom), AxisH);
                Rect(stripContent, "Night", Night, x + TimeX(DayTo), StripH, ColW - TimeX(DayTo), AxisH);
            }
            selGrid = Rect(gridContent, "Selected", SelectedCol, 0, 0, ColW, contentH).rectTransform;
            selStrip = Img(stripContent, "Selected", "hint-menu_selector", 0, 0, ColW, StripH).rectTransform;
            for (int i = 0; i < cols.Count; i++)
            {
                int x = ColX(i);
                if (i > 0)
                {
                    Rect(gridContent, "DayLine", DayLine, x, 0, 1, contentH);
                    Rect(stripContent, "DayLine", DayLine, x, 0, 1, StripH + AxisH);
                }
                int day = cols[i];
                foreach (RectTransform parent in new[] { gridContent, stripContent })
                {
                    var hit = new GameObject("Hit", typeof(RectTransform));
                    hit.transform.SetParent(parent, false);
                    TopLeft((RectTransform)hit.transform, x, 0, ColW, parent == gridContent ? contentH : StripH + AxisH);
                    Hover h = AddHover(hit);
                    h.OnClick = () => Select(day);
                }
                DayHeader(i, today);
            }
            Rect(stripContent, "Axis", DayLine, 0, StripH + AxisH - 1, contentW, 1);

            // Nomes e marcas de cada faixa.
            int journalTop = -1;
            for (int l = 0; l < lanes.Count; l++)
            {
                if (lanes[l].Journal != null && journalTop < 0) journalTop = l;
                LaneLabel(lanes[l], l);
            }
            for (int i = 0; i < cols.Count; i++) ColumnMarks(i, today, now);
            if (journalTop > 0)
            {
                Separator(gridContent, 0, journalTop * LaneH - 2, contentW);
                Separator(labelsContent, 0, journalTop * LaneH - 2, LabelW);
            }

            // Por cima: o passado sombreado e a linha do agora.
            pastShade = Rect(gridContent, "Past", PastShade, 0, 0, 0, contentH).rectTransform;
            // Nos nomes, só os dias que já acabaram; no eixo, até a hora atual.
            int todayIdx = cols.IndexOf(today);
            if (todayIdx > 0) Rect(stripContent, "PastDays", PastShade, 0, 0, ColX(todayIdx), StripH);
            pastShadeStrip = Rect(stripContent, "Past", PastShade, 0, StripH, 0, AxisH).rectTransform;
            nowLine = Rect(gridContent, "Now", NowColor, 0, 0, 1, contentH).rectTransform;
            nowLineStrip = Rect(stripContent, "Now", NowColor, 0, StripH, 1, AxisH).rectTransform;
            Image pill = Img(stripContent, "Clock", "craft_window-header_1-value_cell", 0, StripH - 1, 34, 14);
            nowPill = pill.rectTransform;
            nowText = Label(pill.transform, "Text", "tiny_font-effects_timer", 0, 0, 34, TextAlignmentOptions.Center, false);
            nowText.rectTransform.sizeDelta = new Vector2(34, 14);
            nowText.alignment = TextAlignmentOptions.Midline;

            if (!cols.SequenceEqual(oldCols)) DefaultScroll(today);
            else ApplyScroll();
        }

        private void DayHeader(int idx, int today)
        {
            int x = ColX(idx), day = cols[idx];
            TMP_Text name = Label(stripContent, "Name", "small_font_bold-effects_header", x + 3, 1, ColW - 4, TextAlignmentOptions.Left, false);
            TMP_Text rel = Label(stripContent, "Rel", "tiny_font-effects_timer", x + 4, 16, ColW - 6, TextAlignmentOptions.Left, false);
            name.overflowMode = rel.overflowMode = TextOverflowModes.Ellipsis;
            if (day == Baseline)
            {
                name.text = CalendarL10n.T("before_short");
                JournalTracker j = JournalTracker.Current;
                rel.text = j != null ? CalendarL10n.T("j_before", j.StartDay) : "";
                return;
            }
            string id = CalendarModel.DayIdOf(day);
            name.text = (id?.FontIcon() ?? "") + Hud.HudL10n.Day(id);
            int off = day - today;
            rel.text = off == 0 ? CalendarL10n.T("today") : off == 1 ? CalendarL10n.T("tomorrow") : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("day_n", day);
            if (off < 0) rel.text = CalendarL10n.T("day_n", day);
            foreach ((float tod, string txt) in new[] { (DayFrom, "6"), (0.5f, "12"), (DayTo, "18") })
            {
                int tx = x + TimeX(tod);
                Rect(stripContent, "Tick", Tick, tx, StripH + AxisH - 4, 1, 3);
                TMP_Text t = Label(stripContent, "Hour", "tiny_font-hint_additional_text", tx - 10, StripH - 2, 20, TextAlignmentOptions.Center, false);
                t.text = txt;
            }
        }

        private void LaneLabel(Lane lane, int l)
        {
            int y = l * LaneH;
            string sw = lane.Def != null ? (lane.Def.DayId != null ? BarSprite(lane.Def.DayId) : BarSprite(null)) : JournalSprite(lane.Journal);
            Img(labelsContent, "Swatch", sw, 6, y + 4, 8, 10);
            TMP_Text t = Label(labelsContent, "Name", "small_font-effects_regular_text", 18, y + 1, LabelW - 20, TextAlignmentOptions.Left, false);
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.text = lane.Def != null ? ShortTitle(lane.Def) : CalendarL10n.T("lane_" + lane.Journal);
        }

        private static string ShortTitle(EventDef e) => CalendarL10n.TOrNull("evs_" + e.Id) ?? e.Title;

        private void ColumnMarks(int idx, int today, double now)
        {
            int x = ColX(idx), day = cols[idx];
            JournalTracker j = JournalTracker.Current;
            if (day == Baseline)
            {
                if (j == null || !j.Active) return;
                Dictionary<string, int> counts = j.BaselineCounts();
                int quests = counts.TryGetValue(JournalKinds.Quest, out int q) ? q : 0;
                int unlocks = counts.Where(kv => JournalKinds.Order.Contains(kv.Key) && kv.Key != JournalKinds.Quest && kv.Key != JournalKinds.Tutorial && kv.Key != JournalKinds.DayIcon).Sum(kv => kv.Value);
                BaselineMark(x, "quest", quests, counts, new[] { JournalKinds.Quest }, j.StartDay);
                BaselineMark(x, "unlock", unlocks, counts, JournalKinds.Order.Where(k => k != JournalKinds.Quest && k != JournalKinds.Tutorial && k != JournalKinds.DayIcon), j.StartDay);
                return;
            }
            foreach (Occurrence o in CalendarModel.OnDay(day))
            {
                int l = lanes.FindIndex(ln => ln.Def == o.Def);
                if (l >= 0) EventMark(o, x, l * LaneH, day);
            }
            if (j == null || !j.Active || day > today) return;
            List<JournalLine> lines = JournalView.Lines(j.OnDay(day));
            foreach (IGrouping<int, JournalLine> g in lines.GroupBy(LaneOf))
            {
                if (g.Key < 0) continue;
                var cluster = new List<JournalLine>();
                int start = -100;
                foreach (JournalLine line in g.OrderBy(z => z.Tod))
                {
                    int lx = TimeX(line.Tod);
                    if (cluster.Count > 0 && lx - start > ClusterPx) { JournalMark(cluster, x, g.Key * LaneH, day); cluster = new List<JournalLine>(); }
                    if (cluster.Count == 0) start = lx;
                    cluster.Add(line);
                }
                if (cluster.Count > 0) JournalMark(cluster, x, g.Key * LaneH, day);
            }
        }

        private int LaneOf(JournalLine line)
        {
            if (line.Kind == JournalKinds.Sermon)
            {
                int s = lanes.FindIndex(ln => ln.Def != null && ln.Def.Id == "sermon");
                if (s >= 0) return s;
            }
            return lanes.FindIndex(ln => ln.Journal == (line.Kind == JournalKinds.Quest ? "quest" : "unlock"));
        }

        private void EventMark(Occurrence o, int colX, int y, int day)
        {
            GameObject go;
            if (o.AllDay)
            {
                // Dia certo, hora não: trilho escuro do jogo com o sigilo no meio.
                Image track = Img(gridContent, "Day_" + o.Def.Id, "comm-progress_bar_bg", colX + 3, y + 3, ColW - 6, 12);
                TMP_Text icon = Label(track.transform, "Sigil", "small_font-effects_regular_text", 0, -2, ColW - 6, TextAlignmentOptions.Center, false);
                icon.text = CalendarModel.DayIdOf(day)?.FontIcon() ?? "";
                go = track.gameObject;
            }
            else if (!o.Instant)
            {
                int x0 = TimeX(o.Start), x1 = Mathf.Max(x0 + 8, TimeX(o.End));
                go = Img(gridContent, "Window_" + o.Def.Id, BarSprite(o.Def.DayId ?? CalendarModel.DayIdOf(day)), colX + x0, y + 4, x1 - x0, 10).gameObject;
            }
            else
            {
                int px = TimeX(o.Start);
                int mx = Mathf.Clamp(px - 4, 0, ColW - 8);
                go = Img(gridContent, "At_" + o.Def.Id, BarSprite(o.Def.DayId), colX + mx, y + 4, 8, 10).gameObject;
                if (!o.Def.NoClock)
                {
                    bool right = mx + 8 + 30 <= ColW;
                    TMP_Text t = Label(gridContent, "Clock", "tiny_font-effects_timer", right ? colX + mx + 10 : colX + mx - 32, y + 1, 30,
                        right ? TextAlignmentOptions.Left : TextAlignmentOptions.Right, false);
                    t.text = CalendarModel.Clock(o.Start);
                }
            }
            Occurrence occ = o;
            RectTransform rt = (RectTransform)go.transform;
            Hover h = AddHover(go);
            h.OnEnter = () => ShowTooltip(CalendarTooltip.Occurrence(occ), rt);
            h.OnExit = () => HideTooltip(rt);
            h.OnClick = () => Select(day);
            timelineTips.Add(rt);
        }

        private void JournalMark(List<JournalLine> cluster, int colX, int y, int day)
        {
            int px = TimeX(cluster[0].Tod);
            int mx = Mathf.Clamp(px - 4, 0, ColW - 8);
            JournalLine first = cluster[0];
            string sprite = first.Sermon != null ? (first.Sermon.ok ? "craft_window-progress_cell-green" : "craft_window-progress_cell-red")
                : JournalSprite(first.Kind == JournalKinds.Quest ? "quest" : "unlock");
            Image img = Img(gridContent, "Journal", sprite, colX + mx, y + 4, 8, 10);
            if (cluster.Count > 1)
            {
                TMP_Text t = Label(gridContent, "Count", "tiny_font-effects_timer", colX + mx + 10, y + 1, 24, TextAlignmentOptions.Left, false);
                t.text = "×" + cluster.Count;
            }
            List<JournalLine> lines = cluster;
            RectTransform rt = img.rectTransform;
            Hover h = AddHover(img.gameObject);
            h.OnEnter = () => ShowTooltip(lines.Count == 1 ? JournalView.Tooltip(lines[0]) : CalendarTooltip.JournalCluster(lines), rt);
            h.OnExit = () => HideTooltip(rt);
            h.OnClick = () => Select(day);
            timelineTips.Add(rt);
        }

        private void BaselineMark(int colX, string lane, int count, Dictionary<string, int> counts, IEnumerable<string> kinds, int startDay)
        {
            int l = lanes.FindIndex(ln => ln.Journal == lane);
            if (l < 0 || count <= 0) return;
            int y = l * LaneH;
            Image img = Img(gridContent, "Baseline", JournalSprite(lane), colX + ColW / 2 - 12, y + 4, 8, 10);
            TMP_Text t = Label(gridContent, "Count", "tiny_font-effects_timer", colX + ColW / 2 - 2, y + 1, 30, TextAlignmentOptions.Left, false);
            t.text = "×" + count;
            RectTransform rt = img.rectTransform;
            List<string> k = kinds.ToList();
            Hover h = AddHover(img.gameObject);
            h.OnEnter = () => ShowTooltip(CalendarTooltip.Baseline(counts, k, startDay), rt);
            h.OnExit = () => HideTooltip(rt);
            h.OnClick = () => Select(Baseline);
            timelineTips.Add(rt);
        }

        private void DefaultScroll(int today)
        {
            int idx = cols.IndexOf(today - 1);
            if (idx < 0) idx = Math.Max(0, cols.IndexOf(today));
            scrollX = ColX(idx);
            scrollY = 0;
            ApplyScroll();
        }

        /// <summary>Anda N dias inteiros (0 = só encaixa na coluna mais próxima).</summary>
        private void StepDays(int n)
        {
            int col = Mathf.RoundToInt(scrollX / (float)ColW) + n;
            ScrollBy(col * ColW - scrollX, 0);
        }

        private void ScrollBy(int dx, int dy)
        {
            HideTimelineTips();
            scrollX += dx;
            scrollY += dy;
            ApplyScroll();
        }

        private void ApplyScroll()
        {
            int maxX = Mathf.Max(0, cols.Count * ColW - GridW);
            int maxY = Mathf.Max(0, lanes.Count * LaneH - lanesH);
            scrollX = Mathf.Clamp(scrollX, 0, maxX);
            scrollY = Mathf.Clamp(scrollY, 0, maxY);
            stripContent.anchoredPosition = new Vector2(-scrollX, 0f);
            gridContent.anchoredPosition = new Vector2(-scrollX, scrollY);
            labelsContent.anchoredPosition = new Vector2(0f, scrollY);
            arrowLeft.sprite = SpriteOrNull(scrollX > 0 ? "btn_i_grey-arrow_horizontal_small" : "btn_i_grey-arrow_horizontal_small-inactive");
            arrowRight.sprite = SpriteOrNull(scrollX < maxX ? "btn_i_grey-arrow_horizontal_small" : "btn_i_grey-arrow_horizontal_small-inactive");
            bool overflow = maxY > 0;
            arrowUp.gameObject.SetActive(overflow);
            arrowDown.gameObject.SetActive(overflow);
            arrowUp.color = new Color(1f, 1f, 1f, scrollY > 0 ? 1f : DimAlpha);
            arrowDown.color = new Color(1f, 1f, 1f, scrollY < maxY ? 1f : DimAlpha);
            int below = (maxY - scrollY + LaneH - 1) / LaneH;
            SetText(moreLanes, overflow && below > 0 ? CalendarL10n.T("lanes_below", below) : "");
            int a = Mathf.Clamp(scrollX / ColW, 0, cols.Count - 1), b = Mathf.Clamp((scrollX + GridW - 1) / ColW, 0, cols.Count - 1);
            int firstDay = cols[a] == Baseline && a < b ? cols[a + 1] : cols[a];
            SetText(rangeText, cols[a] == Baseline && a == b ? CalendarL10n.T("before_short") : CalendarL10n.T("days_range", firstDay, cols[b]));
        }

        private void UpdateNow(int today, double now)
        {
            int idx = cols.IndexOf(today);
            if (idx < 0 || nowLine == null) return;
            int x = ColX(idx) + TimeX((float)(now - today));
            nowLine.anchoredPosition = new Vector2(x, 0f);
            nowLineStrip.anchoredPosition = new Vector2(x, -StripH);
            nowPill.anchoredPosition = new Vector2(Mathf.Clamp(x - 17, 0, cols.Count * ColW - 34), -(StripH - 1));
            pastShade.sizeDelta = new Vector2(x, pastShade.sizeDelta.y);
            pastShadeStrip.sizeDelta = new Vector2(x, pastShadeStrip.sizeDelta.y);
            SetText(nowText, Hud.HudFeature.Clock(CalendarModel.Env.TimeOfDay, 10));
        }

        private void UpdateSelection()
        {
            int idx = cols.IndexOf(selected);
            bool on = idx >= 0;
            selGrid.gameObject.SetActive(on);
            selStrip.gameObject.SetActive(on);
            if (!on) return;
            selGrid.anchoredPosition = new Vector2(ColX(idx), 0f);
            selStrip.anchoredPosition = new Vector2(ColX(idx), 0f);
        }

        private void LayoutDetail()
        {
            int y = LanesY + lanesH + 6;
            detail.anchoredPosition = new Vector2(0f, -y);
            int h = Mathf.Max(LineH, pageH - y - HeaderH - 4);
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
            if (rightHeader != null) rightHeader.text = CalendarL10n.T("journal");
            if (root != null && root.activeSelf) Refresh();
        }

        public void Select(int day)
        {
            if (day == selected) return;
            if (!cols.Contains(day)) return;
            selected = day;
            Refresh();
        }

        /// <summary>Para os cenários: escolhe a coluna pelo índice a partir de hoje (negativo = dias passados).</summary>
        internal void SelectOffset(int offset)
        {
            int today = (int)Math.Floor(CalendarModel.Now);
            int idx = cols.IndexOf(today) + offset;
            if (idx < 0 || idx >= cols.Count) return;
            Select(cols[idx]);
            if (ColX(idx) < scrollX || ColX(idx) + ColW > scrollX + GridW) { scrollX = ColX(idx) - ColW; ApplyScroll(); }
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
            SetText(header, CalendarTooltip.ResetLine(today) ?? CalendarL10n.T("tab"));
            UpdateNow(today, now);
            UpdateSelection();
            RefreshDetail(today, now);
        }

        // ---------------- dia escolhido ----------------

        private void RefreshDetail(int today, double now)
        {
            int w = (int)leftScroll.viewport.sizeDelta.x;
            JournalTracker j = JournalTracker.Current;
            List<Occurrence> occ = selected != Baseline ? CalendarModel.OnDay(selected) : new List<Occurrence>();
            string sig = selected + "|" + LLBase.CurrentLang + "|" + (j?.Version ?? -1) + "|" + string.Join(",", occ.Select(o => o.Def.Id + "@" + o.Start.ToString("0.000") + ":" +
                (CalendarModel.State(o, now) == OccState.Past) + ":" + (CalendarTooltip.Extra(o) ?? "")));
            if (sig != detailSig)
            {
                bool sameDay = detailSig != null && detailSig.StartsWith(selected + "|");
                detailSig = sig;
                if (selected == Baseline)
                {
                    SetText(leftHeader, CalendarL10n.T("before_journal"));
                }
                else
                {
                    int off = selected - today;
                    string rel = off >= 0 ? CalendarL10n.Relative(off) : off == -1 ? CalendarL10n.T("yesterday") : CalendarL10n.T("days_ago", -off);
                    SetText(leftHeader, CalendarTooltip.DayIcon(selected) + " " + Hud.HudL10n.Day(CalendarModel.DayIdOf(selected)) + " · " + rel + " · " + CalendarL10n.T("day_n", selected));
                }
                SetText(rightHeader, CalendarL10n.T("journal"));
                RebuildDetail(occ, w, today, now);
                if (!sameDay) leftContent.anchoredPosition = rightContent.anchoredPosition = Vector2.zero;
            }
            foreach (Entry e in entries)
            {
                string t = CalendarTooltip.When(e.Occ, now);
                if (t != e.WhenText) { e.WhenText = t; e.When.text = t; }
            }
        }

        private void RebuildDetail(List<Occurrence> occ, int w, int today, double now)
        {
            HideTooltip();
            entries.Clear();
            tooltipTargets.Clear();
            Clear(leftContent);
            Clear(rightContent);
            JournalTracker j = JournalTracker.Current;
            int ly = 4, ry = 4;
            if (selected == Baseline)
            {
                ly = BaselineText(leftContent, w, ly, j);
                ry = BaselineCounts(rightContent, w, ry, j);
            }
            else
            {
                ly = occ.Count > 0 ? EventsSection(leftContent, occ, w, ly, now) : Empty(leftContent, CalendarL10n.T("nothing"), w, ly);
                if (j == null || !j.Active) ry = Empty(rightContent, CalendarL10n.T("j_off"), w, ry);
                else if (selected > today) ry = Empty(rightContent, CalendarL10n.T("j_future"), w, ry);
                else
                {
                    List<JournalLine> lines = JournalView.Lines(j.OnDay(selected));
                    ry = lines.Count > 0 ? JournalSection(rightContent, lines, w, ry)
                        : Empty(rightContent, CalendarL10n.T(selected == today ? "j_today_empty" : "j_empty"), w, ry);
                }
            }
            leftContent.sizeDelta = new Vector2(w, ly + 4);
            rightContent.sizeDelta = new Vector2(w, ry + 4);
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
                    int dh = Mathf.CeilToInt(d.GetPreferredValues(d.text, textW, 0f).y);
                    if (dh % 2 != 0) dh++;
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

        /// <summary>Diário de um dia: sermão, "Concluído" (quests) e "Desbloqueado" (o resto), uma linha por coisa, com tooltip.</summary>
        private int JournalSection(RectTransform content, List<JournalLine> lines, int w, int y)
        {
            List<JournalLine> sermons = lines.Where(l => l.Kind == JournalKinds.Sermon).ToList();
            List<JournalLine> quests = lines.Where(l => l.Kind == JournalKinds.Quest).ToList();
            List<JournalLine> unlocks = lines.Where(l => l.Kind != JournalKinds.Quest && l.Kind != JournalKinds.Sermon).ToList();
            bool any = false;
            if (sermons.Count > 0) { y = JournalGroup(content, CalendarL10n.T("j_sermon_h"), sermons, w, y); any = true; }
            if (quests.Count > 0)
            {
                if (any) y += 6;
                y = JournalGroup(content, CalendarL10n.T("j_completed_h"), quests, w, y);
                any = true;
            }
            if (unlocks.Count > 0)
            {
                if (any) y += 6;
                y = JournalGroup(content, CalendarL10n.T("j_unlocked_h"), unlocks, w, y);
            }
            return y;
        }

        private int JournalGroup(RectTransform content, string title, List<JournalLine> lines, int w, int y)
        {
            TMP_Text h = Label(content, "Group", "small_font_bold-sub_header_2", Pad, y, w - 2 * Pad, TextAlignmentOptions.Left, false);
            h.text = title;
            y += 18;
            Separator(content, Pad, y, w - 2 * Pad);
            y += 6;
            foreach (JournalLine l in lines)
            {
                JournalLine line = l;
                var go = new GameObject("Line_" + l.Kind + "_" + l.Id, typeof(RectTransform));
                go.transform.SetParent(content, false);
                RectTransform rt = (RectTransform)go.transform;
                int lineH = l.Sermon != null ? LineH + SermonDetailH : LineH;
                TopLeft(rt, Pad, y, w - 2 * Pad, lineH);
                Hover hv = AddHover(go);
                hv.OnEnter = () => ShowTooltip(JournalView.Tooltip(line), rt);
                hv.OnExit = () => HideTooltip(rt);
                tooltipTargets.Add(rt);
                int rightLabelW = 110;
                TMP_Text name = Label(go.transform, "Name", "small_font_bold-effects_header", 0, 3, w - 2 * Pad - rightLabelW - 6, TextAlignmentOptions.Left, false);
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.text = l.Name;
                TMP_Text kind = Label(go.transform, "Kind", "tiny_font-effects_timer", w - 2 * Pad - rightLabelW, 5, rightLabelW, TextAlignmentOptions.Right, false);
                kind.text = JournalView.KindLabel(l.Kind) + " · " + (l.Approx ? "~" : "") + CalendarModel.Clock(l.Tod);
                if (l.Sermon != null)
                {
                    TMP_Text detailText = Label(go.transform, "Detail", "small_font-effects_regular_text", 0, LineH - 2, w - 2 * Pad, TextAlignmentOptions.Left, false);
                    detailText.overflowMode = TextOverflowModes.Ellipsis;
                    detailText.text = JournalView.SermonDetail(l.Sermon);
                }
                y += lineH;
            }
            return y;
        }

        private int BaselineText(RectTransform content, int w, int y, JournalTracker j)
        {
            if (j == null || !j.Active) return Empty(content, CalendarL10n.T("j_off"), w, y);
            TMP_Text d = Label(content, "Text", "small_font-effects_regular_text", Pad, y, w - 2 * Pad, TextAlignmentOptions.TopLeft, true);
            d.text = CalendarL10n.T("j_baseline_d", j.StartDay);
            int dh = Mathf.CeilToInt(d.GetPreferredValues(d.text, w - 2 * Pad, 0f).y);
            if (dh % 2 != 0) dh++;
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
            return $"selected={(selected == Baseline ? "antes" : selected.ToString())} cols={cols.Count} lanes={lanes.Count} scroll={scrollX},{scrollY} marcas={timelineTips.Count} " +
                   $"entries={entries.Count} linhas={tooltipTargets.Count} active={(root != null && root.activeSelf)} header='{header?.text}'";
        }
    }
}
