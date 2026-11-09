using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// GK2Saves: salva VEZES vezes pelo botão Save do ESC (1,5 s entre eles) e abre a janela Load para conferir a lista.
public static class Snippet
{
    const int VEZES = 1;

    public static IEnumerator Run()
    {
        var saves = LazyUI.GetWindow<UISaveSlotsWindow>();
        if (saves.IsShown) saves.Close();
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        for (int i = 0; i < VEZES; i++)
        {
            if (pause.IsShown) pause.Close();
            yield return new WaitForSecondsRealtime(0.3f);
            pause.Open(null);
            yield return new WaitForSecondsRealtime(0.3f);
            var b = GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Save").GetComponent<LazyButton>();
            if (!b.interactable) { yield return "Save desabilitado na volta " + i; yield break; }
            b.onClick.Invoke();
            yield return new WaitForSecondsRealtime(1.5f);
        }
        pause.Open(null);
        yield return new WaitForSecondsRealtime(0.3f);
        GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Load").GetComponent<LazyButton>().onClick.Invoke();
        yield return new WaitForSecondsRealtime(1f);
        var sb = new System.Text.StringBuilder();
        foreach (var s in saves.GetComponentsInChildren<UISaveSlot>()) if (s.LinkedSaveSlot != null) sb.Append(s.LinkedSaveSlot.slotName).Append(" ").Append(s.LinkedSaveSlot.saveDateTime).Append(" | ");
        yield return sb.ToString();
    }
}
