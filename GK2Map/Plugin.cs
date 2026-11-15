using BepInEx;
using GK2Shared;

namespace GK2Map
{
    /// <summary>GK2 Map avulso: só o módulo de mapa (<see cref="MapModule"/>). No GK2 HUD+ ele vem junto com os outros.</summary>
    [BepInPlugin(Guid, Name, Version)]
    [BepInIncompatibility(HudPlusId.Guid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = MapModule.LegacyGuid;
        public const string Name = "GK2 Map";
        public const string Version = "0.3.0";

        internal ModHost Host { get; private set; }
        internal MapModule Module { get; private set; }

        private void Awake()
        {
            Host = new ModHost(this, Logger, Guid, Name, bundle: false);
            Module = new MapModule(Host);
            Host.Start();
            Logger.LogInfo($"{Name} {Version} carregado");
        }

        private void Update() => Host.Update();

        private void OnDestroy() => Host?.Dispose();
    }
}
