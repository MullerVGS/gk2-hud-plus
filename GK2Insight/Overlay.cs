using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>
    /// Desenho técnico sobre o mundo (Shift+F7): colisores, rotas, dock points e hitboxes.
    /// Projeta com a câmera do mundo sem o arredondamento de CameraSystem.WorldToScreenPoint e desenha com GL em
    /// pixels de tela no OnGUI, por cima do blit e do pós-processamento.
    /// </summary>
    internal class Overlay
    {
        private const int Obstacles = 1 << 8, Interactable = 1 << 6, DockPoint = 1 << 7, PlayerLayer = 1 << 10,
            Step = 1 << 12, Drop = 1 << 16, Fighter = 1 << 29, InvisibleWalls = 1 << 14, BuffArea = 1 << 28;
        private const int ScanMask = Obstacles | Interactable | DockPoint | PlayerLayer | Step | Drop | Fighter | InvisibleWalls | BuffArea;

        private static readonly AccessTools.FieldRef<MovementComponent, List<MovementData>> MovementDataRef =
            AccessTools.FieldRefAccess<MovementComponent, List<MovementData>>("movementData");
        private static readonly AccessTools.FieldRef<MovementComponent, int> PathIndexRef =
            AccessTools.FieldRefAccess<MovementComponent, int>("currentPathTargetIndex");
        private static readonly AccessTools.FieldRef<MovementComponent, int> DataIndexRef =
            AccessTools.FieldRefAccess<MovementComponent, int>("currentMovementDataIndex");

        public static readonly Color ObstacleColor = new Color(1f, 0.35f, 0.3f, 0.9f);
        public static readonly Color InteractColor = new Color(0.3f, 0.85f, 1f, 0.9f);
        public static readonly Color PlayerColor = new Color(0.4f, 1f, 0.4f, 0.95f);
        public static readonly Color OtherColor = new Color(0.75f, 0.75f, 0.75f, 0.6f);
        public static readonly Color PathColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        public static readonly Color DockColor = new Color(1f, 0.55f, 1f, 0.95f);
        public static readonly Color HurtColor = new Color(1f, 0.6f, 0.1f, 0.9f);
        public static readonly Color HitColor = new Color(1f, 0.1f, 0.1f, 1f);

        private readonly Collider[] buffer = new Collider[1024];
        private readonly List<Collider> colliders = new List<Collider>();
        private readonly List<DockPoint> docks = new List<DockPoint>();
        private readonly List<Collider> hurtboxes = new List<Collider>();
        private readonly List<Collider> hitboxes = new List<Collider>();
        private readonly List<Vector3> lineBuffer = new List<Vector3>(4096);
        private readonly List<Color> colorBuffer = new List<Color>(2048);
        private float nextScan;
        private Material material;
        private Camera cam;
        private MainCamera mainCamera;

        public float Radius = 9f;
        public bool ShowColliders = true, ShowPaths = true, ShowDocks = true, ShowHitboxes = true;

        public int ColliderCount => colliders.Count;
        public int DockCount => docks.Count;
        public int MovingCount { get; private set; }
        public int HurtCount => hurtboxes.Count;
        public int ActiveHitCount { get; private set; }

        public void Refresh()
        {
            if (Time.unscaledTime < nextScan || MainGame.PlayerController == null) return;
            nextScan = Time.unscaledTime + 0.3f;
            Vector3 center = MainGame.PlayerController.transform.position;
            colliders.Clear();
            docks.Clear();
            int n = Physics.OverlapSphereNonAlloc(center, Radius, buffer, ScanMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = buffer[i];
                if (c == null) continue;
                if (c.gameObject.layer == 7)
                {
                    DockPoint dp = c.GetComponent<DockPoint>();
                    if (dp != null) docks.Add(dp);
                    continue;
                }
                colliders.Add(c);
            }
            if (ShowHitboxes)
            {
                hurtboxes.Clear();
                hitboxes.Clear();
                foreach (HitZone hz in UnityEngine.Object.FindObjectsByType<HitZone>(FindObjectsSortMode.None))
                {
                    Collider c = hz.GetComponent<Collider>();
                    if (c != null && (c.bounds.center - center).sqrMagnitude < Radius * Radius * 4f) hurtboxes.Add(c);
                }
                foreach (SwordHitBox hb in UnityEngine.Object.FindObjectsByType<SwordHitBox>(FindObjectsSortMode.None))
                {
                    Collider c = hb.GetComponent<Collider>();
                    if (c != null) hitboxes.Add(c);
                }
            }
        }

        /// <summary>Chamar no OnGUI.</summary>
        public void Draw()
        {
            if (Event.current.type != EventType.Repaint) return;
            CameraSystem system = CameraSystem.Instance;
            if (system == null || system.WorldCamera == null) return;
            cam = system.WorldCamera;
            mainCamera = system.MainCamera;
            lineBuffer.Clear();
            colorBuffer.Clear();

            if (ShowColliders)
            {
                foreach (Collider c in colliders)
                {
                    if (c == null || !c.enabled) continue;
                    Color color = ColorFor(c);
                    if (c.isTrigger) color.a *= 0.55f;
                    Vector3 ext = c.bounds.size;
                    if (Mathf.Max(ext.x, ext.z) > Radius * 1.5f) color.a *= 0.3f; // paredes e volumes gigantes: só contorno fraco
                    int layer = c.gameObject.layer;
                    AddCollider(c, color, footprint: layer == 8 || layer == 14);
                }
            }
            if (ShowHitboxes)
            {
                ActiveHitCount = 0;
                foreach (Collider c in hurtboxes)
                    if (c != null && c.enabled && c.gameObject.activeInHierarchy) AddCollider(c, HurtColor);
                foreach (Collider c in hitboxes)
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    ActiveHitCount++;
                    AddCollider(c, HitColor);
                }
            }
            if (ShowDocks)
            {
                foreach (DockPoint dp in docks)
                {
                    if (dp == null) continue;
                    Vector3 p = dp.transform.position;
                    AddCross(p, 0.12f, DockColor);
                    AddLine(p, p + dp.transform.forward * 0.35f, DockColor);
                }
            }
            if (ShowPaths) AddPaths();
            Flush();
        }

        private void AddPaths()
        {
            MovementSystemData data = MainGame.Instance?.GameSave?.movementSystemData;
            MovingCount = 0;
            if (data == null) return;
            foreach (MovementComponent mc in data.movingObjects)
            {
                if (mc == null) continue;
                List<MovementData> list = MovementDataRef(mc);
                if (list == null || list.Count == 0) continue;
                MovingCount++;
                int current = Mathf.Clamp(DataIndexRef(mc), 0, list.Count - 1);
                int target = PathIndexRef(mc);
                List<Vector3> path = list[current].worldPath;
                if (path == null || path.Count < 2) continue;
                Color done = new Color(PathColor.r, PathColor.g, PathColor.b, 0.25f);
                for (int i = 1; i < path.Count; i++)
                    AddLine(path[i - 1], path[i], i <= target ? done : PathColor);
                AddCross(path[path.Count - 1], 0.15f, PathColor);
            }
        }

        private static Color ColorFor(Collider c)
        {
            switch (c.gameObject.layer)
            {
                case 8:
                case 14: return ObstacleColor;
                case 6:
                case 16: return InteractColor;
                case 10: return PlayerColor;
                default: return OtherColor;
            }
        }

        /// <summary>footprint: só a face de baixo (o que bloqueia no chão), para obstáculos altos não poluírem a tela.</summary>
        private void AddCollider(Collider c, Color color, bool footprint = false)
        {
            switch (c)
            {
                case BoxCollider box:
                {
                    Transform t = box.transform;
                    Vector3 h = box.size * 0.5f;
                    Vector3[] k = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                        k[i] = t.TransformPoint(box.center + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
                    if (footprint) AddFootprint(k, color);
                    else AddBox(k, color);
                    break;
                }
                case SphereCollider sphere:
                {
                    Vector3 center = sphere.transform.TransformPoint(sphere.center);
                    float r = sphere.radius * MaxAbs(sphere.transform.lossyScale);
                    AddCircle(center, r, Vector3.right, Vector3.forward, color);
                    AddCircle(center, r, Vector3.right, Vector3.up, color);
                    break;
                }
                case CapsuleCollider capsule:
                {
                    Vector3 center = capsule.transform.TransformPoint(capsule.center);
                    float r = capsule.radius * MaxAbs(capsule.transform.lossyScale);
                    Vector3 axis = capsule.direction == 0 ? capsule.transform.right : capsule.direction == 1 ? capsule.transform.up : capsule.transform.forward;
                    float half = Mathf.Max(0f, capsule.height * 0.5f * MaxAbs(capsule.transform.lossyScale) - r);
                    Vector3 a = center + axis * half, b = center - axis * half;
                    AddCircle(a, r, Vector3.right, Vector3.forward, color);
                    AddCircle(b, r, Vector3.right, Vector3.forward, color);
                    AddLine(a + Vector3.right * r, b + Vector3.right * r, color);
                    AddLine(a - Vector3.right * r, b - Vector3.right * r, color);
                    break;
                }
                default:
                {
                    Bounds bb = c.bounds;
                    Vector3[] k = new Vector3[8];
                    for (int i = 0; i < 8; i++)
                        k[i] = bb.center + new Vector3((i & 1) == 0 ? -bb.extents.x : bb.extents.x, (i & 2) == 0 ? -bb.extents.y : bb.extents.y, (i & 4) == 0 ? -bb.extents.z : bb.extents.z);
                    color.a *= 0.7f;
                    if (footprint) AddFootprint(k, color);
                    else AddBox(k, color);
                    break;
                }
            }
        }

        private void AddBox(Vector3[] k, Color color)
        {
            int[] e = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 2, 1, 3, 4, 6, 5, 7, 0, 4, 1, 5, 2, 6, 3, 7 };
            for (int i = 0; i < e.Length; i += 2) AddLine(k[e[i]], k[e[i + 1]], color);
        }

        /// <summary>Face de baixo da caixa: os 4 cantos com menor Y no mundo.</summary>
        private void AddFootprint(Vector3[] k, Color color)
        {
            Vector3[] sorted = (Vector3[])k.Clone();
            System.Array.Sort(sorted, (a, b) => a.y.CompareTo(b.y));
            Vector3[] f = { sorted[0], sorted[1], sorted[2], sorted[3] };
            Vector3 c = (f[0] + f[1] + f[2] + f[3]) * 0.25f;
            System.Array.Sort(f, (a, b) => Mathf.Atan2(a.z - c.z, a.x - c.x).CompareTo(Mathf.Atan2(b.z - c.z, b.x - c.x)));
            for (int i = 0; i < 4; i++) AddLine(f[i], f[(i + 1) % 4], color);
        }

        private void AddCircle(Vector3 c, float r, Vector3 u, Vector3 v, Color color)
        {
            const int seg = 20;
            Vector3 prev = c + u * r;
            for (int i = 1; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 next = c + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * r;
                AddLine(prev, next, color);
                prev = next;
            }
        }

        private void AddCross(Vector3 p, float s, Color color)
        {
            AddLine(p - Vector3.right * s, p + Vector3.right * s, color);
            AddLine(p - Vector3.forward * s, p + Vector3.forward * s, color);
        }

        private void AddLine(Vector3 a, Vector3 b, Color color)
        {
            Vector3 sa = Project(a), sb = Project(b);
            if (sa.z < 0f || sb.z < 0f) return;
            lineBuffer.Add(sa);
            lineBuffer.Add(sb);
            colorBuffer.Add(color);
        }

        private Vector3 Project(Vector3 world)
        {
            Vector3 v = cam.WorldToScreenPoint(world);
            return mainCamera != null && mainCamera.RenderTexture != null ? mainCamera.RenderTargetToScreen(v) : v;
        }

        private void Flush()
        {
            if (lineBuffer.Count == 0) return;
            if (material == null)
            {
                Shader shader = Shader.Find("Hidden/Internal-Colored");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                material.SetInt("_ZWrite", 0);
                material.SetInt("_Cull", 0);
                material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }
            material.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix();
            GL.Begin(GL.LINES);
            for (int i = 0; i < colorBuffer.Count; i++)
            {
                GL.Color(colorBuffer[i]);
                Vector3 a = lineBuffer[i * 2], b = lineBuffer[i * 2 + 1];
                GL.Vertex3(a.x, a.y, 0f);
                GL.Vertex3(b.x, b.y, 0f);
            }
            GL.End();
            GL.PopMatrix();
        }

        public void Dispose()
        {
            if (material != null) UnityEngine.Object.Destroy(material);
        }

        private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
    }
}
