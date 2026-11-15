using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine.UI;

namespace GK2Saves
{
    /// <summary>
    /// Ganchos no jogo: menu de pausa (botões), janela nativa de saves ("Pick a save slot") em modo "dentro do jogo" e
    /// SaveSystem (histórico após cada save; Load/Remove de entradas do histórico).
    /// </summary>
    internal static class Patches
    {
        /// <summary>A janela de saves foi aberta pelo botão Load do menu de pausa.</summary>
        internal static bool InGameWindow;

        private static SavesModule P => SavesModule.Instance;

        private static bool InGameMode => InGameWindow && P != null && MainGame.Instance != null && MainGame.Instance.gameState == MainGame.GameState.InGame;

        public static void Apply(Harmony h)
        {
            Type pause = typeof(UIGamePauseWindow);
            h.Patch(AccessTools.DeclaredMethod(pause, nameof(UIGamePauseWindow.Open), new[] { typeof(LazyWidgetDataBase) }), postfix: M(nameof(PauseOpenPostfix)));
            h.Patch(AccessTools.DeclaredMethod(pause, nameof(UIGamePauseWindow.Close), Type.EmptyTypes), postfix: M(nameof(PauseClosePostfix)));
            Type win = typeof(UISaveSlotsWindow);
            h.Patch(AccessTools.Method(win, "ReadSlotsAndDisplay"), prefix: M(nameof(ReadSlotsPrefix)));
            h.Patch(AccessTools.DeclaredMethod(win, nameof(UISaveSlotsWindow.Open), new[] { typeof(LazyWidgetDataBase) }), postfix: M(nameof(WindowOpenPostfix)));
            h.Patch(AccessTools.Method(win, "OnSaveSlotSelectedHandler"), prefix: M(nameof(SelectPrefix)));
            h.Patch(AccessTools.Method(win, "ReturnToPreviousWindow"), prefix: M(nameof(ReturnPrefix)));
            Type ss = typeof(SaveSystem);
            h.Patch(AccessTools.Method(ss, nameof(SaveSystem.Load)), prefix: M(nameof(LoadPrefix)));
            h.Patch(AccessTools.Method(ss, nameof(SaveSystem.Remove)), prefix: M(nameof(RemovePrefix)), postfix: M(nameof(RemovePostfix)));
            h.Patch(AccessTools.Method(ss, nameof(SaveSystem.Save)), prefix: M(nameof(SavePrefix)), postfix: M(nameof(SavePostfix)));
        }

        private static HarmonyMethod M(string name) => new HarmonyMethod(typeof(Patches), name);

        private static void PauseOpenPostfix(UIGamePauseWindow __instance) => P?.Buttons?.Refresh(__instance);

        private static void PauseClosePostfix() => P?.Buttons?.HideHint();

        private static bool ReadSlotsPrefix(UISaveSlotsWindow __instance)
        {
            if (!InGameMode) return true;
            try
            {
                var tr = Traverse.Create(__instance);
                var showing = tr.Field("showingSlotsElements").GetValue<List<UISaveSlot>>();
                var newSlot = tr.Field("newSaveSlot").GetValue<UISaveSlot>();
                List<Row> rows = BuildRows(P.CurrentBaseSlot());
                var datas = new List<SaveSlotData>();
                foreach (Row row in rows)
                {
                    UISaveSlot el = tr.Method("GetUISaveSlotElement").GetValue<UISaveSlot>();
                    showing.Add(el);
                    el.Show(row.Data, row.CanDelete);
                    el.SetSourceLabel(row.Label);
                    datas.Add(row.Data);
                }
                tr.Field("saveSlotDataList").SetValue(datas);
                tr.Method("SortSlotsByDateTime").GetValue();
                newSlot.gameObject.SetActive(false);
                return false;
            }
            catch (Exception e)
            {
                SavesModule.Log.LogError("lista de saves: " + e);
                return true;
            }
        }

        private static void WindowOpenPostfix(UISaveSlotsWindow __instance)
        {
            if (!InGameMode) return;
            Traverse.Create(__instance).Field("newSaveSlot").GetValue<UISaveSlot>().gameObject.SetActive(false);
            ((UnityEngine.RectTransform)__instance.transform).RefreshContentFitter();
            if (LazyInput.IsGamepadActive) Traverse.Create(__instance).Property("GamepadNavigationController").GetValue<GamepadNavigationController>()?.ReinitItems(focusOnFirstActive: true);
        }

        private static bool SelectPrefix(UISaveSlotsWindow __instance, SaveSlotData slotData)
        {
            if (!InGameMode) return true;
            if (__instance.IsShownAndTop && slotData != null) P.ConfirmLoad(__instance, slotData);
            return false;
        }

