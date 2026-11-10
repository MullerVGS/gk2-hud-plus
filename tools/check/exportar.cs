using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
using FlowCanvas;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;

// Índice de gatilhos (só leitura, green): exporta o GameBalance e os textos (Dumper do GK2 Insight) e todos os grafos de
// script/diálogo do jogo (Addressables em VisualScripts/, JSON do FlowCanvas) para BepInEx/GK2Insight-dump/.
// Usado por `gk2 gatilhos`, que monta decomp/gatilhos.json a partir daqui.
public static class Snippet
{
    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var insight = Chainloader.PluginInfos.TryGetValue("arthur.gk2.insight", out var info) ? info.Instance : null;
        if (insight == null) { yield return "ERRO: GK2 Insight não carregado no green (precisa do Dumper)"; yield break; }
        AccessTools.Method(insight.GetType().Assembly.GetType("GK2Insight.Dumper"), "DumpAll").Invoke(null, null);
        string dir = Path.Combine(Paths.BepInExRootPath, "GK2Insight-dump");

        var keys = new SortedSet<string>();
        foreach (var loc in Addressables.ResourceLocators)
            foreach (object k in loc.Keys)
                if (k is string s && s.StartsWith("Assets/AddressableAssets/VisualScripts/") && s.EndsWith(".asset")) keys.Add(s);
        var field = AccessTools.Field(typeof(NodeCanvas.Framework.Graph), "_serializedGraph");
        int ok = 0, fail = 0;
        using (var w = new StreamWriter(Path.Combine(dir, "graphs.jsonl"), false, new System.Text.UTF8Encoding(false)))
        {
            foreach (string key in keys)
            {
                var h = Addressables.LoadAssetAsync<FlowGraph>(key);
                while (!h.IsDone) yield return null;
                string json = h.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded && h.Result != null ? field.GetValue(h.Result) as string : null;
                if (json != null) { w.Write("{\"path\":"); w.Write(Newtonsoft.Json.JsonConvert.ToString(key)); w.Write(",\"graph\":"); w.Write(json); w.WriteLine("}"); ok++; }
                else fail++;
                Addressables.Release(h);
            }
        }
        File.WriteAllText(Path.Combine(dir, "build.txt"), Application.version + "\n" + Application.unityVersion + "\n");
        yield return "exportado: " + ok + " grafos (" + fail + " falharam), balanço e textos em " + dir;
    }
}
