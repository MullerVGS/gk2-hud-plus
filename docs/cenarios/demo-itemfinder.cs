using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "ItemFinder": com a mesa de alquimia aberta (alquimia-mesa.cs), o "+" do ingrediente abre a escolha dos
// baús com a seção "Disponíveis" (somada, por runas, sem os baús embaixo); o cursor passa por duas células, o botão de
// ordem troca para quantidade e volta, e o X fecha. FIM = "aberta" para com a escolha aberta (ordem por runas).
public static class Snippet
{
    static string FIM = "";

    public static IEnumerator Run()
    {
        var mesa = LazyUI.GetWindow<UIAlchemyWindow>();
        if (!mesa.IsShown) { yield return "mesa de alquimia fechada (rode alquimia-mesa.cs)"; yield break; }
        var cfg = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.Config;
        if (cfg.TryGetEntry<string>("ItemPicker", "Sort", out var sort)) sort.Value = "Alchemy=Runes";
        var plus = mesa.GetComponentsInChildren<UIAlchemyIngredient>(false).Select(i => i.GetComponentInChildren<UIItemCell>(false)).FirstOrDefault(c => c != null);
        if (plus == null) { yield return "sem ingrediente na mesa"; yield break; }
        Vector2 at = Demo.Center(plus.transform);
        Demo.Mouse(at + new Vector2(-180, 160));
        yield return Demo.Wait(0.4f);
        yield return Demo.Glide(at, 0.7f);
        yield return Demo.Wait(0.2f);
        plus.OnPress();
        yield return Demo.Wait(0.9f);
        var win = LazyUI.GetWindow<UIMultiInventoryWindow>();
        if (!win.IsShown) { yield return "a escolha não abriu"; yield break; }
        var cells = win.GetComponentsInChildren<UIItemCell>(false).Where(c => c.DisplayingItem != null && !c.DisplayingItem.IsEmpty).ToList();
        if (FIM == "aberta") { yield return "escolha aberta"; yield break; }
        foreach (int i in new[] { 5, 12 })
        {
            if (i >= cells.Count) continue;
            yield return Demo.HoverOn(cells[i].gameObject, 0.6f, 1.3f);
            Demo.Exit(cells[i].gameObject);
        }
        GameObject btn = Demo.Find("GK2Crafting.Sort", under: win.transform);
        if (btn != null)
        {
            yield return Demo.Glide(Demo.Center(btn.transform), 0.7f);
            Demo.Enter(btn);
            yield return Demo.Wait(1.2f);
            for (int k = 0; k < 2; k++)
            {
                Demo.Click(btn);
                yield return Demo.Wait(0.2f);
                btn = Demo.Find("GK2Crafting.Sort", under: win.transform);
                if (btn == null) break;
                Demo.Enter(btn);
                yield return Demo.Wait(1.4f);
            }
            if (btn != null) Demo.Exit(btn);
        }
        GameObject close = win.GetComponentsInChildren<LazyButton>(false).FirstOrDefault(b => b.name == "CloseButton")?.gameObject;
        if (close != null)
        {
            yield return Demo.Glide(Demo.Center(close.transform) + new Vector2(2, 4), 0.6f);
            yield return Demo.Wait(0.3f);
            Demo.Click(close);
        }
        else win.Close();
        yield return Demo.Wait(0.7f);
        Demo.Mouse(null);
        yield return !win.IsShown && mesa.IsShownAndTop ? "demo itemfinder ok" : "não voltou à mesa";
    }
}
