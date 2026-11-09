using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: (só green, muda o save de teste) ativa o buff PERK se faltar, fecha janelas e mostra o tooltip nativo do ícone do buff na HUD.
public static class Snippet
{
    const string PERK = "buff_talent_red_2";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        PerkSystemData perks = MainGame.Instance.GameSave.perkSystemData;
        if (!perks.HasPerk(PERK)) perks.AddPerk(PERK);
        foreach (var w in new LazyWidgetBase[] { LazyUI.GetWindow<CharacterWindow>(), LazyUI.GetWindow<UICraftWindow>() })
            if (w.gameObject.activeInHierarchy) AccessTools.Method(w.GetType(), "Close", new System.Type[0])?.Invoke(w, null);
        for (int i = 0; i < 20; i++) yield return null;
        var el = Object.FindObjectsByType<UIBuffElement>(FindObjectsSortMode.None).FirstOrDefault(b => b.isActiveAndEnabled && b.Id == PERK);
        if (el == null) { yield return "sem UIBuffElement de " + PERK; yield break; }
        AccessTools.Method(typeof(UIBuffElement), "ShowBuffTooltip").Invoke(el, null);
        for (int i = 0; i < 5; i++) yield return null;
        yield return "buff " + PERK + " | ativos: " + string.Join(", ", perks.activePerks.Select(p => p.id));
    }
}
