using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using LazyBearTechnology;
using Newtonsoft.Json;
using UnityEngine;

namespace GK2Insight
{
    internal static class Dumper
    {
        private const int MaxDepth = 14;

        public static string LastStatus = "";
        public static float LastStatusTime = -999f;

        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        public static string OutputDir => Path.Combine(Paths.BepInExRootPath, "GK2Insight-dump");

        public static void DumpAll()
        {
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(OutputDir);
                GameBalance balance = GameBalance.Me;
                int files = 0;
                foreach (FieldInfo field in typeof(GameBalance).GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    if (field.IsNotSerialized || !typeof(IList).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }
                    WriteFile(field.Name + ".json", w => WriteValue(w, field.GetValue(balance), 0, new HashSet<object>(ReferenceEqualityComparer.Instance), withNames: true));
                    files++;
                }
                foreach (string name in new[] { "yellow", "green", "red", "orange", "blue", "inspirationLevels" })
                {
                    FieldInfo field = typeof(GameBalance).GetField(name);
                    if (field != null)
                    {
                        WriteFile("talent_" + name + ".json", w => WriteValue(w, field.GetValue(balance), 0, new HashSet<object>(ReferenceEqualityComparer.Instance), withNames: true));
                        files++;
                    }
                }
                WriteFile("caches.json", w => WriteCaches(w, balance));
                files++;
                if (WriteLanguage())
                {
                    files++;
                }
                if (MainGame.PlayerData != null)
                {
                    WriteFile("player.json", w => WriteValue(w, MainGame.PlayerData, 0, new HashSet<object>(ReferenceEqualityComparer.Instance), withNames: false));
                    files++;
                }
                SetStatus($"Dump: {files} arquivos em {watch.ElapsedMilliseconds} ms → {OutputDir}");
            }
            catch (Exception e)
            {
                SetStatus("Dump falhou: " + e.Message);
                Plugin.Log.LogError(e);
            }
        }

        private static void SetStatus(string status)
        {
            LastStatus = status;
            LastStatusTime = Time.unscaledTime;
            Plugin.Log.LogInfo(status);
        }

        private static void WriteFile(string name, Action<JsonTextWriter> body)
        {
            string path = Path.Combine(OutputDir, name);
            using (StreamWriter stream = new StreamWriter(path, false, new System.Text.UTF8Encoding(false)))
            using (JsonTextWriter writer = new JsonTextWriter(stream) { Formatting = Formatting.Indented })
            {
                try
                {
                    body(writer);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Dump {name}: {e.Message}");
                    writer.WriteComment("dump interrompido: " + e.Message);
                }
            }
        }

        private static void WriteCaches(JsonTextWriter w, GameBalance balance)
        {
            w.WriteStartObject();
            foreach (string name in new[] { "craftInItemsCache", "craftInItemsCacheShownInTooltips", "workbenchExtensionsByParentCache", "wgoIdsByGroup", "questBrothersCache", "customBuildAreaIdToWgoIds" })
            {
                FieldInfo field = typeof(GameBalance).GetField(name);
                if (field == null)
                {
                    continue;
                }
                w.WritePropertyName(name);
                WriteValue(w, field.GetValue(balance), 0, new HashSet<object>(ReferenceEqualityComparer.Instance), withNames: false);
            }
            w.WriteEndObject();
        }

