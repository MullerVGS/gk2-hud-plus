using System.Collections;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;

// Demo do clipe "MapOrders" (opção Pedidos do mapa). MODO "abrir" (preparo, fora do clipe) abre o mapa (aba Map do TAB) com
// o ícone dos pedidos da cidade (dia da Pride) à vista; o demo (MODO "pedidos") só move o cursor: para no ícone, o tooltip
// mostra cada pedido, o cursor sai. O mapa fica aberto do começo ao fim (o loop não salta). O save precisa de pedido
// aceito. FIM = "meio" para com o tooltip aberto.
public static class Snippet
{
    static string MODO = "pedidos";
    static string FIM = "";

    static UIMapDayActivityIcon Pride(CharacterWindow w)
    {
        UIMapDayActivityIcon pride = null;
        foreach (var ic in w.GetComponentsInChildren<UIMapDayActivityIcon>(false))
            if ((MapDayType)AccessTools.Field(typeof(UIMapDayActivityIcon), "dayType").GetValue(ic) == MapDayType.Pride) pride = ic;
        return pride ?? throw new System.Exception("sem ícone da Pride no mapa");
    }

    public static IEnumerator Run()
    {
        if (MainGame.Instance.GameSave.vendorSystem.currentOrders.Count == 0) throw new System.Exception("save sem pedido aceito");
        var w = LazyUI.GetWindow<CharacterWindow>();
        if (MODO == "abrir")
        {
            if (w.IsShown) { w.Close(); yield return null; yield return null; }
            w.Open(new CharacterWindowData(MainGame.Instance.GameSave, CharacterWindowData.CharPage.Map));
            yield return null; yield return null;
            var icon = Pride(w);
            var scroll = icon.GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (scroll != null)
            {
                Vector3 ic = scroll.viewport.InverseTransformPoint(icon.transform.position), mid = scroll.viewport.rect.center;
                scroll.StopMovement();
                scroll.content.anchoredPosition -= (Vector2)(ic - (Vector3)mid) - new Vector2(-120f, -60f);
            }
            yield return Demo.Wait(1.0f);
            yield return "demo pedidos ok: mapa aberto no ícone";
            yield break;
        }
        object map = Demo.Module("Map");
        if (!((BepInEx.Configuration.ConfigEntry<bool>)AccessTools.Field(map.GetType(), "OrdersEnabled").GetValue(map)).Value)
            throw new System.Exception("opção [Map] Orders desligada");
        if (!w.IsShown) throw new System.Exception("mapa fechado: rode antes MODO=abrir");
        var pride = Pride(w);
        Vector2 at = Demo.Center(pride.transform);
        Demo.Mouse(at + new Vector2(-260, 150));
        yield return Demo.Wait(0.4f);
        yield return Demo.Glide(at + new Vector2(3, 5), 0.8f);
        Demo.Enter(pride.gameObject);
        yield return Demo.Wait(2.8f);
        if (FIM == "meio") { yield return "parado no ícone dos pedidos em " + at; yield break; }
        Demo.Exit(pride.gameObject);
        yield return Demo.Glide(at + new Vector2(-260, 150), 0.7f);
        yield return Demo.Wait(0.5f);
        Demo.Mouse(null);
        yield return "demo pedidos ok";
    }
}
