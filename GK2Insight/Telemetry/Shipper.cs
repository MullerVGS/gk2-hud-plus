using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading;

namespace GK2Insight.Telemetry
{
    /// <summary>
    /// Envio fora da thread do jogo: a thread principal só enfileira strings. A cada 10 s um POST gzip para o
    /// VictoriaMetrics (métricas) e outro para o VictoriaLogs (eventos), pelo vmauth. Falha = volta para a fila
    /// (com teto) e tenta de novo. Uma instância por carga do plugin; Dispose encerra a thread.
    /// </summary>
    internal sealed class Shipper : IDisposable
    {
        private const int MaxQueuedMetricChunks = 720; // ~12 min de lotes de 1 s parados
        private const int MaxQueuedEvents = 5000;
        private const int IntervalMs = 10000;

        private readonly ConcurrentQueue<string> metrics = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> events = new ConcurrentQueue<string>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly string baseUrl, token;
        private readonly Thread thread;
        private volatile bool stop;

        public long SentMetricBytes, SentEvents, Failures, Dropped;
        public volatile string LastError = "";
        public volatile float LastPostMs;

        public Shipper(string baseUrl, string token)
        {
            this.baseUrl = baseUrl.TrimEnd('/');
            this.token = token;
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            thread = new Thread(Loop) { IsBackground = true, Name = "GK2Insight.Shipper", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        public int QueuedMetrics => metrics.Count;

        public void EnqueueMetrics(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (metrics.Count >= MaxQueuedMetricChunks && metrics.TryDequeue(out _)) Interlocked.Increment(ref Dropped);
            metrics.Enqueue(text);
        }

        public void EnqueueEvent(string jsonLine)
        {
            if (events.Count >= MaxQueuedEvents && events.TryDequeue(out _)) Interlocked.Increment(ref Dropped);
            events.Enqueue(jsonLine);
        }

        /// <summary>Não espera o último envio: a thread é de fundo e termina sozinha depois do flush.</summary>
        public void Dispose()
        {
            stop = true;
            wake.Set();
        }

        private void Loop()
        {
            while (!stop)
            {
                wake.WaitOne(IntervalMs);
                ShipAll();
            }
            ShipAll();
        }

        private void ShipAll()
        {
            try
            {
                ShipEvents();
                ShipMetrics();
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
            }
        }

        private void ShipMetrics()
        {
            StringBuilder sb = new StringBuilder(32 * 1024);
            int taken = 0;
            while (taken < 120 && metrics.TryDequeue(out string chunk))
            {
                sb.Append(chunk);
                taken++;
            }
            if (taken == 0) return;
            string body = sb.ToString();
            if (!Post(baseUrl + "/api/v1/import/prometheus", body))
            {
                // Cada linha tem o próprio timestamp: a ordem não importa.
                if (!stop) EnqueueMetrics(body);
                return;
            }
            Interlocked.Add(ref SentMetricBytes, body.Length);
        }

        private void ShipEvents()
        {
            StringBuilder sb = new StringBuilder(4 * 1024);
            int n = 0;
            while (n < 2000 && events.TryDequeue(out string line))
            {
                sb.Append(line).Append('\n');
                n++;
            }
            if (n == 0) return;
            string body = sb.ToString();
            string url = baseUrl + "/insert/jsonline?_stream_fields=app,instance,kind&_time_field=_time&_msg_field=_msg";
            if (!Post(url, body))
            {
                if (!stop)
                    foreach (string line in body.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        EnqueueEvent(line);
                return;
            }
            Interlocked.Add(ref SentEvents, n);
        }

        private bool Post(string url, string body)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                byte[] raw = Encoding.UTF8.GetBytes(body);
                byte[] gz;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (GZipStream z = new GZipStream(ms, CompressionMode.Compress, true)) z.Write(raw, 0, raw.Length);
                    gz = ms.ToArray();
                }
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                req.ContentType = "text/plain";
                req.Headers["Authorization"] = "Bearer " + token;
                req.Headers["Content-Encoding"] = "gzip";
                req.ContentLength = gz.Length;
                using (Stream s = req.GetRequestStream()) s.Write(gz, 0, gz.Length);
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode / 100 == 2)
                    {
                        LastPostMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                        return true;
                    }
                    LastError = "HTTP " + (int)resp.StatusCode;
                }
            }
            catch (WebException e) when (e.Response is HttpWebResponse r)
            {
                LastError = "HTTP " + (int)r.StatusCode;
                r.Dispose();
            }
            catch (Exception e)
            {
                LastError = e.GetType().Name + ": " + e.Message;
            }
            Interlocked.Increment(ref Failures);
            return false;
        }
    }
}
