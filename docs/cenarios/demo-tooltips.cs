using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "Tooltips": com o inventário aberto, o cursor para num item, o tooltip enriquecido aparece e se fixa
// (botão do meio), e o cursor passa num termo destacado que abre outro tooltip dentro. No fim fecha as bolhas (o clipe
// recomeça igual). FIM = "fixado" para no meio (enquadrar a região com um print).
// O item é o primeiro de ITENS visível dentro de AREA (a região do clipe em tools/rec/clips.txt): o inventário do save
// de teste muda a cada snapshot do blue. Sem item ou sem termo destacado, o demo falha e a gravação é descartada.
public static class Snippet
{
    static string ITENS = "nails_iron,nails_bronze,cook_burger,mead,wooden_plank,brick";
    static string AREA = "300,110,900,560";
    static string FIM = "";

    static bool Inside(Vector2 p, Rect area) => area.Contains(p - new Vector2(60, 40)) && area.Contains(p + new Vector2(60, 40));

    static void Pin(object f) => AccessTools.Field(f.GetType(), "TestPin").SetValue(f, true);

    static List<KeyValuePair<string, Rect>> Links(object f, RectTransform rt)
    {
        var links = f.GetType().Assembly.GetType("GK2Hud.Tooltips.Links");
        return (List<KeyValuePair<string, Rect>>)AccessTools.Method(links, "AllLinks").Invoke(null, new object[] { rt });
    }

    public static IEnumerator Run()
    {
        object f = Demo.Hud("Tooltips");
        if (f == null) throw new System.Exception("tooltips desligados");
        object stack = AccessTools.Field(f.GetType(), "Stack").GetValue(f);
        var pinned = (IList)AccessTools.Field(stack.GetType(), "Pinned").GetValue(stack);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);
        var window = LazyUI.GetWindow<CharacterWindow>();
        if (!window.gameObject.activeInHierarchy) { window.Open(new CharacterWindowData(MainGame.Instance.GameSave)); yield return Demo.Wait(1f); }
        int[] a = AREA.Split(',').Select(int.Parse).ToArray();
        var area = new Rect(a[0], a[1], a[2], a[3]);
        var visible = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .Where(c => c.isActiveAndEnabled && c.DisplayingItem != null && Inside(Demo.Center(c.transform), area)).ToList();
        var cell = ITENS.Split(',').Select(id => visible.FirstOrDefault(c => c.DisplayingItem.id == id)).FirstOrDefault(c => c != null);
        if (cell == null) throw new System.Exception("nenhum de " + ITENS + " visível na área; visíveis: " + string.Join(",", visible.Select(c => c.DisplayingItem.id)));

        Vector2 c0 = Demo.Center(cell.transform);
        Demo.Mouse(c0 + new Vector2(-120, 120));
        yield return Demo.Wait(0.5f);
        yield return Demo.Glide(c0 + new Vector2(6, 8), 0.7f);
        UITooltip.ShowItemCell(cell);
        yield return Demo.Wait(1.3f);
        Pin(f);
        yield return Demo.Wait(0.6f);
        if (FIM == "fixado") { yield return "parado com o tooltip fixado"; yield break; }

        object top = pinned[pinned.Count - 1];
        var rt = (RectTransform)AccessTools.Field(top.GetType(), "Rt").GetValue(top);
        var link = Links(f, rt).FirstOrDefault(l => l.Key.StartsWith("craft:") || l.Key.StartsWith("item:") || l.Key.StartsWith("wgo:"));
        if (link.Key == null) throw new System.Exception("tooltip de " + cell.DisplayingItem.id + " sem termo destacado");
        Vector2 p = new Vector2(link.Value.center.x, Screen.height - link.Value.center.y);
        yield return Demo.Glide(p + new Vector2(4, 6), 0.8f);
        yield return Demo.Wait(1.8f);
        if (FIM == "aninhado") { yield return "parado com o tooltip aninhado: " + link.Key; yield break; }
        yield return Demo.Glide(c0 + new Vector2(-120, 120), 0.7f);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);
        UITooltip.HideImmediately();
        yield return Demo.Wait(0.5f);
        Demo.Mouse(null);
        yield return "demo tooltips ok: " + cell.DisplayingItem.id + " → " + link.Key;
    }
}
