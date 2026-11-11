using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GK2Map
{
    /// <summary>
    /// Contorno do objeto sob o cursor, desenhado no mundo (não na interface). O jogo não tem contorno próprio.
    /// Num <c>CommandBuffer</c> da câmera do mundo, a borda é montada numa textura à parte com o shader
    /// <c>UI/Default</c> (o único do build com estêncil configurável): marca o estêncil na área do objeto e desenha a
    /// silhueta deslocada em oito direções só fora dela. Dessa textura vale só o alfa (as malhas do jogo têm cor de
    /// vértice que escurece qualquer shader pronto); ela vai para a tela pelo shader de texto, que pinta uma cor só
    /// com o alfa da textura. Testar profundidade ou redesenhar o objeto por cima não funcionam com os shaders do jogo.
    /// </summary>
    internal class Outline
    {
        private const int StencilBit = 2;
        private static readonly Color Tint = new Color(1f, 0.88f, 0.6f, 1f);
        private static readonly Vector2[] Directions =
        {
            new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1),
            new Vector2(0.7f, 0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, -0.7f),
        };

        private Camera cam;
        private CommandBuffer cmd;
        private Material mark;
        private Material rim;
        private Material paint;
        private bool empty = true;

        public void Attach(Camera camera)
        {
            cam = camera;
            cmd = new CommandBuffer { name = "GK2Map outline" };
            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, cmd);
            Shader ui = Shader.Find("UI/Default");
            if (ui == null) return;
            mark = Make(ui, CompareFunction.Always, StencilOp.Replace, 0);
            rim = Make(ui, CompareFunction.NotEqual, StencilOp.Keep, 15);
            Shader text = Shader.Find("GUI/Text Shader");
            if (text != null)
            {
                paint = new Material(text) { hideFlags = HideFlags.HideAndDontSave };
                paint.SetColor("_Color", Tint);
            }
        }

        private static readonly int MaskId = Shader.PropertyToID("_GK2MapOutlineMask");

        private static Material Make(Shader ui, CompareFunction comp, StencilOp op, int colorMask)
        {
            var m = new Material(ui) { hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_Color", Tint);
            m.SetInt("_Stencil", StencilBit);
            m.SetInt("_StencilComp", (int)comp);
            m.SetInt("_StencilOp", (int)op);
            m.SetInt("_StencilReadMask", StencilBit);
            m.SetInt("_StencilWriteMask", StencilBit);
            m.SetInt("_ColorMask", colorMask);
            m.EnableKeyword("UNITY_UI_ALPHACLIP");
            return m;
        }

        /// <param name="renderers">Renderers do objeto em destaque, ou null para nenhum.</param>
        /// <param name="unitsPerPixel">Unidades do mundo por pixel de tela na escala atual.</param>
        public void Draw(Renderer[] renderers, float unitsPerPixel)
        {
            if (cmd == null) return;
            if (renderers == null || renderers.Length == 0 || mark == null || paint == null)
            {
                if (!empty) { cmd.Clear(); empty = true; }
                return;
            }
            cmd.Clear();
            empty = false;
            Transform t = cam.transform;
            Matrix4x4 view = cam.worldToCameraMatrix, proj = cam.projectionMatrix;
            float thickness = 2f * unitsPerPixel;
            cmd.GetTemporaryRT(MaskId, cam.pixelWidth, cam.pixelHeight, 24, FilterMode.Point, RenderTextureFormat.ARGB32);
            cmd.SetRenderTarget(MaskId);
            cmd.ClearRenderTarget(true, true, Color.clear);
            cmd.SetGlobalInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            cmd.SetViewProjectionMatrices(view, proj);
            Pass(renderers, mark);
            foreach (Vector2 d in Directions)
            {
                Vector3 offset = t.right * (d.x * thickness) + t.up * (d.y * thickness);
                cmd.SetViewProjectionMatrices(view * Matrix4x4.Translate(offset), proj);
                Pass(renderers, rim);
            }
            cmd.SetViewProjectionMatrices(view, proj);
            cmd.Blit(MaskId, BuiltinRenderTextureType.CameraTarget, paint);
            cmd.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            cmd.ReleaseTemporaryRT(MaskId);
        }

        private void Pass(Renderer[] renderers, Material m)
        {
            foreach (Renderer r in renderers)
            {
                if (!Drawable(r, out int subs)) continue;
                for (int i = 0; i < subs; i++) cmd.DrawRenderer(r, m, i, 0);
            }
        }

        /// <summary>Malhas e sprites opacos do objeto; halos e luzes aditivas não têm contorno.</summary>
        private static bool Drawable(Renderer r, out int subs)
        {
            subs = 1;
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) return false;
            Material own = r.sharedMaterial;
            if (own == null || own.shader == null) return false;
            string shader = own.shader.name;
            if (shader.Contains("Additive") || shader.Contains("Particles") || shader.Contains("Light Faker") || shader.Contains("Shadow")) return false;
            if (r is MeshRenderer)
            {
                MeshFilter f = r.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) return false;
                subs = f.sharedMesh.subMeshCount;
                return true;
            }
            if (r is SkinnedMeshRenderer s)
            {
                if (s.sharedMesh == null) return false;
                subs = s.sharedMesh.subMeshCount;
                return true;
            }
            return r is SpriteRenderer;
        }

        public void Detach()
        {
            if (cam != null && cmd != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, cmd);
            cmd?.Release();
            cmd = null;
            foreach (Material m in new[] { mark, rim, paint }) if (m != null) UnityEngine.Object.Destroy(m);
            mark = rim = paint = null;
            cam = null;
        }
    }
}
