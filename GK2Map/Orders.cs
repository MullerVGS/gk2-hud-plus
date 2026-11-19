using System;
using System.Collections.Generic;
using System.Reflection;
using GK2Hud;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine.UI;
using UnityEngine;

namespace GK2Map
{
    /// <summary>
    /// Ícone da Pride no mapa: a dica nativa mostra cada pedido da cidade só como caixote e "0/10". Com a opção ligada,
    /// o hover abre o tooltip nativo com as células de item dos pedidos (ícone do item, entregue/pedido, como o jogo
    /// mostra o que falta num craft) e uma linha por pedido com o nome e a recompensa. Só leitura: as contas são as de
    /// <c>UIVendorOrdersWorldWidgetData.TryCreate</c>.
    /// </summary>
    internal static class Orders
    {
        private const int RowCells = 2;
        private static readonly FieldInfo ItemList = AccessTools.Field(typeof(Item), "inventory");

        internal static void Patch(Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(UIMapActivityIconHint), nameof(UIMapActivityIconHint.ShowAsDayIcon)),
                prefix: new HarmonyMethod(typeof(Orders), nameof(ShowPrefix)));
            h.Patch(AccessTools.Method(typeof(UIMapActivityIconHint), nameof(UIMapActivityIconHint.Hide)),
                postfix: new HarmonyMethod(typeof(Orders), nameof(HidePostfix)));
        }

        /// <summary>Fecha o tooltip dos pedidos se ele estiver aberto (desligar ou recarregar o mod).</summary>
        internal static void Dispose()
        {
            MapModule m = MapModule.Instance;
            if (m != null && m.OrdersShownBy != null) UITooltip.Hide();
            if (m != null) m.OrdersShownBy = null;
        }

        private static bool ShowPrefix(UIMapActivityIconHint __instance, MapDayType dayType)
        {
            MapModule m = MapModule.Instance;
            if (dayType != MapDayType.Pride || m == null || m.OrdersEnabled == null || !m.OrdersEnabled.Value) return true;
            try
            {
                List<LazyWidgetDataBase> list = Build();
                if (list == null) return true;
                Transform icon = __instance.transform.parent != null ? __instance.transform.parent : __instance.transform;
                UIMapDayActivityIcon owner = __instance.GetComponentInParent<UIMapDayActivityIcon>();
                if (owner != null) icon = owner.transform;
                __instance.gameObject.SetActive(false);
                UITooltip.Show(list, (RectTransform)icon);
                m.OrdersShownBy = __instance;
                return false;
            }
            catch (Exception e)
            {
                MapModule.Fail(e, "Orders.Show");
                return true;
            }
        }

        private static void HidePostfix(UIMapActivityIconHint __instance)
        {
            MapModule m = MapModule.Instance;
            if (m == null || m.OrdersShownBy != __instance) return;
            m.OrdersShownBy = null;
            UITooltip.Hide();
        }

        /// <summary>Os pedidos aceitos da semana, na ordem da janela de pedidos; <c>null</c> se não há nenhum.</summary>
        private static List<LazyWidgetDataBase> Build()
        {
            VendorSystem vs = MainGame.Instance?.GameSave?.vendorSystem;
            WorldData world = MainGame.WorldData;
            if (vs?.currentOrders == null || vs.vendors == null || world == null) return null;
            WorldZoneData warehouse = world.GetWorldZoneDataById("warehouse");
            WorldZoneData cellar = world.GetWorldZoneDataById("warehouse_cellar");

            TextStyle header = PlaceInspect.Style("headerTextStyle"), text = PlaceInspect.Style("descriptionTextStyle");
            // Fileiras de até duas células (três passam da largura do tooltip). Cada célula lê o "entregue" do
            // inventário da fileira; o mesmo item pedido duas vezes vai para outra fileira para as contas não somarem. As
            // linhas de texto seguem a ordem de leitura das células.
            var rows = new List<(List<NeedItemData> need, List<Item> have, List<string> lines)>();
            foreach (SGuid guid in vs.currentOrders)
            {
                if (guid == null || guid.IsEmpty) continue;
                VendorOrderData order = Find(vs, guid);
                VendorOrderDef def = order?.Definition;
                if (def == null || def.count <= 0) continue;
                int delivered = order.State == VendorOrderState.Finished || order.IsFinishedThisWeek
                    ? def.count
                    : Count(warehouse, def.itemId) + Count(cellar, def.itemId) + order.Count;

                var row = rows.Find(r => r.need.Count < RowCells && !r.need.Exists(n => n.Id == def.itemId));
                if (row.need == null) rows.Add(row = (new List<NeedItemData>(), new List<Item>(), new List<string>()));
                row.need.Add(new NeedItemData(def.itemId, def.count));
                if (delivered > 0) row.have.Add(new Item(def.itemId, delivered));

                ItemDef item = GameBalance.Me.GetDataOrNull<ItemDef>(def.itemId);
                string name = item != null ? item.GetHeader() : def.itemId;
                row.lines.Add($"{name}: {"happiness".FontIcon()}+{def.happinessReward.EvaluateInt()}");
            }
            var cells = new List<LazyWidgetDataBase>();
            var lines = new List<string>();
            foreach (var (need, items, rowLines) in rows)
            {
                Inventory have = new Inventory("inventory", items.Count);
                ((List<Item>)ItemList.GetValue(have.Data)).AddRange(items);
                // Com CraftDef nulo e itens próprios a célula sai como item que falta: "entregue/pedido", vermelho até completar.
                var cell = new UITooltipNeedsItemWidgetData((CraftDef)null, new MultiInventory(have), need);
                if (cell.CraftItemCellsData.Count == 0) continue;
                cells.Add(cell);
                lines.AddRange(rowLines);
            }
            if (cells.Count == 0) return null;

            var list = new List<LazyWidgetDataBase> { new UITooltipTextWidgetData(LLBase.L("ui_orders"), TextAlignmentOptions.Center, header) };
            list.AddRange(cells);
            list.Add(new UITooltipSeparatorWidgetData());
            foreach (string l in lines) list.Add(new UITooltipTextWidgetData(l, TextAlignmentOptions.Center, text));
            Names.Scrub(list);
            return list;
        }

        private static VendorOrderData Find(VendorSystem vs, SGuid id)
        {
            foreach (Vendor v in vs.vendors)
            {
                if (v?.Orders == null) continue;
                foreach (VendorOrderData o in v.Orders)
                    if (o?.Guid != null && o.Guid.Guid == id.Guid) return o;
            }
            return null;
        }

        private static int Count(WorldZoneData zone, string itemId) =>
            zone == null || string.IsNullOrEmpty(itemId) ? 0 : zone.CountItemsOnTownPalettes(itemId);
    }
}
