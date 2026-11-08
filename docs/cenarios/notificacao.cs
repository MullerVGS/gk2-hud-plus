using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Mostra notificações nativas (UINotificator) com texto próprio, com e sem ícone, para conferir o visual.
public static class Snippet
{
    public static IEnumerator Run()
    {
        UINotificator n = LazySingleton<UINotificator>.Instance;
        n.ShowSimpleTextNotification("GK2 Insight updated to 0.1.1");
        yield return new WaitForSecondsRealtime(0.3f);
        n.ShowSimpleTextWithIconNotification("GK2 Insight updated to 0.1.1", "b_sleep");
        yield return new WaitForSecondsRealtime(1f);
        yield return "ok";
    }
}
