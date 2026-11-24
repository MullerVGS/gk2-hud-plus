using System.Linq;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Lista as máquinas que zumbi opera sozinho (abastecedor: forno, fornalha, barris, cubo; jardineiro: composteira), por zona: fila, status, falta de ingrediente e pedidos abertos. Só leitura.
public static class Snippet
{
    static readonly string[] Ids = { "furnace_1", "furnace_2", "brick_kiln_1", "kitchen_oven", "kitchen_oven_t2", "fermentation_barrel", "wine_barrel", "distillation_cube", "compost_pile_1", "compost_pile_2" };

    public static object Run()
    {
        var sb = new StringBuilder();
        foreach (var id in Ids)
            foreach (var w in MainGame.WorldData.GetWgoDataList(id))
            {
                var cc = w.CraftComponent;
                var cur = cc.CurrentCraftElement;
                var zone = w.WorldZoneData;
                sb.Append(id + " | zona " + (zone != null ? zone.id : "-") + " | status " + cc.Status);
                sb.Append(" | worker " + (w.Worker != null ? w.Worker.GetType().Name : "-"));
                if (cur != null) sb.Append(" | atual " + cur.CraftId + (cur.IsInfinite ? "∞" : "") + " começou " + cur.IsStarted + " auto " + cur.Def.isAuto + " pode " + cur.CanStartCraft(w));
                sb.Append(" | fila [" + string.Join(", ", cc.CraftElementsQueue.Select(e => e.CraftId + (e.IsInfinite ? "∞" : ""))) + "]");
                if (w.AutoCrafterOrderPlacer != null) sb.Append(" | falta ingrediente " + w.AutoCrafterOrderPlacer.HasMissingIngredients());
                if (zone != null)
                {
                    var os = zone.Orders.Where(o => o.TargetWgoUniqueId == w.UniqueId).Select(o => o.GetType().Name + (o.Item != null && !o.Item.IsEmpty ? "(" + o.Item.id + "x" + o.Item.Count + ")" : "") + (o.ExecutorUniqueId.IsEmpty ? "" : "*"));
                    sb.Append(" | pedidos [" + string.Join(", ", os) + "]");
                    var ct = zone.id;
                    int cares = MainGame.Instance.GameSave.zombieSystemData.Cache.Values.Count(z => z.ZombieType == ZombieType.Caretaker && z.WorldZoneData != null && z.WorldZoneData.id == ct);
                    sb.Append(" | abastecedores na zona " + cares);
                }
                sb.AppendLine();
            }
        return sb.ToString();
    }
}
