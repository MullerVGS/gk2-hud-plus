using UnityEngine;

// Põe o jogador para dormir no green (gera sleep_start, save_write, new_day e sleep_end na telemetria). Muda o save de teste.
public static class Snippet
{
    public static object Run()
    {
        EnergySystem e = MainGame.PlayerData.energySystem;
        if (e.IsSleeping) return "já dormindo";
        // Com energia cheia e sem falta de sono o jogo recusa: sleepWithMaxEnergy força.
        e.StartSleeping(sleepWithMaxEnergy: true);
        Debug.Log("[cenario] dormir: StartSleeping");
        return $"dormindo={e.IsSleeping} acordado={e.timeWithoutSleep:0.00} d";
    }
}
