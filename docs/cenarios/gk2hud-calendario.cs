using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud (só leitura): calendário. ALVO = "medalhao" (mouse virtual no medalhão, popup da semana), "sigilo:day_wrath"
// (popup do dia), "aba" (abre o TAB no calendário; DIA = deslocamento a partir de hoje), "fechar" ou "limpar".
// REVELAR = true mostra eventos ainda não descobertos (só para ver o layout; o padrão do mod é sem spoiler).
public static class Snippet
{
    static string ALVO = "medalhao";
    const int DIA = 0;
    const bool REVELAR = false;

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);
    static object Call(object o, string n, params object[] a) => AccessTools.Method(o.GetType(), n).Invoke(o, a);

    static Vector2 Center(Transform t)
    {
        var c = new Vector3[4];
        ((RectTransform)t).GetWorldCorners(c);
        return (c[0] + c[2]) / 2f;
    }

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object p = Plugin();
        object wheel = Prop(p, "Wheel");
        object tab = Prop(p, "CalendarTab");
        var model = p.GetType().Assembly.GetType("GK2Hud.Calendar.CalendarModel");
        AccessTools.Field(model, "RevealAll").SetValue(null, REVELAR);
        var sb = new StringBuilder();
        var w = LazyUI.GetWindow<CharacterWindow>();

        if (ALVO == "limpar" || ALVO == "fechar")
        {
            SetProp(wheel, "VirtualMouse", null);
            if (w != null && w.IsShown) w.Close();
            yield return null;
            yield return "limpo";
            yield break;
        }
        if (ALVO == "aba")
        {
            SetProp(wheel, "VirtualMouse", null);
            if (!w.IsShown)
            {
                w.Open(new CharacterWindowData(MainGame.Instance.GameSave, w.LastOpenedPage == CharacterWindowData.CharPage.Undefined ? CharacterWindowData.CharPage.Main : w.LastOpenedPage));
                yield return null;
                yield return null;
            }
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 1.2f && !(bool)AccessTools.Property(tab.GetType(), "IsOpen").GetValue(tab))
            {
                yield return null;   // a aba é montada no Update do mod (retentativa de 1 s)
                if (Time.realtimeSinceStartup - t1 > 0.1f) Call(tab, "TestOpen", (int?)null);
            }
            int today = MainGame.Instance.GameSave.environmentData.Day;
            Call(tab, "TestHover", (int?)(today + DIA));
            yield return null;
            yield return null;
            yield return Call(tab, "Describe").ToString();
            yield break;
        }

        var leftUp = Traverse.Create(LazyUI.Get<HUD>()).Field("leftUpGroup").GetValue<GameObject>();
        var uiWheel = leftUp.GetComponentInChildren<UIHUDWheel>(true);
        Vector2 m = Center(uiWheel.transform);
        if (ALVO.StartsWith("sigilo:"))
        {
            string id = ALVO.Substring(7);
            var el = uiWheel.GetComponentsInChildren<UIHUDWheelElement>(true).First(e => e.icon.sprite.name.Replace("(Clone)", "") == id);
            m = Center(el.transform);
        }
        SetProp(wheel, "VirtualMouse", (Vector2?)m);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.9f) yield return null;
        sb.AppendLine("mouse " + m.ToString("0"));
        sb.AppendLine(Call(wheel, "Describe").ToString());
        yield return sb.ToString();
    }
}
