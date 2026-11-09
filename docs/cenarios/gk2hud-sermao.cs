using System.Collections;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só green, MUDA o save de teste): simula o fim de um sermão como o UIPrayWindowData.StartCraft faz (novo
// PlayerData.currentSermon com o resultado sorteado), sem gastar felicidade nem pagar; espera o diário anotar.
public static class Snippet
{
    const string SERMAO = "prayer_base:1";
    const int PAROQUIANOS = 10;
    const bool SUCESSO = true;
    const int IGREJA = 31, CEMITERIO = 35;

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        MainGame.PlayerData.currentSermon = new SermonResultData(SERMAO, "donation_box", PAROQUIANOS, SUCESSO, IGREJA, CEMITERIO);
        yield return new WaitForSecondsRealtime(2.5f);   // o diário varre a cada 2 s
        object p = Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
        object j = AccessTools.Property(p.GetType(), "Journal").GetValue(p);
        yield return MainGame.PlayerData.currentSermon + " | " + AccessTools.Method(j.GetType(), "Describe").Invoke(j, null);
    }
}
