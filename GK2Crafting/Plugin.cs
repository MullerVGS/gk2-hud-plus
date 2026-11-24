using BepInEx;
using GK2Shared;

namespace GK2Crafting
{
    /// <summary>GK2 Crafting avulso: só o módulo das estações de craft (<see cref="CraftingModule"/>). No GK2 HUD+ ele vem junto com os outros.</summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility(HudPlusId.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = CraftingModule.StandaloneGuid;
        public const string Name = "GK2 Crafting";
        public const string Version = "0.1.0";

        internal ModHost Host { get; private set; }
        internal CraftingModule Module { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: false);
            Module = new CraftingModule(Host);
            Host.Start();
            Logger.LogInfo($"{Name} {Version} carregado");
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
