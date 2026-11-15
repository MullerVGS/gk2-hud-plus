using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só green, mexe no controle do jogador e devolve): com o tooltip de mundo visível (gk2hud-mundo-hover.cs), tira o controle por ByBuilding exatamente como o BuildManager faz ao entrar no modo de construção (BuildManager.cs:65) e confere que o tooltip some; devolve e confere que volta. Depois põe o 1º item do inventário na mão (PlantingPlayerState) e confere de novo.
public static class Snippet
{
    public static IEnumerator Run()
    {
        object plugin = AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance);
        object world = AccessTools.Property(plugin.GetType(), "WorldHover").GetValue(plugin);
        string D() => (string)AccessTools.Method(world.GetType(), "Describe").Invoke(world, null);
        var pc = MainGame.PlayerController;
        string antes = D() + " ssm=" + pc.Ssm.CurState?.GetType().Name;
        pc.SetControlTakenType(TakenControlType.ByBuilding, isEnabled: false);
        float t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.2f) yield return null;
        string durante = D();
        pc.SetControlTakenType(TakenControlType.ByBuilding, isEnabled: true);
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
        string depois = D();
        // Item na mão para usar/plantar (PlayerData.SetInteractingItem → SSM entra em PlantingPlayerState).
        Item it = MainGame.PlayerData.inventory.Data.Inventory.FirstOrDefault(x => x != null && !x.IsEmpty);
        string plantando = "sem item";
        if (it != null)
        {
            MainGame.PlayerData.SetInteractingItem(it);
            t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.2f) yield return null;
            plantando = "item " + it.id + " ssm=" + pc.Ssm.CurState?.GetType().Name + " → " + D();
            MainGame.PlayerData.RemoveInteractingItem();
        }
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
        yield return "antes: " + antes + "\nconstruindo: " + durante + "\ndepois: " + depois + "\ncom item na mão: " + plantando + "\nno fim: " + D() + " ssm=" + pc.Ssm.CurState?.GetType().Name;
    }
}
