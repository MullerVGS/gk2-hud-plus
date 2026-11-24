using System;
using System.Collections;
using System.Linq;
using UnityEngine;

// Confere que a janela do HUD+ aberta está realmente reproduzindo o clipe, inclusive os recursos do pacote.
public static class Snippet
{
    public static IEnumerator Run()
    {
        Component player = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
            .FirstOrDefault(c => c != null && c.GetType().FullName == "UnityEngine.Video.VideoPlayer"
                && c.gameObject.activeInHierarchy && c.name == "Preview");
        if (player == null) throw new InvalidOperationException("prévia do HUD+ não encontrada");
        object Read(string name) => player.GetType().GetProperty(name).GetValue(player);
        float deadline = Time.realtimeSinceStartup + 8f;
        while (Time.realtimeSinceStartup < deadline)
        {
            if ((bool)Read("isPrepared") && (bool)Read("isPlaying") && Convert.ToInt64(Read("frame")) > 0)
            {
                yield return "prévia reproduzindo: " + Read("width") + "x" + Read("height");
                yield break;
            }
            yield return null;
        }
        throw new InvalidOperationException("prévia não reproduziu um frame");
    }
}
