using System;
using GK2Shared;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2HudPlus.Menu
{
    /// <summary>
    /// Menu do GK2 HUD+: botão "GK2 HUD+" no menu de pausa, logo abaixo de Controls (clonado do botão Settings, como o
    /// jogo cria o "Report a bug"), que abre a <see cref="HudPlusWindow"/> e volta à pausa ao fechar, como Settings e
    /// Controls fazem. Parte do ModGuard fora da lista de funcionalidades: o menu não desliga a si mesmo.
    /// </summary>
    internal sealed class MenuPart : IDisposable
    {
        internal static MenuPart Current;

        private readonly ModHost host;
        private HudPlusWindow window;
        private GameObject button;
        private UIGamePauseWindow pause;

        internal HudPlusWindow Window => window != null ? window : (window = HudPlusWindow.Create(host));

        public MenuPart(Harmony h, ModHost host)
        {
            this.host = host;
            // Janela órfã de uma carga anterior (hot-reload no meio de uma falha): sai antes de tudo.
            foreach (Transform t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (t != null && t.name == "GK2HudPlus_Window") Object.DestroyImmediate(t.gameObject);
            h.Patch(AccessTools.DeclaredMethod(typeof(UIGamePauseWindow), nameof(UIGamePauseWindow.Open), new[] { typeof(LazyWidgetDataBase) }),
                postfix: new HarmonyMethod(typeof(MenuPart), nameof(PauseOpenPostfix)));
            Current = this;
            UIGamePauseWindow p = LazyUI.IsInitialized ? LazyUI.GetWindow<UIGamePauseWindow>() : null;
            if (p != null && p.IsShown) AddButton(p);
        }

        private static void PauseOpenPostfix(UIGamePauseWindow __instance) => Current?.AddButton(__instance);

        private void AddButton(UIGamePauseWindow p)
        {
            if (button != null && pause == p) return;
            if (button != null) Object.Destroy(button);
            pause = p;
            var settings = Traverse.Create(p).Field("settingsBtn").GetValue<LazyButton>();
            var controls = Traverse.Create(p).Field("controlsBtn").GetValue<LazyButton>();
            if (settings == null) throw new InvalidOperationException("settingsBtn não encontrado no menu de pausa");
            button = Object.Instantiate(settings.gameObject, settings.transform.parent);
            button.name = "GK2HudPlus_Menu";
            LazyButton b = button.GetComponent<LazyButton>();
            b.onClick.RemoveAllListeners();
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++) b.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            b.onClick.AddListener(Open);
            b.SetCallbacksIntoGamepadNavigationItem();
            foreach (LocalizedLabel l in button.GetComponentsInChildren<LocalizedLabel>(includeInactive: true)) l.IgnoreLocalize = true;
            foreach (TextMeshProUGUI t in button.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true)) t.text = MenuText.Button.Current;
            Transform after = controls != null && controls.gameObject.activeSelf ? controls.transform : settings.transform;
            button.transform.SetSiblingIndex(after.GetSiblingIndex() + 1);
            button.SetActive(true);
            ((RectTransform)p.transform).RefreshContentFitter();
            if (LazyInput.IsGamepadActive) p.GamepadNavigationControllerReinit();
        }

        /// <summary>Fecha a pausa e abre a janela; ao fechar a janela, a pausa volta (como Settings e Controls).</summary>
        internal void Open()
        {
            UIGamePauseWindow p = pause;
            if (p != null && p.IsShown) p.Close();
            Window.Open(null, _ => { if (p != null) p.Open(null); });
        }

        public void Dispose()
        {
            if (Current == this) Current = null;
            if (button != null) Object.Destroy(button);
            button = null;
            if (pause != null && pause.IsShown) ((RectTransform)pause.transform).RefreshContentFitter();
            if (window != null) window.Teardown();
            window = null;
        }
    }

    internal static class PauseExtensions
    {
        public static void GamepadNavigationControllerReinit(this UIGamePauseWindow p)
        {
            var nav = p.GetComponent<GamepadNavigationController>();
            if (nav != null) nav.ReinitItems(focusOnFirstActive: false);
        }
    }
}
