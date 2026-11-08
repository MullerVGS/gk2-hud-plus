using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>NPCs e zumbis das cenas carregadas: o que fazem, para onde vão, quando é a próxima ação.</summary>
    internal static class PeopleSection
    {
        private const int MaxRows = 8;

        private static readonly AccessTools.FieldRef<MovementComponent, Vector3> EndPos =
            AccessTools.FieldRefAccess<MovementComponent, Vector3>("endPos");
        private static readonly AccessTools.FieldRef<MovementComponent, NPCPointOfInterestData> TargetPoi =
            AccessTools.FieldRefAccess<MovementComponent, NPCPointOfInterestData>("targetPointOfInterest");

        private struct Row
        {
            public float dist;
            public string[] cells;
        }

        public static void WriteNpcs(Panel p, MainGame game, Vector3 playerPos)
        {
            List<Row> rows = new List<Row>();
            int loaded = 0;
            Dictionary<System.Guid, NPCLifeSimulatorActionData> actions = new Dictionary<System.Guid, NPCLifeSimulatorActionData>();
            if (game.GameSave.npcLifeSimulatorData?.ActionsData != null)
                foreach (NPCLifeSimulatorActionData a in game.GameSave.npcLifeSimulatorData.ActionsData)
                    if (a?.WgoId != null) actions[a.WgoId.Guid] = a;

            foreach (GameScene gs in LazySingleton<GameSceneManager>.Instance.LoadedGameScenes)
            {
                if (gs?.GameSceneData == null) continue;
                foreach (WgoData w in gs.GameSceneData.wgoDataList)
                {
                    if (w == null || w is ZombieWgoData || !IsNpc(w)) continue;
                    Wgo view = GameScene.GetWgoViewGlobal(w.UniqueId);
                    if (view == null) continue;
                    loaded++;
                    if (!view.gameObject.activeSelf) continue;
                    float dist = Vector3.Distance(Flat(w.Position), Flat(playerPos));
                    string rep = !string.IsNullOrEmpty(w.Definition.repResName) ? $"rep {MainGame.PlayerData.GetNPCRep(w.Definition.repResName)}" : "";
                    string next = actions.TryGetValue(w.UniqueId.Guid, out NPCLifeSimulatorActionData act)
                        ? Panel.Dim($"{(act.ActionType == NPCLifeSimulatorActionType.GoHome ? "vai p/ casa" : "muda de ponto")} em {TimeSection.Real(act.RemainingTimeToAction)}")
                        : "";
                    rows.Add(new Row { dist = dist, cells = new[] { NpcName(w), $"{dist:0}m", Activity(w), rep, next } });
                }
            }
            if (loaded == 0) return;
            List<Row> visible = rows.OrderBy(r => r.dist).ToList();
            p.Section("NPCs", $"{visible.Count} visíveis · {loaded} carregados");
            p.Table(visible.Take(MaxRows).Select(r => r.cells).ToList());
            if (visible.Count > MaxRows) p.Line(Panel.Dim($"  … +{visible.Count - MaxRows} mais longe"));
        }

        public static void WriteZombies(Panel p, MainGame game, Vector3 playerPos)
        {
            ZombieSystemData zs = MainGame.ZombieSystemData;
            if (zs == null || zs.zombieOnSceneWgoIds.Count == 0 && zs.zombieDrops.Count == 0) return;
            List<Row> rows = new List<Row>();
            Dictionary<ZombieType, int> byType = new Dictionary<ZombieType, int>();
            int loaded = 0;
            foreach (SGuid id in zs.zombieOnSceneWgoIds)
            {
                ZombieWgoData z = zs.GetZombie(id);
                if (z == null) continue;
                byType.TryGetValue(z.ZombieType, out int n);
                byType[z.ZombieType] = n + 1;
                if (GameScene.GetWgoViewGlobal(z.UniqueId) == null) continue;
                loaded++;
                float dist = Vector3.Distance(Flat(z.Position), Flat(playerPos));
                string name = string.IsNullOrEmpty(z.Name) ? "zumbi" : Names.Get(z.Name);
                string station = z.AttachedWgoData != null ? Names.Get(z.AttachedWgoData.id) : Panel.Dim("sem posto");
                string skulls = $"{Panel.Col("#e8e8e8", z.WhiteSkulls.ToString())}/{Panel.Col("#ff7070", z.RedSkulls.ToString())}";
                rows.Add(new Row { dist = dist, cells = new[] { name, TypeName(z.ZombieType), station, State(z), skulls, $"{dist:0}m" } });
            }
            string types = string.Join(" · ", byType.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {TypeName(kv.Key)}"));
            p.Section("Zumbis", $"{zs.zombieOnSceneWgoIds.Count} no mundo · {loaded} carregados · {zs.zombieDrops.Count} guardados");
            if (types.Length > 0) p.Line(" " + Panel.Dim(types));
            p.Table(rows.OrderBy(r => r.dist).Take(MaxRows).Select(r => r.cells).ToList());
        }

        private static bool IsNpc(WgoData w)
        {
            WGODef def = w.Definition;
            return def != null && (w.id.StartsWith("npc_") || !string.IsNullOrEmpty(def.repResName) || !string.IsNullOrEmpty(def.npcLifeSimGroup));
        }

        private static string Activity(WgoData w)
        {
            MovementComponent mc = w.MovementComponent;
            if (mc != null && mc.IsMoving)
            {
                NPCPointOfInterestData poi = TargetPoi(mc);
                if (poi != null) return "→ " + Short(poi.Id);
                Vector3 end = EndPos(mc);
                return $"→ {Vector3.Distance(Flat(w.Position), Flat(end)):0}m";
            }
            if (!string.IsNullOrEmpty(w.occupiedPointOfInterest)) return "em " + Short(w.occupiedPointOfInterest);
            return Panel.Dim("parado");
        }

        private static string State(ZombieWgoData z)
        {
            string warn = z.ShouldShowNoStorageIcon ? Panel.Warn(" SEM ESPAÇO") : "";
            switch (z.ZombieType)
            {
                case ZombieType.Caretaker: return z.CaretakerState + warn;
                case ZombieType.Gardener: return z.GardenerState + warn;
                case ZombieType.Quarryman: return z.QuarrymanState + warn;
                case ZombieType.Crafter:
                case ZombieType.ConveyorCrafter:
                    return (z.WorkerActivity != null ? "trabalhando" : Panel.Dim("ocioso")) + warn;
                default:
                    MovementComponent mc = z.MovementComponent;
                    return (mc != null && mc.IsMoving ? "andando" : Panel.Dim("parado")) + warn;
            }
        }

        private static string TypeName(ZombieType t)
        {
            switch (t)
            {
                case ZombieType.Free: return "livre";
                case ZombieType.Crafter: return "artesão";
                case ZombieType.Caretaker: return "zelador";
                case ZombieType.ConveyorCrafter: return "esteira";
                case ZombieType.Worker: return "operário";
                case ZombieType.Porter: return "carregador";
                case ZombieType.Gardener: return "jardineiro";
                case ZombieType.ConveyorTransporter: return "transportador";
                case ZombieType.Fighter: return "lutador";
                case ZombieType.Quarryman: return "pedreiro";
                default: return t.ToString();
            }
        }

        private static string Short(string id)
        {
            if (id == null) return "";
            if (id.StartsWith("npc_sim_")) id = id.Substring(8);
            return id.Length > 26 ? id.Substring(0, 25) + "…" : id;
        }

        /// <summary>Nome localizado; NPC genérico sem texto (npc_villager_05) vira "villager_05".</summary>
        private static string NpcName(WgoData w)
        {
            string name = Names.Get(w.id);
            return name == w.id && name.StartsWith("npc_") ? Panel.Dim(name.Substring(4)) : name;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
