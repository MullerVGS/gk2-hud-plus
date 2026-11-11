using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>Um lugar que a vista mostra: um interior (área interna do jogo) ou o mundo aberto já explorado.</summary>
    internal class Place
    {
        public bool Outdoor;
        public string DoorId;
        public string ZoneId;
        public string Title;
        /// <summary>Preset de luz e som do lugar ("outdoor", "indoor", "church", "dungeons").</summary>
        public string Light;
        /// <summary>Posição da setinha na arte do mapa (unidades do canvas, a partir do centro do mapa).</summary>
        public Vector2 MapPos;
        /// <summary>Ponto do chão que a vista mostra ao abrir.</summary>
        public Vector3 Center;
        /// <summary>Interior: zonas já visitadas da mesma área. Mundo aberto: regiões do mapa já conhecidas.</summary>
        public List<Bounds> Boxes = new List<Bounds>();
        public IndoorAreaData Area;
    }

    /// <summary>Porta, escada ou alçapão: um objeto que teleporta para outro (<c>WGODef.teleportDestinationWgoIds</c>).</summary>
    internal class Door
    {
        public WgoData Data;
        public WgoData Dest;
        public string Light;
    }

    /// <summary>
    /// O que a vista pode mostrar neste save, montado a cada redesenho do mapa. Interiores: áreas internas do
    /// <c>TeleportPointGraph</c>, só zonas em <c>KnowledgeSystem.visitedWorldZones</c>. Mundo aberto: só dentro das
    /// regiões do mapa em <c>knownMapZones</c> (as mesmas caixas que prendem o mapa). Portas ligam os dois.
    /// </summary>
    internal class Atlas
    {
        private const float MatchRadius = 30f;
        private const float DoorTolerance = 6f;
        private static readonly FieldInfo ExpressionText = AccessTools.Field(typeof(LazyExpression), "expressionString");
        private static readonly AccessTools.FieldRef<TeleportPointGraph, List<IndoorAreaData>> AreasRef =
            AccessTools.FieldRefAccess<TeleportPointGraph, List<IndoorAreaData>>("indoorAreas");

        public readonly List<Place> ArrowPlaces = new List<Place>();
        public readonly List<Door> Doors = new List<Door>();
        public readonly List<Bounds> KnownWorld = new List<Bounds>();
        /// <summary>Lugares de construção da cidade (placas de reparo): objetos onde alguma <c>TownBuildingDef</c> pode ser erguida.</summary>
        public readonly List<WgoData> Sites = new List<WgoData>();
        public WorldZone[] Zones = new WorldZone[0];

        private List<IndoorAreaData> areas;
        private List<string> visited;
        private Vector2 mapSize;
        private Vector3 worldMin, worldMax;

        public static Atlas Build(RectTransform map, List<Vector2> arrows, List<Rect> knownMapRects)
        {
            var a = new Atlas();
            TeleportPointGraph graph = TeleportPointGraph.Instance;
            GUIElements gui = GUIElements.Instance;
            if (graph == null || gui == null || MainGame.Instance == null || MainGame.Instance.GameSave == null) return a;
            a.areas = AreasRef(graph);
            a.visited = MainGame.Instance.GameSave.knowledgeSystem.visitedWorldZones;
            a.Zones = Object.FindObjectsByType<WorldZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            a.mapSize = map.sizeDelta;
            a.worldMin = gui.WorldMin.position;
            a.worldMax = gui.WorldMax.position;
            if (knownMapRects != null)
                foreach (Rect r in knownMapRects)
                {
                    Vector3 lo = a.MapToWorld(r.min), hi = a.MapToWorld(r.max);
                    var b = new Bounds();
                    b.SetMinMax(new Vector3(Mathf.Min(lo.x, hi.x), -100f, Mathf.Min(lo.z, hi.z)), new Vector3(Mathf.Max(lo.x, hi.x), 100f, Mathf.Max(lo.z, hi.z)));
                    a.KnownWorld.Add(b);
                }

            foreach (WGODef def in GameBalance.Me.GetDataCollection<WGODef>())
            {
                if (def == null || def.teleportDestinationWgoIds == null || def.teleportDestinationWgoIds.Count == 0) continue;
                WgoData door = MainGame.WorldData.GetWgoData(def.id);
                if (door == null || door.IsHidden) continue;
                foreach (string destId in def.teleportDestinationWgoIds)
                {
                    WgoData dest = string.IsNullOrEmpty(destId) || destId == def.id ? null : MainGame.WorldData.GetWgoData(destId);
                    if (dest == null) continue;
                    a.Doors.Add(new Door { Data = door, Dest = dest, Light = LightOf(def, destId, a.AreaOf(dest.Position) != null) });
                    break;
                }
            }

            var siteIds = new HashSet<string>();
            foreach (TownBuildingDef def in GameBalance.Me.townBuildingDefs)
                foreach (string id in def.craftsIn)
                {
                    if (string.IsNullOrEmpty(id) || !siteIds.Add(id)) continue;
                    WgoData site = MainGame.WorldData.GetWgoData(id);
                    if (site != null && !site.IsHidden && site.TownBuildingWgoComponent != null) a.Sites.Add(site);
                }

            // Cada setinha do mapa fica com a porta de rua mais próxima que leva a um interior já visitado.
            if (arrows != null)
            {
                var used = new HashSet<Door>();
                foreach (Vector2 arrow in arrows)
                {
                    Door best = null;
                    float bestDist = MatchRadius;
                    foreach (Door d in a.Doors)
                    {
                        if (used.Contains(d) || a.AreaOf(d.Data.Position) != null || a.AreaOf(d.Dest.Position) == null) continue;
                        if (!a.WorldToMap(d.Data.Position, out Vector2 mapPos)) continue;
                        float dist = Vector2.Distance(mapPos, arrow);
                        if (dist < bestDist) { best = d; bestDist = dist; }
                    }
                    if (best == null) continue;
                    used.Add(best);
                    Place place = a.Through(best);
                    if (place == null) continue;
                    place.MapPos = arrow;
                    a.ArrowPlaces.Add(place);
                }
            }
            return a;
        }

        /// <summary>O lugar do outro lado da porta, ou null se o jogador ainda não o conhece.</summary>
        public Place Through(Door door)
        {
            Vector3 p = door.Dest.Position;
            IndoorAreaData area = AreaOf(p);
            // As áreas de nuvens das regiões têm frestas entre si; uma porta de rua logo na borda ainda vale.
            if (area == null) return OutdoorAt(p, DoorTolerance);
            WorldZone zone = ZoneAt(p);
            if (zone == null || !visited.Contains(zone.Id)) return null;
            var place = new Place { DoorId = door.Data.id, ZoneId = zone.Id, Title = LLBase.L("wz_" + zone.Id), Light = door.Light, Area = area };
            Bounds zb = zone.ZoneCollider.bounds;
            place.Center = new Vector3(zb.center.x, p.y, zb.center.z);
            place.Boxes.Add(zb);
            foreach (WorldZone other in Zones)
            {
                if (other == zone || other.ZoneCollider == null || !visited.Contains(other.Id)) continue;
                Bounds ob = other.ZoneCollider.bounds;
                if (ob.size.x < 1f || ob.size.z < 1f || !area.ContainsXZ(ob.center)) continue;
                place.Boxes.Add(ob);
            }
            return place;
        }

        /// <summary>O mundo aberto em volta de um ponto, ou null fora das regiões conhecidas do mapa.</summary>
        public Place OutdoorAt(Vector3 p, float tolerance = 0f)
        {
            if (KnownWorld.Count == 0 || !InKnownWorld(p, tolerance)) return null;
            return new Place { Outdoor = true, Light = "outdoor", Center = p, Boxes = KnownWorld, Title = string.Empty };
        }

        public Place OutdoorAtMap(Vector2 mapPos)
        {
            Vector3 p = MapToWorld(mapPos);
            p.y = GroundY(p);
            return OutdoorAt(p);
        }

        public bool InKnownWorld(Vector3 p, float tolerance = 0f)
        {
            foreach (Bounds b in KnownWorld)
                if (p.x >= b.min.x - tolerance && p.x <= b.max.x + tolerance && p.z >= b.min.z - tolerance && p.z <= b.max.z + tolerance) return true;
            return false;
        }

        /// <summary>Zona visitada sob o ponto, para o título da vista; null se não há ou o jogador não a conhece.</summary>
        public WorldZone TitleZoneAt(Vector3 p)
        {
            WorldZone zone = ZoneAt(p);
            return zone != null && visited.Contains(zone.Id) ? zone : null;
        }

        /// <summary>Zonas visitadas cujo colisor toca o retângulo (XZ) dado: de onde saem os objetos inspecionáveis.</summary>
        public void VisitedZonesIn(Vector3 center, float halfX, float halfZ, List<WorldZone> into)
        {
            into.Clear();
            foreach (WorldZone z in Zones)
            {
                if (z == null || z.ZoneCollider == null || !visited.Contains(z.Id)) continue;
                Bounds b = z.ZoneCollider.bounds;
                if (b.size.x < 1f || b.size.z < 1f) continue;
                if (Mathf.Abs(center.x - b.center.x) > b.extents.x + halfX || Mathf.Abs(center.z - b.center.z) > b.extents.z + halfZ) continue;
                into.Add(z);
            }
        }

        /// <summary>Altura do chão: a da zona com colisor mais próxima no mundo aberto (o mapa não guarda altura).</summary>
        private float GroundY(Vector3 p)
        {
            float best = float.MaxValue, y = 0f;
            foreach (WorldZone z in Zones)
            {
                if (z == null || z.ZoneCollider == null) continue;
                Bounds b = z.ZoneCollider.bounds;
                if (b.size.x < 1f || b.size.z < 1f || AreaOf(b.center) != null) continue;
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x), dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);
                float d = dx * dx + dz * dz;
                if (d < best) { best = d; y = z.GroundPlaneY; }
            }
            return y;
        }

        /// <summary>
        /// A porta teleporta por expressão (<c>TeleportTo("destino", "preset", ...)</c>); o preset é o segundo
        /// argumento. Porta que teleporta por grafo usa "indoor" para interior e "outdoor" para a rua.
        /// </summary>
        private static string LightOf(WGODef def, string destId, bool destIndoor)
        {
            foreach (CustomInteraction ci in new[] { def.customInteraction, def.customInteraction2 })
            {
                if (ci == null || ci.execution == null) continue;
                foreach (LazyExpression e in ci.execution)
                {
                    string text = e == null ? null : ExpressionText.GetValue(e) as string;
                    if (string.IsNullOrEmpty(text)) continue;
                    Match m = Regex.Match(text, "TeleportTo\\(\\s*[\"']?" + Regex.Escape(destId) + "[\"']?\\s*,\\s*[\"']?(\\w+)");
                    if (m.Success) return m.Groups[1].Value;
                }
            }
            return destIndoor ? "indoor" : "outdoor";
        }

        public IndoorAreaData AreaOf(Vector3 pos)
        {
            if (areas == null) return null;
            foreach (IndoorAreaData a in areas)
                if (a != null && a.ContainsXZ(pos)) return a;
            return null;
        }

        /// <summary>Menor zona com colisor que contém o ponto (em XZ).</summary>
        public WorldZone ZoneAt(Vector3 pos)
        {
            WorldZone best = null;
            float bestArea = float.MaxValue;
            foreach (WorldZone z in Zones)
            {
                if (z == null || z.ZoneCollider == null) continue;
                Bounds b = z.ZoneCollider.bounds;
                if (b.size.x < 1f || b.size.z < 1f) continue;
                if (Mathf.Abs(pos.x - b.center.x) > b.extents.x + 1f || Mathf.Abs(pos.z - b.center.z) > b.extents.z + 1f) continue;
                float area = b.size.x * b.size.z;
                if (area < bestArea) { best = z; bestArea = area; }
            }
            return best;
        }

        /// <summary>Mundo → mapa, a mesma conta de <c>MapPageWidget.UpdateMilestones</c>; false fora dos limites do mapa.</summary>
        public bool WorldToMap(Vector3 p, out Vector2 mapPos)
        {
            Vector3 min = worldMin, max = worldMax;
            mapPos = Vector2.zero;
            if (p.x < Mathf.Min(min.x, max.x) || p.x > Mathf.Max(min.x, max.x) || p.z < Mathf.Min(min.z, max.z) || p.z > Mathf.Max(min.z, max.z)) return false;
            float u = Mathf.InverseLerp(min.x, max.x, p.x);
            float v = Mathf.InverseLerp(min.z, max.z, p.z);
            mapPos = new Vector2((u - 0.5f) * mapSize.x, (v - 0.5f) * mapSize.y);
            return true;
        }

        public Vector3 MapToWorld(Vector2 mapPos)
        {
            float u = mapPos.x / mapSize.x + 0.5f, v = mapPos.y / mapSize.y + 0.5f;
            return new Vector3(Mathf.LerpUnclamped(worldMin.x, worldMax.x, u), 0f, Mathf.LerpUnclamped(worldMin.z, worldMax.z, v));
        }
    }

    /// <summary>As setinhas amarelas pintadas na arte do mapa, achadas pela cor na própria textura.</summary>
    internal static class Places
    {
        private static readonly Color32 ArrowLight = new Color32(254, 196, 111, 255);
        private static readonly Color32 ArrowDark = new Color32(253, 150, 33, 255);

        /// <summary>
        /// Acha as setinhas na textura do mapa: grupos de 6–7×4 pixels com as duas cores da seta. Devolve posições em
        /// unidades do canvas a partir do centro do mapa. A textura não é legível, então passa por um RenderTexture.
        /// </summary>
        public static List<Vector2> FindArrows(Image mapImage, Vector2 mapSize)
        {
            var result = new List<Vector2>();
            Sprite sprite = mapImage != null ? mapImage.sprite : null;
            if (sprite == null || sprite.texture == null) return null;
            Texture tex = sprite.texture;
            int w = tex.width, h = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            Object.Destroy(copy);

            var seen = new bool[px.Length];
            var stack = new Stack<int>();
            Rect r = sprite.rect;
            for (int i = 0; i < px.Length; i++)
            {
                if (seen[i] || !IsArrow(px[i])) continue;
                int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, count = 0;
                seen[i] = true;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int cur = stack.Pop();
                    int cx = cur % w, cy = cur / w;
                    count++;
                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int n = ny * w + nx;
                            if (seen[n] || !IsArrow(px[n])) continue;
                            seen[n] = true;
                            stack.Push(n);
                        }
                }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                if (bw < 6 || bw > 7 || bh != 4 || count < 9 || count > 12) continue;
                float ax = ((minX + maxX + 1) * 0.5f - r.x) / r.width;
                float ay = ((minY + maxY + 1) * 0.5f - r.y) / r.height;
                result.Add(new Vector2((ax - 0.5f) * mapSize.x, (ay - 0.5f) * mapSize.y));
            }
            return result;
        }

        private static bool IsArrow(Color32 c)
        {
            return Near(c, ArrowLight) || Near(c, ArrowDark);
        }

        private static bool Near(Color32 a, Color32 b)
        {
            return Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) <= 6;
        }
    }
}
