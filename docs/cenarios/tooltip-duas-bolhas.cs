using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// PoC de tooltips aninhados: tooltip nativo de um item + 2 clones do UITooltip encadeados, com raycast e <link> testados. Limpe com tooltip-limpar-clones.cs.
public static class Snippet
{
    static readonly MethodInfo ShowAt = AccessTools.Method(typeof(UITooltip), "ShowAtTargetAndCalculateOffsets");
    static readonly FieldInfo Instance = AccessTools.Field(typeof(UITooltip), "instance");

    public static object Run() { return Go(); }

    static TextStyle Style(UITooltip t, string f) { return (TextStyle)AccessTools.Field(typeof(UITooltip), f).GetValue(t); }

    static UITooltip Clone(UITooltip original, string name, int order)
    {
        var go = Object.Instantiate(original.gameObject, original.transform.parent);
        go.name = name;
        go.SetActive(false);
        var c = go.GetComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = order;
        return go.GetComponent<UITooltip>();
    }

    static void Show(UITooltip t, List<LazyWidgetDataBase> data, RectTransform target, TooltipPlacementPriority prio = TooltipPlacementPriority.TopRight)
    {
        ShowAt.Invoke(t, new object[] { data, target, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, prio });
    }

    // RectTransform filho do texto cobrindo os caracteres [first, first+len): âncora do tooltip aninhado no termo.
    static RectTransform AnchorOn(TMP_Text tmp, int first, int len)
    {
        tmp.ForceMeshUpdate();
        var ci = tmp.textInfo.characterInfo;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int i = first; i < first + len; i++)
        {
            if (!ci[i].isVisible) continue;
            min = Vector2.Min(min, ci[i].bottomLeft); max = Vector2.Max(max, ci[i].topRight);
        }
        var go = new GameObject("TermAnchor", typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(tmp.transform, false);
        var le = go.AddComponent<LayoutElement>(); le.ignoreLayout = true;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localPosition = (min + max) * 0.5f;
        rt.sizeDelta = max - min;
        return rt;
    }

