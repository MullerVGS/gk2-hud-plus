using System.Collections.Generic;
using System.Text.RegularExpressions;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Termos-link: <c>&lt;link="tipo:id"&gt;Nome&lt;/link&gt;</c> dentro dos textos do tooltip. No hover normal o link
    /// não muda nada na aparência; na bolha fixada o termo ganha o estilo nativo de termo destacado
    /// (tiny_font-hint_regular_text_3, o mesmo que o jogo usa para nomes de perk em "Effect on use").
    /// </summary>
    internal static class Links
    {
        public const string Item = "item";
        public const string Craft = "craft";
        public const string Perk = "perk";
        public const string Station = "wgo";
        public const string Npc = "npc";
        public const string Quest = "quest";

        private static readonly Regex LinkRx = new Regex("<link=\"([^\"]+)\">(.*?)</link>", RegexOptions.Compiled);

        public static string Wrap(string kind, string id, string text) => "<link=\"" + kind + ":" + id + "\">" + text + "</link>";

        public static bool HasLinks(string text) => text != null && text.IndexOf("<link=", System.StringComparison.Ordinal) >= 0;

        public static string Highlight(string text, TextStyle term)
        {
            if (!HasLinks(text) || term == null) return text;
            return LinkRx.Replace(text, m => "<link=\"" + m.Groups[1].Value + "\">" + term.ApplyStyleToString(m.Groups[2].Value) + "</link>");
        }

        /// <summary>Cópia da lista com os termos destacados (os dados nativos são imutáveis; os de texto são recriados).</summary>
        public static List<LazyWidgetDataBase> Highlighted(List<LazyWidgetDataBase> src, Styles s)
        {
            var dst = new List<LazyWidgetDataBase>(src.Count);
            foreach (LazyWidgetDataBase d in src)
            {
                // Cabeçalho com link (tooltip de mundo: o nome do objeto leva à estação) mantém o estilo de cabeçalho.
                if (d is UITooltipTextWidgetData t && HasLinks(t.Text) && (s == null || t.TextStyle != s.Header))
                    dst.Add(new UITooltipTextWidgetData(Highlight(t.Text, s?.Term), t.TextAlignmentOptions, t.TextStyle));
                else
                    dst.Add(d);
            }
            return dst;
        }

        /// <summary>Link sob o ponto de tela entre os textos com link da bolha (coletados no render); devolve o retângulo do termo em px de tela.</summary>
        public static string LinkAt(List<TMP_Text> texts, Vector2 screen, out Rect termRect)
        {
            termRect = default;
            foreach (TMP_Text tmp in texts)
            {
                if (tmp == null || !tmp.isActiveAndEnabled) continue;
                int idx = TMP_TextUtilities.FindIntersectingLink(tmp, screen, null);
                if (idx < 0 || idx >= tmp.textInfo.linkCount) continue;
                TMP_LinkInfo li = tmp.textInfo.linkInfo[idx];
                termRect = TermRect(tmp, li.linkTextfirstCharacterIndex, li.linkTextLength);
                return li.GetLinkID();
            }
            return null;
        }

        /// <summary>Todos os links visíveis (id e centro em tela) — usado pelos cenários de teste.</summary>
        public static List<KeyValuePair<string, Rect>> AllLinks(Transform root)
        {
            var res = new List<KeyValuePair<string, Rect>>();
            foreach (TMP_Text tmp in root.GetComponentsInChildren<TMP_Text>())
            {
                if (!tmp.isActiveAndEnabled || !HasLinks(tmp.text)) continue;
                tmp.ForceMeshUpdate();
                for (int i = 0; i < tmp.textInfo.linkCount; i++)
                {
                    TMP_LinkInfo li = tmp.textInfo.linkInfo[i];
                    res.Add(new KeyValuePair<string, Rect>(li.GetLinkID(), TermRect(tmp, li.linkTextfirstCharacterIndex, li.linkTextLength)));
                }
            }
            return res;
        }

        private static Rect TermRect(TMP_Text tmp, int first, int len)
        {
            TMP_CharacterInfo[] ci = tmp.textInfo.characterInfo;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = first; i < first + len && i < ci.Length; i++)
            {
                if (!ci[i].isVisible) continue;
                Vector3 a = tmp.transform.TransformPoint(ci[i].bottomLeft), b = tmp.transform.TransformPoint(ci[i].topRight);
                min = Vector2.Min(min, a);
                max = Vector2.Max(max, b);
            }
            if (min.x > max.x) return default;
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
