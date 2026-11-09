using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using UnityEngine;

namespace GK2Saves
{
    /// <summary>
    /// Histórico próprio de saves, fora da pasta do jogo (que o Steam Cloud sincroniza): a cada save bem-sucedido copia
    /// <c>&lt;slot&gt;.dat/.info</c> para <c>BepInEx/config/arthur.gk2.saves/history/&lt;slot&gt;/</c> e mantém os últimos N de cada tipo.
    /// Entradas aparecem na janela de saves com <see cref="Marker"/> no slotName; Load/Remove do SaveSystem são desviados para cá.
    /// </summary>
    internal sealed class History
    {
        public const string Marker = "gk2saves|";
        public const string KindManual = "manual";
        public const string KindSleep = "sleep";

        private readonly string root;
        private readonly int keepPerKind;

        public History(string root, int keepPerKind)
        {
            this.root = root;
            this.keepPerKind = Math.Max(1, keepPerKind);
        }

        public sealed class Entry
        {
            public SaveSlotData Data;
            public string Kind;
        }

        private string SlotFolder(string slot) => Path.Combine(root, slot) + "/";

        public void Record(string slot, string kind)
        {
            string src = SaveSystem.SaveFolder + slot;
            if (!File.Exists(src + ".dat") || !File.Exists(src + ".info")) return;
            string folder = SlotFolder(slot);
            Directory.CreateDirectory(folder);
            string stem = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + kind;
            File.Copy(src + ".dat", folder + stem + ".dat", true);
            File.Copy(src + ".info", folder + stem + ".info", true);
            Prune(folder, kind);
        }

        private void Prune(string folder, string kind)
        {
            var stems = new List<string>();
            foreach (string f in Directory.GetFiles(folder, "*-" + kind + ".dat")) stems.Add(Path.GetFileNameWithoutExtension(f));
            stems.Sort(StringComparer.Ordinal);
            for (int i = 0; i < stems.Count - keepPerKind; i++) DeleteStem(folder, stems[i]);
        }

        public List<Entry> Entries(string slot)
        {
            var list = new List<Entry>();
            string folder = SlotFolder(slot);
            if (!Directory.Exists(folder)) return list;
            foreach (string info in Directory.GetFiles(folder, "*.info"))
            {
                string stem = Path.GetFileNameWithoutExtension(info);
                if (!File.Exists(folder + stem + ".dat")) continue;
                SaveSlotData data = ReadInfo(info);
                if (data == null) continue;
                data.slotName = Marker + slot + "/" + stem;
                list.Add(new Entry { Data = data, Kind = stem.EndsWith("-" + KindManual, StringComparison.Ordinal) ? KindManual : KindSleep });
            }
            return list;
        }

        public static SaveSlotData ReadInfo(string path)
        {
            try
            {
                SaveSlotData data = Newtonsoft.Json.JsonConvert.DeserializeObject<SaveSlotData>(File.ReadAllText(path));
                data?.OnAfterSerialize();
                return data;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("info ilegível " + path + ": " + e.Message);
                return null;
            }
        }

        public static bool IsEntry(SaveSlotData data) => data?.slotName != null && data.slotName.StartsWith(Marker, StringComparison.Ordinal);

        private bool Split(string slotName, out string folder, out string stem)
        {
            folder = stem = null;
            string rest = slotName.Substring(Marker.Length);
            int slash = rest.IndexOf('/');
            if (slash <= 0) return false;
            folder = SlotFolder(rest.Substring(0, slash));
            stem = rest.Substring(slash + 1);
            return stem.Length > 0 && stem.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public void Load(SaveSlotData data, Action<GameSave> callback)
        {
            if (!Split(data.slotName, out string folder, out string stem)) { callback?.Invoke(null); return; }
            SaveSystem.lazySaveSystem.Load<GameSave>(folder, stem, callback);
        }

        public bool Remove(SaveSlotData data)
        {
            if (!Split(data.slotName, out string folder, out string stem)) return false;
            DeleteStem(folder, stem);
            return true;
        }

        public void RemoveSameMoment(string slot, string saveDateTime)
        {
            foreach (Entry e in Entries(slot))
                if (e.Data.saveDateTime == saveDateTime) Remove(e.Data);
        }

        private static void DeleteStem(string folder, string stem)
        {
            File.Delete(folder + stem + ".dat");
            File.Delete(folder + stem + ".info");
        }

        public static string DefaultRoot => Path.Combine(Paths.ConfigPath, Plugin.Guid, "history");
    }
}
