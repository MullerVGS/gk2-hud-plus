using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

// Funcionalidades do GK2 HUD+: ACAO "listar" (estado e patches de cada uma), "ligar"/"desligar" (IDS separados por vírgula,
// pelo toggle do .cfg, como o menu faz) ou "ciclo" (desliga e religa cada funcionalidade ligada, conferindo que os
// patches saem e voltam).
public static class Snippet
{
    static string ACAO = "listar";
    static string IDS = "";

    const string Guid = "mullervgs.gk2.hudplus";

    public static IEnumerator Run()
    {
        object plugin = Chainloader.PluginInfos[Guid].Instance;
        object host = AccessTools.Property(plugin.GetType(), "Host").GetValue(plugin);
        var features = ((IEnumerable)AccessTools.Property(host.GetType(), "Features").GetValue(host)).Cast<object>().ToList();
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        object P(object o, string n) => o.GetType().GetField(n, all) is FieldInfo fi ? fi.GetValue(o) : o.GetType().GetProperty(n, all)?.GetValue(o);
        ConfigEntry<bool> Toggle(object f) => (ConfigEntry<bool>)P(f, "Toggle");
        object Find(string id) => features.First(f => (string)P(f, "Id") == id);
        int Patches(string id) => Harmony.GetAllPatchedMethods().Count(m => Harmony.GetPatchInfo(m).Owners.Contains(Guid + "." + id.ToLowerInvariant()));
        string Line(object f)
        {
            string id = (string)P(f, "Id");
            bool option = (bool)P(f, "IsOption");
            object parent = P(f, "Parent");
            string state = (bool)P(f, "Failed") ? "FALHOU" : (bool)P(f, "Running") ? "ligada" : "desligada";
            string extra = option ? "opção de " + P(parent, "Id") : Patches(id) + " patches";
            return $"{id,-14} {state,-9} [{Toggle(f).Definition.Section}] {Toggle(f).Definition.Key} = {Toggle(f).Value} · {P(f, "Category")} · {extra}";
        }

        var sb = new StringBuilder();
        if (ACAO == "ligar" || ACAO == "desligar")
        {
            foreach (string id in IDS.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)) Toggle(Find(id)).Value = ACAO == "ligar";
            yield return null;
            yield return null;
        }
        else if (ACAO == "ciclo")
        {
            foreach (object f in features.Where(f => !(bool)P(f, "IsOption") && (bool)P(f, "Running")).ToList())
            {
                string id = (string)P(f, "Id");
                int antes = Patches(id);
                Toggle(f).Value = false;
                yield return null;
                yield return null;
                bool off = !(bool)P(f, "Running");
                int meio = Patches(id);
                Toggle(f).Value = true;
                yield return null;
                yield return null;
                sb.AppendLine($"{id,-14} desligou={off} patches {antes}→{meio}→{Patches(id)} religou={(bool)P(f, "Running")}");
            }
            yield return sb.ToString();
            yield break;
        }
        foreach (object f in features) sb.AppendLine(Line(f));
        yield return sb.ToString();
    }
}
