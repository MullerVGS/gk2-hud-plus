using LazyBearTechnology;

// Troca o idioma do jogo em memória (sem gravar configurações) — só no green, para ver textos em outra língua. Volte com LANG = "en".
public static class Snippet
{
    const string LANG = "pt-br";

    public static object Run()
    {
        LLBase.LoadLanguageResource(LANG);
        return "idioma: " + LLBase.CurrentLang + " | ui_crafted_at = " + LLBase.L("ui_crafted_at") + " | energy = " + LLBase.L("energy") + " | insanity = " + LLBase.L("insanity");
    }
}
