using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;

namespace GK2Insight.Telemetry
{
    /// <summary>
    /// Custo do Update dos outros mods (GK2 HUD+ e avulsos com <c>ModHost</c>), por funcionalidade. Os mods só medem
    /// (<c>ModHost.TakeCost</c>); a leitura é por reflexão, porque cada mod compila a própria cópia do ModHost.
    /// </summary>
    internal sealed class ModCost
    {
        private sealed class Source
        {
            public string Mod;
            public BaseUnityPlugin Plugin;
            public object Host;
            public MethodInfo Take;
        }

        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private readonly List<Source> sources = new List<Source>();
        private readonly Action<string, double, double, int> sink;
        private readonly object[] args = new object[1];
        private MetricBatch batch;
        private string mod;
        private float nextScan;

        public ModCost()
        {
            sink = Collect;
            args[0] = sink;
        }

        public void Sample(MetricBatch b, float now)
        {
            if (now >= nextScan || sources.Exists(s => s.Plugin == null))
            {
                nextScan = now + 10f;
                Scan();
            }
            batch = b;
            foreach (Source s in sources)
            {
                mod = s.Mod;
                try { s.Take.Invoke(s.Host, args); }
                catch (Exception e) { Plugin.WarnOnce("custo de " + s.Mod, e); }
            }
            batch = null;
        }

        private void Collect(string part, double seconds, double max, int calls)
        {
            batch.Add("mod_seconds", seconds, "mod", mod, "part", part);
            if (max > 0) batch.Add("mod_max_seconds", max, "mod", mod, "part", part);
        }

        /// <summary>Plugins com propriedade Host que tenha TakeCost(Action&lt;string, double, double, int&gt;).</summary>
        private void Scan()
        {
            sources.Clear();
            foreach (PluginInfo info in Chainloader.PluginInfos.Values)
            {
                BaseUnityPlugin p = info.Instance;
                if (p == null || p.GetType().Assembly == typeof(ModCost).Assembly) continue;
                object host = p.GetType().GetProperty("Host", All)?.GetValue(p);
                MethodInfo take = host?.GetType().GetMethod("TakeCost", All, null, new[] { typeof(Action<string, double, double, int>) }, null);
                if (take == null) continue;
                string guid = info.Metadata.GUID;
                sources.Add(new Source { Mod = guid.Substring(guid.LastIndexOf('.') + 1), Plugin = p, Host = host, Take = take });
            }
        }
    }
}
