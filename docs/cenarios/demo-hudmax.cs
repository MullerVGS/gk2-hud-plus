using System.Collections;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Demo do clipe "HudShowMax": liga a opção Mostrar o máximo (como o menu faz); o cursor vai à barra de energia e os números
// aparecem com o máximo (63/150), passa pela insanidade e sai. No fim volta a opção como estava.
public static class Snippet
{
    static ConfigEntry<bool> Toggle(string id)
    {
        object host = AccessTools.Property(Demo.Plugin.GetType(), "Host").GetValue(Demo.Plugin);
        object f = AccessTools.Method(host.GetType(), "Find").Invoke(host, new object[] { id });
        return (ConfigEntry<bool>)AccessTools.Field(f.GetType(), "Toggle").GetValue(f);
    }

    public static IEnumerator Run()
    {
        if (Demo.Hud("HudInfo") == null) throw new System.Exception("HUD numbers desligado");
        var toggle = Toggle("HudShowMax");
        bool was = toggle.Value;
        toggle.Value = true;
        Demo.Mouse(new Vector2(470, 160));
        yield return Demo.Wait(0.5f);
        var bar = Object.FindObjectsByType<UIEnergySanityBar>(FindObjectsSortMode.None).First(b => b.isActiveAndEnabled);
        Vector2 energy = Demo.Center(bar.transform.Find("EnergyIcon")), insanity = Demo.Center(bar.transform.Find("InsanityIcon"));
        yield return Demo.Glide(energy + new Vector2(110, 6), 0.7f);
        yield return Demo.Wait(1.6f);
        yield return Demo.Glide(insanity + new Vector2(4, 6), 0.6f);
        yield return Demo.Wait(1.2f);
        yield return Demo.Glide(new Vector2(470, 160), 0.7f);
        yield return Demo.Wait(0.8f);
        Demo.Mouse(null);
        toggle.Value = was;
        yield return "demo hud máximo ok";
    }
}
