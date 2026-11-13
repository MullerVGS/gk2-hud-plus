using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

// ModGuard de um mod (MOD = GUID, PARTE = id). ACAO: "estado" (partes e finalizers), "finalizer" (exceção do mod é
// engolida e contada; a do jogo passa), "desligar" (5 erros → a parte desliga, solta os patches e mostra o aviso nativo).
// "desligar" deixa a parte desligada até recarregar o mod (gk2 deploy) ou reabrir o green.
public static class Snippet
{
    static string MOD = "arthur.gk2.zombierefund";
    static string PARTE = "Refund";
    static string ACAO = "finalizer";

    public static IEnumerator Run()
    {
        var sb = new StringBuilder();
        object plugin = Chainloader.PluginInfos[MOD].Instance;
        object guard = AccessTools.Property(plugin.GetType(), "Guard").GetValue(plugin);
        Type g = guard.GetType();
        var byOriginal = (IDictionary)AccessTools.Field(g, "ByOriginal").GetValue(null);
        string harmonyId = MOD + "." + PARTE.ToLowerInvariant();
        List<MethodBase> patched = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(harmonyId)).ToList();

        string Estado()
        {
            var off = (string[])AccessTools.Property(g, "OffParts").GetValue(guard);
            bool on = (bool)AccessTools.Method(g, "IsOn").Invoke(guard, new object[] { PARTE });
            var now = Harmony.GetAllPatchedMethods().Where(m => Harmony.GetPatchInfo(m).Owners.Contains(harmonyId)).ToList();
            int fin = now.Count(m => Harmony.GetPatchInfo(m).Finalizers.Any(f => f.owner == harmonyId && f.PatchMethod.Name == "Finalizer"));
            return $"{PARTE} {(on ? "ligada" : "desligada")} · {now.Count} métodos patchados, {fin} com finalizer do guard · desligadas: [{string.Join(",", off)}]";
        }

        sb.AppendLine("antes: " + Estado());
        if (ACAO == "finalizer")
        {
            MethodBase original = patched.FirstOrDefault();
            if (original == null) { yield return sb + "sem método patchado"; yield break; }
            MethodInfo fin = AccessTools.Method(g, "Finalizer");
            var deMod = (Exception)AccessTools.Method(g, "SampleException").Invoke(null, null);
            var doJogo = new InvalidOperationException("exceção sem quadro do mod");
            object r1 = fin.Invoke(null, new object[] { deMod, original });
            object r2 = fin.Invoke(null, new object[] { doJogo, original });
            sb.AppendLine($"finalizer em {original.DeclaringType.Name}.{original.Name}: do mod → {(r1 == null ? "engolida" : "REPASSADA")}, do jogo → {(r2 == doJogo ? "repassada" : "ENGOLIDA")}");
        }
        else if (ACAO == "desligar")
        {
            MethodInfo report = AccessTools.Method(g, "Report");
            for (int i = 0; i < 5; i++) report.Invoke(guard, new object[] { PARTE, new Exception("erro de teste " + (i + 1)), "cenário guard-desligar" });
            yield return null;
            yield return null;
            sb.AppendLine("depois: " + Estado());
        }
        yield return sb.ToString();
    }
}
