using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): abre o popup da semana no medalhão, fixa com o botão de fixar virtual e passa o mouse no ícone
// de evento número ICONE da bolha fixada (abre a bolha do evento ao lado). ICONE < 0 = só fixa. Deixa a pilha aberta.
public static class Snippet
{
    const int ICONE = 1;

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);
    static string Describe(object f) => (string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null);

    public static object Run() { return Go(); }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object wheel = Prop(Plugin(), "Wheel"), tips = Prop(Plugin(), "Tooltips");
        var uiWheel = Resources.FindObjectsOfTypeAll<UIHUDWheel>().First(w => w.gameObject.scene.IsValid());
        var c = new Vector3[4];
        ((RectTransform)uiWheel.transform).GetWorldCorners(c);
        Vector2 m = (c[0] + c[2]) / 2f;
        SetProp(wheel, "VirtualMouse", (Vector2?)m);
        SetProp(tips, "VirtualMouse", (Vector2?)m);
        yield return Wait(0.9f);
        AccessTools.Field(tips.GetType(), "TestPin").SetValue(tips, true);
        yield return Wait(0.2f);
        sb.AppendLine("fixou: " + Describe(tips));
        var stack = AccessTools.Field(tips.GetType(), "Stack").GetValue(tips);
        var pinned = (IList)AccessTools.Field(stack.GetType(), "Pinned").GetValue(stack);
        if (pinned.Count == 0 || ICONE < 0) { yield return sb.ToString(); yield break; }
        var rt = (RectTransform)AccessTools.Field(pinned[pinned.Count - 1].GetType(), "Rt").GetValue(pinned[pinned.Count - 1]);
        var cells = rt.GetComponentsInChildren<RectTransform>().Where(x => x.name.StartsWith("cal:")).ToList();
        sb.AppendLine("ícones: " + string.Join(", ", cells.Select(x => x.name)));
        if (cells.Count == 0) { yield return sb.ToString(); yield break; }
        cells[Mathf.Min(ICONE, cells.Count - 1)].GetWorldCorners(c);
        m = (c[0] + c[2]) / 2f;
        SetProp(wheel, "VirtualMouse", (Vector2?)m);
        SetProp(tips, "VirtualMouse", (Vector2?)m);
        yield return Wait(0.8f);
        sb.AppendLine("hover: " + Describe(tips));
        yield return sb.ToString();
    }
}
