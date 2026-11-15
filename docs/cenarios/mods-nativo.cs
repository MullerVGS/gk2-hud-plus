using LazyBearTechnology;

// Abre a janela nativa de Mods do jogo (a do botão Mods do menu principal), referência de lista com seções e rolagem.
public static class Snippet
{
    public static object Run()
    {
        foreach (var w in UnityEngine.Object.FindObjectsByType<UIGameSettingsWindow>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None))
            if (w.IsShown) w.Close();
        UICodeModsWindow.OpenWindow(null);
        return "mods aberta";
    }
}
