using System.Collections;
using System.Linq;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Conduz a introdução de um jogo novo no green (o green não recebe teclado): escolhe a primeira resposta disponível de
// cada diálogo até o jogador ficar livre por LIVRE segundos seguidos, ou até SEGUNDOS. Devolve as respostas escolhidas.
public static class Snippet
{
    static float SEGUNDOS = 240f;
    static float LIVRE = 12f;

    public static IEnumerator Run()
    {
        var sb = new StringBuilder();
        float end = Time.realtimeSinceStartup + SEGUNDOS, freeSince = -1f;
        int n = 0;
        while (Time.realtimeSinceStartup < end)
        {
            if (UIMultiAnswer.IsShowing)
            {
                freeSince = -1f;
                var opt = Object.FindObjectsByType<UIMultiAnswerOption>(FindObjectsSortMode.None)
                    .FirstOrDefault(o => o.isActiveAndEnabled && o.Available && o.AcceptsChoice);
                if (opt != null)
                {
                    yield return new WaitForSecondsRealtime(0.6f);
                    if (opt != null && opt.isActiveAndEnabled)
                    {
                        sb.Append(opt.AnswerId).Append(" | ");
                        opt.OnItemPress();
                        n++;
                    }
                }
            }
            else
            {
                var pc = MainGame.PlayerController;
                var cine = LazyUI.Get<UICinematic>();
                bool free = pc != null && Slots.InGame && LazyWindowsStackController.ActiveWindow == null && pc.IsControlsEnabled
                    && pc.Ssm.CurState is FreePlayerState && (cine == null || !cine.gameObject.activeSelf);
                if (free && freeSince < 0f) freeSince = Time.realtimeSinceStartup;
                if (!free) freeSince = -1f;
                if (freeSince > 0f && Time.realtimeSinceStartup - freeSince > LIVRE) break;
            }
            yield return new WaitForSecondsRealtime(0.4f);
        }
        var p2 = MainGame.PlayerController;
        yield return n + " respostas: " + sb + (freeSince > 0f ? " · jogador livre" : " · tempo esgotado") + " · estado " + p2?.Ssm.CurState?.GetType().Name + " · controles " + p2?.IsControlsEnabled;
    }
}
