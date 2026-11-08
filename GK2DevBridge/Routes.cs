using System;
using System.Collections;
using System.Collections.Generic;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace GK2DevBridge
{
    internal static class Routes
    {
        public static IEnumerator Ping(Job job)
        {
            job.Json(new
            {
                ok = true,
                bridge = Bridge.Version,
                unity = Application.unityVersion,
                product = Application.productName,
                dataPath = Application.persistentDataPath,
                frame = Time.frameCount,
                fps = Mathf.RoundToInt(1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f)),
                logSeq = LogBuffer.LastSeq
            });
            yield break;
        }

        public static IEnumerator State(Job job)
        {
            List<string> scenes = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                scenes.Add(SceneManager.GetSceneAt(i).name);
            }
            object player = null;
            object time = null;
            MainGame game = MainGame.Instance;
            if (game != null && MainGame.PlayerData != null && game.GameSave != null)
            {
                EnvironmentData env = game.GameSave.environmentData;
                int minutes = (int)(env.TimeOfDay * 1440f);
                time = new { day = env.Day, weekDay = env.CurrentDayNumber, timeOfDay = env.TimeOfDay, clock = $"{minutes / 60:00}:{minutes % 60:00}" };
                PlayerController pc = MainGame.PlayerController;
                player = new
                {
                    position = pc != null ? Vec(pc.transform.position) : null,
                    zone = MainGame.PlayerData.CurrentWorldZoneData?.id,
                    energy = MainGame.PlayerData.GetRes("energy"),
                    insanity = MainGame.PlayerData.GetRes("insanity"),
                    money = MainGame.PlayerData.GetRes("money")
                };
            }
            job.Json(new
            {
                scenes,
                screen = new { Screen.width, Screen.height, lazyUiScale = LazyUI.IsInitialized ? LazyUI.ScaleFactor : 0f },
                inGame = player != null,
                paused = MainGame.IsGamePaused,
                time,
                player,
                openWindows = OpenWindows()
            });
            yield break;
        }

        public static IEnumerator Windows(Job job)
        {
            List<object> all = new List<object>();
            if (LazyUI.IsInitialized)
            {
                foreach (LazyWidgetBase w in LazyUI.GetAllWindows())
                {
                    if (w == null) continue;
                    all.Add(new { type = w.GetType().FullName, go = w.gameObject.name, active = w.gameObject.activeInHierarchy });
                }
            }
            job.Json(all);
            yield break;
        }

        private static List<string> OpenWindows()
        {
            List<string> open = new List<string>();
            if (!LazyUI.IsInitialized) return open;
            foreach (LazyWidgetBase w in LazyUI.GetAllWindows())
            {
                if (w != null && w.gameObject.activeInHierarchy) open.Add(w.GetType().Name);
            }
            return open;
        }

        public static IEnumerator LogTail(Job job)
        {
            long since = long.TryParse(job.Str("since"), out long s) ? s : 0;
            job.Json(new { last = LogBuffer.LastSeq, entries = LogBuffer.Since(since, job.Int("n", 200), job.Str("grep")) });
            yield break;
        }

        public static IEnumerator Shot(Job job)
        {
            bool hideUi = job.Bool("hideui");
            List<Canvas> hidden = new List<Canvas>();
            if (hideUi)
            {
                foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                {
                    if (c.enabled && c.isRootCanvas)
                    {
                        c.enabled = false;
                        hidden.Add(c);
                    }
                }
            }
            for (int i = 0; i < job.Int("frames", 1); i++)
            {
                yield return new WaitForEndOfFrame();
            }
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            foreach (Canvas c in hidden)
            {
                if (c != null) c.enabled = true;
            }
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            job.Bytes("image/png", png);
        }

        public static IEnumerator Quit(Job job)
        {
            job.Json(new { ok = true, quitting = true });
            yield return null;
            Application.Quit();
        }

        public static IEnumerator Ui(Job job)
        {
            string filter = job.Str("filter", "");
            int depth = job.Int("depth", string.IsNullOrEmpty(filter) ? 1 : 8);
            bool inactive = job.Bool("inactive");
            int maxNodes = job.Int("max", 3000);
            UiDumper dumper = new UiDumper(depth, inactive, maxNodes);
            List<object> roots = new List<object>();
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(inactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!canvas.isRootCanvas) continue;
                if (string.IsNullOrEmpty(filter))
                {
                    roots.Add(dumper.Node(canvas.transform, canvas, 0));
                }
                else
                {
                    dumper.CollectMatches(canvas.transform, canvas, filter, roots);
                }
            }
            job.Json(new { screen = new { Screen.width, Screen.height }, truncated = dumper.Truncated, nodes = dumper.Count, roots });
            yield break;
        }

        internal static float[] Vec(Vector3 v) => new[] { (float)Math.Round(v.x, 2), (float)Math.Round(v.y, 2), (float)Math.Round(v.z, 2) };

        internal static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);
    }

    internal class UiDumper
    {
        private readonly int maxDepth;
        private readonly bool inactive;
        private readonly int maxNodes;
        private readonly Vector3[] corners = new Vector3[4];

        public int Count;
        public bool Truncated;

        public UiDumper(int maxDepth, bool inactive, int maxNodes)
        {
            this.maxDepth = maxDepth;
            this.inactive = inactive;
            this.maxNodes = maxNodes;
        }

        public void CollectMatches(Transform t, Canvas canvas, string filter, List<object> into)
        {
            if (!inactive && !t.gameObject.activeInHierarchy) return;
            if (t.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || HasComponentNamed(t, filter))
            {
                into.Add(new { path = PathOf(t), node = Node(t, canvas, 0) });
                return;
            }
            for (int i = 0; i < t.childCount; i++)
            {
                CollectMatches(t.GetChild(i), canvas, filter, into);
            }
        }

        private static bool HasComponentNamed(Transform t, string filter)
        {
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c != null && c.GetType().Name.Equals(filter, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public Dictionary<string, object> Node(Transform t, Canvas canvas, int depth)
        {
            Count++;
            Dictionary<string, object> node = new Dictionary<string, object> { ["name"] = t.name };
            if (!t.gameObject.activeSelf) node["active"] = false;
            if (t is RectTransform rt)
            {
                node["rect"] = ScreenRect(rt, canvas);
                node["size"] = new[] { Mathf.Round(rt.rect.width * 10f) / 10f, Mathf.Round(rt.rect.height * 10f) / 10f };
            }
            List<string> types = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                types.Add(c.GetType().Name);
                switch (c)
                {
                    case Canvas cv when cv.isRootCanvas:
                        node["canvas"] = new { mode = cv.renderMode.ToString(), cv.sortingOrder, cv.scaleFactor, cv.pixelPerfect };
                        break;
                    case Image img:
                        node["image"] = ImageInfo(img);
                        break;
                    case RawImage raw:
                        node["rawImage"] = new { texture = raw.texture ? raw.texture.name : null, color = Routes.Hex(raw.color) };
                        break;
                    case TMP_Text tmp:
                        node["text"] = new
                        {
                            value = Trim(tmp.text, 160),
                            font = tmp.font ? tmp.font.name : null,
                            material = tmp.fontSharedMaterial ? tmp.fontSharedMaterial.name : null,
                            tmp.fontSize,
                            autoSize = tmp.enableAutoSizing,
                            color = Routes.Hex(tmp.color),
                            style = tmp.fontStyle.ToString(),
                            align = tmp.alignment.ToString(),
                            tmp.characterSpacing,
                            tmp.lineSpacing
                        };
                        break;
                    case TextStyleComponent tsc:
                        node["textStyle"] = tsc.CurrentTextStyle ? tsc.CurrentTextStyle.name : null;
                        break;
                    case HorizontalOrVerticalLayoutGroup lg:
                        node["layout"] = new { kind = lg.GetType().Name, lg.spacing, padding = new[] { lg.padding.left, lg.padding.right, lg.padding.top, lg.padding.bottom }, align = lg.childAlignment.ToString() };
                        break;
                    case GridLayoutGroup grid:
                        node["layout"] = new { kind = "Grid", cell = new[] { grid.cellSize.x, grid.cellSize.y }, spacing = new[] { grid.spacing.x, grid.spacing.y } };
                        break;
                }
            }
            node["components"] = types;
            if (t.childCount > 0)
            {
                if (depth >= maxDepth || Count >= maxNodes)
                {
                    node["childCount"] = t.childCount;
                    if (Count >= maxNodes) Truncated = true;
                }
                else
                {
                    List<object> children = new List<object>();
                    for (int i = 0; i < t.childCount; i++)
                    {
                        Transform child = t.GetChild(i);
                        if (!inactive && !child.gameObject.activeSelf) continue;
                        children.Add(Node(child, canvas, depth + 1));
                    }
                    if (children.Count > 0) node["children"] = children;
                }
            }
            return node;
        }

        private static object ImageInfo(Image img)
        {
            Sprite s = img.sprite;
            return new
            {
                sprite = s ? s.name : null,
                texture = s && s.texture ? s.texture.name : null,
                spriteRect = s ? new[] { s.rect.x, s.rect.y, s.rect.width, s.rect.height } : null,
                border = s && s.border != Vector4.zero ? new[] { s.border.x, s.border.y, s.border.z, s.border.w } : null,
                ppu = s ? s.pixelsPerUnit : 0f,
                ppuMultiplier = img.pixelsPerUnitMultiplier,
                type = img.type.ToString(),
                color = Routes.Hex(img.color),
                material = img.material && img.material != Graphic.defaultGraphicMaterial ? img.material.name : null,
                raycast = img.raycastTarget
            };
        }

        private float[] ScreenRect(RectTransform rt, Canvas canvas)
        {
            rt.GetWorldCorners(corners);
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
            return new[] { Mathf.Round(min.x), Mathf.Round(Screen.height - max.y), Mathf.Round(max.x - min.x), Mathf.Round(max.y - min.y) };
        }

        private static string PathOf(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        private static string Trim(string s, int max) => s == null || s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
