using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

// GK2Hud: abre a árvore de quests focada em QUEST, passa o "mouse" no nó (tooltip com retrato) e, com FIXAR, aperta o botão de fixar (abre a ficha do NPC ao lado).
public static class Snippet
{
    const string QUEST = "81_guards_elections_ask";
    const bool FIXAR = true;

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var plugin = Chainloader.PluginInfos["arthur.gk2.hud"].Instance;
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
        for (int i = 0; i < 3; i++) yield return null;
        if (FIXAR)
        {
            AccessTools.Field(tips.GetType(), "TestPin").SetValue(tips, true);
            for (int i = 0; i < 4; i++) yield return null;
        }
        yield return AccessTools.Method(quests.GetType(), "Describe").Invoke(quests, null) + "\n" + AccessTools.Method(tips.GetType(), "Describe").Invoke(tips, null);
    }
}
