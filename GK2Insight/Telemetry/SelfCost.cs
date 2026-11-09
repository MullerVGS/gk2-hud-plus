using System.Diagnostics;

namespace GK2Insight.Telemetry
{
    /// <summary>Quanto o próprio Insight gasta da thread principal, por parte, na janela de 1 s.</summary>
    internal sealed class SelfCost
    {
        public enum Part { Update, OnGui, Local, Tech, Overlay, Telemetry, Count }

        public static readonly string[] Names = { "update", "ongui", "panel_local", "panel_tech", "overlay_scan", "telemetry" };

        private readonly long[] ticks = new long[(int)Part.Count];
        private readonly long[] max = new long[(int)Part.Count];
        private readonly int[] calls = new int[(int)Part.Count];

        public static long Now() => Stopwatch.GetTimestamp();

        public void Stop(Part p, long start)
        {
            long d = Stopwatch.GetTimestamp() - start;
            int i = (int)p;
            ticks[i] += d;
            calls[i]++;
            if (d > max[i]) max[i] = d;
        }

        public double Seconds(int i) => ticks[i] / (double)Stopwatch.Frequency;
        public double MaxSeconds(int i) => max[i] / (double)Stopwatch.Frequency;
        public int Calls(int i) => calls[i];

        public void Reset()
        {
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] = 0;
                max[i] = 0;
                calls[i] = 0;
            }
        }
    }
}
