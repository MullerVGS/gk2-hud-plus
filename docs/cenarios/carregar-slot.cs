using System.Collections;

// Carrega o save do slot SLOT (Steam_1 = cópia do save do blue; Steam_2 = jogo novo de gravação), do menu ou de dentro
// do jogo, sem salvar o que está aberto.
public static class Snippet
{
    static string SLOT = "Steam_1";
    static bool SE_PRECISO = false;   // true: não recarrega se o jogo já está nesse slot

    public static IEnumerator Run()
    {
        if (SE_PRECISO && Slots.InGame && MainGame.Instance.SaveSlotData?.slotName == SLOT) { yield return "já em " + SLOT; yield break; }
        yield return Slots.Load(SLOT);
        yield return Slots.Result;
    }
}
