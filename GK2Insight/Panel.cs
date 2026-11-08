using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>Painel de texto de debug: fonte monoespaçada, seções e colunas alinhadas.</summary>
    internal class Panel
    {
        private static readonly Regex Tag = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static string Dim(string s) => $"<color=#8a8a8a>{s}</color>";
        public static string Hi(string s) => $"<color=#f0d890>{s}</color>";
        public static string Warn(string s) => $"<color=#ff8070>{s}</color>";
        public static string Ok(string s) => $"<color=#90d890>{s}</color>";
        public static string Col(string hex, string s) => $"<color={hex}>{s}</color>";

        private readonly StringBuilder sb = new StringBuilder(2048);
        private GUIStyle style;
        private int styleSize;
        private Texture2D background;
        private Font mono;

        public string Text { get; private set; } = "";

        public void Begin() => sb.Length = 0;

        public void End() => Text = sb.ToString().TrimEnd();

        public void Section(string title, string note = null)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append("<b>").Append(Hi(title)).Append("</b>");
            if (!string.IsNullOrEmpty(note)) sb.Append("  ").Append(Dim(note));
            sb.Append('\n');
        }

        public void Line(string text) => sb.Append(text).Append('\n');

        /// <summary>Linhas em colunas: a largura de cada coluna é a do maior texto visível (sem tags).</summary>
        public void Table(List<string[]> rows, int indent = 1)
        {
            if (rows.Count == 0) return;
            int cols = 0;
            foreach (string[] r in rows) cols = Mathf.Max(cols, r.Length);
            int[] width = new int[cols];
            foreach (string[] r in rows)
                for (int i = 0; i < r.Length; i++)
                    width[i] = Mathf.Max(width[i], VisibleLength(r[i]));
            foreach (string[] r in rows)
            {
                sb.Append(' ', indent);
                for (int i = 0; i < r.Length; i++)
                {
                    string cell = r[i] ?? "";
                    sb.Append(cell);
                    if (i < r.Length - 1) sb.Append(' ', width[i] - VisibleLength(cell) + 2);
                }
                sb.Append('\n');
            }
        }

        /// <summary>x é a borda esquerda, ou a direita quando alignRight.</summary>
        public void Draw(float x, float y, int fontSize, float maxHeight, bool alignRight = false)
        {
            if (string.IsNullOrEmpty(Text)) return;
            EnsureStyle(fontSize);
            GUIContent content = new GUIContent(Text);
            Vector2 size = style.CalcSize(content);
            if (alignRight) x -= size.x;
            GUI.Box(new Rect(x, y, size.x, Mathf.Min(size.y, maxHeight)), content, style);
        }

        private static int VisibleLength(string s) => s == null ? 0 : Tag.Replace(s, "").Length;

        private void EnsureStyle(int fontSize)
        {
            if (style != null && styleSize == fontSize) return;
            if (background == null)
            {
                background = new Texture2D(1, 1);
                background.SetPixel(0, 0, new Color(0.04f, 0.04f, 0.06f, 0.78f));
                background.Apply();
            }
            if (mono == null)
            {
                mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, fontSize);
            }
            style = new GUIStyle(GUI.skin.box)
            {
                font = mono,
                fontSize = fontSize,
                alignment = TextAnchor.UpperLeft,
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Clip,
                padding = new RectOffset(10, 10, 8, 8)
            };
            style.normal.background = background;
            style.normal.textColor = new Color(0.9f, 0.89f, 0.85f);
            styleSize = fontSize;
        }
    }
}
