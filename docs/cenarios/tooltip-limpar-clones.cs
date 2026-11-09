using System.Linq;
using UnityEngine;

// Destrói os clones de UITooltip criados pelos cenários de tooltip (nome UITooltip_Clone*) e as âncoras TermAnchor e esconde o tooltip nativo.
public static class Snippet
{
    public static object Run()
    {
        var clones = Object.FindObjectsByType<UITooltip>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name.StartsWith("UITooltip_Clone")).ToList();
        foreach (var c in clones) Object.Destroy(c.gameObject);
        var anchors = Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(x => x.name == "TermAnchor").ToList();
        foreach (var a in anchors) Object.Destroy(a.gameObject);
        UITooltip.HideImmediately();
        return "clones destruídos: " + clones.Count + ", âncoras: " + anchors.Count;
    }
}
