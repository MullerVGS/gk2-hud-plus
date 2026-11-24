using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "FolioButton": com a mesa de alquimia aberta (alquimia-mesa.cs), o cursor para na célula do Folio (tooltip
// nativo), o clique abre o Folio por cima da mesa e o X dele volta à mesa. FIM = "hover" para no tooltip; "folio", com o
// Folio aberto.
public static class Snippet
{
    static string FIM = "";

    public static IEnumerator Run()
    {
        var mesa = LazyUI.GetWindow<UIAlchemyWindow>();
        if (!mesa.IsShown) { yield return "mesa de alquimia fechada (rode alquimia-mesa.cs)"; yield break; }
        GameObject cell = Demo.Find("GK2Crafting.FolioButton", under: mesa.transform);
        if (cell == null) { yield return "célula do Folio não está na mesa (Folio construído? funcionalidade ligada?)"; yield break; }
        Vector2 at = Demo.Center(cell.transform);
        Demo.Mouse(at + new Vector2(-260, 150));
        yield return Demo.Wait(0.5f);
        yield return Demo.HoverOn(cell, 0.8f, 1.6f);
        if (FIM == "hover") { yield return "tooltip do Folio"; yield break; }
        Demo.Click(cell);
        yield return Demo.Wait(0.3f);
        Demo.Exit(cell);
        var folio = LazyUI.GetWindow<UIAlchemyFolioWindow>();
        yield return Demo.Wait(1.6f);
        if (!folio.IsShown) { yield return "o clique não abriu o Folio"; yield break; }
        if (FIM == "folio") { yield return "Folio aberto"; yield break; }
        GameObject close = Demo.Find("CloseButton", under: folio.transform);
        if (close != null)
        {
            yield return Demo.Glide(Demo.Center(close.transform) + new Vector2(2, 4), 0.7f);
            yield return Demo.Wait(0.3f);
            Demo.Click(close);
        }
        else folio.Close();
        yield return Demo.Wait(0.8f);
        Demo.Mouse(null);
        yield return mesa.IsShownAndTop && !folio.IsShown ? "demo folio ok" : "não voltou à mesa";
    }
}
