using System.Collections;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Map: abre o mapa (aba Map da janela do TAB) e dá PASSOS de scroll (+ aproxima, − afasta) com o mouse virtual em MOUSE (px de tela).
public static class Snippet
{
    const int PASSOS = 1;
    static readonly Vector2 MOUSE = new Vector2(960f, 511f);

    public static IEnumerator Run()
    {
        var w = LazyUI.GetWindow<CharacterWindow>();
        var page = w.transform.Find("Content/Pages/MapPage");
        if (!w.IsShown || page == null || !page.gameObject.activeInHierarchy)
        {
            if (w.IsShown) { w.Close(); yield return null; yield return null; }
            Transform pages = w.transform.Find("Content/Pages");
            if (pages != null) foreach (Transform p in pages) if (p.GetComponent<LazyWidgetBase>() != null) p.gameObject.SetActive(false);
            w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map));
            yield return null; yield return null;
        }
        var zoomType = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Map").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance).GetType().Assembly.GetType("GK2Map.MapZoom");
        AccessTools.Property(zoomType, "VirtualMouse").SetValue(null, (Vector2?)MOUSE);
        int n = Mathf.Abs(PASSOS);
        for (int i = 0; i < n; i++)
        {
            AccessTools.Property(zoomType, "TestScroll").SetValue(null, PASSOS > 0 ? 1 : -1);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.4f) yield return null;
        }
        var plugin = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Map").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
        yield return "zoom índice " + AccessTools.Field(plugin.GetType(), "ZoomIndex").GetValue(plugin);
    }
}
