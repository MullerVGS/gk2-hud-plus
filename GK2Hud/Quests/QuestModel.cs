using System.Collections.Generic;
using System.Linq;
using GK2Hud.Journal;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.Quests
{
    /// <summary>
    /// Leitura das quests para a árvore: um nó da árvore é uma sequência de quests na mesma posição (o jogo esconde as
    /// concluídas quando a seguinte começa), cada uma com o texto que o jogo escreve ao concluir. Só entra o que o
    /// jogador já viveu (em andamento ou concluída). Onde um NPC está agora vem da posição do objeto dele no mundo.
    /// </summary>
    internal static class QuestModel
    {
        private static QuestCollectionData Collection =>
            MainGame.Instance != null && MainGame.Instance.GameSave != null ? MainGame.Instance.GameSave.questSystemData?.questCollection : null;

        public static QuestData Get(string id)
        {
            QuestCollectionData c = Collection;
            if (c?.questsCache == null || string.IsNullOrEmpty(id)) return null;
            return c.questsCache.TryGetValue(id, out QuestData q) ? q : null;
        }

        public static bool Lived(QuestData q) =>
            q != null && q.Definition != null && q.Definition.hasPosInBalance && (q.status == QuestStatus.Completed || q.status == QuestStatus.InProgress);

        public static string Title(QuestData q) => q == null ? null : Names.Text(q.id);

        /// <summary>O texto do jogo para a quest: o de conclusão quando existe, senão o do objetivo.</summary>
        public static string Text(QuestData q)
        {
            if (q == null) return null;
            string open = Names.Text("quest_open_" + q.id + "_d");
            return q.status == QuestStatus.Completed ? Names.Text("quest_closed_" + q.id + "_d") ?? open : open;
        }

        public static string NpcName(string npcId) => Names.Text(npcId);

        public static Sprite Portrait(string npcId)
        {
            if (string.IsNullOrEmpty(npcId)) return null;
            WGODef def = GameBalance.Me.GetDataOrNull<WGODef>(npcId);
            if (def == null || string.IsNullOrEmpty(def.portrait)) return null;
            EasySpritesCollection sprites = LazySingletonSO<EasySpritesCollection>.Instance;
            if (!sprites.HasSprite(def.portrait)) return null;
            if (!portraits.TryGetValue(def.portrait, out Sprite s) || s == null)
            {
                s = sprites.GetSprite(def.portrait);   // o atlas devolve um clone novo a cada chamada: guardar
                portraits[def.portrait] = s;
            }
            return s;
        }

        private static readonly Dictionary<string, Sprite> portraits = new Dictionary<string, Sprite>();
        private static Material portraitMaterial;

        /// <summary>
        /// Monta o retrato num Image próprio como o widget nativo de NPC: material que troca o azul-chave por
        /// transparente e tamanho nativo (1 unidade por pixel de arte). O sprite tem 96×96 com o desenho no meio: o
        /// espaço no layout é só o do desenho (o recorte do atlas), com a imagem inteira centrada nele.
        /// </summary>
        public static void FitPortrait(UnityEngine.UI.LayoutElement holder, UnityEngine.UI.Image img)
        {
            Sprite s = img.sprite;
            if (s == null) return;
            float k = 50f / s.pixelsPerUnit;
            Vector2 full = s.rect.size * k, drawn = full, offset = Vector2.zero;
            if (s.packed && s.packingMode == SpritePackingMode.Rectangle)
            {
                Rect tr = s.textureRect;
                drawn = tr.size * k;
                Vector2 center = (s.textureRectOffset + tr.size * 0.5f) * k;
                offset = new Vector2(Mathf.Round(full.x * 0.5f - center.x), Mathf.Round(full.y * 0.5f - center.y));
            }
            holder.minWidth = holder.preferredWidth = Mathf.Ceil(drawn.x * 0.5f) * 2f;
            holder.minHeight = holder.preferredHeight = Mathf.Ceil(drawn.y * 0.5f) * 2f + 2f;
            img.rectTransform.sizeDelta = full;
            img.rectTransform.anchoredPosition = offset;
            img.type = UnityEngine.UI.Image.Type.Simple;
            img.preserveAspect = true;
            img.raycastTarget = false;
            Material m = PortraitMaterial();
            if (m != null) img.material = m;
        }

        private static Material PortraitMaterial()
        {
            if (portraitMaterial != null) return portraitMaterial;
            UINpcWidget npc = GUIElements.Instance != null ? GUIElements.Instance.NpcWidget : null;
            var src = npc != null ? AccessTools.Field(typeof(UINpcWidget), "portrait").GetValue(npc) as UnityEngine.UI.Image : null;
            if (src == null || src.material == null) return null;
            object key = AccessTools.Field(typeof(UINpcWidget), "toReplace").GetValue(npc);
            portraitMaterial = new Material(src.material) { hideFlags = HideFlags.HideAndDontSave };
            portraitMaterial.SetColor("_Color", key is Color c ? c : new Color(1f, 1f, 1f, 0f));
            return portraitMaterial;
        }

        /// <summary>Quests vividas na mesma posição da árvore, da mais antiga para a atual.</summary>
        public static List<QuestData> Chain(QuestData q)
        {
            var list = new List<QuestData>();
            QuestCollectionData c = Collection;
            if (c == null || q?.Definition == null) return list;
            Vector2Int pos = q.Definition.TreePos;
            JournalTracker j = JournalTracker.Current;
            var order = new List<KeyValuePair<double, QuestData>>();
            for (int i = 0; i < c.quests.Count; i++)
            {
                QuestData o = c.quests[i];
                if (!Lived(o) || o.Definition.TreePos != pos) continue;
                // Concluídas na ordem do balanço (a do roteiro), com a data do diário quando existe; em andamento por último.
                double key = i;
                if (o.status == QuestStatus.InProgress) key += 2e6;
                else if (j != null && j.Find(JournalKinds.Quest, o.id, out _) is JournalEntry e) key = 1e6 + e.When;
                order.Add(new KeyValuePair<double, QuestData>(key, o));
            }
            list.AddRange(order.OrderBy(p => p.Key).Select(p => p.Value));
            if (!list.Contains(q)) list.Add(q);
            return list;
        }

        /// <summary>Quests vividas ligadas a um NPC, das de baixo da árvore (mais recentes) para as de cima.</summary>
        public static List<QuestData> OfNpc(string npcId)
        {
            QuestCollectionData c = Collection;
            if (c == null || string.IsNullOrEmpty(npcId)) return new List<QuestData>();
            return c.quests.Select((q, i) => new { q, i }).Where(x => Lived(x.q) && x.q.Definition.wgoNpcId == npcId)
                .OrderByDescending(x => x.q.Definition.TreePos.y).ThenByDescending(x => x.i).Select(x => x.q).ToList();
        }

        // ---------------- fio ----------------

        /// <summary>A quest que representa um nó vivido: a em andamento, senão a última concluída da posição.</summary>
        private static QuestData NodeAt(Vector2Int pos)
        {
            QuestCollectionData c = Collection;
            if (c == null) return null;
            QuestData best = null;
            foreach (QuestData o in c.quests)
            {
                if (!Lived(o) || o.Definition.TreePos != pos) continue;
                if (o.status == QuestStatus.InProgress) return o;
                best = o;
            }
            return best;
        }

        private static IEnumerable<Vector2Int> Neighbors(Vector2Int pos, bool up)
        {
            QuestCollectionData c = Collection;
            if (c == null) yield break;
            var seen = new HashSet<Vector2Int> { pos };
            foreach (QuestData o in c.quests)
            {
                if (o?.Definition == null || !o.Definition.hasPosInBalance || o.Definition.TreePos != pos) continue;
                foreach (QuestDef d in up ? o.Definition.parentDefinitionList : o.Definition.childDefinitionList)
                    if (d != null && d.hasPosInBalance && seen.Add(d.TreePos)) yield return d.TreePos;
            }
        }

        /// <summary>O fio de um nó: ele, tudo que veio antes (pais dos pais) e tudo que sai dele (filhos dos filhos).</summary>
        public static HashSet<Vector2Int> Thread(QuestData q)
        {
            var set = new HashSet<Vector2Int>();
            if (q?.Definition == null) return set;
            Vector2Int start = q.Definition.TreePos;
            set.Add(start);
            foreach (bool up in new[] { true, false })
            {
                var stack = new Stack<Vector2Int>();
                stack.Push(start);
                while (stack.Count > 0)
                    foreach (Vector2Int n in Neighbors(stack.Pop(), up))
                        if (set.Add(n)) stack.Push(n);
            }
            return set;
        }

        /// <summary>Os nós vividos que levaram a este, do mais antigo ao mais recente (um pai por passo), sem o próprio.</summary>
        public static List<QuestData> Path(QuestData q, int max)
        {
            var path = new List<QuestData>();
            if (q?.Definition == null) return path;
            var seen = new HashSet<Vector2Int> { q.Definition.TreePos };
            Vector2Int pos = q.Definition.TreePos;
            while (path.Count < max)
            {
                QuestData parent = null;
                foreach (Vector2Int p in Neighbors(pos, true))
                {
                    if (seen.Contains(p)) continue;
                    QuestData n = NodeAt(p);
                    if (n != null && n.status == QuestStatus.Completed) { parent = n; break; }
                }
                if (parent == null) break;
                seen.Add(parent.Definition.TreePos);
                path.Insert(0, parent);
                pos = parent.Definition.TreePos;
            }
            return path;
        }

        /// <summary>Os nós que saem deste e que o jogador já vê na árvore.</summary>
        public static List<QuestData> Next(QuestData q)
        {
            var list = new List<QuestData>();
            if (q?.Definition == null) return list;
            foreach (Vector2Int p in Neighbors(q.Definition.TreePos, false))
            {
                QuestData n = NodeAt(p);
                if (n != null && !n.isHidden && !n.isUnknown) list.Add(n);
            }
            return list;
        }

        /// <summary>A pose do nó: o ícone da quest na árvore (cada passo da história tem a sua).</summary>
        public static Sprite Pose(QuestData q)
        {
            string id = q?.Definition?.iconId;
            if (string.IsNullOrEmpty(id)) return null;
            EasySpritesCollection sprites = LazySingletonSO<EasySpritesCollection>.Instance;
            if (!sprites.HasSprite(id)) return null;
            if (!portraits.TryGetValue(id, out Sprite s) || s == null)
            {
                s = sprites.GetSprite(id);
                portraits[id] = s;
            }
            return s;
        }

        // ---------------- quando ----------------

        public struct Moment
        {
            public bool Known;       // o diário viu acontecer
            public bool Approx;      // notado ao carregar
            public int Day;
            public float Tod;
            public bool BeforeJournal;
            public int JournalStart;
        }

        public static Moment When(QuestData q, bool finished)
        {
            var m = new Moment();
            JournalTracker j = JournalTracker.Current;
            if (j == null || !j.Active || q == null) return m;
            JournalEntry e = j.Find(finished ? JournalKinds.Quest : JournalKinds.QStart, q.id, out bool baseline);
            if (e != null) { m.Known = true; m.Approx = e.a; m.Day = e.d; m.Tod = e.t; }
            else if (baseline) { m.BeforeJournal = true; m.JournalStart = j.StartDay; }
            return m;
        }

        // ---------------- onde ----------------

        private static readonly AccessTools.FieldRef<MapPageWidget, RectTransform> MapRef = AccessTools.FieldRefAccess<MapPageWidget, RectTransform>("mapRect");
        private static WorldZone[] zones;
        private static float zonesAt = -100f;
        private static readonly Dictionary<string, WgoData> npcData = new Dictionary<string, WgoData>();
        private static GameSave npcSave;
        private static readonly Vector3[] Corners = new Vector3[4];

        public static void Reset()
        {
            zones = null;
            zonesAt = -100f;
            npcData.Clear();
            npcSave = null;
            foreach (Sprite s in portraits.Values) if (s != null) Object.Destroy(s);
            if (portraitMaterial != null) Object.Destroy(portraitMaterial);
            portraitMaterial = null;
            portraits.Clear();
        }

        private static WgoData NpcData(string npcId)
        {
            GameSave save = MainGame.Instance.GameSave;
            if (save != npcSave) { npcData.Clear(); npcSave = save; }
            if (npcData.TryGetValue(npcId, out WgoData w) && w != null && !w.isRemovingFromData) return w;
            w = null;
            foreach (GameSceneData scene in save.WorldData.gameSceneDataList)
            {
                if (scene?.wgoDataList == null) continue;
                foreach (WgoData d in scene.wgoDataList)
                    if (d != null && d.id == npcId) { w = d; break; }
                if (w != null) break;
            }
            npcData[npcId] = w;
            return w;
        }

        /// <summary>
        /// Nome do lugar onde o NPC está agora, ou null. Interior: a menor zona nomeada que contém a posição, só se o
        /// jogador já esteve nela. Céu aberto: a região do mapa que contém o ponto, só se já está no mapa do jogador.
        /// </summary>
        public static string Where(string npcId)
        {
            if (string.IsNullOrEmpty(npcId) || MainGame.Instance == null || MainGame.Instance.GameSave == null) return null;
            WgoData w = NpcData(npcId);
            if (w == null || w.IsHidden) return null;
            Vector3 pos = w.Position;
            KnowledgeSystem ks = MainGame.Instance.GameSave.knowledgeSystem;
            if (zones == null || Time.unscaledTime - zonesAt > 5f)
            {
                zones = Object.FindObjectsByType<WorldZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                zonesAt = Time.unscaledTime;
            }
            string best = null;
            float bestArea = float.MaxValue;
            foreach (WorldZone z in zones)
            {
                if (z == null || z.ZoneCollider == null) continue;
                Bounds b = z.ZoneCollider.bounds;
                if (b.size.x < 2f || b.size.z < 2f) continue;
                if (Mathf.Abs(pos.x - b.center.x) > b.extents.x || Mathf.Abs(pos.z - b.center.z) > b.extents.z) continue;
                float area = b.size.x * b.size.z;
                if (area >= bestArea || !ks.visitedWorldZones.Contains(z.Id)) continue;
                if (z.Data?.Definition != null && z.Data.Definition.displayType == WorldZoneDef.DisplayType.Hidden) continue;
                string name = Names.Text("wz_" + z.Id);
                if (name == null) continue;
                best = name;
                bestArea = area;
            }
            return best ?? MapRegion(pos, ks);
        }

        private static string MapRegion(Vector3 pos, KnowledgeSystem ks)
        {
            GUIElements gui = GUIElements.Instance;
            CharacterWindow window = LazyUI.GetWindow<CharacterWindow>();
            if (gui == null || window == null) return null;
            MapPageWidget page = window.GetComponentInChildren<MapPageWidget>(true);
            RectTransform map = page != null ? MapRef(page) : null;
            if (map == null) return null;
            Vector3 min = gui.WorldMin.position, max = gui.WorldMax.position;
            if (pos.x < Mathf.Min(min.x, max.x) || pos.x > Mathf.Max(min.x, max.x) || pos.z < Mathf.Min(min.z, max.z) || pos.z > Mathf.Max(min.z, max.z)) return null;
            Rect r = map.rect;
            Vector3 world = map.TransformPoint(new Vector3(r.xMin + Mathf.InverseLerp(min.x, max.x, pos.x) * r.width, r.yMin + Mathf.InverseLerp(min.z, max.z, pos.z) * r.height, 0f));
            string best = null;
            float bestArea = float.MaxValue;
            foreach (UIMapZone zone in map.GetComponentsInChildren<UIMapZone>(true))
            {
                if (!ks.knownMapZones.Contains(zone.Id)) continue;
                RectTransform clouds = zone.transform.Find("Clouds") as RectTransform;
                if (clouds == null) continue;
                Vector2 local = clouds.InverseTransformPoint(world);
                if (!clouds.rect.Contains(local)) continue;
                float area = clouds.rect.width * clouds.rect.height;
                if (area >= bestArea) continue;
                string name = Names.Text("wz_" + zone.Id);
                if (name == null) continue;
                best = name;
                bestArea = area;
            }
            return best;
        }
    }
}
