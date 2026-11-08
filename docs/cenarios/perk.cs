using UnityEngine;

// Adiciona um perk ao jogador no save de teste (troque o id). Útil para ver textos e efeitos de perks na UI.
public static class Snippet
{
    public static object Run()
    {
        const string id = "perk_operations_house";
        PerkSystemData perks = MainGame.Instance.GameSave.perkSystemData;
        if (!perks.HasPerk(id)) perks.AddPerk(id);
        Debug.Log("[cenario] perk ativo: " + id);
        return perks.activePerks.ConvertAll(p => p.id);
    }
}
