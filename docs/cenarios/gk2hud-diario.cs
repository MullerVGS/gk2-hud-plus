using System.Collections;
using System.Linq;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// GK2Hud (só green, MUDA o save de teste): gera anotações para o diário. FASE = "desbloquear" libera a próxima tech
// disponível com as receitas/construções dela (como a compra na Tech Tree, sem gastar pontos), dá PERK e conclui a quest
// da árvore em andamento; "virar" leva o relógio a 23:57 e espera o dia virar. Volte o save com `gk2 save reset`.
public static class Snippet
{
    static string FASE = "desbloquear";
    const string PERK = "perk_axeman";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        var gs = MainGame.Instance.GameSave;
        var ks = gs.knowledgeSystem;
        var sb = new StringBuilder();
        if (FASE == "virar")
        {
            int day = gs.environmentData.Day;
            EnvironmentEngine.Instance.SetTimeOfDay(0.998f);
            float t0 = Time.realtimeSinceStartup;
            while (gs.environmentData.Day == day && Time.realtimeSinceStartup - t0 < 5f) yield return null;
            yield return "dia " + day + " → " + gs.environmentData.Day + " " + gs.environmentData.TimeOfDay.ToString("0.000");
            yield break;
        }
        TechDef tech = GameBalance.Me.techDefs.FirstOrDefault(t => !ks.IsTechUnlocked(t.id) && t.ParentsUnlocked && t.craftsAfterUnlock.Count > 0);
        if (tech != null)
        {
            ks.UnlockTech(tech.id, true);
            foreach (string c in tech.craftsAfterUnlock) ks.UnlockCraft(c);
            foreach (string b in tech.buildingsAfterUnlock) ks.UnlockBuilding(b);
            sb.AppendLine("tech " + tech.id + " crafts=" + string.Join(",", tech.craftsAfterUnlock) + " buildings=" + string.Join(",", tech.buildingsAfterUnlock));
        }
        if (!gs.perkSystemData.HasPerk(PERK)) { gs.perkSystemData.AddPerk(PERK); sb.AppendLine("perk " + PERK); }
        var quest = gs.questSystemData.questCollection.quests.FirstOrDefault(q => q.status == QuestStatus.InProgress && q.Definition != null && q.Definition.hasPosInBalance);
        if (quest != null) { gs.questSystemData.CompleteQuest(quest.id); sb.AppendLine("quest " + quest.id); }
        yield return new WaitForSecondsRealtime(2.5f);   // o diário varre a cada 2 s
        object j = AccessTools.Property(AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance).GetType(), "Journal").GetValue(AccessTools.Property(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance.GetType(), "Hud").GetValue(Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance));
        sb.AppendLine(AccessTools.Method(j.GetType(), "Describe").Invoke(j, null).ToString());
        yield return sb.ToString();
    }
}
