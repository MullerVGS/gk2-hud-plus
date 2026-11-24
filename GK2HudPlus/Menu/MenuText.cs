using System.Collections.Generic;
using GK2Shared;

namespace GK2HudPlus.Menu
{
    /// <summary>Textos próprios do menu (o resto vem do jogo: On/Off, OK, X).</summary>
    internal static class MenuText
    {
        public static readonly Loc Button = new Loc("GK2 HUD+", "GK2 HUD+");
        public static readonly Loc Title = new Loc("GK2 HUD+", "GK2 HUD+");
        public static readonly Loc Live = new Loc("Changes apply right away.", "As mudanças valem na hora.");
        public static readonly Loc GameplayHint = new Loc("These change your save.", "Estas mudam o seu save.");
        public static readonly Loc Failed = new Loc("Stopped after an error. Turn it off and on to retry.",
            "Parou após um erro. Desligue e ligue para tentar de novo.");

        private static readonly Dictionary<string, Loc> Categories = new Dictionary<string, Loc>
        {
            ["tooltips"] = new Loc("Tooltips", "Tooltips"),
            ["hud"] = new Loc("HUD", "HUD"),
            ["calendar"] = new Loc("Calendar", "Calendário"),
            ["map"] = new Loc("Map", "Mapa"),
            ["crafting"] = new Loc("Crafting", "Criação"),
            ["gameplay"] = new Loc("Gameplay", "Jogabilidade"),
        };

        public static string Category(string id) => Categories.TryGetValue(id, out Loc l) ? l.Current : id;
    }
}
