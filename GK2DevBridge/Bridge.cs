using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;

namespace GK2DevBridge
{
    /// <summary>
    /// Ponte HTTP de desenvolvimento, instalada só na instância green. Escuta em 127.0.0.1 e executa cada pedido
    /// na thread principal do Unity.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Bridge : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.devbridge";
        public const string Name = "GK2 DevBridge";
        public const string Version = "0.2.0";

        internal static Bridge Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private readonly ConcurrentQueue<Job> jobs = new ConcurrentQueue<Job>();
        private readonly Dictionary<string, Func<Job, IEnumerator>> routes = new Dictionary<string, Func<Job, IEnumerator>>();
        private HttpListener listener;
        private Thread thread;
        private volatile bool running;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigEntry<int> port = Config.Bind("Server", "Port", 27777, "Porta HTTP em 127.0.0.1.");
            LogBuffer.Install();
            Application.runInBackground = true;

            routes["/ping"] = Routes.Ping;
            routes["/state"] = Routes.State;
            routes["/log"] = Routes.LogTail;
            routes["/shot"] = Routes.Shot;
            routes["/ui"] = Routes.Ui;
            routes["/windows"] = Routes.Windows;
            routes["/reload"] = HotLoader.Reload;
            routes["/unload"] = HotLoader.Unload;
            routes["/run"] = HotLoader.Run;
            routes["/quit"] = Routes.Quit;
            routes["/rec/start"] = Recorder.Start;
            routes["/rec/stop"] = Recorder.Stop;
            routes["/rec/cursor"] = Recorder.CursorAt;

            running = true;
            listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port.Value}/");
            listener.Start();
            thread = new Thread(Serve) { IsBackground = true, Name = "GK2DevBridge" };
            thread.Start();
            Log.LogInfo($"{Name} {Version} ouvindo em 127.0.0.1:{port.Value}");
        }

        private void OnDestroy()
        {
            running = false;
            try { listener?.Stop(); } catch { }
        }

        private void Update()
        {
            while (jobs.TryDequeue(out Job job))
            {
                if (routes.TryGetValue(job.Path, out Func<Job, IEnumerator> route))
                {
                    StartCoroutine(Guarded(job, route));
                }
                else
                {
                    job.Fail(404, "rota desconhecida: " + job.Path + ". Rotas: " + string.Join(" ", routes.Keys));
                }
            }
        }

        private static IEnumerator Guarded(Job job, Func<Job, IEnumerator> route)
        {
            IEnumerator inner;
            try
            {
                inner = route(job);
            }
            catch (Exception e)
            {
                job.Fail(500, e.ToString());
                yield break;
            }
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext())
                    {
                        break;
                    }
                    current = inner.Current;
                }
                catch (Exception e)
                {
                    job.Fail(500, e.ToString());
                    yield break;
                }
                yield return current;
            }
            if (!job.IsDone)
            {
                job.Json(new { ok = true });
            }
        }

        private void Serve()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = listener.GetContext();
                }
                catch
                {
                    if (!running) return;
                    continue;
                }
                Job job = new Job(ctx.Request.Url.AbsolutePath.TrimEnd('/'), ctx.Request.QueryString);
                if (job.Path.Length == 0) job.Path = "/ping";
                jobs.Enqueue(job);
                int timeout = job.Int("timeout", 60) * 1000;
                if (!job.Done.Wait(timeout))
                {
                    job.Fail(504, "tempo esgotado esperando a thread principal (o jogo está carregando ou travado?)");
                }
                try
                {
                    ctx.Response.StatusCode = job.Status;
                    ctx.Response.ContentType = job.ContentType;
                    ctx.Response.ContentLength64 = job.Body.Length;
                    ctx.Response.OutputStream.Write(job.Body, 0, job.Body.Length);
                    ctx.Response.OutputStream.Close();
                }
                catch (Exception e)
                {
                    Log.LogWarning("resposta falhou: " + e.Message);
                }
            }
        }
    }

    internal class Job
    {
        public string Path;
        public readonly NameValueCollection Query;
        public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };

        public Job(string path, NameValueCollection query)
        {
            Path = path;
            Query = query;
        }

        public bool IsDone => Done.IsSet;

        public string Str(string key, string fallback = null) => Query[key] ?? fallback;

        public int Int(string key, int fallback) => int.TryParse(Query[key], out int v) ? v : fallback;

        public bool Bool(string key) => Query[key] == "1" || Query[key] == "true";

        public void Json(object value)
        {
            Finish(200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(value, JsonSettings)));
        }

        public void Bytes(string contentType, byte[] body) => Finish(200, contentType, body);

        public void Fail(int status, string message)
        {
            Finish(status, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new { ok = false, error = message }, JsonSettings)));
        }

        private void Finish(int status, string contentType, byte[] body)
        {
            if (Done.IsSet) return;
            Status = status;
            ContentType = contentType;
            Body = body;
            Done.Set();
        }
    }
}
