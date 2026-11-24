using System;
using BepInEx;
using BepInEx.Configuration;
using GK2Shared;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Rescue
{
    // Ferramenta de resgate pontual, acionada por hot-load autorizado. Inerte ao iniciar o jogo;
    // consome o pedido antes do teleporte, não salva e não repete depois de recarregar a DLL.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "mullervgs.gk2.rescue";
        public const string Name = "GK2 Rescue";
        public const string Version = "0.1.0";
        private const string Destination = "tp_RT_home_enter";
        private ModGuard guard;
        private ConfigEntry<bool> attempted;
        private bool started, restorePause;
        private float deadline;
        private Vector3 expected;
        private GameSave save;

        private void Awake()
        {
            attempted = Config.Bind("Rescue", "Attempted", false,
                "Consumed before a one-time rescue. Startup never triggers a rescue. Do not reset without an explicit rescue request.");
            if (attempted.Value || MainGame.PlayerData == null || MainGame.PlayerController == null || !LazyUI.IsInitialized)
            {
                Logger.LogInfo("Inactive: startup or rescue already consumed.");
                enabled = false;
                return;
            }
            deadline = Time.realtimeSinceStartup + 1f;
            guard = new ModGuard(Logger, Guid, Name);
            guard.Feature("Rescue", "rescue", "resgate", h => this, p => p.Tick());
        }

        private void Update() => guard?.Update();
        private void OnDestroy() => guard?.Dispose();

        private void Tick()
        {
            if (started)
            {
                if (MainGame.Instance.GameSave != save) { Finish("Rescue interrupted: game changed.", false); return; }
                PlayerController pc = MainGame.PlayerController;
                if (pc != null && pc.IsControlEnabledByType(TakenControlType.ByTeleport) &&
                    Vector3.Distance(pc.transform.position, expected) < 2f)
                {
                    Logger.LogInfo($"Rescue complete: position={pc.transform.position:F3}, day={save.environmentData.Day}, slot={MainGame.Instance.SaveSlotData.slotName}. No save written.");
                    Finish(Portuguese ? "Resgate concluído: porta de casa." : "Rescue complete: outside home.", true);
                }
                else if (Time.realtimeSinceStartup > deadline) Finish("Rescue timeout; no retry.", false);
                return;
            }
            if (Time.realtimeSinceStartup < deadline) return;
            // Nunca tentar de novo por frame, inclusive se uma validação ou chamada lançar exceção.
            started = true;
            deadline = Time.realtimeSinceStartup + 30f;
            string blocked = GK2Saves.SaveGuard.BlockReason();
            if (blocked != null) { Finish("Rescue blocked: " + blocked, false); return; }
            save = MainGame.Instance.GameSave;
            if (!save.knowledgeSystem.visitedWorldZones.Contains("yard"))
            {
                Finish("Rescue blocked: destination not visited.", false); return;
            }
            var teleport = new WgoTeleportData(Destination);
            if (!teleport.CanTeleport(out string error) || teleport.GetDestinationSceneData()?.id != MainGame.PlayerData.currentGameSceneId)
            {
                Finish("Rescue blocked: " + (error ?? "destination in another scene"), false); return;
            }
            expected = teleport.GetPosition();
            var pause = LazyUI.GetWindow<UIGamePauseWindow>();
            restorePause = pause.IsShown;
            if (restorePause) pause.Close();
            if (!MainGame.PlayerController.IsControlsEnabled)
            {
                Finish("Rescue blocked: close other windows first.", false); return;
            }
            attempted.Value = true;
            Config.Save();
            Logger.LogInfo($"Rescue requested: {MainGame.PlayerController.transform.position:F3} -> {expected:F3}, day={save.environmentData.Day}, slot={MainGame.Instance.SaveSlotData.slotName}.");
            if (!PlayerController.Teleport(teleport)) Finish("Rescue rejected by native teleport.", false);
        }

        private static bool Portuguese => LLBase.CurrentLang.StartsWith("pt", StringComparison.OrdinalIgnoreCase);

        private void Finish(string message, bool success)
        {
            enabled = false;
            if (success) Logger.LogInfo(message); else Logger.LogWarning(message);
            if (restorePause && MainGame.Instance.GameSave == save) LazyUI.GetWindow<UIGamePauseWindow>().Open(null);
            LazySingleton<UINotificator>.Instance.ShowSimpleTextNotification(message);
        }
    }
}
