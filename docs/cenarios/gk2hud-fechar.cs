using System.Collections;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Hud: com uma pilha aberta (rode gk2hud-pilha.cs antes), testa um jeito de fechar: ACAO = direito | tecla | esquerdo-dentro | esquerdo-fora | esc | janela | esc-prefix.
public static class Snippet
{
    const string ACAO = "direito";

    public static object Run() { return Go(); }

    static object F() { var p = Chainloader.PluginInfos["arthur.gk2.hud"].Instance; return AccessTools.Property(p.GetType(), "Tooltips").GetValue(p); }
    static string D(object f) => (string)AccessTools.Method(f.GetType(), "Describe").Invoke(f, null);
    static void Mouse(object f, Vector2? v) => AccessTools.Property(f.GetType(), "VirtualMouse").SetValue(f, v);
    static void Flag(object f, string n) => AccessTools.Field(f.GetType(), n).SetValue(f, true);
    static IList Pinned(object f) { var s = AccessTools.Field(f.GetType(), "Stack").GetValue(f); return (IList)AccessTools.Field(s.GetType(), "Pinned").GetValue(s); }
    static Vector2 Center(object bubble) { var rt = (RectTransform)AccessTools.Field(bubble.GetType(), "Rt").GetValue(bubble); var c = new Vector3[4]; rt.GetWorldCorners(c); return new Vector2((c[0].x + c[2].x) / 2, c[0].y + 30); }

    static IEnumerator Go()
    {
        var sb = new StringBuilder();
        object f = F();
        sb.AppendLine("antes: " + D(f));
        IList pinned = Pinned(f);
        if (pinned.Count == 0 && ACAO != "esc-prefix") { yield return "sem pilha: rode gk2hud-pilha.cs"; yield break; }
        switch (ACAO)
        {
            case "direito": Mouse(f, Center(pinned[pinned.Count > 1 ? 1 : 0])); Flag(f, "TestRightClick"); break;
            case "tecla": Mouse(f, Center(pinned[pinned.Count > 1 ? 1 : 0])); UITooltip.HideImmediately(); Flag(f, "TestPin"); break;
            case "esquerdo-dentro": Mouse(f, Center(pinned[0])); Flag(f, "TestLeftClick"); break;
            case "esquerdo-fora": Mouse(f, new Vector2(1900, 20)); Flag(f, "TestLeftClick"); break;
            case "esc": Flag(f, "TestEsc"); break;
            case "janela":
                var cw = LazyUI.GetWindow<CharacterWindow>();
                if (cw.gameObject.activeInHierarchy) AccessTools.Method(cw.GetType(), "Close", new System.Type[0]).Invoke(cw, null);
                else cw.Open(new CharacterWindowData(MainGame.Instance.GameSave));
                break;
            case "esc-prefix":
                // Injeta Esc (GameKey das bindings de Escape) no LazyInput só durante esta chamada síncrona e vê se o prefix o engole.
                var inst = AccessTools.Property(typeof(LazyInput), "Instance").GetValue(null);
                var pressed = (List<GameKey>)AccessTools.Field(typeof(LazyInput), "pressedKeys").GetValue(inst);
                GameKey esc = null;
                foreach (var b in LazyInput.GameBindings.keyBindings) if (b.keyCode == KeyCode.Escape) { esc = b.gameKey; break; }
                pressed.Add(esc);
                AccessTools.Field(f.GetType(), "escFrame").SetValue(f, Time.frameCount);
                bool engolido = !LazyInput.GetKeyDown(esc);
                AccessTools.Field(f.GetType(), "escFrame").SetValue(f, -1);
                bool normal = LazyInput.GetKeyDown(esc);
                pressed.Remove(esc);
                yield return "Esc engolido no frame da pilha: " + engolido + " | Esc normal sem pilha: " + normal;
                yield break;
        }
        for (int i = 0; i < 4; i++) yield return null;
        sb.AppendLine("depois de " + ACAO + ": " + D(f));
        Mouse(f, null);
        yield return sb.ToString();
    }
}
