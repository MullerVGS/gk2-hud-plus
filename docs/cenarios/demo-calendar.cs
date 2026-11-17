using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Demos do calendário (clipes "CalendarTab", "Journal" e "Wheel"). MODO:
//   aba      — abre a aba Calendário; o cursor passa em duas marcas da faixa de cenas (tooltip) e clica (o dia embaixo muda)
//   diario   — na aba, o cursor escolhe um dia passado com diário (achado antes por MODO=diario-escolher) e passa na
//              primeira linha do diário dele
//   medalhao — sem janelas: o cursor para no medalhão dos dias (hora e semana) e depois num sigilo (o que há naquele dia)
// FIM = "meio" para no meio (enquadrar com um print).
public static class Snippet
{
    static string MODO = "aba";
    static string FIM = "";

    static object Call(object o, string n, params object[] a) => AccessTools.Method(o.GetType(), n).Invoke(o, a);

    /// <summary>Marcas clicáveis da página (CalendarPage+Hover) dentro de um retângulo de tela (origem em cima).</summary>
    static List<Component> Marks(Rect area)
    {
        return Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .Where(m => m.GetType().Name == "Hover" && m.GetType().DeclaringType?.Name == "CalendarPage")
            .Where(m => area.Contains(Demo.Center(m.transform))).Cast<Component>().ToList();
    }

