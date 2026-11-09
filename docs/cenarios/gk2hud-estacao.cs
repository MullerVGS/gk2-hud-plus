using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: tooltip de craft como o da quest ("Create: <craft>", LinkedEntityWidget) → fixa → passa o "mouse" no link da estação ESTACAO e fixa o popup (com a imagem da construção). Usa um LinkedEntityWidget da Tech Tree redesenhado com o CRAFT (só UI; reabrir a janela restaura).
public static class Snippet
{
    const string CRAFT = "clean_paper_press";
    const string ESTACAO = "wgo:paper_press";

    public static object Run() { return Go(); }

    static object F() { var p = Chainloader.PluginInfos["arthur.gk2.hud"].Instance; return AccessTools.Property(p.GetType(), "Tooltips").GetValue(p); }
    static string D(object f) => (string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null);
    static void Mouse(object f, Vector2? v) => AccessTools.Property(f.GetType(), "VirtualMouse").SetValue(f, v);
    static void Flag(object f, string n) => AccessTools.Field(f.GetType(), n).SetValue(f, true);

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object f = F();
        var stack = AccessTools.Field(f.GetType(), "Stack").GetValue(f);
        var pinned = (IList)AccessTools.Field(stack.GetType(), "Pinned").GetValue(stack);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);
        var cw = LazyUI.GetWindow<CharacterWindow>();
        if (!cw.gameObject.activeInHierarchy) cw.Open(new CharacterWindowData(MainGame.Instance.GameSave));
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1f) yield return null;
        AccessTools.Method(typeof(CharacterWindow), "TrySwitchToPage").Invoke(cw, new object[] { CharacterWindowData.CharPage.TechTree });
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1f) yield return null;
        var w = Object.FindObjectsByType<LinkedEntityWidget>(FindObjectsSortMode.None)
            .Where(x => x.isActiveAndEnabled && x.transform.position.y > 300 && x.transform.position.y < 700 && x.transform.position.x > 400 && x.transform.position.x < 900)
            .OrderBy(x => x.transform.position.x).FirstOrDefault();
        if (w == null) { yield return "sem LinkedEntityWidget na Tech Tree"; yield break; }
        w.Draw(new LinkedEntityWidgetData(GameBalance.GetCraftDef(CRAFT), null));
        yield return null;
        Mouse(f, w.transform.position);
        UITooltip.ShowLinkedEntity(w);
        yield return null;
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixou: " + D(f));
        var top = pinned[pinned.Count - 1];
        var rt = (RectTransform)AccessTools.Field(top.GetType(), "Rt").GetValue(top);
        var linksType = f.GetType().Assembly.GetType("GK2Hud.Tooltips.Links");
        var links = (List<KeyValuePair<string, Rect>>)AccessTools.Method(linksType, "AllLinks").Invoke(null, new object[] { rt });
        sb.AppendLine("links: " + string.Join(", ", links.Select(l => l.Key)));
        var alvo = links.FirstOrDefault(l => l.Key == ESTACAO);
        if (alvo.Key == null) { yield return sb + "sem link " + ESTACAO; yield break; }
        Mouse(f, alvo.Value.center);
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.5f) yield return null;
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixou estação: " + D(f));
        var last = pinned[pinned.Count - 1];
        var lrt = (RectTransform)AccessTools.Field(last.GetType(), "Rt").GetValue(last);
        var icon = lrt.Find("GK2Hud_StationImage/Icon") as RectTransform;
        if (icon != null)
        {
            var c = new Vector3[4]; icon.GetWorldCorners(c);
            var img = icon.GetComponent<UnityEngine.UI.Image>();
            sb.AppendLine("imagem: " + img.sprite.name + " tela " + c[0] + "→" + c[2] + " (" + (c[2].x - c[0].x) + "×" + (c[2].y - c[0].y) + " px)");
        }
        else sb.AppendLine("imagem: nenhuma");
        var hc = new Vector3[4]; lrt.GetWorldCorners(hc);
        Mouse(f, new Vector2((hc[0].x + hc[2].x) / 2f, hc[2].y - 34f));   // cabeçalho da última bolha: nada abre nem fecha
        yield return sb.ToString();
    }
}
