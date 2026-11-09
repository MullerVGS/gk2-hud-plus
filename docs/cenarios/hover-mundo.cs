using System.Linq;
using System.Text;
using UnityEngine;

// Só leitura: projeta objetos (Wgo) perto do jogador na tela e testa o que Physics.RaycastAll atinge nesse ponto (viabilidade de hover no mundo).
public static class Snippet
{
    public static object Run()
    {
        var sb = new StringBuilder();
        var cs = CameraSystem.Instance;
        var cam = cs.WorldCamera;
        var mc = cs.MainCamera;
        sb.AppendLine("cam ortho=" + cam.orthographic + " size=" + cam.orthographicSize + " rot=" + cam.transform.eulerAngles + " RT=" + (mc.RenderTexture != null ? mc.RenderTexture.width + "x" + mc.RenderTexture.height : "null") + " upscaled=" + mc.IsUpscaled + " cullingMask=" + cam.cullingMask);
        sb.AppendLine("layers: " + string.Join(", ", Enumerable.Range(0, 32).Where(i => LayerMask.LayerToName(i) != "").Select(i => i + "=" + LayerMask.LayerToName(i))));
        Vector3 p = MainGame.PlayerController.transform.position;
        var wgos = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None)
            .Where(w => w.isActiveAndEnabled && w.HasData && (w.transform.position - p).sqrMagnitude < 8 * 8)
            .OrderBy(w => (w.transform.position - p).sqrMagnitude).Take(12).ToList();
        sb.AppendLine("jogador " + p + " | Wgo a < 8 m: " + wgos.Count);
        int ok = 0, okVisual = 0, okI = 0, okIV = 0;
        foreach (var w in wgos)
        {
            var rends = w.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.isVisible).ToList();
            Vector3 visual = rends.Count > 0 ? rends.Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; }).center : w.transform.position;
            foreach (var (label, wp) in new[] { ("pivô", w.transform.position), ("centro visual", visual) })
            {
                Vector3 s = cam.WorldToScreenPoint(wp);
                if (mc.RenderTexture != null) s = mc.RenderTargetToScreen(s);
                var ray = CameraSystem.ScreenPointToRay(s);
                var hits = Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).ToList();
                bool found = hits.Any(h => h.collider.GetComponentInParent<Wgo>() == w);
                // Só a layer Interactable (6), primeiro acerto = o que um hover pegaria.
                var first = Physics.RaycastAll(ray, 500f, 1 << 6, QueryTriggerInteraction.Collide).OrderBy(h => h.distance).Select(h => h.collider.GetComponentInParent<Wgo>()).FirstOrDefault(x => x != null);
                if (first == w) { if (label == "pivô") okI++; else okIV++; }
                if (found) { if (label == "pivô") ok++; else okVisual++; }
                sb.AppendLine("  " + w.Id + " (" + w.Data.Definition?.id + ") " + label + " tela=(" + s.x.ToString("0") + "," + s.y.ToString("0") + ") acertou-o=" + found + " | " +
                    string.Join(" ; ", hits.Take(4).Select(h => h.collider.name + "[" + LayerMask.LayerToName(h.collider.gameObject.layer) + (h.collider.isTrigger ? ",trigger" : "") + "," + h.collider.GetType().Name + "] wgo=" + (h.collider.GetComponentInParent<Wgo>()?.Id ?? "-"))));
            }
        }
        sb.AppendLine("acertos pelo pivô " + ok + "/" + wgos.Count + ", pelo centro visual " + okVisual + "/" + wgos.Count);
        sb.AppendLine("1º Interactable é o alvo: pivô " + okI + "/" + wgos.Count + ", centro visual " + okIV + "/" + wgos.Count);
        return sb.ToString();
    }
}
