using System.Collections;
using HarmonyLib;
using UnityEngine;

// Demo do clipe "WheelSpin": o tempo mostrado pelo medalhão corre um dia inteiro em poucos segundos; cada dia passa sob o
// seletor de baixo. Só o desenho do medalhão: o relógio do jogo não muda.
public static class Snippet
{
    static float SEGUNDOS = 6f;

    public static IEnumerator Run()
    {
        object spin = Demo.Hud("WheelSpin");
        if (spin == null) { yield return "giro do medalhão desligado"; yield break; }
        var env = MainGame.Instance.GameSave.environmentData;
        double start = env.Day + (double)env.TimeOfDay;
        var forced = AccessTools.Property(spin.GetType(), "ForcedT");
        for (float t = 0; t < SEGUNDOS; t += Time.unscaledDeltaTime)
        {
            forced.SetValue(spin, (double?)(start + t / SEGUNDOS * 1.5));
            yield return null;
        }
        yield return Demo.Wait(0.4f);
        forced.SetValue(spin, null);
        yield return "demo giro ok";
    }
}
