using System.Collections;
using System.IO;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): força só o tempo mostrado pelo medalhão giratório (WheelSpinFeature.ForcedT) em várias horas do dia
// atual e grava um print do canto em <persistentDataPath>/gk2hud-giro-HHMM.png; no fim devolve ao relógio do jogo. HORAS vazio = só descreve.
public static class Snippet
{
    static readonly float[] HORAS = { 0.0f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 0.999f };
    const int CROP = 260;   // px de tela a partir do canto superior esquerdo

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object spin = Prop(Plugin(), "WheelSpin");
        int day = MainGame.Instance.GameSave.environmentData.Day;
        string log = "";
        foreach (float h in HORAS)
        {
            SetProp(spin, "ForcedT", (double?)(day + (double)h));
            yield return null;
            yield return null;   // >6 dias de diferença ou dentro do limiar: aplica direto; senão anda a 120°/s
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.8f) yield return null;
            yield return new WaitForEndOfFrame();
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            var rgb = new Texture2D(CROP, CROP, TextureFormat.RGB24, false);
            rgb.SetPixels(tex.GetPixels(0, tex.height - CROP, CROP, CROP));
            int min = Mathf.RoundToInt(h * 1440f) % 1440;
            string path = Path.Combine(Application.persistentDataPath, $"gk2hud-giro-{min / 60:00}{min % 60:00}.png");
            File.WriteAllBytes(path, rgb.EncodeToPNG());
            Object.Destroy(tex);
            Object.Destroy(rgb);
            log += AccessTools.Method(spin.GetType(), "Describe").Invoke(spin, null) + "\n";
        }
        SetProp(spin, "ForcedT", null);
        yield return log + AccessTools.Method(spin.GetType(), "Describe").Invoke(spin, null);
    }
}
