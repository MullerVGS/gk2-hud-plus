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
        Debug.Log("[cenario] continuar: clicando Continue");
        menu.OnContinueButtonClicked();
        while (Time.realtimeSinceStartup < deadline)
        {
            bool loading = LazyUI.Get<UILoadingOverlay>().IsShown;
            if (!loading && MainGame.PlayerData != null && MainGame.PlayerController != null) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(2f);
        yield return MainGame.PlayerData != null ? "em jogo" : "tempo esgotado carregando";
    }
}
