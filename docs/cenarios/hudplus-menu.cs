using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// Menu do GK2 HUD+. ACAO: "abrir" (clica no botão GK2 HUD+ da pausa), "trocar" (linha ID: ◀/▶ como o clique),
// "hover" (tooltip da linha ID), "rolar" (POS 0..1, 1 = topo), "fechar" (X; a pausa deve voltar), "estado".
public static class Snippet
{
    static string ACAO = "abrir";
    static string ID = "Quests";
    static float POS = 0f;

    public static IEnumerator Run()
    {
        object plugin = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance;
        object menu = AccessTools.Property(plugin.GetType(), "Menu").GetValue(plugin);
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        if (ACAO == "abrir")
        {
            if (!pause.IsShown) { pause.Open(null); yield return new WaitForSecondsRealtime(0.5f); }
            GameObject btn = pause.transform.GetComponentsInChildren<Transform>(true).First(t => t.name == "GK2HudPlus_Menu").gameObject;
            btn.GetComponent<LazyButton>().onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.6f);
        }
        object window = AccessTools.Property(menu.GetType(), "Window").GetValue(menu);
        var rows = (IDictionary)AccessTools.Property(window.GetType(), "Rows").GetValue(window);
        var lazy = (Component)window;
        if (ACAO == "trocar")
        {
            var sw = (UISwitchButton)rows[ID];
            string field = sw.CurrentFieldIndex == 1 ? "decreaseButton" : "increaseButton";
            ((LazyButton)AccessTools.Field(typeof(UISwitchButton), field).GetValue(sw)).onClick.Invoke();
            yield return null; yield return null; yield return null;
            rows = (IDictionary)AccessTools.Property(window.GetType(), "Rows").GetValue(window);
        }
        else if (ACAO == "hover")
        {
            var sw = (UISwitchButton)rows[ID];
            ExecuteEvents.Execute(sw.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.4f);
        }
        else if (ACAO == "rolar")
        {
            var scroll = lazy.GetComponentInChildren<UnityEngine.UI.ScrollRect>();
            scroll.verticalNormalizedPosition = POS;
            yield return null;
        }
        else if (ACAO == "fechar")
        {
            var close = lazy.GetComponentsInChildren<Transform>(true).First(t => t.name == "CloseButton").GetComponentInChildren<LazyButton>(true);
            close.onClick.Invoke();
            yield return new WaitForSecondsRealtime(0.5f);
            yield return $"janela {(((bool)AccessTools.Property(window.GetType(), "IsShown").GetValue(window)) ? "aberta" : "fechada")} · pausa {(pause.IsShown ? "aberta" : "fechada")}";
            yield break;
        }
        var sb = new StringBuilder();
        sb.Append($"janela {(((bool)AccessTools.Property(window.GetType(), "IsShown").GetValue(window)) ? "aberta" : "fechada")} · {rows.Count} linhas: ");
        foreach (DictionaryEntry e in rows) sb.Append(e.Key + "=" + ((UISwitchButton)e.Value).CurrentFieldData + " ");
        yield return sb.ToString();
    }
}
