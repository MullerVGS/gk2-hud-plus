using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GK2Shared;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UI.Extensions;
using Object = UnityEngine.Object;

namespace GK2HudPlus.Menu
{
    /// <summary>
    /// Janela de funcionalidades do GK2 HUD+, montada como o jogo monta a janela de Mods (<c>UICodeModsWindow</c>):
    /// clona o layout da janela de Settings (moldura, cabeçalho, X, botão OK) e usa as linhas nativas de seleção
    /// (<c>UISwitchButton</c>, "◀ Off/On ▶") numa lista rolável com seções. Cada linha muda o toggle no .cfg; o
    /// <see cref="ModHost"/> liga ou desliga a funcionalidade no frame seguinte. Opções só aparecem com a
    /// funcionalidade dona ligada.
    /// </summary>
    internal sealed class HudPlusWindow : LazyWindow<LazyWidgetDataBase>
    {
        /// <summary>Largura da lista em pixels de arte: a linha nativa (330) mais espaço para nomes longos.</summary>
        private const float ExtraLabel = 76f;
        private const float RowWidth = 330f;
        private const float ListWidth = RowWidth + ExtraLabel;
        private const float RowHeight = 16f;
        private const float RowSpacing = 6f;
        /// <summary>Dez linhas inteiras (toda linha mede 16 + 6), para a lista nunca abrir cortando uma pela metade.</summary>
        private const float ListHeight = 2f + 10 * (RowHeight + RowSpacing) - RowSpacing + 2f;
        private const string DisabledColor = "#E5736A";
        private const string WarningColor = "#F2C94C";

        private static readonly GameKey[] DecreaseKeys = { GameKey.DecSlider, GameKey.Left };
        private static readonly GameKey[] IncreaseKeys = { GameKey.IncSlider, GameKey.Right };
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private ModHost host;
        private UISwitchButton switchTemplate;
        private UIDialogWindowButton buttonTemplate;
        private TextMeshProUGUI sectionTemplate, bodyTemplate;
        private TextMeshProUGUI windowTitle, liveHint;
        private UIDialogWindowButton okButton;
        private ScrollRect listScroll;
        private RectTransform listContent;
        private AutoScroll listAutoScroll;
        private readonly Dictionary<string, UISwitchButton> rows = new Dictionary<string, UISwitchButton>();
        private bool dirty;

        /// <summary>Linha por id (lida pelos cenários).</summary>
        internal IReadOnlyDictionary<string, UISwitchButton> Rows => rows;

