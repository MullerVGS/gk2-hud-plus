using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): linha do tempo do calendário (precisa da aba aberta: gk2hud-calendario.cs com ALVO = "aba").
// ACAO = "rolar:<px>" (anda pelos dias; negativo = passado), "dia:<deslocamento>" (escolhe a coluna a partir de hoje),
// "marca:<nome>[#n]" (passa o mouse na n-ésima marca com esse nome de objeto, ex.: Window_sermon, At_nun_body, Journal,
// Day_ship; só as visíveis) ou "sair" (tira o mouse da marca).
public static class Snippet
{
    static string ACAO = "marca:Window_sermon";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object p = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
        object tab = AccessTools.Property(p.GetType(), "CalendarTab").GetValue(p);
        object page = AccessTools.Field(tab.GetType(), "page").GetValue(tab);
        var root = GameObject.Find("GK2Hud.CalendarPage");
        if (page == null || root == null) { yield return "calendário fechado"; yield break; }
        string[] a = ACAO.Split(new[] { ':' }, 2);
        if (a[0] == "rolar")
        {
            AccessTools.Method(page.GetType(), "ScrollBy").Invoke(page, new object[] { int.Parse(a[1]), 0 });
        }
        else if (a[0] == "dia")
        {
            AccessTools.Method(tab.GetType(), "TestOffset").Invoke(tab, new object[] { int.Parse(a[1]) });
        }
        else if (a[0] == "marca" || a[0] == "sair")
        {
            foreach (var h in root.GetComponentsInChildren<MonoBehaviour>(true).Where(m => m.GetType().Name == "Hover"))
                ((System.Action)AccessTools.Field(h.GetType(), "OnExit").GetValue(h))?.Invoke();
            if (a[0] == "marca")
            {
                string[] n = a[1].Split('#');
                int idx = n.Length > 1 ? int.Parse(n[1]) : 0;
                var grid = (RectTransform)root.transform.Find("Grid");
                var gc = new Vector3[4];
                grid.GetWorldCorners(gc);
                var hits = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == n[0] && t.GetComponents<MonoBehaviour>().Any(m => m.GetType().Name == "Hover"))
                    .Where(t => t.position.x >= gc[0].x - 1 && t.position.x <= gc[2].x).ToList();   // só as visíveis
                if (idx >= hits.Count) { yield return "marca não encontrada: " + a[1] + " (" + hits.Count + ")"; yield break; }
                var hv = hits[idx].GetComponents<MonoBehaviour>().First(m => m.GetType().Name == "Hover");
                ((System.Action)AccessTools.Field(hv.GetType(), "OnEnter").GetValue(hv))?.Invoke();
            }
        }
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 0.4f) yield return null;
        yield return AccessTools.Method(page.GetType(), "Describe").Invoke(page, null).ToString();
    }
}
