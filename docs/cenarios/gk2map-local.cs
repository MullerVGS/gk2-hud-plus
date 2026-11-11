using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Map: abre o mapa e, na setinha da zona ZONA, faz ACAO: "hover" (tooltip), "entrar" (vista do local), "scroll" (zoom máximo e mais um passo: desce ao mundo aberto naquele ponto), "sair" (botão direito), "lista" (zonas com setinha).
public static class Snippet
{
    static string ZONA = "church";
    static string ACAO = "entrar";

    public static IEnumerator Run()
    {
        var asm = Chainloader.PluginInfos["arthur.gk2.map"].Instance.GetType().Assembly;
        var zoomType = asm.GetType("GK2Map.MapZoom");
        var viewType = asm.GetType("GK2Map.PlaceView");
        if (ACAO == "sair")
        {
            AccessTools.Property(viewType, "TestRightClick").SetValue(null, true);
            yield return null; yield return null;
            yield return "vista ativa: " + AccessTools.Property(viewType, "Active").GetValue(null);
            yield break;
        }
        var w = LazyUI.GetWindow<CharacterWindow>();
        var page = w.transform.Find("Content/Pages/MapPage");
        if (!w.IsShown || page == null || !page.gameObject.activeInHierarchy)
        {
            if (w.IsShown) { w.Close(); yield return null; yield return null; }
            Transform pages = w.transform.Find("Content/Pages");
            if (pages != null) foreach (Transform p in pages) if (p.GetComponent<LazyWidgetBase>() != null) p.gameObject.SetActive(false);
            w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map));
            for (int i = 0; i < 5; i++) yield return null;
        }
        var zoom = w.GetComponentInChildren(zoomType);
        var places = ((IEnumerable)AccessTools.Field(zoomType, "places").GetValue(zoom)).Cast<object>().ToList();
        string Zone(object p) => (string)AccessTools.Field(p.GetType(), "ZoneId").GetValue(p);
        if (ACAO == "lista")
        {
            yield return string.Join("\n", places.Select(p => Zone(p) + " \"" + AccessTools.Field(p.GetType(), "Title").GetValue(p) + "\" porta=" + AccessTools.Field(p.GetType(), "DoorId").GetValue(p) + " mapa=" + AccessTools.Field(p.GetType(), "MapPos").GetValue(p)));
            yield break;
        }
        object place = places.FirstOrDefault(p => Zone(p) == ZONA);
        if (place == null) { yield return "sem setinha para " + ZONA + " (" + string.Join(",", places.Select(Zone)) + ")"; yield break; }
        var map = (RectTransform)AccessTools.Property(zoomType, "Map").GetValue(zoom);
        var plugin = Chainloader.PluginInfos["arthur.gk2.map"].Instance;
        // A setinha precisa estar na área visível: centraliza o mapa nela.
        var scroll = (UnityEngine.UI.ScrollRect)AccessTools.Property(zoomType, "Scroll").GetValue(zoom);
        Vector2 arrow = (Vector2)AccessTools.Field(place.GetType(), "MapPos").GetValue(place);
        System.Action center = () =>
        {
            float z = (float)AccessTools.Property(zoomType, "Zoom").GetValue(zoom);
            scroll.content.anchoredPosition = (Vector2)AccessTools.Method(zoomType, "Clamp").Invoke(zoom, new object[] { -arrow * z });
        };
        System.Action aim = () => AccessTools.Property(zoomType, "VirtualMouse").SetValue(null, (Vector2?)(Vector2)map.TransformPoint(arrow));
        center(); yield return null; aim(); yield return null; yield return null;
        if (ACAO == "scroll")
        {
            // Até o zoom máximo e mais um passo: desce ao mundo aberto no ponto da setinha.
            for (int i = 0; i < 4 && !(bool)AccessTools.Property(viewType, "Active").GetValue(null); i++)
            {
                AccessTools.Property(zoomType, "TestScroll").SetValue(null, 1);
                float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 0.4f) yield return null;
                if (!(bool)AccessTools.Property(viewType, "Active").GetValue(null)) { center(); yield return null; aim(); yield return null; }
            }
        }
        else if (ACAO == "entrar") AccessTools.Property(viewType, "TestLeftClick").SetValue(null, true);
        float t1 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t1 < 0.8f) yield return null;
        yield return ACAO + " " + ZONA + " vista ativa: " + AccessTools.Property(viewType, "Active").GetValue(null) + " mouse " + AccessTools.Property(zoomType, "VirtualMouse").GetValue(null);
    }
}
