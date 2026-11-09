using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// GK2Hud: tooltip de ITEM no inventário → fixa → em cada PASSO passa o "mouse" virtual no 1º link do tipo (ou entra por raycast na N-ésima célula, "cell:N") e fixa. Deixa a pilha aberta.
public static class Snippet
{
    const string ITEM = "brick";
    static readonly string[] PASSOS = { "craft:", "cell:0" };   // prefixo do link ("wgo:", "craft:", "item:", "perk:") ou "cell:N"
    const bool FIXAR_ULTIMO = true;

    public static object Run() { return Go(); }

    static object Feature()
    {
        var plugin = Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
        return AccessTools.Property(plugin.GetType(), "Tooltips").GetValue(plugin);
    }

    static void Set(object f, string name, object v) => AccessTools.Property(f.GetType(), name)?.SetValue(f, v);
    static void Flag(object f, string name) => AccessTools.Field(f.GetType(), name).SetValue(f, true);
    static string Describe(object f) => (string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null);

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object f = Feature();
        var stack = AccessTools.Field(f.GetType(), "Stack").GetValue(f);
        var pinned = (IList)AccessTools.Field(stack.GetType(), "Pinned").GetValue(stack);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);

        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        float tOpen = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - tOpen < 1f) yield return null;   // a janela anima ao abrir: fixar no meio guardaria a posição errada
        var cell = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c.isActiveAndEnabled && c.DisplayingItem != null && c.DisplayingItem.id == ITEM
                && c.transform.position.y > 100 && c.transform.position.y < 980 && c.transform.position.x > 0 && c.transform.position.x < 1920);
        if (cell == null) { yield return "sem célula " + ITEM; yield break; }
        Vector2 cellPos = cell.transform.position;
        Set(f, "VirtualMouse", (Vector2?)cellPos);
        UITooltip.ShowItemCell(cell);
        yield return null;
        Flag(f, "TestPin");
        for (int i = 0; i < 3; i++) yield return null;
        sb.AppendLine("fixou nativo: " + Describe(f));

        foreach (string passo in PASSOS)
        {
            var top = pinned[pinned.Count - 1];
            var rt = (RectTransform)AccessTools.Field(top.GetType(), "Rt").GetValue(top);
            if (passo.StartsWith("cell:"))
            {
                var cells = rt.GetComponentsInChildren<UIItemCell>().Where(c => c.isActiveAndEnabled).ToList();
                int n = int.Parse(passo.Substring(5));
                if (n >= cells.Count) { sb.AppendLine("sem célula " + passo + " (" + cells.Count + ")"); break; }
                Vector2 p = cells[n].transform.position;
                Set(f, "VirtualMouse", (Vector2?)p);
                var ped = new PointerEventData(EventSystem.current) { position = p };
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(ped, hits);
                sb.AppendLine("raycast na célula: " + string.Join(" | ", hits.Take(3).Select(h => h.gameObject.name + "<" + h.gameObject.transform.root.name + ":" + (h.gameObject.GetComponentInParent<Canvas>()?.name) + ">")));
                // Igual ao input module: pointer-enter do objeto atingido até a raiz.
                if (hits.Count > 0)
                    for (Transform t = hits[0].gameObject.transform; t != null; t = t.parent)
                        ExecuteEvents.Execute(t.gameObject, ped, ExecuteEvents.pointerEnterHandler);
                for (int i = 0; i < 3; i++) yield return null;
                sb.AppendLine("hover célula " + cells[n].DisplayingItem?.id + ": " + Describe(f));
                Flag(f, "TestPin");
                for (int i = 0; i < 3; i++) yield return null;
                sb.AppendLine("fixou: " + Describe(f));
                continue;
            }
            var links = GK2Links(f, rt);
            sb.AppendLine("links: " + string.Join(", ", links.Select(l => l.Key)));
            var alvo = links.FirstOrDefault(l => l.Key.StartsWith(passo));
            if (alvo.Key == null) { sb.AppendLine("sem link " + passo); break; }
            Set(f, "VirtualMouse", (Vector2?)alvo.Value.center);
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 0.5f) yield return null;
            sb.AppendLine("hover " + alvo.Key + ": " + Describe(f));
            if (passo != PASSOS.Last() || FIXAR_ULTIMO)
            {
                Flag(f, "TestPin");
                for (int i = 0; i < 3; i++) yield return null;
                sb.AppendLine("fixou: " + Describe(f));
            }
        }
        // Mouse no cabeçalho da última bolha (fora de termos e células): nada novo abre e nada fecha.
        var last = pinned[pinned.Count - 1];
        var lrt = (RectTransform)AccessTools.Field(last.GetType(), "Rt").GetValue(last);
        var corners = new Vector3[4];
        lrt.GetWorldCorners(corners);
        Set(f, "VirtualMouse", (Vector2?)new Vector2((corners[0].x + corners[2].x) / 2f, corners[2].y - 34f));
        yield return null;
        yield return sb.ToString();
    }

    static List<KeyValuePair<string, Rect>> GK2Links(object f, RectTransform rt)
    {
        var links = f.GetType().Assembly.GetType("GK2Hud.Tooltips.Links");
        return (List<KeyValuePair<string, Rect>>)AccessTools.Method(links, "AllLinks").Invoke(null, new object[] { rt });
    }
}
