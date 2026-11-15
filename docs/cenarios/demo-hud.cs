using System.Collections;
using System.Linq;
using UnityEngine;

// Demo do clipe "Hud" (tools/rec/clips.txt): o cursor vai à barra de energia, os números aparecem, passa pela insanidade e
// sai; os números somem.
public static class Snippet
{
    public static IEnumerator Run()
    {
        if (Demo.Hud("HudInfo") == null) { yield return "HUD numbers desligado"; yield break; }
        var bar = Object.FindObjectsByType<UIEnergySanityBar>(FindObjectsSortMode.None).First(b => b.isActiveAndEnabled);
        Vector2 energy = Demo.Center(bar.transform.Find("EnergyIcon")), insanity = Demo.Center(bar.transform.Find("InsanityIcon"));
        Demo.Mouse(new Vector2(470, 160));
        yield return Demo.Wait(0.5f);
        yield return Demo.Glide(energy + new Vector2(110, 6), 0.7f);
        yield return Demo.Wait(1.4f);
        yield return Demo.Glide(insanity + new Vector2(4, 6), 0.6f);
        yield return Demo.Wait(1.0f);
        yield return Demo.Glide(new Vector2(470, 160), 0.7f);
        yield return Demo.Wait(0.8f);
        Demo.Mouse(null);
        yield return "demo hud ok";
    }
}
