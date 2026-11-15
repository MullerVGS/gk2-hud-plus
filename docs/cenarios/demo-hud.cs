using System;
using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// Demo para `gk2 rec` (preview "HUD numbers"): o cursor desliza até o ícone de energia, os números aparecem nas barras,
// passa para a insanidade e sai; os números somem. Região sugerida: 0,0,520,180.
public static class Snippet
{
    static object Hud() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);

    /// <summary>Cursor do gravador (origem em cima) e mouse virtual da HUD (origem embaixo) no mesmo ponto.</summary>
    static void Mouse(object hud, Vector2? topLeft)
    {
        Type rec = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GK2DevBridge.Recorder")).FirstOrDefault(t => t != null);
        rec?.GetField("Cursor").SetValue(null, topLeft);
        AccessTools.Property(hud.GetType(), "VirtualMouse").SetValue(hud, topLeft.HasValue ? (Vector2?)new Vector2(topLeft.Value.x, Screen.height - topLeft.Value.y) : null);
    }

    static Vector2 TopLeftCenter(Transform t)
    {
        var c = new Vector3[4];
        ((RectTransform)t).GetWorldCorners(c);
        Vector2 m = (c[0] + c[2]) / 2f;
        return new Vector2(m.x, Screen.height - m.y);
    }

    static IEnumerator Glide(object hud, Vector2 from, Vector2 to, float seconds)
    {
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            float k = Mathf.SmoothStep(0, 1, t / seconds);
            Mouse(hud, Vector2.Lerp(from, to, k));
            yield return null;
        }
        Mouse(hud, to);
    }

    static IEnumerator Wait(float s) { for (float t = 0; t < s; t += Time.unscaledDeltaTime) yield return null; }

    public static IEnumerator Run()
    {
        object hud = AccessTools.Property(Hud().GetType(), "HudInfo").GetValue(Hud());
        if (hud == null) { yield return "HUD numbers desligado"; yield break; }
        var bar = UnityEngine.Object.FindObjectsByType<UIEnergySanityBar>(FindObjectsSortMode.None).First(b => b.isActiveAndEnabled);
        Vector2 start = new Vector2(470, 160), energy = TopLeftCenter(bar.transform.Find("EnergyIcon")) + new Vector2(60, 0);
        Vector2 insanity = TopLeftCenter(bar.transform.Find("InsanityIcon"));
        Mouse(hud, start);
        yield return Wait(0.5f);
        yield return Glide(hud, start, energy, 0.7f);
        yield return Wait(1.4f);
        yield return Glide(hud, energy, insanity, 0.6f);
        yield return Wait(1.0f);
        yield return Glide(hud, insanity, start, 0.7f);
        yield return Wait(0.8f);
        Mouse(hud, null);
        yield return "demo hud ok";
    }
}
