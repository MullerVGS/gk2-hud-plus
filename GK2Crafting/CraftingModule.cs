using BepInEx.Logging;
using GK2Shared;

namespace GK2Crafting
{
    /// <summary>
    /// Atalhos nas janelas das estações de craft. Por ora, o Folio na mesa de alquimia (<see cref="FolioButton"/>):
    /// uma célula com o ícone do Folio no painel de ingredientes abre a janela nativa do Folio por cima da mesa.
    /// </summary>
    internal sealed class CraftingModule
    {
        public const string StandaloneGuid = "mullervgs.gk2.crafting";

        internal static CraftingModule Instance;
        internal static ManualLogSource Log;

        internal readonly ModHost Host;

        public CraftingModule(ModHost host)
        {
            Host = host;
            Instance = this;
            Log = host.Log;
            host.Module("crafting", null, () => { if (Instance == this) Instance = null; });
            host.Feature("FolioButton", "crafting", "Alchemy", "FolioButton",
                new Loc("Folio at the alchemy table", "Folio na mesa de alquimia"),
                new Loc("Open the Folio from the alchemy table window, once the Folio is built.",
                    "Abre o Folio pela janela da mesa de alquimia, depois que o Folio foi construído."),
                h => new FolioButton(h), dispose: b => b.Dispose());
        }

        /// <summary>Erro fora dos patches (clique, hover): conta para a parte e, repetido, desliga a funcionalidade.</summary>
        internal static void Fail(string part, System.Exception e, string where) => Instance?.Host.Guard.Report(part, e, where);
    }
}
