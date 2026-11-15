using System.Collections;
using System.Linq;
using LazyBearTechnology;
using UnityEngine;

/// <summary>
/// Carrega um save pelo nome do slot (o "Continue" do jogo pega o mais recente, e o green tem mais de um: Steam_1 é a
/// cópia do save do blue, os outros são de gravação). Mesmo caminho do Continue: menu principal, tela de carregamento,
/// SaveSystem.Load e MainGame.ContinueGame.
/// </summary>
public static class Slots
{
    public static string Result;

    public static bool InGame => MainGame.PlayerData != null && MainGame.PlayerController != null && !LazyUI.Get<UILoadingOverlay>().IsShown;

    public static IEnumerator Load(string slotName, bool fromGame = true)
    {
        Result = null;
        float deadline = Time.realtimeSinceStartup + 120f;
        if (MainGame.PlayerData != null && fromGame) MainGame.Instance.GoToMenu();
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = LazyUI.IsInitialized ? LazyUI.GetWindow<UIMainMenuWindow>() : null;
            if (menu != null && menu.gameObject.activeInHierarchy && MainGame.Instance.gameState == MainGame.GameState.MainMenu
                && !LazyUI.Get<UILoadingOverlay>().IsShown) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (menu == null) { Result = "menu principal não apareceu"; yield break; }
        yield return new WaitForSecondsRealtime(1f);
        SaveSlotData slot = SaveSystem.SaveSlotDataList.FirstOrDefault(s => s.slotName == slotName);
        if (slot == null) { Result = "slot " + slotName + " não existe (" + string.Join(", ", SaveSystem.SaveSlotDataList.Select(s => s.slotName)) + ")"; yield break; }
        bool failed = false;
        UILoadingOverlay overlay = LazyUI.Get<UILoadingOverlay>();
        overlay.Draw(new LoadingWindowData(MainGame.EntrySceneToLoad, () =>
            SaveSystem.Load(slot, save =>
            {
                if (save == null) { failed = true; overlay.Hide(); menu.Open(null); return; }
                MainGame.Instance.ContinueGame(slot, save);
                menu.Close();
            })));
        yield return new WaitForSecondsRealtime(2f);
        while (Time.realtimeSinceStartup < deadline && !failed && !InGame) yield return new WaitForSecondsRealtime(0.5f);
        yield return new WaitForSecondsRealtime(2f);
        var env = MainGame.Instance.GameSave?.environmentData;
        Result = failed ? "falha ao ler " + slotName : InGame ? "em jogo · " + slotName + " · dia " + env?.Day + " " + env?.TimeOfDay.ToString("0.000") : "tempo esgotado carregando " + slotName;
    }
}
