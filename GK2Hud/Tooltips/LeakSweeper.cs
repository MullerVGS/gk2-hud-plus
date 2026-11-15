using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Remove de <c>PlayerData.inventory.OnItemsAdd/OnItemsRemove</c> só os handlers cujo alvo é um UnityEngine.Object
    /// já destruído. Eles vêm de UITooltipCraftItemCell (inscreve no Draw, só sai no Flush) dentro de bolhas destruídas
    /// pelo GK2Hud 0.1.0: na transferência seguinte o RedrawItem da célula morta lança NRE no meio do
    /// InventoryUIItemMoveOpHandler.TryMoveItem, o item entra no destino e não sai da origem (duplica).
    /// Roda na carga do plugin e a cada 2 s; não toca em handler vivo.
    /// </summary>
    internal sealed class LeakSweeper
    {
        private const float Interval = 2f;
        private static readonly FieldInfo AddField = AccessTools.Field(typeof(Inventory), "OnItemsAdd");
        private static readonly FieldInfo RemoveField = AccessTools.Field(typeof(Inventory), "OnItemsRemove");

        private float next;

        public void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + Interval;
            Sweep();
        }

        public int Sweep()
        {
            Inventory inv = MainGame.PlayerData?.inventory;
            if (inv == null) return 0;
            int removed = 0;
            try
            {
                removed += SweepField(AddField, inv);
                removed += SweepField(RemoveField, inv);
            }
            catch (Exception ex)
            {
                HudModule.Log.LogError("limpeza de handlers de inventário: " + ex);
            }
            if (removed > 0) HudModule.Log.LogWarning($"removidos {removed} handlers de inventário de objetos destruídos (UITooltipCraftItemCell em bolhas antigas)");
            return removed;
        }

        private static int SweepField(FieldInfo field, Inventory inv)
        {
            if (field == null) return 0;
            var current = field.GetValue(inv) as Delegate;
            if (current == null) return 0;
            var dead = new List<Delegate>();
            foreach (Delegate d in current.GetInvocationList())
            {
                object target = d.Target;
                if (target is UnityEngine.Object o && o == null) dead.Add(d);
            }
            if (dead.Count == 0) return 0;
            Delegate result = current;
            foreach (Delegate d in dead) result = Delegate.Remove(result, d);
            field.SetValue(inv, result);
            return dead.Count;
        }
    }
}
