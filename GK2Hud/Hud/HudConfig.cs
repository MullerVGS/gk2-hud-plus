using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace GK2Hud.Hud
{
    public enum HudNumbersMode
    {
        Hover,
        Always,
        Off,
    }

    internal static class HudConfig
    {
        private static readonly ConfigDefinition OldNumbers = new ConfigDefinition("Hud", "EnergyInsanityNumbers");
        private static readonly ConfigDefinition NumbersMode = new ConfigDefinition("Hud", "EnergyInsanityNumbersMode");

        /// <summary>
        /// <c>EnergyInsanityNumbersMode</c> substitui o booleano <c>EnergyInsanityNumbers</c> da 0.2.0: quem tinha
        /// <c>false</c> fica com <c>Off</c>; <c>true</c> vira o novo padrão (<c>Hover</c>). A chave antiga sai do arquivo.
        /// </summary>
        public static ConfigEntry<HudNumbersMode> BindNumbersMode(ConfigFile cfg)
        {
            Dictionary<ConfigDefinition, string> orphans = null;
            try { orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(cfg, null) as Dictionary<ConfigDefinition, string>; }
            catch (Exception) { }
            bool hadMode = orphans != null && orphans.ContainsKey(NumbersMode);
            string old = null;
            bool hadOld = orphans != null && orphans.TryGetValue(OldNumbers, out old);

            ConfigEntry<HudNumbersMode> entry = cfg.Bind(NumbersMode, HudNumbersMode.Hover, new ConfigDescription(
                "Números de energia e insanidade dentro da barra. Hover: aparecem com o mouse sobre a barra ou os ícones; Always: sempre; Off: nunca."));
            if (hadOld)
            {
                if (!hadMode && string.Equals(old?.Trim(), "false", StringComparison.OrdinalIgnoreCase)) entry.Value = HudNumbersMode.Off;
                orphans.Remove(OldNumbers);
                cfg.Save();
                Plugin.Log.LogInfo($"config: EnergyInsanityNumbers={old} migrado para EnergyInsanityNumbersMode={entry.Value}");
            }
            return entry;
        }
    }
}
