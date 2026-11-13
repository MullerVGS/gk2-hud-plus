using BepInEx.Bootstrap;
using BepInEx.Configuration;

// Liga/desliga as camadas do GK2 Insight sem teclado (o green não recebe teclas). LOCAL = F7, TECH = Shift+F7.
public static class Snippet
{
    const bool LOCAL = true;
    const bool TECH = true;

    public static object Run()
    {
        ConfigFile cfg = Chainloader.PluginInfos["arthur.gk2.insight"].Instance.Config;
        ((ConfigEntry<bool>)cfg[new ConfigDefinition("Local", "Visible")]).Value = LOCAL;
        ((ConfigEntry<bool>)cfg[new ConfigDefinition("Tech", "Visible")]).Value = TECH;
        return "camadas: local " + (LOCAL ? "ligada" : "desligada") + ", técnica " + (TECH ? "ligada" : "desligada");
    }
}
