using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Volta ao menu e carrega o último save (descarta o que não foi salvo), como sair e clicar "Continue". Só green.
public static class Snippet
{
    public static IEnumerator Run()
    {
        int dayBefore = MainGame.Instance.GameSave.environmentData.Day;
        float todBefore = MainGame.Instance.GameSave.environmentData.TimeOfDay;
        MainGame.Instance.GoToMenu();
        float deadline = Time.realtimeSinceStartup + 90f;
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = LazyUI.IsInitialized ? LazyUI.GetWindow<UIMainMenuWindow>() : null;
            if (menu != null && menu.gameObject.activeInHierarchy && !LazyUI.Get<UILoadingOverlay>().IsShown) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (menu == null) { yield return "menu principal não apareceu"; yield break; }
        yield return new WaitForSecondsRealtime(1f);
        // MainGame.GoToMenu() deixa PlayerData preenchido: o continuar.cs acharia que já está em jogo.
        menu.OnContinueButtonClicked();
        yield return new WaitForSecondsRealtime(2f);
        while (Time.realtimeSinceStartup < deadline)
        {
            bool loading = LazyUI.Get<UILoadingOverlay>().IsShown;
            if (!loading && MainGame.PlayerData != null && MainGame.PlayerController != null) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(3f);
        var env = MainGame.Instance.GameSave.environmentData;
        yield return $"antes dia {dayBefore} {todBefore:0.000} → carregado dia {env.Day} {env.TimeOfDay:0.000}";
    }
}
