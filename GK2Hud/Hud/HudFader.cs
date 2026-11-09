using UnityEngine;

namespace GK2Hud.Hud
{
    /// <summary>
    /// Fade de alpha num CanvasGroup no ritmo do HUD do jogo: mesmo cálculo de um <c>DOFade</c> do DOTween com a curva
    /// padrão do jogo (<c>DOTween.defaultEaseType</c> = OutQuad), partindo do alpha atual quando o alvo muda no meio.
    /// Tempo não escalado (o <c>UISaveOverlay</c> faz <c>SetUpdate(true)</c>). Sem DOTween: nada a matar no Dispose,
    /// o objeto vai junto com o GameObject. Ao terminar o fade-out desativa o GameObject (nada é desenhado).
    /// </summary>
    internal sealed class HudFader
    {
        private readonly GameObject go;
        private readonly CanvasGroup group;
        private float from, to, elapsed;
        private bool target, done = true;

        public HudFader(GameObject go)
        {
            this.go = go;
            if (!go.TryGetComponent(out group)) group = go.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            group.alpha = 0f;
            go.SetActive(false);
        }

        public bool Target => target;
        public float Alpha => group != null ? group.alpha : 0f;
        public bool Animating => !done;

        public void Set(bool visible)
        {
            if (visible == target) return;
            target = visible;
            from = group.alpha;
            to = visible ? 1f : 0f;
            elapsed = 0f;
            done = false;
            if (visible && !go.activeSelf) go.SetActive(true);
        }

        public void Step(float dt, float duration)
        {
            if (done) return;
            elapsed += dt;
            float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
            float eased = t * (2f - t);   // Ease.OutQuad
            group.alpha = from + (to - from) * eased;
            if (t < 1f) return;
            done = true;
            if (!target && go.activeSelf) go.SetActive(false);
        }
    }
}
