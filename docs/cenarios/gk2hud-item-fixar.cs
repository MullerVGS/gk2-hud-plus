// GK2Hud (só leitura): abre o inventário, mostra o tooltip do item ITEM (parte do id; vazio = primeiro) e, com FIXAR, fixa a bolha com o "botão do meio" virtual (o detalhe só aparece fixado). Deixa a janela aberta.
using System.Collections;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const string ITEM = "";
    const bool FIXAR = true;

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        object plugin = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance;
        object hud = AccessTools.Property(plugin.GetType(), "Hud").GetValue(plugin);
        object tips = AccessTools.Property(hud.GetType(), "Tooltips").GetValue(hud);
        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        for (int i = 0; i < 20; i++) yield return null;
        var cells = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .Where(c => c.isActiveAndEnabled && c.DisplayingItem != null && !string.IsNullOrEmpty(c.DisplayingItem.id) && c.DisplayingItem.id != "empty"
                && c.transform.position.y > 100 && c.transform.position.y < 980 && c.transform.position.x > 0 && c.transform.position.x < 1920)
            .ToList();
        var cell = cells.FirstOrDefault(c => ITEM == "" || c.DisplayingItem.id.Contains(ITEM));
        if (cell == null) { yield return "nenhuma célula com " + ITEM + " | " + string.Join(", ", cells.Select(c => c.DisplayingItem.id)); yield break; }
        UITooltip.ShowItemCell(cell);
        for (int i = 0; i < 5; i++) yield return null;
        if (FIXAR)
        {
            AccessTools.Field(tips.GetType(), "TestPin").SetValue(tips, true);
            for (int i = 0; i < 5; i++) yield return null;
        }
        yield return cell.DisplayingItem.id + " | " + (string)AccessTools.Method(tips.GetType(), "Describe").Invoke(tips, null);
    }
}
