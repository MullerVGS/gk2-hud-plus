using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Quests
{
    /// <summary>
    /// Árvore de quests (aba Quests do TAB) como mapa da história, sem mexer no desenho nativo. Mouse parado num nó:
    /// tooltip nativo com o básico. Clique: a janela cresce para a direita e abre a página do diário daquele nó (bolha
    /// acoplada, mesma moldura dos tooltips) com as poses do caminho, como o jogador chegou ali e o que vale agora; o
    /// fio do nó (o que veio antes e o que sai dele) fica aceso e o resto da árvore apaga. Plaquinha com o nome do NPC
    /// só no fio aceso (sem nó escolhido: só nas quests em andamento). Só leitura.
    /// </summary>
    internal sealed class QuestTreeFeature : IDisposable
    {
        internal static QuestTreeFeature Current;

        private const string LabelName = PinStack.Prefix + "QuestNpcLabel";
        private const string LabelStyle = "tiny_font-item_quantity";
        private const string PlateSprite = "craft_window-header_1-value_cell";
        private const float PageSpace = 190f;      // quanto a janela cresce (px de arte): a página tem até 200 com a margem transparente
        private const float PageContent = 172f;    // largura do conteúdo da página (200 da bolha menos o padding nativo)
        private const float GrowTime = 0.25f;      // o ritmo do HUD do jogo (UISaveOverlay), OutQuad
        private const float DimAlpha = 0.25f;
        private const string BackExtraName = PinStack.Prefix + "QuestBackExtra";

        private static readonly AccessTools.FieldRef<QuestTreePageWidget, LazyScrollRect> ScrollRef =
            AccessTools.FieldRefAccess<QuestTreePageWidget, LazyScrollRect>("scrollRect");
        private static readonly AccessTools.FieldRef<QuestTreePageWidget, List<QuestTreeConnector>> ConnectorsRef =
            AccessTools.FieldRefAccess<QuestTreePageWidget, List<QuestTreeConnector>>("connectors");
        private static readonly AccessTools.FieldRef<QuestTreeConnector, LazyScrollableElement> ConnParentRef =
            AccessTools.FieldRefAccess<QuestTreeConnector, LazyScrollableElement>("parent");
        private static readonly AccessTools.FieldRef<QuestTreeConnector, LazyScrollableElement> ConnChildRef =
            AccessTools.FieldRefAccess<QuestTreeConnector, LazyScrollableElement>("child");

        private readonly ConfigEntry<float> hoverDelay;

        // hover
        private QuestTreeElementWidget hovered;
        private RectTransform target;
        private float hoverSince;
        private bool tooltipShown;

        // nó escolhido
        private QuestData selected;
        private HashSet<Vector2Int> thread;
        private GK2Hud.Tooltips.Bubble pageBubble;
        private readonly HashSet<CanvasGroup> dimGroups = new HashSet<CanvasGroup>();

        // janela
        private QuestTreePageWidget page;
        private RectTransform content, frame, scrollView, back, backExtra;
        private Vector2 baseContent, baseFrame, baseScrollMax, baseBack;
        private bool sized;
        private float extra, extraFrom, extraTarget, growStart = -1f;

        // clique no fundo
        private Vector2 downAt;
        private bool downOnBackground;

        private TextStyle labelStyle;
        private Sprite plateSprite;
        private bool styleSearched;

        public QuestTreeFeature(Harmony harmony, ConfigEntry<float> hoverDelay)
        {
            this.hoverDelay = hoverDelay;
            Current = this;
            Type w = typeof(QuestTreeElementWidget), p = typeof(QuestTreePageWidget), me = typeof(QuestTreeFeature);
            harmony.Patch(AccessTools.Method(w, "OnSelect"), postfix: new HarmonyMethod(me, nameof(PostSelect)));
            harmony.Patch(AccessTools.Method(w, "OnDeselect"), postfix: new HarmonyMethod(me, nameof(PostDeselect)));
            harmony.Patch(AccessTools.Method(w, nameof(QuestTreeElementWidget.Redraw)), postfix: new HarmonyMethod(me, nameof(PostRedraw)));
            harmony.Patch(AccessTools.Method(p, "OnQuestClicked"), prefix: new HarmonyMethod(me, nameof(PreClicked)));
            harmony.Patch(AccessTools.Method(p, nameof(QuestTreePageWidget.Display)), postfix: new HarmonyMethod(me, nameof(PostDisplay)));
            DestroyLabels();
        }

        public void Dispose()
        {
            HideTooltip();
            Close(animate: false);
            foreach (CanvasGroup g in dimGroups) if (g != null) UnityEngine.Object.Destroy(g);
            dimGroups.Clear();
            DestroyLabels();
            QuestModel.Reset();
            if (Current == this) Current = null;
        }

        /// <summary>Os widgets dos nós são do pool do jogo e sobrevivem ao mod: os rótulos saem pelo nome.</summary>
        private static void DestroyLabels()
        {
            foreach (QuestTreeElementWidget w in UnityEngine.Object.FindObjectsByType<QuestTreeElementWidget>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Transform l = w.transform.Find(LabelName);
                if (l != null) UnityEngine.Object.Destroy(l.gameObject);
            }
        }

        // ---------------- Harmony ----------------

        private static void PostSelect(QuestTreeElementWidget __instance)
        {
            try { Current?.Hover(__instance); }
            catch (Exception ex) { HudModule.Log.LogError("quests (hover): " + ex); }
        }

        private static void PostDeselect(QuestTreeElementWidget __instance)
        {
            QuestTreeFeature f = Current;
            if (f != null && f.hovered == __instance) f.HideTooltip();
        }

        private static void PostRedraw(QuestTreeElementWidget __instance)
        {
            try { Current?.Decorate(__instance); }
            catch (Exception ex) { HudModule.Log.LogError("quests (nó): " + ex); }
        }

        /// <summary>O clique abre a página do diário no lugar da janela nativa da quest (com gamepad segue o nativo).</summary>
        private static bool PreClicked(QuestTreePageWidget __instance, QuestTreeElementWidgetData widgetData)
        {
            QuestTreeFeature f = Current;
            if (f == null || LazyInput.IsGamepadActive || widgetData?.questData?.Definition == null || TooltipFeature.Current == null) return true;
            try
            {
                f.page = __instance;
                f.Select(widgetData.questData);
                return false;
            }
            catch (Exception ex)
            {
                HudModule.Log.LogError("quests (clique): " + ex);
                f.Close(animate: false);
                return true;
            }
        }

        /// <summary>A árvore foi remontada (linhas novas do pool): o fio aceso é reaplicado.</summary>
        private static void PostDisplay(QuestTreePageWidget __instance)
        {
            QuestTreeFeature f = Current;
            if (f == null) return;
            try
            {
                f.page = __instance;
                f.ApplyFocus();
            }
            catch (Exception ex) { HudModule.Log.LogError("quests (árvore): " + ex); }
        }

        // ---------------- por frame ----------------

        public void Update()
        {
            try { Tick(); }
            catch (Exception ex)
            {
                HudModule.Log.LogError("quests: " + ex);
                Close(animate: false);
            }
        }

        private void Tick()
        {
            if (page == null || !page.gameObject.activeInHierarchy)
            {
                // Aba trocada ou janela fechada: tudo volta ao nativo na hora.
                if (selected != null || sized) Close(animate: false);
                if (hovered != null) HideTooltip();
                return;
            }
            if (hovered != null && !tooltipShown && Time.unscaledTime - hoverSince >= hoverDelay.Value) ShowTooltip();
            if (selected != null && (pageBubble == null || pageBubble.Rt == null))
            {
                // A pilha de bolhas foi derrubada por fora (outra janela abriu, volta ao menu): a página foi junto.
                pageBubble = null;
                Close(animate: true);
            }
            BackgroundClick();
            if (growStart >= 0f)
            {
                float t = Mathf.Clamp01((Time.unscaledTime - growStart) / GrowTime);
                float eased = 1f - (1f - t) * (1f - t);
                SetExtra(Mathf.Lerp(extraFrom, extraTarget, eased));
                if (t >= 1f)
                {
                    growStart = -1f;
                    if (extraTarget <= 0f) Unsize();
                }
            }
            if (pageBubble != null && pageBubble.Rt != null) PlacePage();
        }

        /// <summary>Clique solto no fundo da árvore (sem arrastar, fora de nó e da página) fecha a página.</summary>
        private void BackgroundClick()
        {
            if (selected == null || scrollView == null) return;
            Vector2 mouse = TooltipFeature.Current?.VirtualMouse ?? (Vector2)Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                downAt = mouse;
                downOnBackground = hovered == null && RectTransformUtility.RectangleContainsScreenPoint(scrollView, mouse, null)
                    && !(pageBubble?.Contains(mouse) ?? false);
            }
            if (Input.GetMouseButtonUp(0) && downOnBackground)
            {
                downOnBackground = false;
                if ((mouse - downAt).sqrMagnitude < 36f && hovered == null) Close(animate: true);
            }
        }

        // ---------------- tooltip básico ----------------

        private static bool InTree(QuestTreeElementWidget w) => w != null && w.GetComponentInParent<QuestTreePageWidget>() != null;

        private static QuestData QuestOf(QuestTreeElementWidget w)
        {
            QuestTreeElementWidgetData d = w != null ? w.Data : null;
            if (d?.questData?.Definition == null) return null;
            return d.displayViewStatus == QuestViewStatus.Unknown || d.displayViewStatus == QuestViewStatus.Hidden ? null : d.questData;
        }

        private void Hover(QuestTreeElementWidget w)
        {
            if (LazyInput.IsGamepadActive || !InTree(w) || QuestOf(w) == null) return;
            if (page == null) page = w.GetComponentInParent<QuestTreePageWidget>();
            hovered = w;
            target = (RectTransform)w.transform;
            hoverSince = Time.unscaledTime;
            tooltipShown = false;
        }

        private void ShowTooltip()
        {
            tooltipShown = true;
            QuestData q = QuestOf(hovered);
            UITooltip native = Native.Instance;
            if (q == null || native == null) return;
            List<LazyWidgetDataBase> list = QuestContent.Basic(q);
            if (list.Count == 0) return;
            Native.ShowAt.Invoke(native, new object[] { list, target, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.TopRight });
        }

        private void HideTooltip()
        {
            if (target != null && UITooltip.IsTooltipShowingAtTarget(target)) UITooltip.Hide();
            hovered = null;
            target = null;
            tooltipShown = false;
        }

        /// <summary>O tooltip fixado (botão do meio) é o do nó sob o mouse? Então abre fixada ao lado a ficha do NPC.</summary>
        internal string OnPinning(GK2Hud.Tooltips.Bubble b, RectTransform pinTarget)
        {
            if (target == null || pinTarget != target || hovered == null) return null;
            QuestData q = QuestOf(hovered);
            if (q == null) return null;
            string npcId = q.Definition.wgoNpcId;
            return QuestModel.NpcName(npcId) != null ? Links.Npc + ":" + npcId : null;
        }

        // ---------------- nó escolhido ----------------

        private void Select(QuestData q)
        {
            if (selected == q) { Close(animate: true); return; }
            if (!EnsureWindow()) throw new InvalidOperationException("janela do TAB sem a estrutura esperada");
            TooltipFeature tips = TooltipFeature.Current;
            if (target != null && UITooltip.IsTooltipShowingAtTarget(target)) UITooltip.HideImmediately();
            tooltipShown = true;   // o tooltip do nó recém-clicado não volta até o mouse sair dele
            selected = q;
            thread = QuestModel.Thread(q);
            ApplyFocus();

            tips.Stack.CloseFloating();
            if (pageBubble == null || pageBubble.Rt == null)
            {
                pageBubble = tips.Stack.Create(0);
                if (pageBubble == null) throw new InvalidOperationException("sem o tooltip nativo para clonar");
                pageBubble.Rt.name = PinStack.Prefix + "QuestPage";
                pageBubble.Pinned = true;
                pageBubble.Docked = true;
                pageBubble.Portrait = true;
                pageBubble.MinWidth = PageContent;
                tips.Stack.Pinned.Insert(0, pageBubble);
            }
            pageBubble.Data = QuestContent.Page(q);
            pageBubble.Image = QuestModel.Pose(q) ?? QuestModel.Portrait(q.Definition.wgoNpcId);
            pageBubble.Strip = new List<Sprite>();
            foreach (QuestData step in QuestModel.Path(q, 3))
            {
                Sprite pose = QuestModel.Pose(step);
                if (pose != null) pageBubble.Strip.Add(pose);
            }
            pageBubble.RootPos = pageBubble.Rt.position;
            pageBubble.RootCorner = -1;
            tips.Stack.Render(pageBubble, Styles.Read());
            Grow(Mathf.Min(PageSpace, Screen.width / Mathf.Max(1f, LazyUI.ScaleFactor) - baseContent.x - 8f));
            PlacePage();
        }

        /// <summary>Fecha a página, reacende a árvore e devolve a janela ao tamanho nativo.</summary>
        private void Close(bool animate)
        {
            selected = null;
            thread = null;
            TooltipFeature tips = TooltipFeature.Current;
            if (pageBubble != null)
            {
                if (tips != null)
                {
                    tips.Stack.CloseFloating();
                    tips.Stack.Pinned.Remove(pageBubble);
                }
                if (pageBubble.Rt != null) PinStack.DestroyClone(pageBubble.Rt.gameObject);
                pageBubble = null;
            }
            ApplyFocus();
            if (!sized) return;
            if (animate) Grow(0f);
            else
            {
                growStart = -1f;
                SetExtra(0f);
                Unsize();
            }
        }

        /// <summary>A página fica no espaço que a janela abriu à direita da árvore, colada no topo; aparece conforme ele abre.</summary>
        private void PlacePage()
        {
            var c = new Vector3[4];
            ((RectTransform)page.transform).GetWorldCorners(c);
            Rect r = pageBubble.ScreenRect();
            float m = GK2Hud.Tooltips.Bubble.FrameMargin;
            // Canto superior direito da moldura visível na grade de 2 px de tela (1 px de arte), em posição absoluta.
            float right = Mathf.Round((c[2].x - 4f) / 2f) * 2f, top = Mathf.Round((c[2].y - 4f) / 2f) * 2f;
            float x = right - (r.xMax - m), y = top - (r.yMax - m);
            if (Mathf.Abs(x) > 0.01f || Mathf.Abs(y) > 0.01f) pageBubble.Rt.position += new Vector3(x, y, 0f);
            CanvasGroup g = Native.GroupOf(pageBubble.Tip);
            if (g != null) g.alpha = extraTarget > 0f ? Mathf.Clamp01((extra / extraTarget - 0.5f) * 2f) : 0f;
        }

        // ---------------- janela ----------------

        private bool EnsureWindow()
        {
            if (sized) return true;
            if (page == null) return false;
            CharacterWindow window = page.GetComponentInParent<CharacterWindow>();
            LazyScrollRect scroll = ScrollRef(page);
            content = window != null ? window.transform.Find("Content") as RectTransform : null;
            frame = content != null ? content.Find("Frame") as RectTransform : null;
            scrollView = scroll != null ? scroll.transform as RectTransform : null;
            if (content == null || frame == null || scrollView == null) return false;
            baseContent = content.sizeDelta;
            baseFrame = frame.sizeDelta;
            baseScrollMax = scrollView.offsetMax;
            back = frame.Find("Back") as RectTransform;
            if (back != null)
            {
                // O fundo da janela é uma textura de tamanho fixo: ele vai para a esquerda com a árvore e uma cópia
                // espelhada, recortada, cobre a faixa nova à direita (espelhada, a emenda continua o desenho).
                baseBack = back.anchoredPosition;
                Transform old = frame.Find(BackExtraName);
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var holder = new GameObject(BackExtraName, typeof(RectTransform), typeof(RectMask2D));
                backExtra = (RectTransform)holder.transform;
                backExtra.SetParent(frame, false);
                backExtra.SetSiblingIndex(back.GetSiblingIndex());
                backExtra.anchorMin = backExtra.anchorMax = back.anchorMin;
                backExtra.pivot = new Vector2(0.5f, 0.5f);
                GameObject copy = UnityEngine.Object.Instantiate(back.gameObject, backExtra);
                copy.name = "Mirror";
                var crt = (RectTransform)copy.transform;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0.5f, 0.5f);
                crt.sizeDelta = back.rect.size;
                crt.localScale = new Vector3(-1f, 1f, 1f);
            }
            sized = true;
            extra = 0f;
            return true;
        }

        private void Grow(float to)
        {
            extraFrom = extra;
            extraTarget = Mathf.Max(0f, to);
            growStart = Time.unscaledTime;
        }

        /// <summary>A janela é centrada: cresce para os dois lados; a árvore mantém a largura e o espaço novo fica à direita dela.</summary>
        private void SetExtra(float value)
        {
            extra = Mathf.Round(value * 0.5f) * 2f;
            if (!sized || content == null || frame == null || scrollView == null) return;
            if (back != null && backExtra != null)
            {
                float width = back.rect.width;
                back.anchoredPosition = new Vector2(baseBack.x - extra * 0.5f, baseBack.y);
                backExtra.sizeDelta = new Vector2(extra, back.rect.height);
                backExtra.anchoredPosition = new Vector2(baseBack.x + width * 0.5f, baseBack.y);
                ((RectTransform)backExtra.GetChild(0)).anchoredPosition = new Vector2(width * 0.5f - extra * 0.5f, 0f);
            }
            content.sizeDelta = new Vector2(baseContent.x + extra, baseContent.y);
            frame.sizeDelta = new Vector2(baseFrame.x + extra, baseFrame.y);
            scrollView.offsetMax = new Vector2(baseScrollMax.x - extra, baseScrollMax.y);
        }

        private void Unsize()
        {
            if (sized) SetExtra(0f);
            sized = false;
            if (backExtra != null) UnityEngine.Object.Destroy(backExtra.gameObject);
            content = frame = scrollView = back = backExtra = null;
        }

        // ---------------- fio aceso ----------------

        private bool InFocus(QuestData q) => thread == null || (q?.Definition != null && thread.Contains(q.Definition.TreePos));

        private void ApplyFocus()
        {
            if (page == null) return;
            LazyScrollRect scroll = ScrollRef(page);
            if (scroll != null)
                foreach (LazyScrollableElement el in scroll.DisplayingElements)
                    if (el != null && el.Widget is QuestTreeElementWidget w) Decorate(w);
            List<QuestTreeConnector> connectors = ConnectorsRef(page);
            if (connectors == null) return;
            foreach (QuestTreeConnector c in connectors)
            {
                if (c == null) continue;
                QuestData a = (ConnParentRef(c)?.Data as QuestTreeElementWidgetData)?.questData, b = (ConnChildRef(c)?.Data as QuestTreeElementWidgetData)?.questData;
                SetAlpha(c.gameObject, InFocus(a) && InFocus(b) ? 1f : DimAlpha);
            }
        }

        /// <summary>O CanvasGroup só é criado quando algo precisa apagar; objetos do pool voltam a 1 ao fechar.</summary>
        private void SetAlpha(GameObject go, float alpha)
        {
            CanvasGroup g = go.GetComponent<CanvasGroup>();
            if (g == null)
            {
                if (alpha >= 1f) return;
                g = go.AddComponent<CanvasGroup>();
                g.interactable = true;
                g.blocksRaycasts = true;
                dimGroups.Add(g);
            }
            if (g.alpha != alpha) g.alpha = alpha;
        }

        /// <summary>Um nó desenhado: apagado se está fora do fio aceso; plaquinha com o nome do NPC se está nele.</summary>
        private void Decorate(QuestTreeElementWidget w)
        {
            bool inTree = InTree(w);
            QuestData q = inTree ? QuestOf(w) : null;
            QuestData any = inTree ? w.Data?.questData : null;
            SetAlpha(w.gameObject, !inTree || InFocus(any) ? 1f : DimAlpha);
            bool labeled = q != null && (thread != null ? InFocus(q) : q.status == QuestStatus.InProgress);
            DrawLabel(w, labeled ? QuestModel.NpcName(q.Definition.wgoNpcId) : null);
        }

        private void DrawLabel(QuestTreeElementWidget w, string name)
        {
            Transform old = w.transform.Find(LabelName);
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
                if (labelStyle == null) HudModule.Log.LogWarning("quests: estilo " + LabelStyle + " não encontrado; nós sem nome");
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
            "hovered=" + (hovered != null ? hovered.name : "-") + " tooltip=" + (target != null && UITooltip.IsTooltipShowingAtTarget(target))
            + " selected=" + (selected != null ? selected.id : "-") + " thread=" + (thread != null ? thread.Count.ToString() : "-")
            + " page=" + (pageBubble?.Rt != null ? pageBubble.ScreenRect().ToString() : "-") + " extra=" + extra + "/" + extraTarget;
    }
}
