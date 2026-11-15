using System.Collections;
using System.Linq;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// Demo do clipe "Refund": abre a árvore de perks de um zumbi, o cursor para num perk comprado (o tooltip ganha a dica do
// refund), o clique direito abre o diálogo nativo e o "No" fecha sem devolver nada. FIM = "meio" para com o diálogo aberto.
public static class Snippet
{
    static string ZUMBI = "McDead";
    static string NO = "ztalent_apprentice_carpenter";
    static string FIM = "";

    public static IEnumerator Run()
    {
        var ch = LazyUI.GetWindow<CharacterWindow>();
        if (ch != null && ch.IsShown) ch.Close();
        var zw = LazyUI.GetWindow<UIZombieWorkerWindow>();
        if (zw.IsShown) zw.Close();
        yield return null;
        ZombieWgoData zombie = null;
        foreach (SGuid id in MainGame.ZombieSystemData.zombieOnSceneWgoIds)
        {
            var z = MainGame.ZombieSystemData.GetZombie(id);
            if (z != null && LLBase.L(z.Name).StartsWith(ZUMBI)) { zombie = z; break; }
        }
        if (zombie == null) { yield return "zumbi " + ZUMBI + " não encontrado"; yield break; }
        typeof(UIZombieWorkerWindow).GetField("lastOpenedPerksTab", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        zw.Open(new UIZombieWorkerWindowData(zombie));
        yield return Demo.Wait(1.0f);
        var node = Object.FindObjectsByType<TalentLevelUpWidget>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == NO);
        if (node == null) { yield return "perk " + NO + " não está na árvore"; yield break; }
        Vector2 at = Demo.Center(node.transform);
        Demo.Mouse(at + new Vector2(-220, 140));
        yield return Demo.Wait(0.5f);
        yield return Demo.HoverOn(node.gameObject, 0.8f, 1.8f);
        Demo.Click(node.gameObject, PointerEventData.InputButton.Right);
        yield return Demo.Wait(1.6f);
        if (FIM == "meio") { yield return "diálogo aberto"; yield break; }
        var dialog = LazyUI.GetWindow<UIDialogWindow>();
        var no = dialog.GetComponentsInChildren<UIDialogWindowButton>(false).Skip(1).FirstOrDefault()?.GetComponentInChildren<LazyButton>();
        if (no != null)
        {
            yield return Demo.Glide(Demo.Center(no.transform) + new Vector2(3, 5), 0.6f);
            yield return Demo.Wait(0.3f);
            no.onClick.Invoke();
        }
        yield return Demo.Wait(0.8f);
        Demo.Mouse(null);
        yield return "demo refund ok";
    }
}
