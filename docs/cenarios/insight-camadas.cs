using BepInEx.Bootstrap;
using BepInEx.Configuration;

// Liga/desliga as camadas do GK2 Insight sem teclado (o green não recebe teclas). Edite os valores abaixo.
public static class Snippet
{
    public static object Run()
    {
        ConfigFile cfg = Chainloader.PluginInfos["arthur.gk2.insight"].Instance.Config;
        ((ConfigEntry<bool>)cfg[new ConfigDefinition("Local", "Visible")]).Value = true;
        ((ConfigEntry<bool>)cfg[new ConfigDefinition("Tech", "Visible")]).Value = true;
        return "camadas: local + técnica";
    }
}