        public static HudPlusWindow Create(ModHost host)
        {
            var go = new GameObject("GK2HudPlus_Window");
            Transform root = UiRoot();
            if (root != null) go.transform.SetParent(root, worldPositionStays: false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 480;
            go.AddComponent<GraphicRaycaster>();
            GamepadNavigationController nav = go.AddComponent<GamepadNavigationController>();
            if (nav.navigationGroupSources == null) nav.navigationGroupSources = new List<GamepadNavigationController.NavigationGroupSource>();
            HudPlusWindow w = go.AddComponent<HudPlusWindow>();
            w.Build(host);
            w.Init();
            return w;
        }

        private static Transform UiRoot()
        {
            GUIElements gui = GUIElements.Instance;
            if (gui == null) return null;
            var fitter = AccessTools.Field(typeof(GUIElements), "uiFitter")?.GetValue(gui) as Component;
            return fitter != null ? fitter.transform : gui.Root;
        }

        private void Build(ModHost host)
        {
            this.host = host;
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Image shadow = gameObject.AddComponent<Image>();
            shadow.color = new Color(0f, 0f, 0f, 0.4f);   // o mesmo escurecimento da janela de Settings (BgShadow #00000066)
            shadow.raycastTarget = true;

            UIGameSettingsWindow settings = LazyUI.GetWindow<UIGameSettingsWindow>();
            if (settings == null) throw new InvalidOperationException("janela de Settings do jogo não encontrada");
            switchTemplate = (UISwitchButton)AccessTools.Field(typeof(UIGameSettingsWindow), "vSyncButton").GetValue(settings);
            buttonTemplate = (UIDialogWindowButton)AccessTools.Field(typeof(UIGameSettingsWindow), "lazyButton").GetValue(settings);
            bodyTemplate = (TextMeshProUGUI)AccessTools.Field(typeof(UISwitchButton), "headerLabel").GetValue(switchTemplate);
            GenericWindowLayout layoutTemplate = settings.GetComponentInChildren<GenericWindowLayout>(includeInactive: true);
            if (switchTemplate == null || buttonTemplate == null || layoutTemplate == null) throw new InvalidOperationException("modelos da janela de Settings mudaram");

            GameObject layout = Instantiate(layoutTemplate.gameObject, transform, worldPositionStays: false);
            layout.name = "GenericWindowLayout";
            var content = (RectTransform)layout.transform.Find("Content");
            Transform header = layout.transform.Find("Frame/HeaderGroup/Header");
            for (int i = content.childCount - 1; i >= 0; i--) DestroyImmediate(content.GetChild(i).gameObject);
            SetupContent(content);
            foreach (LocalizedLabel l in header.GetComponents<LocalizedLabel>()) DestroyImmediate(l);
            windowTitle = header.GetComponent<TextMeshProUGUI>();
            sectionTemplate = windowTitle;
            Transform close = layout.transform.Find("Frame/HeaderGroup/CloseButton");
            if (close != null)
            {
                close.gameObject.SetActive(true);
                closeButton = close.GetComponentInChildren<LazyButton>(includeInactive: true);
            }

            liveHint = Label(content, "Live", "", TextAlignmentOptions.Midline, bodyTemplate);
            Size(liveHint.gameObject, ListWidth, RowHeight);
            listScroll = CreateScroll(content);
            Size(listScroll.gameObject, ListWidth, ListHeight);
            listContent = listScroll.content;
            okButton = Instantiate(buttonTemplate, content, worldPositionStays: false);
            okButton.name = "OkButton";
            foreach (LocalizedLabel l in okButton.GetComponentsInChildren<LocalizedLabel>(includeInactive: true)) l.IgnoreLocalize = true;
            host.Changed += OnHostChanged;
        }

        private static void SetupContent(RectTransform content)
        {
            content.sizeDelta = new Vector2(ListWidth, content.sizeDelta.y);
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter != null)
            {
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            VerticalLayoutGroup v = content.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                v.padding = new RectOffset(0, 0, 2, 0);
                v.spacing = 4f;
                v.childAlignment = TextAnchor.UpperCenter;
                v.childControlWidth = false;
                v.childControlHeight = false;
            }
        }

        public override void Open(LazyWidgetDataBase data)
        {
            base.Open(data);
            Refresh(keepScroll: false);
            if (LazyInput.IsGamepadActive)
            {
                GamepadNavigationController.ReinitItems(focusOnFirstActive: false);
                if (listAutoScroll != null) listAutoScroll.SkipNextAutoscroll = true;
                GamepadNavigationController.FocusOnFirstActive(0);
            }
        }

        public override void Close()
        {
            if (!IsShown) return;
            HideHint();
            base.Close();
        }

        protected override bool OnPressedBack()
        {
            Close();
            return true;
        }

        protected override Dictionary<GameKey, Func<bool>> GetGameKeyDelegates()
        {
            // Como a janela de Settings: esquerda/direita ficam com a linha focada (UISwitchButton).
            Dictionary<GameKey, Func<bool>> d = base.GetGameKeyDelegates();
            d[GameKey.Select] = () => false;
            foreach (GameKey k in new[] { GameKey.DpadLeft, GameKey.DpadRight, GameKey.Left, GameKey.Right })
                if (!d.ContainsKey(k)) d[k] = () => false;
            return d;
        }

        protected override void TestDraw()
        {
        }

        private void OnHostChanged() => dirty = true;

        protected override void Update()
        {
            base.Update();
            if (dirty && IsShown)
            {
                dirty = false;
                Refresh(keepScroll: true);
            }
        }

