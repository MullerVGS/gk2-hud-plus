using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// Fecha janelas e simula pointer-enter num UIMouseTooltip do HUD (tooltip simples nativo). Troque ALVO por parte do nome/lngId.
public static class Snippet
{
    const string ALVO = "happiness";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var cw = LazyUI.GetWindow<CharacterWindow>();
        if (cw.gameObject.activeInHierarchy) { AccessTools.Method(cw.GetType(), "Close", new System.Type[0])?.Invoke(cw, null); }
        for (int i = 0; i < 20; i++) yield return null;
        var all = Object.FindObjectsByType<UIMouseTooltip>(FindObjectsSortMode.None).Where(t => t.isActiveAndEnabled).ToList();
        string Desc(UIMouseTooltip t) => t.name + "{" + AccessTools.Field(typeof(UIMouseTooltip), "lngId").GetValue(t) + "}";
        Debug.Log("[cenario] UIMouseTooltip ativos: " + string.Join(", ", all.Select(Desc)));
        var alvo = all.FirstOrDefault(t => Desc(t).Contains(ALVO));
        if (alvo == null) { yield return "sem alvo " + ALVO + " | " + string.Join(", ", all.Select(Desc)); yield break; }
        alvo.OnPointerEnter(new PointerEventData(EventSystem.current));
        for (int i = 0; i < 5; i++) yield return null;
        yield return Desc(alvo) + " | IsAvailable=" + UIMouseTooltip.IsAvailable + " | todos: " + string.Join(", ", all.Select(Desc));
    }
}
