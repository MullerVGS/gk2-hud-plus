using System;
using Unity.Profiling;

namespace GK2Insight.Telemetry
{
    /// <summary>
    /// Contadores do profiler que a build de release do jogo expõe (<see cref="ProfilerRecorder"/>): tempo de CPU da
    /// main thread e da render thread, tempo de GPU, draw calls, SetPass, sombras, triângulos e memória. Dizem onde está
    /// o gargalo do frame; a main thread sozinha não explica (o jogo costuma esperar a render thread e a GPU).
    /// </summary>
    internal sealed class RenderStats : IDisposable
    {
        private struct Stat
        {
            public string Metric, Label, Value;
            public double Scale;
            public bool PerFrame;   // tempo: mediana dos frames do segundo; contagem e memória: último valor
            public ProfilerRecorder Rec;
            public float[] Frames;
            public int N;
        }

        private readonly Stat[] stats;

        public RenderStats()
        {
            stats = new[]
            {
                Time("CPU Main Thread Frame Time", "main"),
                Time("CPU Render Thread Frame Time", "render"),
                Time("GPU Frame Time", "gpu"),
                Time("Gfx.WaitForPresentOnGfxThread", "present"),
                Count(ProfilerCategory.Render, "Draw Calls Count", "render_count", "draws"),
                Count(ProfilerCategory.Render, "SetPass Calls Count", "render_count", "setpass"),
                Count(ProfilerCategory.Render, "Batches Count", "render_count", "batches"),
                Count(ProfilerCategory.Render, "Shadow Casters Count", "render_count", "shadow_casters"),
                Count(ProfilerCategory.Render, "Triangles Count", "render_count", "triangles"),
                Count(ProfilerCategory.Render, "Render Textures Count", "render_count", "render_textures"),
                Count(ProfilerCategory.Render, "Render Textures Bytes", "memory_bytes", "render_textures"),
                Count(ProfilerCategory.Render, "Used Buffers Bytes", "memory_bytes", "gfx_buffers"),
                Count(ProfilerCategory.Memory, "Total Used Memory", "memory_bytes", "unity_used"),
                Count(ProfilerCategory.Memory, "App Committed Memory", "memory_bytes", "committed"),
            };
        }

        private static Stat Time(string name, string label) => new Stat
        {
            Metric = "thread_seconds", Label = "thread", Value = label, Scale = 1e-9, PerFrame = true,
            Rec = ProfilerRecorder.StartNew(ProfilerCategory.Render, name), Frames = new float[256],
        };

        private static Stat Count(ProfilerCategory cat, string name, string metric, string value) => new Stat
        {
            Metric = metric, Label = metric == "memory_bytes" ? "kind" : "what", Value = value, Scale = 1,
            Rec = ProfilerRecorder.StartNew(cat, name),
        };

        /// <summary>Uma vez por frame: guarda os tempos do frame anterior.</summary>
        public void OnFrame()
        {
            for (int i = 0; i < stats.Length; i++)
            {
                if (!stats[i].PerFrame || !stats[i].Rec.Valid) continue;
                if (stats[i].N == stats[i].Frames.Length) Array.Resize(ref stats[i].Frames, stats[i].N * 2);
                stats[i].Frames[stats[i].N++] = stats[i].Rec.LastValue * 1e-9f;
            }
        }

        /// <summary>Uma vez por segundo: mediana dos tempos (p50) e último valor das contagens.</summary>
        public void Sample(MetricBatch batch)
        {
            for (int i = 0; i < stats.Length; i++)
            {
                if (!stats[i].Rec.Valid) continue;
                if (stats[i].PerFrame)
                {
                    int n = stats[i].N;
                    if (n == 0) continue;
                    Array.Sort(stats[i].Frames, 0, n);
                    batch.Add(stats[i].Metric, stats[i].Frames[n / 2], stats[i].Label, stats[i].Value);
                    stats[i].N = 0;
                }
                else
                {
                    long v = stats[i].Rec.LastValue;
                    if (v > 0) batch.Add(stats[i].Metric, v * stats[i].Scale, stats[i].Label, stats[i].Value);
                }
            }
        }

        public void Dispose()
        {
            for (int i = 0; i < stats.Length; i++) stats[i].Rec.Dispose();
        }
    }
}
