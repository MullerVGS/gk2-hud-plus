using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GK2Shared
{
    /// <summary>Texto curto em inglês e português (o resto dos idiomas cai no inglês).</summary>
    internal readonly struct Loc
    {
        public readonly string En, Pt;

        public Loc(string en, string pt)
        {
            En = en;
            Pt = pt;
        }

        public string Current => LazyBearTechnology.LLBase.CurrentLang != null
            && LazyBearTechnology.LLBase.CurrentLang.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? Pt : En;
    }

    /// <summary>
    /// Uma funcionalidade que o jogador liga e desliga. <see cref="Part"/> é a parte do <see cref="ModGuard"/> que roda
    /// quando ela está ligada; uma opção (<see cref="Part"/> nulo) é um ajuste booleano de outra funcionalidade
    /// (<see cref="Parent"/>), e mudar a opção reinicia a funcionalidade.
    /// </summary>
    internal sealed class FeatureInfo
    {
        public string Id, Module, Category;
        public Loc Name, Description;
        /// <summary>Muda o estado do jogo (save, pontos...): começa desligada no pacote completo.</summary>
        public bool Gameplay;
        public ConfigEntry<bool> Toggle;
        public FeatureInfo Parent;
        internal ModGuard.Part Part;
        /// <summary>Ajustes que, ao mudar, reiniciam a funcionalidade (lidos só na criação).</summary>
        internal readonly List<ConfigEntryBase> Options = new List<ConfigEntryBase>();
        internal bool Retry, Restart;

        public bool IsOption => Part == null;
        public bool Wanted => Toggle.Value && (Parent == null || Parent.Wanted);
        public bool Running => Part != null ? Part.Running : Wanted;
        public bool Failed => Part != null && Part.Failed;
    }

    /// <summary>
    /// Casa de um ou mais módulos (o pacote GK2 HUD+ ou um mod avulso): config, log, ModGuard e a lista de
    /// funcionalidades. Mudar um toggle (menu, cenário ou o .cfg salvo no disco) liga ou desliga a funcionalidade no
    /// próximo frame, sem reabrir o jogo.
    /// </summary>
    internal sealed class ModHost : IDisposable
    {
        public readonly BaseUnityPlugin Plugin;
        public readonly ManualLogSource Log;
        public readonly ConfigFile Config;
        public readonly ModGuard Guard;
        public readonly string Guid, Name;
        /// <summary>Pacote com vários módulos: funcionalidades de gameplay começam desligadas.</summary>
        public readonly bool Bundle;

        private readonly List<FeatureInfo> features = new List<FeatureInfo>();
        private readonly List<Action> updates = new List<Action>();
        private readonly List<Action> disposes = new List<Action>();
        private readonly bool freshConfig;
        private string module = "";
        private bool started, dirty;
        private DateTime cfgStamp;
        private float nextCfgCheck;

        public IReadOnlyList<FeatureInfo> Features => features;

        /// <summary>Alguma funcionalidade ligou, desligou ou falhou (para o menu se redesenhar).</summary>
        public event Action Changed;

        public ModHost(BaseUnityPlugin plugin, ManualLogSource log, string guid, string name, bool bundle)
        {
            Plugin = plugin;
            Log = log;
            Config = plugin.Config;
            Guid = guid;
            Name = name;
            Bundle = bundle;
            freshConfig = !File.Exists(Config.ConfigFilePath);
            Guard = new ModGuard(log, guid, name);
        }

        /// <summary>Daqui em diante as funcionalidades registradas pertencem a este módulo.</summary>
        public void Module(string id, Action update = null, Action dispose = null)
        {
            module = id;
            if (update != null) updates.Add(update);
            if (dispose != null) disposes.Add(dispose);
        }

        /// <summary>
        /// Registra uma funcionalidade com toggle em [<paramref name="section"/>] <paramref name="key"/>. Ela inicia em
        /// <see cref="Start"/> se o toggle estiver ligado.
        /// </summary>
        public FeatureInfo Feature<T>(string id, string category, string section, string key, Loc name, Loc description,
            Func<Harmony, T> create, Action<T> update = null, Action<T> dispose = null, bool gameplay = false,
            FeatureInfo parent = null, params ConfigEntryBase[] options) where T : class
        {
            var f = Register(id, category, section, key, name, description, gameplay ? !Bundle : true, gameplay, parent);
            f.Part = Guard.Define(id, name.En.ToLowerInvariant(), name.Pt.ToLowerInvariant(), create, update, dispose);
            f.Options.AddRange(options);
            return f;
        }

        /// <summary>Ajuste booleano de uma funcionalidade, mostrado no menu abaixo dela; mudar reinicia a funcionalidade.</summary>
        public FeatureInfo Option(FeatureInfo parent, string id, string section, string key, bool on, Loc name, Loc description)
        {
            var f = Register(id, parent.Category, section, key, name, description, on, parent.Gameplay, parent);
            parent.Options.Add(f.Toggle);
            return f;
        }

        private FeatureInfo Register(string id, string category, string section, string key, Loc name, Loc description,
            bool on, bool gameplay, FeatureInfo parent)
        {
            if (features.Any(x => x.Id == id)) throw new ArgumentException("funcionalidade repetida: " + id);
            var f = new FeatureInfo
            {
                Id = id, Module = module, Category = category, Name = name, Description = description, Gameplay = gameplay,
                Parent = parent, Toggle = Config.Bind(section, key, on, description.En),
            };
            features.Add(f);
            return f;
        }

        public FeatureInfo Find(string id) => features.FirstOrDefault(f => f.Id == id);

        /// <summary>
        /// Config de um mod avulso antigo: na primeira vez que este .cfg é criado, os valores com a mesma seção e chave
        /// vêm de lá, e <paramref name="fixup"/> traduz o que mudou de lugar. Funcionalidades de gameplay do módulo
        /// ficam ligadas, porque o jogador já usava o mod.
        /// </summary>
        public void ImportLegacy(string legacyGuid, Action<Dictionary<string, string>> fixup = null)
        {
            if (!freshConfig || legacyGuid == Guid) return;
            string path = Path.Combine(Paths.ConfigPath, legacyGuid + ".cfg");
            if (!File.Exists(path)) return;
            Dictionary<string, string> old = ReadCfg(path);
            int n = 0;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> kv in Config)
            {
                if (!old.TryGetValue(kv.Key.Section + "\n" + kv.Key.Key, out string value)) continue;
                try { kv.Value.SetSerializedValue(value); n++; }
                catch (Exception e) { Log.LogWarning($"importar {kv.Key}: {e.Message}"); }
            }
            foreach (FeatureInfo f in features.Where(f => f.Module == module && f.Gameplay && !f.IsOption)) f.Toggle.Value = true;
            fixup?.Invoke(old);
            Log.LogInfo($"config importada de {legacyGuid}.cfg ({n} valores)");
        }

        /// <summary>Seção\nchave → valor de um .cfg do BepInEx.</summary>
        internal static Dictionary<string, string> ReadCfg(string path)
        {
            var d = new Dictionary<string, string>();
            string section = "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq > 0) d[section + "\n" + line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            return d;
        }

        /// <summary>Inicia o que está ligado e passa a seguir as mudanças de config.</summary>
        public void Start()
        {
            started = true;
            Config.SettingChanged += OnSettingChanged;
            cfgStamp = Stamp();
            Apply();
        }

        private void OnSettingChanged(object sender, SettingChangedEventArgs e)
        {
            foreach (FeatureInfo f in features)
            {
                if (f.Toggle == e.ChangedSetting && f.Toggle.Value)
                    foreach (FeatureInfo g in features.Where(g => g == f || IsUnder(g, f))) g.Retry = true;
                if (f.Options.Contains(e.ChangedSetting)) f.Restart = true;
            }
            dirty = true;
        }

        private static bool IsUnder(FeatureInfo f, FeatureInfo ancestor)
        {
            for (FeatureInfo p = f.Parent; p != null; p = p.Parent) if (p == ancestor) return true;
            return false;
        }

        /// <summary>Liga o que foi pedido e desliga o resto: primeiro os desligamentos (de trás para frente), depois as partidas.</summary>
        private void Apply()
        {
            bool changed = false;
            for (int i = features.Count - 1; i >= 0; i--)
            {
                FeatureInfo f = features[i];
                if (f.Part == null || !f.Part.Running) continue;
                if (!f.Wanted || f.Restart)
                {
                    Guard.Stop(f.Part);
                    Log.LogInfo($"{f.Id}: desligada{(f.Wanted ? " para reiniciar" : "")}");
                    changed = true;
                }
            }
            foreach (FeatureInfo f in features)
            {
                bool retry = f.Retry;
                f.Retry = f.Restart = false;
                if (f.Part == null || f.Part.Running || !f.Wanted) continue;
                if (f.Part.Failed && !retry) continue;
                bool ok = Guard.Start(f.Part);
                Log.LogInfo($"{f.Id}: {(ok ? "ligada" : "não iniciou")}");
                changed = true;
            }
            if (changed) Changed?.Invoke();
        }

        public void Update()
        {
            CheckCfgFile();
            if (dirty)
            {
                dirty = false;
                Apply();
            }
            int failedBefore = features.Count(f => f.Failed);
            Guard.Update();
            foreach (Action u in updates)
            {
                try { u(); }
                catch (Exception e) { Log.LogError("update do módulo: " + e); }
            }
            if (features.Count(f => f.Failed) != failedBefore) Changed?.Invoke();
        }

        /// <summary>O .cfg salvo no disco (editado à mão ou por fora) vale sem reabrir: relê a cada 1 s se mudou.</summary>
        private void CheckCfgFile()
        {
            if (!started || Time.unscaledTime < nextCfgCheck) return;
            nextCfgCheck = Time.unscaledTime + 1f;
            DateTime stamp = Stamp();
            if (stamp == cfgStamp) return;
            cfgStamp = stamp;
            try { Config.Reload(); }
            catch (Exception e) { Log.LogWarning("reler o cfg: " + e.Message); }
            cfgStamp = Stamp();
        }

        private DateTime Stamp()
        {
            try { return File.Exists(Config.ConfigFilePath) ? File.GetLastWriteTimeUtc(Config.ConfigFilePath) : DateTime.MinValue; }
            catch (Exception) { return DateTime.MinValue; }
        }

        public void Dispose()
        {
            Config.SettingChanged -= OnSettingChanged;
            Guard.Dispose();
            for (int i = disposes.Count - 1; i >= 0; i--)
            {
                try { disposes[i](); }
                catch (Exception e) { Log.LogError("descarte do módulo: " + e); }
            }
        }

        /// <summary>Pasta de dados de um módulo em BepInEx/config: a do mod antigo, se existir (diário e histórico
        /// continuam), senão uma pasta própria deste host.</summary>
        public string DataDir(string legacyGuid, string sub)
        {
            string legacy = Path.Combine(Paths.ConfigPath, legacyGuid);
            if (Directory.Exists(legacy) || Guid == legacyGuid) return string.IsNullOrEmpty(sub) ? legacy : Path.Combine(legacy, sub);
            string own = Path.Combine(Paths.ConfigPath, Guid);
            return string.IsNullOrEmpty(sub) ? own : Path.Combine(own, sub);
        }
    }
}
