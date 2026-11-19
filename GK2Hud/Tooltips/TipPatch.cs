using System.Collections.Generic;
using System.Reflection;
using GK2Hud.World;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// Redesenho no lugar de um <c>UITooltip</c> já aberto (reconciliação, como num DOM virtual). Mostrar de novo pelo
    /// caminho nativo (<c>ShowAtTargetAndCalculateOffsets</c>) desliga e religa todos os widgets, chama
    /// <c>Canvas.ForceUpdateCanvases()</c> (todos os canvases do jogo), força o layout e refaz a animação de entrada; numa
    /// contagem regressiva isso acontecia a cada mudança de texto. Quando a lista nova tem os mesmos tipos na mesma ordem
    /// que os widgets à mostra, só os itens cuja chave mudou recebem <c>Draw</c>, e o layout fica marcado para o passe
    /// normal do frame.
    /// </summary>
    internal static class TipPatch
    {
        private static readonly FieldInfo ContainerField = AccessTools.Field(typeof(UITooltip), "widgetContainer");
        private static readonly FieldInfo DisplayedField = AccessTools.Field(typeof(LazyWidgetContainer), "displayedWidgets");
        private static readonly FieldInfo DataListField = AccessTools.Field(typeof(LazyWidgetContainer), "dataList");

        /// <summary>
        /// Chaves por item, depois de tirar os separadores do fim como o <c>ShowAtPosition</c> nativo faz (ele altera a
        /// própria lista; aqui também, para as duas contas baterem).
        /// </summary>
        public static string[] Keys(List<LazyWidgetDataBase> list)
        {
            while (list.Count > 0 && list[list.Count - 1] is UITooltipSeparatorWidgetData) list.RemoveAt(list.Count - 1);
            var keys = new string[list.Count];
            for (int i = 0; i < list.Count; i++) keys[i] = WorldContent.ItemKey(list[i]);
            return keys;
        }

        /// <summary>
        /// Tenta redesenhar <paramref name="tip"/> no lugar. Falso quando a estrutura mudou (número ou tipo dos itens) ou
        /// não há o que comparar: aí quem chamou segue pelo caminho nativo e guarda <see cref="Keys"/> da lista mostrada.
        /// </summary>
        public static bool TryPatch(UITooltip tip, List<LazyWidgetDataBase> list, string[] shown, out string[] keys)
        {
            keys = Keys(list);
            if (tip == null || shown == null || !tip.gameObject.activeSelf || shown.Length != keys.Length) return false;
            if (!(ContainerField?.GetValue(tip) is LazyWidgetContainer container)) return false;
            if (!(DisplayedField?.GetValue(container) is List<LazyWidgetBase> displayed) || displayed.Count != keys.Length) return false;
            for (int i = 0; i < keys.Length; i++)
            {
                LazyWidgetBase w = displayed[i];
                if (w == null || !w.gameObject.activeSelf || w.GetDataType() != list[i].GetType()) return false;
            }
            bool changed = false;
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i] == shown[i]) continue;
                displayed[i].Draw(list[i]);
                changed = true;
            }
            DataListField?.SetValue(container, list);
            if (changed) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)tip.transform);
            return true;
        }
    }
}
