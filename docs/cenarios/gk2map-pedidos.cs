using System.Collections;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// GK2Map: abre o mapa (aba Map do TAB) e passa o mouse no ícone dos pedidos da cidade (dia da Pride); ACAO "sair" tira o mouse.
public static class Snippet
{
    const string ACAO = "entrar";

    public static IEnumerator Run()
    {
        var w = LazyUI.GetWindow<CharacterWindow>();
        var page = w.transform.Find("Content/Pages/MapPage");
        if (!w.IsShown || page == null || !page.gameObject.activeInHierarchy)
        {
            if (w.IsShown) { w.Close(); yield return null; yield return null; }
            Transform pages = w.transform.Find("Content/Pages");
            if (pages != null) foreach (Transform p in pages) if (p.GetComponent<LazyWidgetBase>() != null) p.gameObject.SetActive(false);
            w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map));
            yield return null; yield return null;
        }
        UIMapDayActivityIcon pride = null;
        foreach (var ic in w.GetComponentsInChildren<UIMapDayActivityIcon>(false))
            if ((MapDayType)AccessTools.Field(typeof(UIMapDayActivityIcon), "dayType").GetValue(ic) == MapDayType.Pride) pride = ic;
        if (pride == null) { yield return "sem ícone da Pride no mapa (dia não liberado?)"; yield break; }
        var scroll = pride.GetComponentInParent<UnityEngine.UI.ScrollRect>();
        if (scroll != null && ACAO != "sair")
        {
            Vector3 ic = scroll.viewport.InverseTransformPoint(pride.transform.position), mid = scroll.viewport.rect.center;
            scroll.StopMovement();
            scroll.content.anchoredPosition -= (Vector2)(ic - (Vector3)mid) - new Vector2(-120f, -60f);
            yield return null; yield return null;
        }
        AccessTools.Method(typeof(UIMapDayActivityIcon), ACAO == "sair" ? "OnHoverExit" : "OnHoverEnter").Invoke(pride, null);
        yield return null; yield return null;
        var r = (RectTransform)pride.transform;
        Vector3[] c = new Vector3[4];
        r.GetWorldCorners(c);
        var vs = MainGame.Instance.GameSave.vendorSystem;
        yield return $"{ACAO}: {vs.currentOrders.Count} pedidos; ícone em {c[0].x:0},{Screen.height - c[1].y:0}";
    }
}
