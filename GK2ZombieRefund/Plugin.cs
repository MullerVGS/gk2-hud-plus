using BepInEx;
using GK2Shared;

namespace GK2ZombieRefund
{
    /// <summary>GK2 Zombie Refund avulso: só o módulo de refund (<see cref="RefundModule"/>). No GK2 HUD+ ele vem junto com os outros.</summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility(HudPlusId.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = RefundModule.LegacyGuid;
        public const string Name = "GK2 Zombie Refund";
        public const string Version = "0.3.0";

        internal ModHost Host { get; private set; }
        internal RefundModule Module { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: false);
            Module = new RefundModule(Host);
            Host.Start();
            Logger.LogInfo($"{Name} {Version} carregado");
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
