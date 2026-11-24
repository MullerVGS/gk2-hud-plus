// Com a janela de escolher item dos baús aberta (alquimia-ingrediente.cs etc.): ACAO = "listar" (seções e a ordem da seção
// "Disponíveis" do HUD+), "ordem" (clica no botão de ordem do cabeçalho), "hover-ordem" (tooltip do botão), "clicar" (clica na célula CELULA da seção).
using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const string ACAO = "listar";
    const int CELULA = 0;

    public static IEnumerator Run()
    {
        var win = LazyUI.GetWindow<UIMultiInventoryWindow>();
        if (!win.IsShown) { yield return "janela de escolha fechada"; yield break; }
        var w = AccessTools.Field(typeof(UIMultiInventoryWindow), "multiInventoryWidget").GetValue(win) as MultiInventoryWidget;
        var top = w.DrawnInventories.FirstOrDefault(d => d.Data?.Inventory?.Data?.id == "gk2crafting_available");
        if (top == null) { yield return "sem a seção Disponíveis: " + string.Join(" | ", w.DrawnInventories.Select(d => d.Data?.Inventory?.Data?.id)); yield break; }
        var sort = top.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "GK2Crafting.Sort");
        if (ACAO == "ordem" || ACAO == "hover-ordem")
        {
            if (sort == null) { yield return "sem botão de ordem"; yield break; }
            if (ACAO == "ordem") sort.GetComponent<LazyButton>().onClick.Invoke();
            else { Demo.Mouse(Demo.Center(sort)); Demo.Enter(sort.gameObject); yield return Demo.Wait(1.5f); }
            for (int i = 0; i < 20; i++) yield return null;
            top = w.DrawnInventories.FirstOrDefault(d => d.Data?.Inventory?.Data?.id == "gk2crafting_available");
        }
        if (ACAO == "clicar")
        {
            var cell = top.Cells[CELULA];
            string id = cell.DisplayingItem?.id;
            cell.OnPress();
            for (int i = 0; i < 10; i++) yield return null;
            yield return "clicou " + id + " → escolha aberta " + win.IsShown;
            yield break;
        }
        var label = sort != null ? sort.GetComponentInChildren<TMPro.TextMeshProUGUI>(true)?.text : "-";
        yield return "ordem " + label + " | " + string.Join(", ", top.Cells.Where(c => c.DisplayingItem != null && !c.DisplayingItem.IsEmpty).Select(c => c.DisplayingItem.id + "×" + c.DisplayingItem.Count))
            + " | baús visíveis " + w.DrawnInventories.Count(d => d != top && d.gameObject.activeSelf);
    }
}
