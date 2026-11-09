using System.Linq;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Só leitura: descreve o GameObject do UITooltip (hierarquia, canvas, raycast, 9-slice, layout, TextStyles) e o ColorizeTags.
public static class Snippet
{
    public static object Run()
    {
        var sb = new StringBuilder();
        var inst = (UITooltip)AccessTools.Field(typeof(UITooltip), "instance").GetValue(null);
        var go = inst.gameObject;
        sb.AppendLine("activeSelf=" + go.activeSelf + " layer=" + LayerMask.LayerToName(go.layer) + " scale=" + go.transform.localScale + " lossy=" + go.transform.lossyScale);
        var rt = (RectTransform)go.transform;
        sb.AppendLine("rect: anchorMin=" + rt.anchorMin + " anchorMax=" + rt.anchorMax + " pivot=" + rt.pivot + " size=" + rt.rect.size + " siblingIndex=" + rt.GetSiblingIndex() + "/" + rt.parent.childCount);
        var cv = go.GetComponent<Canvas>();
        sb.AppendLine("Canvas: overrideSorting=" + cv.overrideSorting + " sortingOrder=" + cv.sortingOrder + " sortingLayer=" + cv.sortingLayerName + " renderMode=" + cv.renderMode + " rootRenderMode=" + cv.rootCanvas.renderMode + " root=" + cv.rootCanvas.name + " rootScale=" + cv.rootCanvas.scaleFactor + " pixelPerfect=" + cv.pixelPerfect + " shaderChannels=" + cv.additionalShaderChannels);
        sb.AppendLine("GraphicRaycaster no tooltip: " + (go.GetComponent<GraphicRaycaster>() != null) + " | no root: " + (cv.rootCanvas.GetComponent<GraphicRaycaster>() != null));
        var cg = go.GetComponent<CanvasGroup>();
        sb.AppendLine("CanvasGroup: alpha=" + cg.alpha + " interactable=" + cg.interactable + " blocksRaycasts=" + cg.blocksRaycasts + " ignoreParentGroups=" + cg.ignoreParentGroups);
        var vlg = go.GetComponent<VerticalLayoutGroup>();
        sb.AppendLine("VLG: padding L" + vlg.padding.left + " R" + vlg.padding.right + " T" + vlg.padding.top + " B" + vlg.padding.bottom + " spacing=" + vlg.spacing + " align=" + vlg.childAlignment + " ctrlW=" + vlg.childControlWidth + " ctrlH=" + vlg.childControlHeight + " expandW=" + vlg.childForceExpandWidth + " expandH=" + vlg.childForceExpandHeight);
        var csf = go.GetComponent<ContentSizeFitter>();
        sb.AppendLine("ContentSizeFitter: h=" + csf.horizontalFit + " v=" + csf.verticalFit);
        var ole = go.GetComponent<OverrideLayoutElement>();
        sb.AppendLine("OverrideLayoutElement: useMaxWidth=" + ole.useMaxWidth + " maxWidth=" + ole.maxWidth + " useMaxHeight=" + ole.useMaxHeight + " maxHeight=" + ole.maxHeight + " snapping=" + ole.useHeightSnapping + "/" + ole.heightSnappingType + " minW=" + ole.minWidth + " prefW=" + ole.preferredWidth);
        var img = go.GetComponent<Image>();
        sb.AppendLine("Image: sprite=" + img.sprite.name + " rect=" + img.sprite.rect + " border=" + img.sprite.border + " ppu=" + img.sprite.pixelsPerUnit + " ppuMult=" + img.pixelsPerUnitMultiplier + " type=" + img.type + " fillCenter=" + img.fillCenter + " raycast=" + img.raycastTarget + " material=" + img.material.name);
        foreach (var f in new[] { "offsetAdd", "alternativeDownOffsetAdd" }) sb.AppendLine(f + "=" + AccessTools.Field(typeof(UITooltip), f).GetValue(inst));
        foreach (var f in new[] { "flipBackgroundH", "useSmoothPositioning", "instantUpdateOffset", "animationTime" }) sb.AppendLine("bubble." + f + "=" + AccessTools.Field(typeof(UIBasicBubble), f).GetValue(inst));
        var corners = (System.Collections.IList)AccessTools.Field(typeof(UIBasicBubble), "corners").GetValue(inst);
        sb.AppendLine("corners: " + string.Join(", ", corners.Cast<Component>().Select(c => c.name + (c.gameObject.activeSelf ? "*" : ""))));
        sb.AppendLine("TextStyles:");
        foreach (var f in AccessTools.GetDeclaredFields(typeof(UITooltip)).Where(f => f.FieldType == typeof(TextStyle)))
        {
            var ts = (TextStyle)f.GetValue(inst);
            sb.AppendLine("  " + f.Name + " = " + (ts == null ? "null" : ts.name + " font=" + ts.Font?.name + " color=#" + (ts.data.containsFontColor ? ColorUtility.ToHtmlStringRGBA(ts.data.fontColor) : "-")));
        }
        var hb = (TextStyle)AccessTools.Field(typeof(UITooltip), "headerBoldTextStyle").GetValue(inst);
        sb.AppendLine("ColorizeTags(\"Crafted at: [Workbench]\") = " + hb.ColorizeTags("Crafted at: [Workbench]"));
        sb.AppendLine("Hierarquia (inclui inativos):");
        Dump(go.transform, 0, sb);
        var wc = (LazyWidgetContainer)AccessTools.Field(typeof(UITooltip), "widgetContainer").GetValue(inst);
        var wcgo = (GameObject)AccessTools.Field(typeof(LazyWidgetContainer), "widgetContainerGO").GetValue(wc);
        sb.AppendLine("widgetContainer==self: " + (wc.gameObject == go) + " widgetContainerGO=" + (wcgo == null ? "null" : wcgo.name));
        return sb.ToString();
    }

    static void Dump(Transform t, int d, StringBuilder sb)
    {
        var r = (RectTransform)t;
        var comps = t.GetComponents<Component>().Where(c => !(c is RectTransform)).Select(c => c.GetType().Name);
        string extra = "";
        var g = t.GetComponent<Graphic>();
        if (g != null) extra += " raycastTarget=" + g.raycastTarget;
        var tmp = t.GetComponent<TMP_Text>();
        if (tmp != null) extra += " text=\"" + tmp.text.Replace("\n", "\\n") + "\" font=" + tmp.font.name + " size=" + tmp.fontSize + " wrap=" + tmp.textWrappingMode + " overflow=" + tmp.overflowMode + " margin=" + tmp.margin;
        var le = t.GetComponent<LayoutElement>();
        if (le != null && t != t.root) extra += " LE(minW=" + le.minWidth + " prefW=" + le.preferredWidth + " minH=" + le.minHeight + " prefH=" + le.preferredHeight + " ignore=" + le.ignoreLayout + ")";
        sb.AppendLine(new string(' ', d * 2) + (t.gameObject.activeSelf ? "" : "(off) ") + t.name + " size=" + r.rect.size + " [" + string.Join(",", comps) + "]" + extra);
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), d + 1, sb);
    }
}
