using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GK2Hud.Calendar;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Hud.Hud
{
    /// <summary>
    /// Medalhão dos dias (<c>UIHUDWheel</c>) girando com a hora, sem mexer em estado do jogo:
    /// - cada dia é uma fatia de 60° do anel, separada das vizinhas por divisórias de bronze;
    /// - o anel gira no sentido da troca de dia nativa (o próximo vem da direita); o seletor de baixo e a seta ficam parados,
    ///   então o seletor passa pela fatia do dia de meia-noite a meia-noite e ao meio-dia tudo fica na posição nativa;
    /// - os sigilos perdem o aro de bronze e ficam em pé no meio da fatia (só transladam: sem rotação de pixel art).
    /// O fundo nativo (<c>hud-day_dial-bg</c>) é escondido e redesenhado girado (vizinho mais próximo, grade de pixel de arte)
    /// numa textura própria, refeita a cada <see cref="StepDeg"/>. As posições dos sigilos são aplicadas em
    /// <c>Canvas.willRenderCanvases</c>, depois do <c>Update</c> nativo, então a animação de troca de dia do jogo só
    /// contribui com os fades de cor. Troca de vários dias (dia pulado, carregar save) anda na velocidade nativa (120°/s).
    /// </summary>
    internal sealed class WheelSpinFeature : IDisposable
    {
        internal static WheelSpinFeature Current;

        private const string RingName = HudFeature.Prefix + "WheelRing";
        private const float AttachRetry = 1f;
        private const float StepDeg = 0.5f;            // 0,5° ≈ 3,75 s reais acordado; 1 px de arte na borda ≈ 1°
        private const double CatchUpDaysPerSec = 2.0;   // = dayWheelAngularSpeedDeg 120°/s do UIHUDWheel
        private const double CatchUpThreshold = 0.02;   // dias; abaixo disso segue o relógio direto (inclui o sono 50×)
        private const float IconKeepRadius = 7.4f;      // px de arte: mantém o disco colorido, tira o aro de bronze

        // Divisória (px de arte, medido do centro do anel): núcleo claro, corpo bronze com lado de sombra e contorno escuro,
        // da borda de bronze do céu (r 30) até a borda externa do anel (r 48). Paleta = aro dos sigilos nativos.
        private const float DivHi = 0.45f, DivMid = 1.0f, DivOut = 1.7f, DivR0 = 30f, DivR1 = 48f;
        private static readonly Color32 BronzeOut = new Color32(0x33, 0x16, 0x06, 0xFF);
        private static readonly Color32 BronzeShadow = new Color32(0x6C, 0x3D, 0x22, 0xFF);
        private static readonly Color32 BronzeMid = new Color32(0x99, 0x5C, 0x39, 0xFF);
        private static readonly Color32 BronzeHi = new Color32(0xCA, 0x8A, 0x50, 0xFF);

        private readonly ConfigEntry<bool> enabled;

        private UIHUDWheel wheel;
        private Image nativeBg;
        private UIHUDWheelElement[] elements;
        private float iconRadius;
        private readonly Dictionary<string, int> dayNumberByName = new Dictionary<string, int>();
        private readonly Dictionary<Sprite, Sprite> stripped = new Dictionary<Sprite, Sprite>();   // nativo → sem aro
        private readonly Dictionary<Sprite, Sprite> original = new Dictionary<Sprite, Sprite>();   // sem aro → nativo

        private GameObject ringGo;
        private Texture2D ringTex;
        private Sprite ringSprite;
        private int size;
        private Color32[] src, buf;

        private bool hooked, hasShown, loggedError;
        private double shownT;
        private float drawnRho = float.NaN, nextAttach;

        /// <summary>Teste (cenário gk2hud-medalhao-giro.cs): força o tempo mostrado (dia + hora) sem mexer no jogo.</summary>
        internal double? ForcedT { get; set; }

        public WheelSpinFeature(ConfigEntry<bool> enabled)
        {
            this.enabled = enabled;
            Current = this;
        }

        public void Dispose()
        {
            Detach(true);
            if (Current == this) Current = null;
        }

        public void Update()
        {
            if (!enabled.Value) { Detach(true); return; }
            if (wheel == null && hooked) Detach(false);   // HUD destruído (menu): só limpa o que é nosso
            if (!hooked) TryAttach();
        }

        // ---------------- montagem ----------------

        private void TryAttach()
        {
            float now = Time.unscaledTime;
            if (now < nextAttach || !CalendarModel.Ready) return;
            nextAttach = now + AttachRetry;
            try
            {
                HUD h;
                try { h = LazyUI.Get<HUD>(); }
                catch (Exception) { return; }
                if (h == null) return;
                GameObject leftUp = Traverse.Create(h).Field("leftUpGroup").GetValue<GameObject>();
                UIHUDWheel w = leftUp != null ? leftUp.GetComponentInChildren<UIHUDWheel>(true) : null;
                if (w == null) return;
                Image bg = w.GetComponent<Image>();
                UIHUDWheelElement[] els = Traverse.Create(w).Field("elements").GetValue<UIHUDWheelElement[]>();
                if (bg == null || bg.sprite == null || els == null || els.Length != 6 || els.Any(e => e == null || e.icon == null))
                {
                    if (!loggedError) Plugin.Log.LogWarning("medalhão giratório: estrutura nativa não encontrada.");
                    loggedError = true;
                    return;
                }

                dayNumberByName.Clear();
                foreach (string d in LazyConsts.ConstDefs.AllDays) dayNumberByName[d] = ConstDef.Get(d).IntValue;

                float[] radii = Traverse.Create(w).Field("slotRadii").GetValue<float[]>();
                iconRadius = Mathf.Round(radii != null && radii.Length > 0 ? radii.Average()
                    : els.Average(e => e.rectTransform.anchoredPosition.magnitude));

                Sprite bgSprite = bg.sprite;
                Rect r = bgSprite.rect;
                size = Mathf.RoundToInt(Mathf.Min(r.width, r.height));
                src = ReadSprite(bgSprite, out _, out _);
                buf = new Color32[src.Length];
                ringTex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = RingName };
                ringSprite = Sprite.Create(ringTex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), bgSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                ringSprite.name = RingName;

                for (int i = w.transform.childCount - 1; i >= 0; i--)
                {
                    Transform c = w.transform.GetChild(i);
                    if (c.name == RingName) Object.Destroy(c.gameObject);
                }
                ringGo = new GameObject(RingName, typeof(RectTransform));
                RectTransform rt = (RectTransform)ringGo.transform;
                rt.SetParent(w.transform, false);
                rt.SetSiblingIndex(0);   // logo acima do fundo nativo, abaixo dos sigilos, da borda do céu e do seletor
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                Image img = ringGo.AddComponent<Image>();
                img.sprite = ringSprite;
                img.raycastTarget = false;
                img.color = bg.color;

                wheel = w;
                nativeBg = bg;
                elements = els;
                nativeBg.enabled = false;
                hasShown = false;
                drawnRho = float.NaN;
                Canvas.willRenderCanvases += OnWillRender;
                hooked = true;
                loggedError = false;
                OnWillRender();
            }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("medalhão giratório: " + ex);
                loggedError = true;
                Detach(true);
            }
        }

        private void Detach(bool restoreNative)
        {
            if (hooked) Canvas.willRenderCanvases -= OnWillRender;
            hooked = false;
            if (restoreNative && wheel != null)
            {
                try
                {
                    if (elements != null)
                        foreach (UIHUDWheelElement e in elements)
                            if (e != null && e.icon != null && e.icon.sprite != null && original.TryGetValue(e.icon.sprite, out Sprite o)) e.icon.sprite = o;
                    if (nativeBg != null) nativeBg.enabled = true;
                    // Volta as posições e cores nativas (o mesmo que o jogo faz ao carregar).
                    if (CalendarModel.Ready && EnvironmentEngine.Instance != null) wheel.OnNewDayStarted(CalendarModel.Env.Day, false);
                }
                catch (Exception ex) { Plugin.Log.LogWarning("medalhão giratório: restaurar: " + ex.Message); }
            }
            if (ringGo != null) Object.Destroy(ringGo);
            ringGo = null;
            if (ringSprite != null) Object.Destroy(ringSprite);
            if (ringTex != null) Object.Destroy(ringTex);
            ringSprite = null;
            ringTex = null;
            foreach (Sprite s in stripped.Values)
            {
                if (s == null) continue;
                if (s.texture != null) Object.Destroy(s.texture);
                Object.Destroy(s);
            }
            stripped.Clear();
            original.Clear();
            wheel = null;
            nativeBg = null;
            elements = null;
            src = buf = null;
        }

        // ---------------- por frame ----------------

        private void OnWillRender()
        {
            try { Apply(); }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("medalhão giratório: " + ex);
                loggedError = true;
                Detach(true);
            }
        }

        private void Apply()
        {
            if (wheel == null || !wheel.isActiveAndEnabled || !CalendarModel.Ready) return;
            EnvironmentData env = CalendarModel.Env;
            double target = ForcedT ?? env.Day + (double)env.TimeOfDay;
            double diff = target - shownT;
            if (!hasShown || Math.Abs(diff) > 6.0) { shownT = target; hasShown = true; }
            else if (Math.Abs(diff) > CatchUpThreshold)
            {
                double step = CatchUpDaysPerSec * Time.unscaledDeltaTime;
                shownT = Math.Abs(diff) <= step ? target : shownT + Math.Sign(diff) * step;
            }
            else shownT = target;

            // Rotação do anel em graus (anti-horário positivo); ao meio-dia de qualquer dia é múltiplo de 60 (posição nativa).
            double rot = (0.5 - shownT) * 60.0;
            rot -= Math.Floor(rot / 360.0) * 360.0;
            float rho = Mathf.Round((float)rot / StepDeg) * StepDeg;
            if (rho >= 360f) rho -= 360f;
            if (rho != drawnRho)
            {
                DrawRing(rho);
                drawnRho = rho;
            }

            int baseDay = env.Day;
            int current = env.CurrentDayNumber;
            foreach (UIHUDWheelElement e in elements)
            {
                Sprite sp = e.icon.sprite;
                if (sp == null) continue;
                if (!original.ContainsKey(sp))
                {
                    Sprite s = Stripped(sp);
                    if (s != null) { e.icon.sprite = s; sp = s; }
                }
                if (!dayNumberByName.TryGetValue(sp.name, out int number)) continue;
                int k = Mod(number - current, 6);
                // Fatia do dia (baseDay + k): centro em -90° + 60°·dia + rotação.
                double ang = (-90.0 + 60.0 * Mod(baseDay + k, 6) + rho) * Math.PI / 180.0;
                var pos = new Vector2(Mathf.Round((float)(Math.Cos(ang) * iconRadius)), Mathf.Round((float)(Math.Sin(ang) * iconRadius)));
                if (e.rectTransform.anchoredPosition != pos) e.rectTransform.anchoredPosition = pos;
            }
        }

        private void DrawRing(float rhoDeg)
        {
            int n = size;
            float c = n * 0.5f;
            float a = rhoDeg * Mathf.Deg2Rad;
            float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < n; y++)
            {
                float dy = y + 0.5f - c;
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c;
                    // Origem no fundo nativo: o ponto girado de −ρ.
                    float sx = dx * ca + dy * sa + c;
                    float sy = -dx * sa + dy * ca + c;
                    int ix = Mathf.FloorToInt(sx), iy = Mathf.FloorToInt(sy);
                    buf[y * n + x] = ix >= 0 && iy >= 0 && ix < n && iy < n ? src[iy * n + ix] : clear;
                }
            }

            // Divisórias nas fronteiras entre dias: −60° + 60°·j + ρ (os sigilos ficam em −90° + 60°·j + ρ).
            for (int y = 0; y < n; y++)
            {
                float dy = y + 0.5f - c;
                for (int x = 0; x < n; x++)
                {
                    float dx = x + 0.5f - c;
                    float rr = Mathf.Sqrt(dx * dx + dy * dy);
                    if (rr < DivR0 || rr > DivR1) continue;
                    int i = y * n + x;
                    if (buf[i].a < 255) continue;
                    float phi = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    float rel = phi - (-60f + rhoDeg);
                    float j = Mathf.Round(rel / 60f);
                    float b = (-60f + rhoDeg + 60f * j) * Mathf.Deg2Rad;
                    float ux = Mathf.Cos(b), uy = Mathf.Sin(b);
                    if (dx * ux + dy * uy <= 0f) continue;
                    float d = -dx * uy + dy * ux;
                    float ad = Mathf.Abs(d);
                    if (ad <= DivHi) buf[i] = BronzeHi;
                    else if (ad <= DivMid) buf[i] = d > 0f ? BronzeMid : BronzeShadow;
                    else if (ad <= DivOut) buf[i] = BronzeOut;
                }
            }
            ringTex.SetPixels32(buf);
            ringTex.Apply(false);
        }

        // ---------------- sprites ----------------

        /// <summary>Cópia do sigilo só com o disco colorido (tira o aro de bronze pelo raio, a partir do centro do desenho).</summary>
        private Sprite Stripped(Sprite sp)
        {
            if (stripped.TryGetValue(sp, out Sprite done)) return done;
            if (!dayNumberByName.ContainsKey(sp.name)) return null;
            Color32[] px = ReadSprite(sp, out int w, out int h);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (px[y * w + x].a > 0) { minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); }
            if (maxX < 0) return null;
            float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;
            var clear = new Color32(0, 0, 0, 0);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy >= IconKeepRadius * IconKeepRadius) px[y * w + x] = clear;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = RingName + "." + sp.name };
            tex.SetPixels32(px);
            tex.Apply(false);
            Rect r = sp.rect;
            Sprite s = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(sp.pivot.x / r.width, sp.pivot.y / r.height), sp.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            s.name = sp.name;   // o WheelFeature e o calendário reconhecem o dia pelo nome da sprite
            stripped[sp] = s;
            original[s] = sp;
            return s;
        }

        /// <summary>Pixels do retângulo da sprite (textura do jogo não é legível: passa por RenderTexture).</summary>
        private static Color32[] ReadSprite(Sprite sp, out int w, out int h)
        {
            Texture2D t = sp.texture;
            Rect r = sp.textureRect;
            w = Mathf.RoundToInt(sp.rect.width);
            h = Mathf.RoundToInt(sp.rect.height);
            RenderTexture rt = RenderTexture.GetTemporary(t.width, t.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(t, rt);
                RenderTexture.active = rt;
                tmp.ReadPixels(new Rect(Mathf.Round(r.x - sp.textureRectOffset.x), Mathf.Round(r.y - sp.textureRectOffset.y), w, h), 0, 0);
                tmp.Apply(false);
                return tmp.GetPixels32();
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                Object.Destroy(tmp);
            }
        }

        private static int Mod(int v, int m) => (v % m + m) % m;

        internal string Describe()
        {
            return $"hooked={hooked} shownT={shownT:0.000} rho={drawnRho:0.0} radius={iconRadius} stripped={stripped.Count} forced={ForcedT?.ToString("0.000") ?? "-"}";
        }
    }
}
