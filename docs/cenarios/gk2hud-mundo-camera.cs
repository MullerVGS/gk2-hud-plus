using System.Collections;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só green, move o jogador): desloca o jogador DESLOC metros (a câmera vai junto, com amortecimento) e, com ACOMPANHAR, leva o mouse virtual junto com o objeto (como quem segue o objeto com o mouse); o tooltip de mundo deve ficar preso ao objeto sem sumir. Rode antes gk2hud-mundo-hover.cs.
public static class Snippet
{
    static readonly Vector3 DESLOC = new Vector3(2.5f, 0f, 0f);
    const bool ACOMPANHAR = true;
    const float ESPERA = 2.5f;

    public static IEnumerator Run()
    {
        object plugin = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
        object world = AccessTools.Property(plugin.GetType(), "WorldHover").GetValue(plugin);
        object tips = AccessTools.Property(plugin.GetType(), "Tooltips").GetValue(plugin);
        var targetField = AccessTools.Field(world.GetType(), "target");
        object t = targetField.GetValue(world);
        if (t == null) { yield return "sem alvo (rode gk2hud-mundo-hover.cs)"; yield break; }
        var centerField = AccessTools.Field(t.GetType(), "Center");
        Vector2 c0 = (Vector2)centerField.GetValue(t);
        Vector2 m0 = (Vector2)AccessTools.Property(world.GetType(), "VirtualMouse").GetValue(world);
        string before = (string)AccessTools.Method(world.GetType(), "Describe").Invoke(world, null) + " tooltip " + TipPos().ToString("0");
        var pc = MainGame.PlayerController;
        var rb = pc.GetComponentInChildren<Rigidbody>();
        Vector3 to = pc.transform.position + DESLOC;
        pc.transform.position = to;
        if (rb != null) rb.position = to;
        float t0 = Time.realtimeSinceStartup;
        bool lost = false;
        while (Time.realtimeSinceStartup - t0 < ESPERA)
        {
            object cur = targetField.GetValue(world);
            if (cur == null) lost = true;
            else if (ACOMPANHAR)
            {
                Vector2 m = m0 + ((Vector2)centerField.GetValue(cur) - c0);
                AccessTools.Property(world.GetType(), "VirtualMouse").SetValue(world, (Vector2?)m);
                AccessTools.Property(tips.GetType(), "VirtualMouse").SetValue(tips, (Vector2?)m);
            }
            yield return null;
        }
        string after = (string)AccessTools.Method(world.GetType(), "Describe").Invoke(world, null) + " tooltip " + TipPos().ToString("0");
        yield return "antes: " + before + "\ndepois: " + after + "\nperdeu o alvo no meio: " + lost;
    }

    static Vector2 TipPos()
    {
        var inst = (UITooltip)AccessTools.Field(typeof(UITooltip), "instance").GetValue(null);
        return inst != null ? (Vector2)inst.transform.position : Vector2.zero;
    }
}
