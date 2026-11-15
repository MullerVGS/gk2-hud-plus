using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud: com o tooltip de mundo visível (rode antes gk2hud-mundo-hover.cs), fixa com o "botão do meio" virtual e passa o mouse no 1º link do tipo LINK (abre o aninhado ao lado; FIXAR_ANINHADO fixa também). Deixa a pilha aberta.
public static class Snippet
{
    const string LINK = "craft:";        // prefixo do link a abrir na bolha fixada ("craft:", "item:", "wgo:"); vazio = só fixa
    const bool FIXAR_ANINHADO = false;

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);
    static void Flag(object f, string name) => AccessTools.Field(f.GetType(), name).SetValue(f, true);
    static string Describe(object f) => (string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null);

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object tips = Prop(Plugin(), "Tooltips");
        object world = Prop(Plugin(), "WorldHover");
        sb.AppendLine("antes: " + Describe(world));
        Flag(tips, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixou: " + Describe(tips));
        sb.AppendLine("mundo: " + Describe(world));
        if (LINK == "") { yield return sb.ToString(); yield break; }
        var stack = AccessTools.Field(tips.GetType(), "Stack").GetValue(tips);
        var pinned = (IList)AccessTools.Field(stack.GetType(), "Pinned").GetValue(stack);
        if (pinned.Count == 0) { yield return sb + "nada fixado"; yield break; }
        var top = pinned[pinned.Count - 1];
        var rt = (RectTransform)AccessTools.Field(top.GetType(), "Rt").GetValue(top);
        var linksType = tips.GetType().Assembly.GetType("GK2Hud.Tooltips.Links");
        var links = (List<KeyValuePair<string, Rect>>)AccessTools.Method(linksType, "AllLinks").Invoke(null, new object[] { rt });
        sb.AppendLine("links: " + string.Join(", ", links.Select(l => l.Key)));
        var alvo = links.FirstOrDefault(l => l.Key.StartsWith(LINK));
        if (alvo.Key == null) { yield return sb + "sem link " + LINK; yield break; }
        SetProp(tips, "VirtualMouse", (Vector2?)alvo.Value.center);
        SetProp(world, "VirtualMouse", (Vector2?)alvo.Value.center);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
        sb.AppendLine("hover " + alvo.Key + ": " + Describe(tips));
        if (FIXAR_ANINHADO)
        {
            Flag(tips, "TestPin");
            for (int i = 0; i < 3; i++) yield return null;
            sb.AppendLine("fixou aninhado: " + Describe(tips));
        }
        sb.AppendLine("mundo: " + Describe(world));
        yield return sb.ToString();
    }
}
