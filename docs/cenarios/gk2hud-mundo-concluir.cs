using System.Collections;
using System.Linq;
using UnityEngine;

// GK2Hud (só green, muda o save de teste): conclui todos os crafts da fila da estação ALVO (TryFinishCurCraft, como se terminassem) para ver a bolha de mundo fixada trocar o tempo por "Concluído"/"Done".
public static class Snippet
{
    const string ALVO = "millstone";

    public static IEnumerator Run()
    {
        var view = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).FirstOrDefault(w => w.HasData && w.Data.id == ALVO);
        if (view == null) { yield return "sem " + ALVO; yield break; }
        CraftComponent cc = view.Data.CraftComponent;
        int n = 0;
        while (cc.HasCraftsInQueue && n < 20)
        {
            cc.TryFinishCurCraft();
            n++;
            yield return new WaitForSecondsRealtime(0.3f);
        }
        yield return new WaitForSecondsRealtime(1.2f);
        yield return ALVO + ": " + n + " concluídos · fila " + cc.CraftElementsQueue.Count + " · status " + cc.Status;
    }
}
