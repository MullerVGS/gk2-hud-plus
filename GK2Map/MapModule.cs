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
                new Loc("Better map windows, showing only what you have explored.", "Mapas melhores, mostrando só o que você já explorou."),
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
                new Loc("Zoom the map with the mouse wheel.", "Zoom no mapa com o scroll do mouse.")).Toggle;
            PlacesEnabled = host.Option(map, "MapPlaces", "Map", "Places", true,
                new Loc("Look inside places", "Ver os locais por dentro"),
                new Loc("Zoom into a yellow arrow to peek inside that place.", "Aproxime numa setinha amarela para espiar o local por dentro.")).Toggle;
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
