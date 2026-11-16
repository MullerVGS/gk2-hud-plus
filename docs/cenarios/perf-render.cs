using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Profiling;
using UnityEngine;

// Contadores do profiler que a build de release expõe (ProfilerRecorder), mediana de FRAMES quadros: tempo da main
// thread, da render thread e da GPU, draw calls, SetPass, sombras, triângulos. Diz onde está o gargalo do frame.
public static class Snippet
{
    static int FRAMES = 240;

    static readonly (ProfilerCategory cat, string name, string label, double div)[] Stats =
    {
        (ProfilerCategory.Render, "CPU Main Thread Frame Time", "main", 1e6),
        (ProfilerCategory.Render, "CPU Render Thread Frame Time", "render", 1e6),
        (ProfilerCategory.Render, "GPU Frame Time", "gpu", 1e6),
        (ProfilerCategory.Render, "Gfx.WaitForPresentOnGfxThread", "present", 1e6),
        (ProfilerCategory.Render, "Draw Calls Count", "draws", 1),
        (ProfilerCategory.Render, "SetPass Calls Count", "setpass", 1),
        (ProfilerCategory.Render, "Shadow Casters Count", "shadows", 1),
        (ProfilerCategory.Render, "Triangles Count", "tris", 1000),
        (ProfilerCategory.Render, "Render Textures Count", "rts", 1),
        (ProfilerCategory.Render, "Render Textures Bytes", "rtMB", 1048576),
    };

    public static IEnumerator Run()
    {
        var recs = Stats.Select(s => ProfilerRecorder.StartNew(s.cat, s.name)).ToArray();
        var vals = Stats.Select(_ => new List<double>()).ToArray();
        var dt = new List<float>();
        yield return null;
        for (int i = 0; i < FRAMES; i++)
        {
            yield return null;
            dt.Add(Time.unscaledDeltaTime * 1000f);
            for (int k = 0; k < recs.Length; k++) if (recs[k].Valid) vals[k].Add(recs[k].LastValue / Stats[k].div);
        }
        foreach (var r in recs) r.Dispose();
        double Med(List<double> l) { if (l.Count == 0) return -1; l.Sort(); return l[l.Count / 2]; }
        dt.Sort();
        string s = $"frame {dt[dt.Count / 2]:0.0} ms";
        for (int k = 0; k < Stats.Length; k++) s += $" · {Stats[k].label} {Med(vals[k]):0.#}";
        yield return s;
    }
}
