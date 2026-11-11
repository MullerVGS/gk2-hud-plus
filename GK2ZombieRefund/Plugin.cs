using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GK2ZombieRefund
{
    /// <summary>
    /// Desfaz a compra de um perk de zumbi na árvore de perks: um X nativo no canto do nó comprado ou clique direito no
    /// nó. Devolve os pontos e a caveira pela mesma lógica do respec que o jogo tem e não usa
    /// (<c>ZombieWgoData.RespecSpentTalents</c>), um nó por vez e só quando nenhum perk comprado depende dele.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.zombierefund";
        public const string Name = "GK2 Zombie Refund";
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony harmony;
        private readonly List<GameObject> created = new List<GameObject>();
        private readonly List<Component> added = new List<Component>();
        private Sprite cross;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony = new Harmony(Guid);
            harmony.Patch(AccessTools.Method(typeof(TalentLevelUpWidget), nameof(TalentLevelUpWidget.Redraw)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(RedrawPostfix)));
            foreach (TalentLevelUpWidget w in FindObjectsByType<TalentLevelUpWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (w.Data != null) Decorate(w);
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            foreach (GameObject go in created) if (go != null) Destroy(go);
            foreach (Component c in added) if (c != null) Destroy(c);
            created.Clear();
            added.Clear();
            if (Instance == this) Instance = null;
        }

        private static void RedrawPostfix(TalentLevelUpWidget __instance)
        {
            try { Instance?.Decorate(__instance); }
            catch (Exception e) { Log.LogError(e); }
        }

        private void Decorate(TalentLevelUpWidget w)
        {
            TalentLevelUpWidgetData d = w.Data;
            bool zombie = d != null && d.Def != null && d.Def.isZombiePerk && d.ZombieWgoData != null;
            if (zombie && w.GetComponent<RightClick>() == null) added.Add(w.gameObject.AddComponent<RightClick>());
            Transform x = w.transform.Find(XName);
            bool show = zombie && Refund.CanRefund(d.ZombieWgoData, d.Def);
            if (!show)
            {
                if (x != null) x.gameObject.SetActive(false);
                return;
            }
            if (x == null)
            {
                Sprite sp = CrossSprite();
                if (sp == null) return;
                var go = new GameObject(XName, typeof(RectTransform), typeof(Image), typeof(CrossClick));
                var rt = (RectTransform)go.transform;
                rt.SetParent(w.transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(sp.rect.width, sp.rect.height);
                rt.anchoredPosition = new Vector2(1f, 0f);
                go.GetComponent<Image>().sprite = sp;
                created.Add(go);
                x = rt;
            }
            x.SetAsLastSibling();
            x.gameObject.SetActive(true);
        }

        private const string XName = "GK2ZombieRefund_X";

        private Sprite CrossSprite()
        {
            if (cross == null) cross = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(s => s.name == "btn_i-close_cross-s");
            return cross;
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
            Plugin.Log.LogInfo($"{def.id} devolvido: {LLBase.L(z.Name)} {def.talentId}={branch.curTalentValue}, tech R{z.techRed} G{z.techGreen} B{z.techBlue}, perks {z.GetUsedPerksCount()}/{z.RedSkulls}");
            return true;
        }

        private static void RaisePurchased()
        {
            FieldInfo f = typeof(ZombieWgoData).GetField("OnTalentLevelUpPurchased", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            (f?.GetValue(null) as Action)?.Invoke();
        }

        public static void FromWidget(TalentLevelUpWidget w)
        {
            TalentLevelUpWidgetData d = w != null ? w.Data : null;
            if (d?.Def == null || d.ZombieWgoData == null) return;
            if (Do(d.ZombieWgoData, d.Def))
            {
                UITooltip.Hide();
                w.Redraw();
            }
        }
    }

    /// <summary>Clique direito no nó comprado.</summary>
    internal sealed class RightClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            try { Refund.FromWidget(GetComponent<TalentLevelUpWidget>()); }
            catch (Exception ex) { Plugin.Log.LogError(ex); }
        }
    }

    /// <summary>Clique (esquerdo) no X.</summary>
    internal sealed class CrossClick : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            try { Refund.FromWidget(GetComponentInParent<TalentLevelUpWidget>()); }
            catch (Exception ex) { Plugin.Log.LogError(ex); }
        }
    }
}
