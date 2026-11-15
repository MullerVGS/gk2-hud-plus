using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Do menu principal até o jogo carregado no save de teste (equivale a clicar em "Continue").
public static class Snippet
{
    public static IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 90f;
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            if (MainGame.PlayerData != null && MainGame.PlayerController != null)
            {
                yield return "já estava em jogo";
                yield break;
            }
            menu = LazyUI.IsInitialized ? LazyUI.GetWindow<UIMainMenuWindow>() : null;
            if (menu != null && menu.gameObject.activeInHierarchy) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (menu == null) { yield return "menu principal não apareceu"; yield break; }
        // O Continue pegaria o save mais recente; o green tem slots de gravação, então carrega Steam_1 pelo nome.
        yield return Slots.Load("Steam_1", fromGame: false);
        yield return Slots.Result != null && Slots.Result.StartsWith("em jogo") ? "em jogo" : Slots.Result;
    }
}
