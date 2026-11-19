using System;
using System.Collections.Generic;
using System.Reflection;
using GK2Hud;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace GK2Map
{
    /// <summary>
    /// Inspeção na vista do local, só leitura: parar o mouse num objeto mostra o nome no tooltip nativo; clicar fixa o
    /// tooltip com os detalhes (qualidade, o que está sendo feito, o que há dentro) até clicar em outro lugar.
    /// Nada aqui chama o jogo para interagir: lê <c>WgoData</c> e desenha no <c>UITooltip</c>.
    /// </summary>
    internal static class PlaceInspect
    {
        private const float RebuildSeconds = 0.5f;
        private const float PickSeconds = 0.1f;
        private const int MaxItems = 8;

        private class Target
        {
            public Wgo View;
            public WgoData Data;
            public string Name;
            public Renderer[] Renderers;
            /// <summary>Personagem (NPC, zumbi): os limites do esqueleto são bem maiores que o desenho.</summary>
            public bool Character;
            /// <summary>Porta ou escada: clicar atravessa em vez de fixar o tooltip.</summary>
            public Door Door;
            public Vector3 Anchor;
            public Rect Rect;
        }

        private static readonly FieldInfo TooltipInstance = AccessTools.Field(typeof(UITooltip), "instance");
        private static readonly Dictionary<string, FieldInfo> StyleFields = new Dictionary<string, FieldInfo>();
        private static readonly List<Target> Candidates = new List<Target>();
        private static readonly Vector3[] Corners = new Vector3[8];
        private static float nextRebuild;
        private static float nextPick;
        private static Target hover;
        private static Target pinned;
        private static RectTransform proxy;

        public static string Describe => (pinned != null ? "fixado " + pinned.Data.id : hover != null ? "hover " + hover.Data.id : "nada") + " candidatos " + Candidates.Count;

        /// <summary>Renderers do objeto sob o cursor (ou fixado), para o contorno.</summary>
        public static Renderer[] HoverRenderers
        {
            get
            {
                Target t = hover ?? pinned;
                return t != null && t.View != null && t.Renderers != null && t.Renderers.Length > 0 ? t.Renderers : null;
            }
        }

        /// <summary>Porta sob o cursor ou a mais próxima dele dentro de <paramref name="radius"/> pixels.</summary>
        public static Door DoorNear(Vector2 mouse, float radius)
        {
            Door best = null;
            float bestDist = radius;
            foreach (Target t in Candidates)
            {
                if (t.Door == null || !TryRect(t)) continue;
                float d = t.Rect.Contains(mouse) ? 0f : Vector2.Distance(t.Rect.center, mouse);
                if (d < bestDist) { best = t.Door; bestDist = d; }
            }
            return best;
        }

        /// <returns>A porta clicada, quando o clique foi numa porta; senão null.</returns>
        public static Door Tick(PlaceView view, Vector2 mouse, bool over, bool clicked)
        {
            float now = Time.unscaledTime;
            if (now >= nextRebuild)
            {
                nextRebuild = now + RebuildSeconds;
                Rebuild(view);
            }
            if (clicked || now >= nextPick)
            {
                nextPick = now + PickSeconds;
                Target at = over ? Pick(mouse) : null;
                if (clicked && at != null && at.Door != null) return at.Door;
                if (clicked)
                {
                    pinned = at;
                    hover = at;
                    if (at == null) UITooltip.Hide();
                    else Show(view, at, true);
                }
                else if (at != hover)
                {
                    hover = at;
                    if (pinned == null)
                    {
                        if (at == null) UITooltip.Hide();
                        else Show(view, at, false);
                    }
                }
            }
            Target shown = pinned ?? hover;
            if (shown != null)
            {
                if (shown.Door == null && (shown.View == null || !shown.View.gameObject.activeInHierarchy))
                {
                    pinned = hover = null;
                    UITooltip.Hide();
                }
                else if (TryRect(shown)) PlaceProxy(view, shown.Rect);
            }
            return null;
        }

        public static void Clear()
        {
            if (pinned != null || hover != null) UITooltip.Hide();
            pinned = hover = null;
            Candidates.Clear();
            Seen.Clear();
            nextRebuild = nextPick = 0f;
            if (proxy != null) UnityEngine.Object.Destroy(proxy.gameObject);
            proxy = null;
        }

        private static readonly List<WorldZone> TmpZones = new List<WorldZone>();

        /// <summary>
        /// O que dá para inspecionar agora: portas e escadas dentro da vista e os objetos com nome das zonas visitadas
        /// que a vista toca.
        /// </summary>
        private static void Rebuild(PlaceView view)
        {
            Target keepPinned = pinned, keepHover = hover;
            Candidates.Clear();
            Seen.Clear();
            Atlas atlas = view.Atlas;
            if (atlas == null) return;
            Vector3 focus = view.Focus;
            Vector2 half = view.ViewHalfExtents() + new Vector2(2f, 2f);
            bool outdoor = view.Place.Outdoor;

            foreach (Door door in atlas.Doors)
            {
                Vector3 p = door.Data.Position;
                if (Mathf.Abs(p.x - focus.x) > half.x || Mathf.Abs(p.z - focus.z) > half.y) continue;
                if ((atlas.AreaOf(p) == null) != outdoor) continue;
                Wgo w = GameScene.GetWgoViewGlobal(door.Data.UniqueId);
                if (w != null && !w.gameObject.activeInHierarchy) w = null;
                if (w != null) Seen.Add(w);
                Target t = Reuse(keepPinned, keepHover, door, w) ?? new Target { Door = door, Data = door.Data, Name = DoorName(atlas, door) };
                if (t.Name == null) continue;
                t.View = w;
                t.Anchor = p;
                t.Renderers = w != null ? BodyRenderers(w.gameObject) : new Renderer[0];
                Candidates.Add(t);
            }

            foreach (WgoData site in atlas.Sites)
            {
                Vector3 p = site.Position;
                if (Mathf.Abs(p.x - focus.x) > half.x || Mathf.Abs(p.z - focus.z) > half.y || site.IsHidden) continue;
                if ((atlas.AreaOf(p) == null) != outdoor) continue;
                Wgo w = GameScene.GetWgoViewGlobal(site.UniqueId);
                if (w == null || !w.gameObject.activeInHierarchy || !Seen.Add(w)) continue;
                Target t = Reuse(keepPinned, keepHover, null, w);
                if (t == null)
                {
                    string name = Names.Wgo(site.id);
                    if (name == null) continue;
                    t = new Target { View = w, Data = site, Name = name };
                }
                t.Renderers = BodyRenderers(w.gameObject);
                if (t.Renderers.Length > 0) Candidates.Add(t);
            }

            atlas.VisitedZonesIn(focus, half.x, half.y, TmpZones);
            foreach (WorldZone zone in TmpZones)
            {
                foreach (Wgo w in zone.Wgos)
                {
                    if (w == null || !w.gameObject.activeInHierarchy || w.Data == null || w.Data.IsHidden || !Seen.Add(w)) continue;
                    Target t = Reuse(keepPinned, keepHover, null, w);
                    if (t == null)
                    {
                        string name = Names.Wgo(w.Data.id);
                        if (name == null) continue;
                        t = new Target { View = w, Data = w.Data, Name = name };
                    }
                    t.Renderers = BodyRenderers(w.gameObject);
                    t.Character = false;
                    foreach (Renderer r in t.Renderers) if (r.name == "bdy") { t.Character = true; break; }
                    // Só o que importa: personagens e objetos de uso (bancada, baú, canteiro, estação...). Natureza
                    // colhível (árvore, arbusto, toco: tipo Work) e enfeite (tipo None) não ganham destaque nem tooltip.
                    WGODef.InteractionType kind = w.Data.Definition.interactionType;
                    if (!t.Character && (kind == WGODef.InteractionType.None || kind == WGODef.InteractionType.Work)) continue;
                    if (t.Renderers.Length > 0) Candidates.Add(t);
                }
            }
        }

        private static Target Reuse(Target a, Target b, Door door, Wgo view)
        {
            if (a != null && a.Door == door && (door != null || a.View == view)) return a;
            if (b != null && b.Door == door && (door != null || b.View == view)) return b;
            return null;
        }

        /// <summary>Para onde a porta leva: o nome da zona de destino, ou "Lado de fora"; null se o destino é desconhecido.</summary>
        private static string DoorName(Atlas atlas, Door door)
        {
            Place target = atlas.Through(door);
            if (target == null) return null;
            if (!target.Outdoor) return string.IsNullOrEmpty(target.Title) ? null : target.Title;
            WorldZone zone = atlas.TitleZoneAt(door.Dest.Position);
            string title = zone != null ? LLBase.L("wz_" + zone.Id) : null;
            return string.IsNullOrEmpty(title) || title == "wz_" + zone.Id ? L10n.Outside : title;
        }

        private static readonly List<Renderer> TmpRenderers = new List<Renderer>();
        private static readonly HashSet<Wgo> Seen = new HashSet<Wgo>();

        /// <summary>Renderers que formam o objeto; sombras, máscaras, luzes falsas e partículas inflam o retângulo.</summary>
        private static Renderer[] BodyRenderers(GameObject go)
        {
            TmpRenderers.Clear();
            go.GetComponentsInChildren(TmpRenderers);
            TmpRenderers.RemoveAll(r =>
            {
                string type = r.GetType().Name;
                if (type == "ParticleSystemRenderer" || type == "TrailRenderer" || type == "LineRenderer") return true;
                string n = r.name;
                // "-hed", "-bdy"...: cópias de lado do sprite do personagem, só para luz e sombra.
                if (n == "sh" || n.StartsWith("-", StringComparison.Ordinal) || n.EndsWith("_sh", StringComparison.Ordinal)) return true;
                if (n.Contains("blackout") || n.Contains("LightFaker") || n.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                Material m = r.sharedMaterial;
                return m != null && m.HasProperty("_SpriteInvisible") && m.GetFloat("_SpriteInvisible") > 0.5f;
            });
            return TmpRenderers.ToArray();
        }

        /// <summary>Entre os objetos sob o cursor vence o de menor retângulo (o mais específico).</summary>
        private static Target Pick(Vector2 mouse)
        {
            Target best = null;
            float bestArea = float.MaxValue;
            foreach (Target t in Candidates)
            {
                if (!TryRect(t) || !t.Rect.Contains(mouse)) continue;
                float area = t.Rect.width * t.Rect.height;
                if (area < bestArea) { best = t; bestArea = area; }
            }
            return best;
        }

        private static bool TryRect(Target t)
        {
            if (t.Character && t.View != null)
            {
                // Caixa do tamanho do desenho de um personagem, a partir dos pés (posição do objeto).
                Transform cam = CameraSystem.Instance.WorldCamera.transform;
                Vector3 feet = t.View.transform.position;
                Vector3 a = CameraSystem.WorldToScreenPoint(feet - cam.right * 0.4f);
                Vector3 b = CameraSystem.WorldToScreenPoint(feet + cam.right * 0.4f + cam.up * 1.3f);
                t.Rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                return true;
            }
            if (t.Door != null && (t.View == null || t.Renderers.Length == 0))
            {
                // Porta sem desenho próprio (só o gatilho no vão): caixa do tamanho de uma passagem.
                Transform cam = CameraSystem.Instance.WorldCamera.transform;
                Vector3 a = CameraSystem.WorldToScreenPoint(t.Anchor - cam.right * 0.6f);
                Vector3 b = CameraSystem.WorldToScreenPoint(t.Anchor + cam.right * 0.6f + cam.up * 1.4f);
                t.Rect = Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
                return true;
            }
            bool any = false;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (Renderer r in t.Renderers)
            {
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                Vector3 c = b.center, e = b.extents;
                int i = 0;
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                        for (int sz = -1; sz <= 1; sz += 2)
                            Corners[i++] = new Vector3(c.x + e.x * sx, c.y + e.y * sy, c.z + e.z * sz);
                for (i = 0; i < 8; i++)
                {
                    Vector3 s = CameraSystem.WorldToScreenPoint(Corners[i]);
                    if (s.x < x0) x0 = s.x;
                    if (s.x > x1) x1 = s.x;
                    if (s.y < y0) y0 = s.y;
                    if (s.y > y1) y1 = s.y;
                }
                any = true;
            }
            if (!any) return false;
            t.Rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return true;
        }

        private static void PlaceProxy(PlaceView view, Rect screen)
        {
            if (proxy == null)
            {
                var go = new GameObject("GK2Map_Target", typeof(RectTransform));
                proxy = (RectTransform)go.transform;
                proxy.SetParent(view.Viewport, false);
                proxy.anchorMin = proxy.anchorMax = proxy.pivot = new Vector2(0.5f, 0.5f);
            }
            float scale = Mathf.Max(0.01f, proxy.lossyScale.x);
            proxy.position = new Vector3(screen.center.x, screen.center.y, proxy.position.z);
            proxy.sizeDelta = screen.size / scale;
        }

        private static void Show(PlaceView view, Target t, bool details)
        {
            if (!TryRect(t)) return;
            PlaceProxy(view, t.Rect);
            var list = new List<LazyWidgetDataBase>();
            TextStyle header = Style("headerTextStyle"), text = Style("descriptionTextStyle"), small = Style("smallDescriptionTextStyle");
            list.Add(new UITooltipTextWidgetData(t.Name, TextAlignmentOptions.Center, header));
            if (t.Door != null)
            {
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(new UITooltipTextWidgetData(view.Atlas.AreaOf(t.Door.Dest.Position) != null ? L10n.DoorEnter : L10n.DoorLeave, TextAlignmentOptions.Center, text));
            }
            else if (AddBuildSite(list, t.Data, text)) { }
            else if (details)
            {
                var body = new List<LazyWidgetDataBase>();
                AddQuality(body, t.Data);
                AddCraft(body, t.Data, text);
                AddContents(body, t.Data, text, small);
                if (body.Count > 0)
                {
                    list.Add(new UITooltipSeparatorWidgetData());
                    list.AddRange(body);
                }
            }
            Names.Scrub(list);
            UITooltip.Show(list, proxy);
        }

        /// <summary>
        /// Lugar de construção da cidade ainda vazio: o que o jogo oferece erguer ali
        /// (<c>TownBuildingWgoComponent.GetAvailableBuildings</c>, que já respeita o que está liberado) e o custo de cada
        /// opção, no widget nativo de itens necessários (mostra quanto o jogador tem de cada um).
        /// </summary>
        private static bool AddBuildSite(List<LazyWidgetDataBase> list, WgoData w, TextStyle text)
        {
            TownBuildingWgoComponent comp = w.TownBuildingWgoComponent;
            if (comp == null) return false;
            if (comp.IsActive)
            {
                string built = Names.Text(comp.TownBuildingId);
                if (built == null) return false;
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(new UITooltipTextWidgetData(built, TextAlignmentOptions.Center, text));
                return true;
            }
            bool any = false;
            MultiInventory have = MainGame.PlayerController != null ? MainGame.PlayerController.WorkerMultiInventory : null;
            foreach (TownBuildingDef def in comp.GetAvailableBuildings(w))
            {
                string name = Names.Text(def.id);
                if (name == null) continue;
                list.Add(new UITooltipSeparatorWidgetData());
                list.Add(new UITooltipTextWidgetData(name, TextAlignmentOptions.Center, text));
                if (have != null && def.needItems != null && def.needItems.Count > 0)
                {
                    var cost = new UITooltipNeedsItemWidgetData(def.needItems, have);
                    if (cost.CraftItemCellsData.Count > 0) list.Add(cost);
                }
                any = true;
            }
            return any;
        }

        /// <summary>A mesma condição e o mesmo widget que o jogo usa para a qualidade de um objeto (<c>Wgo.SetWorldZoneWidgets</c>).</summary>
        private static void AddQuality(List<LazyWidgetDataBase> list, WgoData w)
        {
            WorldZoneData zone = w.WorldZoneData;
            if (zone == null || string.IsNullOrEmpty(zone.id) || zone.Definition == null) return;
            if (zone.Definition.displayType == 0 || w.Definition.qualityDisplayType != WGODef.QualityDisplayType.Show) return;
            if (string.IsNullOrEmpty(zone.Definition.qualityIcon)) return;
            list.Add(new UIQualityTooltipWidgetData(zone.Definition.qualityIcon, w));
        }

        private static void AddCraft(List<LazyWidgetDataBase> list, WgoData w, TextStyle text)
        {
            CraftComponent cc = w.CraftComponent;
            if (cc == null || !cc.HasCraftsInQueue) return;
            CraftElementBase el = cc.CurrentCraftElement;
            if (el == null || el.Def == null || el.Def.isHidden) return;
            string name = Names.Text(el.Def.id);
            if (name == null && el.Def is CraftDef cd)
            {
                string h = cd.TryGetResultingItemDef()?.GetHeader();
                if (!string.IsNullOrEmpty(h) && !Names.IsDevId(h)) name = h;
            }
            if (name == null) return;
            int total = el.TotalProgressTicks, done = el.ProgressTicks;
            string progress = total > 0 ? "  " + Mathf.Clamp(Mathf.RoundToInt(100f * done / total), 0, 100) + "%" : "";
            list.Add(new UITooltipTextWidgetData(L10n.Making + ": " + name + progress, TextAlignmentOptions.Center, text));
        }

        private static void AddContents(List<LazyWidgetDataBase> list, WgoData w, TextStyle text, TextStyle small)
        {
            Inventory inv = w.Inventory;
            List<Item> items = inv != null && inv.Data != null ? inv.Data.Inventory : null;
            if (items == null) return;
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            foreach (Item it in items)
            {
                if (it == null || it.IsEmpty || it.Definition == null) continue;
                string name = it.Definition.GetHeader();
                if (string.IsNullOrEmpty(name) || Names.IsDevId(name)) continue;
                if (!counts.ContainsKey(name)) { counts[name] = 0; order.Add(name); }
                counts[name] += Mathf.Max(1, it.Count);
            }
            if (order.Count == 0) return;
            list.Add(new UITooltipTextWidgetData(L10n.Contents, TextAlignmentOptions.Center, small));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < order.Count && i < MaxItems; i++)
            {
                if (i > 0) sb.Append('\n');
                sb.Append(order[i]).Append(" ×").Append(counts[order[i]]);
            }
            if (order.Count > MaxItems) sb.Append("\n+").Append(order.Count - MaxItems);
            list.Add(new UITooltipTextWidgetData(sb.ToString(), TextAlignmentOptions.Center, text));
        }

        internal static TextStyle Style(string field)
        {
            if (!StyleFields.TryGetValue(field, out FieldInfo f)) StyleFields[field] = f = AccessTools.Field(typeof(UITooltip), field);
            object tip = TooltipInstance.GetValue(null);
            return tip == null || f == null ? null : (TextStyle)f.GetValue(tip);
        }
    }
}
