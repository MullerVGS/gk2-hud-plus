using System.Collections;
using UnityEngine;

// Muda a hora do dia no save de teste (0..1; 0.445 = logo antes da rolagem de atividade dos NPCs às 0.45).
public static class Snippet
{
    public static IEnumerator Run()
    {
        const float timeOfDay = 0.445f;
        EnvironmentEngine.Instance.SetTimeOfDay(timeOfDay);
        Debug.Log("[cenario] hora do dia = " + timeOfDay);
        yield return new WaitForSecondsRealtime(8f);
        yield return "agora " + MainGame.Instance.GameSave.environmentData.TimeOfDay.ToString("0.000");
    }
}
