using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.Tooltips
{
    internal enum EntityKind { Item, Craft, Perk, Station }

    /// <summary>A entidade por trás de um tooltip: gravada pelos prefixes dos Show* e consumida no funil.</summary>
    internal sealed class Entity
    {
        public EntityKind Kind;
        public string Id;
        public ItemDef ItemDef;
        public Item Instance;      // só no hover de célula com item real (durabilidade)
        public CraftDef CraftDef;
        public WgoData Wgo;        // estação do ShowCraftInfo
        public PerkDef PerkDef;
        public RectTransform Target;   // alvo do Show* que gravou a entidade
        public int Frame;              // frame em que foi gravada (o funil só usa se bater alvo e frame)

        public static Entity ForItem(ItemDef def, Item instance = null) =>
            def == null ? null : new Entity { Kind = EntityKind.Item, Id = def.id, ItemDef = def, Instance = instance };

        public static Entity ForCraft(CraftDef def, WgoData wgo = null) =>
            def == null ? null : new Entity { Kind = EntityKind.Craft, Id = def.id, CraftDef = def, Wgo = wgo };

        public static Entity ForPerk(PerkDef def) =>
            def == null ? null : new Entity { Kind = EntityKind.Perk, Id = def.id, PerkDef = def };

        public static Entity ForStation(string wgoId) =>
            string.IsNullOrEmpty(wgoId) ? null : new Entity { Kind = EntityKind.Station, Id = wgoId };

        /// <summary>Resolve um id de link (tipo:id) a partir do balanço.</summary>
        public static Entity FromLink(string link)
        {
            if (string.IsNullOrEmpty(link)) return null;
            int c = link.IndexOf(':');
            if (c <= 0) return null;
            string kind = link.Substring(0, c), id = link.Substring(c + 1);
            GameBalance gb = GameBalance.Me;
            switch (kind)
            {
                case Links.Item: return ForItem(gb.GetDataOrNull<ItemDef>(id));
                case Links.Craft: return id.StartsWith("mix") ? null : ForCraft(gb.GetDataOrNull<CraftDef>(id));
                case Links.Perk: return ForPerk(gb.GetDataOrNull<PerkDef>(id));
                case Links.Station: return ForStation(id);
            }
            return null;
        }
    }

    /// <summary>Acesso ao UITooltip nativo: singleton, TextStyles privados e os Add*Widgets privados.</summary>
    internal static class Native
    {
        private static readonly FieldInfo InstanceField = AccessTools.Field(typeof(UITooltip), "instance");
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(UITooltip), "target");
        private static readonly FieldInfo CanvasGroupField = AccessTools.Field(typeof(UITooltip), "canvasGroup");
        private static readonly FieldInfo CornersField = AccessTools.Field(typeof(UIBasicBubble), "corners");
        public static readonly FieldInfo UsedWidgetsField = AccessTools.Field(typeof(LazyWidgetContainer), "usedWidgetsList");

        public static readonly MethodInfo ShowAt = AccessTools.Method(typeof(UITooltip), "ShowAtTargetAndCalculateOffsets");
        private static readonly MethodInfo AddItemWidgetsM = AccessTools.Method(typeof(UITooltip), "AddItemWidgets");
        private static readonly MethodInfo AddPerkWidgetsM = AccessTools.Method(typeof(UITooltip), "AddPerkWidgets");
        private static readonly MethodInfo TryAddNeedItemsWidgetM = AccessTools.Method(typeof(UITooltip), "TryAddNeedItemsWidget");
        private static readonly MethodInfo TryAddRequiredExtensionWidgetM = AccessTools.Method(typeof(UITooltip), "TryAddRequiredExtensionWidget");

        public static UITooltip Instance => (UITooltip)InstanceField.GetValue(null);

        public static RectTransform TargetOf(UITooltip t) => t == null ? null : (RectTransform)TargetField.GetValue(t);

        public static void SetTarget(UITooltip t, RectTransform target) => TargetField.SetValue(t, target);

        public static CanvasGroup GroupOf(UITooltip t) => (CanvasGroup)CanvasGroupField.GetValue(t);

        public static IEnumerable<UIBubbleCorner> Corners(UITooltip t) => (List<UIBubbleCorner>)CornersField.GetValue(t);

        public static bool IsVisible(UITooltip t)
        {
            if (t == null || !t.gameObject.activeSelf) return false;
            CanvasGroup g = GroupOf(t);
            return g == null || g.alpha > 0.5f;
        }

        private static readonly Dictionary<string, FieldInfo> StyleFields = new Dictionary<string, FieldInfo>();

        public static TextStyle Style(string field)
        {
            if (!StyleFields.TryGetValue(field, out FieldInfo fi)) StyleFields[field] = fi = AccessTools.Field(typeof(UITooltip), field);
            return (TextStyle)fi.GetValue(Instance);
        }

        public static void AddItemWidgets(List<LazyWidgetDataBase> list, ItemDef def, bool addHeader = true, bool notShowStudy = false, bool noCraftedAt = false, IReadOnlyList<ItemDef> variants = null) =>
            AddItemWidgetsM.Invoke(null, new object[] { list, def, addHeader, "", notShowStudy, noCraftedAt, variants });

        public static void AddPerkWidgets(List<LazyWidgetDataBase> list, PerkDef def) =>
            AddPerkWidgetsM.Invoke(null, new object[] { list, def, true, "" });

        public static void TryAddNeedItemsWidget(List<LazyWidgetDataBase> list, List<NeedItemData> needs) =>
            TryAddNeedItemsWidgetM.Invoke(null, new object[] { list, needs });

        public static void TryAddRequiredExtensionWidget(List<LazyWidgetDataBase> list, CraftDef def) =>
            TryAddRequiredExtensionWidgetM.Invoke(null, new object[] { list, def, null });
    }

    /// <summary>TextStyles do próprio UITooltip, lidos a cada uso (seguem o idioma e o asset do jogo).</summary>
    internal sealed class Styles
    {
        public TextStyle Header, HeaderBold, Description, Small, SmallSelected, Term;
        public string Lang;
        public string CraftedAtText;    // LLBase.L("ui_crafted_at") no idioma atual
        public string CraftedAtLabel;   // rótulo "Crafted at: " já estilizado como o jogo monta

        private static Styles cached;
        private static UITooltip cachedFor;

        /// <summary>Cacheado por idioma e instância do UITooltip (o ShowItemCell roda a cada Draw da célula).</summary>
        public static Styles Read()
        {
            UITooltip inst = Native.Instance;
            if (inst == null) return null;
            string lang = LLBase.CurrentLang;
            if (cached != null && cachedFor == inst && cached.Lang == lang) return cached;
            var s = new Styles
            {
                Lang = lang,
                Header = Native.Style("headerTextStyle"),
                HeaderBold = Native.Style("headerBoldTextStyle"),
                Description = Native.Style("descriptionTextStyle"),
                Small = Native.Style("smallDescriptionTextStyle"),
                SmallSelected = Native.Style("selectedSmallDescriptionTextStyle"),
                Term = Native.Style("perkDescriptionTextStyle"),
            };
            s.CraftedAtText = LLBase.L("ui_crafted_at");
            s.CraftedAtLabel = s.SmallSelected.ApplyStyleToString(s.CraftedAtText + ": ") ?? "";
            cached = s;
            cachedFor = inst;
            return s;
        }
    }
}
