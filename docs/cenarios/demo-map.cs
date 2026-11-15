using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Demos do mapa (clipes "Map" e "MapPlaces"). MODO:
//   zoom  — abre o mapa; o cursor aproxima com o scroll até o máximo e mais um passo desce ao mundo aberto; botão direito volta
//   local — passa numa setinha amarela (tooltip do local), clica, a vista do interior abre; passeia e volta com o botão direito
// ZONA = zona da setinha (padrão: a igreja). FIM = "meio" para no meio.
public static class Snippet
{
    static string MODO = "zoom";
    static string ZONA = "church";
    static string FIM = "";

    static void PlaceView_Click(System.Type viewType) => AccessTools.Property(viewType, "TestLeftClick").SetValue(null, true);

    public static IEnumerator Run()
    {
        object module = Demo.Module("Map");
        var asm = module.GetType().Assembly;
        var zoomType = asm.GetType("GK2Map.MapZoom");
        var viewType = asm.GetType("GK2Map.PlaceView");
        var w = LazyUI.GetWindow<CharacterWindow>();
        if (w.IsShown) { w.Close(); yield return null; yield return null; }
        w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map));
        yield return Demo.Wait(1.0f);
        var zoom = w.GetComponentInChildren(zoomType);
        if (zoom == null) { yield return "mapa sem zoom (funcionalidade desligada?)"; yield break; }
        var map = (RectTransform)AccessTools.Property(zoomType, "Map").GetValue(zoom);
        var scroll = (UnityEngine.UI.ScrollRect)AccessTools.Property(zoomType, "Scroll").GetValue(zoom);
        System.Action<int> wheel = d => AccessTools.Property(zoomType, "TestScroll").SetValue(null, d);
        Vector2 top(Vector3 bottomLeft) => new Vector2(bottomLeft.x, Screen.height - bottomLeft.y);

        if (MODO == "zoom")
        {
            // Aproxima em dois passos, continua até o zoom máximo e mais um: desce ao mundo aberto naquele ponto; volta
            // com o botão direito.
            Vector2 a = new Vector2(900, 560);
            Demo.Mouse(a + new Vector2(-220, 130));
            yield return Demo.Wait(0.5f);
            yield return Demo.Glide(a, 0.6f);
            for (int i = 0; i < 6 && !(bool)AccessTools.Property(viewType, "Active").GetValue(null); i++)
            {
                wheel(1);
                yield return Demo.Wait(0.8f);
            }
            yield return Demo.Wait(1.8f);
            if (FIM == "meio") { yield return "no mundo: " + AccessTools.Property(viewType, "Active").GetValue(null); yield break; }
            AccessTools.Property(viewType, "TestRightClick").SetValue(null, true);
            yield return Demo.Wait(1.2f);
            Demo.Mouse(null);
            yield return "demo zoom ok";
            yield break;
        }

        var places = ((IEnumerable)AccessTools.Field(zoomType, "places").GetValue(zoom)).Cast<object>().ToList();
        object place = places.FirstOrDefault(p => (string)AccessTools.Field(p.GetType(), "ZoneId").GetValue(p) == ZONA);
        if (place == null) { yield return "sem setinha para " + ZONA; yield break; }
        Vector2 arrow = (Vector2)AccessTools.Field(place.GetType(), "MapPos").GetValue(place);
        float z0 = (float)AccessTools.Property(zoomType, "Zoom").GetValue(zoom);
        scroll.content.anchoredPosition = (Vector2)AccessTools.Method(zoomType, "Clamp").Invoke(zoom, new object[] { -arrow * z0 });
        yield return null;
        Vector2 at = top(map.TransformPoint(arrow));
        Demo.Mouse(at + new Vector2(-240, 140));
        yield return Demo.Wait(0.5f);
        yield return Demo.Glide(at, 0.8f);
        yield return Demo.Wait(1.0f);   // tooltip com o nome do local
        PlaceView_Click(viewType);
        yield return Demo.Wait(1.6f);
        if (FIM == "meio") { yield return "dentro: " + AccessTools.Property(viewType, "Active").GetValue(null); yield break; }
        yield return Demo.Glide(new Vector2(1060, 560), 0.9f);   // passeia pelo interior: hover nos objetos
        yield return Demo.Wait(1.4f);
        AccessTools.Property(viewType, "TestRightClick").SetValue(null, true);
        yield return Demo.Wait(1.0f);
        Demo.Mouse(null);
        yield return "demo local ok";
    }
}
