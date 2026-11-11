using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>
    /// Vista de um interior a partir do mapa. Não cria câmera nem carrega cena: o mundo inteiro (interiores incluídos)
    /// vive na mesma cena e só é ligado por pedaços conforme a câmera do mundo. A vista desliga o Cinemachine, leva a
    /// câmera do mundo para o local com o dobro do campo de visão e pede ao <c>ChunkManager</c> que refaça a
    /// visibilidade só quando a câmera anda. Na janela, o pergaminho e o fundo da moldura somem e o mundo aparece
    /// pelo vão; ao sair (scroll para trás, botão direito, fechar a janela ou trocar de aba) tudo volta como estava.
    /// </summary>
    internal class PlaceView : MonoBehaviour
    {
        private const float CameraBack = 10f;
        private const float ZoomSeconds = 0.15f;
        private const float DragThreshold = 6f;
        private const float KeyboardSpeed = 14f;

        public static PlaceView Current { get; private set; }
        public static bool Active => Current != null;

        /// <summary>Para os cenários do green: clique esquerdo/direito simulados na posição do mouse virtual.</summary>
        public static bool TestLeftClick { get; set; }
        public static bool TestRightClick { get; set; }

        private MapZoom owner;
        private Place place;
        private Camera cam;
        private Behaviour brain;
        private bool brainWasEnabled;
        private Vector3 savedPos;
        private float savedOrtho;
        private Vector3 focus;
        private float ortho;
        private int level;
        private bool exited;

        private TimeOfDayPresets savedLight;
        private bool lightChanged;
        private readonly List<GameObject> hidden = new List<GameObject>();

        private GameObject content;
        private Image back;
        private Image shadow;
        private readonly List<GameObject> created = new List<GameObject>();

        private bool pressed;
        private bool dragging;
        private Vector2 pressPos;
        private Vector2 lastMouse;

        private struct Frame
        {
            public Place Place;
            public Vector3 Focus;
            public int Level;
        }

        private readonly List<Frame> stack = new List<Frame>();
        private TextMeshProUGUI title;
        private float nextTitle;
        private bool anchoring;
        private Vector2 anchorPx;
        private Vector3 anchorWorld;
        private readonly Outline outline = new Outline();

        public Place Place => place;
        public Atlas Atlas => owner.Atlas;
        public float Ortho => ortho;
        public Vector3 Focus => focus;
        private WorldZoneWidget hudZone;
        public Camera Camera => cam;
        public RectTransform Viewport => owner.Scroll.viewport;

        public static bool Enter(MapZoom owner, Place place)
        {
            if (Active || owner == null || place == null) return false;
            CameraSystem cs = CameraSystem.Instance;
            Camera cam = cs != null ? cs.WorldCamera : null;
            if (cam == null || !cam.orthographic) return false;
            PlaceView v = owner.gameObject.AddComponent<PlaceView>();
            v.owner = owner;
            v.place = place;
            v.cam = cam;
            v.Begin();
            Current = v;
            return true;
        }

        private void Begin()
        {
            brain = cam.GetComponent("CinemachineBrain") as Behaviour;
            brainWasEnabled = brain != null && brain.enabled;
            savedPos = cam.transform.position;
            savedOrtho = cam.orthographicSize;
            if (brain != null) brain.enabled = false;
            BuildUi();
            savedLight = EnvironmentEngine.Instance != null ? LightField.GetValue(EnvironmentEngine.Instance) as TimeOfDayPresets : null;
            outline.Attach(cam);
            Show(place, place.Center, place.Outdoor ? 0 : -1);
        }

        /// <summary>Troca o lugar mostrado (sem mexer na pilha): foco, enquadramento, luz, título e carga do mundo.</summary>
        private void Show(Place next, Vector3 at, int lvl)
        {
            place = next;
            focus = at;
            level = lvl >= 0 ? lvl : FitsNear() ? 1 : 0;
            ortho = OrthoFor(level);
            anchoring = false;
            pressed = dragging = false;
            focus = ClampFocus(focus);
            PlaceInspect.Clear();
            ApplyLight();
            ApplyCamera(true);
            nextTitle = 0f;
            UpdateTitle();
            UITooltip.Hide();
            LazyAudio.PlayAndForget("gui_click");
        }

        /// <summary>
        /// Atravessa uma porta ou escada. Se ela leva de volta ao lugar de onde a vista veio, desfaz o passo (a pilha
        /// encolhe); senão empilha o lugar atual, e o scroll para trás volta por ele.
        /// </summary>
        public void Through(Door door)
        {
            Place next = Atlas != null ? Atlas.Through(door) : null;
            if (next == null) return;
            if (stack.Count > 0 && SamePlace(stack[stack.Count - 1].Place, next))
            {
                Frame f = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                Show(f.Place, next.Outdoor ? door.Dest.Position : f.Focus, f.Level);
                return;
            }
            stack.Add(new Frame { Place = place, Focus = focus, Level = level });
            Show(next, next.Center, next.Outdoor ? 1 : -1);
        }

        private static bool SamePlace(Place a, Place b)
        {
            return a.Outdoor == b.Outdoor && (a.Outdoor || a.Area == b.Area);
        }

        /// <summary>Um passo para trás: o lugar anterior da pilha ou, sem pilha, o mapa (centrado onde a vista estava).</summary>
        private void Back()
        {
            if (stack.Count == 0)
            {
                Place was = place;
                Vector3 at = focus;
                MapZoom map = owner;
                Exit();
                if (was.Outdoor && map != null) map.CenterOnWorld(at);
                return;
            }
            Frame f = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            Show(f.Place, f.Focus, f.Level);
        }

        private float OrthoFor(int lvl) => lvl == 0 ? savedOrtho * 2f : savedOrtho;

        /// <summary>O local cabe inteiro na área do mapa na escala normal do jogo? Senão a vista abre afastada (metade).</summary>
        private bool FitsNear()
        {
            Vector3[] c = new Vector3[4];
            Viewport.GetWorldCorners(c);
            float units = 2f * savedOrtho / Screen.height;
            float width = (c[2].x - c[0].x) * units;
            float depth = (c[2].y - c[0].y) * units / Mathf.Max(0.1f, cam.transform.up.z);
            Bounds b = place.Boxes[0];
            return b.size.x <= width && b.size.z <= depth;
        }

        private void Update()
        {
            if (exited) return;
            if (cam == null || owner == null || owner.Scroll == null) { Exit(); return; }
            Vector2 mouse = MapZoom.Mouse;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(Viewport, mouse, null);

            int steps = MapZoom.ReadScroll();
            if (steps != 0 && over)
            {
                if (steps < 0 && level == 0) { Back(); return; }
                if (steps > 0 && level == 1)
                {
                    // Já na escala normal: mais um passo de scroll sobre uma porta ou escada entra por ela.
                    Door door = PlaceInspect.DoorNear(mouse, DoorScrollRadius);
                    if (door != null) { Through(door); return; }
                }
                int nextLevel = Mathf.Clamp(level + steps, 0, 1);
                if (nextLevel != level)
                {
                    level = nextLevel;
                    anchoring = true;
                    anchorPx = mouse - ViewCenter();
                    anchorWorld = focus + GroundOffset(anchorPx, ortho);
                }
            }
            if (Input.GetMouseButtonDown(1) || TestRightClick)
            {
                TestRightClick = false;
                Exit();
                return;
            }

            float units = 2f * ortho / Screen.height;
            if (Input.GetMouseButtonDown(0) && over)
            {
                pressed = true;
                dragging = false;
                pressPos = lastMouse = mouse;
            }
            if (pressed && Input.GetMouseButton(0))
            {
                if (!dragging && (mouse - pressPos).sqrMagnitude > DragThreshold * DragThreshold) dragging = true;
                if (dragging)
                {
                    Vector2 d = mouse - lastMouse;
                    focus.x -= d.x * units;
                    focus.z -= d.y * units / Mathf.Max(0.1f, cam.transform.up.z);
                }
                lastMouse = mouse;
            }
            bool clicked = TestLeftClick;
            TestLeftClick = false;
            if (pressed && Input.GetMouseButtonUp(0))
            {
                if (!dragging && over) clicked = true;
                pressed = dragging = false;
            }
            if (!LazyInput.IsGamepadActive)
            {
                Vector2 dir = LazyInput.GetDirection();
                if (dir.sqrMagnitude > 0.0001f)
                {
                    float k = KeyboardSpeed * (ortho / savedOrtho) * Time.unscaledDeltaTime;
                    focus.x += dir.x * k;
                    focus.z += dir.y * k;
                }
            }
            float target = OrthoFor(level);
            if (!Mathf.Approximately(ortho, target))
            {
                ortho = Mathf.MoveTowards(ortho, target, savedOrtho * Time.unscaledDeltaTime / ZoomSeconds);
                // O ponto do chão sob o cursor fica parado enquanto a escala muda.
                if (anchoring && !dragging) focus = anchorWorld - GroundOffset(anchorPx, ortho);
            }
            else anchoring = false;
            focus = ClampFocus(focus);
            ApplyCamera(false);
            UpdateTitle();
            Door through = PlaceInspect.Tick(this, mouse, over && !dragging, clicked);
            outline.Draw(PlaceInspect.HoverRenderers, 2f * ortho / Screen.height);
            if (through != null) Through(through);
        }

        private const float DoorScrollRadius = 70f;

        private Vector2 ViewCenter()
        {
            Vector3[] c = new Vector3[4];
            Viewport.GetWorldCorners(c);
            return (c[0] + c[2]) * 0.5f;
        }

        /// <summary>Deslocamento no chão (XZ) de um ponto a <paramref name="px"/> pixels do centro da vista.</summary>
        private Vector3 GroundOffset(Vector2 px, float atOrtho)
        {
            float units = 2f * atOrtho / Screen.height;
            return new Vector3(px.x * units, 0f, px.y * units / Mathf.Max(0.1f, cam.transform.up.z));
        }

        /// <summary>Metade do que a vista mostra do chão, em unidades do mundo (X e Z).</summary>
        public Vector2 ViewHalfExtents()
        {
            Vector3[] c = new Vector3[4];
            Viewport.GetWorldCorners(c);
            Vector3 half = GroundOffset((c[2] - c[0]) * 0.5f, ortho);
            return new Vector2(half.x, half.z);
        }

        /// <summary>
        /// Interior: o foco anda dentro das zonas visitadas. Mundo aberto: a vista inteira (não só o foco) fica dentro
        /// de uma região conhecida do mapa, para não mostrar vizinhança ainda não explorada.
        /// </summary>
        private Vector3 ClampFocus(Vector3 p)
        {
            Vector2 shrink = place.Outdoor ? ViewHalfExtents() : Vector2.zero;
            Vector3 best = p;
            float bestDist = float.MaxValue;
            foreach (Bounds b in place.Boxes)
            {
                float x0 = b.min.x + shrink.x, x1 = b.max.x - shrink.x, z0 = b.min.z + shrink.y, z1 = b.max.z - shrink.y;
                float x = x0 > x1 ? b.center.x : Mathf.Clamp(p.x, x0, x1);
                float z = z0 > z1 ? b.center.z : Mathf.Clamp(p.z, z0, z1);
                float d = (x - p.x) * (x - p.x) + (z - p.z) * (z - p.z);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = new Vector3(x, p.y, z);
                }
            }
            return best;
        }

        private void UpdateTitle()
        {
            if (title == null || Time.unscaledTime < nextTitle) return;
            nextTitle = Time.unscaledTime + 0.25f;
            WorldZone zone = Atlas != null ? Atlas.TitleZoneAt(focus) : null;
            string text = zone != null ? ZoneInfo.Label(zone, hudZone) : string.Empty;
            if (title.text != text) title.text = text ?? string.Empty;
        }

        /// <summary>Põe o ponto <see cref="focus"/> no centro da área do mapa (que não é o centro da tela).</summary>
        private void ApplyCamera(bool force)
        {
            Transform t = cam.transform;
            float units = 2f * ortho / Screen.height;
            Vector3[] corners = new Vector3[4];
            Viewport.GetWorldCorners(corners);
            Vector2 center = (corners[0] + corners[2]) * 0.5f;
            Vector2 off = center - new Vector2(Screen.width, Screen.height) * 0.5f;
            Vector3 pos = focus - t.forward * CameraBack - t.right * (off.x * units) - t.up * (off.y * units);
            t.position = pos;
            cam.orthographicSize = ortho;
            if (force) RefreshChunks(true);
            NotifyCameraMoved();
        }

        /// <summary>
        /// Com o Cinemachine desligado ninguém avisa o jogo que a câmera andou, e é nesse aviso
        /// (<c>CinemachineCore.CameraUpdatedEvent</c>) que as bolhas dos objetos se reposicionam e o
        /// <c>ChunkManager</c> refaz a visibilidade. A vista dispara o mesmo aviso a cada frame.
        /// </summary>
        private void NotifyCameraMoved()
        {
            if (brain == null) return;
            if (cameraEvent == null)
            {
                Assembly cine = brain.GetType().Assembly;
                Type core = cine.GetType("Cinemachine.CinemachineCore") ?? cine.GetType("Unity.Cinemachine.CinemachineCore");
                FieldInfo f = core?.GetField("CameraUpdatedEvent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                cameraEvent = f?.GetValue(null);
                cameraEventInvoke = cameraEvent?.GetType().GetMethod("Invoke", new[] { brain.GetType() });
                cameraEventArgs = new object[] { brain };
                if (cameraEventInvoke == null)
                {
                    Plugin.Log.LogWarning("CameraUpdatedEvent não encontrado: bolhas não vão acompanhar a vista");
                    brainEventMissing = true;
                }
            }
            if (brainEventMissing) { RefreshChunks(false); return; }
            cameraEventInvoke.Invoke(cameraEvent, cameraEventArgs);
        }

        private object cameraEvent;
        private MethodInfo cameraEventInvoke;
        private object[] cameraEventArgs;
        private bool brainEventMissing;

        private static readonly FieldInfo LightField = AccessTools.Field(typeof(EnvironmentEngine), "timesOfDay");
        private static readonly FieldInfo LightsField = AccessTools.Field(typeof(EnvironmentEngine), "availablePresets");
        private static readonly MethodInfo LightChanged = AccessTools.Method(typeof(EnvironmentEngine), "OnTimeOfDayChanged");

        /// <summary>
        /// Luz e som ambiente do interior, como ao entrar pela porta, mas trocando só o preset em uso: o nome do preset
        /// que vai para o save (<c>EnvironmentData.timeOfDayPresetName</c>) não muda.
        /// </summary>
        private void ApplyLight()
        {
            EnvironmentEngine env = EnvironmentEngine.Instance;
            if (env == null || string.IsNullOrEmpty(place.Light)) return;
            var all = LightsField.GetValue(env) as Dictionary<string, TimeOfDayPresets>;
            if (all == null || !all.TryGetValue(place.Light, out TimeOfDayPresets wanted) || wanted == null) return;
            if (LightField.GetValue(env) as TimeOfDayPresets == wanted) return;
            SetLight(env, wanted);
            lightChanged = true;
        }

        /// <summary>
        /// O que o jogo faz ao trocar de preset no teleporte (<c>SetTimeOfDayPresetAsync</c>), menos gravar o nome no
        /// save: luz, som ambiente e o clima pausado em interior (sem isso chove dentro de casa).
        /// </summary>
        private static void SetLight(EnvironmentEngine env, TimeOfDayPresets preset)
        {
            LightField.SetValue(env, preset);
            LightChanged.Invoke(env, new object[] { env.Data.TimeOfDay, true });
            WeatherSystem weather = WeatherSystem.Instance;
            if (weather != null)
            {
                weather.SetPauseState(WeatherSystemPauseFlag.TimeOfDay, preset.indoorPreset);
                weather.ApplySoundParameters(preset.indoorPreset, preset.applySfxFromOutdoor);
            }
            env.SyncIndoorAudioSnapshot();
        }

        private static readonly MethodInfo ProcessChunks = AccessTools.Method(typeof(ChunkManager), "TryProcessChunkVisibility");
        private static readonly FieldInfo ChunkLastPos = AccessTools.Field(typeof(ChunkManager), "lastVisibilityCameraPosition");
        private static readonly FieldInfo ChunkHasLastPos = AccessTools.Field(typeof(ChunkManager), "hasLastVisibilityCameraPosition");

        /// <summary>
        /// Refaz a visibilidade dos pedaços do mundo para a câmera atual. O jogo só carrega as partes dos objetos aos
        /// poucos (sem travar o frame) quando entende que a câmera está andando devagar; a vista diz isso a ele
        /// (<paramref name="spread"/>) para a primeira abertura de um local não virar uma travada.
        /// </summary>
        private void RefreshChunks(bool spread)
        {
            ChunkManager chunks = LazySingleton<ChunkManager>.Instance;
            if (chunks == null) return;
            if (spread && cam != null && ProcessChunks != null && ChunkLastPos != null && ChunkHasLastPos != null)
            {
                ChunkLastPos.SetValue(chunks, cam.transform.position - cam.transform.right * 0.01f);
                ChunkHasLastPos.SetValue(chunks, true);
                ProcessChunks.Invoke(chunks, new object[] { true });
            }
            else chunks.ForceProcessVisibility();
        }

        private void BuildUi()
        {
            content = owner.Scroll.content.gameObject;
            content.SetActive(false);
            // As bordas esfumadas do pergaminho (irmãs do conteúdo) não fazem sentido sobre o mundo.
            foreach (Transform t in Viewport)
                if (t.gameObject != content && t.gameObject.activeSelf && t.name.StartsWith("Fade"))
                {
                    t.gameObject.SetActive(false);
                    hidden.Add(t.gameObject);
                }

            Transform root = owner.transform;
            while (root != null && root.Find("BgShadow") == null) root = root.parent;
            if (root != null)
            {
                back = FindBack(root);
                if (back != null) back.enabled = false;
                shadow = root.Find("BgShadow").GetComponent<Image>();
                if (shadow != null && shadow.enabled)
                {
                    shadow.enabled = false;
                    BuildShadowStrips(shadow);
                }
                else shadow = null;
            }
            BuildTitle();
        }

        /// <summary>
        /// Fundo opaco da moldura da janela atrás do mapa: a imagem "Back" (fora da página do mapa) que cobre a área
        /// visível. O caminho muda entre a janela do TAB e a janela de mapa dos marcos; o nome não.
        /// </summary>
        private Image FindBack(Transform root)
        {
            Vector3[] c = new Vector3[4];
            Viewport.GetWorldCorners(c);
            Vector2 center = (c[0] + c[2]) * 0.5f;
            foreach (Image img in root.GetComponentsInChildren<Image>(false))
            {
                if (img.name != "Back" || !img.enabled || img.transform.IsChildOf(owner.transform)) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint(img.rectTransform, center, null)) return img;
            }
            return null;
        }

        private const int BlackSortingOrder = 45;

        /// <summary>
        /// Em volta do vão: o escurecimento nativo (que cobria a tela toda) é refeito em quatro faixas, e por baixo
        /// do HUD entra um fundo preto, porque o mundo ali é o do local visto e vazaria pela borda da moldura. O preto
        /// fica acima das bolhas de objetos (ordem 40) e abaixo do HUD (50).
        /// </summary>
        private void BuildShadowStrips(Image shadowImage)
        {
            RectTransform host = shadowImage.rectTransform;
            Strips(host, shadowImage.color, shadowImage.sprite, shadowImage.raycastTarget);

            Canvas rootCanvas = host.GetComponentInParent<Canvas>();
            Transform uiRoot = rootCanvas != null ? rootCanvas.transform.parent : null;
            if (uiRoot == null) return;
            var go = new GameObject("GK2Map_Black", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(uiRoot, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            Canvas canvas = go.GetComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = BlackSortingOrder;
            created.Add(go);
            Strips(rt, Color.black, null, false);
        }

        private void Strips(RectTransform host, Color color, Sprite sprite, bool raycast)
        {
            Vector3[] c = new Vector3[4];
            Viewport.GetWorldCorners(c);
            Vector2 lo = host.InverseTransformPoint(c[0]);
            Vector2 hi = host.InverseTransformPoint(c[2]);
            Rect r = host.rect;
            Strip(host, color, sprite, raycast, r.xMin, r.yMin, lo.x, r.yMax);
            Strip(host, color, sprite, raycast, hi.x, r.yMin, r.xMax, r.yMax);
            Strip(host, color, sprite, raycast, lo.x, hi.y, hi.x, r.yMax);
            Strip(host, color, sprite, raycast, lo.x, r.yMin, hi.x, lo.y);
        }

        private void Strip(RectTransform host, Color color, Sprite sprite, bool raycast, float x0, float y0, float x1, float y1)
        {
            if (x1 - x0 <= 0f || y1 - y0 <= 0f) return;
            var go = new GameObject("GK2Map_Shadow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(host, false);
            rt.anchorMin = rt.anchorMax = host.pivot;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x0, y0);
            rt.sizeDelta = new Vector2(x1 - x0, y1 - y0);
            Image img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = raycast;
            created.Add(go);
        }

        private static readonly AccessTools.FieldRef<WorldZoneWidget, TextMeshProUGUI> HudLabelRef = AccessTools.FieldRefAccess<WorldZoneWidget, TextMeshProUGUI>("worldZoneLabel");

        /// <summary>
        /// Nome e informação do local no alto da vista ("Morgue 5/7"), no rótulo clonado da placa de zona do HUD: mesma
        /// fonte, estilo e ícones que o jogo usa para dizer onde o jogador está.
        /// </summary>
        private void BuildTitle()
        {
            hudZone = FindFirstObjectByType<WorldZoneWidget>(FindObjectsInactive.Include);
            TextMeshProUGUI model = hudZone != null ? HudLabelRef(hudZone) : null;
            if (model == null) return;
            GameObject go = Instantiate(model.gameObject, Viewport, false);
            go.name = "GK2Map_Title";
            foreach (LocalizedLabel l in go.GetComponents<LocalizedLabel>()) DestroyImmediate(l);
            foreach (LayoutElement l in go.GetComponents<LayoutElement>()) DestroyImmediate(l);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(400f, 24f);
            rt.anchoredPosition = new Vector2(0f, -8f);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = string.Empty;
            title = text;
            go.SetActive(true);
            created.Add(go);
        }

        public void Exit()
        {
            if (exited) return;
            exited = true;
            if (Current == this) Current = null;
            outline.Detach();
            stack.Clear();
            PlaceInspect.Clear();
            foreach (GameObject go in created) if (go != null) Destroy(go);
            created.Clear();
            foreach (GameObject go in hidden) if (go != null) go.SetActive(true);
            hidden.Clear();
            EnvironmentEngine env = EnvironmentEngine.Instance;
            if (lightChanged && env != null && savedLight != null) SetLight(env, savedLight);
            lightChanged = false;
            if (shadow != null) shadow.enabled = true;
            if (back != null) back.enabled = true;
            if (content != null) content.SetActive(true);
            if (cam != null)
            {
                cam.transform.position = savedPos;
                cam.orthographicSize = savedOrtho;
                if (brain != null) brain.enabled = brainWasEnabled;
                RefreshChunks(false);
            }
            Destroy(this);
        }

        private void OnDisable()
        {
            Exit();
        }
    }
}
