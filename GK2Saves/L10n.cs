using System.Collections.Generic;
using LazyBearTechnology;

namespace GK2Saves
{
    /// <summary>Textos próprios do mod (os botões usam os textos nativos <c>ui_save</c> e <c>ui_load_save_btn</c>).</summary>
    internal static class L10n
    {
        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["save_failed"] = "Save failed (see log)",
            ["load_failed"] = "Could not load this save",
            ["load_confirm"] = "Load this save?\nProgress since your last save will be lost.",
            ["label_current"] = "Current",
            ["label_backup"] = "Backup {0}",
            ["label_manual"] = "Manual",
            ["label_sleep"] = "Sleep",
            ["block_fight"] = "Can't save during a fight",
            ["block_flow"] = "Can't save during a dialogue or cutscene",
            ["block_sleep"] = "Can't save while sleeping",
            ["block_scene"] = "Can't save while changing area",
            ["block_carry"] = "Put down what you're carrying first",
            ["block_busy"] = "Finish what you're doing first",
            ["block_other"] = "Can't save right now",
        };

        private static readonly Dictionary<string, string> PtBr = new Dictionary<string, string>
        {
            ["save_failed"] = "Falha ao salvar (veja o log)",
            ["load_failed"] = "Não foi possível carregar este save",
            ["load_confirm"] = "Carregar este save?\nO progresso desde o último save será perdido.",
            ["label_current"] = "Atual",
            ["label_backup"] = "Backup {0}",
            ["label_manual"] = "Manual",
            ["label_sleep"] = "Ao dormir",
            ["block_fight"] = "Não dá para salvar durante uma luta",
            ["block_flow"] = "Não dá para salvar durante diálogo ou cena",
            ["block_sleep"] = "Não dá para salvar dormindo",
            ["block_scene"] = "Não dá para salvar trocando de área",
            ["block_carry"] = "Largue o que está carregando primeiro",
            ["block_busy"] = "Termine o que está fazendo primeiro",
            ["block_other"] = "Não dá para salvar agora",
        };

        public static string T(string key)
        {
            Dictionary<string, string> table = LLBase.CurrentLang == "pt-br" ? PtBr : En;
            if (table.TryGetValue(key, out string s)) return s;
            return En.TryGetValue(key, out s) ? s : key;
        }
    }
}
