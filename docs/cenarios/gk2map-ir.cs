using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
// GK2Map: abre a vista do mundo aberto centrada no objeto WGO (id), na escala NIVEL (0 longe, 1 perto).
public static class Snippet {
  static string WGO = "repair_sign_1102"; const int NIVEL = 1;
  public static IEnumerator Run() {
    var asm = Chainloader.PluginInfos["arthur.gk2.map"].Instance.GetType().Assembly;
    var zoomType = asm.GetType("GK2Map.MapZoom"); var viewType = asm.GetType("GK2Map.PlaceView");
    var cur = AccessTools.Property(viewType, "Current").GetValue(null);
    if (cur != null) { AccessTools.Method(viewType, "Exit").Invoke(cur, null); yield return null; yield return null; }
    var w = LazyUI.GetWindow<CharacterWindow>();
    var zoom = w.GetComponentInChildren(zoomType);
    if (zoom == null) { yield return "abra o mapa antes (gk2map-zoom.cs)"; yield break; }
    var atlas = AccessTools.Property(zoomType, "Atlas").GetValue(zoom);
    var data = MainGame.WorldData.GetWgoData(WGO);
    if (data == null) { yield return "sem " + WGO; yield break; }
    var place = AccessTools.Method(atlas.GetType(), "OutdoorAt").Invoke(atlas, new object[] { data.Position, 0f });
    if (place == null) { yield return WGO + " fica fora das regiões conhecidas do mapa"; yield break; }
    AccessTools.Method(viewType, "Enter").Invoke(null, new object[] { zoom, place });
    cur = AccessTools.Property(viewType, "Current").GetValue(null);
    AccessTools.Field(viewType, "level").SetValue(cur, NIVEL);
    float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 1.2f) yield return null;
    yield return "vista em " + WGO + " foco " + AccessTools.Field(viewType, "focus").GetValue(cur);
  } }
