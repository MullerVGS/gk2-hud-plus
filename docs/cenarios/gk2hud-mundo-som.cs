using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud (só leitura): conta as chamadas de LazyAudio.PlayAndForget durante SEGUNDOS (patch Harmony temporário, removido no fim). Com uma bolha de mundo fixada ou o tooltip de mundo visível, "gui_hover_light" tem de ficar em 0 nas atualizações ao vivo.
public static class Snippet
{
    const float SEGUNDOS = 6f;
    static readonly Dictionary<string, int> Count = new Dictionary<string, int>();

    static void Pre(string id) { Count.TryGetValue(id ?? "-", out int n); Count[id ?? "-"] = n + 1; }

    // Linhas com "Time left"/"Falta"/"Progress" do objeto pelo nome (bolha fixada ou o UITooltip nativo), para provar que houve atualização.
    static string Texts(string name)
    {
        var go = UnityEngine.Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled && t.transform.root != null && HasAncestor(t.transform, name))
            .Select(t => System.Text.RegularExpressions.Regex.Replace(t.text, "<[^>]+>", "")).Where(t => t.Contains("Time left") || t.Contains("Progress") || t.Contains("Falta") || t.Contains("Progresso"));
        return string.Join(" / ", go);
    }

    static bool HasAncestor(Transform t, string name) { for (; t != null; t = t.parent) if (t.name == name) return true; return false; }

    public static IEnumerator Run()
    {
        Count.Clear();
        string b0 = Texts("GK2Hud_Bubble0"), n0 = Texts("UITooltip");
        var h = new Harmony("gk2hud.cenario.som");
        h.Patch(AccessTools.Method(typeof(LazyAudio), nameof(LazyAudio.PlayAndForget), new[] { typeof(string) }), prefix: new HarmonyMethod(typeof(Snippet), nameof(Pre)));
        float t0 = Time.realtimeSinceStartup;
        try
        {
            while (Time.realtimeSinceStartup - t0 < SEGUNDOS) yield return null;
        }
        finally { h.UnpatchSelf(); }
        string b1 = Texts("GK2Hud_Bubble0"), n1 = Texts("UITooltip");
        yield return "sons em " + SEGUNDOS + " s: " + (Count.Count == 0 ? "nenhum" : string.Join(", ", Count.Select(kv => kv.Key + "×" + kv.Value)))
            + "\nbolha fixada mudou: " + (b0 != b1) + " | " + b0 + " → " + b1 + "\ntooltip nativo mudou: " + (n0 != n1) + " | " + n0 + " → " + n1;
    }
}
