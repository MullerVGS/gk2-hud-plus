using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using GK2Shared;
using Newtonsoft.Json;
using UnityEngine;

namespace GK2DevBridge
{
    /// <summary>
    /// Recarrega plugins BepInEx sem reiniciar o jogo e executa cenários (DLL com <c>static class Snippet { static object Run() }</c>).
    /// Um plugin recarregável precisa desfazer tudo em OnDestroy (UnpatchSelf, destruir objetos criados).
    /// </summary>
    internal static class HotLoader
    {
        private static GameObject host;
        private static readonly Dictionary<string, BaseUnityPlugin> Loaded = new Dictionary<string, BaseUnityPlugin>();

        private static GameObject Host
        {
            get
            {
                if (host == null)
                {
                    host = new GameObject("GK2DevBridge_HotPlugins");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                }
                return host;
            }
        }

        public static IEnumerator Reload(Job job)
        {
            Assembly asm = LoadFresh(job.Str("path"));
            List<Type> pluginTypes = PluginReloader.PluginTypes(asm);
            if (pluginTypes.Count == 0)
            {
                job.Fail(400, "nenhum [BepInPlugin] em " + asm.FullName);
                yield break;
            }
            List<string> destroyed = pluginTypes.Select(PluginReloader.GuidOf).Where(g => PluginReloader.DestroyExisting(g, Loaded)).ToList();
            yield return null; // Destroy é adiado: espera o OnDestroy das versões antigas antes de criar as novas.
            long logStart = LogBuffer.LastSeq;
            List<string> started = new List<string>();
            foreach (Type type in pluginTypes)
            {
                BaseUnityPlugin plugin = PluginReloader.Start(type, Host, Loaded);
                started.Add($"{plugin.Info.Metadata.GUID} {plugin.Info.Metadata.Version}");
            }
            yield return null; // deixa Awake/Start rodarem para os logs entrarem na resposta.
            job.Json(new { ok = true, assembly = asm.GetName().Name, destroyed, started, logs = LogBuffer.Since(logStart, 200, null) });
        }

        public static IEnumerator Unload(Job job)
        {
            string guid = job.Str("guid");
            bool done = PluginReloader.DestroyExisting(guid, Loaded);
            yield return null;
            job.Json(new { ok = done, guid });
        }

        public static IEnumerator Run(Job job)
        {
            Assembly asm = LoadFresh(job.Str("path"));
            Type type = asm.GetTypes().FirstOrDefault(t => t.Name == "Snippet");
            MethodInfo run = type?.GetMethod("Run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (run == null)
            {
                job.Fail(400, "esperava static class Snippet com static object Run()");
                yield break;
            }
            long logStart = LogBuffer.LastSeq;
            object result = run.Invoke(null, null);
            if (result is IEnumerator routine)
            {
                // Cenário de vários frames: o último valor não-YieldInstruction vira o resultado.
                object last = null;
                while (true)
                {
                    bool more;
                    try
                    {
                        more = routine.MoveNext();
                    }
                    catch (Exception e)
                    {
                        job.Json(new { ok = false, error = e.ToString(), logs = LogBuffer.Since(logStart, 300, null) });
                        yield break;
                    }
                    if (!more) break;
                    object current = routine.Current;
                    if (current is YieldInstruction || current is CustomYieldInstruction || current is IEnumerator || current == null)
                    {
                        yield return current;
                    }
                    else
                    {
                        last = current;
                        yield return null;
                    }
                }
                result = last;
            }
            job.Json(new { ok = true, result = Describe(result), logs = LogBuffer.Since(logStart, 300, null) });
        }

        private static Assembly LoadFresh(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                throw new FileNotFoundException("DLL não encontrada: " + path);
            }
            return PluginReloader.LoadRenamed(File.ReadAllBytes(path));
        }

        private static object Describe(object value)
        {
            if (value == null || value is string || value.GetType().IsPrimitive) return value;
            if (value is UnityEngine.Object uo) return uo ? $"{uo.GetType().Name}:{uo.name}" : "null";
            try
            {
                return JsonConvert.DeserializeObject(JsonConvert.SerializeObject(value, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MaxDepth = 6, Error = (s, e) => e.ErrorContext.Handled = true }));
            }
            catch
            {
                return value.ToString();
            }
        }
    }
}
