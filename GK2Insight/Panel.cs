using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>Texto de um painel de debug: seções e colunas alinhadas (desenhado por PanelView).</summary>
    internal class Panel
    {
        private static readonly Regex Tag = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static string Dim(string s) => $"<color=#8a8a8a>{s}</color>";
        public static string Hi(string s) => $"<color=#f0d890>{s}</color>";
        public static string Warn(string s) => $"<color=#ff8070>{s}</color>";
        public static string Ok(string s) => $"<color=#90d890>{s}</color>";
        public static string Col(string hex, string s) => $"<color={hex}>{s}</color>";

        private readonly StringBuilder sb = new StringBuilder(2048);

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

        private static int VisibleLength(string s) => s == null ? 0 : Tag.Replace(s, "").Length;
    }
}
