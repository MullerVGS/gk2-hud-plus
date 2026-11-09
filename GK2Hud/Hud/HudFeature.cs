using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Hud.Hud
{
    /// <summary>
    /// Informações extras no HUD nativo, todas só leitura:
    /// - placa com dia da semana e hora sob o "0/10" da felicidade (clone do painel de tech points, mesma moldura
    ///   <c>hud-gameres_frame</c> e TextStyle <c>tiny_font-perk_techpoint_cost</c>; posição = âncora que o próprio HUD usa
    ///   para esse painel no modo de janela Small);
    /// - números de energia e insanidade dentro da barra dupla (energia enche da esquerda, insanidade da direita), com o
    ///   TextStyle nativo de texto sobre barra <c>tiny_font-inspiration-plate_bar_progress</c>.
    /// Atualiza no máximo 4×/s e só mexe no texto quando o valor muda. A placa some enquanto há janela aberta.
    /// </summary>
    internal sealed class HudFeature : IDisposable
    {
        internal const string Prefix = "GK2Hud.";
        private const float Interval = 0.25f;
        private const float AttachRetry = 1f;
        private const string BarTextStyle = "tiny_font-inspiration-plate_bar_progress";
        private const int BarTextInset = 5;
        private const int PlatePadLeft = 7, PlatePadRight = 7, PlateHeight = 20, LabelHeight = 16, LabelCenterY = -11;

        private readonly ConfigEntry<bool> clockOn;
        private readonly ConfigEntry<bool> weekDayOn;
        private readonly ConfigEntry<bool> barsOn;
        private readonly ConfigEntry<bool> barsShowMax;
        private readonly ConfigEntry<int> clockStep;

        private float nextTick;
        private float nextAttach;
        private bool loggedError;

        private HUD hud;
        private RectTransform techPanel;
        private GameObject clock;
        private RectTransform clockRect;
        private TextMeshProUGUI clockLabel;
        private TextMeshProUGUI energyLabel;
        private TextMeshProUGUI insanityLabel;
        private readonly Dictionary<int, string> dayIdByNumber = new Dictionary<int, string>();

        private string lang;
        private string clockText;
        private string energyText;
        private string insanityText;

        public HudFeature(ConfigEntry<bool> clockOn, ConfigEntry<bool> weekDayOn, ConfigEntry<bool> barsOn, ConfigEntry<bool> barsShowMax, ConfigEntry<int> clockStep)
        {
            this.clockOn = clockOn;
            this.weekDayOn = weekDayOn;
            this.barsOn = barsOn;
            this.barsShowMax = barsShowMax;
            this.clockStep = clockStep;
            LazyWindowsStackController.OnWindowOpened += OnWindowChanged;
            LazyWindowsStackController.OnWindowClosed += OnWindowChanged;
        }

        public void Update()
        {
            float now = Time.unscaledTime;
            if (now < nextTick) return;
            nextTick = now + Interval;
            try
            {
                if (!EnsureAttached()) return;
                if (LLBase.CurrentLang != lang)
                {
                    lang = LLBase.CurrentLang;
                    Restyle(clockLabel);
                    Restyle(energyLabel);
                    Restyle(insanityLabel);
                    clockText = energyText = insanityText = null;
                }
                UpdateClock();
                UpdateBars();
            }
            catch (Exception e)
            {
                if (!loggedError) Plugin.Log.LogError("HUD: " + e);
                loggedError = true;
            }
        }

        public void Dispose()
        {
            LazyWindowsStackController.OnWindowOpened -= OnWindowChanged;
            LazyWindowsStackController.OnWindowClosed -= OnWindowChanged;
            DestroyAll();
        }

        /// <summary>Janelas abrem por cima do HUD e cobririam a placa só em parte: some no mesmo frame em que a janela abre.</summary>
        private void OnWindowChanged(LazyWidgetBase _)
        {
            if (clock != null) RefreshClockVisibility();
        }

        // ---------------- montagem ----------------

        private bool EnsureAttached()
        {
            if (hud != null && clock != null && energyLabel != null && insanityLabel != null) return true;
            if (clock != null || energyLabel != null || insanityLabel != null) DestroyAll();
            hud = null;
            float now = Time.unscaledTime;
            if (now < nextAttach) return false;
            nextAttach = now + AttachRetry;
            MainGame game = MainGame.Instance;
            if (game == null || game.GameSave == null || EnvironmentEngine.Instance == null) return false;
            HUD h;
            try { h = LazyUI.Get<HUD>(); }
            catch (Exception) { return false; }
            if (h == null) return false;
            Build(h);
            return hud != null;
        }

        private void Build(HUD h)
        {
            Traverse t = Traverse.Create(h);
            GameObject leftUp = t.Field("leftUpGroup").GetValue<GameObject>();
            RectTransform panel = t.Field("techPointsPanel").GetValue<RectTransform>();
            RectTransform smallRight = t.Field("techPointsSmallRightAnchor").GetValue<RectTransform>();
            UIEnergySanityBar bar = t.Field("energySanityBar").GetValue<UIEnergySanityBar>();
            RectTransform energySlider = bar != null ? bar.transform.Find("CurrentEnergy") as RectTransform : null;
            Transform insanitySlider = bar != null ? bar.transform.Find("Insanity") : null;
            TextStyle barStyle = Resources.FindObjectsOfTypeAll<TextStyle>().FirstOrDefault(s => s.name == BarTextStyle);
            if (leftUp == null || panel == null || smallRight == null || bar == null || energySlider == null || insanitySlider == null || barStyle == null)
            {
                if (!loggedError) Plugin.Log.LogWarning("HUD: estrutura nativa não encontrada; feature desligada até o próximo HUD.");
                loggedError = true;
                return;
            }
            DestroyOrphans(leftUp.transform);
            DestroyOrphans(bar.transform);

            dayIdByNumber.Clear();
            foreach (string id in LazyConsts.ConstDefs.AllDays)
            {
                dayIdByNumber[ConstDef.Get(id).IntValue] = id;
            }

            BuildClock(leftUp.transform, panel, smallRight, bar.transform.parent);
            if (clock == null) return;

            int left = Mathf.RoundToInt(energySlider.anchoredPosition.x);
            int right = left + Mathf.RoundToInt(energySlider.sizeDelta.x);
            int sibling = insanitySlider.GetSiblingIndex() + 1;
            energyLabel = BarLabel(bar.transform, "Energy", barStyle, left + BarTextInset, 0f, TextAlignmentOptions.Left, sibling);
            insanityLabel = BarLabel(bar.transform, "Insanity", barStyle, right - BarTextInset, 1f, TextAlignmentOptions.Right, sibling + 1);

            hud = h;
            techPanel = panel;
            lang = LLBase.CurrentLang;
            clockText = energyText = insanityText = null;
            loggedError = false;
        }

        private void BuildClock(Transform parent, RectTransform panel, RectTransform anchor, Transform barsBack)
        {
            GameObject go = Object.Instantiate(panel.gameObject, parent, false);
            go.name = Prefix + "Clock";
            go.SetActive(false);
            Object.DestroyImmediate(go.GetComponent<ContentSizeFitter>());
            Object.DestroyImmediate(go.GetComponent<HorizontalLayoutGroup>());
            TextMeshProUGUI label = null;
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = go.transform.GetChild(i);
                TextMeshProUGUI tmp = c.GetComponent<TextMeshProUGUI>();
                if (label == null && tmp != null && i == 0) label = tmp;
                else Object.DestroyImmediate(c.gameObject);
            }
            if (label == null)
            {
                Object.Destroy(go);
                return;
            }
            label.name = "Label";
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.Left;
            label.raycastTarget = false;
            RectTransform lr = label.rectTransform;
            lr.anchorMin = lr.anchorMax = new Vector2(0f, 1f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(PlatePadLeft, LabelCenterY);
            lr.sizeDelta = new Vector2(0f, LabelHeight);

            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = anchor.anchorMin;
            rt.anchorMax = anchor.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchor.anchoredPosition;
            rt.sizeDelta = new Vector2(PlatePadLeft + PlatePadRight, PlateHeight);
            if (barsBack != null && barsBack.parent == parent) go.transform.SetSiblingIndex(barsBack.GetSiblingIndex() + 1);

            clock = go;
            clockRect = rt;
            clockLabel = label;
        }

        private static TextMeshProUGUI BarLabel(Transform parent, string name, TextStyle style, int x, float pivotX, TextAlignmentOptions align, int sibling)
        {
            GameObject go = new GameObject(Prefix + name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(Mathf.Min(sibling, parent.childCount - 1));
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(pivotX, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(40f, LabelHeight);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = align;
            tmp.raycastTarget = false;
            go.AddComponent<TextStyleComponent>().SetTextStyle(style);
            return tmp;
        }

        private static void Restyle(TMP_Text label)
        {
            if (label != null) label.GetComponent<TextStyleComponent>()?.ApplyStyle();
        }

        private static void DestroyOrphans(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform c = parent.GetChild(i);
                if (c.name.StartsWith(Prefix))
                {
                    c.gameObject.SetActive(false);
                    Object.Destroy(c.gameObject);
                }
            }
        }

        private void DestroyAll()
        {
            if (clock != null) Object.Destroy(clock);
            if (energyLabel != null) Object.Destroy(energyLabel.gameObject);
            if (insanityLabel != null) Object.Destroy(insanityLabel.gameObject);
            clock = null;
            clockRect = null;
            clockLabel = null;
            energyLabel = null;
            insanityLabel = null;
            techPanel = null;
            hud = null;
        }

        // ---------------- valores ----------------

        private bool RefreshClockVisibility()
        {
            // No modo de janela Small o painel de tech points desce para este mesmo lugar (HUD.GetTechPointsRightAnchor).
            bool techPanelHere = GUIElements.Instance != null && GUIElements.Instance.UIWindowSizeType == UIWindowSizeType.Small
                && techPanel != null && techPanel.gameObject.activeSelf;
            bool visible = (clockOn.Value || weekDayOn.Value) && !techPanelHere && LazyWindowsStackController.ActiveWindow == null;
            if (clock.activeSelf != visible) clock.SetActive(visible);
            return visible;
        }

        private void UpdateClock()
        {
            if (!RefreshClockVisibility()) return;
            bool showClock = clockOn.Value, showDay = weekDayOn.Value;
            EnvironmentData env = MainGame.Instance.GameSave.environmentData;
            string day = showDay && dayIdByNumber.TryGetValue(env.CurrentDayNumber, out string id) ? HudL10n.Day(id) : null;
            string time = showClock ? Clock(env.TimeOfDay, clockStep.Value) : null;
            string text = day != null && time != null ? day + " · " + time : day ?? time;
            if (text == clockText) return;
            clockText = text;
            clockLabel.text = text;
            int w = Mathf.CeilToInt(clockLabel.GetPreferredValues(text).x);
            clockLabel.rectTransform.sizeDelta = new Vector2(w, LabelHeight);
            int plate = PlatePadLeft + w + PlatePadRight;
            if (plate % 2 != 0) plate++;
            clockRect.sizeDelta = new Vector2(plate, PlateHeight);
        }

        /// <summary>timeOfDay 0..1 → relógio de 24 h (0 = meia-noite, 0,5 = meio-dia), arredondado para baixo no passo.</summary>
        internal static string Clock(float timeOfDay, int step)
        {
            step = Mathf.Clamp(step, 1, 60);
            int minutes = Mathf.FloorToInt(Mathf.Repeat(timeOfDay, 1f) * 1440f) % 1440;
            minutes -= minutes % step;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        private void UpdateBars()
        {
            bool on = barsOn.Value;
            if (energyLabel.gameObject.activeSelf != on) energyLabel.gameObject.SetActive(on);
            if (insanityLabel.gameObject.activeSelf != on) insanityLabel.gameObject.SetActive(on);
            if (!on) return;

            PlayerData pd = MainGame.PlayerData;
            if (pd == null) return;
            bool max = barsShowMax.Value;
            // Mesma leitura da barra nativa (UIEnergySanityBarData.UpdateFillData). Energia arredonda para baixo e
            // insanidade para cima: o número nunca promete mais folga do que existe.
            string e = Value(Mathf.FloorToInt(pd.GetRes("energy") + 0.001f), max ? pd.GetResSystem("energy") : null);
            string i = Value(Mathf.CeilToInt(pd.GetRes("insanity") - 0.001f), max ? pd.GetResSystem("insanity") : null);
            if (e != energyText) { energyText = e; energyLabel.text = e; }
            if (i != insanityText) { insanityText = i; insanityLabel.text = i; }
        }

        private static string Value(int v, GameResSystemBase system)
        {
            if (v < 0) v = 0;
            return system is GK2GameResSystem s ? $"{v}/{Mathf.RoundToInt(s.Max)}" : v.ToString();
        }
    }
}
