using System.Collections;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "SaveLoad": abre a pausa, o cursor passa no Save e clica no Load; a lista nativa de saves abre com o save
// atual, os backups e o histórico do mod. FIM = "meio" para com a lista aberta.
public static class Snippet
{
    static string FIM = "";

    public static IEnumerator Run()
    {
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        if (pause.IsShown) { pause.Close(); yield return Demo.Wait(0.3f); }
        pause.Open(null);
        yield return Demo.Wait(0.8f);
        GameObject save = GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Save");
        GameObject load = GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Load");
        if (save == null || load == null) { yield return "botões do Save/Load não estão na pausa"; yield break; }
        Demo.Mouse(Demo.Center(save.transform) + new Vector2(260, 120));
        yield return Demo.Wait(0.5f);
        yield return Demo.HoverOn(save, 0.7f, 0.9f);
        Demo.Exit(save);
        yield return Demo.HoverOn(load, 0.4f, 0.5f);
        Demo.Click(load);
        yield return Demo.Wait(2.6f);
        if (FIM == "meio") { yield return "lista aberta"; yield break; }
        var list = LazyUI.GetWindow<UISaveSlotsWindow>();
        if (list.IsShown) list.Close();
        yield return Demo.Wait(0.4f);
        if (pause.IsShown) pause.Close();
        Demo.Mouse(null);
        yield return "demo saves ok";
    }
}
