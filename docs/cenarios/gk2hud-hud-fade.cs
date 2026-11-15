using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud (só leitura): dispara um fade da HUD (CASO), amostra o alpha a cada frame e grava um print no meio do fade em <persistentDataPath>/gk2hud-fade-<CASO>.png (recorte do canto do HUD).
public static class Snippet
{
    const string CASO = "numeros-in";   // numeros-in | numeros-out | placa-out (abre o inventário) | placa-in (fecha)
    const float ALVO_MEIO = 0.5f;       // alpha em que o print é tirado

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);
    static object Field(object o, string n) => AccessTools.Field(o.GetType(), n).GetValue(o);

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object hud = Prop(Plugin(), "HudInfo");
        bool placa = CASO.StartsWith("placa");
        object fader = Field(hud, placa ? "clockFader" : "numbersFader");
        var bar = Object.FindObjectsByType<UIEnergySanityBar>(FindObjectsSortMode.None).First(b => b.isActiveAndEnabled);
        var c = new Vector3[4];
        ((RectTransform)bar.transform.Find("CurrentEnergy")).GetWorldCorners(c);
        Vector2 sobre = (c[0] + c[2]) / 2f;
        var cw = LazyUI.GetWindow<CharacterWindow>();

        // estado inicial estável
        if (CASO == "numeros-in") SetProp(hud, "VirtualMouse", (Vector2?)new Vector2(960, 540));
        if (CASO == "numeros-out") SetProp(hud, "VirtualMouse", (Vector2?)sobre);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1f) yield return null;

        // dispara
        float start = Time.realtimeSinceStartup;
        if (CASO == "numeros-in") SetProp(hud, "VirtualMouse", (Vector2?)sobre);
        if (CASO == "numeros-out") SetProp(hud, "VirtualMouse", (Vector2?)new Vector2(960, 540));
        if (CASO == "placa-out") cw.Open(new CharacterWindowData(MainGame.Instance.GameSave));
        if (CASO == "placa-in") AccessTools.Method(cw.GetType(), "Close", new System.Type[0]).Invoke(cw, null);

        var sb = new StringBuilder();
        bool shot = false;
        float prev = -1f;
        while (Time.realtimeSinceStartup - start < 1.2f)
        {
            yield return new WaitForEndOfFrame();
            float a = (float)Prop(fader, "Alpha");
            if (a != prev) sb.Append($"{Time.realtimeSinceStartup - start:0.000}s={a:0.00} ");
            prev = a;
            bool meio = CASO.EndsWith("in") ? a >= ALVO_MEIO : a <= ALVO_MEIO;
            if (!shot && meio && a > 0.02f && a < 0.98f)
            {
                shot = true;
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                var rgb = new Texture2D(560, 200, TextureFormat.RGB24, false);
                rgb.SetPixels(tex.GetPixels(0, tex.height - 200, 560, 200));
                string path = Path.Combine(Application.persistentDataPath, "gk2hud-fade-" + CASO + ".png");
                File.WriteAllBytes(path, rgb.EncodeToPNG());
                Object.Destroy(tex);
                Object.Destroy(rgb);
                sb.Append($"[print alpha={a:0.00}] ");
            }
        }
        yield return "curva " + CASO + ": " + sb + "\n" + AccessTools.Method(hud.GetType(), "Describe").Invoke(hud, null);
    }
}
