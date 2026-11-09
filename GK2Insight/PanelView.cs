using UnityEngine;
using UnityEngine.UI;

namespace GK2Insight
{
    /// <summary>
    /// Painel de texto em uGUI. O IMGUI recalculava tamanho e regerava o texto rico em todo evento de OnGUI
    /// (Layout, Repaint e entrada, várias vezes por frame); aqui o Canvas só reconstrói quando o texto muda.
    /// </summary>
    internal sealed class PanelView
    {
        private const int PadX = 10, PadY = 8;

        private readonly GameObject root;
        private readonly RectTransform rect;
        private readonly Text text;
        private readonly bool alignRight;
        private string shown;
        private int shownSize = -1;
        private float shownX = float.NaN, shownY = float.NaN, shownMax = float.NaN;

        public PanelView(Transform canvas, string name, bool alignRight, Font font)
        {
            this.alignRight = alignRight;
            root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            rect = (RectTransform)root.transform;
            rect.SetParent(canvas, false);
            Vector2 corner = alignRight ? new Vector2(1f, 1f) : new Vector2(0f, 1f);
            rect.anchorMin = rect.anchorMax = rect.pivot = corner;
            Image bg = root.GetComponent<Image>();
            bg.color = new Color(0.04f, 0.04f, 0.06f, 0.78f);
            bg.raycastTarget = false;

            GameObject label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform lr = (RectTransform)label.transform;
            lr.SetParent(rect, false);
            lr.anchorMin = new Vector2(0f, 0f);
            lr.anchorMax = new Vector2(1f, 1f);
            lr.offsetMin = new Vector2(PadX, PadY);
            lr.offsetMax = new Vector2(-PadX, -PadY);
            text = label.GetComponent<Text>();
            text.font = font;
            text.supportRichText = true;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = new Color(0.9f, 0.89f, 0.85f);
            text.raycastTarget = false;
            root.SetActive(false);
        }

        /// <summary>x é a borda esquerda, ou a direita quando alignRight; y a partir do topo (como no IMGUI).</summary>
        public void Set(string content, int fontSize, float x, float y, float maxHeight)
        {
            bool visible = !string.IsNullOrEmpty(content);
            if (root.activeSelf != visible) root.SetActive(visible);
            if (!visible) return;
            if (content == shown && fontSize == shownSize && x == shownX && y == shownY && maxHeight == shownMax) return;
            shown = content;
            shownSize = fontSize;
            shownX = x;
            shownY = y;
            shownMax = maxHeight;
            text.fontSize = fontSize;
            text.text = content;
            float w = Mathf.Ceil(text.preferredWidth) + PadX * 2;
            float h = Mathf.Min(Mathf.Ceil(text.preferredHeight) + PadY * 2, maxHeight);
            rect.sizeDelta = new Vector2(w, h);
            rect.anchoredPosition = alignRight ? new Vector2(x - Screen.width, -y) : new Vector2(x, -y);
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        /// <summary>Retângulo em pixels de tela (Canvas overlay: mundo = tela, origem embaixo à esquerda).</summary>
        public bool TryGetScreenRect(out Rect r)
        {
            r = default;
            if (!root.activeSelf) return false;
            rect.GetWorldCorners(Corners);
            r = Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
            return true;
        }

        public void Hide()
        {
            if (root.activeSelf) root.SetActive(false);
        }
    }

    /// <summary>OnGUI isolado: só existe custo de IMGUI enquanto o overlay técnico está ligado.</summary>
    internal sealed class GuiHost : MonoBehaviour
    {
        public System.Action Draw;

        private void Awake() => useGUILayout = false;

        private void OnGUI() => Draw?.Invoke();
    }
}
