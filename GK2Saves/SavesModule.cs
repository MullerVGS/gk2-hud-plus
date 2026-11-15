using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
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
    internal sealed class SavesModule
    {
        /// <summary>GUID do mod avulso: nome do .cfg antigo e da pasta do histórico em BepInEx/config.</summary>
        public const string LegacyGuid = "arthur.gk2.saves";

        internal static ManualLogSource Log;
        internal static SavesModule Instance;

        internal readonly ModHost Host;

        internal History History { get; private set; }

        internal PauseButtons Buttons { get; private set; }

        internal bool ManualSavePending { get; private set; }

        internal bool FixBackupSlot => fixBackupSlot.Value;

        private readonly ConfigEntry<bool> fixBackupSlot;
        private readonly List<Coroutine> running = new List<Coroutine>();

        private bool busy;

        public SavesModule(ModHost host)
        {
            Host = host;
            Log = host.Log;
            Instance = this;
            host.Module("saves", null, () => { if (Instance == this) Instance = null; });
            ConfigEntry<int> keep = host.Config.Bind("History", "KeepPerKind", 10, new ConfigDescription(
                "How many saves the mod keeps in its history for each kind (manual and on sleep). Each one takes about 13 MB.",
                new AcceptableValueRange<int>(1, 100)));
            fixBackupSlot = host.Config.Bind("Saves", "FixBackupSlot", true,
                "When playing a game backup (e.g. loaded from the main menu list), the next save goes to the main slot instead of creating Steam_1_backup_1_backup_1.");
            host.Feature("SaveLoad", "gameplay", "Saves", "Enabled",
                new Loc("Save and Load in the pause menu", "Save e Load no menu de pausa"),
                new Loc("Save whenever the game is free (no dialogue, cutscene, fight, sleep or carried load) and load the current save, the game's backups or the mod's history. Changes how the game saves.",
                    "Salve quando o jogo estiver livre (sem diálogo, cena, luta, sono ou carga na cabeça) e carregue o save atual, os backups do jogo ou o histórico do mod. Muda como o jogo salva."),
                h =>
                {
                    History = new History(host.DataDir(LegacyGuid, "history"), keep.Value);
                    Buttons = new PauseButtons(OnSaveClicked, OnLoadClicked);
                    Patches.Apply(h);
                    UIGamePauseWindow pause = LazyUI.IsInitialized ? LazyUI.GetWindow<UIGamePauseWindow>() : null;
                    if (pause != null && pause.IsShown) Buttons.Refresh(pause);
                    return this;
                }, dispose: p => p.StopSaveLoad(), gameplay: true, options: keep);
            host.ImportLegacy(LegacyGuid);
        }

        private void Run(IEnumerator routine) => running.Add(Host.Plugin.StartCoroutine(routine));

        private void OnSaveClicked()
        {
            if (busy || SaveGuard.BlockReason() != null) return;
            LazyUI.GetWindow<UIGamePauseWindow>().Close();
            Run(SaveNextFrame());
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
                Run(LoadRoutine(slot, baseSlot));
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

        private void StopSaveLoad()
        {
            foreach (Coroutine c in running) if (c != null) Host.Plugin.StopCoroutine(c);
            running.Clear();
            busy = false;
            Buttons?.Dispose();
            Buttons = null;
            Patches.InGameWindow = false;
        }
    }
}
