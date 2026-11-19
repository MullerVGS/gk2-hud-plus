// Só leitura: abre a janela nativa de Controls (UIGameBindingSettingsWindow) e rola a lista de teclas até o fim (ROLAR), onde fica a linha da tecla de fixar do GK2 HUD+. Se já estiver aberta, fecha.
using System.Collections;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

public static class Snippet
{
    const bool ROLAR = true;

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var win = LazyUI.GetWindow<UIGameBindingSettingsWindow>();
        if (win.IsShown) { win.Close(); yield return "fechada"; yield break; }
        win.Open(null);
        for (int i = 0; i < 20; i++) yield return null;
        var scroll = (ScrollRect)AccessTools.Field(typeof(UIGameBindingSettingsWindow), "scrollRect").GetValue(win);
        if (ROLAR) scroll.verticalNormalizedPosition = 0f;
        for (int i = 0; i < 10; i++) yield return null;
        yield return "aberta, linhas: " + scroll.content.childCount;
    }
}
