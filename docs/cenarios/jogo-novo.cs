using System.Collections;
using System.Linq;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Começa um jogo novo no green (o botão New Game do menu, num slot novo; o Steam_1 fica intacto) e espera chegar ao jogo.
// Devolve onde parou: cena, zona, diálogo/cinemática abertos, quests ativas.
public static class Snippet
{
    public static IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 240f;
        if (MainGame.PlayerData != null) MainGame.Instance.GoToMenu();
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = LazyUI.IsInitialized ? LazyUI.GetWindow<UIMainMenuWindow>() : null;
            if (menu != null && menu.gameObject.activeInHierarchy && MainGame.Instance.gameState == MainGame.GameState.MainMenu && !LazyUI.Get<UILoadingOverlay>().IsShown) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(1f);
        menu.OnStartNewGameButtonClicked();
        yield return new WaitForSecondsRealtime(3f);
        while (Time.realtimeSinceStartup < deadline && !Slots.InGame) yield return new WaitForSecondsRealtime(0.5f);
        yield return new WaitForSecondsRealtime(4f);
        var sb = new StringBuilder();
        sb.Append("slot " + MainGame.Instance.SaveSlotData?.slotName + " · estado " + MainGame.Instance.gameState);
        sb.Append(" · dia " + MainGame.Instance.GameSave?.environmentData.Day);
        sb.Append(" · janelas: " + string.Join(",", Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(m => m.GetType().Name.StartsWith("UI") && m.GetType().Name.EndsWith("Window") && m.gameObject.activeInHierarchy).Select(m => m.GetType().Name).Distinct()));
        yield return sb.ToString();
    }
}
