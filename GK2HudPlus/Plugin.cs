using System.Linq;
using BepInEx;
using GK2Hud;
using GK2HudPlus.Menu;
using GK2Crafting;
using GK2Map;
using GK2Saves;
using GK2Shared;
using GK2ZombieRefund;

namespace GK2HudPlus
{
    /// <summary>
    /// GK2 HUD+: interface (<see cref="HudModule"/>), mapa (<see cref="MapModule"/>), estações de craft
    /// (<see cref="CraftingModule"/>), saves (<see cref="SavesModule"/>) e refund de perk de zumbi
    /// (<see cref="RefundModule"/>) num plugin só, com um .cfg e uma lista de funcionalidades que
    /// ligam e desligam sem reabrir o jogo. Funcionalidades que mudam o estado do jogo começam desligadas.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = HudPlusId.Guid;
        public const string Name = HudPlusId.Name;
        public const string Version = "0.7.0";

        internal ModHost Host { get; private set; }

        /// <summary>Acessados por reflexão nos cenários (docs/cenarios/).</summary>
        internal HudModule Hud { get; private set; }
        internal MapModule Map { get; private set; }
        internal CraftingModule Crafting { get; private set; }
        internal SavesModule Saves { get; private set; }
        internal RefundModule Refund { get; private set; }
        internal ModGuard Guard => Host?.Guard;
        internal MenuPart Menu { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: true);
            Hud = new HudModule(Host);
            Map = new MapModule(Host);
            Crafting = new CraftingModule(Host);
            Saves = new SavesModule(Host);
            Refund = new RefundModule(Host);
            Host.Start();
            Menu = Host.Guard.Feature("Menu", "menu", "menu", h => new MenuPart(h, Host), dispose: m => { m.Dispose(); Menu = null; });
            Logger.LogInfo($"{Name} {Version} carregado: " + string.Join(", ", Host.Features.Where(f => !f.IsOption).Select(f => f.Id + (f.Running ? "" : " (off)"))));
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