        private static bool WriteLanguage()
        {
            FieldInfo current = AccessTools.Field(typeof(LLBase), "currentLang");
            object lang = current?.GetValue(null);
            if (lang == null)
            {
                return false;
            }
            string id = AccessTools.Field(lang.GetType(), "id")?.GetValue(lang) as string ?? "lang";
            if (!(AccessTools.Field(lang.GetType(), "dictionary")?.GetValue(lang) is Dictionary<string, string> dict))
            {
                return false;
            }
            WriteFile($"lang_{id}.json", w =>
            {
                w.WriteStartObject();
                List<string> keys = new List<string>(dict.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                {
                    w.WritePropertyName(key);
                    w.WriteValue(dict[key]);
                }
                w.WriteEndObject();
            });
            return true;
        }

        private static void WriteValue(JsonTextWriter w, object value, int depth, HashSet<object> stack, bool withNames)
        {
            if (value == null)
            {
                w.WriteNull();
                return;
            }
            Type type = value.GetType();
            if (value is string s)
            {
                w.WriteValue(s);
                return;
            }
            if (type.IsEnum)
            {
                w.WriteValue(value.ToString());
                return;
            }
            if (type.IsPrimitive || value is decimal)
            {
                if (value is float f && (float.IsNaN(f) || float.IsInfinity(f)))
                {
                    w.WriteValue(f.ToString());
                    return;
                }
                w.WriteValue(value);
                return;
            }
            if (value is UnityEngine.Object uo)
            {
                if (depth == 0 || !(uo is ScriptableObject))
                {
                    w.WriteValue(uo ? $"{type.Name}:{uo.name}" : $"{type.Name}:null");
                    return;
                }
            }
            if (value is Delegate || value is IntPtr || value is Type || value is MemberInfo)
            {
                w.WriteNull();
                return;
            }
            if (depth >= MaxDepth)
            {
                w.WriteValue("…");
                return;
            }
            bool isRef = !type.IsValueType;
            if (isRef && !stack.Add(value))
            {
                w.WriteValue("↺" + type.Name);
                return;
            }
            try
            {
                if (value is IDictionary dict)
                {
                    w.WriteStartObject();
                    foreach (DictionaryEntry entry in dict)
                    {
                        w.WritePropertyName(KeyName(entry.Key));
                        WriteValue(w, entry.Value, depth + 1, stack, withNames);
                    }
                    w.WriteEndObject();
                    return;
                }
                if (value is IEnumerable list)
                {
                    w.WriteStartArray();
                    foreach (object item in list)
                    {
                        WriteValue(w, item, depth + 1, stack, withNames);
                    }
                    w.WriteEndArray();
                    return;
                }
                w.WriteStartObject();
                if (withNames && value is BalanceBaseObject bo && !string.IsNullOrEmpty(bo.id))
                {
                    WriteLocalized(w, bo.id);
                }
                foreach (FieldInfo field in SerializedFields(type))
                {
                    object fieldValue;
                    try
                    {
                        fieldValue = field.GetValue(value);
                    }
                    catch (Exception e)
                    {
                        fieldValue = "erro: " + e.Message;
                    }
                    if (IsEmpty(fieldValue))
                    {
                        continue;
                    }
                    w.WritePropertyName(field.Name);
                    WriteValue(w, fieldValue, depth + 1, stack, withNames);
                }
                w.WriteEndObject();
            }
            finally
            {
                if (isRef)
                {
                    stack.Remove(value);
                }
            }
        }

        private static void WriteLocalized(JsonTextWriter w, string id)
        {
            string name = LLBase.L(id);
            if (name != id)
            {
                w.WritePropertyName("_name");
                w.WriteValue(name);
            }
            string descKey = id + "_d";
            string desc = LLBase.L(descKey);
            if (desc != descKey)
            {
                w.WritePropertyName("_desc");
                w.WriteValue(desc);
            }
        }

        private static bool IsEmpty(object value)
        {
            if (value == null)
            {
                return true;
            }
            if (value is string s)
            {
                return s.Length == 0;
            }
            if (value is ICollection c)
            {
                return c.Count == 0;
            }
            return false;
        }

        private static string KeyName(object key)
        {
            if (key is UnityEngine.Object uo)
            {
                return uo ? uo.name : "null";
            }
            if (key is BalanceBaseObject bo)
            {
                return bo.id ?? key.ToString();
            }
            return key?.ToString() ?? "null";
        }

        private static FieldInfo[] SerializedFields(Type type)
        {
            if (FieldCache.TryGetValue(type, out FieldInfo[] cached))
            {
                return cached;
            }
            List<FieldInfo> fields = new List<FieldInfo>();
            HashSet<string> seen = new HashSet<string>();
            for (Type t = type; t != null && t != typeof(object) && t != typeof(ScriptableObject) && t != typeof(UnityEngine.Object); t = t.BaseType)
            {
                foreach (FieldInfo field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (field.IsNotSerialized || !seen.Add(field.Name))
                    {
                        continue;
                    }
                    bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
                    if (serialized && !field.Name.Contains("<"))
                    {
                        fields.Add(field);
                    }
                }
            }
            FieldInfo[] result = fields.ToArray();
            FieldCache[type] = result;
            return result;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
