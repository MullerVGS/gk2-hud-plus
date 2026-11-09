using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Abre o menu de pausa (ESC) em jogo, fechando antes se já estiver aberto (para os mods reagirem ao Open).
public static class Snippet
{
    public static IEnumerator Run()
    {
        var w = LazyUI.GetWindow<UIGamePauseWindow>();
        if (w.IsShown) { w.Close(); yield return new WaitForSecondsRealtime(0.5f); }
        w.Open(null);
        yield return new WaitForSecondsRealtime(0.8f);
        yield return "pausa aberta";
    }
}
