using System.Linq;
using System.Text;
using UnityEngine;

// GK2Hud (só green, muda o save de teste): prepara craft em andamento na estação ALVO. Se estiver pronta esperando retirada, conclui (ContinueAutoCraft, como o jogador retirando); se a fila estiver vazia, enfileira CRAFT ×QTD; DAR_INGREDIENTES põe no inventário o que falta para o próximo da fila; COMECAR inicia o próximo como o jogador.
public static class Snippet
{
    const string ALVO = "kitchen_oven";
    const string CRAFT = "cook_baked_fish";
    const int QTD = 3;
    const bool CONCLUIR_PRONTO = true;
    const bool DAR_INGREDIENTES = true;
    const bool COMECAR = true;

    static string Dump(CraftComponent cc)
    {
        var el = cc.CurrentCraftElement;
        return $"status={cc.Status} fila={cc.CraftElementsQueue.Count} [" + string.Join(", ", cc.CraftElementsQueue.Select(e => e.CraftId + "×" + e.Count + (e.IsStarted ? " " + e.ProgressTicks + "/" + e.TotalProgressTicks : "") + " " + e.CraftStatus)) + "] atual=" + el?.CraftId;
    }

    public static object Run()
    {
        var sb = new StringBuilder();
        var view = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).FirstOrDefault(w => w.HasData && w.Data.id == ALVO);
        if (view == null) return "sem " + ALVO + " carregado";
        WgoData w = view.Data;
        CraftComponent cc = w.CraftComponent;
        sb.AppendLine("antes: " + Dump(cc));
        if (CONCLUIR_PRONTO && cc.Status == CraftComponentStatus.ReadyToFinishAutoCraft) cc.ContinueAutoCraft();
        if (!cc.HasCraftsInQueue)
        {
            var def = GameBalance.Me.GetData<CraftDef>(CRAFT);
            cc.AddToQueue(new CraftElement(CRAFT, QTD, new System.Collections.Generic.List<NeedItemData>(def.needItems), new CraftParamsData(CRAFT, w)));
        }
        if (DAR_INGREDIENTES && cc.CurrentCraftElement != null && !cc.CurrentCraftElement.IsStarted)
        {
            var multi = w.GetCraftableMultiInventory();
            foreach (var need in cc.CurrentCraftElement.Def.needItems)
            {
                if (need.IsGroup) continue;
                int has = multi.GetTotalCount(need.id), want = need.GetCount(w);
                if (has < want) { MainGame.PlayerData.inventory.AddItemToInventory(new Item(need.id, want - has)); sb.AppendLine("deu " + need.id + "×" + (want - has)); }
            }
        }
        // Estação servida por zumbi espera ele iniciar: COMECAR força como se o jogador iniciasse (TryStartCurCraft forceStart).
        if (COMECAR && cc.CurrentCraftElement != null && !cc.CurrentCraftElement.IsStarted) cc.TryStartCurCraft(true, MainGame.PlayerController);
        sb.AppendLine("depois: " + Dump(cc));
        return sb.ToString();
    }
}
