using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Fecha as janelas que os cenários abrem (personagem/TAB, zumbi, saves, diálogo, Settings, Mods, menu do GK2 HUD+,
// pausa) e volta ao jogo livre.
public static class Snippet
{
    public static IEnumerator Run()
    {
        int n = 0;
        var dialog = LazyUI.GetWindow<UIDialogWindow>(); if (dialog != null && dialog.IsShown) { dialog.Close(); n++; }
        var saves = LazyUI.GetWindow<UISaveSlotsWindow>(); if (saves != null && saves.IsShown) { saves.Close(); n++; }
        var zombie = LazyUI.GetWindow<UIZombieWorkerWindow>(); if (zombie != null && zombie.IsShown) { zombie.Close(); n++; }
        var character = LazyUI.GetWindow<CharacterWindow>(); if (character != null && character.IsShown) { character.Close(); n++; }
        var settings = LazyUI.GetWindow<UIGameSettingsWindow>(); if (settings != null && settings.IsShown) { settings.CloseWithoutCallback(); n++; }
        foreach (var w in Object.FindObjectsByType<UICodeModsWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (w.IsShown) { w.Close(); n++; }
        foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (mb.GetType().Name != "HudPlusWindow") continue;
            var shown = mb.GetType().GetProperty("IsShown"); if (shown == null || !(bool)shown.GetValue(mb)) continue;
            mb.GetType().GetMethod("CloseWithoutCallback").Invoke(mb, null); n++;
        }
        var pause = LazyUI.GetWindow<UIGamePauseWindow>(); if (pause != null && pause.IsShown) { pause.Close(); n++; }
        yield return new WaitForSecondsRealtime(0.4f);
        yield return "fechadas " + n;
    }
}
