using System;
using System.Collections.Generic;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>Orquestra as camadas: painel Local (F7), painel Técnico + overlays (Shift+F7).</summary>
    internal class Hud
    {
        private const float RefreshInterval = 0.5f;

        private readonly Panel local = new Panel();
        private readonly Panel tech = new Panel();
        private readonly Overlay overlay = new Overlay();
        private readonly HashSet<string> reportedErrors = new HashSet<string>();
        private float nextLocal, nextTech;

        public Overlay Overlay => overlay;

        public void Refresh(bool showLocal, bool showTech)
        {
            MainGame game = MainGame.Instance;
            PlayerData player = MainGame.PlayerData;
            bool inGame = game != null && player != null && game.gameState == MainGame.GameState.InGame && game.GameSave != null;
            if (!inGame)
            {
                local.Begin(); local.End();
                tech.Begin(); tech.End();
                return;
            }
            Vector3 playerPos = MainGame.PlayerController != null ? MainGame.PlayerController.transform.position : Vector3.zero;
            if (showLocal && Time.unscaledTime >= nextLocal)
            {
                nextLocal = Time.unscaledTime + RefreshInterval;
                local.Begin();
                Safe(local, "tempo", () => TimeSection.Write(local, game, player));
                Safe(local, "clima", () => LocalSection.WriteWeather(local, game));
                Safe(local, "zona", () => LocalSection.WriteZone(local, game, player));
                Safe(local, "crafts", () => LocalSection.WriteCrafts(local, game, player));
                Safe(local, "npcs", () => PeopleSection.WriteNpcs(local, game, playerPos));
                Safe(local, "zumbis", () => PeopleSection.WriteZombies(local, game, playerPos));
                Safe(local, "chão", () => LocalSection.WriteDrops(local, game));
                if (Time.unscaledTime - Dumper.LastStatusTime < 10f) local.Line("\n" + Panel.Col("#80d0ff", Dumper.LastStatus));
                local.End();
            }
            if (showTech)
            {
                Safe(tech, "overlay", overlay.Refresh);
                if (Time.unscaledTime >= nextTech)
                {
                    nextTech = Time.unscaledTime + RefreshInterval;
                    tech.Begin();
                    Safe(tech, "técnico", () => TechSection.Write(tech, overlay, player));
                    tech.End();
                }
            }
        }

        public void Draw(bool showLocal, bool showTech, float x, float y, int fontSize)
        {
            if (showTech)
            {
                Safe(null, "desenho", overlay.Draw);
                tech.Draw(Screen.width - 12f, 80f, fontSize, Screen.height - 160f, alignRight: true);
            }
            if (showLocal)
            {
                local.Draw(x, y, fontSize, Screen.height - y - 110f);
            }
        }

        public void Dispose() => overlay.Dispose();

        private void Safe(Panel p, string section, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                p?.Line(Panel.Dim($" ({section}: {e.GetType().Name}: {e.Message})"));
                if (reportedErrors.Add(section + e.Message)) Plugin.Log.LogWarning($"seção {section}: {e}");
            }
        }
    }
}
