using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using LazyBearTechnology;
using UnityEngine;

namespace GK2PerkFix
{
    /// <summary>
    /// Correção de uso único: desfaz a compra de um nível de talento de um zumbi (devolve os pontos e a caveira) quando o
    /// save com esse zumbi estiver carregado, e marca <c>Done</c> no .cfg para nunca repetir.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.perkfix";
        public const string Name = "GK2 Perk Fix";
        public const string Version = "0.1.0";

        private ConfigEntry<string> zombieId;
        private ConfigEntry<string> levelUpId;
        private ConfigEntry<bool> done;
        private float nextCheck;

        private void Awake()
        {
            zombieId = Config.Bind("Fix", "ZombieId", "4148223f-bd1b-4d88-a30b-a8acd58320b2", "UniqueId do zumbi (Addy Gnaw).");
            levelUpId = Config.Bind("Fix", "LevelUpId", "z_building_lvl_up_1_1", "Nível de talento a desfazer.");
            done = Config.Bind("Fix", "Done", false, "true depois de aplicado; o mod não faz mais nada.");
            Logger.LogInfo($"{Name} {Version} carregado (done={done.Value})");
        }

        private void Update()
        {
            if (done.Value || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 1f;
            MainGame game = MainGame.Instance;
            if (game == null || game.gameState != MainGame.GameState.InGame || game.GameSave == null) return;
            try { TryApply(); }
            catch (Exception e) { Logger.LogError(e); done.Value = true; }
        }

        private void TryApply()
        {
            ZombieWgoData zombie = null;
            foreach (SGuid id in MainGame.ZombieSystemData.zombieOnSceneWgoIds)
            {
                ZombieWgoData z = MainGame.ZombieSystemData.GetZombie(id);
                if (z != null && z.UniqueId.Id == zombieId.Value) { zombie = z; break; }
            }
            if (zombie == null) return;

            TalentLevelUpDef def = GameBalance.Me.GetData<TalentLevelUpDef>(levelUpId.Value);
            ZombieTalentData branch = def == null ? null : zombie.GetTalentBranch(def.talentId);
            if (branch == null || !branch.studiedLevelUps.Contains(def.id))
            {
                Logger.LogInfo($"{levelUpId.Value} não está em {zombieId.Value}; nada a fazer");
                done.Value = true;
                return;
            }

            branch.studiedLevelUps.Remove(def.id);
            if (!zombie.disabledTalentLevelUps.Remove(def.id)) branch.curTalentValue -= def.talentValueAdd;
            if (!string.IsNullOrEmpty(def.linkedPerk)) Logger.LogWarning("perk ligado não removido: " + def.linkedPerk);
            zombie.techRed += def.techRed;
            zombie.techGreen += def.techGreen;
            zombie.techBlue += def.techBlue;
            done.Value = true;

            RaiseLevelUpsChanged();
            string name = LLBase.L(zombie.Name);
            Logger.LogInfo($"{def.id} desfeito em {name}: talento {def.talentId}={branch.curTalentValue}, tech R{zombie.techRed} G{zombie.techGreen} B{zombie.techBlue}, perks {zombie.GetUsedPerksCount()}/{zombie.RedSkulls}");
            bool pt = LLBase.CurrentLang.StartsWith("pt");
            Notify(pt ? $"{name}: perk desfeito. Salve o jogo para manter." : $"{name}: perk refunded. Save the game to keep it.");
        }

        private void RaiseLevelUpsChanged()
        {
            try
            {
                FieldInfo f = typeof(ZombieWgoData).GetField("OnTalentLevelUpPurchased", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                (f?.GetValue(null) as Action)?.Invoke();
            }
            catch (Exception e) { Logger.LogWarning("refresh: " + e.Message); }
        }

        private void Notify(string text)
        {
            try { LazySingleton<UINotificator>.Instance?.ShowSimpleTextNotification(text); }
            catch (Exception e) { Logger.LogWarning("aviso: " + e.Message); }
        }
    }
}
