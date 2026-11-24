using System;
using System.Linq;
using UnityEngine;

// Prepara só o inventário descartável do green: o snapshot do blue pode não ter o item usado na bateria.
public static class Snippet
{
    public static object Run()
    {
        if (Application.productName != "GK2 Green Instance") throw new InvalidOperationException("preparo exclusivo do green");
        var inventory = MainGame.PlayerData.inventory;
        if (inventory.Data.GetTotalCountInInventory("nails_iron", ignoreAllBags: true) == 0
            && !inventory.AddItemToInventory(new Item("nails_iron", 1), ignoreAllBags: true))
            throw new InvalidOperationException("sem espaço para o item de teste");
        // Fora das bolsas e na primeira célula visível: não depende do scroll salvo pelo jogador.
        var item = inventory.Data.Inventory.First(i => i.id == "nails_iron");
        inventory.Data.Inventory.Remove(item);
        inventory.Data.Inventory.Insert(0, item);
        return "inventário preparado: nails_iron";
    }
}
