using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using GK2Shared;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Crafting
{
    /// <summary>
    /// Na janela de escolher item dos baús (<c>UIMultiInventoryWindow</c>: ingrediente da alquimia, órgão, semente, sermão,
    /// coleira...), uma seção a mais no topo com só os itens que a tela aceita (os que o jogo acende), juntados por tipo
    /// com a soma de todos os baús e bolsas. É o mesmo widget de inventário do jogo.
    /// <list type="bullet">
    /// <item>Clique: repassa a célula real do maior monte ao callback da tela. Várias telas tiram o item pelo UID
    /// (<c>RemoveItemFromInventoryByUID</c>) ou adicionam o próprio item clicado, então a cópia somada só serve para mostrar.
    /// Itens com durabilidade não se juntam.</item>
    /// <item>Ordem e selo dependem da tela (<see cref="Kind"/>): runas na alquimia, caveiras na autópsia e no embalsamar,
    /// maestria no equipamento de zumbi, qualidade (a estrela já é da célula nativa) em oração, canteiro e túmulo. Com duas
    /// ordens, o botão do cabeçalho (clone do "mover parecidos" nativo) alterna; a escolha fica no .cfg por tela.</item>
    /// <item>Com <c>[ItemPicker] HideChests</c>, as seções de cada baú ficam escondidas (continuam desenhadas: o clique
    /// usa as células delas).</item>
    /// </list>
    /// </summary>
    internal sealed class ItemFinder : IDisposable
    {
        internal static readonly Loc Header = new Loc("Available", "Disponíveis");
        private static readonly Loc SortTitle = new Loc("Sort", "Ordenar");
        private static readonly Loc QualityName = new Loc("Quality", "Qualidade");
        private static readonly Loc QuantityName = new Loc("Quantity", "Quantidade");
        private static readonly Loc ClickTo = new Loc("Click: {0}", "Clique: {0}");
        private const string HeaderIcon = "comm-header_2-type_icon-store";
        private const string BadgeName = "GK2Crafting.Badge", SortName = "GK2Crafting.Sort";
        private const int Row = 5;
        /// <summary>Tons claros dos ícones de runa da fonte (rune_r/g/b), legíveis sobre a célula escura.</summary>
        private const string RuneRed = "#E06078", RuneGreen = "#59B454", RuneBlue = "#5893FF";
        /// <summary>Tons claros dos ícones de caveira vermelha e branca (rskull/wskull).</summary>
        private const string SkullRed = "#FF603B", SkullWhite = "#DAC9B5";

        internal enum Kind { Generic, Alchemy, Skulls, Equipment, Quality }
        internal enum Order { Quantity, Runes, Skulls, Best, Quality }

        private static ItemFinder current;
        private readonly bool hideChests;
        private readonly ConfigEntry<string> sortCfg;
        private Session session;

        public ItemFinder(Harmony h, bool hideChests)
        {
            current = this;
            this.hideChests = hideChests;
            sortCfg = CraftingModule.Instance.Host.Config.Bind("ItemPicker", "Sort", "",
                "Last sort chosen in each kind of item picker (managed by the mod).");
            h.Patch(AccessTools.Constructor(typeof(UIMultiInventoryWindowData), new[]
                {
                    typeof(PlayerData), typeof(Action<UIItemCell>), typeof(Func<Item, bool>), typeof(bool), typeof(string),
                    typeof(Func<Item, string>),
                }),
                postfix: new HarmonyMethod(typeof(ItemFinder), nameof(CtorPostfix)));
            h.Patch(AccessTools.Method(typeof(UIMultiInventoryWindow), nameof(UIMultiInventoryWindow.Redraw)),
                postfix: new HarmonyMethod(typeof(ItemFinder), nameof(RedrawPostfix)));
            h.Patch(AccessTools.Method(typeof(UIMultiInventoryWindow), "UpdateGamepadNavigation"),
                postfix: new HarmonyMethod(typeof(ItemFinder), nameof(RedrawPostfix)));
            h.Patch(AccessTools.Method(typeof(UIMultiInventoryWindow), nameof(UIMultiInventoryWindow.Hide)),
                prefix: new HarmonyMethod(typeof(ItemFinder), nameof(HidePrefix)));
        }

        public void Dispose()
        {
            Cleanup();
            if (current == this) current = null;
        }

        private sealed class Entry
        {
            public Item Shown, Real;
            public int Total;
        }

        /// <summary>Uma abertura da janela: a seção, as entradas e a tela que a abriu.</summary>
        private sealed class Session
        {
            public UIMultiInventoryWindowData Data;
            public InventoryWidgetData Widget;
            public Inventory Section;
            public Kind Kind;
            public Order[] Orders;
            public int OrderIndex;
            public List<Entry> Entries;
            public Dictionary<Item, Entry> ByShown;
            public Order Order => Orders[OrderIndex];
        }

        // ---------- dados ----------

        private static void CtorPostfix(UIMultiInventoryWindowData __instance, Action<UIItemCell> onCellClicked, Func<Item, bool> itemsAvailableCondition)
        {
            if (current == null || itemsAvailableCondition == null) return;
            try { current.AddSection(__instance, onCellClicked, itemsAvailableCondition); }
            catch (Exception e) { CraftingModule.Fail("ItemFinder", e, "UIMultiInventoryWindowData"); }
        }

        private void AddSection(UIMultiInventoryWindowData data, Action<UIItemCell> clicked, Func<Item, bool> available)
        {
            List<InventoryWidgetDataBase> list = data.MultiInventoryWidgetData.inventoriesData;
            if (list.Count == 0 || data.MultiInventory == null) return;
            InventoryWidgetDataBase native = list[0];

            var entries = new List<Entry>();
            var byId = new Dictionary<string, Entry>();
            foreach (Inventory inv in data.MultiInventory.inventoryList)
            {
                if (inv?.Data?.Inventory == null) continue;
                foreach (Item item in inv.Data.Inventory)
                {
                    if (item == null || item.IsEmpty) continue;
                    if (item.IsBag) { foreach (Item inner in item.Inventory) Collect(inner, available, entries, byId); }
                    else Collect(item, available, entries, byId);
                }
            }
            if (entries.Count == 0) return;

            Kind kind = Detect(clicked, available);
            CraftingModule.Log.LogInfo($"ItemFinder: {kind} ({clicked?.Method.DeclaringType?.FullName}.{clicked?.Method.Name}), {entries.Count} tipos");
            var s = new Session
            {
                Data = data, Kind = kind, Orders = OrdersOf(kind), Entries = entries, ByShown = new Dictionary<Item, Entry>(),
                Section = new Inventory("gk2crafting_available", (entries.Count + Row - 1) / Row * Row),
            };
            s.OrderIndex = Math.Max(0, Array.IndexOf(s.Orders, SavedOrder(kind, s.Orders[0])));
            foreach (Entry e in entries)
            {
                e.Shown = new Item(e.Real.id, e.Total);
                s.ByShown[e.Shown] = e;
            }
            Fill(s);

            Action<UIItemCell> press = native.OnItemCellPress;
            Action<UIItemCell> forward = cell =>
            {
                try
                {
                    if (cell?.DisplayingItem == null || !s.ByShown.TryGetValue(cell.DisplayingItem, out Entry e)) return;
                    UIItemCell real = FindDrawnCell(e.Real);
                    if (real == null)
                    {
                        CraftingModule.Log.LogWarning($"ItemFinder: célula de {e.Real.id} não está desenhada");
                        return;
                    }
                    press?.Invoke(real);
                }
                catch (Exception ex) { CraftingModule.Fail("ItemFinder", ex, "click"); }
            };
            var header = new InventoryHeaderWidgetData(s.Section, HeaderIcon, Header.Current);
            s.Widget = new InventoryWidgetData(s.Section, header, native.OnItemCellOver, native.OnItemCellOut, forward, null, null, available)
            {
                ExtraRedTooltipLocIdProvider = native.ExtraRedTooltipLocIdProvider,
            };
            list.Insert(0, s.Widget);
            session = s;
        }

        private static void Collect(Item item, Func<Item, bool> available, List<Entry> entries, Dictionary<string, Entry> byId)
        {
            if (item == null || item.IsEmpty || item.IsBag || !available(item)) return;
            bool stacks = !item.HasProperty<DurabilitySerializedItemProperty>();
            if (stacks && byId.TryGetValue(item.id, out Entry e))
            {
                e.Total += item.Count;
                if (item.Count > e.Real.Count) e.Real = item;
                return;
            }
            e = new Entry { Real = item, Total = item.Count };
            entries.Add(e);
            if (stacks) byId[item.id] = e;
        }

        /// <summary>Quem abriu a janela, pelo tipo que declara o callback (a lambda mora numa classe aninhada da tela).</summary>
        internal static Kind Detect(Action<UIItemCell> clicked, Func<Item, bool> available)
        {
            foreach (Delegate d in new Delegate[] { clicked, available })
            {
                if (d == null) continue;
                Type t = d.Method.DeclaringType;
                while (t != null && t.DeclaringType != null) t = t.DeclaringType;
                switch (t?.Name)
                {
                    case "UIAlchemyWindow": return Kind.Alchemy;
                    case "UIAutopsyWindowData":
                    case "UIEmbalmWindowData": return Kind.Skulls;
                    case "ZombieEquipmentInventoryWidgetData": return d.Method.Name.Contains("Collar") ? Kind.Generic : Kind.Equipment;
                    case "UIPrayWindowData":
                    case "UIGardenBedWindow":
                    case "UIGraveElementWidgetData": return Kind.Quality;
                }
            }
            return Kind.Generic;
        }

        private static Order[] OrdersOf(Kind k)
        {
            switch (k)
            {
                case Kind.Alchemy: return new[] { Order.Runes, Order.Quantity };
                case Kind.Skulls: return new[] { Order.Skulls, Order.Quantity };
                case Kind.Quality: return new[] { Order.Quality, Order.Quantity };
                case Kind.Equipment: return new[] { Order.Best };
                default: return new[] { Order.Quantity };
            }
        }

        private Order SavedOrder(Kind k, Order fallback)
        {
            foreach (string part in (sortCfg.Value ?? "").Split(';'))
            {
                string[] kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == k.ToString() && Enum.TryParse(kv[1], out Order o)) return o;
            }
            return fallback;
        }

        private void SaveOrder(Kind k, Order o)
        {
            var map = (sortCfg.Value ?? "").Split(';').Select(p => p.Split('=')).Where(kv => kv.Length == 2 && kv[0] != k.ToString())
                .Select(kv => kv[0] + "=" + kv[1]).ToList();
            map.Add(k + "=" + o);
            sortCfg.Value = string.Join(";", map);
        }

        /// <summary>Ordena as entradas e refaz o conteúdo da seção (mesmo objeto de inventário, só a ordem muda).</summary>
        private static void Fill(Session s)
        {
            Order o = s.Order;
            List<Entry> sorted = s.Entries
                .OrderBy(e => Key(e, o).Item1).ThenByDescending(e => Key(e, o).Item2).ThenByDescending(e => Key(e, o).Item3)
                .ThenByDescending(e => e.Total).ThenBy(e => e.Real.Definition?.sortOrder ?? 0).ThenBy(e => e.Real.id, StringComparer.Ordinal)
                .ToList();
            s.Entries = sorted;
            List<Item> inv = s.Section.Data.Inventory;
            inv.Clear();
            foreach (Entry e in sorted) inv.Add(e.Shown);
        }

        /// <summary>(grupo crescente, valor principal decrescente, desempate decrescente).</summary>
        private static (int, float, float) Key(Entry e, Order o)
        {
            ItemDef d = e.Real.Definition;
            if (d == null) return (9, 0, 0);
            switch (o)
            {
                case Order.Runes:
                {
                    if (!RunesKnown(d, out Vector3Int r)) return (r == Vector3Int.zero ? 4 : 3, 0, 0);
                    int g = r.x >= r.y && r.x >= r.z ? 0 : r.y >= r.z ? 1 : 2;
                    return (g, g == 0 ? r.x : g == 1 ? r.y : r.z, r.x + r.y + r.z);
                }
                case Order.Skulls: return (0, d.redSkulls, d.whiteSkulls);
                case Order.Quality: return (0, d.quality, 0);
                case Order.Best: return (0, d.isTool ? d.talentBonus : d.quality, Durability(e.Real));
                default: return (0, e.Total, 0);
            }
        }

        /// <summary>Runas do item como o tooltip nativo as mostra: só com a pesquisa do item concluída.</summary>
        internal static bool RunesKnown(ItemDef d, out Vector3Int runes)
        {
            runes = d.GetRunesAsVector3Int();
            SurveyDef survey = GameBalance.GetSurveyDefForItemOrNull(d.id);
            return runes != Vector3Int.zero && survey != null && MainGame.Instance.GameSave.knowledgeSystem.IsSurveyCompleted(survey);
        }

        private static float Durability(Item item) =>
            item.TryGetProperty(out DurabilitySerializedItemProperty p) ? p.Durability : 0f;

        /// <summary>Texto do selo no canto de cima à esquerda da célula; nulo quando a tela não tem nada a acrescentar.</summary>
        internal static string BadgeText(Kind k, Item item)
        {
            ItemDef d = item.Definition;
            if (d == null) return null;
            switch (k)
            {
                case Kind.Alchemy:
                {
                    if (!RunesKnown(d, out Vector3Int r)) return r == Vector3Int.zero ? null : "?";
                    // Números na cor de cada runa: o ícone da fonte tem 12 px de arte e três não cabem na célula de 42.
                    // A legenda (os ícones) fica no botão de ordem do cabeçalho.
                    var parts = new List<string>();
                    if (r.x > 0) parts.Add("<color=" + RuneRed + ">" + r.x + "</color>");
                    if (r.y > 0) parts.Add("<color=" + RuneGreen + ">" + r.y + "</color>");
                    if (r.z > 0) parts.Add("<color=" + RuneBlue + ">" + r.z + "</color>");
                    return string.Join(" ", parts);
                }
                case Kind.Skulls:
                {
                    // Como as runas: números na cor de cada caveira (o ícone da caveira branca, com a coroa, cobre o item).
                    var parts = new List<string>();
                    if (d.redSkulls > 0) parts.Add("<color=" + SkullRed + ">" + d.redSkulls + "</color>");
                    if (d.whiteSkulls > 0) parts.Add("<color=" + SkullWhite + ">" + d.whiteSkulls + "</color>");
                    return parts.Count > 0 ? string.Join(" ", parts) : null;
                }
                case Kind.Equipment:
                    if (d.isTool && d.talentBonus > 0 && d.talentIds.Count > 0) return d.talentIds[0].FontIcon() + "+" + d.talentBonus;
                    if (d.type == ItemType.BodyArmor && d.quality != 0) return "equip_icon_armor".FontIcon() + d.quality;
                    return null;
                default: return null;
            }
        }

        private static string OrderName(Order o)
        {
            switch (o)
            {
                case Order.Runes: return LLBase.L("item_runes");
                case Order.Skulls: return LLBase.L("ui_skulls");
                case Order.Best: return LLBase.L("ui_mastery");
                case Order.Quality: return QualityName.Current;
                default: return QuantityName.Current;
            }
        }

        private static string OrderGlyph(Order o)
        {
            switch (o)
            {
                case Order.Runes: return "rune_r".FontIcon() + "<space=-2>" + "rune_g".FontIcon() + "<space=-2>" + "rune_b".FontIcon();
                case Order.Skulls: return "rskull".FontIcon() + "<space=-2>" + "wskull".FontIcon();
                case Order.Quality: return "icon_star".FontIcon();
                default: return "123";
            }
        }

        // ---------- desenho ----------

        private static MultiInventoryWidget WidgetOf(UIMultiInventoryWindow win) =>
            AccessTools.Field(typeof(UIMultiInventoryWindow), "multiInventoryWidget").GetValue(win) as MultiInventoryWidget;

        private static void RedrawPostfix(UIMultiInventoryWindow __instance)
        {
            try { current?.Decorate(__instance); }
            catch (Exception e) { CraftingModule.Fail("ItemFinder", e, "UIMultiInventoryWindow.Redraw"); }
        }

        private static void HidePrefix()
        {
            try { current?.Cleanup(); }
            catch (Exception e) { CraftingModule.Fail("ItemFinder", e, "UIMultiInventoryWindow.Hide"); }
        }

        private void Decorate(UIMultiInventoryWindow win)
        {
            Session s = session;
            MultiInventoryWidget widget = WidgetOf(win);
            if (s == null || widget == null || !ReferenceEquals(AccessTools.Field(typeof(LazyWindow<UIMultiInventoryWindowData>), "data").GetValue(win), s.Data)) return;
            List<InventoryWidget> drawn = widget.DrawnInventories;
            InventoryWidget top = drawn.FirstOrDefault(w => ReferenceEquals(w.Data, s.Widget));
            if (top == null) return;

            TextMeshProUGUI countProto = null;
            foreach (UIItemCell cell in top.Cells)
            {
                if (cell == null) continue;
                RemoveBadge(cell);
                var label = AccessTools.Field(typeof(UIItemCell), "countLabel").GetValue(cell) as TextMeshProUGUI;
                if (countProto == null && label != null) countProto = label;
                Item item = cell.DisplayingItem;
                if (item == null || item.IsEmpty || label == null) continue;
                string text = BadgeText(s.Kind, item);
                if (string.IsNullOrEmpty(text)) continue;
                AddBadge(cell, label, text);
            }
            SortButton(top, s, countProto);

            bool changed = false;
            foreach (InventoryWidget w in drawn)
            {
                if (ReferenceEquals(w, top)) continue;
                bool show = !hideChests;
                if (w.gameObject.activeSelf != show) { w.gameObject.SetActive(show); changed = true; }
            }
            if (changed && top.transform.parent is RectTransform content) LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        }

        private static void AddBadge(UIItemCell cell, TextMeshProUGUI countLabel, string text)
        {
            var go = Object.Instantiate(countLabel.gameObject, countLabel.transform.parent, false);
            go.name = BadgeName;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(40f, 10f);
            rt.anchoredPosition = new Vector2(2f, -1f);
            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            tmp.text = text;
            go.SetActive(true);
        }

        private static void RemoveBadge(UIItemCell cell)
        {
            foreach (Transform t in cell.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == BadgeName) Discard(t);
        }

        /// <summary>Botão do cabeçalho da seção que alterna a ordem: clone do "mover parecidos" nativo, com o ícone da
        /// ordem atual no lugar do desenho dele.</summary>
        private void SortButton(InventoryWidget top, Session s, TextMeshProUGUI labelProto)
        {
            InventoryHeaderWidget header = top.GetComponentInChildren<InventoryHeaderWidget>(true);
            if (header == null) return;
            Discard(header.transform.Find(SortName));
            if (s.Orders.Length < 2 || labelProto == null) return;
            var native = AccessTools.Field(typeof(InventoryHeaderWidget), "moveAllSimilarItemsToBagBtn").GetValue(header) as LazyButton;
            if (native == null) return;

            var go = Object.Instantiate(native.gameObject, native.transform.parent, false);
            go.name = SortName;
            go.SetActive(true);
            var btn = go.GetComponent<LazyButton>();
            btn.onClick.RemoveAllListeners();
            btn.interactable = true;
            Transform icon = go.transform.Find("BtnImage/Icon");
            if (icon != null) icon.gameObject.SetActive(false);
            var labelGo = Object.Instantiate(labelProto.gameObject, icon != null ? icon.parent : go.transform, false);
            labelGo.name = "Label";
            var lrt = (RectTransform)labelGo.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.pivot = new Vector2(0.5f, 0.5f);
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            lrt.anchoredPosition = new Vector2(0f, -1f);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.raycastTarget = false;
            tmp.text = OrderGlyph(s.Order);
            labelGo.SetActive(true);

            Order next = s.Orders[(s.OrderIndex + 1) % s.Orders.Length];
            // Texto próprio: o Attach só mostra chave de tradução que existe; o AttachCustom chama o tooltip nativo direto.
            string tip = OrderName(s.Order) + "\n" + string.Format(ClickTo.Current, OrderName(next));
            var target = (RectTransform)go.transform;
            UIMouseTooltip.AttachCustom(go, () => UITooltip.ShowSimpleInfo(target, tip, Vector2.zero, SortTitle.Current));
            btn.onClick.AddListener(() =>
            {
                try
                {
                    UIMouseTooltip.HideIfShowingAt((RectTransform)go.transform, immediately: true);
                    s.OrderIndex = (s.OrderIndex + 1) % s.Orders.Length;
                    SaveOrder(s.Kind, s.Order);
                    Fill(s);
                    // Só a seção: o Draw da janela inteira não solta as seções antigas (duplicaria e o Hide nativo quebraria).
                    top.Redraw();
                    UIMultiInventoryWindow win = LazyUI.GetWindow<UIMultiInventoryWindow>();
                    if (win.IsShown) Decorate(win);
                }
                catch (Exception ex) { CraftingModule.Fail("ItemFinder", ex, "sort"); }
            });
        }

        /// <summary>Tira o que foi posto em widgets do pool (selos, botão) e devolve as seções escondidas.</summary>
        private void Cleanup()
        {
            if (!LazyUI.IsInitialized) return;
            UIMultiInventoryWindow win = LazyUI.GetWindow<UIMultiInventoryWindow>();
            MultiInventoryWidget widget = win != null ? WidgetOf(win) : null;
            if (widget != null)
            {
                foreach (InventoryWidget w in widget.DrawnInventories)
                {
                    if (w == null) continue;
                    if (!w.gameObject.activeSelf) w.gameObject.SetActive(true);
                    foreach (UIItemCell c in w.Cells) if (c != null) RemoveBadge(c);
                    InventoryHeaderWidget header = w.GetComponentInChildren<InventoryHeaderWidget>(true);
                    Discard(header != null ? header.transform.Find(SortName) : null);
                }
            }
        }

        /// <summary>O Destroy só age no fim do quadro: tira o nome e esconde já, para o próximo Find não achar o velho.</summary>
        private static void Discard(Transform t)
        {
            if (t == null) return;
            t.name = "GK2Crafting.Discarded";
            t.gameObject.SetActive(false);
            Object.Destroy(t.gameObject);
        }

        private static UIItemCell FindDrawnCell(Item real)
        {
            MultiInventoryWidget widget = WidgetOf(LazyUI.GetWindow<UIMultiInventoryWindow>());
            if (widget == null) return null;
            foreach (InventoryWidget inv in widget.DrawnInventories)
                foreach (UIItemCell c in inv.Cells)
                    if (c != null && c.DisplayingItem == real) return c;
            return null;
        }
    }
}
