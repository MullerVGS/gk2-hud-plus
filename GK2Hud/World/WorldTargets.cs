using System.Collections.Generic;
using UnityEngine;

namespace GK2Hud.World
{
    /// <summary>
    /// Um objeto do mundo com algo em andamento: o Wgo (estação, canteiro...) ou um item no chão (corpo apodrecendo).
    /// Guarda os renderers para projetar o retângulo de tela a cada frame enquanto é o alvo.
    /// </summary>
    internal sealed class WorldTarget
    {
        public Wgo View;
        public WgoData Data;
        public DropData Drop;
        public Component DropView;   // DropView do item no chão
        public Renderer[] Renderers;
        public Rect Rect;            // px de tela: objeto ∪ bolha nativa
        public Rect BodyRect;        // px de tela: só o objeto
        public Vector2 Center;       // px de tela: centro visual do objeto
        public bool RayHit;          // atingido pelo raycast físico do mouse nesta escolha

        public bool Alive => Drop != null ? DropView != null : View != null && View.isActiveAndEnabled && View.HasData && View.Data == Data;

        public static bool Same(WorldTarget a, WorldTarget b)
        {
            if (a == null || b == null) return a == b;
            return a.Drop != null ? a.Drop == b.Drop : a.Data != null && a.Data == b.Data;
        }

        public void UpdateRect()
        {
            BodyRect = WorldTargets.ProjectRenderers(Renderers, out Center);
            if (BodyRect.width <= 0f)
            {
                Vector3 p = Drop != null ? DropView.transform.position : View.transform.position;
                Vector2 s = WorldTargets.Project(p);
                BodyRect = new Rect(s.x - 24f, s.y, 48f, 48f);
                Center = BodyRect.center;
            }
            Rect = BodyRect;
            if (View != null && WorldTargets.NativeBubbleRect(Data, out Rect b)) Rect = WorldTargets.Union(Rect, b);
        }

        public string Describe()
        {
            string id = Drop != null ? "drop:" + Drop.Id : Data?.id;
            return id + " rect=" + Rect.ToString("0") + " center=" + Center.ToString("0") + (RayHit ? " ray" : "");
        }
    }

    /// <summary>
    /// Candidatos (objetos com algo em andamento na cena, refeitos a cada 1 s) e escolha do alvo sob o mouse: entre os
    /// que contêm o cursor no retângulo (objeto ∪ bolha nativa) ou foram atingidos pelo raycast físico (layers 6 e 16),
    /// vence o de centro mais próximo do cursor. O raycast sozinho erra (o vizinho da frente rouba), o retângulo
    /// sozinho também (renderers grandes): a combinação resolve os dois.
    /// </summary>
    internal sealed class WorldTargets
    {
        private const float RebuildInterval = 1f;
        private const int RayMask = (1 << 6) | (1 << 16);
        private const float RayBonus = 24f;   // px: o objeto que o raio físico atinge ganha desempate

        private readonly List<WorldTarget> candidates = new List<WorldTarget>();
        private readonly Dictionary<WgoData, WorldTarget> byData = new Dictionary<WgoData, WorldTarget>();
        private readonly Dictionary<DropData, WorldTarget> byDrop = new Dictionary<DropData, WorldTarget>();
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
                c.UpdateRect();
                if (c.Rect.xMax < 0f || c.Rect.xMin > sw || c.Rect.yMax < 0f || c.Rect.yMin > sh) continue;
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
            var keepData = new Dictionary<WgoData, WorldTarget>();
            var keepDrop = new Dictionary<DropData, WorldTarget>();
            candidates.Clear();
            foreach (KeyValuePair<WgoData, Wgo> kv in WorldProcesses.WgosWithProcess())
            {
                if (!byData.TryGetValue(kv.Key, out WorldTarget t) || t.View != kv.Value)
                    t = new WorldTarget { View = kv.Value, Data = kv.Key };
                t.Renderers = RenderersOf(kv.Value.gameObject);   // partes do objeto mudam (estágios do canteiro, AddWgoPart)
                keepData[kv.Key] = t;
                candidates.Add(t);
            }
            foreach (KeyValuePair<DropData, Component> kv in WorldProcesses.DropsWithProcess())
            {
                if (!byDrop.TryGetValue(kv.Key, out WorldTarget t) || t.DropView != kv.Value)
                    t = new WorldTarget { Drop = kv.Key, DropView = kv.Value };
                t.Renderers = RenderersOf(kv.Value.gameObject);
                keepDrop[kv.Key] = t;
                candidates.Add(t);
            }
            byData.Clear();
            foreach (var kv in keepData) byData[kv.Key] = kv.Value;
            byDrop.Clear();
            foreach (var kv in keepDrop) byDrop[kv.Key] = kv.Value;
        }

        private static Renderer[] RenderersOf(GameObject go)
        {
            // Sombras (sprites "_sh" deitados no chão), máscaras "blackout", luzes falsas e partículas aumentam muito o
            // retângulo sem ser o objeto: ficam de fora. Objeto todo chato (canteiro, corpo): usa o que houver.
            var solid = new List<Renderer>();
            var flat = new List<Renderer>();
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                string tn = r.GetType().Name;
                if (tn == "ParticleSystemRenderer" || tn == "TrailRenderer" || tn == "LineRenderer") continue;
                string n = r.name;
                if (n.EndsWith("_sh") || n.Contains("blackout") || n.Contains("LightFaker") || n.Contains("shadow")) continue;
                if (r.bounds.size.y < 0.25f) flat.Add(r);
                else solid.Add(r);
            }
            return (solid.Count > 0 ? solid : flat).ToArray();
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
            foreach (UnityEngine.UI.Graphic g in bubble.GetComponentsInChildren<UnityEngine.UI.Graphic>())
            {
                if (!g.isActiveAndEnabled || g.color.a <= 0.01f) continue;
                g.rectTransform.GetWorldCorners(Corners);
                Rect r = Rect.MinMaxRect(Corners[0].x, Corners[0].y, Corners[2].x, Corners[2].y);
                if (r.width <= 0f || r.height <= 0f) continue;
                rect = any ? Union(rect, r) : r;
                any = true;
            }
            return any;
        }

        public static Rect Union(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }
}
