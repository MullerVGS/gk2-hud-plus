using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Shared
{
    /// <summary>
    /// Isola as partes de um mod. Cada parte tem os próprios patches (Harmony com id próprio) e um Update protegido.
    /// Se ela não inicia, ou erra 5 vezes em 60 s (no Update, num patch ou no que ela mesma reportar), só ela desliga:
    /// sai do Update, solta os patches, é descartada e o jogador vê um aviso nativo. O resto do mod e o jogo seguem.
    /// Exceção que nasce num patch da parte não chega ao código do jogo (finalizer); a do próprio jogo passa intacta.
    /// </summary>
    internal sealed class ModGuard : IDisposable
    {
        private const int MaxErrors = 5;
        private const float WindowSeconds = 60f;

        /// <summary>Uma parte: definida uma vez, ligada e desligada quantas vezes for preciso (menu, cfg, erro).</summary>
        internal sealed class Part
        {
            public ModGuard Owner;
            public string Id, En, Pt;
            public Harmony Harmony;
            public Func<Harmony, object> Create;
            public Action<object> Tick;
            public Action<object> Release;
            public object Instance;
            /// <summary>Iniciada e rodando o Update.</summary>
            public bool Running;
            /// <summary>Desligada por erro (não iniciou ou errou demais) desde o último <see cref="Start"/>.</summary>
            public bool Failed;
            public readonly Queue<float> Errors = new Queue<float>();
        }

        /// <summary>Partes por método original patchado, para o finalizer saber de quem é a exceção.</summary>
        private static readonly Dictionary<MethodBase, List<Part>> ByOriginal = new Dictionary<MethodBase, List<Part>>();

        private readonly ManualLogSource log;
        private readonly string guid;
        private readonly string modName;
        private readonly List<Part> parts = new List<Part>();
        private readonly List<Part> pendingOff = new List<Part>();
        private readonly Queue<Func<string>> notices = new Queue<Func<string>>();

        public ModGuard(ManualLogSource log, string guid, string modName)
        {
            this.log = log;
            this.guid = guid;
            this.modName = modName;
        }

        /// <summary>
        /// Define uma parte sem iniciá-la. <paramref name="create"/> recebe o Harmony da parte (patches feitos nele saem
        /// juntos quando ela desliga) e é chamado a cada <see cref="Start"/>.
        /// </summary>
        public Part Define<T>(string id, string en, string pt, Func<Harmony, T> create, Action<T> update = null, Action<T> dispose = null) where T : class
        {
            if (parts.Any(p => p.Id == id)) throw new ArgumentException("parte repetida: " + id);
            var part = new Part
            {
                Owner = this, Id = id, En = en, Pt = pt,
                Harmony = new Harmony(guid + "." + id.ToLowerInvariant()),
                Create = h => create(h),
                Tick = update == null ? (Action<object>)null : o => update((T)o),
                Release = dispose == null ? (Action<object>)null : o => dispose((T)o),
            };
            parts.Add(part);
            return part;
        }

        /// <summary>Define e inicia uma parte. Devolve a instância, ou <c>null</c> se ela não iniciou.</summary>
        public T Feature<T>(string id, string en, string pt, Func<Harmony, T> create, Action<T> update = null, Action<T> dispose = null) where T : class
        {
            Part part = Define(id, en, pt, create, update, dispose);
            return Start(part) ? (T)part.Instance : null;
        }

        /// <summary>Inicia a parte (de novo, se ela desligou por erro). Falhar ao iniciar desliga só ela, com aviso.</summary>
        public bool Start(Part p)
        {
            if (p.Running) return true;
            if (pendingOff.Remove(p)) Release(p);
            p.Errors.Clear();
            p.Failed = false;
            try
            {
                p.Instance = p.Create(p.Harmony);
                if (p.Instance == null) throw new InvalidOperationException("a parte não devolveu instância");
            }
            catch (Exception e)
            {
                log.LogError($"[guard] {p.Id}: não iniciou: {e}");
                p.Instance = null;
                p.Failed = true;
                Unpatch(p);
                Announce(p);
                return false;
            }
            p.Running = true;
            InstallFinalizers(p);
            return true;
        }

        /// <summary>Desliga a parte: sai do Update, é descartada e solta os patches.</summary>
        public void Stop(Part p)
        {
            pendingOff.Remove(p);
            p.Running = false;
            Release(p);
        }

        public bool IsOn(string id) => parts.Any(p => p.Id == id && p.Running);

        /// <summary>Partes desligadas por erro nesta sessão (lido pelos cenários de teste).</summary>
        public string[] OffParts => parts.Where(p => p.Failed).Select(p => p.Id).ToArray();

        /// <summary>Erro visto fora do Update e dos patches (MonoBehaviour próprio, callback de UI...).</summary>
        public void Report(string id, Exception e, string where)
        {
            Part part = parts.FirstOrDefault(p => p.Id == id);
            if (part == null) log.LogError($"[guard] parte desconhecida {id} ({where}): {e}");
            else Count(part, e, where);
        }

        /// <summary>Chamar no Update do plugin: desliga o que ficou pendente, mostra avisos e roda o Update das partes.</summary>
        public void Update()
        {
            if (pendingOff.Count > 0)
            {
                foreach (Part p in pendingOff.ToArray()) Release(p);
                pendingOff.Clear();
            }
            FlushNotices();
            foreach (Part p in parts.ToArray())
            {
                if (!p.Running || p.Tick == null) continue;
                try { p.Tick(p.Instance); }
                catch (Exception e) { Count(p, e, "Update"); }
            }
        }

        /// <summary>Descarta as partes na ordem inversa da criação e solta todos os patches.</summary>
        public void Dispose()
        {
            for (int i = parts.Count - 1; i >= 0; i--)
            {
                parts[i].Running = false;
                Release(parts[i]);
            }
            parts.Clear();
            pendingOff.Clear();
            notices.Clear();
        }

        private void Count(Part p, Exception e, string where)
        {
            if (!p.Running) return;
            float now = Time.realtimeSinceStartup;
            p.Errors.Enqueue(now);
            while (p.Errors.Count > 0 && now - p.Errors.Peek() > WindowSeconds) p.Errors.Dequeue();
            if (p.Errors.Count == 1) log.LogError($"[guard] {p.Id}: erro em {where} (1/{MaxErrors}): {e}");
            else log.LogError($"[guard] {p.Id}: erro em {where} ({p.Errors.Count}/{MaxErrors}): {e.GetType().Name}: {e.Message}");
            if (p.Errors.Count < MaxErrors) return;
            log.LogError($"[guard] {p.Id}: desligada após {MaxErrors} erros em {WindowSeconds:0} s");
            p.Running = false;
            p.Failed = true;
            pendingOff.Add(p);   // soltar patches fora da pilha do patch que falhou
            Announce(p);
        }

        /// <summary>Descarta a instância (uma vez) e solta os patches da parte.</summary>
        private void Release(Part p)
        {
            object instance = p.Instance;
            p.Instance = null;
            if (instance != null && p.Release != null)
            {
                try { p.Release(instance); }
                catch (Exception e) { log.LogError($"[guard] {p.Id}: erro ao descartar: {e}"); }
            }
            Unpatch(p);
        }

        private void Announce(Part p)
        {
            notices.Enqueue(() => Portuguese()
                ? $"{modName} · {p.Pt}: desligado após um erro (ver log)"
                : $"{modName} · {p.En}: turned off after an error (see log)");
        }

        private void InstallFinalizers(Part p)
        {
            var finalizer = new HarmonyMethod(AccessTools.Method(typeof(ModGuard), nameof(Finalizer)));
            foreach (MethodBase original in p.Harmony.GetPatchedMethods().ToList())
            {
                p.Harmony.Patch(original, finalizer: finalizer);
                if (!ByOriginal.TryGetValue(original, out List<Part> list)) ByOriginal[original] = list = new List<Part>();
                list.Add(p);
            }
        }

        private static void Unpatch(Part p)
        {
            try { p.Harmony.UnpatchSelf(); }
            catch (Exception e) { p.Owner.log.LogError($"[guard] {p.Id}: erro ao soltar patches: {e}"); }
            foreach (List<Part> list in ByOriginal.Values) list.Remove(p);
        }

        /// <summary>Exceção de um patch deste mod: conta para a parte e não chega ao jogo. A do jogo segue adiante.</summary>
        private static Exception Finalizer(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null) return null;
            if (__originalMethod == null || !ByOriginal.TryGetValue(__originalMethod, out List<Part> list) || list.Count == 0) return __exception;
            if (!FromThisMod(__exception)) return __exception;
            string where = "patch " + __originalMethod.DeclaringType?.Name + "." + __originalMethod.Name;
            foreach (Part p in list.ToArray()) p.Owner.Count(p, __exception, where);
            return null;
        }

        private static bool FromThisMod(Exception e)
        {
            Assembly mine = typeof(ModGuard).Assembly;
            for (Exception x = e; x != null; x = x.InnerException)
            {
                StackFrame[] frames = new StackTrace(x, false).GetFrames();
                if (frames == null) continue;
                foreach (StackFrame f in frames)
                    if (f.GetMethod()?.DeclaringType?.Assembly == mine) return true;
            }
            return false;
        }

        /// <summary>Exceção com quadro deste mod, para o cenário de teste do finalizer.</summary>
        internal static Exception SampleException()
        {
            try { throw new InvalidOperationException("teste do ModGuard"); }
            catch (Exception e) { return e; }
        }

        private float readySince = -1f;

        /// <summary>Em jogo e visível: save carregado, tela de carregamento fechada há 1,5 s. Antes disso o aviso some
        /// atrás do carregamento, e tocar no UINotificator antes do LazyUI.Init quebra a UI do jogo.</summary>
        private bool ReadyForNotice()
        {
            bool ready = false;
            if (MainGame.PlayerData != null && MainGame.PlayerController != null && LazyUI.IsInitialized)
            {
                try { ready = !LazyUI.Get<UILoadingOverlay>().IsShown; }
                catch (Exception) { ready = false; }
            }
            if (!ready) { readySince = -1f; return false; }
            if (readySince < 0f) readySince = Time.realtimeSinceStartup;
            return Time.realtimeSinceStartup - readySince >= 1.5f;
        }

        private void FlushNotices()
        {
            if (notices.Count == 0) return;
            if (!ReadyForNotice()) return;
            UINotificator notificator = LazySingleton<UINotificator>.Instance;
            if (notificator == null) return;
            try { notificator.ShowSimpleTextNotification(notices.Dequeue()()); }
            catch (Exception e)
            {
                log.LogWarning("[guard] aviso nativo indisponível: " + e.Message);
                notices.Clear();
            }
        }

        private static bool Portuguese() => LLBase.CurrentLang != null && LLBase.CurrentLang.StartsWith("pt", StringComparison.OrdinalIgnoreCase);
    }
}
