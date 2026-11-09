using System.Collections;
using System.Linq;
using UnityEngine;

// GK2Hud (só green, muda o save de teste): larga um "body_corpse" no chão ao lado do jogador (DropSystem.DropItem, o timer de apodrecer começa sozinho) e lista os corpos com timer na cena.
public static class Snippet
{
    const bool LARGAR = true;
    static readonly Vector3 OFFSET = new Vector3(1.5f, 0f, -0.5f);

    public static IEnumerator Run()
    {
        if (!MainGame.PlayerController.TryGetCurrentGameScene(out GameScene gs)) { yield return "sem cena"; yield break; }
        if (LARGAR)
        {
            Vector3 p = MainGame.PlayerController.transform.position + OFFSET;
            bool ok = MainGame.Instance.dropSystem.DropItem(new Item("body_corpse", 1), gs.Id, p);
            Debug.Log("[cenario] corpo largado: " + ok);
            yield return new WaitForSecondsRealtime(1.5f);
        }
        yield return string.Join("\n", gs.GameSceneData.droppedItems.Where(d => d != null && d.AutoDestroyTimer > 0f)
            .Select(d => d.Id + " timer=" + d.AutoDestroyTimer.ToString("0") + " pos=" + d.Position + " flags=" + string.Join(",", d.CanNotBeAutoDestroyed.GetAllFlags().Select(kv => kv.Key + ":" + kv.Value)) + " view=" + gs.TryGetDropView(d.Item, out DropView v)));
    }
}
