// Abre a janela do zumbi (padrão: Addy Gnaw) na aba Perks, para conferir talentos/pontos/caveiras.
using System.Collections;
using System.Reflection;
using LazyBearTechnology;
using UnityEngine;
public static class Snippet
{
    public static IEnumerator Run()
    {
        ZombieWgoData zombie = null;
        foreach (SGuid id in MainGame.ZombieSystemData.zombieOnSceneWgoIds)
        {
            var z = MainGame.ZombieSystemData.GetZombie(id);
            if (z != null && LLBase.L(z.Name).StartsWith("Addy")) { zombie = z; break; }
        }
        if (zombie == null) { yield return "zumbi não encontrado"; yield break; }
        typeof(UIZombieWorkerWindow).GetField("lastOpenedPerksTab", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        LazyUI.GetWindow<UIZombieWorkerWindow>().Open(new UIZombieWorkerWindowData(zombie));
        yield return new WaitForSecondsRealtime(1f);
        yield return "aberta: " + LLBase.L(zombie.Name) + " perks " + zombie.GetUsedPerksCount() + "/" + zombie.RedSkulls + " R" + zombie.techRed;
    }
}
