using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

// Demo do clipe "World": o cursor para sobre as duas coisas em andamento mais perto do jogador (craft, canteiro, corpo...)
// e o tooltip de mundo mostra como vai e quanto falta. FIM = "primeiro" para no primeiro alvo (enquadrar com um print).
public static class Snippet
{
    static string FIM = "";

    public static IEnumerator Run()
    {
        object world = Demo.Hud("WorldHover");
        if (world == null) { yield return "tooltip do mundo desligado"; yield break; }
        object targets = AccessTools.Field(world.GetType(), "Targets").GetValue(world);
        AccessTools.Method(targets.GetType(), "Invalidate").Invoke(targets, null);
        AccessTools.Method(targets.GetType(), "Pick").Invoke(targets, new object[] { new Vector2(-1000, -1000) });
        Vector3 pp = MainGame.PlayerController.transform.position;
        var picks = new List<KeyValuePair<string, Vector2>>();
        foreach (object c in ((IEnumerable)Demo.Prop(targets, "All")).Cast<object>())
        {
            AccessTools.Method(c.GetType(), "UpdateRect").Invoke(c, null);
            var view = (Component)AccessTools.Field(c.GetType(), "View").GetValue(c) ?? (Component)AccessTools.Field(c.GetType(), "DropView").GetValue(c);
            if (view == null) continue;
            var data = (WgoData)AccessTools.Field(c.GetType(), "Data").GetValue(c);
            Vector2 center = (Vector2)AccessTools.Field(c.GetType(), "Center").GetValue(c);
            Vector2 top = new Vector2(center.x, Screen.height - center.y);
            if (top.x < 300 || top.x > 1620 || top.y < 250 || top.y > 900) continue;
            picks.Add(new KeyValuePair<string, Vector2>((data?.id ?? "drop") + ":" + (view.transform.position - pp).magnitude.ToString("0.0"), top));
        }
        picks = picks.OrderBy(p => float.Parse(p.Key.Split(':')[1])).GroupBy(p => p.Key.Split(':')[0]).Select(g => g.First()).Take(2).ToList();
        if (picks.Count == 0) { yield return "nada em andamento na tela"; yield break; }

        Vector2 rest = new Vector2(1250, 560);   // caminho de pedras do quintal: nada em andamento embaixo
        Demo.Mouse(rest);
        yield return Demo.Wait(0.6f);
        foreach (var p in picks)
        {
            yield return Demo.Glide(p.Value, 0.8f);
            yield return Demo.Wait(2.2f);
            if (FIM == "primeiro") { yield return "parado em " + p.Key + " " + p.Value; yield break; }
        }
        yield return Demo.Glide(rest, 0.8f);
        yield return Demo.Wait(0.8f);
        Demo.Mouse(null);
        yield return "demo world ok: " + string.Join(", ", picks.Select(p => p.Key + "@" + p.Value));
    }
}
