using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Map: com a vista do local aberta (gk2map-local.cs), põe o mouse virtual no objeto cujo id contém ALVO e, com CLICAR, fixa os detalhes; ALVO "lista" devolve os objetos inspecionáveis.
public static class Snippet
{
    static string ALVO = "lista";
    const bool CLICAR = true;

    public static IEnumerator Run()
    {
        var asm = Chainloader.PluginInfos["arthur.gk2.map"].Instance.GetType().Assembly;
        var zoomType = asm.GetType("GK2Map.MapZoom");
        var viewType = asm.GetType("GK2Map.PlaceView");
        var inspect = asm.GetType("GK2Map.PlaceInspect");
        if (!(bool)AccessTools.Property(viewType, "Active").GetValue(null)) { yield return "vista fechada (rode gk2map-local.cs)"; yield break; }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.7f) yield return null;
        var cands = ((IEnumerable)AccessTools.Field(inspect, "Candidates").GetValue(null)).Cast<object>().ToList();
        string Id(object c) => ((WgoData)AccessTools.Field(c.GetType(), "Data").GetValue(c)).id;
        Rect R(object c) => (Rect)AccessTools.Field(c.GetType(), "Rect").GetValue(c);
        if (ALVO == "lista")
        {
            yield return string.Join("\n", cands.Select(c => Id(c) + " \"" + AccessTools.Field(c.GetType(), "Name").GetValue(c) + "\" " + R(c)));
            yield break;
        }
        object target = cands.FirstOrDefault(c => Id(c).Contains(ALVO));
        if (target == null) { yield return "sem objeto " + ALVO; yield break; }
        AccessTools.Method(inspect, "TryRect").Invoke(null, new[] { target });
        AccessTools.Property(zoomType, "VirtualMouse").SetValue(null, (Vector2?)R(target).center);
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
        if (CLICAR) AccessTools.Property(viewType, "TestLeftClick").SetValue(null, true);
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.4f) yield return null;
        yield return AccessTools.Property(inspect, "Describe").GetValue(null) + " mouse " + R(target).center;
    }
}
