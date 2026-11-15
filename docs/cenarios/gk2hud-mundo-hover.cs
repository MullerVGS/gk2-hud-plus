using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): põe o "mouse" virtual sobre o objeto em andamento mais próximo do jogador (ou o de id ALVO) e espera o tooltip de mundo aparecer. Deixa o mouse lá (zere com LIMPAR = true).
public static class Snippet
{
    const string ALVO = "";                 // id do WgoData (ex.: "kitchen_oven") ou "drop:<item>" (ex.: "drop:body_corpse"); vazio = candidato mais perto do jogador
    const bool BOLHA = false;      // true: mira no centro do retângulo (inclui a bolha nativa) em vez do centro do objeto
    const bool LIMPAR = false;

    static object Plugin() => AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object world = Prop(Plugin(), "WorldHover");
        object tips = Prop(Plugin(), "Tooltips");
        if (LIMPAR) { SetProp(world, "VirtualMouse", null); SetProp(tips, "VirtualMouse", null); yield return "limpo"; yield break; }
        object targets = AccessTools.Field(world.GetType(), "Targets").GetValue(world);
        // Força a lista de candidatos.
        AccessTools.Method(targets.GetType(), "Invalidate").Invoke(targets, null);
        AccessTools.Method(targets.GetType(), "Pick").Invoke(targets, new object[] { new Vector2(-1000, -1000) });
        var all = ((IEnumerable)Prop(targets, "All")).Cast<object>().ToList();
        Vector3 pp = MainGame.PlayerController.transform.position;
        object best = null; float bestD = float.MaxValue;
        foreach (var c in all)
        {
            var data = (WgoData)AccessTools.Field(c.GetType(), "Data").GetValue(c);
            var drop = (DropData)AccessTools.Field(c.GetType(), "Drop").GetValue(c);
            var dv = (Component)AccessTools.Field(c.GetType(), "DropView").GetValue(c);
            var view = (Component)AccessTools.Field(c.GetType(), "View").GetValue(c) ?? dv;
            string id = drop != null ? "drop:" + drop.Id : data?.id;
            AccessTools.Method(c.GetType(), "UpdateRect").Invoke(c, null);
            string desc = (string)AccessTools.Method(c.GetType(), "Describe").Invoke(c, null);
            float d = view != null ? (view.transform.position - pp).magnitude : 999f;
            sb.AppendLine($"  cand {desc} dist={d:0.0}");
            if (ALVO != "" ? id == ALVO && d < bestD : d < bestD) { best = c; bestD = d; }
        }
        if (best == null) { yield return sb + "sem candidato"; yield break; }
        Rect rect = (Rect)AccessTools.Field(best.GetType(), "Rect").GetValue(best);
        Vector2 center = (Vector2)AccessTools.Field(best.GetType(), "Center").GetValue(best);
        Vector2 m = BOLHA ? rect.center : center;
        SetProp(world, "VirtualMouse", (Vector2?)m);
        SetProp(tips, "VirtualMouse", (Vector2?)m);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.2f) yield return null;
        sb.AppendLine("mouse " + m.ToString("0") + " → " + AccessTools.Method(world.GetType(), "Describe").Invoke(world, null));
        yield return sb.ToString();
    }
}
