using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using Newtonsoft.Json;
using UnityEngine;

// `gk2 check GK2HudPlus` (só leitura, green): passa todos os ids do balanço pelas funções de nome e pelos tooltips do GK2Hud
// carregado (textos com cara de id), e exporta as regras de revelação do calendário (EventDef) com o que o save carregado
// sabe (ícones, tutoriais, respostas, quests, recursos). Grava BepInEx/gk2check/gk2hud.json; quem julga é check.py.
public static class Snippet
{
    static readonly Regex Dev = new Regex(@"(^|[\s>(:,])([a-z0-9]+(_[a-z0-9]+)+)([\s<),.:]|$)");
    static string DevTok(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        var m = Dev.Match(Regex.Replace(s, "<[^>]+>", " "));
        return m.Success ? m.Groups[2].Value : null;
    }

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object plugin = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance;   // GK2 HUD+ (traz o GK2Hud)
        Assembly asm = plugin.GetType().Assembly;
        Func<string, Type> T = n => asm.GetTypes().First(t => t.FullName == n);
        Type jv = T("GK2Hud.Journal.JournalView"), jl = T("GK2Hud.Journal.JournalLine"), ent = T("GK2Hud.Tooltips.Entity");
        Type tf = T("GK2Hud.Tooltips.TooltipFeature"), names = T("GK2Hud.Names"), tc = T("GK2Hud.Tooltips.TooltipContent");
        Type cm = T("GK2Hud.Calendar.CalendarModel");
        MethodInfo name = AccessTools.Method(jv, "Name"), tip = AccessTools.Method(jv, "Tooltip");
        MethodInfo wgo = AccessTools.Method(names, "Wgo"), craftName = AccessTools.Method(tc, "CraftName");
        object content = AccessTools.Field(tf, "Content").GetValue(AccessTools.Field(tf, "Current").GetValue(null));
        MethodInfo build = AccessTools.Method(content.GetType(), "Build");
        GameBalance gb = GameBalance.Me;

        var hits = new List<object>();
        int n = 0;
        Action<string, string> check = (where, text) =>
        {
            string tok = DevTok(text);
            if (tok != null) hits.Add(new { where, token = tok, text = Regex.Replace(text, "<[^>]+>", "") });
        };
        Action<string, IEnumerable> scan = (where, list) =>
        {
            if (list == null) return;
            foreach (object d in list) if (d is UITooltipTextWidgetData t) check(where, t.Text);
        };

        // nomes soltos
        var kinds = new Dictionary<string, IEnumerable<string>> {
            { "quest", gb.questDefs.Select(d => d.id) }, { "tech", gb.techDefs.Select(d => d.id) }, { "building", gb.buildingDefs.Select(d => d.id) },
            { "town", gb.townBuildingDefs.Select(d => d.id) }, { "craft", gb.craftDefs.Select(d => d.id) }, { "alchemy", gb.alchemyFormulaDefs.Select(d => d.id) },
            { "perk", gb.perkDefs.Select(d => d.id) }, { "phrase", new[] { "63_swamp_goddess_32_c" } },
        };
        foreach (var kv in kinds)
            foreach (string id in kv.Value.Distinct())
            {
                string nm = (string)name.Invoke(null, new object[] { kv.Key, id });
                check("diário " + kv.Key + ":" + id, nm);
                if (nm != null && kv.Key != "craft")
                {
                    object line = Activator.CreateInstance(jl, true);
                    jl.GetField("Kind").SetValue(line, kv.Key); jl.GetField("Id").SetValue(line, id); jl.GetField("Name").SetValue(line, nm); jl.GetField("Day").SetValue(line, 1);
                    try { scan("tooltip do diário " + kv.Key + ":" + id, (IEnumerable)tip.Invoke(null, new[] { line })); }
                    catch (Exception e) { hits.Add(new { where = "tooltip do diário " + id, token = "EXCEÇÃO", text = e.InnerException?.Message }); }
                }
                if (++n % 50 == 0) yield return null;
            }
        foreach (WGODef d in gb.wgoDefs) check("mundo " + d.id, (string)wgo.Invoke(null, new object[] { d.id }));
        foreach (CraftDef d in gb.craftDefs) check("craft " + d.id, (string)craftName.Invoke(null, new object[] { d }));

