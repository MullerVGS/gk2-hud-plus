// Abre a janela do zumbi (padrão: Addy Gnaw) na aba Perks, árvore laranja, com as outras janelas fechadas.
using System.Collections;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;
public static class Snippet
{
    const string ZUMBI = "Addy";

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
        if (zombie == null) { yield return "zumbi não encontrado"; yield break; }
        typeof(UIZombieWorkerWindow).GetField("lastOpenedPerksTab", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        zw.Open(new UIZombieWorkerWindowData(zombie));
        yield return new WaitForSecondsRealtime(1f);
        yield return "aberta: " + LLBase.L(zombie.Name) + " perks " + zombie.GetUsedPerksCount() + "/" + zombie.RedSkulls + " R" + zombie.techRed + " G" + zombie.techGreen;
    }
}
