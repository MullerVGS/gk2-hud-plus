using BepInEx;
using GK2Shared;

namespace GK2Saves
{
    /// <summary>GK2 Saves avulso: só o módulo de saves (<see cref="SavesModule"/>). No GK2 HUD+ ele vem junto com os outros.</summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility(HudPlusId.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = SavesModule.LegacyGuid;
        public const string Name = "GK2 Saves";
        public const string Version = "0.2.0";

        internal ModHost Host { get; private set; }
        internal SavesModule Module { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: false);
            Module = new SavesModule(Host);
            Host.Start();
            Logger.LogInfo($"{Name} {Version} carregado");
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