        // tooltips completos (itens, receitas, perks, estações)
        MethodInfo fItem = AccessTools.Method(ent, "ForItem"), fCraft = AccessTools.Method(ent, "ForCraft"), fPerk = AccessTools.Method(ent, "ForPerk"), fSt = AccessTools.Method(ent, "ForStation");
        var jobs = new List<KeyValuePair<string, Func<object>>>();
        foreach (ItemDef d in gb.itemDefs) jobs.Add(new KeyValuePair<string, Func<object>>("tooltip item " + d.id, () => fItem.Invoke(null, new object[] { d, null })));
        foreach (CraftDef d in gb.craftDefs) jobs.Add(new KeyValuePair<string, Func<object>>("tooltip craft " + d.id, () => fCraft.Invoke(null, new object[] { d, null })));
        foreach (PerkDef d in gb.perkDefs) jobs.Add(new KeyValuePair<string, Func<object>>("tooltip perk " + d.id, () => fPerk.Invoke(null, new object[] { d })));
        foreach (WGODef d in gb.wgoDefs) jobs.Add(new KeyValuePair<string, Func<object>>("tooltip estação " + d.id, () => fSt.Invoke(null, new object[] { d.id })));
        foreach (var job in jobs)
        {
            try { scan(job.Key, (IEnumerable)build.Invoke(content, new[] { job.Value() })); }
            catch (Exception e) { hits.Add(new { where = job.Key, token = "EXCEÇÃO", text = e.InnerException?.Message ?? e.Message }); }
            if (++n % 50 == 0) yield return null;
        }

        // calendário: regras de revelação e o que o save sabe
        var ks = MainGame.Instance.GameSave.knowledgeSystem;
        var res = AccessTools.Field(typeof(PlayerData), "res").GetValue(MainGame.PlayerData) as GameRes;
        MethodInfo known = AccessTools.Method(cm, "Known"), since = AccessTools.Method(cm, "KnownSince");
        var events = new List<object>();
        foreach (object e in (IEnumerable)AccessTools.Field(cm, "Defs").GetValue(null))
        {
            Func<string, object> f = fn => AccessTools.Field(e.GetType(), fn).GetValue(e);
            string resFlag = (string)f("ResFlag");
            int s = (int)since.Invoke(null, new[] { e });
            string title = (string)AccessTools.Property(e.GetType(), "Title").GetValue(e);
            string desc = (string)AccessTools.Property(e.GetType(), "Description").GetValue(e);
            check("calendário título " + f("Id"), title);
            check("calendário descrição " + f("Id"), desc);
            events.Add(new
            {
                id = f("Id"), dayId = f("DayId"), logicId = f("LogicId"), mapIcons = f("MapIcons"), tutorials = f("Tutorials"),
                quests = f("Quests"), phrases = f("Phrases"), resFlag, nativeDesc = f("NativeDesc"),
                known = (bool)known.Invoke(null, new[] { e }), since = s == int.MinValue ? (int?)null : s,
                resValue = resFlag != null && res != null ? res.GetWithoutSystemsCheck(resFlag) : 0f,
            });
        }
        var quests = MainGame.Instance.GameSave.questSystemData.questCollection.quests;
        var outp = new
        {
            version = AccessTools.Field(plugin.GetType(), "Version")?.GetValue(null) ?? plugin.GetType().GetField("Version")?.GetValue(null),
            lang = LLBase.CurrentLang,
            day = MainGame.Instance.GameSave.environmentData.Day,
            names = hits,
            events,
            save = new
            {
                mapIcons = ks.unlockedMapDayIcons, tutorials = ks.unlockedTutorials, phrases = ks.blackListPhrases,
                questsCompleted = quests.Where(q => q != null && q.status == QuestStatus.Completed).Select(q => q.id).ToList(),
                questsStarted = quests.Where(q => q != null && q.status != QuestStatus.Completed).Select(q => q.id).ToList(),
            },
        };
        string dir = Path.Combine(Paths.BepInExRootPath, "gk2check");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "gk2hud.json"), JsonConvert.SerializeObject(outp, Formatting.Indented));
        yield return "ok: " + n + " verificações, " + hits.Count + " textos suspeitos, " + events.Count + " eventos";
    }
}
