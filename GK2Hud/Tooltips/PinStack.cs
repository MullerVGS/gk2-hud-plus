using System.Collections.Generic;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Tooltips
{
    /// <summary>Uma bolha clonada do UITooltip: fixada (parada, com raycast) ou aninhada transitória.</summary>
    internal sealed class Bubble
    {
        public UITooltip Tip;
        public RectTransform Rt;
        public Bubble Parent;
        public int Depth;
        public bool Pinned;
        public List<LazyWidgetDataBase> Data;   // conteúdo sem destaque (o destaque é aplicado ao renderizar fixada)
        public string SourceLink;               // termo da mãe que abriu esta bolha
        public Rect SourceRect;                 // retângulo do termo/célula na mãe (px de tela)
        public Vector3? RootPos;                // raiz: posição do tooltip nativo no momento de fixar
        public int RootCorner = -1;             // raiz: ponta ativa do nativo
        public int Side = 1;                    // +1 abre à direita da mãe, -1 à esquerda

        public bool Contains(Vector2 screen) => Rt != null && Rt.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(Rt, screen, null);

        public Rect ScreenRect()
        {
            var c = new Vector3[4];
            Rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }
    }

    /// <summary>
    /// Pilha de bolhas: clones de um template limpo do UITooltip nativo (sem os widgets do pool), canvas abaixo do
    /// nativo (700) e acima das janelas (400+), para o tooltip nativo continuar aparecendo por cima.
    /// Aninhadas abrem ao lado da mãe, nunca sobre ela.
    /// </summary>
    internal sealed class PinStack
    {
        public const string Prefix = "GK2Hud_";
        private const int BaseSorting = 600;
        private const float Gap = -24f;   // a moldura hint-frame tem ~14 px transparentes em cada lado: sobra ~4 px visíveis

        private GameObject template;
        public readonly List<Bubble> Pinned = new List<Bubble>();
        public Bubble Transient;

        public bool Any => Pinned.Count > 0 || Transient != null;
        public Bubble Top => Pinned.Count > 0 ? Pinned[Pinned.Count - 1] : null;

        /// <summary>Destrói restos de uma carga anterior (crash ou reload que não limpou).</summary>
        public static int DestroyOrphans()
        {
            int n = 0;
            foreach (UITooltip t in Object.FindObjectsByType<UITooltip>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.name.StartsWith(Prefix)) { Object.Destroy(t.gameObject); n++; }
            }
            return n;
        }

        private bool EnsureTemplate()
        {
            if (template != null) return true;
            UITooltip native = Native.Instance;
            if (native == null) return false;
            bool was = native.gameObject.activeSelf;
            template = Object.Instantiate(native.gameObject, native.transform.parent);
            template.name = Prefix + "Template";
            template.SetActive(false);
            if (was != native.gameObject.activeSelf) native.gameObject.SetActive(was);
            var tip = template.GetComponent<UITooltip>();
            var container = template.GetComponent<LazyWidgetContainer>();
            var used = (List<LazyWidgetBase>)Native.UsedWidgetsField.GetValue(container);
            foreach (LazyWidgetBase w in used)
                if (w != null) Object.DestroyImmediate(w.gameObject);
            used.Clear();
            // Qualquer outro filho que não seja ponta da moldura (ex.: lixo de cenários) também sai.
            var keep = new HashSet<Transform>();
            foreach (UIBubbleCorner c in Native.Corners(tip)) keep.Add(c.transform);
            for (int i = template.transform.childCount - 1; i >= 0; i--)
            {
                Transform ch = template.transform.GetChild(i);
                if (!keep.Contains(ch)) Object.DestroyImmediate(ch.gameObject);
            }
            return true;
        }

        public Bubble Create(int depth)
        {
            if (!EnsureTemplate()) return null;
            GameObject go = Object.Instantiate(template, template.transform.parent);
            go.name = Prefix + "Bubble" + depth;
            Canvas cv = go.GetComponent<Canvas>();
            cv.overrideSorting = true;
            cv.sortingOrder = BaseSorting + depth * 2;
            if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
            return new Bubble { Tip = go.GetComponent<UITooltip>(), Rt = (RectTransform)go.transform, Depth = depth };
        }

        /// <summary>Monta os widgets na bolha. Raiz: mesma posição que o nativo teria; filhas: ao lado da mãe.</summary>
        public void Render(Bubble b, Styles s)
        {
            List<LazyWidgetDataBase> data = b.Pinned ? Links.Highlighted(b.Data, s) : new List<LazyWidgetDataBase>(b.Data);
            CanvasGroup g = Native.GroupOf(b.Tip);
            // Longe antes de mostrar: o UIBasicBubble usa DOMove quando a distância é pequena e sobrescreveria a posição.
            b.Rt.position = new Vector3(-10000f, -10000f, 0f);
            RectTransform target = b.Parent != null ? b.Parent.Rt : (RectTransform)Native.Instance.transform;
            Native.ShowAt.Invoke(b.Tip, new object[] { data, target, UIBasicBubble.ForceCornerPosition.Auto, false, Vector2.zero, TooltipPlacementPriority.TopRight });
            if (g != null) { g.alpha = 1f; g.interactable = true; g.blocksRaycasts = true; }
            if (b.Parent != null) PlaceBeside(b);
            else if (b.RootPos.HasValue)
            {
                b.Rt.position = b.RootPos.Value;
                int i = 0;
                foreach (UIBubbleCorner c in Native.Corners(b.Tip)) c.gameObject.SetActive(i++ == b.RootCorner);
            }
        }

        /// <summary>Ao lado da mãe (direita ou esquerda conforme espaço), topo alinhado ao termo, dentro da tela.</summary>
        private static void PlaceBeside(Bubble b)
        {
            foreach (UIBubbleCorner c in Native.Corners(b.Tip)) c.gameObject.SetActive(false);
            Rect m = b.Parent.ScreenRect();
            Rect r = b.ScreenRect();
            float w = r.width, h = r.height, sw = Screen.width, sh = Screen.height;
            bool fitsRight = m.xMax + Gap + w <= sw, fitsLeft = m.xMin - Gap - w >= 0f;
            int side = b.Parent.Side;
            if (side > 0 && !fitsRight && fitsLeft) side = -1;
            else if (side < 0 && !fitsLeft && fitsRight) side = 1;
            else if (!fitsRight && !fitsLeft) side = sw - m.xMax >= m.xMin ? 1 : -1;
            b.Side = side;
            float x = side > 0 ? m.xMax + Gap : m.xMin - Gap - w;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, sw - w));
            float top = b.SourceRect.height > 0f ? b.SourceRect.yMax + 28f : m.yMax;
            top = Mathf.Clamp(top, Mathf.Min(sh, h), sh);
            Vector2 d = new Vector2(Even(x) - r.xMin, Even(top) - r.yMax);
            b.Rt.position += (Vector3)d;
        }

        private static float Even(float v) => Mathf.Round(v / 2f) * 2f;

        public void Destroy(Bubble b)
        {
            if (b?.Rt != null) Object.Destroy(b.Rt.gameObject);
        }

        public void CloseTransient()
        {
            if (Transient == null) return;
            Destroy(Transient);
            Transient = null;
        }

        /// <summary>Fecha a bolha fixada e tudo acima dela (filhas e transitória).</summary>
        public void CloseFrom(Bubble b)
        {
            int i = Pinned.IndexOf(b);
            if (i < 0) return;
            CloseTransient();
            for (int k = Pinned.Count - 1; k >= i; k--)
            {
                Destroy(Pinned[k]);
                Pinned.RemoveAt(k);
            }
        }

        public void CloseAll()
        {
            CloseTransient();
            foreach (Bubble b in Pinned) Destroy(b);
            Pinned.Clear();
        }

        public void Dispose()
        {
            CloseAll();
            if (template != null) Object.Destroy(template);
            template = null;
        }
    }
}
