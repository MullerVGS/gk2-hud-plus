using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Teleporta o jogador (save de teste) até um NPC que esteja andando agora, para ver rotas no overlay técnico.
public static class Snippet
{
    public static IEnumerator Run()
    {
        WgoData target = null;
        int moving = 0;
        foreach (GameSceneData sd in MainGame.WorldData.gameSceneDataList)
            foreach (WgoData w in sd.wgoDataList)
            {
                if (w == null || w is ZombieWgoData || w.MovementComponent == null || !w.MovementComponent.IsMoving) continue;
                if (w.Definition == null || string.IsNullOrEmpty(w.Definition.npcLifeSimGroup)) continue;
                moving++;
                if (target == null) target = w;
            }
        if (target == null) { yield return "nenhum NPC andando agora"; yield break; }
        Debug.Log($"[cenario] {moving} NPCs andando; teleportando para {target.id} ({target.WorldId})");
        PlayerController.Teleport(new WgoTeleportData(target.id));
        yield return new WaitForSecondsRealtime(7f);
        yield return $"{moving} andando · alvo {target.id} · zona {MainGame.PlayerData.CurrentWorldZoneData?.id}";
    }
}
