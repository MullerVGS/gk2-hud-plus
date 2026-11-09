using System.Collections;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;

// Abre o inventário (CharacterWindow) e mostra o tooltip nativo do primeiro perk da lista de perks.
public static class Snippet
{
    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        for (int i = 0; i < 20; i++) yield return null;
        var perks = Object.FindObjectsByType<PerkWidget>(FindObjectsSortMode.None).Where(p => p.isActiveAndEnabled).ToList();
        if (perks.Count == 0) { yield return "nenhum PerkWidget"; yield break; }
        var p0 = perks.OrderByDescending(p => p.transform.position.y).First();
        UITooltip.ShowPerkWidget(p0);
        for (int i = 0; i < 5; i++) yield return null;
        yield return p0.name + " | perks: " + perks.Count;
    }
}
