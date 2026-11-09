using System.Linq;
using UnityEngine;

// GK2Hud: só leitura — lista os GameObjects do mod que existem na UI (bolhas, template) e quantos UITooltip há na cena. Confere órfãos após reload.
public static class Snippet
{
    public static object Run()
    {
        var all = Object.FindObjectsByType<UITooltip>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var mine = all.Where(t => t.name.StartsWith("GK2Hud_")).Select(t => t.name + (t.gameObject.activeSelf ? "*" : "")).ToList();
        return "UITooltip na cena: " + all.Length + " | do mod: " + (mine.Count == 0 ? "nenhum" : string.Join(", ", mine));
    }
}
