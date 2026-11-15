using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

// Demo do clipe "Quests": abre a árvore de quests numa quest do começo (sem spoiler), o cursor para no nó (tooltip curto
// com o NPC) e o clique abre a página do diário com a janela crescendo. FIM = "pagina" para com a página aberta.
public static class Snippet
{
    static string QUEST = "7_village_head_talk";
    static string FIM = "";

    public static IEnumerator Run()
    {
        object tips = Demo.Hud("Tooltips");
        if (Demo.Hud("Quests") == null) { yield return "árvore de quests desligada"; yield break; }
        var stack = AccessTools.Field(tips.GetType(), "Stack").GetValue(tips);
        AccessTools.Method(stack.GetType(), "CloseAll").Invoke(stack, null);
        var w = LazyUI.GetWindow<CharacterWindow>();
        if (w.IsShown) { w.Close(); yield return null; yield return null; }
        var data = new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.QuestTree) { FocusOnQuest = QUEST };
        w.Open(data);
        yield return Demo.Wait(1f);
        var page = w.GetComponentInChildren<QuestTreePageWidget>();
        var scroll = (LazyScrollRect)AccessTools.Field(typeof(QuestTreePageWidget), "scrollRect").GetValue(page);
        var el = scroll.DisplayingElements.FirstOrDefault(e => (e.Data as QuestTreeElementWidgetData)?.questData?.id == QUEST);
        if (el == null) { yield return "quest " + QUEST + " não está na árvore"; yield break; }
        scroll.ScrollToTargetInstant(el.RectTransform, RectTransform.Axis.Vertical);
        scroll.CheckVisibility();
        for (int i = 0; i < 3; i++) yield return null;
        var node = Object.FindObjectsByType<QuestTreeElementWidget>(FindObjectsSortMode.None)
            .FirstOrDefault(n => n.isActiveAndEnabled && n.Data?.questData?.id == QUEST && n.GetComponentInParent<QuestTreePageWidget>() != null);
        if (node == null) { yield return "nó fora da vista"; yield break; }
        Vector2 at = Demo.Center(node.transform);
        Demo.Mouse(at + new Vector2(-220, 150));
        yield return Demo.Wait(0.6f);
        yield return Demo.Glide(at + new Vector2(4, 6), 0.8f);
        node.button.ForceOnEnter();
        yield return Demo.Wait(1.8f);
        node.button.ForceOnClick();
        node.button.ForceOnExit();
        yield return Demo.Wait(FIM == "pagina" ? 0.8f : 3.2f);
        if (FIM == "pagina") { yield return "página aberta em " + at; yield break; }
        Demo.Mouse(null);
        yield return "demo quests ok em " + at;
    }
}
