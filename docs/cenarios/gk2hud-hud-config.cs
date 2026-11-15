using BepInEx.Bootstrap;
using BepInEx.Configuration;

// GK2Hud: muda uma opção da seção [Hud] em memória (vale no próximo tick, ≤0,25 s). CHAVE/VALOR abaixo; volte ao padrão depois.
public static class Snippet
{
    const string CHAVE = "EnergyInsanityShowMax";
    const string VALOR = "true";

    public static object Run()
    {
        ConfigFile cfg = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.Config;
        ConfigEntryBase e = cfg[new ConfigDefinition("Hud", CHAVE)];
        e.SetSerializedValue(VALOR);
        return CHAVE + " = " + e.GetSerializedValue();
    }
}
