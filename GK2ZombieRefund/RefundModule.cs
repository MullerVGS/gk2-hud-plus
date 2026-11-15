using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2ZombieRefund
{
    /// <summary>
    /// Desfaz a compra de um perk de zumbi na árvore de perks: clique direito no nó comprado abre a janela de diálogo
    /// nativa (Sim/Não), e o tooltip nativo do perk ganha uma linha com a dica. Devolve os pontos e a caveira pela mesma
    /// lógica do respec que o jogo tem e não usa
    /// (<c>ZombieWgoData.RespecSpentTalents</c>), um nó por vez e só quando nenhum perk comprado depende dele.
    /// </summary>
    internal sealed class RefundModule
    {
        public const string LegacyGuid = "arthur.gk2.zombierefund";

        internal static RefundModule Instance;
        internal static ManualLogSource Log;

        internal readonly ModHost Host;
        private readonly List<Component> added = new List<Component>();

        public RefundModule(ModHost host)
        {
            Host = host;
            Instance = this;
            Log = host.Log;
            host.Module("zombierefund", null, () => { if (Instance == this) Instance = null; });
            host.Feature("Refund", "gameplay", "ZombieRefund", "Enabled",
                new Loc("Zombie perk refund", "Devolver perk de zumbi"),
                new Loc("Right-click a bought zombie perk to get its points back.", "Clique direito num perk de zumbi comprado para ter os pontos de volta."),
                h =>
                {
                    h.Patch(AccessTools.Method(typeof(TalentLevelUpWidget), nameof(TalentLevelUpWidget.Redraw)),
                        postfix: new HarmonyMethod(typeof(RefundModule), nameof(RedrawPostfix)));
                    h.Patch(AccessTools.Method(typeof(UITooltip), nameof(UITooltip.ShowTalentLevelUpWidget)),
                        prefix: new HarmonyMethod(typeof(Tooltip), nameof(Tooltip.Prefix)));
                    foreach (TalentLevelUpWidget w in Object.FindObjectsByType<TalentLevelUpWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (w.Data != null) Decorate(w);
                    return this;
                }, dispose: p => p.RemoveAdded(), gameplay: true);
        }

        /// <summary>Erro fora dos patches (clique, diálogo): conta para a parte e, repetido, desliga o refund.</summary>
        internal static void Fail(Exception e, string where) => Instance?.Host.Guard.Report("Refund", e, where);

        private void RemoveAdded()
        {
            foreach (Component c in added) if (c != null) Object.Destroy(c);
            added.Clear();
            UIDialogWindow dialog = LazyUI.IsInitialized ? LazyUI.GetWindow<UIDialogWindow>() : null;
            if (dialog != null && dialog.IsShown && Confirm.Open) dialog.Close();
        }

        private static void RedrawPostfix(TalentLevelUpWidget __instance)
        {
            try { Instance?.Decorate(__instance); }
            catch (Exception e) { Fail(e, "TalentLevelUpWidget.Redraw"); }
        }

        private void Decorate(TalentLevelUpWidget w)
        {
            TalentLevelUpWidgetData d = w.Data;
            bool zombie = d != null && d.Def != null && d.Def.isZombiePerk && d.ZombieWgoData != null;
            if (zombie && w.GetComponent<RightClick>() == null) added.Add(w.gameObject.AddComponent<RightClick>());
        }
    }

    internal static class Refund
    {
        public static bool CanRefund(ZombieWgoData z, TalentLevelUpDef def)
        {
            if (!def.isZombiePerk || def.availableAtStart) return false;
            ZombieTalentData branch = z.GetTalentBranch(def.talentId);
            if (branch == null || !branch.studiedLevelUps.Contains(def.id)) return false;
            // Nenhum perk comprado pode perder o pai (lockType All: todos os pais comprados; Any: ao menos um).
            foreach (string other in branch.studiedLevelUps)
            {
                TalentLevelUpDef o = GameBalance.Me.GetDataOrNull<TalentLevelUpDef>(other);
                if (o == null || o.parents == null || !o.parents.Contains(def.id)) continue;
                if (o.lockType == TalentLevelUpDef.LockType.All) return false;
                if (!o.parents.Any(p => p != def.id && branch.studiedLevelUps.Contains(p))) return false;
            }
            return true;
        }

        /// <summary>O inverso de <c>PurchaseTalentLevelUp</c>, como em <c>RespecSpentTalents</c>, para um nó.</summary>
        public static bool Do(ZombieWgoData z, TalentLevelUpDef def)
        {
            if (!CanRefund(z, def)) return false;
            ZombieTalentData branch = z.GetTalentBranch(def.talentId);
            if (!z.disabledTalentLevelUps.Remove(def.id))
            {
                if (!string.IsNullOrEmpty(def.linkedPerk)) z.RemovePerk(def.linkedPerk);
                branch.curTalentValue = Math.Max(0, branch.curTalentValue - def.talentValueAdd);
            }
            branch.studiedLevelUps.Remove(def.id);
            z.CheckRedSkulls(addRedSkulls: true);
            z.DoTechPointsReward(z, def.techRed, def.techGreen, def.techBlue);
            RaisePurchased();
            LazyAudio.PlayAndForget("coins_sound");
            RefundModule.Log.LogInfo($"{def.id} devolvido: {LLBase.L(z.Name)} {def.talentId}={branch.curTalentValue}, tech R{z.techRed} G{z.techGreen} B{z.techBlue}, perks {z.GetUsedPerksCount()}/{z.RedSkulls}");
            return true;
        }

        private static void RaisePurchased()
        {
            FieldInfo f = typeof(ZombieWgoData).GetField("OnTalentLevelUpPurchased", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            (f?.GetValue(null) as Action)?.Invoke();
        }

        /// <summary>Título do perk como no tooltip nativo (<c>UITooltip.AddTalentLevelUpWidgets</c>).</summary>
        public static string Header(TalentLevelUpDef def)
        {
            if (!string.IsNullOrEmpty(def.linkedPerk))
            {
                PerkDef p = GameBalance.Me.GetData<PerkDef>(def.linkedPerk);
                if (p != null) return p.GetHeader();
            }
            // Sem perk ligado o nível só soma maestria e não tem nome traduzido: o mesmo título do tooltip nativo.
            return string.Format("{0}: {1} +{2}", LLBase.L("ui_add_mastery"), def.talentId.FontIcon(), def.talentValueAdd);
        }

        public static string Points(TalentLevelUpDef def) => def.ZombieTechPointsIcon + def.ZombieTechPoints;
    }

    /// <summary>Janela de diálogo nativa: "Devolver este perk?" com Sim/Não.</summary>
    internal static class Confirm
    {
        public static bool Open;

        public static void Ask(TalentLevelUpWidget w)
        {
            TalentLevelUpWidgetData d = w != null ? w.Data : null;
            if (d?.Def == null || d.ZombieWgoData == null || !Refund.CanRefund(d.ZombieWgoData, d.Def)) return;
            ZombieWgoData z = d.ZombieWgoData;
            TalentLevelUpDef def = d.Def;
            UIDialogWindow window = LazyUI.GetWindow<UIDialogWindow>();
            UITooltip.Hide();
            string info = L10n.T("ask", Refund.Points(def), "rskull".FontIcon());
            window.Open(new UIDialogWindowData(Refund.Header(def), info, () =>
            {
                Open = false;
                window.Close();
                if (Refund.Do(z, def) && w != null) w.Redraw();
            }, () =>
            {
                Open = false;
                window.Close();
            }));
            Open = true;
        }
    }

    /// <summary>Linha a mais no tooltip nativo do perk comprado que pode ser devolvido.</summary>
    internal static class Tooltip
    {
        private static readonly MethodInfo AddWidgets = AccessTools.Method(typeof(UITooltip), "AddTalentLevelUpWidgets");
        private static readonly MethodInfo ShowAt = AccessTools.Method(typeof(UITooltip), "ShowAtTargetAndCalculateOffsets");
        private static readonly FieldInfo InstanceField = AccessTools.Field(typeof(UITooltip), "instance");
        private static readonly FieldInfo SmallStyle = AccessTools.Field(typeof(UITooltip), "smallDescriptionTextStyle");

        public static bool Prefix(TalentLevelUpWidget widget)
        {
            try
            {
                TalentLevelUpWidgetData d = widget != null ? widget.Data : null;
                if (d?.Def == null || d.ZombieWgoData == null || !Refund.CanRefund(d.ZombieWgoData, d.Def)) return true;
                object instance = InstanceField.GetValue(null);
                if (instance == null || AddWidgets == null || ShowAt == null) return true;
                var list = new List<LazyWidgetDataBase>();
                AddWidgets.Invoke(null, new object[] { list, d.Def });
                if (list.Count > 0 && !(list[list.Count - 1] is UITooltipSeparatorWidgetData)) list.Add(new UITooltipSeparatorWidgetData());
                list.Add(new UITooltipTextWidgetData(L10n.T("hint", Refund.Points(d.Def)), TMPro.TextAlignmentOptions.Center, (TextStyle)SmallStyle.GetValue(instance)));
                object[] args = ShowAt.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                args[0] = list;
                args[1] = widget.transform as RectTransform;
                if (args.Length > 4) args[4] = default(Vector2);
                ShowAt.Invoke(instance, args);
                return false;
            }
            catch (Exception e)
            {
                RefundModule.Fail(e, "UITooltip.ShowTalentLevelUpWidget");
                return true;
            }
        }
    }

    internal static class L10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["hint"] = "Right-click: refund ({0})",
            ["ask"] = "Refund this perk?\n{0} and 1 {1} back.",
        };

        private static readonly Dictionary<string, string> Pt = new Dictionary<string, string>
        {
            ["hint"] = "Clique direito: devolver ({0})",
            ["ask"] = "Devolver este perk?\nVoltam {0} e 1 {1}.",
        };

        public static string T(string key, params object[] args)
        {
            Dictionary<string, string> t = LLBase.CurrentLang.StartsWith("pt") ? Pt : En;
            return string.Format(t.TryGetValue(key, out string s) ? s : En[key], args);
        }
    }

    /// <summary>Clique direito no nó comprado.</summary>
    internal sealed class RightClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            try { Confirm.Ask(GetComponent<TalentLevelUpWidget>()); }
            catch (Exception ex) { RefundModule.Fail(ex, "clique direito"); }
        }
    }
}
