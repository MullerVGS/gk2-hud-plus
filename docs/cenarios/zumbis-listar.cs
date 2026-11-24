using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Lista os zumbis do save carregado (só leitura): função, posto, zona, caveiras, coleira, pontos, maestria, perks, ferramentas e fila.
public static class Snippet
{
    static string L(string id) { if (string.IsNullOrEmpty(id)) return ""; var t = LLBase.L(id); return string.IsNullOrEmpty(t) || t == id ? id : t + " (" + id + ")"; }

    public static object Run()
    {
        var zs = MainGame.Instance.GameSave.zombieSystemData;
        var sb = new StringBuilder();
        var onScene = new HashSet<System.Guid>(zs.zombieOnSceneWgoIds.Select(g => g.Guid));
        foreach (var kv in zs.Cache)
        {
            var z = kv.Value;
            sb.AppendLine("=== " + LLBase.L(z.Name ?? "") + " [" + z.Name + "]" + " | " + z.ZombieType + (onScene.Contains(kv.Key) ? " | na cena" : " | guardado"));
            WgoData host = null;
            try { host = z.AttachedWgoData; } catch { }
            if (host != null) sb.AppendLine("  posto: " + L(host.id) + " | zona: " + (host.WorldZoneData != null ? host.WorldZoneData.id : "-") + " | talento do posto: " + host.Definition.talent);
            else sb.AppendLine("  posto: - | zona: " + (z.WorldZoneData != null ? z.WorldZoneData.id : "-"));
            Item collar = null; try { collar = z.Collar; } catch { }
            int used = z.GetUsedPerksCount();
            sb.AppendLine("  caveiras: vermelhas " + z.RedSkulls + " brancas " + z.WhiteSkulls + " | perks usados " + used + "/" + z.RedSkulls + " | coleira " + (collar != null && !collar.IsEmpty ? collar.id : "-") + " | desativados " + z.disabledTalentLevelUps.Count);
            sb.AppendLine("  pontos: vermelho " + z.techRed + " verde " + z.techGreen + " azul " + z.techBlue);
            var organs = z.ZombieItem.Inventory.Where(i => i != null && !i.IsEmpty).Select(i => i.id + (i.Count > 1 ? "x" + i.Count : ""));
            sb.AppendLine("  corpo: " + string.Join(", ", organs));
            foreach (var t in z.talentData)
            {
                if (t.studiedLevelUps.Count == 0 && t.curTalentValue == 0) continue;
                sb.AppendLine("  " + t.id + " valor " + t.curTalentValue + ": " + string.Join(", ", t.studiedLevelUps.Select(s => z.disabledTalentLevelUps.Contains(s) ? s + "[off]" : s)));
            }
            var tools = z.WorkerToolInventory.Data.Inventory.Where(i => i != null && !i.IsEmpty).Select(i => i.id + " (+" + i.Definition.talentBonus + " " + string.Join("/", i.Definition.talentIds ?? new List<string>()) + ")");
            sb.AppendLine("  ferramentas: " + string.Join(", ", tools));
            try { var w = z.Weapon; if (w != null && !w.IsEmpty) sb.AppendLine("  arma: " + w.id); } catch { }
            if (host != null && host.CraftComponent != null)
            {
                var cc = host.CraftComponent;
                var cur = cc.CurrentCraftElement;
                if (cur != null)
                {
                    int m = z.GetMasteryLevelForTalentBranch(host.Definition.talent, cur.Def);
                    sb.AppendLine("  craft: " + cur.CraftId + " | status " + cc.Status + " | maestria " + m + "/" + cur.Def.talentLock + " | ferramenta ok " + z.HasToolForWork(host, cur.Def) + " | infinito " + cur.IsInfinite);
                }
                if (cc.AvailableCrafts != null && cc.AvailableCrafts.Count > 0)
                    sb.AppendLine("  receitas: " + string.Join(", ", cc.AvailableCrafts.Where(d => d != null).Take(30).Select(d => d.id + " " + z.GetMasteryLevelForTalentBranch(host.Definition.talent, d) + "/" + d.talentLock + (d.isStarCraft ? "*" : "") + (z.HasToolForWork(host, d) ? "" : " sem-ferr") + (d.linkedPerks != null && d.linkedPerks.Count > 0 ? " {" + string.Join("/", d.linkedPerks) + "}" : ""))));
                if (cc.CraftElementsQueue.Count > 0)
                    sb.AppendLine("  fila: " + string.Join(", ", cc.CraftElementsQueue.Select(e => e.CraftId + (e.IsInfinite ? "∞" : ""))));
            }
            var act = z.WorkerActivity;
            sb.AppendLine("  atividade: " + (act != null ? act.GetType().Name : "-"));
        }
        return sb.ToString();
    }
}
