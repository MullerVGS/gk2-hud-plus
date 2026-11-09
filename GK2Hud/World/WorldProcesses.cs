using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.World
{
    /// <summary>
    /// Leitura (só leitura) dos processos em andamento no mundo e das contas de tempo. Fontes no decomp:
    /// - crafts: CraftSystem.CustomUpdate (CraftSystem.cs:14) chama CraftComponent.Update(deltaTime) para os autocrafts;
    ///   um tick a cada autoCraftTickDuration s (CraftComponent.cs:756-783), 5 s se ≤ 0. Craft manual avança por
    ///   UpdateManual (golpes do jogador, CraftComponent.cs:786) ou pelo zumbi (ZombieCraftActivity.cs:83-150: um golpe
    ///   por segundo × (1 + zombieSpeedItemModificators), a cada zombie_craft_sub_ticks_count golpes soma
    ///   clamp(maestria/talentLock, 0, max_cells_per_one_hit) ticks em craft comum).
    /// - canteiro: o crescimento é um autocraft com CraftParamsType.GardenGrowing (CraftComponent.UpdateGardenGrowingCraft,
    ///   CraftComponent.cs:1387): por tick avança 1 célula (falha ou sucesso) ou clamp(maestria/trava, 0..3).
    /// </summary>
    internal static class WorldProcesses
    {
        private static readonly AccessTools.FieldRef<CraftComponent, float> TickDuration =
            AccessTools.FieldRefAccess<CraftComponent, float>("autoCraftTickDuration");
        private static readonly AccessTools.FieldRef<CraftComponent, float> TickTime =
            AccessTools.FieldRefAccess<CraftComponent, float>("currentAutoCraftTickTime");

        /// <summary>Segundos por tick de autocraft como o CraftComponent usa (5 s quando o WGODef não define).</summary>
        public static float SecondsPerTick(CraftComponent cc)
        {
            float t = TickDuration(cc);
            return t > 0f ? t : 5f;
        }

        public static float TickElapsed(CraftComponent cc) => TickTime(cc);

        /// <summary>Wgos carregados na cena com algo em andamento (crafts ativos registrados no CraftSystem).</summary>
        public static IEnumerable<KeyValuePair<WgoData, Wgo>> WgosWithProcess()
        {
            CraftSystemData csd = MainGame.Instance?.GameSave?.craftSystemData;
            if (csd?.activeCrafts == null) yield break;
            var seen = new HashSet<WgoData>();
            foreach (CraftComponent cc in csd.activeCrafts)
            {
                if (cc == null || !(cc.CraftableObject is WgoData w) || !seen.Add(w)) continue;
                if (!HasCraftProcess(cc)) continue;
                Wgo view = GameScene.GetWgoViewGlobal(w.UniqueId);
                if (view == null || !view.isActiveAndEnabled || !view.HasData || view.Data != w) continue;
                yield return new KeyValuePair<WgoData, Wgo>(w, view);
            }
        }

        /// <summary>
        /// Itens no chão com temporizador (corpos que somem): DropData.AutoDestroyTimer &gt; 0. Contagem regressiva em
        /// DropSystem.CustomUpdate (DropSystem.cs:137-160, delta de jogo a cada 0,2 s, MainGame.cs:204): ao zerar, o corpo
        /// some e vira "body_drop_grave_object". Só corpos têm timer (DropData.cs:196).
        /// </summary>
        public static IEnumerable<KeyValuePair<DropData, Component>> DropsWithProcess()
        {
            GameSceneManager gsm = LazySingleton<GameSceneManager>.Instance;
            if (gsm?.LoadedGameScenes == null) yield break;
            foreach (GameScene scene in gsm.LoadedGameScenes)
            {
                List<DropData> drops = scene?.GameSceneData?.droppedItems;
                if (drops == null) continue;
                foreach (DropData d in drops)
                {
                    if (!IsRotting(d)) continue;
                    if (scene.TryGetDropView(d.Item, out DropView view) && view != null && view.isActiveAndEnabled)
                        yield return new KeyValuePair<DropData, Component>(d, view);
                }
            }
        }

        /// <summary>
        /// Corpo com timer, que não está saindo e não é "nunca some". A pausa por estar visível (WhenVisibleOnScreen) não
        /// tira da lista: na tela ele está sempre pausado, e o tooltip diz isso.
        /// </summary>
        public static bool IsRotting(DropData d) =>
            d != null && d.Item != null && d.AutoDestroyTimer > 0f && !d.IsRemoving
            && !(d.CanNotBeAutoDestroyed.HasFlag(CanNotBeAutoDestroyedReason.Never) && d.CanNotBeAutoDestroyed.GetFlag(CanNotBeAutoDestroyedReason.Never));

        /// <summary>Há um craft para mostrar: fila não vazia e elemento atual visível (o jogo esconde isHidden na bolha).</summary>
        public static bool HasCraftProcess(CraftComponent cc)
        {
            if (cc == null || !cc.HasCraftsInQueue) return false;
            CraftElementBase el = cc.CurrentCraftElement;
            if (el == null || el.Def == null) return false;
            return true;
        }
    }
}
