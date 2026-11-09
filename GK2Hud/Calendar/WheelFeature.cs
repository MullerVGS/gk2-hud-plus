using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GK2Hud.Hud;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Medalhão dos dias (<c>UIHUDWheel</c>), só leitura:
    /// - mouse sobre o medalhão: a hora aparece em cima dele (fade, mesmo TextStyle dos números da barra de energia);
    /// - mouse parado sobre o medalhão: mini calendário da semana no <c>UITooltip</c> nativo;
    /// - mouse sobre um sigilo: o que acontece naquele dia, com contagem regressiva ao vivo.
    /// Hover por geometria (círculo do medalhão e rects dos sigilos), sem Graphic com raycast. O tooltip ancora num
    /// RectTransform proxy filho do UIRoot (como o hover no mundo), então o botão de fixar do GK2Hud funciona igual.
    /// </summary>
    internal sealed class WheelFeature : IDisposable
    {
        internal static WheelFeature Current;

        internal const string ProxyName = PinStack.Prefix + "WheelProxy";
        private const string ClockName = HudFeature.Prefix + "WheelClock";
        private const string ClockStyle = "tiny_font-inspiration-plate_bar_progress";
        private const float AttachRetry = 1f;
        private const float RefreshInterval = 0.25f;
        private const float HoverLinger = 0.3f;
        private const float MoveThreshold = 4f;
        private const int ClockOffsetY = -16;   // px de arte abaixo do centro do céu (fora do caminho do sol/lua ao meio-dia/meia-noite)

        private readonly ConfigEntry<HudNumbersMode> clockMode;
        private readonly ConfigEntry<int> clockStep;
        private readonly ConfigEntry<float> delay;
        private readonly ConfigEntry<float> fadeDuration;
        private readonly ConfigEntry<bool> popupOn;

        private HUD hud;
        private RectTransform wheel;
        private RectTransform sky;
        private readonly List<KeyValuePair<RectTransform, Func<string>>> sigils = new List<KeyValuePair<RectTransform, Func<string>>>();
        private GameObject clock;
        private TextMeshProUGUI clockLabel;
        private HudFader clockFader;
        private string clockText, lang;
        private float nextAttach, nextClock, lastOver = float.NegativeInfinity;
        private bool loggedError;

        private RectTransform proxy;
        private string target;          // "week" ou o day_* do sigilo; null = fora
        private float stillSince, nextRefresh;
        private Vector2 stillAnchor;
        private bool showing, dismissed;
        private string shownSig;

        /// <summary>Modo de teste (cenários gk2hud-calendario-*.cs): no green o mouse não mexe e o cursor fica oculto.</summary>
        internal Vector2? VirtualMouse { get; set; }

        public WheelFeature(ConfigEntry<HudNumbersMode> clockMode, ConfigEntry<int> clockStep, ConfigEntry<float> delay, ConfigEntry<float> fadeDuration,
            ConfigEntry<bool> popupOn)
        {
            this.clockMode = clockMode;
            this.clockStep = clockStep;
            this.delay = delay;
            this.fadeDuration = fadeDuration;
            this.popupOn = popupOn;
            Current = this;
        }

        public void Dispose()
        {
            HideOwn();
            if (proxy != null) Object.Destroy(proxy.gameObject);
            proxy = null;
            if (clock != null) Object.Destroy(clock);
            clock = null;
            VirtualMouse = null;
            if (Current == this) Current = null;
        }

        public void Update()
        {
            try { Tick(); }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("medalhão: " + ex);
                loggedError = true;
                HideOwn();
                target = null;
            }
        }

        private void Tick()
        {
            float now = Time.unscaledTime;
            if (!EnsureAttached()) { HideOwn(); return; }
            Vector2 mouse = VirtualMouse ?? (Vector2)Input.mousePosition;
            if ((mouse - stillAnchor).sqrMagnitude > MoveThreshold * MoveThreshold)
            {
                stillAnchor = mouse;
                stillSince = now;
            }

            bool usable = Usable();
            string over = usable ? Pick(mouse) : null;
            if (over != null) lastOver = now;

            // Hora em cima do medalhão.
            if (now >= nextClock)
            {
                nextClock = now + RefreshInterval;
                if (LLBase.CurrentLang != lang) { lang = LLBase.CurrentLang; clockLabel.GetComponent<TextStyleComponent>()?.ApplyStyle(); clockText = null; }
                UpdateClock();
            }
            bool clockWanted = clockMode.Value == HudNumbersMode.Always ? LazyWindowsStackController.ActiveWindow == null
                : clockMode.Value == HudNumbersMode.Hover && usable && now - lastOver < HoverLinger;
            clockFader.Set(clockWanted);
            clockFader.Step(Time.unscaledDeltaTime, Mathf.Clamp(fadeDuration.Value, 0f, 2f));

            // Popup.
            if (!popupOn.Value || over == null || Native.Instance == null)
            {
                Lose();
                return;
            }
            if (over != target)
            {
                bool wasShowing = showing;
                TooltipFeature.Current?.ReleaseSuppress(proxy);
                target = over;
                shownSig = null;
                if (dismissed && !wasShowing) stillSince = now;   // depois de fixar, só volta com nova parada
                dismissed = false;
                if (wasShowing) { Show(true); return; }           // já aberto: troca na hora (medalhão ↔ sigilo)
            }
            if (showing && !UITooltip.IsTooltipShowingAtTarget(proxy))
            {
                // Outro tooltip tomou o singleton ou a bolha foi fixada: não volta por cima até sair do medalhão.
                showing = false;
                dismissed = true;
            }
            if (dismissed) return;
            if (!showing)
            {
                if (now - stillSince < delay.Value || now < nextRefresh) return;
                nextRefresh = now + RefreshInterval;
                Show(true);
            }
            else if (now >= nextRefresh)
            {
                nextRefresh = now + RefreshInterval;
                Show(false);
            }
        }

        private bool Usable()
        {
            return LazyWindowsStackController.ActiveWindow == null && !LazyWindowsStackController.HasAnyModalWindowOpened
                && !LazyInput.IsGamepadActive && (VirtualMouse.HasValue || Cursor.visible)
                && wheel != null && wheel.gameObject.activeInHierarchy && CalendarModel.Ready;
        }

        /// <summary>Sigilo sob o mouse (day_*), "week" no resto do medalhão, null fora.</summary>
        private string Pick(Vector2 mouse)
        {
            foreach (KeyValuePair<RectTransform, Func<string>> s in sigils)
            {
                if (s.Key == null || !s.Key.gameObject.activeInHierarchy) continue;
                if (!RectTransformUtility.RectangleContainsScreenPoint(s.Key, mouse, null)) continue;
                string id = s.Value();
                if (id != null) return id;
            }
            Rect r = ScreenRect(wheel);
            float radius = r.width * 0.42f;   // anel externo do medalhão (o rect inclui a sombra)
            return (mouse - r.center).sqrMagnitude <= radius * radius ? "week" : null;
        }

        private static Rect ScreenRect(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);   // canvas overlay: mundo = px de tela
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        // ---------------- montagem ----------------

        private bool EnsureAttached()
        {
            if (hud != null && wheel != null && clock != null) return true;
            if (clock != null) Object.Destroy(clock);
            clock = null;
            hud = null;
            float now = Time.unscaledTime;
            if (now < nextAttach || !CalendarModel.Ready) return false;
            nextAttach = now + AttachRetry;
            HUD h;
            try { h = LazyUI.Get<HUD>(); }
            catch (Exception) { return false; }
            if (h == null) return false;
            GameObject leftUp = Traverse.Create(h).Field("leftUpGroup").GetValue<GameObject>();
            UIHUDWheel w = leftUp != null ? leftUp.GetComponentInChildren<UIHUDWheel>(true) : null;
            TextStyle style = Resources.FindObjectsOfTypeAll<TextStyle>().FirstOrDefault(s => s.name == ClockStyle);
            if (w == null || style == null)
            {
                if (!loggedError) Plugin.Log.LogWarning("medalhão: estrutura nativa não encontrada.");
                loggedError = true;
                return false;
            }
            for (int i = leftUp.transform.childCount - 1; i >= 0; i--)
            {
                Transform c = leftUp.transform.GetChild(i);
                if (c.name == ClockName) Object.Destroy(c.gameObject);
            }
            wheel = (RectTransform)w.transform;
            sky = leftUp.transform.Find("Picture/Sky/Sky") as RectTransform;
            sigils.Clear();
            foreach (UIHUDWheelElement el in w.GetComponentsInChildren<UIHUDWheelElement>(true))
            {
                UIHUDWheelElement e = el;
                Func<string> day = () =>
                {
                    Sprite sp = e.icon != null ? e.icon.sprite : null;
                    string n = sp != null ? sp.name.Replace("(Clone)", "") : null;
                    return n != null && LazyConsts.ConstDefs.AllDays.Contains(n) ? n : null;
                };
                sigils.Add(new KeyValuePair<RectTransform, Func<string>>(e.rectTransform != null ? e.rectTransform : (RectTransform)e.transform, day));
            }
            BuildClock(leftUp.transform, style);
            hud = h;
            loggedError = false;
            return true;
        }

        private void BuildClock(Transform parent, TextStyle style)
        {
            var go = new GameObject(ClockName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.SetSiblingIndex(wheel.GetSiblingIndex() + 1);   // acima do medalhão, abaixo do botão de tutorial
            RectTransform rt = (RectTransform)go.transform;
            RectTransform anchorRt = sky != null ? sky : wheel;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(60f, 16f);
            // Mesmo centro do céu, em coordenadas do pai, arredondado para o pixel de arte.
            Vector3 local = parent.InverseTransformPoint(anchorRt.TransformPoint(anchorRt.rect.center));
            RectTransform prt = (RectTransform)parent;
            Vector2 fromCenter = (Vector2)local - prt.rect.center;
            rt.anchoredPosition = new Vector2(Mathf.Round(fromCenter.x), Mathf.Round(fromCenter.y) + ClockOffsetY);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            go.AddComponent<TextStyleComponent>().SetTextStyle(style);
            clock = go;
            clockLabel = tmp;
            clockFader = new HudFader(go);
            clockText = null;
            lang = LLBase.CurrentLang;
        }

        private void UpdateClock()
        {
            string t = HudFeature.Clock(CalendarModel.Env.TimeOfDay, clockStep.Value);
            if (t == clockText) return;
            clockText = t;
            clockLabel.text = t;
        }

        // ---------------- tooltip ----------------

        private bool EnsureProxy()
        {
            if (proxy != null) return true;
            UITooltip native = Native.Instance;
            if (native == null || native.transform.parent == null) return false;
            Transform parent = native.transform.parent;
            for (int i = parent.childCount - 1; i >= 0; i--)
                if (parent.GetChild(i).name == ProxyName) Object.Destroy(parent.GetChild(i).gameObject);
            var go = new GameObject(ProxyName, typeof(RectTransform));
            proxy = (RectTransform)go.transform;
            proxy.SetParent(parent, false);
            proxy.anchorMin = proxy.anchorMax = proxy.pivot = new Vector2(0.5f, 0.5f);
            return true;
        }

        private RectTransform TargetRect(string t)
        {
            if (t == "week") return wheel;
            foreach (KeyValuePair<RectTransform, Func<string>> s in sigils)
                if (s.Key != null && s.Value() == t) return s.Key;
            return wheel;
        }

        private void Show(bool first)
        {
            if (!EnsureProxy()) return;
            List<LazyWidgetDataBase> list = Build(target);
            if (list == null || list.Count == 0) { HideOwn(); return; }
            string sig = World.WorldContent.Signature(list);
            Rect r = ScreenRect(TargetRect(target));
            Vector3 scale = proxy.lossyScale;
            proxy.sizeDelta = new Vector2(r.width / Mathf.Max(0.0001f, scale.x), r.height / Mathf.Max(0.0001f, scale.y));
            proxy.position = new Vector3(r.center.x, r.center.y, proxy.position.z);
            if (!first && sig == shownSig) return;
            UITooltip native = Native.Instance;
            Native.ShowAt.Invoke(native, new object[] { list, proxy, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.BottomRight });
            if (!first) { CanvasGroup g = Native.GroupOf(native); if (g != null) g.alpha = 1f; }
            showing = UITooltip.IsTooltipShowingAtTarget(proxy);
            shownSig = sig;
        }

        private static List<LazyWidgetDataBase> Build(string t)
        {
            if (t == null) return null;
            return t == "week" ? CalendarTooltip.Week() : CalendarTooltip.Day(t);
        }

        private void Lose()
        {
            HideOwn();
            target = null;
            dismissed = false;
        }

        private void HideOwn()
        {
            if (showing && proxy != null && UITooltip.IsTooltipShowingAtTarget(proxy)) UITooltip.Hide();
            showing = false;
            shownSig = null;
        }

        /// <summary>Conteúdo vivo para a bolha fixada a partir do medalhão (contagem regressiva segue andando).</summary>
        internal Func<List<LazyWidgetDataBase>> LiveFor(RectTransform tooltipTarget)
        {
            if (tooltipTarget == null || tooltipTarget != proxy || target == null) return null;
            string t = target;
            return () => Build(t);
        }

        internal string Describe()
        {
            return $"target={target ?? "-"} showing={showing} dismissed={dismissed} clock={clockText} alpha={(clockFader != null ? clockFader.Alpha : 0f):0.00} " +
                   $"sigils={sigils.Count} mouse={VirtualMouse?.ToString("0") ?? "real"}";
        }
    }
}
