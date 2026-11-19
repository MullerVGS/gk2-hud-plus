// Só leitura: abre a janela nativa do canteiro (UIGardenBedWindow) no canteiro em crescimento mais perto do jogador (ou o de id ALVO); se já estiver aberta, fecha (o fechar-janelas.cs não conhece esta janela). Referência visual da barra de células.
using System.Collections;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const string ALVO = "";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        Vector3 p = MainGame.PlayerController.transform.position;
        var wgo = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None)
            .Where(w => w != null && w.Data != null && w.Data.id.StartsWith("garden_") && w.Data.CraftComponent != null && w.Data.CraftComponent.CurrentCraftElement is CraftElement)
            .Where(w => ALVO == "" || w.Data.id == ALVO)
            .OrderBy(w => (w.transform.position - p).sqrMagnitude).FirstOrDefault();
        if (wgo == null) { yield return "nenhum canteiro crescendo"; yield break; }
        var win = LazyUI.GetWindow<UIGardenBedWindow>();
        if (win.IsShown) { win.Close(); yield return "fechada"; yield break; }
        win.Open(new UIGardenBedWindowData(wgo.Data));
        for (int i = 0; i < 20; i++) yield return null;
        yield return wgo.Data.id;
    }
}
