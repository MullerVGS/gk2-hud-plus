using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só green, muda o save de teste): move 1 ITEM do baú para o inventário pelo caminho do jogo (InventoryUIItemMoveOpHandler) e conta antes/depois; lista handlers quebrados em inventory.OnItemsAdd/Remove. Precisa do CharacterWindow aberto (gk2hud-pilha.cs abre).
public static class Snippet
{
    const string ITEM = "";   // vazio = primeiro item do baú

    public static object Run()
    {
        var sb = new StringBuilder();
        Inventory player = MainGame.PlayerData.inventory;
        sb.AppendLine("antes: " + Dangling(player));
        var cell = UnityEngine.Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .Where(c => c.isActiveAndEnabled && c.DisplayingItem != null && !c.DisplayingItem.IsEmpty && (ITEM == "" || c.DisplayingItem.id == ITEM))
            .Select(c => new { c, w = c.GetComponentInParent<InventoryWidget>() })
            .FirstOrDefault(x => x.w != null && x.w.Data != null && x.w.Data.Inventory != null && x.w.Data.Inventory != player);
        if (cell == null) return sb + "nenhuma célula de baú com item (abra o inventário perto de um baú)";
        Inventory chest = cell.w.Data.Inventory;
        string id = cell.c.DisplayingItem.id;
        int p0 = player.Data.GetTotalCountInInventory(id), c0 = chest.Data.GetTotalCountInInventory(id);
        string erro = "nenhuma";
        try
        {
            new InventoryUIItemMoveOpHandler(() => chest, () => player).OnInventory1ItemPress2(cell.c);
        }
        catch (Exception e) { erro = e.GetType().Name + " em " + e.StackTrace.Split('\n').FirstOrDefault(l => l.Contains("RedrawItem") || l.Contains("SetWidgetState"))?.Trim(); }
        int p1 = player.Data.GetTotalCountInInventory(id), c1 = chest.Data.GetTotalCountInInventory(id);
        sb.AppendLine($"item {id}: inventário {p0}→{p1} ({p1 - p0:+0;-0;0}) · baú {c0}→{c1} ({c1 - c0:+0;-0;0}) · total {p0 + c0}→{p1 + c1}" + (p1 + c1 > p0 + c0 ? "  ← DUPLICOU" : ""));
        sb.AppendLine("exceção: " + erro);
        sb.AppendLine("depois: " + Dangling(player));
        return sb.ToString();
    }

    static string Dangling(Inventory inv)
    {
        string One(string ev)
        {
            var d = (Delegate)AccessTools.Field(typeof(Inventory), ev).GetValue(inv);
            if (d == null) return ev + " 0";
            var list = d.GetInvocationList();
            int dead = list.Count(x => x.Target is UnityEngine.Object o && o == null);
            return ev + " " + list.Length + " handlers (" + dead + " de objeto destruído)";
        }
        return One("OnItemsAdd") + " · " + One("OnItemsRemove");
    }
}
