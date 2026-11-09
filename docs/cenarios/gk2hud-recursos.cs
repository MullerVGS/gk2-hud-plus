using System.Collections;
using UnityEngine;

// GK2Hud: põe energia e insanidade do save de teste em valores intermediários (ENERGIA, INSANIDADE) para prints dos números na barra.
public static class Snippet
{
    const float ENERGIA = 63f;
    const float INSANIDADE = 27f;

    public static IEnumerator Run()
    {
        PlayerData pd = MainGame.PlayerData;
        pd.SetRes("energy", ENERGIA);
        pd.SetRes("insanity", INSANIDADE);
        yield return new WaitForSecondsRealtime(0.6f);
        yield return $"energia {pd.GetRes("energy")} · insanidade {pd.GetRes("insanity")}";
    }
}
