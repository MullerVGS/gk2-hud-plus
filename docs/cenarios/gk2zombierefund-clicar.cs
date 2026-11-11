// GK2ZombieRefund: com a janela do zumbi aberta (zumbi-perks.cs), age no nó NO. ACAO = "hover" (tooltip), "direito"
// (abre a confirmação), "sim" / "nao" (responde a confirmação aberta) ou "comprar" (esquerdo no nó).
using System.Collections;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
public static class Snippet
{
    static string ACAO = "hover";
    const string NO = "z_building_lvl_up_1_1";

    public static IEnumerator Run()
    {
        var w = Object.FindObjectsByType<TalentLevelUpWidget>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == NO);
        if (w == null) { yield return "nó não encontrado (abra zumbi-perks.cs)"; yield break; }
        var z = w.Data.ZombieWgoData;
        string antes = Estado(z);
        var dialog = LazyUI.GetWindow<UIDialogWindow>();
        if (ACAO == "sim" || ACAO == "nao")
        {
            var btns = dialog.GetComponentsInChildren<UIDialogWindowButton>(false);
            if (!dialog.IsShown || btns.Length < 2) { yield return "sem diálogo aberto"; yield break; }
            var b = btns[ACAO == "sim" ? 0 : 1].GetComponentInChildren<LazyButton>();
            b.onClick.Invoke();
        }
        else
        {
            var ped = new PointerEventData(EventSystem.current) { button = ACAO == "direito" ? PointerEventData.InputButton.Right : PointerEventData.InputButton.Left };
            if (ACAO == "hover") ExecuteEvents.Execute(w.gameObject, ped, ExecuteEvents.pointerEnterHandler);
            else ExecuteEvents.Execute(w.gameObject, ped, ExecuteEvents.pointerClickHandler);
        }
        yield return new WaitForSecondsRealtime(0.6f);
        yield return ACAO + ": " + antes + " → " + Estado(z) + " · diálogo " + (dialog.IsShown ? "aberto" : "fechado");
    }

    static string Estado(ZombieWgoData z) => "perks " + z.GetUsedPerksCount() + "/" + z.RedSkulls + " R" + z.techRed + " laranja " + z.GetTalentBranch("talent_orange").curTalentValue;
}
