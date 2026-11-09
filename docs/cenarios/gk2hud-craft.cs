using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: abre a janela de craft de uma estação da zona (ESTACAO = parte do id; vazio = primeira com crafts conhecidos) e mostra o tooltip nativo do CRAFT-ésimo craft.
public static class Snippet
{
    const string ESTACAO = "";
    const int CRAFT = 0;

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var cw = LazyUI.GetWindow<CharacterWindow>();
        if (cw.gameObject.activeInHierarchy) AccessTools.Method(cw.GetType(), "Close", new System.Type[0])?.Invoke(cw, null);
        for (int i = 0; i < 10; i++) yield return null;
        var wgos = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None)
            .Where(w => w != null && w.Data != null && w.Data.CraftComponent != null && w.Data.CraftComponent.HasCraftsByBalance)
            .Where(w => w.Data.CraftComponent.AvailableCrafts.Count > 0 && !w.Data.CraftComponent.AvailableCrafts[0].isAuto)
            .ToList();
        Debug.Log("[cenario] estações com craft: " + string.Join(", ", wgos.Select(w => w.Data.id)));
        var wgo = wgos.FirstOrDefault(w => ESTACAO == "" || w.Data.id.Contains(ESTACAO));
        if (wgo == null) { yield return "nenhuma estação | " + string.Join(", ", wgos.Select(w => w.Data.id)); yield break; }
        var win = LazyUI.GetWindow<UICraftWindow>();
        if (!win.gameObject.activeInHierarchy)
            win.Open(new UIBaseCraftWindowData(wgo, ce => { }, ce => { }));
        for (int i = 0; i < 20; i++) yield return null;
        var cells = Object.FindObjectsByType<UICraftPreviewItemCell>(FindObjectsSortMode.None)
            .Select(p => p.GetComponentInChildren<UIItemCell>())
            .Where(c => c != null && c.isActiveAndEnabled && c.CustomTooltipShowAction != null)
            .OrderByDescending(c => c.transform.position.y).ThenBy(c => c.transform.position.x).ToList();
        if (cells.Count <= CRAFT) { yield return wgo.Data.id + ": " + cells.Count + " crafts visíveis"; yield break; }
        UITooltip.ShowItemCell(cells[CRAFT]);
        for (int i = 0; i < 5; i++) yield return null;
        yield return wgo.Data.id + " | crafts visíveis: " + cells.Count;
    }
}
