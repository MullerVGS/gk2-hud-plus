using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>
    /// Mapa do jogo com zoom pelo scroll do mouse e, nas setinhas amarelas (portas), a vista do interior: a câmera do
    /// mundo olha o local de longe, só para ver e inspecionar, sem interagir.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "arthur.gk2.map";
        public const string Name = "GK2 Map";
        public const string Version = "0.2.1";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        internal ConfigEntry<bool> ZoomEnabled;
        internal ConfigEntry<bool> PlacesEnabled;

        /// <summary>Nível de zoom atual do mapa (índice em <see cref="MapZoom.Levels"/>), comum às duas janelas de mapa.</summary>
        internal int ZoomIndex = MapZoom.DefaultLevel;

        private Harmony harmony;
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

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ZoomEnabled = Config.Bind("Map", "Zoom", true, "Scroll do mouse aproxima e afasta o mapa.");
            PlacesEnabled = Config.Bind("Map", "Places", true, "Nas setinhas amarelas do mapa, aproximar ou clicar mostra o interior do local.");
            harmony = new Harmony(Guid);
            harmony.Patch(AccessTools.Method(typeof(MapPageWidget), nameof(MapPageWidget.Redraw)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(RedrawPostfix)));
            foreach (MapPageWidget w in FindObjectsByType<MapPageWidget>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                Attach(w);
            Log.LogInfo($"{Name} {Version} carregado");
        }

        private void OnDestroy()
        {
            harmony?.UnpatchSelf();
            foreach (MapZoom z in FindObjectsByType<MapZoom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                z.Restore();
                Destroy(z);
            }
            if (Instance == this) Instance = null;
        }

        private static void RedrawPostfix(MapPageWidget __instance)
        {
            try { Instance?.Attach(__instance); }
            catch (Exception e) { Log.LogError(e); }
        }

        private void Attach(MapPageWidget w)
        {
            MapZoom z = w.GetComponent<MapZoom>();
            if (z == null) z = w.gameObject.AddComponent<MapZoom>();
            z.OnRedraw(w);
        }
    }
}