    static TMPro.TMP_Text Text(string text) => Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)
        .FirstOrDefault(t => t.isActiveAndEnabled && t.text == text);

    /// <summary>Área da coluna do diário (dois terços da página, à direita), abaixo do título "Journal".</summary>
    static Rect JournalArea()
    {
        var title = Text("Journal");
        Vector2 c = title != null ? Demo.Center(title.transform) : new Vector2(1280, 692);
        return new Rect(c.x - 410, c.y + 40, 820, 400);
    }

    /// <summary>Área da faixa de cenas (cenas, legendas e ícones): do topo da página até os títulos de baixo.</summary>
    static Rect TimelineArea()
    {
        var title = Text("Journal");
        float bottom = title != null ? Demo.Center(title.transform).y - 20 : 660;
        return new Rect(330, 240, 1264, bottom - 240 > 40 ? bottom - 240 : 240);
    }

    /// <summary>Linhas da coluna do diário.</summary>
    static List<Component> JournalLines() => Marks(JournalArea()).OrderBy(m => Demo.Center(m.transform).y).ToList();

    static IEnumerator Touch(Component mark, bool click, float stay)
    {
        yield return Demo.Glide(Demo.Center(mark.transform) + new Vector2(3, 5), 0.7f);
        Demo.Enter(mark.gameObject);
        yield return Demo.Wait(stay);
        if (click)
        {
            Demo.Exit(mark.gameObject);
            Demo.Click(mark.gameObject);
            yield return Demo.Wait(1.2f);
        }
    }

    static IEnumerator OpenTab(object tab)
    {
        var w = LazyUI.GetWindow<CharacterWindow>();
        if (!w.IsShown) w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Main));
        float t = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t < 1.5f && !(bool)AccessTools.Property(tab.GetType(), "IsOpen").GetValue(tab))
        {
            yield return null;
            if (Time.realtimeSinceStartup - t > 0.1f) Call(tab, "TestOpen", (int?)null);
        }
        yield return Demo.Wait(0.6f);
    }

    public static IEnumerator Run()
    {
        if (MODO == "medalhao")
        {
            if (Demo.Hud("Wheel") == null) { yield return "medalhão desligado"; yield break; }
            var leftUp = Traverse.Create(LazyUI.Get<HUD>()).Field("leftUpGroup").GetValue<GameObject>();
            var wheel = leftUp.GetComponentInChildren<UIHUDWheel>(true);
            Vector2 center = Demo.Center(wheel.transform);
            var sigils = wheel.GetComponentsInChildren<UIHUDWheelElement>(true).Where(e => e.isActiveAndEnabled).OrderBy(e => Demo.Center(e.transform).y).ToList();
            Demo.Mouse(center + new Vector2(300, 170));
            yield return Demo.Wait(0.5f);
            yield return Demo.Glide(center + new Vector2(4, 6), 0.8f);
            yield return Demo.Wait(2.2f);
            if (FIM == "meio") { yield return "parado no medalhão " + center; yield break; }
            if (sigils.Count > 0)
            {
                yield return Demo.Glide(Demo.Center(sigils.Last().transform) + new Vector2(3, 4), 0.6f);
                yield return Demo.Wait(2.2f);
            }
            yield return Demo.Glide(center + new Vector2(300, 170), 0.8f);
            yield return Demo.Wait(0.8f);
            Demo.Mouse(null);
            yield return "demo medalhão ok";
            yield break;
        }

        object tab = Demo.Hud("CalendarTab");
        if (tab == null) { yield return "aba Calendário desligada"; yield break; }
        yield return OpenTab(tab);
        Rect timeline = TimelineArea();
        Demo.Mouse(new Vector2(470, 760));
        yield return Demo.Wait(0.4f);
        if (MODO == "aba")
        {
            var marks = Marks(timeline).OrderBy(m => Demo.Center(m.transform).x).ToList();
            if (marks.Count == 0) { yield return "sem marcas na linha do tempo"; yield break; }
            yield return Touch(marks[marks.Count / 3], true, 1.4f);
            if (FIM == "meio") { yield return "parado na primeira marca"; yield break; }
            yield return Touch(marks[marks.Count * 2 / 3], true, 1.4f);
            yield return Demo.Glide(new Vector2(470, 760), 0.7f);
            yield return Demo.Wait(0.6f);
            Demo.Mouse(null);
            yield return "demo aba ok (" + marks.Count + " marcas)";
            yield break;
        }

        // diario: o dia que o passo "diario-escolher" achou (o painel do diário mostra só alguns tipos de anotação); o cursor
        // "clica" no cabeçalho dele e passa na primeira linha do diário.
        string memo = System.IO.Path.Combine(Application.temporaryCachePath, "gk2demo-dia-diario.txt");
        int today = MainGame.Instance.GameSave.environmentData.Day;
        if (MODO == "diario-escolher")
        {
            for (int d = today; d > today - 60; d--)   // no jogo novo de gravação, o diário só tem o dia de hoje
            {
                Call(tab, "TestOffset", d - today);   // seleciona e rola a linha do tempo até o dia
                yield return null; yield return null;
                int n = JournalLines().Count;
                if (n >= 1) { System.IO.File.WriteAllText(memo, d.ToString()); yield return "dia " + d + " com " + n + " linhas"; yield break; }
            }
            yield return "nenhum dia com diário nos últimos 60";
            yield break;
        }
        if (!System.IO.File.Exists(memo)) { yield return "rode antes MODO=diario-escolher"; yield break; }
        int day = int.Parse(System.IO.File.ReadAllText(memo));
        Call(tab, "TestOffset", day + 1 - today);   // a semana do dia na tela, com o dia seguinte escolhido
        if (day == today) Call(tab, "TestOffset", 1);
        yield return Demo.Wait(0.6f);
        string label = day == today ? "Today" : "Day " + day;
        var header = Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)
            .FirstOrDefault(t => t.isActiveAndEnabled && t.GetParsedText() == label && Demo.Center(t.transform).y < 420);
        Vector2 at = header != null ? Demo.Center(header.transform) : new Vector2(700, 290);
        yield return Demo.Glide(at + new Vector2(3, 5), 0.8f);
        Call(tab, "TestOffset", day - today);
        yield return Demo.Wait(1.0f);
        var lines = JournalLines();
        if (FIM == "meio") { yield return "dia " + day + ", " + lines.Count + " linhas no diário"; yield break; }
        // Mira pela entrada: a linha que tem o horário à direita ("Quest · 18:07") e o texto dela na mesma altura (os títulos
        // de grupo, "Completed"/"Unlocked", não têm horário).
        var texts = Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None)
            .Where(t => t.isActiveAndEnabled && JournalArea().Contains(Demo.Center(t.transform))).ToList();
        var stamp = texts.Where(t => t.GetParsedText().Contains("·") && t.GetParsedText().Contains(":")).OrderBy(t => Demo.Center(t.transform).y).FirstOrDefault();
        var text = stamp == null ? null : texts.Where(t => t != stamp && Mathf.Abs(Demo.Center(t.transform).y - Demo.Center(stamp.transform).y) < 14
                && Demo.Center(t.transform).x < Demo.Center(stamp.transform).x && t.GetComponentsInParent<MonoBehaviour>().Any(m => m.GetType().Name == "Hover"))
            .OrderBy(t => Demo.Center(t.transform).x).FirstOrDefault();
        if (text != null)
        {
            var hover = text.GetComponentsInParent<MonoBehaviour>().First(m => m.GetType().Name == "Hover");
            yield return Demo.Glide(Demo.Center(text.transform) + new Vector2(-20, 5), 0.7f);
            Demo.Enter(hover.gameObject);
            yield return Demo.Wait(2.2f);
            Demo.Exit(hover.gameObject);
        }
        yield return Demo.Glide(new Vector2(470, 760), 0.7f);
        yield return Demo.Wait(0.6f);
        Demo.Mouse(null);
        yield return "demo diário ok: dia " + day + " (" + lines.Count + " linhas)";
    }
}
