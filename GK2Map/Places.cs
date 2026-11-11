using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>Um interior alcançável por uma setinha amarela do mapa.</summary>
    internal class Place
    {
        public string DoorId;
        public string ZoneId;
        public string Title;
        /// <summary>Preset de luz e som que a porta aplica ao entrar ("indoor", "church", "dungeons").</summary>
        public string Light;
        /// <summary>Posição da setinha na arte do mapa (unidades do canvas, a partir do centro do mapa).</summary>
        public Vector2 MapPos;
        /// <summary>Ponto do chão que a vista mostra ao abrir.</summary>
        public Vector3 Center;
        /// <summary>Caixas (zonas já visitadas do mesmo interior) dentro das quais a vista pode andar.</summary>
        public readonly List<Bounds> Boxes = new List<Bounds>();
        /// <summary>As zonas dessas caixas, na mesma ordem.</summary>
        public readonly List<WorldZone> Zones = new List<WorldZone>();
    }

    /// <summary>
    /// Liga as setinhas amarelas pintadas na arte do mapa aos interiores do jogo. As setinhas saem da própria textura
    /// (pela cor); as portas, do balanço (<c>WGODef.teleportDestinationWgoIds</c>) e das áreas internas do
    /// <c>TeleportPointGraph</c>. Só entra interior cuja zona o jogador já visitou
    /// (<c>KnowledgeSystem.visitedWorldZones</c>, o mesmo critério dos rótulos nativos do mapa).
    /// </summary>
    internal static class Places
    {
        private const float MatchRadius = 30f;
        private static readonly Color32 ArrowLight = new Color32(254, 196, 111, 255);
        private static readonly Color32 ArrowDark = new Color32(253, 150, 33, 255);
        private static readonly FieldInfo ExpressionText = AccessTools.Field(typeof(LazyExpression), "expressionString");
        private static readonly AccessTools.FieldRef<TeleportPointGraph, List<IndoorAreaData>> AreasRef =
            AccessTools.FieldRefAccess<TeleportPointGraph, List<IndoorAreaData>>("indoorAreas");

        public static List<Place> Build(RectTransform map, List<Vector2> arrows)
        {
            var places = new List<Place>();
            TeleportPointGraph graph = TeleportPointGraph.Instance;
            GUIElements gui = GUIElements.Instance;
            if (graph == null || gui == null || MainGame.Instance == null || MainGame.Instance.GameSave == null || arrows == null) return places;
            List<IndoorAreaData> areas = AreasRef(graph);
            List<string> visited = MainGame.Instance.GameSave.knowledgeSystem.visitedWorldZones;
            WorldZone[] zones = Object.FindObjectsByType<WorldZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Vector2 size = map.sizeDelta;

            var doors = new List<Place>();
            foreach (WGODef def in GameBalance.Me.GetDataCollection<WGODef>())
            {
                if (def == null || def.teleportDestinationWgoIds == null || def.teleportDestinationWgoIds.Count == 0) continue;
                WgoData door = MainGame.WorldData.GetWgoData(def.id);
                if (door == null || door.IsHidden || AreaOf(areas, door.Position) != null) continue;
                if (!TryMapPos(gui, size, door.Position, out Vector2 mapPos)) continue;
                foreach (string destId in def.teleportDestinationWgoIds)
                {
                    WgoData dest = string.IsNullOrEmpty(destId) ? null : MainGame.WorldData.GetWgoData(destId);
                    IndoorAreaData area = dest == null ? null : AreaOf(areas, dest.Position);
                    if (area == null) continue;
                    WorldZone zone = ZoneAt(zones, dest.Position);
                    if (zone == null || !visited.Contains(zone.Id)) continue;
                    var place = new Place { DoorId = def.id, ZoneId = zone.Id, Title = LLBase.L("wz_" + zone.Id), MapPos = mapPos, Light = LightOf(def, destId) };
                    Bounds zb = zone.ZoneCollider.bounds;
                    place.Center = new Vector3(zb.center.x, dest.Position.y, zb.center.z);
                    place.Boxes.Add(zb);
                    place.Zones.Add(zone);
                    foreach (WorldZone other in zones)
                    {
                        if (other == zone || other.ZoneCollider == null || !visited.Contains(other.Id)) continue;
                        Bounds ob = other.ZoneCollider.bounds;
                        if (ob.size.x < 1f || ob.size.z < 1f || !area.ContainsXZ(ob.center)) continue;
                        place.Boxes.Add(ob);
                        place.Zones.Add(other);
                    }
                    doors.Add(place);
                    break;
                }
            }

            // Cada setinha fica com a porta mais próxima; a posição da vista no mapa passa a ser a da setinha pintada.
            var used = new HashSet<Place>();
            foreach (Vector2 arrow in arrows)
            {
                Place best = null;
                float bestDist = MatchRadius;
                foreach (Place d in doors)
                {
                    float dist = Vector2.Distance(d.MapPos, arrow);
                    if (dist < bestDist && !used.Contains(d)) { best = d; bestDist = dist; }
                }
                if (best == null) continue;
                used.Add(best);
                best.MapPos = arrow;
                places.Add(best);
            }
            return places;
        }

        /// <summary>
        /// A porta teleporta por expressão (<c>TeleportTo("destino", "preset", ...)</c>); o preset é o segundo
        /// argumento. Porta que teleporta por grafo cai no "indoor".
        /// </summary>
        private static string LightOf(WGODef def, string destId)
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
            return "indoor";
        }

        private static IndoorAreaData AreaOf(List<IndoorAreaData> areas, Vector3 pos)
        {
            if (areas == null) return null;
            foreach (IndoorAreaData a in areas)
                if (a != null && a.ContainsXZ(pos)) return a;
            return null;
        }

        /// <summary>Menor zona com colisor que contém o ponto (em XZ).</summary>
        private static WorldZone ZoneAt(WorldZone[] zones, Vector3 pos)
        {
            WorldZone best = null;
            float bestArea = float.MaxValue;
            foreach (WorldZone z in zones)
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

        /// <summary>Mundo → mapa, a mesma conta de <c>MapPageWidget.UpdateMilestones</c>.</summary>
        private static bool TryMapPos(GUIElements gui, Vector2 size, Vector3 p, out Vector2 mapPos)
        {
            Vector3 min = gui.WorldMin.position, max = gui.WorldMax.position;
            float tilt = Mathf.Deg2Rad * gui.WorldMin.rotation.x;
            float z = p.z + p.y * Mathf.Tan(tilt);
            mapPos = Vector2.zero;
            if (p.x < Mathf.Min(min.x, max.x) || p.x > Mathf.Max(min.x, max.x) || z < Mathf.Min(min.z, max.z) || z > Mathf.Max(min.z, max.z)) return false;
            float u = Mathf.InverseLerp(min.x, max.x, p.x);
            float v = Mathf.InverseLerp(min.z, max.z, z);
            mapPos = new Vector2((u - 0.5f) * size.x, (v - 0.5f) * size.y);
            return true;
        }

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
