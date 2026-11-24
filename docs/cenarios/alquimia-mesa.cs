// Teleporta o jogador (save de teste) para a mesa de alquimia (interactionType Alchemy) e abre a janela nativa (UIAlchemyWindow); ACAO = "listar" só lista mesas e livros de receita, "folio" abre o Folio como o livro do mundo faz, "fechar" fecha; MESA = índice da mesa na lista.
using System.Collections;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const string ACAO = "abrir";
    const int MESA = 0;

    public static IEnumerator Run()
    {
        var all = MainGame.Instance.GameSave.WorldData.gameSceneDataList.SelectMany(s => s.wgoDataList).Where(d => d != null && d.Definition != null).ToList();
        var mesas = all.Where(d => d.Definition.interactionType == WGODef.InteractionType.Alchemy).ToList();
        var livros = all.Where(d => d.id.Contains("recipe_book")).ToList();
        if (ACAO == "listar")
        {
            yield return "mesas: " + string.Join(", ", mesas.Select(d => d.id + "@" + d.WorldId)) + " | livros: " + string.Join(", ", livros.Select(d => d.id + "@" + d.WorldId));
            yield break;
        }
        var win = LazyUI.GetWindow<UIAlchemyWindow>();
        if (ACAO == "fechar")
        {
            var folio = LazyUI.GetWindow<UIAlchemyFolioWindow>();
            if (folio.IsShown) folio.Close();
            if (win.IsShown) win.Close();
            yield return "fechada"; yield break;
        }
        if (ACAO == "folio")
        {
            var fd = new UIAlchemyFolioWindowData();
            fd.FillFromGaveSave(livros.FirstOrDefault());
            LazyUI.GetWindow<UIAlchemyFolioWindow>().Open(fd);
            for (int i = 0; i < 20; i++) yield return null;
            yield return "folio aberto"; yield break;
        }
        var mesa = mesas.Skip(MESA).FirstOrDefault();
        if (mesa == null) { yield return "nenhuma mesa de alquimia no save"; yield break; }
        Wgo view = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).FirstOrDefault(w => w.HasData && w.Data == mesa);
        if (view == null || (view.transform.position - MainGame.PlayerController.transform.position).magnitude > 4f)
        {
            var tp = new WgoTeleportData(mesa.id, "indoor", "", true);
            AccessTools.Field(typeof(WgoTeleportData), "wgoData").SetValue(tp, mesa);
            AccessTools.Field(typeof(WgoTeleportData), "gameSceneData").SetValue(tp, MainGame.WorldData.GetGameSceneDataById(mesa.WorldId));
            PlayerController.Teleport(tp);
            yield return new WaitForSecondsRealtime(4f);
            view = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).FirstOrDefault(w => w.HasData && w.Data == mesa);
        }
        if (view == null) { yield return "mesa não carregou: " + mesa.id; yield break; }
        if (mesa.CraftComponent.IsStarted) { yield return "mesa ocupada (mistura em andamento): " + mesa.id; yield break; }
        win.Open(new UIAlchemyWindowData(view));
        for (int i = 0; i < 20; i++) yield return null;
        yield return "aberta: " + mesa.id;
    }
}
