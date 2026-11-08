using System.Collections;
using UnityEngine;

// Teleporta o jogador (save de teste) para o posto do primeiro zumbi com posto, para ver rotas e trabalho.
public static class Snippet
{
    public static IEnumerator Run()
    {
        ZombieSystemData zs = MainGame.ZombieSystemData;
        WgoData target = null;
        foreach (SGuid id in zs.zombieOnSceneWgoIds)
        {
            ZombieWgoData z = zs.GetZombie(id);
            if (z != null && z.AttachedWgoData != null && z.ZombieType == ZombieType.Caretaker) { target = z.AttachedWgoData; break; }
        }
        if (target == null) { yield return "nenhum zumbi zelador com posto"; yield break; }
        Debug.Log("[cenario] teleportando para " + target.id + " em " + target.WorldId);
        bool ok = PlayerController.Teleport(new WgoTeleportData(target.id));
        yield return new WaitForSecondsRealtime(6f);
        yield return (ok ? "teleportado para " : "falhou: ") + target.id + " · zona " + MainGame.PlayerData.CurrentWorldZoneData?.id;
    }
}
