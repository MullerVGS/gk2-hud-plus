using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Kit dos cenários de demonstração (clipes do menu do GK2 HUD+, gravados pelo `gk2 rec`). Compilado junto com todo
/// cenário. Posições em pixels de tela com origem em cima (como o recorte do `gk2 shot`); o kit converte para a origem
/// embaixo dos mouses virtuais.
/// </summary>
public static class Demo
{
    public const string HudPlus = "mullervgs.gk2.hudplus";

    public static object Plugin => Chainloader.PluginInfos[HudPlus].Instance;

    /// <summary>Módulo do HUD+ ("Hud", "Map", "Saves", "Refund").</summary>
    public static object Module(string name) => AccessTools.Property(Plugin.GetType(), name).GetValue(Plugin);

    public static object Prop(object o, string name) => o == null ? null : AccessTools.Property(o.GetType(), name)?.GetValue(o);

    /// <summary>Funcionalidade do módulo Hud pelo nome da propriedade (Tooltips, HudInfo, WorldHover, Wheel...).</summary>
    public static object Hud(string feature) => Prop(Module("Hud"), feature);

    private static Vector2? mouse;

    public static Vector2? CurrentMouse => mouse;

    /// <summary>
    /// Cursor desenhado no clipe e mouse virtual de todas as funcionalidades que têm um (HUD, tooltips, mundo,
    /// medalhão, mapa), no mesmo ponto. null tira o cursor.
    /// </summary>
    public static void Mouse(Vector2? topLeft)
    {
        mouse = topLeft;
        Type rec = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("GK2DevBridge.Recorder")).FirstOrDefault(t => t != null);
        rec?.GetField("Cursor").SetValue(null, topLeft);
        Vector2? bottomLeft = topLeft.HasValue ? (Vector2?)new Vector2(topLeft.Value.x, Screen.height - topLeft.Value.y) : null;
        foreach (string f in new[] { "Tooltips", "HudInfo", "WorldHover", "Wheel" })
        {
            object feature = Hud(f);
            AccessTools.Property(feature?.GetType(), "VirtualMouse")?.SetValue(feature, bottomLeft);
        }
        Type zoom = Plugin.GetType().Assembly.GetType("GK2Map.MapZoom");
        AccessTools.Property(zoom, "VirtualMouse")?.SetValue(null, bottomLeft);
    }

    public static IEnumerator Wait(float seconds)
    {
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime) yield return null;
    }

    /// <summary>Cursor deslizando com aceleração suave, como uma mão de verdade.</summary>
    public static IEnumerator Glide(Vector2 to, float seconds = 0.6f)
    {
        Vector2 from = mouse ?? to;
        for (float t = 0; t < seconds; t += Time.unscaledDeltaTime)
        {
            Mouse(Vector2.Lerp(from, to, Mathf.SmoothStep(0, 1, t / seconds)));
            yield return null;
        }
        Mouse(to);
    }

    /// <summary>Centro de um elemento de UI (canvas overlay), origem em cima.</summary>
    public static Vector2 Center(Transform t)
    {
        var c = new Vector3[4];
        ((RectTransform)t).GetWorldCorners(c);
        Vector2 m = (c[0] + c[2]) / 2f;
        return new Vector2(Mathf.Round(m.x), Mathf.Round(Screen.height - m.y));
    }

    /// <summary>Ponto da tela de um objeto do mundo (câmera do mundo → tela), origem em cima.</summary>
    public static Vector2 World(Vector3 position)
    {
        CameraSystem cs = CameraSystem.Instance;
        Vector3 v = cs.WorldCamera.WorldToScreenPoint(position);
        MainCamera mc = cs.MainCamera;
        if (mc != null && mc.RenderTexture != null) v = mc.RenderTargetToScreen(v);
        return new Vector2(Mathf.Round(v.x), Mathf.Round(Screen.height - v.y));
    }

    private static PointerEventData Event(Vector2? topLeft, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
    {
        Vector2 p = topLeft ?? mouse ?? Vector2.zero;
        return new PointerEventData(EventSystem.current) { position = new Vector2(p.x, Screen.height - p.y), button = button };
    }

    /// <summary>Hover num elemento de UI nativo (pointer-enter), como o mouse real faz.</summary>
    public static void Enter(GameObject go) => ExecuteEvents.Execute(go, Event(Center(go.transform)), ExecuteEvents.pointerEnterHandler);

    public static void Exit(GameObject go) => ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current) { fullyExited = true }, ExecuteEvents.pointerExitHandler);

    public static void Click(GameObject go, PointerEventData.InputButton button = PointerEventData.InputButton.Left)
    {
        LazyButton b = go.GetComponent<LazyButton>();
        if (b != null && button == PointerEventData.InputButton.Left) { b.onClick.Invoke(); return; }
        ExecuteEvents.Execute(go, Event(Center(go.transform), button), ExecuteEvents.pointerClickHandler);
    }

    /// <summary>Desliza até o elemento, para em cima (hover) e espera.</summary>
    public static IEnumerator HoverOn(GameObject go, float glide = 0.6f, float stay = 1.2f)
    {
        yield return Glide(Center(go.transform), glide);
        Enter(go);
        yield return Wait(stay);
    }

    /// <summary>Primeiro GameObject ativo cujo nome é <paramref name="name"/> (ou contém, com <paramref name="contains"/>).</summary>
    public static GameObject Find(string name, bool contains = false, Transform under = null)
    {
        IEnumerable<Transform> all = under != null ? under.GetComponentsInChildren<Transform>(false)
            : UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return all.FirstOrDefault(t => contains ? t.name.Contains(name) : t.name == name)?.gameObject;
    }

    public static void Log(string text) => Debug.Log("[demo] " + text);
}
