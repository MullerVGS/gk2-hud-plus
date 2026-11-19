using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "WheelCarousel": liga a opção Semana em carrossel; o cursor para no medalhão, a semana aparece numa linha só
// e a hora avança HORAS horas, com a faixa deslizando; o cursor sai e a hora volta devagar (o clipe recomeça igual, sem
// salto de luz). No fim volta a opção como estava. FIM = "meio" para com a faixa aberta.
public static class Snippet
{
    static float HORAS = 5f;
    static string FIM = "";

    static ConfigEntry<bool> Toggle(string id)
    {
        object host = AccessTools.Property(Demo.Plugin.GetType(), "Host").GetValue(Demo.Plugin);
        object f = AccessTools.Method(host.GetType(), "Find").Invoke(host, new object[] { id });
        return (ConfigEntry<bool>)AccessTools.Field(f.GetType(), "Toggle").GetValue(f);
    }

    static IEnumerator Clock(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            EnvironmentEngine.Instance.SetTimeOfDay(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds))));
            yield return null;
        }
    }

    public static IEnumerator Run()
    {
        if (Demo.Hud("Wheel") == null) throw new System.Exception("medalhão desligado");
        var toggle = Toggle("WheelCarousel");
        bool was = toggle.Value;
        toggle.Value = true;
        var leftUp = Traverse.Create(LazyUI.Get<HUD>()).Field("leftUpGroup").GetValue<GameObject>();
        var wheel = leftUp.GetComponentInChildren<UIHUDWheel>(true);
        Vector2 center = Demo.Center(wheel.transform);
        float t0 = MainGame.Instance.GameSave.environmentData.TimeOfDay, t1 = t0 + HORAS / 24f;
        if (t1 >= 0.98f) throw new System.Exception("hora " + t0 + " perto da virada do dia");
        Demo.Mouse(center + new Vector2(300, 170));
        yield return Demo.Wait(0.5f);
        yield return Demo.Glide(center + new Vector2(4, 6), 0.8f);
        yield return Demo.Wait(1.2f);
        yield return Clock(t0, t1, 3.0f);
        yield return Demo.Wait(1.0f);
        if (FIM == "meio") { yield return "parado com a faixa aberta"; yield break; }
        yield return Demo.Glide(center + new Vector2(300, 170), 0.8f);
        yield return Clock(t1, t0, 0.8f);
        yield return Demo.Wait(0.4f);
        Demo.Mouse(null);
        toggle.Value = was;
        yield return "demo carrossel ok";
    }
}
