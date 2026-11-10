using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

// Teleporta o jogador (save de teste) para o objeto carregado mais perto com renovação escondida em andamento (arbusto, toco, árvore voltando), cujo id começa com PREFIXO, para ver o hover de mundo.
public static class Snippet
{
    const string PREFIXO = "";
    public static IEnumerator Run()
    {
        Vector3 p = MainGame.PlayerController.transform.position;
        Wgo view = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None)
            .Where(w => w.HasData && w.Data.id.StartsWith(PREFIXO) && w.Data.CraftComponent?.CurrentCraftElement?.Def is CraftDefBase d && d.isHidden && d.isAuto)
            .OrderBy(w => (w.transform.position - p).sqrMagnitude).FirstOrDefault();
        if (view == null) { yield return "nenhuma renovação carregada"; yield break; }
        var tp = new WgoTeleportData(view.Data.id);
        AccessTools.Field(typeof(WgoTeleportData), "wgoData").SetValue(tp, view.Data);
        AccessTools.Field(typeof(WgoTeleportData), "gameSceneData").SetValue(tp, MainGame.WorldData.GetGameSceneDataById(view.Data.WorldId));
        bool ok = PlayerController.Teleport(tp);
        yield return new WaitForSecondsRealtime(4f);
        yield return (ok ? "teleportado para " : "falhou: ") + view.Data.id + " dist " + (MainGame.PlayerController.transform.position - view.transform.position).magnitude.ToString("0.0");
    }
}
