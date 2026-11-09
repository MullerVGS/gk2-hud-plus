using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Clica num botão do GK2Saves no menu de pausa (abre o ESC antes). BOTAO = "Save" ou "Load".
public static class Snippet
{
    const string BOTAO = "Load";

    public static IEnumerator Run()
    {
        var saves = LazyUI.GetWindow<UISaveSlotsWindow>();
        if (saves.IsShown) saves.Close();
        var w = LazyUI.GetWindow<UIGamePauseWindow>();
        if (w.IsShown) { w.Close(); yield return new WaitForSecondsRealtime(0.4f); }
        w.Open(null);
        yield return new WaitForSecondsRealtime(0.6f);
        var go = GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_" + BOTAO);
        if (go == null) { yield return "botão GK2Saves_" + BOTAO + " não encontrado"; yield break; }
        var b = go.GetComponent<LazyButton>();
        if (!b.interactable) { yield return "botão desabilitado"; yield break; }
        b.onClick.Invoke();
        yield return new WaitForSecondsRealtime(1.2f);
        yield return "clicado " + BOTAO;
    }
}