    static List<GameObject> Raycast(Vector2 pos)
    {
        var ped = new PointerEventData(EventSystem.current) { position = pos };
        var res = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, res);
        return res.Select(r => r.gameObject).ToList();
    }

    static string Path(Transform t) { return t.parent == null || t.name.StartsWith("UITooltip") ? t.name : Path(t.parent) + "/" + t.name; }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        foreach (var oldA in Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name == "TermAnchor"))
            Object.Destroy(oldA.gameObject);
        foreach (var old in Object.FindObjectsByType<UITooltip>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name.StartsWith("UITooltip_Clone")))
            Object.Destroy(old.gameObject);
        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        for (int i = 0; i < 20; i++) yield return null;

        var cell = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.isActiveAndEnabled && c.DisplayingItem != null && c.DisplayingItem.id == "brick");
        UITooltip.ShowItemCell(cell);
        for (int i = 0; i < 3; i++) yield return null;
        var original = (UITooltip)Instance.GetValue(null);

        // Bolha 1: ancorada no último texto do tooltip original ("Crafted at: ...").
        var a = Clone(original, "UITooltip_Clone1", 710);
        var hb = Style(original, "headerBoldTextStyle");
        var dataA = new List<LazyWidgetDataBase> {
            new UITooltipTextWidgetData("Smithy I", TextAlignmentOptions.Center, Style(original, "headerTextStyle")),
            new UITooltipSeparatorWidgetData(),
            new UITooltipTextWidgetData(hb.ColorizeTags("Crafted at: [Workbench]"), TextAlignmentOptions.Center, Style(original, "descriptionTextStyle")),
            new UITooltipTextWidgetData("Requer <link=\"kiln\">" + hb.ApplyStyleToString("Kiln") + "</link> e tijolos.", TextAlignmentOptions.Center, Style(original, "smallDescriptionTextStyle")),
        };
        var craftedTmp = original.GetComponentsInChildren<UITooltipTextWidget>().Last().GetComponent<TMP_Text>();
        craftedTmp.ForceMeshUpdate();
        string parsed = craftedTmp.GetParsedText();
        int idx = parsed.IndexOf("Smithy I");
        var anchorA = AnchorOn(craftedTmp, idx, "Smithy I".Length);
        sb.AppendLine("âncora A sobre 'Smithy I' (idx " + idx + "): pos=" + anchorA.position + " size=" + anchorA.rect.size);
        Show(a, dataA, anchorA);
        for (int i = 0; i < 3; i++) yield return null;

        // Bolha 2: ancorada no texto com link da bolha 1 (recursão).
        var b = Clone(original, "UITooltip_Clone2", 720);
        var dataB = new List<LazyWidgetDataBase> {
            new UITooltipTextWidgetData("Kiln", TextAlignmentOptions.Center, Style(original, "headerTextStyle")),
            new UITooltipSeparatorWidgetData(),
            new UITooltipTextWidgetData("Bolha aninhada de nível 2.", TextAlignmentOptions.Center, Style(original, "descriptionTextStyle")),
        };
        var linkWidget = a.GetComponentsInChildren<UITooltipTextWidget>().First(w => w.GetComponent<TMP_Text>().text.Contains("<link"));
        var lt = linkWidget.GetComponent<TMP_Text>();
        lt.ForceMeshUpdate();
        var l0 = lt.textInfo.linkInfo[0];
        var anchorB = AnchorOn(lt, l0.linkTextfirstCharacterIndex, l0.linkTextLength);
        Show(b, dataB, anchorB, TooltipPlacementPriority.BottomRight);
        for (int i = 0; i < 3; i++) yield return null;

        foreach (var t in new[] { original, a, b })
            sb.AppendLine(t.name + ": active=" + t.gameObject.activeSelf + " alpha=" + t.GetComponent<CanvasGroup>().alpha + " order=" + t.GetComponent<Canvas>().sortingOrder + " pos=" + t.transform.position + " size=" + ((RectTransform)t.transform).rect.size + " filhos=" + t.GetComponentsInChildren<UITooltipTextWidget>().Length);
        sb.AppendLine("instance continua o original: " + ((UITooltip)Instance.GetValue(null) == original));

        // Raycast antes de ajustar: centro de cada bolha.
        foreach (var t in new[] { original, a })
        {
            var hits = Raycast(t.transform.position);
            sb.AppendLine("raycast centro " + t.name + " (sem GraphicRaycaster): " + (hits.Count == 0 ? "nada" : string.Join(" | ", hits.Take(4).Select(h => Path(h.transform)))));
        }

        // Ajuste no clone 1: GraphicRaycaster + raycastTarget no TMP.
        a.gameObject.AddComponent<GraphicRaycaster>();
        var tmp = linkWidget.GetComponent<TMP_Text>();
        tmp.raycastTarget = true;
        yield return null;
        tmp.ForceMeshUpdate();
        var ti = tmp.textInfo;
        sb.AppendLine("linkCount=" + ti.linkCount);
        if (ti.linkCount > 0)
        {
            var li = ti.linkInfo[0];
            var c0 = ti.characterInfo[li.linkTextfirstCharacterIndex];
            var c1 = ti.characterInfo[li.linkTextfirstCharacterIndex + li.linkTextLength - 1];
            Vector3 mid = tmp.transform.TransformPoint((c0.bottomLeft + c1.topRight) * 0.5f);
            Vector2 p = mid;
            var hits = Raycast(p);
            sb.AppendLine("ponto do link (tela)=" + p + " id=" + li.GetLinkID() + " texto=" + li.GetLinkText());
            sb.AppendLine("raycast no link: " + string.Join(" | ", hits.Take(4).Select(h => Path(h.transform))));
            sb.AppendLine("FindIntersectingLink(cam=null)=" + TMP_TextUtilities.FindIntersectingLink(tmp, p, null));
            sb.AppendLine("FindIntersectingLink fora (p+60,0)=" + TMP_TextUtilities.FindIntersectingLink(tmp, p + new Vector2(200, 0), null));
            var hitsOrig = Raycast(original.transform.position);
            sb.AppendLine("raycast centro original depois: " + (hitsOrig.Count == 0 ? "nada" : string.Join(" | ", hitsOrig.Take(3).Select(h => Path(h.transform)))));
        }
        yield return sb.ToString();
    }
}
