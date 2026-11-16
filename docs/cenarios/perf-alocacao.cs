using System.Collections;
using UnityEngine;

// Taxa de alocação do heap gerenciado (soma dos aumentos de GC.GetTotalMemory quadro a quadro, ignorando as quedas das
// coletas) em SEGUNDOS segundos. Cada coleta do Mono com heap de ~500 MB trava o jogo; menos alocação = menos travadas.
public static class Snippet
{
    static float SEGUNDOS = 20f;

    public static IEnumerator Run()
    {
        long last = System.GC.GetTotalMemory(false);
        long allocated = 0;
        int gc0 = System.GC.CollectionCount(0);
        float t0 = Time.realtimeSinceStartup;
        int frames = 0;
        while (Time.realtimeSinceStartup - t0 < SEGUNDOS)
        {
            yield return null;
            frames++;
            long now = System.GC.GetTotalMemory(false);
            if (now > last) allocated += now - last;
            last = now;
        }
        float s = Time.realtimeSinceStartup - t0;
        yield return $"aloca {allocated / s / 1024:0} KB/s · {allocated / (double)frames / 1024:0.0} KB/frame · {frames / s:0} fps · coletas {System.GC.CollectionCount(0) - gc0} em {s:0} s";
    }
}