        private static bool ReturnPrefix()
        {
            if (!InGameWindow) return true;
            InGameWindow = false;
            if (MainGame.Instance == null || MainGame.Instance.gameState != MainGame.GameState.InGame) return true;
            LazyUI.GetWindow<UIGamePauseWindow>().Open(null);
            return false;
        }

        private static bool LoadPrefix(SaveSlotData slotData, Action<GameSave> callback)
        {
            if (!History.IsEntry(slotData) || P == null) return true;
            P.LoadFromHistory(slotData, callback);
            return false;
        }

        private static bool RemovePrefix(SaveSlotData slotData, ref bool __result)
        {
            if (!History.IsEntry(slotData) || P == null) return true;
            try { __result = P.History.Remove(slotData); }
            catch (Exception e) { SavesModule.Log.LogError("apagar do histórico: " + e); __result = false; }
            return false;
        }

        /// <summary>Apagar um backup do jogo apaga também a cópia dele no histórico (senão ele reaparece como entrada do histórico).</summary>
        private static void RemovePostfix(SaveSlotData slotData, bool __result)
        {
            if (!__result || P == null || slotData?.slotName == null || History.IsEntry(slotData) || string.IsNullOrEmpty(slotData.saveDateTime)) return;
            string baseSlot = BaseSlot(slotData.slotName);
            if (baseSlot == slotData.slotName) return;
            try { P.History.RemoveSameMoment(baseSlot, slotData.saveDateTime); }
            catch (Exception e) { SavesModule.Log.LogError("apagar cópia no histórico: " + e); }
        }

        private static void SavePrefix(SaveSlotData slotData, out DateTime __state)
        {
            __state = DateTime.UtcNow;
            if (P == null || !P.FixBackupSlot || slotData?.slotName == null || History.IsEntry(slotData)) return;
            string baseSlot = BaseSlot(slotData.slotName);
            if (baseSlot == slotData.slotName || !File.Exists(SaveSystem.SaveFolder + baseSlot + ".info")) return;
            SavesModule.Log.LogInfo("save do backup " + slotData.slotName + " vai para o slot principal " + baseSlot);
            slotData.slotName = baseSlot;
        }

        private static readonly Regex BackupSuffix = new Regex(@"^(.+?)(_backup_\d+)+$");

        /// <summary>Slot principal de um backup do jogo (<c>Steam_1_backup_1_backup_2</c> → <c>Steam_1</c>).</summary>
        internal static string BaseSlot(string slot)
        {
            Match m = BackupSuffix.Match(slot ?? string.Empty);
            return m.Success ? m.Groups[1].Value : slot;
        }

        private static void SavePostfix(SaveSlotData slotData, DateTime __state)
        {
            if (P == null || slotData?.slotName == null || History.IsEntry(slotData)) return;
            try
            {
                string dat = SaveSystem.SaveFolder + slotData.slotName + ".dat";
                if (!File.Exists(dat) || File.GetLastWriteTimeUtc(dat) < __state.AddSeconds(-2)) return;
                P.History.Record(slotData.slotName, P.ManualSavePending ? History.KindManual : History.KindSleep);
            }
            catch (Exception e)
            {
                SavesModule.Log.LogError("histórico: " + e);
            }
        }

        internal sealed class Row
        {
            public SaveSlotData Data;
            public string Label;
            public bool CanDelete;
        }

        /// <summary>Save atual, os 3 backups do jogo e o histórico do mod (sem repetir o que já está nos backups).</summary>
        internal static List<Row> BuildRows(string slot)
        {
            var rows = new List<Row>();
            var seen = new HashSet<string>();
            void AddNative(string name, string label, bool canDelete)
            {
                string path = SaveSystem.SaveFolder + name;
                if (!File.Exists(path + ".dat") || !File.Exists(path + ".info")) return;
                SaveSlotData data = History.ReadInfo(path + ".info");
                if (data == null) return;
                data.slotName = name;
                seen.Add(data.saveDateTime);
                rows.Add(new Row { Data = data, Label = label, CanDelete = canDelete });
            }
            AddNative(slot, L10n.T("label_current"), false);
            for (int i = 1; i <= 3; i++) AddNative(slot + "_backup_" + i, string.Format(L10n.T("label_backup"), i), true);
            foreach (History.Entry e in P.History.Entries(slot))
            {
                if (!seen.Add(e.Data.saveDateTime)) continue;
                rows.Add(new Row { Data = e.Data, Label = L10n.T(e.Kind == History.KindManual ? "label_manual" : "label_sleep"), CanDelete = true });
            }
            return rows;
        }
    }
}
