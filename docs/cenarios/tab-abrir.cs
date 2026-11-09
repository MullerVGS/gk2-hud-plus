using LazyBearTechnology;
using UnityEngine;

// Abre a janela do TAB (CharacterWindow) na página PAGINA, como o jogo faz; "fechar" fecha.
public static class Snippet
{
    static string PAGINA = "fechar";   // Main | TechTree | Inspiration | QuestTree | Map | fechar

    public static object Run()
    {
        var w = LazyUI.GetWindow<CharacterWindow>();
        if (PAGINA == "fechar") { if (w != null && w.IsShown) w.Close(); return "fechada"; }
        var page = (CharacterWindowData.CharPage)System.Enum.Parse(typeof(CharacterWindowData.CharPage), PAGINA);
        return Abrir(w, page);
    }

    static System.Collections.IEnumerator Abrir(CharacterWindow w, CharacterWindowData.CharPage page)
    {
        // O construtor de CharacterWindowData grava a página em PlayerData.lastOpenedPage: construir com a janela aberta
        // deixa a página antiga visível. Fecha antes e esconde as páginas que sobraram.
        if (w.IsShown) { w.Close(); yield return null; yield return null; }
        Transform pages = w.transform.Find("Content/Pages");
        if (pages != null) foreach (Transform p in pages) if (p.GetComponent<LazyWidgetBase>() != null) p.gameObject.SetActive(false);
        w.Open(new CharacterWindowData(MainGame.Instance.GameSave, page));
        yield return null;
        yield return "aberta " + page;
    }
}
