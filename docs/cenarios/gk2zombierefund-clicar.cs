// GK2ZombieRefund: com a janela do zumbi aberta (zumbi-perks.cs), clica no nó NO. ACAO = "x" (esquerdo no X), "direito" (no nó) ou "comprar" (esquerdo no nó).
using System.Collections;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
public static class Snippet
{
    static string ACAO = "x";
    const string NO = "z_building_lvl_up_1_1";

    public static IEnumerator Run()
    {
        var w = Object.FindObjectsByType<TalentLevelUpWidget>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == NO);
        if (w == null) { yield return "nó não encontrado (abra zumbi-perks.cs)"; yield break; }
        var z = w.Data.ZombieWgoData;
        string antes = Estado(z);
        GameObject alvo = w.gameObject;
        var btn = PointerEventData.InputButton.Left;
        if (ACAO == "x") { var x = w.transform.Find("GK2ZombieRefund_X"); if (x == null || !x.gameObject.activeSelf) { yield return "sem X ativo; " + antes; yield break; } alvo = x.gameObject; }
        if (ACAO == "direito") btn = PointerEventData.InputButton.Right;
        var ped = new PointerEventData(EventSystem.current) { button = btn };
        ExecuteEvents.Execute(alvo, ped, ExecuteEvents.pointerClickHandler);
        yield return new WaitForSecondsRealtime(0.5f);
        var xx = w.transform.Find("GK2ZombieRefund_X");
        yield return ACAO + ": " + antes + " → " + Estado(z) + " · X " + (xx != null && xx.gameObject.activeSelf ? "visível" : "oculto");
    }

    static string Estado(ZombieWgoData z) => "perks " + z.GetUsedPerksCount() + "/" + z.RedSkulls + " R" + z.techRed + " G" + z.techGreen + " laranja " + z.GetTalentBranch("talent_orange").curTalentValue;
}
