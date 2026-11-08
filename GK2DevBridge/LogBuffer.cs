using System;
using System.Collections.Generic;
using BepInEx.Logging;

namespace GK2DevBridge
{
    /// <summary>Guarda as últimas linhas do log do BepInEx (que inclui o log do Unity) com número de sequência.</summary>
    internal class LogBuffer : ILogListener
    {
        private const int Capacity = 4000;

        public struct Entry
        {
            public long seq;
            public string time;
            public string level;
            public string source;
            public string message;
        }

        private static readonly object Gate = new object();
        private static readonly Queue<Entry> Entries = new Queue<Entry>();
        private static long nextSeq = 1;

        public static long LastSeq
        {
            get { lock (Gate) return nextSeq - 1; }
        }

        public static void Install()
        {
            Logger.Listeners.Add(new LogBuffer());
        }

        public void LogEvent(object sender, LogEventArgs e)
        {
            lock (Gate)
            {
                Entries.Enqueue(new Entry
                {
                    seq = nextSeq++,
                    time = DateTime.Now.ToString("HH:mm:ss.fff"),
                    level = e.Level.ToString(),
                    source = e.Source?.SourceName,
                    message = e.Data?.ToString()
                });
                while (Entries.Count > Capacity)
                {
                    Entries.Dequeue();
                }
            }
        }

        public static List<Entry> Since(long afterSeq, int max, string grep)
        {
            List<Entry> result = new List<Entry>();
            lock (Gate)
            {
                foreach (Entry entry in Entries)
                {
                    if (entry.seq <= afterSeq) continue;
                    if (!string.IsNullOrEmpty(grep) && (entry.message == null || entry.message.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0) && (entry.source == null || entry.source.IndexOf(grep, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    result.Add(entry);
                }
            }
            if (result.Count > max)
            {
                result.RemoveRange(0, result.Count - max);
            }
            return result;
        }

        public void Dispose()
        {
        }
    }
}
