using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud: tooltip de mundo em A (ex.: forno), fixa com o "botão do meio" virtual e leva o mouse ao vizinho B (ex.: moinho, cujo retângulo fica dentro do de A): o tooltip de B tem de aparecer (a supressão do alvo fixado não pode prender o proxy). Deixa a bolha de A fixada.
public static class Snippet
{
    const string A = "kitchen_oven";
    const string B = "millstone";

    static object Plugin() => Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void Mouse(object world, object tips, Vector2 m) { AccessTools.Property(world.GetType(), "VirtualMouse").SetValue(world, (Vector2?)m); AccessTools.Property(tips.GetType(), "VirtualMouse").SetValue(tips, (Vector2?)m); }
    static string D(object o) => (string)AccessTools.Method(o.GetType(), "Describe").Invoke(o, null);

    public static object Run() { return Go(); }

    static Vector2? CenterOf(object world, string id)
    {
        object targets = AccessTools.Field(world.GetType(), "Targets").GetValue(world);
        AccessTools.Method(targets.GetType(), "Invalidate").Invoke(targets, null);
        AccessTools.Method(targets.GetType(), "Pick").Invoke(targets, new object[] { new Vector2(-1000, -1000) });
        foreach (var c in ((IEnumerable)Prop(targets, "All")).Cast<object>())
        {
            var data = (WgoData)AccessTools.Field(c.GetType(), "Data").GetValue(c);
            if (data == null || data.id != id) continue;
            AccessTools.Method(c.GetType(), "UpdateRect").Invoke(c, null);
            return (Vector2)AccessTools.Field(c.GetType(), "Center").GetValue(c);
        }
        return null;
    }

    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object world = Prop(Plugin(), "WorldHover"), tips = Prop(Plugin(), "Tooltips");
        Vector2? a = CenterOf(world, A), b = CenterOf(world, B);
        if (a == null || b == null) { yield return "sem candidato " + (a == null ? A : B); yield break; }
        Mouse(world, tips, a.Value);
        yield return Wait(1.2f);
        sb.AppendLine("hover A: " + D(world));
        AccessTools.Field(tips.GetType(), "TestPin").SetValue(tips, true);
        yield return Wait(0.2f);
        sb.AppendLine("fixou A: " + D(tips));
        Mouse(world, tips, b.Value);
        yield return Wait(1.5f);
        sb.AppendLine("mouse em B: " + D(world));
        sb.AppendLine("pilha: " + D(tips));
        yield return sb.ToString();
    }
}
