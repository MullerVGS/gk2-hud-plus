using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

// GK2Hud: abre a árvore de quests, rola até QUEST e, conforme MODO, deixa o "mouse" parado no nó (tooltip básico), aperta o botão de fixar (ficha do NPC) ou clica (página do diário, janela crescida, fio aceso).
public static class Snippet
{
    const string QUEST = "81_guards_elections_ask";
    const string MODO = "abrir";   // hover (tooltip básico) | fixar (botão do meio: ficha do NPC) | abrir (clique: página do diário)
    const float ESPERA = 0.6f;     // segundos depois do clique (0,1 pega a animação no meio)

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var plugin = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
        object tips = AccessTools.Property(plugin.GetType(), "Tooltips").GetValue(plugin);
        object quests = AccessTools.Property(plugin.GetType(), "Quests").GetValue(plugin);
        var stack = AccessTools.Field(tips.GetType(), "Stack").GetValue(tips);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);

        var w = LazyUI.GetWindow<CharacterWindow>();
        if (w.IsShown) { w.Close(); yield return null; yield return null; }
        Transform pages = w.transform.Find("Content/Pages");
        if (pages != null) foreach (Transform p in pages) if (p.GetComponent<LazyWidgetBase>() != null) p.gameObject.SetActive(false);
        var data = new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.QuestTree);
        data.FocusOnQuest = QUEST;
        w.Open(data);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1f) yield return null;

        // A árvore só instancia os nós visíveis: rola até o nó pedido (o FocusOnQuest só vale na primeira abertura).
        var page = w.GetComponentInChildren<QuestTreePageWidget>();
        var scroll = (LazyScrollRect)AccessTools.Field(typeof(QuestTreePageWidget), "scrollRect").GetValue(page);
        var el = scroll.DisplayingElements.FirstOrDefault(e => (e.Data as QuestTreeElementWidgetData)?.questData?.id == QUEST);
        if (el == null) { yield return "quest " + QUEST + " não está na árvore"; yield break; }
        scroll.ScrollToTargetInstant(el.RectTransform, RectTransform.Axis.Vertical);
        scroll.CheckVisibility();
        for (int i = 0; i < 3; i++) yield return null;

        var node = Object.FindObjectsByType<QuestTreeElementWidget>(FindObjectsSortMode.None)
            .FirstOrDefault(n => n.isActiveAndEnabled && n.Data?.questData != null && n.Data.questData.id == QUEST && n.GetComponentInParent<QuestTreePageWidget>() != null);
        if (node == null) { yield return "nó " + QUEST + " não está na árvore visível"; yield break; }
        AccessTools.Property(tips.GetType(), "VirtualMouse").SetValue(tips, (Vector2?)(Vector2)node.transform.position);
        node.button.ForceOnEnter();
        if (MODO == "hover" || MODO == "fixar")
        {
            float t1 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t1 < 1.2f) yield return null;   // o tooltip básico espera o mouse parar ([Quests] HoverDelay)
        }
        if (MODO == "fixar")
        {
            AccessTools.Field(tips.GetType(), "TestPin").SetValue(tips, true);
            for (int i = 0; i < 4; i++) yield return null;
        }
        if (MODO == "abrir")
        {
            node.button.ForceOnClick();
            node.button.ForceOnExit();
            float t2 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t2 < ESPERA) yield return null;   // a janela cresce em 0,25 s
        }
        yield return AccessTools.Method(quests.GetType(), "Describe").Invoke(quests, null) + "\n" + AccessTools.Method(tips.GetType(), "Describe").Invoke(tips, null);
    }
}
