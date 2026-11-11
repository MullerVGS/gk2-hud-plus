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

        public Place Place => place;
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
            focus = place.Center;
            BuildUi();
            level = FitsNear() ? 1 : 0;
            ortho = OrthoFor(level);
            ApplyLight();
            ApplyCamera(true);
            UITooltip.Hide();
            LazyAudio.PlayAndForget("gui_click");
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
                if (steps < 0 && level == 0) { Exit(); return; }
                level = Mathf.Clamp(level + steps, 0, 1);
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
            focus = ClampToBoxes(focus);

            float target = OrthoFor(level);
            if (!Mathf.Approximately(ortho, target))
                ortho = Mathf.MoveTowards(ortho, target, savedOrtho * Time.unscaledDeltaTime / ZoomSeconds);
            ApplyCamera(false);
            PlaceInspect.Tick(this, mouse, over && !dragging, clicked);
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
            savedLight = LightField.GetValue(env) as TimeOfDayPresets;
            if (savedLight == wanted) return;
            SetLight(env, wanted);
            lightChanged = true;
        }

        private static void SetLight(EnvironmentEngine env, TimeOfDayPresets preset)
        {
            LightField.SetValue(env, preset);
            LightChanged.Invoke(env, new object[] { env.Data.TimeOfDay, true });
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

        private Vector3 ClampToBoxes(Vector3 p)
        {
            Vector3 best = p;
            float bestDist = float.MaxValue;
            foreach (Bounds b in place.Boxes)
            {
                float x = Mathf.Clamp(p.x, b.min.x, b.max.x);
                float z = Mathf.Clamp(p.z, b.min.z, b.max.z);
                float d = (x - p.x) * (x - p.x) + (z - p.z) * (z - p.z);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = new Vector3(x, p.y, z);
                }
            }
            return best;
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

        /// <summary>Nome do local no alto da vista, clonado de um rótulo de região do próprio mapa.</summary>
        private void BuildTitle()
        {
            TextMeshProUGUI model = null;
            foreach (TextMeshProUGUI t in owner.Map.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (t.GetComponent<TextStyleComponent>() != null && t.GetComponentInParent<UIMapMilestone>(true) == null) { model = t; break; }
            if (model == null) return;
            GameObject go = Instantiate(model.gameObject, Viewport, false);
            go.name = "GK2Map_Title";
            foreach (LocalizedLabel l in go.GetComponents<LocalizedLabel>()) DestroyImmediate(l);
            var rt = (RectTransform)go.transform;
            rt.localScale = Vector3.one;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(300f, 24f);
            rt.anchoredPosition = new Vector2(0f, -8f);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Top;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = place.Title;
            go.SetActive(true);
            created.Add(go);
        }

        public void Exit()
        {
            if (exited) return;
            exited = true;
            if (Current == this) Current = null;
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
