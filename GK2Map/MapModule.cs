using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using GK2Shared;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Map
{
    /// <summary>
    /// Mapa do jogo com zoom pelo scroll do mouse e, nas setinhas amarelas (portas), a vista do interior: a câmera do
    /// mundo olha o local de longe, só para ver e inspecionar, sem interagir.
    /// </summary>
    internal sealed class MapModule
    {
        /// <summary>GUID do mod avulso: nome do .cfg antigo.</summary>
        public const string LegacyGuid = "arthur.gk2.map";

        internal static MapModule Instance;
        internal static ManualLogSource Log;

        internal readonly ModHost Host;
        internal ConfigEntry<bool> ZoomEnabled;
        internal ConfigEntry<bool> PlacesEnabled;

        /// <summary>Nível de zoom atual do mapa (índice em <see cref="MapZoom.Levels"/>), comum às duas janelas de mapa.</summary>
        internal int ZoomIndex = MapZoom.DefaultLevel;

        private TerrainMap terrain;

        /// <summary>Mapa de onde há chão, montado na primeira vez que a vista precisa e refeito se o mundo for recarregado.</summary>
        internal TerrainMap Terrain
        {
            get
            {
                ChunkManager cm = LazyBearTechnology.LazySingleton<ChunkManager>.Instance;
                if (cm == null) return null;
                if (terrain == null || !terrain.IsFor(cm) || terrain.Empty) terrain = new TerrainMap(cm);
                return terrain;
            }
        }

        private Texture arrowsTexture;
        private List<Vector2> arrows;

        /// <summary>Setinhas da arte do mapa, lidas uma vez por textura; <c>null</c> enquanto a textura não carregou.</summary>
        internal List<Vector2> Arrows(Image mapImage, Vector2 mapSize)
        {
            Texture tex = mapImage != null && mapImage.sprite != null ? mapImage.sprite.texture : null;
            if (tex == null) return null;
            if (arrows == null || arrowsTexture != tex)
            {
                arrows = Places.FindArrows(mapImage, mapSize);
                arrowsTexture = tex;
            }
            return arrows;
        }

        public MapModule(ModHost host)
        {
            Host = host;
            Instance = this;
            Log = host.Log;
            host.Module("map", null, () => { if (Instance == this) Instance = null; });
            FeatureInfo map = host.Feature("Map", "map", "Map", "Enabled",
                new Loc("Better map", "Mapa melhorado"),
                new Loc("Extra tools on both map windows, always limited to the area you have explored.",
                    "Ferramentas a mais nas duas janelas de mapa, sempre presas à área que você já explorou."),
                h =>
                {
                    h.Patch(AccessTools.Method(typeof(MapPageWidget), nameof(MapPageWidget.Redraw)),
                        postfix: new HarmonyMethod(typeof(MapModule), nameof(RedrawPostfix)));
                    foreach (MapPageWidget w in Object.FindObjectsByType<MapPageWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                        Attach(w);
                    return this;
                }, dispose: m => m.RestoreAll());
            ZoomEnabled = host.Option(map, "MapZoom", "Map", "Zoom", true,
                new Loc("Zoom", "Zoom"),
                new Loc("The mouse wheel zooms the map in and out, anchored at the cursor.", "O scroll do mouse aproxima e afasta o mapa, ancorado no cursor.")).Toggle;
            PlacesEnabled = host.Option(map, "MapPlaces", "Map", "Places", true,
                new Loc("Look inside places", "Ver os locais por dentro"),
                new Loc("Zooming in on a yellow arrow (or clicking it) shows that place; one more scroll at full zoom goes down to the open world. Hover and click to inspect.",
                    "Aproximar numa setinha amarela (ou clicar nela) mostra o local; mais um scroll no zoom máximo desce ao mundo aberto. Passe o mouse e clique para inspecionar.")).Toggle;
            host.ImportLegacy(LegacyGuid);
        }

        private void RestoreAll()
        {
            foreach (MapZoom z in Object.FindObjectsByType<MapZoom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                z.Restore();
                Object.Destroy(z);
            }
        }

        /// <summary>Erro num componente do mod (MapZoom, PlaceView): conta para a parte e, repetido, desliga o mapa.</summary>
        internal static void Fail(Exception e, string where) => Instance?.Host.Guard.Report("Map", e, where);

        private static void RedrawPostfix(MapPageWidget __instance)
        {
            try { Instance?.Attach(__instance); }
            catch (Exception e) { Fail(e, "MapPageWidget.Redraw"); }
        }

        private void Attach(MapPageWidget w)
        {
            MapZoom z = w.GetComponent<MapZoom>();
            if (z == null) z = w.gameObject.AddComponent<MapZoom>();
            z.OnRedraw(w);
        }
    }
}
