using System;
using System.Collections.Generic;
using GK2Insight.Telemetry;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>
    /// Orquestra as camadas: painel Local (F7), painel Técnico + overlays (Shift+F7). Painéis em uGUI (custo só
    /// quando o texto muda); o overlay GL é o único que ainda passa por OnGUI, e só enquanto a camada técnica está
    /// ligada.
    /// </summary>
    internal class Hud
    {
        private const float RefreshInterval = 0.5f;

        private readonly Panel local = new Panel();
        private readonly Panel tech = new Panel();
        private readonly Overlay overlay = new Overlay();
        private readonly HashSet<string> reportedErrors = new HashSet<string>();
        private readonly SelfCost cost;
        private readonly GameObject canvasObject;
        private readonly GameObject guiObject;
        private readonly GuiHost gui;
        private readonly Font font;
        private readonly PanelView localView, techView;
        private float nextLocal, nextTech;
        private int fontSize;

        public Hud(SelfCost cost, int fontSize)
        {
            this.cost = cost;
            this.fontSize = fontSize;
            canvasObject = new GameObject("GK2Insight.Canvas", typeof(Canvas));
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, fontSize);
            localView = new PanelView(canvasObject.transform, "Local", false, font);
            techView = new PanelView(canvasObject.transform, "Tech", true, font);

            guiObject = new GameObject("GK2Insight.Gui");
            UnityEngine.Object.DontDestroyOnLoad(guiObject);
            gui = guiObject.AddComponent<GuiHost>();
            gui.Draw = DrawOverlay;
            gui.enabled = false;
        }

        public Overlay Overlay => overlay;

        /// <summary>A cada frame: visibilidade, atualização dos textos (2×/s) e do overlay.</summary>
        public void Tick(bool showLocal, bool showTech, float x, float y, int size)
        {
            fontSize = size;
            bool overlayOn = showTech && (overlay.ShowColliders || overlay.ShowPaths || overlay.ShowDocks || overlay.ShowHitboxes);
            if (gui.enabled != overlayOn) gui.enabled = overlayOn;
            if (!showLocal) localView.Hide();
            if (!showTech) techView.Hide();
            if (!showLocal && !showTech) return;

            MainGame game = MainGame.Instance;
            PlayerData player = MainGame.PlayerData;
            bool inGame = game != null && player != null && game.gameState == MainGame.GameState.InGame && game.GameSave != null;
            if (!inGame)
            {
                localView.Hide();
                techView.Hide();
                return;
            }
            Vector3 playerPos = MainGame.PlayerController != null ? MainGame.PlayerController.transform.position : Vector3.zero;
            if (showLocal && Time.unscaledTime >= nextLocal)
            {
                nextLocal = Time.unscaledTime + RefreshInterval;
                long t0 = SelfCost.Now();
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
                localView.Set(local.Text, fontSize, x, y, Screen.height - y - 110f);
                cost?.Stop(SelfCost.Part.Local, t0);
            }
            if (showTech)
            {
                long t1 = SelfCost.Now();
                try
                {
                    if (overlay.Refresh()) cost?.Stop(SelfCost.Part.Overlay, t1);
                }
                catch (Exception e)
                {
                    Report("overlay", e);
                }
                if (Time.unscaledTime >= nextTech)
                {
                    nextTech = Time.unscaledTime + RefreshInterval;
                    long t2 = SelfCost.Now();
                    tech.Begin();
                    Safe(tech, "técnico", () => TechSection.Write(tech, overlay, player));
                    tech.End();
                    techView.Set(tech.Text, fontSize, Screen.width - 12f, 80f, Screen.height - 160f);
                    cost?.Stop(SelfCost.Part.Tech, t2);
                }
            }
        }

        private void DrawOverlay()
        {
            long t0 = SelfCost.Now();
            try
            {
                if (Event.current.type == EventType.Repaint)
                {
                    overlay.Masks.Clear();
                    if (localView.TryGetScreenRect(out Rect a)) overlay.Masks.Add(a);
                    if (techView.TryGetScreenRect(out Rect b)) overlay.Masks.Add(b);
                }
                overlay.Draw();
            }
            catch (Exception e)
            {
                Report("desenho", e);
            }
            cost?.Stop(SelfCost.Part.OnGui, t0);
        }

        public void Dispose()
        {
            overlay.Dispose();
            UnityEngine.Object.Destroy(guiObject);
            UnityEngine.Object.Destroy(canvasObject);
            UnityEngine.Object.Destroy(font);
        }

        private void Safe(Panel p, string section, Action body)
        {
            try
            {
                body();
            }
            catch (Exception e)
            {
                p.Line(Panel.Dim($" ({section}: {e.GetType().Name}: {e.Message})"));
                Report(section, e);
            }
        }

        private void Report(string section, Exception e)
        {
            if (reportedErrors.Add(section + e.Message)) Plugin.Log.LogWarning($"seção {section}: {e}");
        }
    }
}
