using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud (só leitura): transições da aba Calendário no TAB. Abre o calendário, clica na aba nativa ABA_NATIVA pelo botão
// do jogo, volta ao calendário, fecha a janela e reabre como o TAB faz; relata página visível e LastOpenedPage a cada passo.
public static class Snippet
{
    const CharacterWindowData.CharPage ABA_NATIVA = CharacterWindowData.CharPage.Main;

    static object Tab() => AccessTools.Property(Chainloader.PluginInfos["arthur.gk2.hud"].Instance.GetType(), "CalendarTab").GetValue(Chainloader.PluginInfos["arthur.gk2.hud"].Instance);

    public static object Run() { return Go(); }

    static string Visible(CharacterWindow w)
    {
        var sb = new StringBuilder();
        foreach (Transform p in w.transform.Find("Content/Pages")) if (p.gameObject.activeSelf) sb.Append(p.name + " ");
        return sb.ToString().Trim() + " | last=" + w.LastOpenedPage + " save=" + MainGame.PlayerData.lastOpenedPage;
    }

    static IEnumerator Wait(float s) { float t = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t < s) yield return null; }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        var w = LazyUI.GetWindow<CharacterWindow>();
        object tab = Tab();
        if (!w.IsShown) { w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map)); yield return Wait(1.2f); }
        AccessTools.Method(tab.GetType(), "TestOpen").Invoke(tab, new object[] { null });
        yield return null;
        sb.AppendLine("1 calendário: " + Visible(w));
        var buttons = Traverse.Create(w).Field("pagesButtons").GetValue<Dictionary<CharacterWindowData.CharPage, CharPageTabButton>>();
        Traverse.Create(buttons[ABA_NATIVA]).Field("button").GetValue<LazyButton>().onClick.Invoke();
        yield return null;
        sb.AppendLine("2 aba nativa " + ABA_NATIVA + ": " + Visible(w));
        AccessTools.Method(tab.GetType(), "TestOpen").Invoke(tab, new object[] { null });
        yield return null;
        sb.AppendLine("3 calendário de novo: " + Visible(w));
        Traverse.Create(buttons[w.LastOpenedPage]).Field("button").GetValue<LazyButton>().onClick.Invoke();
        yield return null;
        sb.AppendLine("4 clique na mesma aba nativa (LastOpenedPage): " + Visible(w));
        AccessTools.Method(tab.GetType(), "TestOpen").Invoke(tab, new object[] { null });
        yield return null;
        w.Close();
        yield return Wait(0.3f);
        sb.AppendLine("5 fechada com calendário aberto: shown=" + w.IsShown + " " + AccessTools.Method(tab.GetType(), "Describe").Invoke(tab, null));
        w.Open(new CharacterWindowData(MainGame.Instance.GameSave));
        yield return null;
        sb.AppendLine("6 reaberta (TAB): " + Visible(w));
        yield return sb.ToString();
    }
}
