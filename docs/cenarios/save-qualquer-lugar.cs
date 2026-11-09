using System.Collections;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Teste de "salvar em qualquer lugar": fora de casa, meio do dia, salva pelo SaveSystem, volta ao menu, recarrega e compara.
public static class Snippet
{
    static string Estado()
    {
        var g = MainGame.Instance.GameSave;
        var p = MainGame.PlayerData;
        var sb = new StringBuilder();
        sb.Append("dia=").Append(g.environmentData.Day);
        sb.Append(" hora=").Append(g.environmentData.TimeOfDay.ToString("0.000"));
        sb.Append(" pos=").Append(MainGame.PlayerController.transform.position.ToString("0.0"));
        sb.Append(" zona=").Append(p.CurrentWorldZoneData?.id);
        sb.Append(" energia=").Append(p.GetRes("energy").ToString("0.0"));
        sb.Append(" crafts=").Append(g.craftSystemData != null ? "ok" : "null");
        sb.Append(" mover=").Append(g.movementSystemData.movingObjects.Count);
        return sb.ToString();
    }

    public static IEnumerator Run()
    {
        ZombieSystemData zs = MainGame.ZombieSystemData;
        WgoData target = null;
        foreach (SGuid id in zs.zombieOnSceneWgoIds)
        {
            ZombieWgoData z = zs.GetZombie(id);
            if (z != null && z.AttachedWgoData != null) { target = z.AttachedWgoData; break; }
        }
        if (target != null) { PlayerController.Teleport(new WgoTeleportData(target.id)); yield return new WaitForSecondsRealtime(6f); }
        EnvironmentEngine.Instance.SetTimeOfDay(0.40f);
        yield return new WaitForSecondsRealtime(3f);
        string antes = Estado();
        Debug.Log("[cenario] antes: " + antes);
        float t0 = Time.realtimeSinceStartup;
        bool ok = false;
        SaveSystem.Save(MainGame.Instance.SaveSlotData, MainGame.Instance.GameSave, () => ok = true, null);
        float ms = (Time.realtimeSinceStartup - t0) * 1000f;
        string depoisDoSave = Estado();
        Debug.Log("[cenario] salvo ok=" + ok + " em " + ms.ToString("0") + " ms; depois do save: " + depoisDoSave);
        yield return new WaitForSecondsRealtime(1f);
        MainGame.Instance.GoToMenu();
        float deadline = Time.realtimeSinceStartup + 60f;
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = LazyUI.GetWindow<UIMainMenuWindow>();
            if (menu != null && menu.gameObject.activeInHierarchy && MainGame.Instance.gameState == MainGame.GameState.MainMenu) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(2f);
        menu.OnContinueButtonClicked();
        while (Time.realtimeSinceStartup < deadline + 60f)
        {
            if (!LazyUI.Get<UILoadingOverlay>().IsShown && MainGame.PlayerController != null && MainGame.Instance.gameState == MainGame.GameState.InGame) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(3f);
        string depois = Estado();
        Debug.Log("[cenario] recarregado: " + depois);
        yield return "save " + ms.ToString("0") + " ms ok=" + ok + "\nantes:      " + antes + "\npós-save:   " + depoisDoSave + "\nrecarregado: " + depois;
    }
}
