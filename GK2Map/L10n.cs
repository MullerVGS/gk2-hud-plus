using LazyBearTechnology;

namespace GK2Map
{
    /// <summary>Textos próprios do mod, no idioma atual do jogo (en e pt-br).</summary>
    internal static class L10n
    {
        private static bool Pt => LLBase.CurrentLang == "pt-br";

        public static string LookInside => Pt ? "Clique para ver por dentro" : "Click to look inside";
        public static string Making => Pt ? "Fazendo" : "Making";
        public static string Contents => Pt ? "Dentro" : "Inside";
        public static string Outside => Pt ? "Lado de fora" : "Outside";
        public static string DoorEnter => Pt ? "Clique para entrar" : "Click to go in";
        public static string DoorLeave => Pt ? "Clique para sair" : "Click to go out";
        public static string BackToMap => Pt ? "Botão direito: voltar ao mapa" : "Right-click: back to map";
    }
}
