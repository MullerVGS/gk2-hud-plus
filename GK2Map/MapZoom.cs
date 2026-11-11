using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>
    /// Zoom do mapa nativo. A arte tem 1200×1152 pixels e o jogo a mostra a 2 px de tela por pixel de arte; os níveis
    /// mantêm a razão inteira (1, 2, 3 e 4 px de tela por pixel de arte) para a arte não borrar. O conteúdo do
    /// <c>ScrollRect</c> cresce com o zoom (<c>sizeDelta</c>), então o arrasto e o pan por teclado nativos continuam
    /// limitando certo; marcadores e rótulos ficam do tamanho de sempre.
    /// </summary>
    internal class MapZoom : MonoBehaviour
    {
        public static readonly float[] Levels = { 0.5f, 1f, 1.5f, 2f };
        public const int DefaultLevel = 1;
        private const float AnimSeconds = 0.12f;

        /// <summary>Para os cenários do green: posição do mouse e passos de scroll simulados.</summary>
        public static Vector2? VirtualMouse { get; set; }
        public static int TestScroll { get; set; }

        private static readonly AccessTools.FieldRef<MapPageWidget, ScrollRect> ScrollRef = AccessTools.FieldRefAccess<MapPageWidget, ScrollRect>("scrollRect");
        private static readonly AccessTools.FieldRef<MapPageWidget, RectTransform> MapRef = AccessTools.FieldRefAccess<MapPageWidget, RectTransform>("mapRect");
        private static readonly AccessTools.FieldRef<MapPageWidget, RectTransform> PlayerRef = AccessTools.FieldRefAccess<MapPageWidget, RectTransform>("playerIcon");
        private static readonly AccessTools.FieldRef<MapPageWidget, RectTransform> MilestonesRef = AccessTools.FieldRefAccess<MapPageWidget, RectTransform>("milestonesRect");

        private MapPageWidget widget;
        private ScrollRect scroll;
        private RectTransform map;
        private RectTransform player;
        private RectTransform milestones;
        private Vector2 baseSize;
        private float zoom = 1f;
        private Vector2 pivot;
        private readonly Dictionary<Transform, Vector3> markers = new Dictionary<Transform, Vector3>();
        private readonly Dictionary<Transform, Vector3> labels = new Dictionary<Transform, Vector3>();
        private List<Place> places = new List<Place>();
        private bool placesPending;
        private Place hovered;
        private RectTransform hoverProxy;
        private bool pressOnPlace;
        private Rect? explored;
        private readonly List<GameObject> hiddenLabels = new List<GameObject>();

        public MapPageWidget Widget => widget;
        public ScrollRect Scroll => scroll;
        public RectTransform Map => map;
        public float Zoom => zoom;
        public static Vector2 Mouse => VirtualMouse ?? (Vector2)Input.mousePosition;

        public void OnRedraw(MapPageWidget w)
        {
            widget = w;
            scroll = ScrollRef(w);
            map = MapRef(w);
            player = PlayerRef(w);
            milestones = MilestonesRef(w);
            if (scroll == null || map == null || scroll.content == null || scroll.viewport == null) return;
            baseSize = map.sizeDelta;
            CollectMarkers();
            ComputeExplored();
            // O jogo acabou de centralizar no jogador com o mapa em escala 1; refaz na escala atual.
            Vector2 focus = scroll.content.anchoredPosition;
            zoom = 1f;
            Apply(TargetZoom, Vector2.zero, focus * TargetZoom);
            placesPending = true;
        }

        /// <summary>As setinhas saem da textura do mapa, que o jogo carrega de forma assíncrona: tenta até ela existir.</summary>
        private void BuildPlaces()
        {
            Plugin p = Plugin.Instance;
            places.Clear();
            if (!p.PlacesEnabled.Value) { placesPending = false; return; }
            List<Vector2> arrows = p.Arrows(map.GetComponent<Image>(), baseSize);
            if (arrows == null) return;
            placesPending = false;
            places = Places.Build(map, arrows);
        }

        public Place PlaceAt(Vector2 mouse, float radius)
        {
            if (places.Count == 0 || widget.CurrentSelected != null) return null;
            if (!RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, mouse, null)) return null;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(map, mouse, null, out Vector2 local);
            Place best = null;
            float bestDist = radius;
            foreach (Place pl in places)
            {
                float d = Vector2.Distance(pl.MapPos, local);
                if (d < bestDist) { best = pl; bestDist = d; }
            }
            return best;
        }

        private void UpdatePlaces(int steps)
        {
            Vector2 mouse = Mouse;
            Place at = PlaceAt(mouse, Mathf.Max(8f, 10f / zoom));
            if (at != hovered)
            {
                hovered = at;
                if (at == null) UITooltip.Hide();
                else
                {
                    if (hoverProxy == null)
                    {
                        var go = new GameObject("GK2Map_Arrow", typeof(RectTransform));
                        hoverProxy = (RectTransform)go.transform;
                        hoverProxy.SetParent(map, false);
                        hoverProxy.sizeDelta = new Vector2(12f, 10f);
                    }
                    hoverProxy.anchoredPosition = at.MapPos;
                    hoverProxy.localScale = Vector3.one / zoom;
                    UITooltip.ShowSimpleInfo(hoverProxy, L10n.LookInside, default, at.Title);
                }
            }
            if (Input.GetMouseButtonDown(0)) pressOnPlace = hovered != null;
            bool click = PlaceView.TestLeftClick;
            if (Input.GetMouseButtonUp(0))
            {
                click |= pressOnPlace && hovered != null;
                pressOnPlace = false;
            }
            Plugin p = Plugin.Instance;
            Place enter = click ? hovered : null;
            // No zoom máximo, mais um passo de scroll perto de uma setinha entra no local.
            if (enter == null && steps > 0 && p.ZoomIndex == Levels.Length - 1)
                enter = hovered ?? PlaceAt(mouse, 40f / zoom);
            if (enter != null)
            {
                PlaceView.TestLeftClick = false;
                hovered = null;
                PlaceView.Enter(this, enter);
            }
        }

        private float TargetZoom
        {
            get
            {
                Plugin p = Plugin.Instance;
                if (p == null || !p.ZoomEnabled.Value) return 1f;
                return Levels[Mathf.Clamp(p.ZoomIndex, 0, Levels.Length - 1)];
            }
        }

        private void Update()
        {
            Plugin p = Plugin.Instance;
            if (p == null || scroll == null || map == null) return;
            if (PlaceView.Active) return;
            if (placesPending) BuildPlaces();
            int steps = ReadScroll();
            UpdatePlaces(steps);
            if (PlaceView.Active) return;
            if (steps != 0 && p.ZoomEnabled.Value && MouseOverViewport(out Vector2 local))
            {
                int next = Mathf.Clamp(p.ZoomIndex + steps, 0, Levels.Length - 1);
                if (next != p.ZoomIndex)
                {
                    p.ZoomIndex = next;
                    pivot = local;
                    LazyAudio.PlayAndForget("gui_hover_light");
                }
            }
            float target = TargetZoom;
            if (!Mathf.Approximately(zoom, target))
            {
                float step = Mathf.Abs(target - zoom) <= 0.001f ? 1f : Time.unscaledDeltaTime / AnimSeconds;
                float next = Mathf.MoveTowards(zoom, target, Mathf.Max(0.02f, step * Mathf.Max(0.5f, Mathf.Abs(target - zoom))));
                Apply(next, pivot, null);
            }
        }

        public static int ReadScroll()
        {
            if (TestScroll != 0)
            {
                int t = TestScroll;
                TestScroll = 0;
                return t;
            }
            if (LazyInput.IsGamepadActive) return 0;
            float y = Input.mouseScrollDelta.y;
            return y > 0.01f ? 1 : y < -0.01f ? -1 : 0;
        }

        /// <summary>Mouse dentro da área do mapa; <paramref name="local"/> sai em unidades do canvas a partir do centro dela.</summary>
        public bool MouseOverViewport(out Vector2 local)
        {
            local = Vector2.zero;
            RectTransform vp = scroll.viewport;
            Vector2 mouse = Mouse;
            if (!RectTransformUtility.RectangleContainsScreenPoint(vp, mouse, null)) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(vp, mouse, null, out local);
            local -= vp.rect.center;
            return true;
        }

        /// <summary>
        /// Aplica a escala <paramref name="z"/> mantendo parado o ponto do mapa que está sob <paramref name="around"/>
        /// (posição na área visível, a partir do centro), ou põe o conteúdo em <paramref name="position"/>.
        /// </summary>
        private void Apply(float z, Vector2 around, Vector2? position)
        {
            RectTransform content = scroll.content;
            Vector2 pos = position ?? around - (around - content.anchoredPosition) * (z / zoom);
            zoom = z;
            content.sizeDelta = baseSize * z;
            map.localScale = new Vector3(z, z, 1f);
            content.anchoredPosition = Clamp(pos);
            // Rótulos ficam sempre do tamanho nativo; marcadores também, menos no mapa afastado, onde encolhem com a
            // arte (1 px de tela por pixel de arte) para não cobrir os nomes.
            float inv = 1f / z;
            float markerInv = z < 1f ? 1f : inv;
            foreach (KeyValuePair<Transform, Vector3> m in labels)
                if (m.Key != null) m.Key.localScale = m.Value * inv;
            foreach (KeyValuePair<Transform, Vector3> m in markers)
                if (m.Key != null) m.Key.localScale = m.Value * markerInv;
        }

        private static readonly AccessTools.FieldRef<UIMapZone, bool> AlwaysVisibleRef = AccessTools.FieldRefAccess<UIMapZone, bool>("alwaysVisible");
        private static readonly Vector3[] CornerBuffer = new Vector3[4];

        /// <summary>
        /// Área já explorada do mapa: a união das áreas de nuvens das regiões que o jogo marca como conhecidas
        /// (<c>KnowledgeSystem.knownMapZones</c>), mais onde o jogador está. A arte tem trechos sem nuvem e rótulos
        /// soltos fora de qualquer região (o jogo conta com o jogador não rolar até lá); com o zoom afastado eles
        /// apareceriam de uma vez, então a vista do mapa fica presa a essa área e o que está fora dela some.
        /// </summary>
        private void ComputeExplored()
        {
            explored = null;
            foreach (GameObject go in hiddenLabels) if (go != null) go.SetActive(true);
            hiddenLabels.Clear();
            GameSave save = MainGame.Instance != null ? MainGame.Instance.GameSave : null;
            if (save == null) return;
            List<string> known = save.knowledgeSystem.knownMapZones;
            bool any = false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            void Grow(Vector2 a, Vector2 b)
            {
                any = true;
                x0 = Mathf.Min(x0, a.x); y0 = Mathf.Min(y0, a.y);
                x1 = Mathf.Max(x1, b.x); y1 = Mathf.Max(y1, b.y);
            }
            foreach (UIMapZone zone in map.GetComponentsInChildren<UIMapZone>(true))
            {
                if (!known.Contains(zone.Id) && !AlwaysVisibleRef(zone)) continue;
                RectTransform clouds = zone.transform.Find("Clouds") as RectTransform;
                if (clouds == null) continue;
                clouds.GetWorldCorners(CornerBuffer);
                Grow(map.InverseTransformPoint(CornerBuffer[0]), map.InverseTransformPoint(CornerBuffer[2]));
            }
            if (!any) return;
            if (player != null && player.gameObject.activeSelf)
            {
                Vector2 p = player.anchoredPosition;
                Grow(p - new Vector2(80f, 80f), p + new Vector2(80f, 80f));
            }
            Vector2 half = baseSize * 0.5f;
            Rect e = Rect.MinMaxRect(Mathf.Max(x0, -half.x), Mathf.Max(y0, -half.y), Mathf.Min(x1, half.x), Mathf.Min(y1, half.y));
            explored = e;
            // Rótulos soltos (fora de qualquer região) e marcos fora da área explorada não aparecem.
            foreach (TextMeshProUGUI label in map.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (label.GetComponentInParent<UIMapZone>(true) != null || (milestones != null && label.transform.IsChildOf(milestones))) continue;
                if (label.gameObject.activeSelf && !e.Contains((Vector2)map.InverseTransformPoint(label.transform.position)))
                {
                    label.gameObject.SetActive(false);
                    hiddenLabels.Add(label.gameObject);
                }
            }
            if (milestones != null)
                foreach (Transform t in milestones)
                    if (t.gameObject.activeSelf && !e.Contains((Vector2)map.InverseTransformPoint(t.position))) t.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (scroll == null || scroll.content == null || explored == null || PlaceView.Active) return;
            Vector2 pos = scroll.content.anchoredPosition;
            Vector2 clamped = Clamp(pos);
            if ((clamped - pos).sqrMagnitude > 0.0001f) scroll.content.anchoredPosition = clamped;
        }

        public Vector2 Clamp(Vector2 pos)
        {
            if (explored.HasValue && zoom > 0f)
            {
                Rect e = explored.Value;
                Vector2 view = scroll.viewport.rect.size * (0.5f / zoom);
                Vector2 c = -pos / zoom;
                c.x = e.width <= view.x * 2f ? e.center.x : Mathf.Clamp(c.x, e.xMin + view.x, e.xMax - view.x);
                c.y = e.height <= view.y * 2f ? e.center.y : Mathf.Clamp(c.y, e.yMin + view.y, e.yMax - view.y);
                pos = -c * zoom;
            }
            Vector2 half = (scroll.content.sizeDelta - scroll.viewport.rect.size) * 0.5f;
            pos.x = half.x <= 0f ? 0f : Mathf.Clamp(pos.x, -half.x, half.x);
            pos.y = half.y <= 0f ? 0f : Mathf.Clamp(pos.y, -half.y, half.y);
            return pos;
        }

        /// <summary>Marcadores e rótulos que não crescem com o mapa: marcos, jogador, ícones de luta e de dia, nomes.</summary>
        private void CollectMarkers()
        {
            float labelK = zoom;
            float markerK = zoom < 1f ? 1f : zoom;
            void Add(Dictionary<Transform, Vector3> set, Transform t, float k)
            {
                if (t != null && !markers.ContainsKey(t) && !labels.ContainsKey(t)) set[t] = t.localScale * k;
            }
            if (milestones != null) foreach (Transform t in milestones) Add(markers, t, markerK);
            Add(markers, player, markerK);
            foreach (UIMapFightIcon icon in map.GetComponentsInChildren<UIMapFightIcon>(true)) Add(markers, icon.transform, markerK);
            foreach (UIMapDayActivityIcon icon in map.GetComponentsInChildren<UIMapDayActivityIcon>(true)) Add(markers, icon.transform, markerK);
            foreach (TextMeshProUGUI label in map.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (milestones == null || !label.transform.IsChildOf(milestones)) Add(labels, label.transform, labelK);
        }

        private void OnDisable()
        {
            if (hovered != null) UITooltip.Hide();
            hovered = null;
            pressOnPlace = false;
        }

        public void Restore()
        {
            if (PlaceView.Current != null) PlaceView.Current.Exit();
            if (hoverProxy != null) Destroy(hoverProxy.gameObject);
            if (scroll == null || map == null || scroll.content == null) return;
            Vector2 pos = scroll.content.anchoredPosition / zoom;
            foreach (KeyValuePair<Transform, Vector3> m in markers)
                if (m.Key != null) m.Key.localScale = m.Value;
            foreach (KeyValuePair<Transform, Vector3> m in labels)
                if (m.Key != null) m.Key.localScale = m.Value;
            markers.Clear();
            labels.Clear();
            foreach (GameObject go in hiddenLabels) if (go != null) go.SetActive(true);
            hiddenLabels.Clear();
            explored = null;
            zoom = 1f;
            map.localScale = Vector3.one;
            scroll.content.sizeDelta = baseSize;
            scroll.content.anchoredPosition = Clamp(pos);
        }
    }
}
