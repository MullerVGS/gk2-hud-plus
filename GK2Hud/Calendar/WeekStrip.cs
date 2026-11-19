using System.Collections.Generic;
using System.Linq;
using System.Text;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// A semana em cartões dentro do <c>UITooltip</c> nativo: um por dia, com a cena que o jogo desenha no centro do
    /// medalhão (<c>UIHUDWheel</c>: céu, horizonte e a arte do dia, na versão dia ou noite), o sigilo e os ícones dos
    /// eventos conhecidos. Sem spoiler: a cena própria do dia só entra se o jogo já a liberou
    /// (<c>unlockedCustomHudDaySprites</c>, a mesma lista que o medalhão consulta); senão, a cidade padrão.
    /// Unidades locais = pixels de arte (o canvas tem escala 2).
    /// </summary>
    internal static class WeekStrip
    {
        internal const string Name = PinStack.Prefix + "WeekStrip";
        private const float SceneW = 112f, SceneH = 80f, Gap = 6f, IconCell = 24f, TimeH = 10f;
        private const int Columns = 3;
        // Na semana a cena aparece recortada (um pouco dos lados, mais em cima e embaixo) e os cartões ficam colados.
        private const float CropSide = 8f, CropTop = 14f, CropBottom = 10f;
        internal const float WeekW = SceneW - 2f * CropSide, WeekH = SceneH - CropTop - CropBottom;

        private static UIHUDWheel wheel;
        private static Gradient gradSky, gradHorizon;   // do UIHUDWheel, guardados com ele
        private static readonly Dictionary<int, string> builtSig = new Dictionary<int, string>();   // por tooltip (nativo ou bolha fixada)
        private const string LinkPrefix = "cal:";
        private const string TimePrefix = "t:";
        private const string DayPrefix = "cday:";
        private const float CarouselBack = WeekW * 0.25f;   // o carrossel mostra 6 horas para trás; o marcador de agora fica aqui
        private const string ViewportName = "Viewport", ContentName = "Content";
        private const int CarouselVisible = 4;   // dias inteiros visíveis no carrossel

        /// <summary>Semana em carrossel (uma linha que desliza com a hora) em vez de empilhada (3×2).</summary>
        internal static bool Carousel;
        private static float? nativeMaxWidth;   // largura máxima do tooltip nativo, devolvida no Remove

        public static void Remove(Transform tip)
        {
            if (tip == null) return;
            var cap = tip.GetComponent<OverrideLayoutElement>();
            if (cap != null && nativeMaxWidth.HasValue) cap.maxWidth = nativeMaxWidth.Value;
            nativeMaxWidth = null;
            builtSig.Remove(tip.GetInstanceID());
            Transform old = tip.Find(Name);
            if (old != null) Object.DestroyImmediate(old.gameObject);
        }

        /// <summary>Clone do tooltip nativo feito enquanto ele estava alargado: devolve a largura máxima original ao clone.</summary>
        public static void RestoreCap(Transform clone)
        {
            var cap = clone != null ? clone.GetComponent<OverrideLayoutElement>() : null;
            if (cap != null && nativeMaxWidth.HasValue) cap.maxWidth = nativeMaxWidth.Value;
        }

        /// <summary>Garante a tira (ou a cena de um dia só) como filha do tooltip, logo depois do cabeçalho.</summary>
        public static void Place(Transform tip, string target, Styles s, bool clone = false)
        {
            if (tip == null || s == null || !CalendarModel.Ready || !EnsureWheel()) { Remove(tip); return; }
            double now = CalendarModel.Now;
            int today = (int)System.Math.Floor(now);
            var days = new List<int>();
            bool carousel = target == "week" && Carousel;
            // Carrossel: de ontem (as 6 horas para trás podem cair nele) até o último dia que a janela alcança.
            if (carousel) for (int d = today - 1; d <= today + CarouselVisible; d++) days.Add(d);
            else if (target == "week") for (int d = today; d < today + CalendarModel.DaysInWeek; d++) days.Add(d);
            else days.Add(CalendarModel.NextDayOf(target, today));
            string sig = Signature(target, days, now) + (clone ? "|fixada" : "");
            Transform old = tip.Find(Name);
            builtSig.TryGetValue(tip.GetInstanceID(), out string had);
            if (old == null || sig != had)
            {
                if (old != null) Object.DestroyImmediate(old.gameObject);
                old = Build(tip, days, now, s, target == "week", carousel, clone).transform;
                if (builtSig.Count > 16) builtSig.Clear();   // bolhas fechadas não avisam: não deixa crescer
                builtSig[tip.GetInstanceID()] = sig;
            }
            old.SetSiblingIndex(IndexAfterHeader(tip, old));
            UpdateLive(old, now);
            // O tooltip nativo tem largura máxima; a semana inteira não cabe nela.
            var cap = tip.GetComponent<OverrideLayoutElement>();
            var vlg = tip.GetComponent<VerticalLayoutGroup>();
            if (cap != null && cap.useMaxWidth && vlg != null)
            {
                int cols = carousel ? CarouselVisible : Mathf.Min(Columns, days.Count);
                float need = (days.Count > 1 ? cols * WeekW : SceneW) + vlg.padding.horizontal;
                if (clone) cap.maxWidth = Mathf.Max(cap.maxWidth, need);   // a bolha fixada é um clone: fica com a largura
                else
                {
                    if (!nativeMaxWidth.HasValue) nativeMaxWidth = cap.maxWidth;
                    cap.maxWidth = Mathf.Max(nativeMaxWidth.Value, need);
                }
            }
        }

        // Depois do terceiro widget visível (título, "Day N", separador).
        private static int IndexAfterHeader(Transform tip, Transform self)
        {
            int seen = 0;
            for (int i = 0; i < tip.childCount; i++)
            {
                Transform c = tip.GetChild(i);
                if (c == self || !c.gameObject.activeSelf || c.GetComponent<UIBubbleCorner>() != null) continue;
                if (++seen == 3) return i + (self.GetSiblingIndex() < i ? 0 : 1);
            }
            return tip.childCount - 1;
        }

        // Falta menos de um dia: contagem regressiva; mais longe (ou já passou): a hora do relógio.
        internal static string TimeText(Occurrence o, double now)
        {
            double left = o.StartAbs - now;
            return left > 0 && left < 1 ? CalendarModel.Countdown(left) : CalendarModel.Clock(o.Start);
        }

        /// <summary>
        /// O que muda sem refazer a tira: as contagens, o céu do cartão de hoje (anda com o relógio) e o deslocamento do
        /// carrossel. As referências ficam guardadas num componente da tira (<see cref="StripLive"/>), montado uma vez.
        /// </summary>
        private static void UpdateLive(Transform strip, double now)
        {
            StripLive live = StripLive.Of(strip);
            int today = (int)System.Math.Floor(now);
            for (int i = 0; i < live.Times.Count; i++)
            {
                StripLive.Clock t = live.Times[i];
                if (t.Text == null) continue;
                if (Time.unscaledTime >= t.Recheck)
                {
                    // O jogo pode remarcar um evento (execDay/execTime): a ocorrência é relida de tempos em tempos.
                    t.Occ = CalendarModel.OnDay(t.Day).Find(o => o.Def.Id == t.Id);
                    t.Has = t.Occ.Def != null;
                    t.Recheck = Time.unscaledTime + 2f;
                }
                if (!t.Has) continue;
                string v = TimeText(t.Occ, now);
                if (t.Text.text != v) t.Text.text = v;
            }
            if (live.TodayDay == today && live.TodayScene != null) Retint(live.TodayScene, Frac(now));
            if (live.Content != null)
                live.Content.anchoredPosition = new Vector2(CarouselBack - Mathf.Floor((1f + Frac(now)) * WeekW), 0f);   // o conteúdo começa em ontem
        }

        /// <summary>Referências do que a tira atualiza ao vivo; um clone (bolha fixada) remonta pela hierarquia na primeira vez.</summary>
        internal sealed class StripLive : MonoBehaviour
        {
            internal sealed class Clock
            {
                public TextMeshProUGUI Text;
                public string Id;
                public int Day;
                public float Recheck;
                public Occurrence Occ;
                public bool Has;
            }

            internal readonly List<Clock> Times = new List<Clock>();
            internal RectTransform Content;
            internal Transform TodayScene;
            internal int TodayDay = int.MinValue;
            private bool ready;

            internal static StripLive Of(Transform strip)
            {
                StripLive live = strip.GetComponent<StripLive>();
                if (live == null) live = strip.gameObject.AddComponent<StripLive>();
                if (!live.ready) live.Scan(strip);
                return live;
            }

            private void Scan(Transform strip)
            {
                ready = true;
                Times.Clear();
                foreach (TextMeshProUGUI t in strip.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (!t.name.StartsWith(TimePrefix)) continue;
                    string[] p = t.name.Substring(TimePrefix.Length).Split(':');
                    if (p.Length == 2 && int.TryParse(p[1], out int day)) Times.Add(new Clock { Text = t, Id = p[0], Day = day });
                }
                Content = strip.Find(ViewportName + "/" + ContentName) as RectTransform;
                TodayScene = null;
                TodayDay = int.MinValue;
                foreach (Transform c in strip.GetComponentsInChildren<Transform>(true))
                {
                    if (!c.name.StartsWith(TodayMark)) continue;
                    if (int.TryParse(c.name.Substring(TodayMark.Length), out int d)) { TodayScene = c.parent; TodayDay = d; }
                    break;
                }
            }
        }

        private const string TodayMark = "today:";

        /// <summary>Ícone de evento sob o mouse numa bolha com a tira: devolve o termo ("cal:evento:dia") e o retângulo na tela.</summary>
        public static string EventAt(Transform tip, Vector2 screen, out Rect rect)
        {
            rect = default;
            Transform strip = tip != null ? tip.Find(Name) : null;
            if (strip == null) return null;
            var c = new Vector3[4];
            var view = strip.Find(ViewportName) as RectTransform;   // carrossel: o que saiu da janela não conta
            if (view != null && !RectTransformUtility.RectangleContainsScreenPoint(view, screen, null)) return null;
            foreach (RectTransform cell in strip.GetComponentsInChildren<RectTransform>())
            {
                if (!cell.name.StartsWith(LinkPrefix) && !cell.name.StartsWith(DayPrefix)) continue;
                cell.GetWorldCorners(c);
                Rect r = Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
                if (!r.Contains(screen)) continue;
                rect = r;
                return cell.name;
            }
            return null;
        }

        /// <summary>Conteúdo da bolha de um termo "cal:evento:dia": o mesmo popup de uma marca da linha do tempo.</summary>
        public static List<LazyWidgetDataBase> EventData(string link, bool full)
        {
            if (link == null || !CalendarModel.Ready) return null;
            if (link.StartsWith(DayPrefix))
            {
                // Cena de um dia: o popup daquele dia (de hoje em diante).
                string[] d = link.Substring(DayPrefix.Length).Split(':');
                int today = (int)System.Math.Floor(CalendarModel.Now);
                return d.Length == 2 && int.TryParse(d[1], out int abs) && abs >= today ? CalendarTooltip.Day(d[0], full) : null;
            }
            if (!link.StartsWith(LinkPrefix)) return null;
            string[] p = link.Substring(LinkPrefix.Length).Split(':');
            if (p.Length != 2 || !int.TryParse(p[1], out int day)) return null;
            foreach (Occurrence o in CalendarModel.OnDay(day))
                if (o.Def.Id == p[0]) return CalendarTooltip.Occurrence(o, full);
            return null;
        }

        internal static bool EnsureWheel()
        {
            if (wheel != null) return true;
            wheel = Resources.FindObjectsOfTypeAll<UIHUDWheel>().FirstOrDefault(w => w.gameObject.scene.IsValid());
            if (wheel == null) return false;
            Traverse tw = Traverse.Create(wheel);
            gradSky = tw.Field("gradientSky").GetValue<Gradient>();
            gradHorizon = tw.Field("gradientHorizon").GetValue<Gradient>();
            return true;
        }

        private static string Signature(string target, List<int> days, double now)
        {
            var sb = new StringBuilder(target).Append(Carousel ? 'c' : 's').Append('|').Append(LLBase.CurrentLang).Append('|').Append(IsDaytime(Frac(now)) ? 'D' : 'N');
            KnowledgeSystem ks = MainGame.Instance.GameSave.knowledgeSystem;
            foreach (int d in days)
            {
                sb.Append('|').Append(d).Append(ks.unlockedCustomHudDaySprites.Contains(CalendarModel.DayIdOf(d)) ? 'u' : 'l');
                foreach (Occurrence o in CalendarModel.OnDay(d)) sb.Append(o.Def.Id).Append((int)CalendarModel.State(o, now));
            }
            return sb.ToString();
        }

        private static float Frac(double now) => (float)(now - System.Math.Floor(now));

        private static GameObject Build(Transform tip, List<int> days, double now, Styles s, bool week, bool carousel, bool labels)
        {
            var root = new GameObject(Name, typeof(RectTransform));
            root.transform.SetParent(tip, false);
            var rows = root.AddComponent<VerticalLayoutGroup>();
            rows.spacing = 2f;
            rows.childAlignment = TextAnchor.UpperCenter;
            rows.childControlWidth = rows.childControlHeight = true;
            rows.childForceExpandWidth = rows.childForceExpandHeight = false;
            rows.padding = new RectOffset(0, 0, 2, 2);
            int today = (int)System.Math.Floor(now);
            Transform row = null;
            if (carousel)
            {
                // Uma linha só, dentro de uma janela com máscara; o conteúdo desliza para a esquerda conforme o dia passa.
                var view = new GameObject(ViewportName, typeof(RectTransform));
                view.transform.SetParent(root.transform, false);
                view.AddComponent<RectMask2D>();
                var vle = view.AddComponent<LayoutElement>();
                vle.minWidth = vle.preferredWidth = CarouselVisible * WeekW;
                var content = new GameObject(ContentName, typeof(RectTransform));
                var crt = (RectTransform)content.transform;
                crt.SetParent(view.transform, false);
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 1f);
                var ch = content.AddComponent<HorizontalLayoutGroup>();
                ch.childAlignment = TextAnchor.UpperLeft;
                ch.childControlWidth = ch.childControlHeight = true;
                ch.childForceExpandWidth = ch.childForceExpandHeight = false;
                var fit = content.AddComponent<ContentSizeFitter>();
                fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                foreach (int d in days) Card(crt, d, d == today, now, s, true, labels);
                LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
                vle.minHeight = vle.preferredHeight = crt.rect.height + 6f;   // folga para a hora embaixo do ícone
                // Marcador de agora, fixo na janela: o que está à esquerda dele já passou (escurecido).
                float top = 0f;
                foreach (RectTransform r in crt.GetComponentsInChildren<RectTransform>())
                    if (r.name.StartsWith(DayPrefix)) { var c = new Vector3[4]; r.GetWorldCorners(c); top = crt.InverseTransformPoint(c[1]).y; break; }
                Mark(view.transform, "Past", 0f, top, CarouselBack, new Color(0f, 0f, 0f, 0.35f));
                Mark(view.transform, "NowShade", CarouselBack - 1f, top, 3f, new Color(0.17f, 0.09f, 0.05f, 1f));
                Mark(view.transform, "Now", CarouselBack, top, 1f, new Color(1f, 0.91f, 0.66f, 1f));
                return root;
            }
            for (int i = 0; i < days.Count; i++)
            {
                if (i % Columns == 0)
                {
                    var go = new GameObject("Row", typeof(RectTransform));
                    go.transform.SetParent(root.transform, false);
                    var h = go.AddComponent<HorizontalLayoutGroup>();
                    h.spacing = 0f;
                    h.childAlignment = TextAnchor.UpperCenter;
                    h.childControlWidth = h.childControlHeight = true;
                    h.childForceExpandWidth = h.childForceExpandHeight = false;
                    row = go.transform;
                }
                Card(row, days[i], days[i] == today, now, s, week, labels);
            }
            return root;
        }

        private static void Mark(Transform view, string name, float x, float top, float width, Color color)
        {
            Image img = Child(view, name, null, new Vector2(width, WeekH));
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, top);
            img.color = color;
        }

        private static void Card(Transform parent, int day, bool isToday, double now, Styles s, bool week, bool labels)
        {
            var card = new GameObject("Day", typeof(RectTransform));
            card.transform.SetParent(parent, false);
            var v = card.AddComponent<VerticalLayoutGroup>();
            v.spacing = 2f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;
            card.AddComponent<LayoutElement>().minWidth = week ? WeekW : SceneW;

            int today = (int)System.Math.Floor(now);
            // O rótulo do dia ("Today", "In 2 days") só entra na bolha fixada; no hover, o sigilo e a ordem bastam.
            if (week && labels)
                Label(card.transform, day < today ? CalendarL10n.T("yesterday") : CalendarL10n.Relative(day - today), isToday ? s.SmallSelected : s.Small, SceneW);
            RectTransform scene = Scene(card.transform, day, isToday ? Frac(now) : 0.5f, week);
            if (isToday) new GameObject(TodayMark + day, typeof(RectTransform)).transform.SetParent(scene, false);
            if (!week) return;

            // Os que têm hora vão por último: a hora fica embaixo do ícone, na folga depois da última linha.
            List<Occurrence> occ = CalendarModel.OnDay(day).Where(o => o.Def.Art != null).OrderBy(o => o.Instant && !o.Def.NoClock ? 1 : 0).ToList();
            if (occ.Count == 0) return;
            var icons = new GameObject("Events", typeof(RectTransform));
            icons.transform.SetParent(card.transform, false);
            var g = icons.AddComponent<GridLayoutGroup>();
            bool anyTime = occ.Any(o => o.Instant && !o.Def.NoClock);
            g.cellSize = new Vector2(IconCell, IconCell);
            g.padding = new RectOffset(0, 0, 0, anyTime ? (int)TimeH : 0);
            g.spacing = new Vector2(4f, 2f);
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = Mathf.Min(4, occ.Count);
            g.childAlignment = TextAnchor.UpperCenter;
            EasySpritesCollection col = LazySingletonSO<EasySpritesCollection>.Instance;
            foreach (Occurrence o in occ)
            {
                var cell = new GameObject(LinkPrefix + o.Def.Id + ":" + day, typeof(RectTransform));
                cell.transform.SetParent(icons.transform, false);
                if (!col.HasSprite(o.Def.Art)) continue;
                Sprite sp = col.GetSprite(o.Def.Art);
                bool past = CalendarModel.State(o, now) == OccState.Past;
                var img = Child(cell.transform, "Icon", sp, sp.rect.size * (25f / sp.pixelsPerUnit));   // metade do tamanho nativo: 1 texel = 1 px de tela
                RectTransform irt = img.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 1f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.anchoredPosition = new Vector2(0f, -IconCell * 0.5f) + PackedOffset(sp) * 0.5f;
                img.color = new Color(1f, 1f, 1f, past ? 0.35f : 1f);
                Material key = Quests.QuestModel.PortraitMaterial();   // tira o contorno azul (cor-chave) dos ícones
                if (key != null) img.material = key;
                if (o.Instant && !o.Def.NoClock)
                {
                    TextMeshProUGUI t = Label(cell.transform, TimeText(o, now), past ? s.Small : s.SmallSelected, IconCell);
                    t.gameObject.name = TimePrefix + o.Def.Id + ":" + day;
                    RectTransform trt = t.rectTransform;
                    trt.anchorMin = trt.anchorMax = new Vector2(0.5f, 0f);
                    trt.pivot = new Vector2(0.5f, 1f);
                    trt.sizeDelta = new Vector2(IconCell + 8f, TimeH);
                    trt.anchoredPosition = Vector2.zero;
                }
            }
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

        internal static RectTransform Scene(Transform parent, int day, float tod, bool badge)
        {
            Traverse w = Traverse.Create(wheel);
            string dayId = CalendarModel.DayIdOf(day);
            bool daytime = tod >= 0.25f && tod < 0.8f;   // mesmo corte do UIHUDWheel.OnTimeOfDayChanged
            bool unlocked = MainGame.Instance.GameSave.knowledgeSystem.unlockedCustomHudDaySprites.Contains(dayId);
            // Arte expandida embutida na DLL (Calendar/art/<dia>_dia|noite.png); sem ela, a nativa do medalhão.
            // Na semana, o ladrilho próprio (<dia>_semana_*: já no tamanho do recorte, com bordas que emendam no vizinho).
            string key = (unlocked ? dayId : "city"), suffix = daytime ? "_dia" : "_noite";
            Sprite tile = badge ? Expanded(key + "_semana" + suffix) : null;
            Sprite art = tile ?? Expanded(key + suffix);
            if (art == null && unlocked)
                art = w.Field(daytime ? "dayCustomWheelCenterIconDay" : "dayCustomWheelCenterIconNight").GetValue<List<Sprite>>()
                    .Find(x => x != null && x.name == dayId);
            if (art == null) art = w.Field(daytime ? "centerWheelIconDaySpriteDefault" : "centerWheelIconNightSpriteDefault").GetValue<Sprite>();

            var holder = new GameObject(badge ? DayPrefix + dayId + ":" + day : "Scene", typeof(RectTransform));
            holder.transform.SetParent(parent, false);
            var le = holder.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = badge ? WeekW : SceneW;
            le.minHeight = le.preferredHeight = badge ? WeekH : SceneH;
            if (badge && tile == null) holder.AddComponent<RectMask2D>();   // só o recorte da cena inteira precisa de máscara
            Image skySrc = w.Field("sky").GetValue<Image>(), horizonSrc = w.Field("horizon").GetValue<Image>();
            Fill(Child(holder.transform, "Sky", null, Vector2.zero)).color = w.Field("gradientSky").GetValue<Gradient>().Evaluate(tod);
            Fill(Child(holder.transform, "Horizon", horizonSrc.sprite, Vector2.zero)).color = w.Field("gradientHorizon").GetValue<Gradient>().Evaluate(tod);
            Image a = Child(holder.transform, "Art", art, art.rect.size * (50f / art.pixelsPerUnit));
            a.rectTransform.anchorMin = a.rectTransform.anchorMax = a.rectTransform.pivot = new Vector2(0.5f, 0f);
            a.rectTransform.anchoredPosition = badge && tile == null ? new Vector2(0f, -CropBottom) : Vector2.zero;
            if (!badge) return (RectTransform)holder.transform;
            Sprite sigil = w.Field("dayIcons").GetValue<List<Sprite>>().Find(x => x != null && x.name == dayId);
            if (sigil == null) return (RectTransform)holder.transform;
            Image b = Child(holder.transform, "Sigil", sigil, sigil.rect.size * (50f / sigil.pixelsPerUnit));
            b.rectTransform.anchorMin = b.rectTransform.anchorMax = b.rectTransform.pivot = new Vector2(0f, 1f);
            b.rectTransform.anchoredPosition = new Vector2(1f, -1f);
            return (RectTransform)holder.transform;
        }

        /// <summary>Atualiza as cores do céu e do horizonte de uma cena já montada para a hora dada (a cena de hoje anda com o relógio).</summary>
        internal static void Retint(Transform scene, float tod)
        {
            if (scene == null || !EnsureWheel()) return;
            Tint(scene.Find("Sky"), gradSky, tod);
            Tint(scene.Find("Horizon"), gradHorizon, tod);
        }

        // Só toca na cor quando ela muda de fato: cor nova suja a malha do canvas.
        private static void Tint(Transform t, Gradient g, float tod)
        {
            Image img = t != null && g != null ? t.GetComponent<Image>() : null;
            if (img == null) return;
            Color32 want = g.Evaluate(tod), have = img.color;
            if (want.r != have.r || want.g != have.g || want.b != have.b || want.a != have.a) img.color = want;
        }

        /// <summary>Mesmo corte dia/noite do UIHUDWheel.OnTimeOfDayChanged.</summary>
        internal static bool IsDaytime(float tod) => tod >= 0.25f && tod < 0.8f;

        private static readonly Dictionary<string, Sprite> expanded = new Dictionary<string, Sprite>();
        private static GK2Shared.FrameBudget warm;

        // Por reflexão: as sobrecargas com Span do módulo não compilam contra net472.
        private static System.Reflection.MethodInfo loadImage;
        private static System.Reflection.MethodInfo LoadImage => loadImage ?? (loadImage = AccessTools.Method(
            System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule"), "LoadImage", new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) }));

        /// <summary>
        /// Decodifica a arte embutida aos poucos, antes do primeiro uso (uma imagem pequena por frame, dentro de
        /// <paramref name="ms"/>): o primeiro popup e a primeira aba não pagam a decodificação de uma vez.
        /// </summary>
        internal static void WarmUp(double ms)
        {
            if (warm == null)
            {
                warm = new GK2Shared.FrameBudget();
                const string pre = "calart.", ext = ".png";
                foreach (string res in typeof(WeekStrip).Assembly.GetManifestResourceNames())
                {
                    if (!res.StartsWith(pre) || !res.EndsWith(ext)) continue;
                    string name = res.Substring(pre.Length, res.Length - pre.Length - ext.Length);
                    warm.Add(() => Expanded(name));
                }
            }
            if (warm.Pending > 0) warm.Run(ms);
        }

        private static Sprite Expanded(string name)
        {
            if (expanded.TryGetValue(name, out Sprite cached)) return cached;
            Sprite sprite = null;
            using (System.IO.Stream st = typeof(WeekStrip).Assembly.GetManifestResourceStream("calart." + name + ".png"))
            {
                if (st != null)
                {
                    var bytes = new byte[st.Length];
                    st.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                    System.Reflection.MethodInfo load = LoadImage;
                    if (load != null && (bool)load.Invoke(null, new object[] { tex, bytes, true }))
                    {
                        sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 50f);
                        sprite.hideFlags = HideFlags.HideAndDontSave;
                    }
                }
            }
            expanded[name] = sprite;
            return sprite;
        }

        /// <summary>Solta as texturas carregadas (hot-reload).</summary>
        public static void Dispose()
        {
            foreach (Sprite s in expanded.Values)
                if (s != null) { Object.Destroy(s.texture); Object.Destroy(s); }
            expanded.Clear();
            warm = null;
            builtSig.Clear();
            wheel = null;
        }

        private static Image Fill(Image img)
        {
            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return img;
        }

        private static Image Child(Transform parent, string name, Sprite sprite, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.type = Image.Type.Simple;
            img.raycastTarget = false;
            return img;
        }

        private static TextMeshProUGUI Label(Transform parent, string text, TextStyle style, float width)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            go.AddComponent<TextStyleComponent>().SetTextStyle(style);
            tmp.text = text;
            return tmp;
        }
    }
}
