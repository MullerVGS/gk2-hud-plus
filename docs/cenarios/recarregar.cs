using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Volta ao menu e recarrega o slot em jogo do disco (descarta o que não foi salvo; backup do jogo volta ao Steam_1). Só green.
public static class Snippet
{
    public static IEnumerator Run()
    {
        int dayBefore = MainGame.Instance.GameSave.environmentData.Day;
        float todBefore = MainGame.Instance.GameSave.environmentData.TimeOfDay;
        string slot = MainGame.Instance.SaveSlotData?.slotName ?? "Steam_1";
        yield return Slots.Load(slot.Contains("_backup_") ? "Steam_1" : slot);
        if (Slots.Result == null || !Slots.Result.StartsWith("em jogo")) { yield return Slots.Result; yield break; }
        var env = MainGame.Instance.GameSave.environmentData;
        yield return $"antes dia {dayBefore} {todBefore:0.000} → carregado dia {env.Day} {env.TimeOfDay:0.000}";
    }
}
