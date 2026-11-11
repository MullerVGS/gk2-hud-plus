using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
// Estado da vista: lugar, nível, pilha, candidatos (portas marcadas). PASSOS de scroll antes, no mouse virtual atual ou em ALVO (id contém).
public static class Snippet {
  const int PASSOS = 0; static string ALVO = "";
  public static IEnumerator Run() {
    var asm = Chainloader.PluginInfos["arthur.gk2.map"].Instance.GetType().Assembly;
    var zoomType = asm.GetType("GK2Map.MapZoom"); var viewType = asm.GetType("GK2Map.PlaceView"); var inspect = asm.GetType("GK2Map.PlaceInspect");
    var cur = AccessTools.Property(viewType, "Current").GetValue(null);
    if (cur == null) { yield return "vista fechada"; yield break; }
    float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 0.7f) yield return null;
    var cands = ((IEnumerable)AccessTools.Field(inspect, "Candidates").GetValue(null)).Cast<object>().ToList();
    string Id(object c) => ((WgoData)AccessTools.Field(c.GetType(), "Data").GetValue(c)).id;
    if (ALVO != "") {
      object target = cands.FirstOrDefault(c => Id(c).Contains(ALVO));
      if (target == null) { yield return "sem " + ALVO + ": " + string.Join(",", cands.Select(Id)); yield break; }
      AccessTools.Method(inspect, "TryRect").Invoke(null, new[] { target });
      AccessTools.Property(zoomType, "VirtualMouse").SetValue(null, (Vector2?)((Rect)AccessTools.Field(target.GetType(), "Rect").GetValue(target)).center);
      t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 0.3f) yield return null;
    }
    for (int i = 0; i < Mathf.Abs(PASSOS); i++) {
      AccessTools.Property(zoomType, "TestScroll").SetValue(null, PASSOS > 0 ? 1 : -1);
      t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 0.6f) yield return null;
    }
    cur = AccessTools.Property(viewType, "Current").GetValue(null);
    if (cur == null) { yield return "voltou ao mapa"; yield break; }
    var place = AccessTools.Field(viewType, "place").GetValue(cur);
    var sb = new StringBuilder();
    sb.AppendLine("outdoor=" + AccessTools.Field(place.GetType(), "Outdoor").GetValue(place) + " zona=" + AccessTools.Field(place.GetType(), "ZoneId").GetValue(place) + " luz=" + AccessTools.Field(place.GetType(), "Light").GetValue(place) + " nível=" + AccessTools.Field(viewType, "level").GetValue(cur) + " pilha=" + ((System.Collections.ICollection)AccessTools.Field(viewType, "stack").GetValue(cur)).Count + " foco=" + AccessTools.Field(viewType, "focus").GetValue(cur) + " título='" + ((TMPro.TextMeshProUGUI)AccessTools.Field(viewType, "title").GetValue(cur))?.text + "'");
    cands = ((IEnumerable)AccessTools.Field(inspect, "Candidates").GetValue(null)).Cast<object>().ToList();
    sb.AppendLine(cands.Count + " candidatos; portas: " + string.Join(", ", cands.Where(c => AccessTools.Field(c.GetType(), "Door").GetValue(c) != null).Select(c => Id(c) + "→'" + AccessTools.Field(c.GetType(), "Name").GetValue(c) + "'")));
    sb.AppendLine(AccessTools.Property(inspect, "Describe").GetValue(null).ToString());
    yield return sb.ToString();
  } }
