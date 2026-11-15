using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: hover real numa célula (isHovered) → fixa → redesenha a célula ainda sob o mouse (CheckPreviousTooltip, como o Draw faz) e confere que o nativo não volta por cima; aperta fixar de novo; tira o mouse. Deixa a bolha fixada.
public static class Snippet
{
    const string ITEM = "brick";

    public static object Run() { return Go(); }

    static object F() { var p = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance); return AccessTools.Property(p.GetType(), "Tooltips").GetValue(p); }
    static string D(object f) => ((string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null)).Split(new[] { " rect=" }, System.StringSplitOptions.None)[0] + " ..." + Tail((string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null));
    static string Tail(string s) { int i = s.IndexOf(" native="); return i < 0 ? "" : s.Substring(i); }
    static void Mouse(object f, Vector2? v) => AccessTools.Property(f.GetType(), "VirtualMouse").SetValue(f, v);
    static void Flag(object f, string n) => AccessTools.Field(f.GetType(), n).SetValue(f, true);
    static bool NativeVisible() { var t = (UITooltip)AccessTools.Field(typeof(UITooltip), "instance").GetValue(null); return t.gameObject.activeSelf && t.GetComponent<CanvasGroup>().alpha > 0.5f; }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object f = F();
        var stack = AccessTools.Field(f.GetType(), "Stack").GetValue(f);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);
        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        float tOpen = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tOpen < 1f) yield return null;   // a janela anima ao abrir: fixar no meio guardaria a posição errada
        var cell = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.isActiveAndEnabled && c.DisplayingItem != null && c.DisplayingItem.id == ITEM
                && c.transform.position.y > 100 && c.transform.position.y < 980 && c.transform.position.x > 0 && c.transform.position.x < 1920);
        if (cell == null) { yield return "sem célula " + ITEM; yield break; }
        var redraw = AccessTools.Method(typeof(UIItemCell), "CheckPreviousTooltip");
        Mouse(f, new Vector2(1900, 20));   // solta supressão de uma rodada anterior
        for (int i = 0; i < 2; i++) yield return null;
        Mouse(f, cell.transform.position);
        cell.ShowUITooltip();   // igual ao OnOver: isHovered = true + ShowItemCell
        yield return null;
        sb.AppendLine("hover: nativo=" + NativeVisible());
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixou: " + D(f));
        redraw.Invoke(cell, null);
        yield return null;
        sb.AppendLine("redraw com o mouse na célula: nativo=" + NativeVisible() + " | " + D(f));
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixar de novo: " + D(f));
        Mouse(f, new Vector2(1900, 20));
        for (int i = 0; i < 2; i++) yield return null;
        Mouse(f, cell.transform.position);
        redraw.Invoke(cell, null);
        yield return null;
        sb.AppendLine("saiu e voltou + redraw: nativo=" + NativeVisible() + " (supressão liberada)");
        UITooltip.HideImmediately();
        // Deixa o mouse na célula e redesenha de novo para o print: só a bolha fixada.
        cell.ShowUITooltip();
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        redraw.Invoke(cell, null);
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("estado do print: nativo=" + NativeVisible() + " | " + D(f));
        yield return sb.ToString();
    }
}
