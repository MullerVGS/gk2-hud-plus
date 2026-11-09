using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.World
{
    /// <summary>
    /// Um objeto do mundo com algo em andamento: o Wgo (estação, canteiro...) ou um item no chão (corpo apodrecendo).
    /// Os renderers vêm do cache de <see cref="WorldTargets"/>; o retângulo de tela é projetado a cada escolha/frame.
    /// </summary>
    internal sealed class WorldTarget
    {
        public Wgo View;
        public WgoData Data;
        public DropData Drop;
        public DropView DropView;
        public Renderer[] Renderers;
        public Rect Rect;            // px de tela: objeto ∪ bolha nativa
        public Rect BodyRect;        // px de tela: só o objeto
        public Vector2 Center;       // px de tela: centro visual do objeto
        public bool RayHit;          // atingido pelo raycast físico do mouse nesta escolha

        /// <summary>
        /// Wgo: view ativa e ainda ligada a este WgoData. Corpo: DropView é poolable (DropView.cs:11,195,225), então exige
        /// view ativa, não despawnando e ainda mostrando este DropData.
        /// </summary>
        public bool Alive => Drop != null
            ? DropView != null && DropView.isActiveAndEnabled && !DropView.IsDespawning && DropView.Data == Drop
            : View != null && View.isActiveAndEnabled && View.HasData && View.Data == Data;

        public static bool Same(WorldTarget a, WorldTarget b)
        {
            if (a == null || b == null) return a == b;
            return a.Drop != null ? a.Drop == b.Drop : a.Data != null && a.Data == b.Data;
        }

        public Vector3 Pivot => Drop != null ? DropView.transform.position : View.transform.position;

        /// <summary>Retângulo do objeto; a bolha nativa só entra quando <paramref name="withBubble"/> (alvo atual ou mouse perto).</summary>
        public void UpdateBody()
        {
            BodyRect = WorldTargets.ProjectRenderers(Renderers, out Center);
            if (BodyRect.width <= 0f)
            {
                Vector2 s = WorldTargets.Project(Pivot);
                BodyRect = new Rect(s.x - 24f, s.y, 48f, 48f);
                Center = BodyRect.center;
            }
            Rect = BodyRect;
        }

        public void UpdateRect()
        {
            UpdateBody();
            AddBubble();
        }

        public void AddBubble()
        {
            if (View != null && WorldTargets.NativeBubbleRect(Data, out Rect b)) Rect = WorldTargets.Union(BodyRect, b);
        }

        public string Describe()
        {
            string id = Drop != null ? "drop:" + Drop.Id : Data?.id;
            return id + " rect=" + Rect.ToString("0") + " center=" + Center.ToString("0") + (RayHit ? " ray" : "");
        }
    }

    /// <summary>
    /// Candidatos (objetos com algo em andamento na cena, refeitos a cada 1 s, só os perto da tela) e escolha do alvo sob
    /// o mouse: entre os que contêm o cursor no retângulo (objeto ∪ bolha nativa) ou foram atingidos pelo raycast físico
    /// (layers 6 e 16), vence o de centro mais próximo do cursor. O raycast sozinho erra (o vizinho da frente rouba), o
    /// retângulo sozinho também (renderers grandes): a combinação resolve os dois.
    /// Custo: renderers por Wgo em cache (refeito só se a hierarquia muda, um renderer some ou a cada 10 s), listas
    /// reaproveitadas, bolha nativa medida só para candidatos com o mouse por perto.
    /// </summary>
    internal sealed class WorldTargets
    {
        private const float RebuildInterval = 1f;
        private const float RendererCacheSeconds = 10f;
        private const float ScreenMargin = 600f;   // px fora da tela que ainda contam (a câmera anda entre reconstruções)
        private const float BubbleReachX = 160f, BubbleReachUp = 260f;   // px: a bolha nativa fica sobre o objeto
        private const int RayMask = (1 << 6) | (1 << 16);
        private const float RayBonus = 24f;   // px: o objeto que o raio físico atinge ganha desempate

        private sealed class RendererCache
        {
            public Renderer[] Renderers;
            public int HierarchyCount;
            public float Expires;
        }

        private List<WorldTarget> candidates = new List<WorldTarget>();
        private List<WorldTarget> spare = new List<WorldTarget>();
        private Dictionary<WgoData, WorldTarget> byData = new Dictionary<WgoData, WorldTarget>();
        private Dictionary<WgoData, WorldTarget> byDataNext = new Dictionary<WgoData, WorldTarget>();
        private Dictionary<DropData, WorldTarget> byDrop = new Dictionary<DropData, WorldTarget>();
        private Dictionary<DropData, WorldTarget> byDropNext = new Dictionary<DropData, WorldTarget>();
        private static readonly Dictionary<int, RendererCache> RendererCaches = new Dictionary<int, RendererCache>();
        private static readonly Dictionary<Type, bool> SkippedRendererTypes = new Dictionary<Type, bool>();
        private static readonly List<Renderer> TmpRenderers = new List<Renderer>();
        private static readonly List<Renderer> TmpSolid = new List<Renderer>();
        private static readonly List<Renderer> TmpFlat = new List<Renderer>();
        private static readonly List<Graphic> TmpGraphics = new List<Graphic>();
        private readonly List<KeyValuePair<WgoData, Wgo>> wgoBuffer = new List<KeyValuePair<WgoData, Wgo>>();
        private readonly List<KeyValuePair<DropData, DropView>> dropBuffer = new List<KeyValuePair<DropData, DropView>>();
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private float nextRebuild;
        private static readonly Vector3[] Corners = new Vector3[4];

        public int Count => candidates.Count;
        public IReadOnlyList<WorldTarget> All => candidates;

        public WorldTarget Pick(Vector2 mouse)
        {
            if (Time.unscaledTime >= nextRebuild) Rebuild();
            if (candidates.Count == 0) return null;

            foreach (WorldTarget c in candidates) c.RayHit = false;
            Ray ray = CameraSystem.ScreenPointToRay(mouse);
            int n = Physics.RaycastNonAlloc(ray, hits, 500f, RayMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Wgo w = hits[i].collider != null ? hits[i].collider.GetComponentInParent<Wgo>() : null;
                if (w != null && w.HasData && byData.TryGetValue(w.Data, out WorldTarget t)) t.RayHit = true;
            }

            WorldTarget best = null;
            float bestScore = float.MaxValue;
            float sw = Screen.width, sh = Screen.height;
            foreach (WorldTarget c in candidates)
            {
                if (!c.Alive) continue;
                c.UpdateBody();
                Rect b = c.BodyRect;
                if (b.xMax < -BubbleReachX || b.xMin > sw + BubbleReachX || b.yMax < 0f || b.yMin > sh) continue;
                if (mouse.x >= b.xMin - BubbleReachX && mouse.x <= b.xMax + BubbleReachX && mouse.y >= b.yMin && mouse.y <= b.yMax + BubbleReachUp)
                    c.AddBubble();
                if (!c.RayHit && !c.Rect.Contains(mouse)) continue;
                float score = (c.Center - mouse).magnitude - (c.RayHit ? RayBonus : 0f);
                if (score < bestScore) { bestScore = score; best = c; }
            }
            return best;
        }

        public void Invalidate() => nextRebuild = 0f;

        private void Rebuild()
        {
            nextRebuild = Time.unscaledTime + RebuildInterval;
            float sw = Screen.width, sh = Screen.height;
            spare.Clear();
            byDataNext.Clear();
            byDropNext.Clear();

            wgoBuffer.Clear();
            WorldProcesses.WgosWithProcess(wgoBuffer);
            foreach (KeyValuePair<WgoData, Wgo> kv in wgoBuffer)
            {
                if (!NearScreen(kv.Value.transform.position, sw, sh)) continue;
                if (!byData.TryGetValue(kv.Key, out WorldTarget t) || t.View != kv.Value)
                    t = new WorldTarget { View = kv.Value, Data = kv.Key };
                t.Renderers = RenderersCached(kv.Value.gameObject);
                byDataNext[kv.Key] = t;
                spare.Add(t);
            }
            dropBuffer.Clear();
            WorldProcesses.DropsWithProcess(dropBuffer);
            foreach (KeyValuePair<DropData, DropView> kv in dropBuffer)
            {
                if (!NearScreen(kv.Value.transform.position, sw, sh)) continue;
                if (!byDrop.TryGetValue(kv.Key, out WorldTarget t) || t.DropView != kv.Value)
                    t = new WorldTarget { Drop = kv.Key, DropView = kv.Value };
                t.Renderers = RenderersCached(kv.Value.gameObject);
                byDropNext[kv.Key] = t;
                spare.Add(t);
            }
            (candidates, spare) = (spare, candidates);
            (byData, byDataNext) = (byDataNext, byData);
            (byDrop, byDropNext) = (byDropNext, byDrop);
            if (RendererCaches.Count > 512) RendererCaches.Clear();
        }

        private static bool NearScreen(Vector3 world, float sw, float sh)
        {
            Vector2 s = Project(world);
            return s.x > -ScreenMargin && s.x < sw + ScreenMargin && s.y > -ScreenMargin && s.y < sh + ScreenMargin;
        }

        /// <summary>
        /// Renderers que formam o objeto, em cache por GameObject. Sombras ("_sh"), máscaras "blackout", luzes falsas e
        /// partículas aumentam muito o retângulo sem ser o objeto: ficam de fora. Objeto todo chato (canteiro, corpo): usa o
        /// que houver. Refeito quando a hierarquia muda (AddWgoPart, estágios), um renderer some ou o cache vence.
        /// </summary>
        internal static Renderer[] RenderersCached(GameObject go)
        {
            int id = go.GetInstanceID();
            int hc = go.transform.hierarchyCount;
            float now = Time.unscaledTime;
            if (RendererCaches.TryGetValue(id, out RendererCache c) && c.HierarchyCount == hc && now < c.Expires && AllAlive(c.Renderers))
                return c.Renderers;
            TmpRenderers.Clear();
            TmpSolid.Clear();
            TmpFlat.Clear();
            go.GetComponentsInChildren(TmpRenderers);
            foreach (Renderer r in TmpRenderers)
            {
                Type t = r.GetType();
                if (!SkippedRendererTypes.TryGetValue(t, out bool skip))
                {
                    string tn = t.Name;
                    SkippedRendererTypes[t] = skip = tn == "ParticleSystemRenderer" || tn == "TrailRenderer" || tn == "LineRenderer";
                }
                if (skip) continue;
                string n = r.name;
                if (n.EndsWith("_sh", StringComparison.Ordinal) || n.Contains("blackout") || n.Contains("LightFaker") || n.Contains("shadow")) continue;
                if (r.bounds.size.y < 0.25f) TmpFlat.Add(r);
                else TmpSolid.Add(r);
            }
            Renderer[] arr = (TmpSolid.Count > 0 ? TmpSolid : TmpFlat).ToArray();
            if (c == null) RendererCaches[id] = c = new RendererCache();
            c.Renderers = arr;
            c.HierarchyCount = hc;
            c.Expires = now + RendererCacheSeconds;
            return arr;
        }

        private static bool AllAlive(Renderer[] rs)
        {
            foreach (Renderer r in rs) if (r == null) return false;
            return true;
        }

        /// <summary>
        /// Usado só pelo cenário gk2hud-mundo-custo.cs: o caminho de reconstrução + escolha aplicado a uma lista arbitrária
        /// de Wgos (estresse: como se todos estivessem rebrotando).
        /// </summary>
        internal string StressMeasure(List<Wgo> wgos, Vector2 mouse)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var list = new List<WorldTarget>(wgos.Count);
            float w = Screen.width, h = Screen.height;
            int near = 0;
            foreach (Wgo g in wgos)
            {
                if (g == null || !NearScreen(g.transform.position, w, h)) continue;
                near++;
                list.Add(new WorldTarget { View = g, Data = g.Data, Renderers = RenderersCached(g.gameObject) });
            }
            double rebuild = sw.Elapsed.TotalMilliseconds;
            sw.Restart();
            int bubbles = 0;
            foreach (WorldTarget c in list)
            {
                c.UpdateBody();
                Rect b = c.BodyRect;
                if (mouse.x >= b.xMin - BubbleReachX && mouse.x <= b.xMax + BubbleReachX && mouse.y >= b.yMin && mouse.y <= b.yMax + BubbleReachUp) { c.AddBubble(); bubbles++; }
            }
            return $"{wgos.Count} Wgo ({near} perto da tela) · reconstrução {rebuild:0.00} ms · escolha {sw.Elapsed.TotalMilliseconds:0.00} ms ({bubbles} bolhas medidas)";
        }

        // ---------------- projeção ----------------

        /// <summary>Mundo → px de tela, sem o arredondamento de CameraSystem.WorldToScreenPoint (como o GK2Insight).</summary>
        public static Vector2 Project(Vector3 world)
        {
            CameraSystem cs = CameraSystem.Instance;
            Vector3 v = cs.WorldCamera.WorldToScreenPoint(world);
            MainCamera mc = cs.MainCamera;
            if (mc != null && mc.RenderTexture != null) v = mc.RenderTargetToScreen(v);
            return v;
        }

        /// <summary>Retângulo de tela dos renderers visíveis (8 cantos de cada bounds) e o centro visual.</summary>
        public static Rect ProjectRenderers(Renderer[] renderers, out Vector2 center)
        {
            center = default;
            if (renderers == null) return default;
            bool any = false;
            Bounds all = default;
            foreach (Renderer r in renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || !r.isVisible) continue;
                Bounds b = r.bounds;
                if (b.size.x > 30f || b.size.z > 30f) continue;   // volumes gigantes (sombras de cena, áreas) estragam o retângulo
                if (!any) { all = b; any = true; }
                else all.Encapsulate(b);
            }
            if (!any) return default;
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            Vector3 c = all.center, e = all.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
                Vector2 s = Project(p);
                min = Vector2.Min(min, s);
                max = Vector2.Max(max, s);
            }
            center = Project(c);
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>Retângulo de tela da bolha nativa sobre o objeto (UIObjectBubbleManager), só os gráficos ativos.</summary>
        public static bool NativeBubbleRect(WgoData data, out Rect rect)
        {
            rect = default;
            UIObjectBubbleManager mgr = UIObjectBubbleManager.Instance;
            if (mgr == null || data == null || !mgr.TryGetDisplayedBubble(data.UniqueId, out UIObjectBubble bubble) || bubble == null) return false;
            if (!bubble.gameObject.activeInHierarchy || bubble.IsOutOfScreen) return false;
            bool any = false;
            TmpGraphics.Clear();
            bubble.GetComponentsInChildren(TmpGraphics);
            foreach (Graphic g in TmpGraphics)
            {
                if (!g.isActiveAndEnabled || g.color.a <= 0.01f) continue;
                g.rectTransform.GetWorldCorners(Corners);
                Rect r = Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
                if (r.width <= 0f || r.height <= 0f) continue;
                rect = any ? Union(rect, r) : r;
                any = true;
            }
            TmpGraphics.Clear();
            return any;
        }

        public static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }
}
