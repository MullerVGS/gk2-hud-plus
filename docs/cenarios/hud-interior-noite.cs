using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Leva o jogador (save de teste) para dentro de casa, perto da cama, às 22:30: fundo escuro atrás do HUD, para prints do GK2Hud.
public static class Snippet
{
    const string WGO = "bed";
    const float TIME = 0.9375f;

    public static IEnumerator Run()
    {
        if (MainGame.PlayerData.CurrentWorldZoneData?.id != "home")
        {
            PlayerController.Teleport(new WgoTeleportData(WGO));
            yield return new WaitForSecondsRealtime(7f);
        }
        EnvironmentEngine.Instance.SetTimeOfDay(TIME);
        yield return new WaitForSecondsRealtime(3f);
        yield return $"zona {MainGame.PlayerData.CurrentWorldZoneData?.id} · hora {MainGame.Instance.GameSave.environmentData.TimeOfDay:0.000}";
    }
}
