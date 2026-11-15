using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace GK2DevBridge
{
    /// <summary>
    /// Gravador de clipes para os previews do menu e os GIFs das páginas. Captura no fim do frame, recorta uma região da
    /// tela e reduz pela escala (2 = 1 pixel de arte por pixel do clipe, sem borrar), em RGB24 na memória. O cursor do
    /// sistema não sai na captura: o cenário informa onde ele está (<see cref="Cursor"/>) e o gravador desenha o cursor
    /// atual do jogo ali. <c>/rec/stop</c> grava os quadros crus (.rgb + .json) para o <c>gk2 rec</c> passar no ffmpeg.
    /// </summary>
    internal static class Recorder
    {
        private sealed class Session
        {
            public int X, Y, W, H, Scale, Fps, Max;
            public int OutW, OutH;
            public readonly List<byte[]> Frames = new List<byte[]>();
            public bool Stop;
            public float Started;
        }

        private static Session current;
        private static Coroutine routine;

        /// <summary>Posição do cursor desenhado, em pixels de tela com origem no canto de cima; null = sem cursor.</summary>
        public static Vector2? Cursor;

        public static IEnumerator Start(Job job)
        {
            if (current != null) { job.Fail(409, "já há uma gravação em andamento"); yield break; }
            int scale = Math.Max(1, job.Int("scale", 2));
            var s = new Session
            {
                X = job.Int("x", 0), Y = job.Int("y", 0), W = job.Int("w", Screen.width), H = job.Int("h", Screen.height),
                Scale = scale, Fps = Math.Max(1, job.Int("fps", 20)), Max = job.Int("max", 900), Started = Time.realtimeSinceStartup,
            };
            s.W = Math.Min(s.W, Screen.width - s.X);
            s.H = Math.Min(s.H, Screen.height - s.Y);
            s.OutW = s.W / scale;
            s.OutH = s.H / scale;
            current = s;
            Cursor = null;
            routine = Bridge.Instance.StartCoroutine(Capture(s));
            job.Json(new { ok = true, w = s.OutW, h = s.OutH, fps = s.Fps });
        }

        public static IEnumerator CursorAt(Job job)
        {
            string x = job.Str("x");
            Cursor = string.IsNullOrEmpty(x) ? (Vector2?)null : new Vector2(job.Int("x", 0), job.Int("y", 0));
            job.Json(new { ok = true, cursor = Cursor?.ToString() });
            yield break;
        }

        public static IEnumerator Stop(Job job)
        {
            Session s = current;
            if (s == null) { job.Fail(409, "nenhuma gravação em andamento"); yield break; }
            s.Stop = true;
            while (current == s) yield return null;
            string path = job.Str("out");
            if (string.IsNullOrEmpty(path)) { job.Fail(400, "falta out=caminho (sem extensão)"); yield break; }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var f = File.Create(path + ".rgb"))
                foreach (byte[] frame in s.Frames) f.Write(frame, 0, frame.Length);
            float seconds = Time.realtimeSinceStartup - s.Started;
            File.WriteAllText(path + ".json",
                $"{{\"w\":{s.OutW},\"h\":{s.OutH},\"fps\":{s.Fps},\"frames\":{s.Frames.Count},\"seconds\":{seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
            job.Json(new { ok = true, frames = s.Frames.Count, w = s.OutW, h = s.OutH, fps = s.Fps, path });
        }

        private static IEnumerator Capture(Session s)
        {
            float interval = 1f / s.Fps, next = 0f;
            var eof = new WaitForEndOfFrame();
            while (!s.Stop && s.Frames.Count < s.Max)
            {
                yield return eof;
                float now = Time.realtimeSinceStartup;
                if (now < next) continue;
                next = next == 0f ? now + interval : next + interval;
                Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
                try { s.Frames.Add(Crop(tex, s)); }
                finally { UnityEngine.Object.Destroy(tex); }
                // Quadros atrasados (frame lento) se repetem para o clipe manter o tempo real.
                while (next + interval < now && s.Frames.Count < s.Max)
                {
                    s.Frames.Add(s.Frames[s.Frames.Count - 1]);
                    next += interval;
                }
            }
            current = null;
            routine = null;
        }

        private static byte[] Crop(Texture2D tex, Session s)
        {
            Color32[] px = tex.GetPixels32();
            int tw = tex.width, th = tex.height;
            var outp = new byte[s.OutW * s.OutH * 3];
            int half = s.Scale / 2;
            for (int j = 0; j < s.OutH; j++)
            {
                int sy = th - 1 - (s.Y + j * s.Scale + half);   // textura começa embaixo; o clipe, em cima
                if (sy < 0 || sy >= th) continue;
                int row = sy * tw;
                int o = j * s.OutW * 3;
                for (int i = 0; i < s.OutW; i++, o += 3)
                {
                    int sx = s.X + i * s.Scale + half;
                    if (sx < 0 || sx >= tw) continue;
                    Color32 c = px[row + sx];
                    outp[o] = c.r; outp[o + 1] = c.g; outp[o + 2] = c.b;
                }
            }
            if (Cursor.HasValue) DrawCursor(outp, s, Cursor.Value);
            return outp;
        }

        private static Texture2D cursorTex;
        private static Color32[] cursorPx;
        private static Vector2 cursorHot;

        /// <summary>Cursor atual do jogo (CursorController: último estado da pilha), na escala de arte do clipe.</summary>
        private static void DrawCursor(byte[] frame, Session s, Vector2 screenPos)
        {
            if (!LoadCursor()) return;
            int cw = cursorTex.width, ch = cursorTex.height;
            // O cursor de hardware tem o tamanho da textura em pixels de tela; no clipe, divide pela escala.
            int ox = Mathf.RoundToInt((screenPos.x - s.X - cursorHot.x) / s.Scale);
            int oy = Mathf.RoundToInt((screenPos.y - s.Y - cursorHot.y) / s.Scale);
            for (int j = 0; j < ch / s.Scale; j++)
            {
                int y = oy + j;
                if (y < 0 || y >= s.OutH) continue;
                int sy = ch - 1 - j * s.Scale;
                for (int i = 0; i < cw / s.Scale; i++)
                {
                    int x = ox + i;
                    if (x < 0 || x >= s.OutW) continue;
                    Color32 c = cursorPx[sy * cw + i * s.Scale];
                    if (c.a < 8) continue;
                    int o = (y * s.OutW + x) * 3;
                    float a = c.a / 255f;
                    frame[o] = (byte)(c.r * a + frame[o] * (1 - a));
                    frame[o + 1] = (byte)(c.g * a + frame[o + 1] * (1 - a));
                    frame[o + 2] = (byte)(c.b * a + frame[o + 2] * (1 - a));
                }
            }
        }

        private static bool LoadCursor()
        {
            if (cursorPx != null) return true;
            try
            {
                Type ctl = Type.GetType("CursorController, Assembly-CSharp");
                object inst = ctl?.BaseType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var configs = ctl?.GetField("cursorConfigurations", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(inst) as IList;
                if (configs == null || configs.Count == 0) return false;
                object cfg = configs[0];
                var tex = cfg.GetType().GetField("sprite")?.GetValue(cfg) as Texture2D;
                if (tex == null) return false;
                cursorPx = Readable(tex);
                cursorTex = tex;
                cursorHot = (Vector2)(cfg.GetType().GetField("hotSpot")?.GetValue(cfg) ?? Vector2.zero);
                return cursorPx != null;
            }
            catch (Exception e)
            {
                Bridge.Log.LogWarning("cursor do jogo indisponível para o gravador: " + e.Message);
                cursorPx = new Color32[0];
                return false;
            }
        }

        /// <summary>Pixels de uma textura mesmo sem Read/Write: copia por um RenderTexture.</summary>
        private static Color32[] Readable(Texture2D src)
        {
            RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = copy.GetPixels32();
            UnityEngine.Object.Destroy(copy);
            return px;
        }
    }
}
