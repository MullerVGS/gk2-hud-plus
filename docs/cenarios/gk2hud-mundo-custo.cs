using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): mede o custo da escolha de alvo do tooltip de mundo. (A) real: N× reconstruir candidatos + escolher sob o mouse; (B) estresse: trata TODO Wgo carregado como candidato (como uma floresta de árvores rebrotando) e mede uma reconstrução + uma escolha.
public static class Snippet
{
    const int N = 20;

    static object World() { var p = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance); return AccessTools.Property(p.GetType(), "WorldHover").GetValue(p); }

    public static object Run()
    {
        var sb = new StringBuilder();
        object world = World();
        object targets = AccessTools.Field(world.GetType(), "Targets").GetValue(world);
        var tt = targets.GetType();
        var inval = AccessTools.Method(tt, "Invalidate");
        var pick = AccessTools.Method(tt, "Pick");
        var mouse = new Vector2(Screen.width / 2f, Screen.height / 2f);
        pick.Invoke(targets, new object[] { mouse });
        long gc0 = System.GC.CollectionCount(0);
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) { inval.Invoke(targets, null); pick.Invoke(targets, new object[] { mouse }); }
        sw.Stop();
        sb.AppendLine($"(A) real: {AccessTools.Property(tt, "Count").GetValue(targets)} candidatos · rebuild+pick {sw.Elapsed.TotalMilliseconds / N:0.000} ms · pick só: {PickOnly(targets, pick, mouse):0.000} ms");

        var stress = AccessTools.Method(tt, "StressMeasure");
        var wgos = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).Where(w => w.HasData && w.isActiveAndEnabled).ToList();
        if (stress != null)
        {
            var r1 = (string)stress.Invoke(targets, new object[] { wgos, mouse });
            var r2 = (string)stress.Invoke(targets, new object[] { wgos, mouse });
            sb.AppendLine("(B) estresse 1ª vez: " + r1 + "\n(B) estresse 2ª vez (cache): " + r2);
        }
        else
        {
            // Código antigo (0.2.0/0.2.1): renderers + projeção + bolha por candidato, sem cache.
            var rendOf = AccessTools.Method(tt, "RenderersOf");
            var proj = AccessTools.Method(tt, "ProjectRenderers");
            var bub = AccessTools.Method(tt, "NativeBubbleRect");
            var swb = Stopwatch.StartNew();
            var arrs = new List<object>();
            foreach (var w in wgos) arrs.Add(rendOf.Invoke(null, new object[] { w.gameObject }));
            double tRebuild = swb.Elapsed.TotalMilliseconds;
            swb.Restart();
            for (int i = 0; i < wgos.Count; i++)
            {
                proj.Invoke(null, new object[] { arrs[i], null });
                bub.Invoke(null, new object[] { wgos[i].Data, null });
            }
            sb.AppendLine($"(B) estresse antigo: {wgos.Count} Wgo · reconstrução {tRebuild:0.00} ms · escolha {swb.Elapsed.TotalMilliseconds:0.00} ms");
        }
        sb.AppendLine("GC gen0 durante (A): " + (System.GC.CollectionCount(0) - gc0));
        return sb.ToString();
    }

    static double PickOnly(object targets, System.Reflection.MethodInfo pick, Vector2 mouse)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < N; i++) pick.Invoke(targets, new object[] { mouse });
        return sw.Elapsed.TotalMilliseconds / N;
    }
}
