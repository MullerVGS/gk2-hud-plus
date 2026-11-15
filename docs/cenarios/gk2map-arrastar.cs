using System.Collections;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
// GK2Map: com a vista aberta, empurra o foco DX,DZ unidades por frame durante FRAMES (como um arrasto longo) e devolve onde parou.
public static class Snippet {
  const float DX = -0.1f, DZ = 0.05f; const int FRAMES = 30;
  public static IEnumerator Run() {
    var viewType = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Map").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance).GetType().Assembly.GetType("GK2Map.PlaceView");
    var cur = AccessTools.Property(viewType, "Current").GetValue(null);
    if (cur == null) { yield return "vista fechada"; yield break; }
    var f = AccessTools.Field(viewType, "focus");
    for (int i = 0; i < FRAMES; i++) { Vector3 p = (Vector3)f.GetValue(cur); p.x += DX; p.z += DZ; f.SetValue(cur, p); yield return null; }
    for (int i = 0; i < 40; i++) yield return null;
    yield return "foco " + f.GetValue(cur);
  } }
