using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Tooltips enriquecidos + fixar/recursão estilo BG3.
    /// Captura: prefixes nos UITooltip.Show* gravam a entidade; o prefix do funil (ShowAtTargetAndCalculateOffsets,
    /// só no singleton nativo) acrescenta as seções e guarda o conteúdo para fixar. Bolhas: <see cref="PinStack"/>.
    /// </summary>
    internal sealed class TooltipFeature : IDisposable
    {
        /// <summary>Instância viva (os patches Harmony são estáticos). Zerada no Dispose.</summary>
        internal static TooltipFeature Current;

        private readonly Harmony harmony;
        private readonly ConfigEntry<KeyboardShortcut> pinKey;
        private readonly ConfigEntry<float> hoverDelay;
        private readonly ConfigEntry<int> maxDepth;

        internal readonly TooltipContent Content = new TooltipContent();
        internal readonly PinStack Stack = new PinStack();

        private Entity pending;
        private List<LazyWidgetDataBase> lastData;
        private List<LazyWidgetDataBase> lastPlain;   // o mesmo tooltip como o jogo o montou, antes do nosso básico
        private Entity lastEntity;
        private RectTransform lastTarget;
        private RectTransform suppressTarget;   // alvo recém-fixado: o nativo não volta por cima até o mouse sair dele
        private bool indexReady;

        private string hoverLink;
        private Bubble hoverOwner;
        private float hoverTime;
        private Rect hoverRect;
        private float transientAway;
        private int escFrame = -1;
        private HashSet<int> escKeys;

        // Modo de teste (cenários gk2hud-*.cs): no green o mouse não mexe.
        internal Vector2? VirtualMouse { get; set; }
        internal bool TestPin, TestRightClick, TestLeftClick, TestEsc;

        public TooltipFeature(Harmony harmony, ConfigEntry<KeyboardShortcut> pinKey, ConfigEntry<float> hoverDelay, ConfigEntry<int> maxDepth)
        {
            this.harmony = harmony;
            this.pinKey = pinKey;
            this.hoverDelay = hoverDelay;
            this.maxDepth = maxDepth;
            Current = this;
            int orphans = PinStack.DestroyOrphans();
            if (orphans > 0) HudModule.Log.LogInfo($"bolhas órfãs destruídas: {orphans}");
            Patch();
            LazyWindowsStackController.OnWindowOpened += OnWindowChanged;
            LazyWindowsStackController.OnWindowClosed += OnWindowChanged;
            MainGame.OnGoToMainMenu += OnGoToMainMenu;
        }

        public void Dispose()
        {
            LazyWindowsStackController.OnWindowOpened -= OnWindowChanged;
            LazyWindowsStackController.OnWindowClosed -= OnWindowChanged;
            MainGame.OnGoToMainMenu -= OnGoToMainMenu;
            Stack.Dispose();
            Content.Dispose();
            TipWidgets.Release();
            PinKeyControl.Dispose();
            if (Current == this) Current = null;
        }

        private void OnWindowChanged(LazyWidgetBase _) => Stack.CloseAll();

        private void OnGoToMainMenu()
        {
            Stack.CloseAll();
            lastData = null;
            lastTarget = suppressTarget = null;
        }

        // ---------------- Harmony ----------------

        /// <summary>Dica do canto do hover ("[MMB] more"); o mouse do meio é o padrão.</summary>
        internal string PinHint => TipWidgets.KeyText(pinKey.Value) + " " + L10n.T("pin_more");

        private void Patch()
        {
            TipWidgets.Acquire(harmony, false);
            PinKeyControl.Patch(harmony, pinKey);
            Type t = typeof(UITooltip);
            Type p = typeof(TooltipFeature);
            // Finalizer (não postfix): se o Show* nativo lançar, a entidade não fica presa para o próximo tooltip.
            HarmonyMethod clear = new HarmonyMethod(p, nameof(ClearPending));
            harmony.Patch(Native.ShowAt, prefix: new HarmonyMethod(p, nameof(FunnelPrefix)));
            void Ctx(string method, string prefix) => harmony.Patch(AccessTools.Method(t, method), prefix: new HarmonyMethod(p, prefix), finalizer: clear);
            Ctx(nameof(UITooltip.ShowItemCell), nameof(PreItemCell));
            Ctx(nameof(UITooltip.ShowCraftOutputCell), nameof(PreCraftOutputCell));
            Ctx(nameof(UITooltip.ShowCraftInfo), nameof(PreCraftInfo));
            Ctx(nameof(UITooltip.ShowPerk), nameof(PrePerk));
            Ctx(nameof(UITooltip.ShowPerkWidget), nameof(PrePerkWidget));
            Ctx(nameof(UITooltip.ShowGardenFertilizerSlot), nameof(PreFertilizer));
            Ctx(nameof(UITooltip.ShowLinkedEntity), nameof(PreLinkedEntity));
            Ctx(nameof(UITooltip.ShowOrderWidget), nameof(PreOrderWidget));
            harmony.Patch(AccessTools.Method(typeof(LazyInput), nameof(LazyInput.GetKeyDown), new[] { typeof(GameKey) }), prefix: new HarmonyMethod(p, nameof(PreGetKeyDown)));
        }

        private static void SetPending(Entity e, Component target)
        {
            if (Current == null || e == null) return;
            e.Target = target != null ? target.transform as RectTransform : null;
            e.Frame = Time.frameCount;
            Current.pending = e;
        }

        private static void ClearPending()
        {
            if (Current != null) Current.pending = null;
        }

        private static void PreItemCell(UIItemCell uiItemCell)
        {
            if (uiItemCell == null || uiItemCell.CustomTooltipShowAction != null || uiItemCell.DisplayingOutputPreview != null) return;
            Item it = uiItemCell.DisplayingItem;
            if (it == null || string.IsNullOrEmpty(it.id) || it.id == "empty") return;
            SetPending(Entity.ForItem(it.Definition, it), uiItemCell);
        }

        private static void PreCraftOutputCell(UIItemCell uiItemCell)
        {
            string craftId = uiItemCell?.DisplayingOutputPreview?.craftId;
            // Mistura de alquimia (conhecida ou não) fica de fora: GetCraftDef("mix...") escreve nos caches do balanço.
            if (string.IsNullOrEmpty(craftId) || craftId.StartsWith("mix")) return;
            SetPending(Entity.ForCraft(GameBalance.Me.GetDataOrNull<CraftDef>(craftId)), uiItemCell);
        }

        private static void PreCraftInfo(UIItemCell uiItemCell, WgoData wgoData, CraftDef craftDef, RectTransform customTarget) =>
            SetPending(Entity.ForCraft(craftDef, wgoData), customTarget != null ? (Component)customTarget : uiItemCell);

        private static void PrePerk(PerkDef perkDef, RectTransform target) => SetPending(Entity.ForPerk(perkDef), target);

        private static void PrePerkWidget(PerkWidget perkWidget) => SetPending(Entity.ForPerk(perkWidget?.PerkData?.Definition), perkWidget);

        private static void PreFertilizer(UIGardenBedSlot fertilizerSlot) => SetPending(Entity.ForPerk(fertilizerSlot?.PerkData?.Definition), fertilizerSlot);

        private static void PreOrderWidget(UIVendorOrderWidget orderWidget)
        {
            string id = orderWidget?.Data?.VendorOrderData?.Definition?.itemId;
            if (id != null) SetPending(Entity.ForItem(GameBalance.Me.GetDataOrNull<ItemDef>(id)), orderWidget);
        }

        private static void PreLinkedEntity(LinkedEntityWidget entityWidget)
        {
            LinkedEntityWidgetData d = entityWidget?.WidgetData;
            if (d == null) return;
            Entity e = null;
            switch (d.LinkedEntityType)
            {
                case LinkedEntityType.CraftDef: e = Entity.ForCraft(d.CraftDef); break;
                case LinkedEntityType.ItemDef:
                case LinkedEntityType.Order: e = Entity.ForItem(d.ItemDef); break;
                case LinkedEntityType.Item: e = Entity.ForItem(d.Item?.Definition); break;
                case LinkedEntityType.PerkDef: e = Entity.ForPerk(d.PerkDef); break;
                case LinkedEntityType.BuildingDef: e = Entity.ForStation(d.BuildingDef?.wgoId); break;
            }
            SetPending(e, entityWidget);
        }

        private static bool FunnelPrefix(UITooltip __instance, List<LazyWidgetDataBase> dataList, RectTransform target)
        {
            TooltipFeature f = Current;
            if (f == null || dataList == null || __instance != Native.Instance) return true;
            Entity e = f.pending;
            f.pending = null;
            // O Draw da célula ainda sob o mouse chama ShowItemCell de novo: não reabre o nativo por cima da bolha fixada.
            if (f.suppressTarget != null && target == f.suppressTarget) return false;
            // Só enriquece se a entidade é deste tooltip (mesmo alvo, mesmo frame).
            f.lastEntity = null;
            f.lastPlain = null;
            if (e != null && e.Target == target && e.Frame == Time.frameCount)
            {
                // Guarda o que o jogo montou: a bolha fixada parte daí e recebe o detalhe (o hover só o básico).
                f.lastPlain = new List<LazyWidgetDataBase>(dataList);
                f.lastEntity = e;
                try { f.Content.Enrich(dataList, e); }
                catch (Exception ex) { HudModule.Log.LogError($"enriquecer {e.Kind} {e.Id}: {ex}"); }
            }
            f.lastData = dataList;   // lista nova a cada Show* do jogo: guardar a referência basta
            f.lastTarget = target;
            return true;
        }

        private static bool PreGetKeyDown(GameKey key, ref bool __result)
        {
            TooltipFeature f = Current;
            if (f == null || key is null) return true;   // GameKey (Enumeration) tem == que sempre dá false com null
            int frame = Time.frameCount;
            if (f.escFrame != frame && !(f.Stack.AnyFloating && Input.GetKeyDown(KeyCode.Escape))) return true;
            if (!f.IsEscKey(key)) return true;
            __result = false;
            return false;
        }

        private bool IsEscKey(GameKey key)
        {
            if (escKeys == null)
            {
                escKeys = new HashSet<int>();
                foreach (KeyBinding b in LazyInput.GameBindings.keyBindings)
                    if (b.keyCode == KeyCode.Escape && !(b.gameKey is null)) escKeys.Add(b.gameKey.value);
            }
            return escKeys.Contains(key.value);
        }

        // ---------------- por frame ----------------

        public void Update()
        {
            try { Tick(); }
            catch (Exception ex) { HudModule.Log.LogError("tooltips: " + ex); Stack.CloseAll(); }
        }

        private void Tick()
        {
            bool pin = TestPin || pinKey.Value.IsDown();
            bool right = TestRightClick || Input.GetMouseButtonDown(1);
            bool left = TestLeftClick || Input.GetMouseButtonDown(0);
            bool esc = TestEsc || Input.GetKeyDown(KeyCode.Escape);
            TestPin = TestRightClick = TestLeftClick = TestEsc = false;

            Stack.Prune();
            if (!indexReady && MainGame.PlayerData != null) indexReady = Content.Index.EnsureBuilt();
            if (LazyInput.IsGamepadActive)
            {
                if (Stack.Any) Stack.CloseAll();
                return;
            }
            if (esc && Stack.AnyFloating)
            {
                escFrame = Time.frameCount;
                Stack.CloseFloating();
                return;
            }
            Vector2 mouse = VirtualMouse ?? (Vector2)Input.mousePosition;
            if (suppressTarget != null && (!suppressTarget.gameObject.activeInHierarchy || !RectTransformUtility.RectangleContainsScreenPoint(suppressTarget, mouse, null)))
                suppressTarget = null;
            if (pin) HandlePin(mouse);
            else if (right)
            {
                Bubble b = PinnedAt(mouse);
                if (b != null && !b.Docked) Stack.CloseFrom(b);
            }
            else if (left && Stack.AnyFloating && PinnedAt(mouse) == null && !(Stack.Transient?.Contains(mouse) ?? false))
            {
                Stack.CloseFloating();
            }
            UpdateHover(mouse);
            Stack.RefreshLive(Styles.Read(), World.WorldContent.Signature);
            Stack.Follow();
        }

        /// <summary>
        /// O tooltip de mundo usa um proxy único que muda de objeto: quando o alvo do mundo troca, a supressão do objeto
        /// recém-fixado não vale para o próximo (senão o proxy, já sob o mouse no vizinho, nunca sai da supressão).
        /// </summary>
        internal void ReleaseSuppress(RectTransform rt)
        {
            if (rt != null && suppressTarget == rt) suppressTarget = null;
        }

        private Bubble PinnedAt(Vector2 mouse)
        {
            for (int i = Stack.Pinned.Count - 1; i >= 0; i--)
                if (Stack.Pinned[i].Contains(mouse)) return Stack.Pinned[i];
            return null;
        }

        internal void HandlePin(Vector2 mouse)
        {
            Bubble tr = Stack.Transient;
            if (tr != null && (tr.Contains(mouse) || Grow(tr.SourceRect, 6f).Contains(mouse)))
            {
                PinTransient();
                return;
            }
            UITooltip native = Native.Instance;
            if (Native.IsVisible(native) && lastData != null && lastData.Count > 0)
            {
                PinNative(native);
                return;
            }
            Bubble b = PinnedAt(mouse);
            if (b != null && !b.Docked) Stack.CloseFrom(b);
        }

        private void PinTransient()
        {
            Bubble b = Stack.Transient;
            Stack.Transient = null;
            b.Pinned = true;
            if (b.PinData != null)
            {
                List<LazyWidgetDataBase> full = b.PinData();
                if (full != null && full.Count > 0) b.Data = full;
            }
            Stack.Pinned.Add(b);
            b.Rt.name = PinStack.Prefix + "Bubble" + b.Depth;
            Stack.Render(b, Styles.Read());
            ResetHover();
        }

        private void PinNative(UITooltip native)
        {
            Bubble parent = null;
            if (lastTarget != null)
                parent = Stack.Pinned.LastOrDefault(p => lastTarget.IsChildOf(p.Rt));
            if (parent != null)
            {
                int i = Stack.Pinned.IndexOf(parent);
                if (i + 1 < Stack.Pinned.Count) Stack.CloseFrom(Stack.Pinned[i + 1]);
                if (Stack.Pinned.Count >= maxDepth.Value) return;
                Stack.CloseTransient();
            }
            else
            {
                Stack.CloseFloating();
            }
            Bubble b = Stack.Create(parent == null ? 0 : parent.Depth + 1);
            if (b == null) return;
            b.Pinned = true;
            b.Parent = parent;
            b.Data = new List<LazyWidgetDataBase>(lastData);
            b.Live = World.WorldHoverFeature.Current?.LiveFor(lastTarget) ?? Calendar.WheelFeature.Current?.LiveFor(lastTarget);
            if (b.Live == null && lastEntity != null && lastPlain != null)
            {
                var full = new List<LazyWidgetDataBase>(lastPlain);
                try { Content.Enrich(full, lastEntity, true); b.Data = full; }
                catch (Exception ex) { HudModule.Log.LogError($"detalhe {lastEntity.Kind} {lastEntity.Id}: {ex}"); }
            }
            b.Data.RemoveAll(d => d is PinHintData);
            if (b.Live != null)
            {
                // A bolha fixada já nasce com o detalhe (o hover só tem o básico).
                List<LazyWidgetDataBase> full = b.Live();
                if (full != null && full.Count > 0) b.Data = full;
                b.LiveSig = World.WorldContent.Signature(b.Data);
            }
            Calendar.WheelFeature.Current?.OnPinning(b, lastTarget);
            // Nó da árvore de quests: a bolha leva o retrato e, depois de fixada, abre ao lado a ficha do NPC.
            string childLink = parent == null ? Quests.QuestTreeFeature.Current?.OnPinning(b, lastTarget) : null;
            if (parent != null)
            {
                b.SourceRect = ScreenRectOf(lastTarget);
                b.Side = parent.Side;
            }
            else
            {
                b.RootPos = native.transform.position;
                b.Anchor = World.WorldHoverFeature.Current?.AnchorFor(lastTarget);
                Vector2? at = b.Anchor?.Invoke();
                if (at.HasValue) { b.AnchorAtPin = at.Value; b.PosAtPin = native.transform.position; }
                else b.Anchor = null;
                int k = 0;
                foreach (UIBubbleCorner c in Native.Corners(native)) { if (c.gameObject.activeSelf) b.RootCorner = k; k++; }
            }
            Stack.Pinned.Add(b);
            Stack.Render(b, Styles.Read());
            UITooltip.HideImmediately();
            suppressTarget = lastTarget;
            ResetHover();
            if (childLink != null) PinChild(b, childLink);
        }

        /// <summary>Abre e já fixa a bolha de um termo da bolha mãe (o mesmo caminho do hover no termo + fixar).</summary>
        private void PinChild(Bubble parent, string link)
        {
            Rect rect = default;
            foreach (KeyValuePair<string, Rect> l in Links.AllLinks(parent.Rt))
                if (l.Key == link) { rect = l.Value; break; }
            OpenTransient(parent, link, rect);
            if (Stack.Transient != null && Stack.Transient.SourceLink == link) PinTransient();
        }

        private void UpdateHover(Vector2 mouse)
        {
            Bubble top = Stack.Top;
            Bubble tr = Stack.Transient;
            if (top == null) { Stack.CloseTransient(); ResetHover(); return; }

            if (tr != null && tr.Contains(mouse)) { transientAway = 0f; hoverLink = null; return; }

            string link = null;
            Rect rect = default;
            if (top.Contains(mouse)) link = Links.LinkAt(top.LinkTexts, mouse, out rect);
            if (link == null && top.Hot != null && top.Contains(mouse)) link = top.Hot(mouse, out rect);

            if (tr != null && link == tr.SourceLink && tr.Parent == top) { transientAway = 0f; hoverLink = null; return; }

            if (link != null)
            {
                if (link == hoverLink && hoverOwner == top) hoverTime += Time.unscaledDeltaTime;
                else { hoverLink = link; hoverOwner = top; hoverTime = 0f; hoverRect = rect; }
                if (hoverTime >= hoverDelay.Value) OpenTransient(top, link, hoverRect);
            }
            else
            {
                hoverLink = null;
            }

            if (Stack.Transient != null)
            {
                transientAway += Time.unscaledDeltaTime;
                if (transientAway > 0.35f) Stack.CloseTransient();
            }
        }

        internal void OpenTransient(Bubble parent, string link, Rect termRect)
        {
            if (Stack.Transient != null && Stack.Transient.SourceLink == link && Stack.Transient.Parent == parent) return;
            Stack.CloseTransient();
            if (Stack.Pinned.Count >= maxDepth.Value) return;
            // Termo de uma área própria da bolha (ícone de evento do calendário): o conteúdo vem de quem a decorou.
            List<LazyWidgetDataBase> data = null;
            Entity e = null;
            try
            {
                data = parent.HotData?.Invoke(link);
                if (data == null)
                {
                    e = Entity.FromLink(link);
                    if (e == null) return;
                    data = Content.Build(e);
                }
            }
            catch (Exception ex) { HudModule.Log.LogError($"montar {link}: {ex}"); return; }
            if (data == null || data.Count == 0) return;
            Bubble b = Stack.Create(parent.Depth + 1);
            if (b == null) return;
            b.Rt.name = PinStack.Prefix + "Transient" + b.Depth;
            b.Parent = parent;
            b.Side = parent.Side;
            b.Data = data;
            b.SourceLink = link;
            b.SourceRect = termRect;
            if (e == null && parent.HotDataPinned != null)
            {
                System.Func<string, List<LazyWidgetDataBase>> pinned = parent.HotDataPinned;
                b.PinData = () => pinned(link);
            }
            if (e == null) { }
            else if (e.Kind == EntityKind.Station) b.Image = Content.StationImage(e.Id);
            else if (e.Kind == EntityKind.Npc && parent.Image == null)
            {
                // A ficha aberta a partir do tooltip do nó não repete o retrato que a mãe já mostra.
                b.Image = Quests.QuestModel.Portrait(e.Id);
                b.Portrait = true;
            }
            Stack.Transient = b;
            Stack.Render(b, Styles.Read());
            transientAway = 0f;
            hoverLink = null;
        }

        private void ResetHover()
        {
            hoverLink = null;
            hoverOwner = null;
            hoverTime = 0f;
            transientAway = 0f;
        }

        private static Rect Grow(Rect r, float by) => Rect.MinMaxRect(r.xMin - by, r.yMin - by, r.xMax + by, r.yMax + by);

        private static Rect ScreenRectOf(RectTransform rt)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Resumo do estado para os cenários.</summary>
        internal string Describe()
        {
            string B(Bubble b) => b == null ? "-" : b.Rt == null ? "(destruída)" : $"{b.Rt.name} pinned={b.Pinned} depth={b.Depth} rect={b.ScreenRect()} link={b.SourceLink}";
            return "pinned=[" + string.Join(" | ", Stack.Pinned.Select(B)) + "] transient=" + B(Stack.Transient) + " native=" + Native.IsVisible(Native.Instance)
                + " suppress=" + (suppressTarget != null ? suppressTarget.name : "-");
        }
    }
}
