using System;
using System.IO;
using System.Reflection;
using BepInEx;
using GK2Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace GK2HudPlus.Menu
{
    /// <summary>
    /// Painel à direita da lista: nome da funcionalidade, um clipe do jogo com ela em ação (WebM em previews/, gravado
    /// pelo <c>gk2 rec</c>) numa moldura escura nativa e uma frase curta. O clipe é gravado na resolução da tela e
    /// mostrado 1 pixel de textura por pixel de tela, sem filtro, então fica tão nítido quanto o jogo.
    /// </summary>
    internal sealed class PreviewPanel : IDisposable
    {
        public const float Width = 300f;
        private const float TitleHeight = 16f;
        private const float CaptionHeight = 40f;
        private const float Gap = 4f;
        private const float FramePad = 3f;

        private readonly RectTransform root;
        private readonly TextMeshProUGUI title, caption;
        private readonly RectTransform box;
        private readonly RawImage raw;
        private readonly VideoPlayer player;
        private readonly string dir;
        private readonly float videoMaxW, videoMaxH;
        private RenderTexture rt;
        private string current;

        public RectTransform Root => root;
        public string Current => current;

        public PreviewPanel(Transform parent, float height, Sprite frame, TextMeshProUGUI titleTemplate, TextMeshProUGUI bodyTemplate)
        {
            var go = new GameObject("Preview", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            root = (RectTransform)go.transform;
            var le = go.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = Width;
            le.minHeight = le.preferredHeight = height;

            title = HudPlusWindow.Label(root, "Title", "", TextAlignmentOptions.Midline, titleTemplate);
            Place(title.rectTransform, 0f, TitleHeight);

            float boxH = height - TitleHeight - CaptionHeight - 2 * Gap;
            var boxGo = new GameObject("Frame", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            boxGo.transform.SetParent(root, false);
            box = (RectTransform)boxGo.transform;
            Place(box, TitleHeight + Gap, boxH);
            Image img = boxGo.GetComponent<Image>();
            img.sprite = frame;
            img.type = Image.Type.Sliced;
            img.raycastTarget = false;
            videoMaxW = Width - 2 * FramePad;
            videoMaxH = boxH - 2 * FramePad;

            var rawGo = new GameObject("Clip", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawGo.transform.SetParent(box, false);
            raw = rawGo.GetComponent<RawImage>();
            raw.raycastTarget = false;
            var rrt = (RectTransform)rawGo.transform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
            raw.enabled = false;

            caption = HudPlusWindow.Label(root, "Caption", "", TextAlignmentOptions.Top, bodyTemplate);
            caption.textWrappingMode = TextWrappingModes.Normal;
            caption.overflowMode = TextOverflowModes.Ellipsis;
            Place(caption.rectTransform, TitleHeight + Gap + boxH + Gap, CaptionHeight);

            player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = true;
            player.skipOnDrop = true;
            player.source = VideoSource.Url;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.prepareCompleted += OnPrepared;
            dir = FindDir();
        }

        /// <summary>Pasta previews/ ao lado do plugin; com hot-reload a DLL roda de outra pasta, então cai na de plugins.</summary>
        private static string FindDir()
        {
            string location = Assembly.GetExecutingAssembly().Location;   // vazio quando a DLL veio da memória (hot-reload)
            if (!string.IsNullOrEmpty(location))
            {
                string a = Path.Combine(Path.GetDirectoryName(location) ?? "", "previews");
                if (Directory.Exists(a)) return a;
            }
            return Path.Combine(Path.Combine(Paths.PluginPath, "GK2HudPlus"), "previews");
        }

        private static void Place(RectTransform rt, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -(top + height));
            rt.offsetMax = new Vector2(0f, -top);
        }

        public void Show(FeatureInfo f, string extra)
        {
            if (f == null) return;
            title.text = f.Name.Current;
            caption.text = extra == null ? f.Description.Current : f.Description.Current + "\n" + extra;
            // Opção sem clipe próprio mostra o da funcionalidade dona.
            string id = f.Id;
            string path = Path.Combine(dir, id + ".webm");
            for (FeatureInfo p = f.Parent; !File.Exists(path) && p != null; p = p.Parent) path = Path.Combine(dir, (id = p.Id) + ".webm");
            if (!File.Exists(path))
            {
                current = null;
                player.Stop();
                raw.enabled = false;
                return;
            }
            if (current == id && player.isPrepared) return;
            current = id;
            player.Stop();
            player.url = path;
            player.Prepare();
        }

        private void OnPrepared(VideoPlayer vp)
        {
            int w = (int)vp.width, h = (int)vp.height;
            if (w <= 0 || h <= 0) return;
            if (rt == null || rt.width != w || rt.height != h)
            {
                if (rt != null) rt.Release();
                rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, name = "GK2HudPlus_Preview" };
                rt.Create();
            }
            vp.targetTexture = rt;
            raw.texture = rt;
            // 1 px de textura = 1 px de tela (canvas em escala 2: tamanho em unidades = metade); se não couber, metade disso.
            float k = 0.5f;
            while ((w * k > videoMaxW || h * k > videoMaxH) && k > 0.125f) k /= 2f;
            ((RectTransform)raw.transform).sizeDelta = new Vector2(Mathf.Round(w * k), Mathf.Round(h * k));
            raw.enabled = true;
            vp.Play();
        }

        public void Dispose()
        {
            if (player != null)
            {
                player.prepareCompleted -= OnPrepared;
                player.Stop();
            }
            if (rt != null) rt.Release();
            rt = null;
        }
    }
}
