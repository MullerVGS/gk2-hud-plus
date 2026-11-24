// Com a mesa de alquimia aberta (alquimia-mesa.cs), clica no "+" do ingrediente INDICE e abre a escolha de item dos baús (UIMultiInventoryWindow); lista as seções.
using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const int INDICE = 0;

    public static IEnumerator Run()
    {
        var mesa = LazyUI.GetWindow<UIAlchemyWindow>();
        if (!mesa.IsShown) { yield return "mesa de alquimia fechada (rode alquimia-mesa.cs)"; yield break; }
        var cells = mesa.GetComponentsInChildren<UIAlchemyIngredient>(false).Select(i => i.GetComponentInChildren<UIItemCell>(false)).Where(c => c != null).ToList();
        if (cells.Count <= INDICE) { yield return "ingrediente " + INDICE + " não existe (" + cells.Count + ")"; yield break; }
        cells[INDICE].OnPress();
        for (int i = 0; i < 20; i++) yield return null;
        var win = LazyUI.GetWindow<UIMultiInventoryWindow>();
        if (!win.IsShown) { yield return "a escolha não abriu"; yield break; }
        var w = AccessTools.Field(typeof(UIMultiInventoryWindow), "multiInventoryWidget").GetValue(win) as MultiInventoryWidget;
        yield return "seções: " + string.Join(" | ", w.DrawnInventories.Select(d => (d.Data as InventoryWidgetDataBase)?.Inventory?.Data?.id + " " + d.Cells.Count(c => c.DisplayingItem != null && !c.DisplayingItem.IsEmpty)));
    }
}
