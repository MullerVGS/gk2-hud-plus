using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Abre a lista nativa de saves (menu principal → Load) e lista os slots que o jogo enxerga.
public static class Snippet
{
    public static IEnumerator Run()
    {
        float deadline = Time.realtimeSinceStartup + 60f;
        UIMainMenuWindow menu = null;
        while (Time.realtimeSinceStartup < deadline)
        {
            menu = LazyUI.IsInitialized ? LazyUI.GetWindow<UIMainMenuWindow>() : null;
            if (menu != null && menu.gameObject.activeInHierarchy) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        if (menu == null) { yield return "menu principal não apareceu"; yield break; }
        menu.OnLoadButtonClicked();
        yield return new WaitForSecondsRealtime(1f);
        var sb = new System.Text.StringBuilder();
        foreach (var s in SaveSystem.SaveSlotDataList)
            sb.Append(s.slotName).Append(" dia ").Append(s.day).Append(" ").Append(s.saveDateTime).Append(" | ");
        yield return sb.ToString();
    }
}
