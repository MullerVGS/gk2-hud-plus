using System;
using System.Collections.Generic;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Quests
{
    /// <summary>
    /// Árvore de quests (aba Quests do TAB) com mais contexto, sem mexer no desenho nativo: o nome do NPC sob cada nó;
    /// mouse sobre o nó abre o tooltip nativo com o retrato do NPC, onde ele está agora, o estado com a data do diário,
    /// o texto da quest e os passos anteriores daquele nó; o botão de fixar (meio) fixa o tooltip e abre ao lado a ficha
    /// do NPC. Só leitura.
    /// </summary>
    internal sealed class QuestTreeFeature : IDisposable
    {
        internal static QuestTreeFeature Current;

        private const string LabelName = PinStack.Prefix + "QuestNpcLabel";
        private const string PortraitName = PinStack.Prefix + "QuestPortrait";
        private const string LabelStyle = "tiny_font-item_quantity";
        private const string PlateSprite = "craft_window-header_1-value_cell";

        private QuestTreeElementWidget hovered;
        private RectTransform target;
        private GameObject portraitHolder;
        private Image portraitImage;
        private TextStyle labelStyle;
        private bool styleSearched;
        private Sprite plateSprite;

        public QuestTreeFeature(Harmony harmony)
        {
            Current = this;
            Type w = typeof(QuestTreeElementWidget), me = typeof(QuestTreeFeature);
            harmony.Patch(AccessTools.Method(w, "OnSelect"), postfix: new HarmonyMethod(me, nameof(PostSelect)));
            harmony.Patch(AccessTools.Method(w, "OnDeselect"), postfix: new HarmonyMethod(me, nameof(PostDeselect)));
            harmony.Patch(AccessTools.Method(w, nameof(QuestTreeElementWidget.Redraw)), postfix: new HarmonyMethod(me, nameof(PostRedraw)));
            DestroyLeftovers();
        }

        public void Dispose()
        {
            Hide();
            DestroyLeftovers();
            QuestModel.Reset();
            if (Current == this) Current = null;
        }

        /// <summary>Rótulos nos nós (os widgets são do pool do jogo e sobrevivem ao mod) e o retrato no tooltip nativo.</summary>
        private void DestroyLeftovers()
        {
            foreach (QuestTreeElementWidget w in UnityEngine.Object.FindObjectsByType<QuestTreeElementWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform l = w.transform.Find(LabelName);
                if (l != null) UnityEngine.Object.Destroy(l.gameObject);
            }
            UITooltip native = Native.Instance;
            Transform p = native != null ? native.transform.Find(PortraitName) : null;
            if (p != null) UnityEngine.Object.Destroy(p.gameObject);
            portraitHolder = null;
            portraitImage = null;
        }

        // ---------------- Harmony ----------------

        private static void PostSelect(QuestTreeElementWidget __instance)
        {
            try { Current?.Hover(__instance); }
            catch (Exception ex) { Plugin.Log.LogError("quests (hover): " + ex); }
        }

        private static void PostDeselect(QuestTreeElementWidget __instance)
        {
            QuestTreeFeature f = Current;
            if (f != null && f.hovered == __instance) f.Hide();
        }

        private static void PostRedraw(QuestTreeElementWidget __instance)
        {
            try { Current?.DrawLabel(__instance); }
            catch (Exception ex) { Plugin.Log.LogError("quests (rótulo): " + ex); }
        }

        // ---------------- tooltip ----------------

        private static bool InTree(QuestTreeElementWidget w) => w != null && w.GetComponentInParent<QuestTreePageWidget>() != null;

        private static QuestData QuestOf(QuestTreeElementWidget w)
        {
            QuestTreeElementWidgetData d = w != null ? w.Data : null;
            if (d?.questData?.Definition == null) return null;
            return d.displayViewStatus == QuestViewStatus.Unknown || d.displayViewStatus == QuestViewStatus.Hidden ? null : d.questData;
        }

        private void Hover(QuestTreeElementWidget w)
        {
            if (LazyInput.IsGamepadActive || !InTree(w)) return;
            QuestData q = QuestOf(w);
            UITooltip native = Native.Instance;
            if (q == null || native == null) return;
            List<LazyWidgetDataBase> list = QuestContent.Node(q, hint: true);
            if (list.Count == 0) return;
            hovered = w;
            target = (RectTransform)w.transform;
            SetPortrait(native, QuestModel.Portrait(q.Definition.wgoNpcId));
            Native.ShowAt.Invoke(native, new object[] { list, target, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.TopRight });
            // O Show nativo põe os widgets dele na frente: o retrato volta ao topo (a altura já contou com ele).
            if (portraitHolder != null && portraitHolder.activeSelf)
            {
                portraitHolder.transform.SetSiblingIndex(0);
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)native.transform);
            }
        }

        private void Hide()
        {
            if (target != null && UITooltip.IsTooltipShowingAtTarget(target)) UITooltip.Hide();
            hovered = null;
            target = null;
        }

        /// <summary>
        /// O tooltip nativo não tem widget de imagem: o retrato é um filho próprio no topo da bolha (primeiro no layout),
        /// ligado só enquanto o tooltip mostra um nó da árvore (<see cref="OnNativeShow"/> roda antes de cada Show nativo).
        /// </summary>
        private void SetPortrait(UITooltip native, Sprite sprite)
        {
            if (portraitHolder == null)
            {
                portraitHolder = new GameObject(PortraitName, typeof(RectTransform));
                portraitHolder.transform.SetParent(native.transform, false);
                portraitHolder.AddComponent<LayoutElement>();
                var go = new GameObject("Icon", typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(portraitHolder.transform, false);
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                portraitImage = go.AddComponent<Image>();
            }
            portraitImage.sprite = sprite;
            QuestModel.FitPortrait(portraitHolder.GetComponent<LayoutElement>(), portraitImage);
        }

        /// <summary>Chamado no funil de todo Show do tooltip nativo: o retrato só fica ligado no tooltip do nó.</summary>
        internal void OnNativeShow(RectTransform showTarget)
        {
            if (portraitHolder == null) return;
            bool on = target != null && showTarget == target && portraitImage.sprite != null;
            if (portraitHolder.activeSelf != on) portraitHolder.SetActive(on);
        }

        // ---------------- fixar ----------------

        /// <summary>O tooltip fixado é o do nó sob o mouse? Então a bolha leva o retrato e o conteúdo sem a dica.</summary>
        internal string OnPinning(GK2Hud.Tooltips.Bubble b, RectTransform pinTarget)
        {
            if (target == null || pinTarget != target || hovered == null) return null;
            QuestData q = QuestOf(hovered);
            if (q == null) return null;
            b.Data = QuestContent.Node(q, hint: false);
            b.Image = QuestModel.Portrait(q.Definition.wgoNpcId);
            b.Portrait = true;
            string npcId = q.Definition.wgoNpcId;
            return QuestModel.NpcName(npcId) != null ? Links.Npc + ":" + npcId : null;
        }

        // ---------------- rótulo do nó ----------------

        private void DrawLabel(QuestTreeElementWidget w)
        {
            Transform old = w.transform.Find(LabelName);
            QuestData q = InTree(w) ? QuestOf(w) : null;
            string name = q != null ? QuestModel.NpcName(q.Definition.wgoNpcId) : null;
            if (name == null)
            {
                if (old != null) old.gameObject.SetActive(false);
                return;
            }
            if (!styleSearched)
            {
                styleSearched = true;
                foreach (TextStyle ts in Resources.FindObjectsOfTypeAll<TextStyle>())
                    if (ts.name == LabelStyle) { labelStyle = ts; break; }
                if (labelStyle == null) Plugin.Log.LogWarning("quests: estilo " + LabelStyle + " não encontrado; nós sem nome");
                foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>())
                    if (sp.name.Replace("(Clone)", "").Trim() == PlateSprite) { plateSprite = sp; break; }
            }
            if (labelStyle == null) return;
            TextMeshProUGUI tmp;
            RectTransform plate;
            if (old == null)
            {
                // Plaquinha escura atrás do nome: o rótulo fica em cima da linha que desce do nó.
                var go = new GameObject(LabelName, typeof(RectTransform));
                plate = (RectTransform)go.transform;
                plate.SetParent(w.transform, false);
                plate.anchorMin = plate.anchorMax = new Vector2(0.5f, 0f);
                plate.pivot = new Vector2(0.5f, 1f);
                plate.anchoredPosition = new Vector2(0f, -8f);
                if (plateSprite != null)
                {
                    Image bg = go.AddComponent<Image>();
                    bg.sprite = plateSprite;
                    bg.type = Image.Type.Sliced;
                    bg.raycastTarget = false;
                }
                var text = new GameObject("Text", typeof(RectTransform));
                var trt = (RectTransform)text.transform;
                trt.SetParent(plate, false);
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = trt.offsetMax = Vector2.zero;
                tmp = text.AddComponent<TextMeshProUGUI>();
                tmp.fontSize = 16;
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                text.AddComponent<TextStyleComponent>().SetTextStyle(labelStyle);
            }
            else
            {
                plate = (RectTransform)old;
                tmp = old.GetComponentInChildren<TextMeshProUGUI>(true);
                old.gameObject.SetActive(true);
            }
            if (tmp.text != name)
            {
                tmp.text = name;
                float width = Mathf.Ceil(tmp.GetPreferredValues(name).x * 0.5f) * 2f;
                plate.sizeDelta = new Vector2(width + 10f, 12f);
            }
        }

        /// <summary>Resumo para os cenários.</summary>
        internal string Describe() =>
            "hovered=" + (hovered != null ? hovered.name : "-") + " portrait=" + (portraitHolder != null && portraitHolder.activeSelf ? portraitImage.sprite?.name : "-")
            + " nativeAtNode=" + (target != null && UITooltip.IsTooltipShowingAtTarget(target));
    }
}
