using System.Collections;
using System.Linq;
using UnityEngine;

// GK2Hud (só green, muda o save de teste): transforma N canteiros "garden_carrot_ready" em cenoura crescendo (ChangeWgoData → garden_carrot + craft garden_carrot_growing, como o fim do plantio faz), adianta CELULAS boas e teleporta o jogador para a horta.
public static class Snippet
{
    const int N = 2;
    const int CELULAS = 3;          // células boas já crescidas (gameRes succeded_cells, lido no início do craft)
    const bool TELEPORTAR = true;

    public static IEnumerator Run()
    {
        var gs = MainGame.WorldData.gameSceneDataList.First(g => g.id == "RuinedTemple");
        var ready = gs.wgoDataList.Where(w => w != null && w.id == "garden_carrot_ready").Take(N).ToList();
        var made = gs.wgoDataList.Where(w => w != null && w.id == "garden_carrot").ToList();
        foreach (var w in ready)
        {
            MainGame.WorldData.ChangeWgoData(w, "garden_carrot");
            w.SetGameRes("crop", 0); w.SetGameRes("seed", 0);
            w.SetGameRes("succeded_cells", CELULAS); w.SetGameRes("failed_cells", 0);
            w.CraftComponent.AddToQueue(new CraftElement("garden_carrot_growing", 1, new CraftParamsData("garden_carrot_growing", w)));
            made.Add(w);
        }
        var first = made.FirstOrDefault();
        if (first == null) { yield return "nenhum canteiro"; yield break; }
        if (TELEPORTAR && MainGame.PlayerData.CurrentWorldZoneData?.id != "garden")
        {
            PlayerController.Teleport(new WgoTeleportData("garden_crafter"));
            yield return new WaitForSecondsRealtime(6f);
        }
        yield return string.Join("\n", made.Select(w => { var e = w.CraftComponent.CurrentCraftElement; return w.id + " " + w.Position + " " + (e == null ? "-" : e.CraftId + " " + e.ProgressTicks + "/" + e.TotalProgressTicks + " " + w.CraftComponent.Status); }))
            + "\nzona " + MainGame.PlayerData.CurrentWorldZoneData?.id;
    }
}
