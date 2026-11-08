using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace GK2Shared
{
    /// <summary>
    /// Troca plugins BepInEx em execução por uma versão nova sem reiniciar o jogo.
    /// Usado pela DevBridge (green) e pelo HotLoader (blue). O plugin recarregado precisa desfazer tudo no OnDestroy.
    /// </summary>
    internal static class PluginReloader
    {
        private static int generation;

        /// <summary>Carrega com nome único: o Mono devolve o assembly em cache quando o nome se repete.</summary>
        public static Assembly LoadRenamed(byte[] raw)
        {
            using (MemoryStream input = new MemoryStream(raw))
            using (Mono.Cecil.AssemblyDefinition def = Mono.Cecil.AssemblyDefinition.ReadAssembly(input))
            using (MemoryStream output = new MemoryStream())
            {
                string name = def.Name.Name + "_hot" + (++generation) + "_" + DateTime.Now.Ticks;
                def.Name.Name = name;
                def.MainModule.Name = name + ".dll";
                def.Write(output);
                return Assembly.Load(output.ToArray());
            }
        }

        public static List<Type> PluginTypes(Assembly asm)
        {
            return asm.GetTypes().Where(t => typeof(BaseUnityPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.GetCustomAttributes(typeof(BepInPlugin), false).Length > 0).ToList();
        }

        public static string GuidOf(Type pluginType) => ((BepInPlugin)pluginType.GetCustomAttributes(typeof(BepInPlugin), false)[0]).GUID;

        /// <summary>Destrói a instância em execução (carregada pelo Chainloader ou por reload anterior). Destroy é adiado ao fim do frame.</summary>
        public static bool DestroyExisting(string guid, Dictionary<string, BaseUnityPlugin> loaded)
        {
            bool any = false;
            if (loaded.TryGetValue(guid, out BaseUnityPlugin hot) && hot != null)
            {
                UnityEngine.Object.Destroy(hot);
                any = true;
            }
            loaded.Remove(guid);
            if (Chainloader.PluginInfos.TryGetValue(guid, out PluginInfo info))
            {
                if (info.Instance != null && info.Instance != hot)
                {
                    UnityEngine.Object.Destroy(info.Instance);
                    any = true;
                }
                Chainloader.PluginInfos.Remove(guid);
            }
            return any;
        }

        /// <summary>Cria a versão nova. Chamar um frame depois do DestroyExisting.</summary>
        public static BaseUnityPlugin Start(Type pluginType, GameObject host, Dictionary<string, BaseUnityPlugin> loaded)
        {
            BaseUnityPlugin plugin = (BaseUnityPlugin)host.AddComponent(pluginType);
            string guid = plugin.Info.Metadata.GUID;
            loaded[guid] = plugin;
            Chainloader.PluginInfos[guid] = plugin.Info;
            return plugin;
        }
    }
}
