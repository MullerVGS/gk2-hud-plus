using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só leitura): com a aba Calendário aberta (gk2hud-calendario.cs ALVO="aba"), escolhe a linha da lista pelo
// deslocamento a partir de hoje (DIA; negativo = dias passados, o primeiro é "antes do diário") e passa o mouse na linha
// do diário cujo nome do objeto começa com LINHA ("" = nenhuma).
public static class Snippet
{
    const int DIA = -2;
    const string LINHA = "";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object p = Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
        object tab = AccessTools.Property(p.GetType(), "CalendarTab").GetValue(p);
        AccessTools.Method(tab.GetType(), "TestOffset").Invoke(tab, new object[] { DIA });
        yield return null;
        yield return null;
        string hovered = "-";
        if (LINHA != "")
        {
            var page = GameObject.Find("GK2Hud.CalendarPage");
            var line = page != null ? page.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name.StartsWith(LINHA)) : null;
            if (line != null)
            {
                var hover = line.GetComponents<Component>().First(c => c.GetType().Name == "Hover");
                var enter = (System.Action)AccessTools.Field(hover.GetType(), "OnEnter").GetValue(hover);
                enter();
                hovered = line.name;
            }
        }
        yield return new WaitForSecondsRealtime(0.3f);
        yield return "linha=" + hovered + " | " + AccessTools.Method(tab.GetType(), "Describe").Invoke(tab, null);
    }
}
