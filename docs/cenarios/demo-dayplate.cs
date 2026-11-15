using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Demo do clipe "HudDayPlate": liga a opção Placa do dia (como o menu faz) e a placa aparece sob a felicidade com o dia da
// semana e a hora; no fim volta a opção como estava.
public static class Snippet
{
    public static IEnumerator Run()
    {
        object host = AccessTools.Property(Demo.Plugin.GetType(), "Host").GetValue(Demo.Plugin);
        object plate = AccessTools.Method(host.GetType(), "Find").Invoke(host, new object[] { "HudDayPlate" });
        var toggle = (ConfigEntry<bool>)AccessTools.Field(plate.GetType(), "Toggle").GetValue(plate);
        bool was = toggle.Value;
        toggle.Value = false;
        yield return Demo.Wait(1.0f);
        toggle.Value = true;
        yield return Demo.Wait(4.0f);
        toggle.Value = false;
        yield return Demo.Wait(1.0f);
        toggle.Value = was;
        yield return "demo placa ok";
    }
}