        /// <summary>Refaz a lista: seções por categoria (na ordem em que aparecem), funcionalidades e as opções das ligadas.</summary>
        internal void Refresh(bool keepScroll)
        {
            Vector2 pos = listContent.anchoredPosition;
            HideHint();
            rows.Clear();
            for (int i = listContent.childCount - 1; i >= 0; i--)
            {
                GameObject child = listContent.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            windowTitle.text = MenuText.Title.Current;
            liveHint.text = MenuText.Live.Current;
            okButton.Draw(new UIDialogWindowData.ButtonData(Close, LLBase.L("btn_ok"), null, replaceForGamepad: true, GameKey.Select));

            List<FeatureInfo> top = host.Features.Where(f => !f.IsOption).ToList();
            foreach (string category in top.Select(f => f.Category).Distinct())
            {
                Section(MenuText.Category(category));
                if (category == "gameplay") Hint(Colorize(MenuText.GameplayHint.Current, WarningColor));
                foreach (FeatureInfo f in top.Where(f => f.Category == category)) RowWithOptions(f);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(listContent);
            ((RectTransform)transform).RefreshContentFitter();
            listScroll.StopMovement();
            if (keepScroll) listContent.anchoredPosition = pos;
            else listScroll.verticalNormalizedPosition = 1f;
            if (LazyInput.IsGamepadActive) GamepadNavigationController.ReinitItems(focusOnFirstActive: false);
        }

        /// <summary>A linha e, se ela estiver ligada, as opções dela (que podem ter opções também).</summary>
        private void RowWithOptions(FeatureInfo f)
        {
            Row(f);
            if (!f.Wanted) return;
            foreach (FeatureInfo o in host.Features.Where(o => o.IsOption && o.Parent == f)) RowWithOptions(o);
        }

        private void Section(string text)
        {
            TextMeshProUGUI t = Label(listContent, "Section", text, TextAlignmentOptions.MidlineLeft, sectionTemplate);
            t.margin = new Vector4(2f, 0f, 2f, 0f);
            Height(t.gameObject, RowHeight);
        }

        private void Hint(string text)
        {
            TextMeshProUGUI t = Label(listContent, "Hint", text, TextAlignmentOptions.MidlineLeft, bodyTemplate);
            t.margin = new Vector4(2f, 0f, 2f, 0f);
            Height(t.gameObject, RowHeight);
        }

        private void Row(FeatureInfo f)
        {
            // A linha nativa tem 330 de largura; ela fica à direita da lista e o rótulo cresce para a esquerda.
            var holder = new GameObject("Row_" + f.Id, typeof(RectTransform));
            holder.transform.SetParent(listContent, worldPositionStays: false);
            Height(holder, RowHeight);
            UISwitchButton sw = Instantiate(switchTemplate, holder.transform, worldPositionStays: false);
            sw.name = "Switch";
            foreach (LazyPlatformDependentElement p in sw.GetComponentsInChildren<LazyPlatformDependentElement>(includeInactive: true)) DestroyImmediate(p);
            foreach (LocalizedLabel l in sw.GetComponentsInChildren<LocalizedLabel>(includeInactive: true)) l.IgnoreLocalize = true;
            var srt = (RectTransform)sw.transform;
            srt.anchorMin = srt.anchorMax = new Vector2(1f, 0.5f);
            srt.pivot = new Vector2(1f, 0.5f);
            srt.anchoredPosition = Vector2.zero;
            srt.sizeDelta = new Vector2(RowWidth, RowHeight);
            var name = (TextMeshProUGUI)AccessTools.Field(typeof(UISwitchButton), "headerLabel").GetValue(sw);
            RectTransform nrt = name.rectTransform;
            nrt.offsetMin = new Vector2(nrt.offsetMin.x - ExtraLabel, nrt.offsetMin.y);
            string label = f.Name.Current;
            if (f.Failed) label = Colorize(label, DisabledColor);
            FeatureInfo feature = f;
            sw.Initialize(i => OnSwitch(feature, i == 1), new[] { LLBase.L("ui_off"), LLBase.L("ui_on") }, f.Toggle.Value ? 1 : 0,
                label, DecreaseKeys, IncreaseKeys);
            sw.gameObject.SetActive(true);
            AddHint(sw.gameObject, f);
            rows[f.Id] = sw;
        }

        private void OnSwitch(FeatureInfo f, bool on)
        {
            if (f.Toggle.Value == on) return;
            f.Toggle.Value = on;   // o ModHost aplica no próximo frame e avisa (Changed) para a lista se refazer
            dirty = true;
        }

        private void AddHint(GameObject go, FeatureInfo f)
        {
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                string text = f.Description.Current;
                if (f.Failed) text += "\n" + TooltipRed(MenuText.Failed.Current);
                UITooltip.ShowSimpleInfo(go.transform, text, default, f.Name.Current);
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => UITooltip.Hide());
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        private void HideHint()
        {
            foreach (UISwitchButton sw in rows.Values)
                if (sw != null && UITooltip.IsTooltipShowingAtTarget((RectTransform)sw.transform)) UITooltip.HideImmediately();
        }

        private ScrollRect CreateScroll(Transform parent)
        {
            var go = new GameObject("List", typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, worldPositionStays: false);
            ScrollRect scroll = go.AddComponent<ScrollRect>();
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(go.transform, worldPositionStays: false);
            viewport.GetComponent<Image>().color = Color.clear;
            Stretch((RectTransform)viewport.transform);
            var c = new GameObject("Content", typeof(RectTransform));
            c.transform.SetParent(viewport.transform, worldPositionStays: false);
            var crt = (RectTransform)c.transform;
            crt.anchorMin = new Vector2(0f, 1f);
            crt.anchorMax = Vector2.one;
            crt.pivot = new Vector2(0.5f, 1f);
            crt.offsetMin = crt.offsetMax = Vector2.zero;
            VerticalLayoutGroup v = c.AddComponent<VerticalLayoutGroup>();
            v.spacing = RowSpacing;   // o espaçamento das linhas da janela de Settings
            v.padding = new RectOffset(0, 0, 2, 2);
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = true;
            v.childControlHeight = true;
            v.childForceExpandWidth = true;
            v.childForceExpandHeight = false;
            c.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = crt;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;
            scroll.scrollSensitivity = RowHeight + RowSpacing;   // um passo do scroll = uma linha
            listAutoScroll = go.AddComponent<AutoScroll>();
            AccessTools.Field(typeof(AutoScroll), "content")?.SetValue(listAutoScroll, crt);
            AccessTools.Field(typeof(AutoScroll), "gamepadController")?.SetValue(listAutoScroll, GetComponent<GamepadNavigationController>());
            go.SetActive(true);
            return scroll;
        }

        private static TextMeshProUGUI Label(Transform parent, string name, string text, TextAlignmentOptions align, TextMeshProUGUI template)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, worldPositionStays: false);
            TextMeshProUGUI t = go.GetComponent<TextMeshProUGUI>();
            t.fontSize = template.enableAutoSizing ? template.fontSizeMax : template.fontSize;
            t.enableAutoSizing = false;
            t.font = template.font;
            t.color = template.color;
            t.isOrthographic = template.isOrthographic;
            t.extraPadding = template.extraPadding;
            t.characterSpacing = template.characterSpacing;
            t.wordSpacing = template.wordSpacing;
            t.text = text;
            t.alignment = align;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            TextStyleComponent style = template.GetComponent<TextStyleComponent>();
            if (style != null && style.CurrentTextStyle != null) go.AddComponent<TextStyleComponent>().SetTextStyle(style.CurrentTextStyle);
            return t;
        }

