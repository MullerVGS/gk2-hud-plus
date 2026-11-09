using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Leva o jogador (save de teste) ao quintal de casa ao meio-dia: fundo claro atrás do HUD, para prints do GK2Hud.
public static class Snippet
{
    const string WGO = "tp_RT_home_enter";
    const float TIME = 0.5f;

    public static IEnumerator Run()
    {
        if (MainGame.PlayerData.CurrentWorldZoneData?.id != "yard")
        {
            PlayerController.Teleport(new WgoTeleportData(WGO));
            yield return new WaitForSecondsRealtime(7f);
        }
        EnvironmentEngine.Instance.SetTimeOfDay(TIME);
        yield return new WaitForSecondsRealtime(3f);
        yield return $"zona {MainGame.PlayerData.CurrentWorldZoneData?.id} · hora {MainGame.Instance.GameSave.environmentData.TimeOfDay:0.000}";
    }
}
