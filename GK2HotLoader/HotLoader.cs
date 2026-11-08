using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using BepInEx;
using GK2Shared;
using LazyBearTechnology;
using UnityEngine;

namespace GK2HotLoader
{
    /// <summary>
    /// Aplica mods promovidos com o jogo aberto. Observa só <c>BepInEx/hot/*.dll</c> (escrito pelo <c>gk2 promote</c>),
    /// troca a versão em execução e avisa com a notificação nativa do jogo. Não abre porta nem executa código avulso.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class HotLoader : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.hotloader";
        public const string Name = "GK2 HotLoader";
        public const string Version = "0.1.0";

        private const float PollSeconds = 2f;

        private readonly Dictionary<string, string> appliedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, BaseUnityPlugin> loaded = new Dictionary<string, BaseUnityPlugin>();
        private readonly Queue<string> pendingNotices = new Queue<string>();
        private string hotDir;
        private float nextPoll;
        private bool busy;

        private void Awake()
        {
            hotDir = Path.Combine(Paths.BepInExRootPath, "hot");
            Directory.CreateDirectory(hotDir);
            // O que já está na pasta ao abrir o jogo é a mesma versão que o pacote Mods/Code acabou de carregar.
            foreach (string file in Directory.GetFiles(hotDir, "*.dll"))
            {
                string hash = TryHash(file, out _);
                if (hash != null) appliedHashes[Path.GetFileName(file)] = hash;
            }
            Logger.LogInfo($"{Name} {Version} observando {hotDir} ({appliedHashes.Count} já aplicados)");
        }

        private void Update()
        {
            FlushNotices();
            if (busy || Time.unscaledTime < nextPoll) return;
            nextPoll = Time.unscaledTime + PollSeconds;
            foreach (string file in Directory.GetFiles(hotDir, "*.dll"))
            {
                string name = Path.GetFileName(file);
                string hash = TryHash(file, out byte[] raw);
                if (hash == null || (appliedHashes.TryGetValue(name, out string old) && old == hash)) continue;
                appliedHashes[name] = hash;
                StartCoroutine(Apply(name, raw));
                return; // um por vez
            }
        }

        private IEnumerator Apply(string fileName, byte[] raw)
        {
            busy = true;
            List<Type> types;
            try
            {
                types = PluginReloader.PluginTypes(PluginReloader.LoadRenamed(raw));
                types.RemoveAll(t => PluginReloader.GuidOf(t) == Guid);
            }
            catch (Exception e)
            {
                Fail(fileName, e);
                busy = false;
                yield break;
            }
            foreach (Type type in types)
            {
                PluginReloader.DestroyExisting(PluginReloader.GuidOf(type), loaded);
            }
            yield return null;
            foreach (Type type in types)
            {
                try
                {
                    BaseUnityPlugin plugin = PluginReloader.Start(type, gameObject, loaded);
                    string message = $"{plugin.Info.Metadata.Name} {Updated()} {plugin.Info.Metadata.Version}";
                    Logger.LogInfo(message + $" ({fileName})");
                    pendingNotices.Enqueue(message);
                }
                catch (Exception e)
                {
                    Fail(fileName, e);
                }
            }
            busy = false;
        }

        private void Fail(string fileName, Exception e)
        {
            Logger.LogError($"falha ao aplicar {fileName}: {e}");
            pendingNotices.Enqueue($"{Path.GetFileNameWithoutExtension(fileName)}: {(IsPortuguese() ? "falha ao atualizar (ver log)" : "update failed (see log)")}");
        }

        private void FlushNotices()
        {
            if (pendingNotices.Count == 0) return;
            UINotificator notificator = LazySingleton<UINotificator>.Instance;
            if (notificator == null || MainGame.PlayerData == null) return; // espera estar em jogo para o aviso aparecer
            try
            {
                notificator.ShowSimpleTextNotification(pendingNotices.Dequeue());
            }
            catch (Exception e)
            {
                Logger.LogWarning("aviso nativo indisponível: " + e.Message);
                pendingNotices.Clear();
            }
        }

        private static string Updated() => IsPortuguese() ? "atualizado para" : "updated to";

        private static bool IsPortuguese() => LLBase.CurrentLang.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        private static string TryHash(string file, out byte[] raw)
        {
            raw = null;
            try
            {
                raw = File.ReadAllBytes(file);
                using (SHA256 sha = SHA256.Create())
                {
                    return BitConverter.ToString(sha.ComputeHash(raw));
                }
            }
            catch (IOException)
            {
                return null; // ainda sendo escrito; tenta no próximo ciclo
            }
        }
    }
}
