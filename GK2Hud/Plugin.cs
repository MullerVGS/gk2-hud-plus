using BepInEx;
using GK2Shared;

namespace GK2Hud
{
    /// <summary>GK2 Hud avulso: só o módulo de interface (<see cref="HudModule"/>). No GK2 HUD+ ele vem junto com os outros.</summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility(HudPlusId.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = HudModule.LegacyGuid;
        public const string Name = "GK2 Hud";
        public const string Version = "0.11.2";

        internal ModHost Host { get; private set; }
        internal HudModule Module { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: false);
            Module = new HudModule(Host);
            Host.Start();
            Logger.LogInfo($"{Name} {Version} carregado");
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
