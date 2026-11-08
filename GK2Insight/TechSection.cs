using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

namespace GK2Insight
{
    /// <summary>Painel técnico (Shift+F7): legenda e contagens dos overlays, câmera, objetos carregados e recursos crus.</summary>
    internal static class TechSection
    {
        private static readonly AccessTools.FieldRef<PlayerData, GameRes> PlayerRes =
            AccessTools.FieldRefAccess<PlayerData, GameRes>("res");

        public static void Write(Panel p, Overlay overlay, PlayerData player)
        {
            p.Section("Técnico", $"overlays num raio de {overlay.Radius:0} m");
            List<string[]> legend = new List<string[]>
            {
                new[] { Swatch(Overlay.ObstacleColor), "obstáculo / parede invisível", Count(overlay.ShowColliders, overlay.ColliderCount, "colisores") },
                new[] { Swatch(Overlay.InteractColor), "interação / drop", "" },
                new[] { Swatch(Overlay.PlayerColor), "jogador", "" },
                new[] { Swatch(Overlay.OtherColor), "outros (degrau, lutador, buff)", Panel.Dim("trigger = mais fraco") },
                new[] { Swatch(Overlay.PathColor), "rota de movimento", Count(overlay.ShowPaths, overlay.MovingCount, "agentes") },
                new[] { Swatch(Overlay.DockColor), "dock point (+ direção)", Count(overlay.ShowDocks, overlay.DockCount, "") },
                new[] { Swatch(Overlay.HurtColor), "hurtbox (recebe dano)", Count(overlay.ShowHitboxes, overlay.HurtCount, "") },
                new[] { Swatch(Overlay.HitColor), "hitbox de arma ativa", Count(overlay.ShowHitboxes, overlay.ActiveHitCount, "") },
            };
            p.Table(legend);

            CameraSystem cs = CameraSystem.Instance;
            if (cs != null && cs.WorldCamera != null)
            {
                MainCamera mc = cs.MainCamera;
                string mode = mc == null || mc.RenderTexture == null ? "direto na tela" : $"RT {mc.RenderTexture.width}×{mc.RenderTexture.height}" + (mc.IsUpscaled ? " upscale" : "");
                p.Line($" câmera orto {cs.WorldCamera.orthographicSize:0.00} · {mode} · pixelSize {cs.ResolutionPiexelSize}");
            }
            if (MainGame.PlayerController != null)
            {
                Vector3 pos = MainGame.PlayerController.transform.position;
                p.Line($" jogador ({pos.x:0.00}, {pos.y:0.00}, {pos.z:0.00})");
            }
            WriteCounts(p);
            WriteRes(p, player);
        }

        private static void WriteCounts(Panel p)
        {
            int world = MainGame.WorldData?.Cache?.wgoDataByUidCache?.Count ?? 0;
            int loaded = 0, visible = 0, scenes = 0;
            foreach (GameScene gs in LazySingleton<GameSceneManager>.Instance.LoadedGameScenes)
            {
                if (gs?.GameSceneData == null) continue;
                scenes++;
                foreach (WgoData w in gs.GameSceneData.wgoDataList)
                {
                    if (w == null) continue;
                    loaded++;
                    Wgo view = GameScene.GetWgoViewGlobal(w.UniqueId);
                    if (view != null && view.gameObject.activeSelf) visible++;
                }
            }
            p.Line($" objetos {world} no mundo · {loaded} em {scenes} cenas carregadas · {visible} visíveis");
        }

        private static void WriteRes(Panel p, PlayerData player)
        {
            GameRes res = PlayerRes(player);
            if (res == null) return;
            p.Section("Recursos do jogador", "valores crus (PlayerData.res, sem efeito colateral)");
            List<GameResAtom> atoms = new List<GameResAtom>(res.List);
            atoms.RemoveAll(a => a.value == 0f);
            atoms.Sort((a, b) => string.CompareOrdinal(a.type, b.type));
            List<string[]> rows = new List<string[]>();
            const int cols = 3;
            for (int i = 0; i < atoms.Count; i += cols)
            {
                string[] row = new string[cols];
                for (int c = 0; c < cols; c++)
                    row[c] = i + c < atoms.Count ? $"{atoms[i + c].type}={atoms[i + c].value:0.##}" : "";
                rows.Add(row);
            }
            p.Table(rows);
        }

        private static string Swatch(Color c) => Panel.Col("#" + ColorUtility.ToHtmlStringRGB(c), "■");

        private static string Count(bool on, int n, string unit) => on ? Panel.Dim($"{n} {unit}".Trim()) : Panel.Dim("desligado");
    }
}
