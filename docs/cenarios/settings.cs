using LazyBearTechnology;

// Abre a janela nativa de Settings (a mesma do botão do menu de pausa), para print e `gk2 ui UIGameSettingsWindow`.
public static class Snippet
{
    public static object Run()
    {
        UIGameSettingsWindow w = LazyUI.GetWindow<UIGameSettingsWindow>();
        if (w == null) return "janela não encontrada";
        w.Open(null);
        return "settings aberta";
    }
}
