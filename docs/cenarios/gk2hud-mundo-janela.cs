using System.Collections;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: com o mouse virtual já sobre um objeto em andamento (rode gk2hud-mundo-hover.cs), abre o inventário e confere que o tooltip de mundo some e não volta enquanto a janela está aberta; fecha e confere que volta após o atraso.
public static class Snippet
{
    public static IEnumerator Run()
    {
        object plugin = Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
        object world = AccessTools.Property(plugin.GetType(), "WorldHover").GetValue(plugin);
        string D() => (string)AccessTools.Method(world.GetType(), "Describe").Invoke(world, null);
        string antes = D();
        var w = LazyUI.GetWindow<CharacterWindow>();
        w.Open(new CharacterWindowData(MainGame.Instance.GameSave));
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
        string aberta = D() + " nativoVisivel=" + IsNativeVisible();
        w.Close();
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
        yield return "antes: " + antes + "\njanela aberta: " + aberta + "\ndepois de fechar: " + D();
    }

    static bool IsNativeVisible()
    {
        var inst = (UITooltip)AccessTools.Field(typeof(UITooltip), "instance").GetValue(null);
        return inst != null && inst.gameObject.activeSelf;
    }
}
