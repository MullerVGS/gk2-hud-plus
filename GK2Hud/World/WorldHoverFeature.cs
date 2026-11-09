using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GK2Hud.Tooltips;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace GK2Hud.World
{
    /// <summary>
    /// Tooltip ao parar o mouse sobre algo em andamento no mundo (craft, canteiro, corpo...). Mostra o UITooltip nativo
    /// ancorado num RectTransform proxy invisível (filho do UIRoot) que acompanha a posição de tela do objeto: o tooltip
    /// segue a câmera e o botão de fixar do <see cref="TooltipFeature"/> clona o singleton como em qualquer outro tooltip.
    /// Escolha do alvo a ~5 Hz; conteúdo refeito a ~4 Hz e só redesenhado quando muda (sem piscar, sem som repetido).
    /// Só leitura do estado do jogo.
    /// </summary>
    internal sealed class WorldHoverFeature : IDisposable
    {
        internal static WorldHoverFeature Current;

        internal const string ProxyName = PinStack.Prefix + "WorldProxy";
        private const float PickInterval = 0.2f;
        private const float RefreshInterval = 0.25f;
        private const float MoveThreshold = 4f;   // px de tela: menos que isso conta como mouse parado

        private readonly ConfigEntry<float> delay;
        internal readonly WorldTargets Targets = new WorldTargets();
        internal readonly WorldContent Content = new WorldContent();

        private RectTransform proxy;
        private WorldTarget target;
        private float targetSince, stillSince, nextPick, nextRefresh;
        private Vector2 stillAnchor;
        private bool showing, dismissed;
        private string shownSig;
        private bool loggedError;
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private PointerEventData pointer;
        private EventSystem pointerOwner;

        /// <summary>Modo de teste (cenários gk2hud-mundo-*.cs): no green o mouse não mexe.</summary>
        internal Vector2? VirtualMouse { get; set; }

        public WorldHoverFeature(ConfigEntry<float> delay)
        {
            this.delay = delay;
            Current = this;
            int orphans = DestroyOrphans();
            if (orphans > 0) Plugin.Log.LogInfo($"proxies de mundo órfãos destruídos: {orphans}");
        }

        public void Dispose()
        {
            HideOwn();
            if (proxy != null) Object.Destroy(proxy.gameObject);
            proxy = null;
            target = null;
            if (Current == this) Current = null;
        }

        /// <summary>Restos de uma carga anterior: procura pelo nome entre os filhos do pai do UITooltip.</summary>
        private static int DestroyOrphans()
        {
            UITooltip native = Native.Instance;
            if (native == null || native.transform.parent == null) return 0;
            int n = 0;
            Transform parent = native.transform.parent;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform ch = parent.GetChild(i);
                if (ch.name == ProxyName) { Object.Destroy(ch.gameObject); n++; }
            }
            return n;
        }

        public void Update()
        {
            try { Tick(); }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("hover no mundo: " + ex);
                loggedError = true;
                HideOwn();
                target = null;
            }
        }

        private void Tick()
        {
            float now = Time.unscaledTime;
            Vector2 mouse = VirtualMouse ?? (Vector2)Input.mousePosition;
            if ((mouse - stillAnchor).sqrMagnitude > MoveThreshold * MoveThreshold)
            {
                stillAnchor = mouse;
                stillSince = now;
            }

            if (!Ready())
            {
                Lose();
                return;
            }

            if (now >= nextPick)
            {
                nextPick = now + PickInterval;
                WorldTarget picked = OverUI(mouse) ? null : Targets.Pick(mouse);
                if (!WorldTarget.Same(picked, target))
                {
                    HideOwn();
                    TooltipFeature.Current?.ReleaseSuppress(proxy);
                    target = picked;
                    targetSince = now;
                    dismissed = false;
                    shownSig = null;
                }
                else if (picked != null)
                {
                    target = picked;   // mesmo objeto, retângulo atualizado
                }
            }
            if (target == null || !target.Alive) { Lose(); return; }

            if (!EnsureProxy()) return;
            target.UpdateRect();
            PlaceProxy(target.Rect);

            if (showing && !UITooltip.IsTooltipShowingAtTarget(proxy))
            {
                // Outro tooltip tomou o singleton ou ele foi fixado (PinNative esconde o nativo): não volta por cima.
                showing = false;
                dismissed = true;
            }
            if (dismissed) return;

            if (!showing)
            {
                // Tentativas a 4 Hz: se o funil recusar (alvo recém-fixado sob o mouse), não remonta o conteúdo por frame.
                if (now - Mathf.Max(stillSince, targetSince) < delay.Value || now < nextRefresh) return;
                nextRefresh = now + RefreshInterval;
                Show(true);
            }
            else if (now >= nextRefresh)
            {
                nextRefresh = now + RefreshInterval;
                Show(false);
            }
        }

        private void Lose()
        {
            HideOwn();
            target = null;
            dismissed = false;
        }

        /// <summary>Gameplay livre com mouse: sem janela, sem gamepad, sem cutscene/diálogo/sono, sem mira/ataque, sem construir nem carregar objeto para plantar/colocar.</summary>
        private static bool Ready()
        {
            if (LazyInput.IsGamepadActive) return false;
            MainGame game = MainGame.Instance;
            PlayerController pc = MainGame.PlayerController;
            if (game == null || game.GameSave == null || pc == null || MainGame.PlayerData == null) return false;
            if (CameraSystem.Instance == null || CameraSystem.Instance.WorldCamera == null) return false;
            if (LazyWindowsStackController.ActiveWindow != null || LazyWindowsStackController.HasAnyModalWindowOpened) return false;
            if (!pc.IsControlsEnabledExcept(TakenControlType.ByWork)) return false;
            if (PlayerInputHandler.IsAttackFocusHeld()) return false;
            SSMState st = pc.Ssm?.CurState;
            if (st != null && (st is PlantingPlayerState || st.GetType().Name.StartsWith("Attack", StringComparison.Ordinal))) return false;
            // Modo de construção (BuildManager já tira o controle por ByBuilding; o campo direto evita o getter Instance,
            // que procura na cena e loga erro quando não acha).
            BuildController bc = BuildControllerRef();
            if (bc != null && bc.IsBuildModeActive) return false;
            return Native.Instance != null;
        }

        private static readonly AccessTools.FieldRef<BuildController> BuildControllerRef =
            AccessTools.StaticFieldRefAccess<BuildController>(AccessTools.Field(typeof(BuildController), "cachedInstance"));

        /// <summary>
        /// Ponteiro sobre UI (HUD, bolhas fixadas...). Raycast próprio em vez de IsPointerOverGameObject para valer também
        /// com o mouse virtual; as bolhas nativas sobre objetos (UIObjectBubbleManager) contam como mundo.
        /// </summary>
        private bool OverUI(Vector2 mouse)
        {
            EventSystem es = EventSystem.current;
            if (es == null) return false;
            if (pointer == null || pointerOwner != es) { pointer = new PointerEventData(es); pointerOwner = es; }
            pointer.position = mouse;
            uiHits.Clear();
            es.RaycastAll(pointer, uiHits);
            Transform bubbles = UIObjectBubbleManager.Instance != null ? UIObjectBubbleManager.Instance.transform : null;
            foreach (RaycastResult h in uiHits)
            {
                if (h.gameObject == null) continue;
                if (bubbles != null && h.gameObject.transform.IsChildOf(bubbles)) continue;
                return true;
            }
            return false;
        }

        private bool EnsureProxy()
        {
            if (proxy != null) return true;
            UITooltip native = Native.Instance;
            if (native == null || native.transform.parent == null) return false;
            var go = new GameObject(ProxyName, typeof(RectTransform));
            proxy = (RectTransform)go.transform;
            proxy.SetParent(native.transform.parent, false);
            proxy.anchorMin = proxy.anchorMax = proxy.pivot = new Vector2(0.5f, 0.5f);
            return true;
        }

        /// <summary>Retângulo em px de tela → RectTransform sob o canvas overlay (posição de mundo = px de tela).</summary>
        private void PlaceProxy(Rect r)
        {
            Vector3 scale = proxy.lossyScale;
            float sx = Mathf.Max(0.0001f, scale.x), sy = Mathf.Max(0.0001f, scale.y);
            proxy.sizeDelta = new Vector2(r.width / sx, r.height / sy);
            proxy.position = new Vector3(r.center.x, r.center.y, proxy.position.z);
        }

        private void Show(bool first)
        {
            List<LazyWidgetDataBase> list = Content.Build(target);
            if (list == null || list.Count == 0)
            {
                HideOwn();
                return;
            }
            string sig = WorldContent.Signature(list);
            if (!first && sig == shownSig) return;
            UITooltip native = Native.Instance;
            Native.ShowAt.Invoke(native, new object[] { list, proxy, UIBasicBubble.ForceCornerPosition.Auto, true, Vector2.zero, TooltipPlacementPriority.TopRight });
            // O fade nativo (duração 0) zera o alfa por um frame: na atualização ele já estava visível.
            if (!first) { CanvasGroup g = Native.GroupOf(native); if (g != null) g.alpha = 1f; }
            showing = UITooltip.IsTooltipShowingAtTarget(proxy);
            shownSig = sig;
        }

        private void HideOwn()
        {
            if (showing && proxy != null && UITooltip.IsTooltipShowingAtTarget(proxy)) UITooltip.Hide();
            showing = false;
            shownSig = null;
        }

        /// <summary>Conteúdo vivo para a bolha fixada a partir do tooltip de mundo (null se o alvo não é o proxy).</summary>
        internal Func<List<LazyWidgetDataBase>> LiveFor(RectTransform tooltipTarget)
        {
            if (tooltipTarget == null || tooltipTarget != proxy || target == null) return null;
            WorldTarget t = target;
            return () => Content.BuildLive(t);
        }

        internal bool IsProxy(RectTransform rt) => rt != null && rt == proxy;

        /// <summary>Resumo do estado para os cenários.</summary>
        internal string Describe()
        {
            return "target=" + (target?.Describe() ?? "-") + " showing=" + showing + " dismissed=" + dismissed
                + " proxy=" + (proxy != null ? proxy.position.ToString("0") + " " + proxy.sizeDelta.ToString("0") : "-")
                + " nativeAtProxy=" + (proxy != null && UITooltip.IsTooltipShowingAtTarget(proxy));
        }
    }
}
