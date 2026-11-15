using System.Collections;
using System.Linq;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Jogo novo de gravação (Steam_2): põe em andamento quests do começo (INICIAR) e conclui as de CONCLUIR, para a árvore de
// quests e o diário terem o que mostrar sem spoiler; com SALVAR, grava o slot. Só green.
public static class Snippet
{
    static string INICIAR = "6_intro_guards_burial,7_village_head_talk";
    static string CONCLUIR = "6_intro_guards_burial";
    static bool SALVAR = true;

    public static IEnumerator Run()
    {
        var q = MainGame.Instance.GameSave.questSystemData;
        var sb = new StringBuilder();
        foreach (string id in INICIAR.Split(',').Where(s => s.Length > 0)) { q.StartQuest(id); yield return new WaitForSecondsRealtime(1.5f); }
        foreach (string id in CONCLUIR.Split(',').Where(s => s.Length > 0)) { q.CompleteQuest(id); yield return new WaitForSecondsRealtime(1.5f); }
        yield return new WaitForSecondsRealtime(2f);
        foreach (string id in (INICIAR + "," + CONCLUIR).Split(',').Where(s => s.Length > 0).Distinct())
        {
            string st = new[] { QuestStatus.InProgress, QuestStatus.Completed }.Where(s => q.IsQuestInStatus(id, s)).Select(s => s.ToString()).FirstOrDefault() ?? "?";
            sb.Append(id + "=" + st + " ");
        }
        if (SALVAR)
        {
            bool ok = false;
            SaveSystem.Save(MainGame.Instance.SaveSlotData, MainGame.Instance.GameSave, () => ok = true, null);
            yield return new WaitForSecondsRealtime(2f);
            sb.Append("· salvo " + MainGame.Instance.SaveSlotData.slotName + " " + (ok ? "ok" : "FALHOU"));
        }
        yield return sb.ToString();
    }
}
