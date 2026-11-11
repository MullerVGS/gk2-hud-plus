using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.Journal
{
    /// <summary>Uma linha do diário: algo que passou a existir num momento do jogo.</summary>
    [Serializable]
    internal sealed class JournalEntry
    {
        public int d;        // dia absoluto
        public float t;      // timeOfDay
        public string k;     // tipo (JournalKinds)
        public string id;
        public bool a;       // aproximado: notado ao carregar (aconteceu enquanto o mod não registrava)

        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public SermonNote s; // só nos sermões: o resultado (o save só guarda o último)

        [Newtonsoft.Json.JsonIgnore]
        public double When => d + (double)t;
    }

    /// <summary>Resultado de um sermão, copiado do <c>SermonResultData</c> do jogo (mesmas contas da janela de relatório).</summary>
    [Serializable]
    internal sealed class SermonNote
    {
        public int n;        // paroquianos
        public bool ok;      // sucesso
        public int m, mb;    // dinheiro em bronze (doações) e bônus
        public int f, fb;    // fé dos paroquianos e bônus
        public int cq, gq;   // qualidade da igreja e do cemitério usadas na conta
        public string buff;  // buff do sucesso

        [Newtonsoft.Json.JsonIgnore]
        public int Money => m + mb;

        [Newtonsoft.Json.JsonIgnore]
        public int Faith => f + fb;

        public static SermonNote From(SermonResultData r) => new SermonNote
        {
            n = r.parishionersCount, ok = r.success, m = r.MoneyOnlyParishioners, mb = r.MoneyOnlyBonus,
            f = r.FaithOnlyParishioners, fb = r.FaithOnlyBonus, cq = r.churchQuality, gq = r.graveyardQuality,
            buff = r.success && !string.IsNullOrEmpty(r.Definition?.successRewardBuff) ? r.Definition.successRewardBuff : null,
        };
    }

    [Serializable]
    internal sealed class JournalFile
    {
        public int version = 1;   // 2: tecnologias reveladas (JournalKinds.Reveal)
        public string slot;
        public int startDay;
        public float startTod;
        public List<string> baseline = new List<string>();   // "tipo:id" já conhecidos quando o diário começou
        public List<JournalEntry> entries = new List<JournalEntry>();
    }

    internal static class JournalKinds
    {
        public const string Quest = "quest", Tech = "tech", Building = "building", Town = "town", Craft = "craft", Alchemy = "alchemy",
            Perk = "perk", DayIcon = "dayicon", Tutorial = "tutorial", Sermon = "sermon",
            Reveal = "reveal",   // tecnologia que nasce escondida (TechDef.hiddenAtStart) e apareceu na Tech Tree
            Phrase = "phrase";   // resposta de diálogo já escolhida (KnowledgeSystem.blackListPhrases): só para datar revelações, nunca listada

        /// <summary>Ordem de exibição dentro do dia.</summary>
        public static readonly string[] Order = { Sermon, Quest, Reveal, Tech, Building, Town, Craft, Alchemy, Perk, DayIcon, Tutorial };

        /// <summary>Tipos que só crescem no jogo: se um some, o save carregado é outra linha do tempo.</summary>
        public static readonly string[] Monotonic = { Quest, Reveal, Tech, Building, Craft, Alchemy };
    }

    /// <summary>
    /// Diário de desbloqueios por save, só leitura do jogo. A cada 2 s compara as listas do <c>KnowledgeSystem</c>, as
    /// perks permanentes e as quests concluídas da árvore de quests com o que já foi visto e anota as novidades com dia e
    /// hora. Fica em <c>BepInEx/config/arthur.gk2.hud/journal-&lt;slot&gt;.json</c> (o save do jogo não é tocado).
    /// O jogo só salva ao dormir: ao carregar um save, o que o diário anotou depois daquele ponto é descartado; se algo
    /// "que só cresce" sumiu, é outro jogo no mesmo slot e o diário recomeça.
    /// Sermões são eventos, não desbloqueios: cada <c>PlayerData.currentSermon</c> novo (o jogo troca o objeto quando o
    /// sermão começa, com o resultado já sorteado) vira uma anotação com o resultado.
    /// </summary>
    internal sealed class JournalTracker : IDisposable
    {
        internal static JournalTracker Current;

        private const float Interval = 2f;
        private const float WriteDelay = 3f;

        private JournalFile file;
        private string path;
        private GameSave gameSave;
        private SermonResultData lastSermon;
        private readonly HashSet<string> seen = new HashSet<string>();
        private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        private float nextTick, writeAt = -1f;
        private bool loggedError;
        private int version;   // muda a cada anotação (caches de consulta e da página)

        public JournalTracker()
        {
            Current = this;
        }

        public int Version => version;
        public bool Active => file != null && gameSave != null;
        public int StartDay => file?.startDay ?? 0;
        public IReadOnlyList<JournalEntry> Entries => (IReadOnlyList<JournalEntry>)file?.entries ?? Array.Empty<JournalEntry>();

        public void Dispose()
        {
            Flush();
            if (Current == this) Current = null;
        }

        public void Update()
        {
            try
            {
                float now = Time.unscaledTime;
                if (writeAt > 0f && now >= writeAt) Flush();
                if (now < nextTick) return;
                nextTick = now + Interval;
                GameSave gs = MainGame.Instance != null ? MainGame.Instance.GameSave : null;
                if (gs == null || MainGame.PlayerData == null || gs.knowledgeSystem == null || EnvironmentEngine.Instance == null)
                {
                    if (gameSave != null) { Flush(); gameSave = null; file = null; }
                    return;
                }
                if (gs != gameSave) Attach(gs);
                else { Scan(false); CheckSermon(); }
            }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("diário: " + ex);
                loggedError = true;
            }
        }

        // ---------------- ciclo ----------------

        private void Attach(GameSave gs)
        {
            Flush();
            gameSave = gs;
            // Os backups que o jogo gira (Steam_1_backup_1..3) são a mesma partida, mais atrás no tempo: mesmo diário.
            lastSermon = MainGame.PlayerData.currentSermon;   // o último sermão do save carregado: sem data, não entra
            string slot = System.Text.RegularExpressions.Regex.Replace(SaveSystem.RelevantSlot?.slotName ?? "", "_backup_[0-9]+$", "");
            if (string.IsNullOrEmpty(slot)) slot = "default";
            path = Path.Combine(Path.Combine(Paths.ConfigPath, Plugin.Guid), "journal-" + Safe(slot) + ".json");
            file = Read(path);
            HashSet<string> current = Snapshot();
            double now = Now();
            if (file == null || file.slot != slot)
            {
                Restart(slot, current, "novo");
                return;
            }
            if (file.version < 2)
            {
                // Diário de antes das revelações: o que já está visível na Tech Tree entra como já conhecido (sem data).
                foreach (string key in current) if (KindOf(key) == JournalKinds.Reveal && !file.baseline.Contains(key)) file.baseline.Add(key);
                file.version = 2;
                Dirty();
            }
            int before = file.entries.Count;
            // Linha do tempo abandonada: o que foi anotado depois do ponto carregado não aconteceu neste save.
            file.entries.RemoveAll(e => e.When > now + 0.0005);
            int dropped = before - file.entries.Count;
            List<string> missing = file.baseline.Concat(file.entries.Select(e => e.k + ":" + e.id))
                .Where(key => JournalKinds.Monotonic.Contains(KindOf(key)) && !current.Contains(key)).ToList();
            int monotonicKnown = file.baseline.Count(key => JournalKinds.Monotonic.Contains(KindOf(key)));
            int envDay = gs.environmentData.Day;
            if (envDay < file.startDay || missing.Count > Math.Max(5, monotonicKnown / 10))
            {
                Restart(slot, current, envDay < file.startDay
                    ? $"save do dia {envDay}, antes do início do diário"
                    : "outro jogo no slot (" + missing.Count + " desbloqueios sumiram, ex.: " + missing[0] + ")");
                return;
            }
            if (missing.Count > 0)
            {
                // Poucos sumiços (receita trocada por versão melhor etc.): só tira essas linhas.
                var gone = new HashSet<string>(missing);
                file.entries.RemoveAll(e => gone.Contains(e.k + ":" + e.id));
                dropped += missing.Count;
            }
            seen.Clear();
            foreach (string key in file.baseline) seen.Add(key);
            foreach (JournalEntry e in file.entries) seen.Add(e.k + ":" + e.id);
            if (dropped > 0) Dirty();
            int added = AddNew(current, approx: true);
            counts.Clear();
            Plugin.Log.LogInfo($"diário: {slot} desde o dia {file.startDay}, {file.entries.Count} anotações ({dropped} descartadas após o ponto carregado, {added} notadas ao carregar)");
            version++;
        }

        private void Restart(string slot, HashSet<string> current, string why)
        {
            EnvironmentData env = MainGame.Instance.GameSave.environmentData;
            file = new JournalFile { version = 2, slot = slot, startDay = env.Day, startTod = env.TimeOfDay, baseline = current.OrderBy(x => x).ToList() };
            seen.Clear();
            foreach (string key in current) seen.Add(key);
            counts.Clear();
            Dirty();
            version++;
            Plugin.Log.LogInfo($"diário: {slot} começa no dia {env.Day} com {current.Count} coisas já conhecidas ({why})");
        }

        /// <summary>Contagens baratas primeiro: as listas só crescem, então só se monta o conjunto quando alguma mudou.</summary>
        private void Scan(bool force)
        {
            if (file == null) return;
            KnowledgeSystem ks = gameSave.knowledgeSystem;
            bool changed = force;
            changed |= Count(JournalKinds.Tech, ks.unlockedTechs?.Count ?? 0);
            changed |= Count(JournalKinds.Reveal, ks.hiddenTechs?.Count ?? 0);
            changed |= Count(JournalKinds.Craft, ks.unlockedCrafts?.Count ?? 0);
            changed |= Count(JournalKinds.Building, ks.unlockedBuildings?.Count ?? 0);
            changed |= Count(JournalKinds.Town, ks.unlockedTownBuildings?.Count ?? 0);
            changed |= Count(JournalKinds.Alchemy, ks.unlockedAlchemyFormulas?.Count ?? 0);
            changed |= Count(JournalKinds.DayIcon, ks.unlockedMapDayIcons?.Count ?? 0);
            changed |= Count(JournalKinds.Tutorial, ks.unlockedTutorials?.Count ?? 0);
            changed |= Count(JournalKinds.Phrase, ks.blackListPhrases?.Count ?? 0);
            changed |= Count(JournalKinds.Perk, gameSave.perkSystemData?.activePerks?.Count ?? 0);
            changed |= Count(JournalKinds.Quest, CompletedQuestCount());
            if (!changed) return;
            if (AddNew(Snapshot(), approx: false) > 0) version++;
        }

        private void CheckSermon()
        {
            if (file == null) return;
            SermonResultData cur = MainGame.PlayerData.currentSermon;
            if (cur == null || ReferenceEquals(cur, lastSermon)) return;
            lastSermon = cur;
            EnvironmentData env = gameSave.environmentData;
            SermonNote note = SermonNote.From(cur);
            file.entries.Add(new JournalEntry { d = env.Day, t = env.TimeOfDay, k = JournalKinds.Sermon, id = cur.id, s = note });
            Dirty();
            version++;
            Plugin.Log.LogInfo($"diário: sermão {cur.id} no dia {env.Day}: {note.n} paroquianos, {(note.ok ? "sucesso" : "falha")}, {note.Money} b, fé {note.Faith}");
        }

        private bool Count(string kind, int n)
        {
            if (counts.TryGetValue(kind, out int old) && old == n) return false;
            counts[kind] = n;
            return true;
        }

        private int CompletedQuestCount()
        {
            List<QuestData> qs = gameSave.questSystemData?.questCollection?.quests;
            if (qs == null) return 0;
            int n = 0;
            foreach (QuestData q in qs) if (q != null && q.status == QuestStatus.Completed) n++;
            return n;
        }

        private int AddNew(HashSet<string> current, bool approx)
        {
            EnvironmentData env = gameSave.environmentData;
            int added = 0;
            foreach (string key in current)
            {
                if (seen.Contains(key)) continue;
                seen.Add(key);
                int c = key.IndexOf(':');
                file.entries.Add(new JournalEntry { d = env.Day, t = env.TimeOfDay, k = key.Substring(0, c), id = key.Substring(c + 1), a = approx });
                added++;
            }
            if (added > 0) Dirty();
            return added;
        }

        /// <summary>Tudo que o diário acompanha, como "tipo:id".</summary>
        private HashSet<string> Snapshot()
        {
            var set = new HashSet<string>();
            KnowledgeSystem ks = gameSave.knowledgeSystem;
            Add(set, JournalKinds.Tech, ks.unlockedTechs);
            List<TechDef> techs = GameBalance.Me?.techDefs;
            if (techs != null && ks.hiddenTechs != null)
                foreach (TechDef t in techs)
                    if (t != null && t.hiddenAtStart && !ks.hiddenTechs.Contains(t.id)) set.Add(JournalKinds.Reveal + ":" + t.id);
            Add(set, JournalKinds.Craft, ks.unlockedCrafts);
            Add(set, JournalKinds.Building, ks.unlockedBuildings);
            Add(set, JournalKinds.Town, ks.unlockedTownBuildings);
            Add(set, JournalKinds.Alchemy, ks.unlockedAlchemyFormulas);
            Add(set, JournalKinds.DayIcon, ks.unlockedMapDayIcons);
            Add(set, JournalKinds.Tutorial, ks.unlockedTutorials);
            Add(set, JournalKinds.Phrase, ks.blackListPhrases);
            List<PerkData> perks = gameSave.perkSystemData?.activePerks;
            if (perks != null)
                foreach (PerkData p in perks)
                    if (p != null && p.Definition != null && p.Definition.perkType == PerkType.Default) set.Add(JournalKinds.Perk + ":" + p.id);
            List<QuestData> qs = gameSave.questSystemData?.questCollection?.quests;
            if (qs != null)
                foreach (QuestData q in qs)
                    if (q != null && q.status == QuestStatus.Completed && q.Definition != null && q.Definition.hasPosInBalance)
                        set.Add(JournalKinds.Quest + ":" + q.id);
            return set;
        }

        private static void Add(HashSet<string> set, string kind, List<string> ids)
        {
            if (ids == null) return;
            foreach (string id in ids) if (!string.IsNullOrEmpty(id)) set.Add(kind + ":" + id);
        }

        private static string KindOf(string key)
        {
            int c = key.IndexOf(':');
            return c > 0 ? key.Substring(0, c) : key;
        }

        private double Now() => gameSave.environmentData.Day + (double)gameSave.environmentData.TimeOfDay;

        // ---------------- arquivo ----------------

        private void Dirty()
        {
            if (writeAt < 0f) writeAt = Time.unscaledTime + WriteDelay;
        }

        private void Flush()
        {
            writeAt = -1f;
            if (file == null || path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Newtonsoft.Json.JsonConvert.SerializeObject(file));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("diário: não gravou " + path + ": " + ex.Message); }
        }

        private static JournalFile Read(string p)
        {
            try
            {
                if (!File.Exists(p)) return null;
                JournalFile f = Newtonsoft.Json.JsonConvert.DeserializeObject<JournalFile>(File.ReadAllText(p));
                if (f == null) return null;
                f.baseline = f.baseline ?? new List<string>();
                f.entries = f.entries ?? new List<JournalEntry>();
                return f;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("diário: arquivo ilegível, recomeçando: " + ex.Message);
                return null;
            }
        }

        private static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());

        // ---------------- consultas ----------------

        public bool InBaseline(string kind, string id) => file != null && file.baseline.Contains(kind + ":" + id);

        /// <summary>Quando foi anotado (null = desconhecido; baseline → isBaseline).</summary>
        public JournalEntry Find(string kind, string id, out bool isBaseline)
        {
            isBaseline = false;
            if (file == null) return null;
            JournalEntry e = file.entries.Find(x => x.k == kind && x.id == id);
            if (e != null) return e;
            isBaseline = InBaseline(kind, id);
            return null;
        }

        public List<JournalEntry> Sermons() => file == null ? new List<JournalEntry>() : file.entries.Where(e => e.k == JournalKinds.Sermon && e.s != null).ToList();

        public List<JournalEntry> OnDay(int day) => file == null ? new List<JournalEntry>() : file.entries.Where(e => e.d == day).ToList();

        /// <summary>Dias com anotação, do mais antigo ao mais novo.</summary>
        public List<int> Days() => file == null ? new List<int>() : file.entries.Select(e => e.d).Distinct().OrderBy(d => d).ToList();

        public Dictionary<string, int> BaselineCounts()
        {
            var r = new Dictionary<string, int>();
            if (file == null) return r;
            foreach (string key in file.baseline)
            {
                string k = KindOf(key);
                r[k] = r.TryGetValue(k, out int n) ? n + 1 : 1;
            }
            return r;
        }

        /// <summary>Para os cenários: força uma varredura agora e resume o estado.</summary>
        internal string Describe()
        {
            Scan(true);
            CheckSermon();
            return file == null ? "sem diário" : $"slot={file.slot} início=dia {file.startDay} baseline={file.baseline.Count} anotações={file.entries.Count} sermões={Sermons().Count} arquivo={path}";
        }
    }
}
