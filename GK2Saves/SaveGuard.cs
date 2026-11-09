using LazyBearTechnology;

namespace GK2Saves
{
    /// <summary>
    /// Decide se dá para salvar agora: só com o jogo "livre" (controle do jogador intacto, exceto pela própria UI de pausa,
    /// sem luta, sono, carga na cabeça e no estado livre da máquina de estados). Devolve a chave do motivo ou null.
    /// </summary>
    internal static class SaveGuard
    {
        public static string BlockReason()
        {
            MainGame game = MainGame.Instance;
            if (game == null || game.gameState != MainGame.GameState.InGame || game.GameSave == null || game.SaveSlotData == null) return "block_other";
            PlayerController pc = MainGame.PlayerController;
            PlayerData pd = MainGame.PlayerData;
            if (pc == null || pd == null) return "block_other";
            if (LazyUI.Get<UILoadingOverlay>()?.IsShown == true) return "block_scene";
            FightingGameController fight = LazySingleton<FightingGameController>.Instance;
            if (fight != null && fight.CurrentFightState != FightState.Disabled) return "block_fight";
            EnergySystem energy = pd.energySystem;
            if (energy != null && (energy.IsSleeping || energy.IsInTransitionBetweenSleep)) return "block_sleep";
            if (!pc.IsControlEnabledByType(TakenControlType.BySleep)) return "block_sleep";
            if (!pc.IsControlEnabledByType(TakenControlType.BySceneLoading) || !pc.IsControlEnabledByType(TakenControlType.ByTeleport)) return "block_scene";
            if (!pc.IsControlEnabledByType(TakenControlType.ByFlow) || !pc.IsControlEnabledByType(TakenControlType.ByCinematics)) return "block_flow";
            if (!pc.IsControlEnabledByType(TakenControlType.ByAttack)) return "block_fight";
            if (!pc.IsControlEnabledByType(TakenControlType.ByDeath)) return "block_other";
            if (!pc.IsControlsEnabledExcept(TakenControlType.ByUI)) return "block_busy";
            if (pd.HasOverheadItem) return "block_carry";
            if (!(pc.Ssm?.CurState is FreePlayerState)) return "block_busy";
            return null;
        }
    }
}
