using System.Collections;
using System.Linq;
using UnityEngine;
using LazyBearTechnology;

// Abre o inventário (CharacterWindow) e mostra o tooltip nativo de um item visível na tela. Troque ITEM por parte do id (vazio = primeiro).
public static class Snippet
{
    const string ITEM = "brick";

    public static object Run() { return Go(); }

    static IEnumerator Go()
    {
        if (!LazyUI.GetWindow<CharacterWindow>().gameObject.activeInHierarchy)
            LazyUI.GetWindow<CharacterWindow>().Open(new CharacterWindowData(MainGame.Instance.GameSave));
        for (int i = 0; i < 20; i++) yield return null;
        var cells = Object.FindObjectsByType<UIItemCell>(FindObjectsSortMode.None)
            .Where(c => c.isActiveAndEnabled && c.DisplayingItem != null && !string.IsNullOrEmpty(c.DisplayingItem.id) && c.DisplayingItem.id != "empty"
                && c.transform.position.y > 100 && c.transform.position.y < 980 && c.transform.position.x > 0 && c.transform.position.x < 1920)
            .ToList();
        Debug.Log("[cenario] células com item: " + string.Join(", ", cells.Select(c => c.DisplayingItem.id)));
        var cell = cells.FirstOrDefault(c => ITEM == "" || c.DisplayingItem.id.Contains(ITEM));
        if (cell == null) { yield return "nenhuma célula com " + ITEM; yield break; }
        UITooltip.ShowItemCell(cell);
        for (int i = 0; i < 5; i++) yield return null;
        yield return cell.DisplayingItem.id + " | itens: " + string.Join(", ", cells.Select(c => c.DisplayingItem.id));
    }
}
