using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

// Amostra de desempenho: tempo de frame (FRAMES quadros) e contagem de objetos da Unity por tipo e por raiz, para achar
// acúmulo ao longo da sessão (rode de tempos em tempos e compare). Só leitura.
public static class Snippet
{
    static int FRAMES = 300;
    static int TOP = 12;

    public static IEnumerator Run()
    {
        var dts = new List<float>(FRAMES);
        for (int i = 0; i < FRAMES; i++)
        {
            yield return null;
            dts.Add(Time.unscaledDeltaTime * 1000f);
        }
        dts.Sort();
        var sb = new StringBuilder();
        sb.AppendLine($"frame p50 {dts[dts.Count / 2]:0.00} ms · p90 {dts[(int)(dts.Count * 0.9f)]:0.00} · avg {dts.Average():0.00} · max {dts[dts.Count - 1]:0.0}");

        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int active = all.Count(t => t.gameObject.activeInHierarchy);
        sb.AppendLine($"transforms {all.Length} (ativos {active}) · canvas {Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}"
            + $" · graphic {Object.FindObjectsByType<UnityEngine.UI.Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}"
            + $" · tmp {Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}"
            + $" · behaviours {Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length}");
        sb.AppendLine($"assets: sprite {Resources.FindObjectsOfTypeAll<Sprite>().Length} · tex2d {Resources.FindObjectsOfTypeAll<Texture2D>().Length}"
            + $" · rt {Resources.FindObjectsOfTypeAll<RenderTexture>().Length} · material {Resources.FindObjectsOfTypeAll<Material>().Length}"
            + $" · mesh {Resources.FindObjectsOfTypeAll<Mesh>().Length}");
        int mine = all.Count(t => t.name.StartsWith("GK2") || t.name.StartsWith("HudPlus"));
        sb.AppendLine($"objetos com nome GK2*: {mine}");

        // Raiz + primeiro filho: onde os objetos se acumulam.
        var byRoot = new Dictionary<string, int>();
        foreach (Transform t in all)
        {
            Transform r = t;
            Transform child = null;
            while (r.parent != null) { child = r; r = r.parent; }
            string key = r.name + (child != null && r.name == "UI" ? "/" + child.name : "");
            byRoot.TryGetValue(key, out int n);
            byRoot[key] = n + 1;
        }
        foreach (var kv in byRoot.OrderByDescending(kv => kv.Value).Take(TOP)) sb.AppendLine($"  {kv.Value,6} {kv.Key}");
        sb.AppendLine($"mono {System.GC.GetTotalMemory(false) / 1048576} MB · gc {System.GC.CollectionCount(0)}");
        yield return sb.ToString();
    }
}