        /// <summary>Texto no vermelho dos efeitos negativos do tooltip nativo (legível no pergaminho).</summary>
        private static string TooltipRed(string text)
        {
            object tooltip = AccessTools.Field(typeof(UITooltip), "instance")?.GetValue(null);
            var style = tooltip != null ? AccessTools.Field(typeof(UITooltip), "redEffectTextStyle")?.GetValue(tooltip) as TextStyle : null;
            return style != null ? style.ApplyStyleToString(text) : text;
        }

        private static string Colorize(string text, string color) => "<color=" + color + ">" + text + "</color>";

        private static void Size(GameObject go, float w, float h)
        {
            ((RectTransform)go.transform).sizeDelta = new Vector2(w, h);
            LayoutElement e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            e.minWidth = e.preferredWidth = w;
            e.minHeight = e.preferredHeight = h;
        }

        private static void Height(GameObject go, float h)
        {
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            LayoutElement e = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            e.minHeight = e.preferredHeight = h;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>Fecha, tira da pilha de janelas e solta os eventos estáticos que a LazyWindow assina (hot-reload).</summary>
        internal void Teardown()
        {
            if (IsShown) CloseWithoutCallback();
            if (host != null) host.Changed -= OnHostChanged;
            foreach (string ev in new[] { "OnWindowBecameVisibleInStack", "OnWindowBecameHiddenInStack" })
            {
                FieldInfo field = typeof(LazyWindowsStackController).GetField(ev, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (!(field?.GetValue(null) is Delegate d)) continue;
                foreach (Delegate h in d.GetInvocationList().Where(h => ReferenceEquals(h.Target, this))) d = Delegate.Remove(d, h);
                field.SetValue(null, d);
            }
            Destroy(gameObject);
        }
    }
}
