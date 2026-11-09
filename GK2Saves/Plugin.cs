using System;
using System.Collections;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Saves
{
    /// <summary>
    /// Saves dentro do jogo: Save e Load no menu de pausa (ESC). Save grava no slot atual pelo mesmo caminho do save ao
    /// dormir, só com o jogo livre (<see cref="SaveGuard"/>). Load abre a janela nativa de saves com o save atual, os
    /// backups do jogo e o histórico do mod (<see cref="History"/>), com rótulos e lixeira; carregar volta ao menu e entra
    /// no save escolhido, mantendo o slot atual para que o próximo save siga a fila normal.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.saves";
        public const string Name = "GK2 Saves";
        public const string Version = "0.1.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;

        private Harmony harmony;

        internal History History { get; private set; }

        internal PauseButtons Buttons { get; private set; }

        internal bool ManualSavePending { get; private set; }

        internal bool FixBackupSlot => fixBackupSlot.Value;

        private ConfigEntry<bool> fixBackupSlot;

        private bool busy;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            ConfigEntry<int> keep = Config.Bind("History", "KeepPerKind", 10, new ConfigDescription(
                "Quantos saves guardar no histórico do mod, para cada tipo (manual e ao dormir). Cada um ocupa ~13 MB em BepInEx/config/" + Guid + "/history.",
                new AcceptableValueRange<int>(1, 100)));
            fixBackupSlot = Config.Bind("Saves", "FixBackupSlot", true,
                "Jogando um backup do jogo (ex.: carregado pela lista do menu principal), o próximo save vai para o slot principal em vez de criar Steam_1_backup_1_backup_1.");
            History = new History(History.DefaultRoot, keep.Value);
            Buttons = new PauseButtons(OnSaveClicked, OnLoadClicked);
            harmony = new Harmony(Guid);
            Patches.Apply(harmony);
            UIGamePauseWindow pause = LazyUI.IsInitialized ? LazyUI.GetWindow<UIGamePauseWindow>() : null;
            if (pause != null && pause.IsShown) Buttons.Refresh(pause);
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void OnSaveClicked()
        {
            if (busy || SaveGuard.BlockReason() != null) return;
            LazyUI.GetWindow<UIGamePauseWindow>().Close();
            StartCoroutine(SaveNextFrame());
        }

        private IEnumerator SaveNextFrame()
        {
            busy = true;
            yield return null;
            string reason = SaveGuard.BlockReason();
            if (reason != null)
            {
                busy = false;
                Notify(L10n.T(reason));
                yield break;
            }
            bool ok = false;
            ManualSavePending = true;
            try
            {
                SaveSystem.Save(MainGame.Instance.SaveSlotData, MainGame.Instance.GameSave, () => ok = true, null);
            }
            catch (Exception e)
            {
                Log.LogError("save manual: " + e);
            }
            finally
            {
                ManualSavePending = false;
                busy = false;
            }
            Log.LogInfo("save manual " + (ok ? "ok" : "falhou") + " em " + MainGame.Instance.SaveSlotData.slotName);
            if (!ok) Notify(L10n.T("save_failed"));
        }

        private void OnLoadClicked()
        {
            if (busy) return;
            LazyUI.GetWindow<UIGamePauseWindow>().Close();
            Patches.InGameWindow = true;
            LazyUI.GetWindow<UISaveSlotsWindow>().Open(null);
        }

        internal void ConfirmLoad(UISaveSlotsWindow window, SaveSlotData slot)
        {
            UIDialogWindow dialog = LazyUI.GetWindow<UIDialogWindow>();
            dialog.Open(new UIDialogWindowData(LLBase.L("exit_menu_confirm"), L10n.T("load_confirm"), Yes, dialog.Close) { ShowCloseButton = false });
            void Yes()
            {
                dialog.Close();
                if (busy) return;
                string baseSlot = CurrentBaseSlot();
                Patches.InGameWindow = false;
                window.Close();
                StartCoroutine(LoadRoutine(slot, baseSlot));
            }
        }

        /// <summary>Mesmo caminho do jogo: volta ao menu e faz o que o Continue faz, com o slot escolhido.</summary>
        private IEnumerator LoadRoutine(SaveSlotData slot, string baseSlot)
        {
            busy = true;
            Log.LogInfo("carregando " + slot.slotName + " (dia " + slot.day + ", " + slot.saveDateTime + ") no slot " + baseSlot);
            MainGame.Instance.GoToMenu();
            float deadline = Time.realtimeSinceStartup + 90f;
            UIMainMenuWindow menu = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                menu = LazyUI.GetWindow<UIMainMenuWindow>();
                if (MainGame.Instance.gameState == MainGame.GameState.MainMenu && menu != null && menu.IsShown) break;
                yield return null;
            }
            busy = false;
            if (menu == null || !menu.IsShown)
            {
                Log.LogError("menu principal não apareceu; carregamento cancelado");
                yield break;
            }
            yield return new WaitForSecondsRealtime(0.5f);
            UILoadingOverlay overlay = LazyUI.Get<UILoadingOverlay>();
            overlay.Draw(new LoadingWindowData(MainGame.EntrySceneToLoad, () =>
            {
                SaveSystem.Load(slot, save =>
                {
                    if (save != null)
                    {
                        SaveSlotData target = slot.Copy();
                        target.slotName = baseSlot;
                        MainGame.Instance.ContinueGame(target, save);
                        menu.Close();
                    }
                    else
                    {
                        Log.LogError("falha ao ler " + slot.slotName);
                        overlay.Hide();
                        menu.Open(null);
                    }
                });
            }));
        }

        /// <summary>Slot em jogo; se for um backup do jogo e o principal existir, o principal.</summary>
        internal string CurrentBaseSlot()
        {
            string slot = MainGame.Instance.SaveSlotData.slotName;
            string baseSlot = Patches.BaseSlot(slot);
            return FixBackupSlot && System.IO.File.Exists(SaveSystem.SaveFolder + baseSlot + ".info") ? baseSlot : slot;
        }

        internal void LoadFromHistory(SaveSlotData slot, Action<GameSave> callback)
        {
            Action started = AccessTools.Field(typeof(SaveSystem), "OnSaveLoadingStarted").GetValue(null) as Action;
            Action ended = AccessTools.Field(typeof(SaveSystem), "OnSaveLoadingEnded").GetValue(null) as Action;
            started?.Invoke();
            GameSave result = null;
            try
            {
                History.Load(slot, s => result = s);
            }
            catch (Exception e)
            {
                Log.LogError("ler do histórico " + slot.slotName + ": " + e);
            }
            callback?.Invoke(result);
            ended?.Invoke();
        }

        private static void Notify(string text)
        {
            try { LazySingleton<UINotificator>.Instance?.ShowSimpleTextNotification(text); }
            catch (Exception e) { Log.LogWarning("aviso: " + e.Message); }
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            Buttons?.Dispose();
            Buttons = null;
            harmony?.UnpatchSelf();
            Patches.InGameWindow = false;
            if (Instance == this) Instance = null;
        }
    }
}
