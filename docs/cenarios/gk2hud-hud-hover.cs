using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// GK2Hud (só leitura): põe o mouse virtual da HUD sobre a barra/ícone (ALVO) e espera o fade; lista o raycast nativo no ponto e, com TOOLTIP, abre o tooltip nativo do ícone pelo pointer-enter dele. Deixa o mouse lá (ALVO = "limpar" zera).
public static class Snippet
{
    static string ALVO = "energia";   // barra | energia | insanidade | fora | limpar
    const bool TOOLTIP = true;       // só para energia/insanidade: dispara o UIMouseTooltip nativo do ícone

    static object Plugin() => Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
    static object Prop(object o, string n) => AccessTools.Property(o.GetType(), n).GetValue(o);
    static void SetProp(object o, string n, object v) => AccessTools.Property(o.GetType(), n).SetValue(o, v);

    static Vector2 Center(Transform t)
    {
        var c = new Vector3[4];
        ((RectTransform)t).GetWorldCorners(c);   // canvas overlay: mundo = px de tela
        return (c[0] + c[2]) / 2f;
    }

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object hud = Prop(Plugin(), "HudInfo");
        foreach (var t in Object.FindObjectsByType<UIMouseTooltip>(FindObjectsSortMode.None)) { try { t.OnPointerExit(new PointerEventData(EventSystem.current) { fullyExited = true }); } catch (System.Exception) { } }
        if (ALVO == "limpar") { SetProp(hud, "VirtualMouse", null); yield return "limpo"; yield break; }
        var bar = Object.FindObjectsByType<UIEnergySanityBar>(FindObjectsSortMode.None).First(b => b.isActiveAndEnabled);
        Transform alvo = ALVO == "energia" ? bar.transform.Find("EnergyIcon")
                       : ALVO == "insanidade" ? bar.transform.Find("InsanityIcon")
                       : ALVO == "barra" ? bar.transform.Find("CurrentEnergy") : null;
        Vector2 m = alvo != null ? Center(alvo) : new Vector2(960, 540);
        SetProp(hud, "VirtualMouse", (Vector2?)m);

        var sb = new StringBuilder();
        // Prova de que a HUD não rouba pointer: o que o EventSystem do jogo acerta nesse ponto.
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = m }, hits);
        sb.AppendLine("mouse " + m.ToString("0") + " raycast: " + (hits.Count == 0 ? "(nada)" : string.Join(" > ", hits.Select(h => h.gameObject.name))));
        if (TOOLTIP && alvo != null && alvo.TryGetComponent(out UIMouseTooltip tip))
        {
            tip.OnPointerEnter(new PointerEventData(EventSystem.current) { position = m });
        }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.8f) yield return null;
        var native = (UITooltip)AccessTools.Field(typeof(UITooltip), "instance").GetValue(null);
        sb.AppendLine("tooltip nativo ativo=" + (native != null && native.gameObject.activeInHierarchy));
        sb.AppendLine(AccessTools.Method(hud.GetType(), "Describe").Invoke(hud, null).ToString());
        yield return sb.ToString();
    }
}
