using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

namespace GK2Hud.Tooltips
{
    /// <summary>
    /// A tecla de fixar na janela de Controls do jogo: uma linha a mais no fim da lista, com o mesmo elemento nativo
    /// (<see cref="UIGameBindingElement"/>, clique e aperte a tecla). O binding é só nosso: não entra em
    /// <c>LazyInput.GameBindings</c> nem no arquivo de teclas do jogo; o valor vai para <c>[Tooltips] PinKey</c>.
    /// </summary>
    internal static class PinKeyControl
    {
        private static readonly AccessTools.FieldRef<UIGameBindingSettingsWindow, UnityEngine.UI.ScrollRect> ScrollRef =
            AccessTools.FieldRefAccess<UIGameBindingSettingsWindow, UnityEngine.UI.ScrollRect>("scrollRect");
        private static readonly AccessTools.FieldRef<UIGameBindingElement, TextMeshProUGUI> ActionLabelRef =
            AccessTools.FieldRefAccess<UIGameBindingElement, TextMeshProUGUI>("actionLabel");
        private static readonly System.Reflection.MethodInfo LockM = AccessTools.Method(typeof(UIGameBindingSettingsWindow), "SetLockOnBindingButtons");

        private static ConfigEntry<KeyboardShortcut> pinKey;
        private static UIGameBindingElement element;
        private static UIGameBindingSettingsWindow window;

        public static void Patch(Harmony harmony, ConfigEntry<KeyboardShortcut> key)
        {
            pinKey = key;
            Type t = typeof(UIGameBindingSettingsWindow);
            harmony.Patch(AccessTools.Method(t, nameof(UIGameBindingSettingsWindow.Redraw)),
                prefix: new HarmonyMethod(typeof(PinKeyControl), nameof(ReleasePrefix)), postfix: new HarmonyMethod(typeof(PinKeyControl), nameof(AddPostfix)));
            harmony.Patch(AccessTools.Method(t, "OnReset"), postfix: new HarmonyMethod(typeof(PinKeyControl), nameof(ResetPostfix)));
        }

        public static void Dispose()
        {
            Release();
            pinKey = null;
            window = null;
        }

        private static void Release()
        {
            if (element != null && UIPrefabsPooler.Instance != null) UIPrefabsPooler.Instance.ReleaseElementToPool(element);
            element = null;
        }

        private static void ReleasePrefix() => Release();

        private static void AddPostfix(UIGameBindingSettingsWindow __instance)
        {
            if (pinKey == null) return;
            UnityEngine.UI.ScrollRect scroll = ScrollRef(__instance);
            if (scroll == null || scroll.content == null) return;
            window = __instance;
            element = UIPrefabsPooler.Instance.GetElementFromPool<UIGameBindingElement>(scroll.content);
            element.gameObject.SetActive(true);
            element.transform.SetAsLastSibling();
            // GameKey fora da faixa do jogo: o elemento só o usa para comparar com os outros bindings.
            var binding = new KeyBinding { gameKey = new GameKey(-27777), keyCode = pinKey.Value.MainKey, localeId = "" };
            element.Initialize(binding, OnSubmit, () => Lock(true));
            ActionLabelRef(element).text = L10n.T("pin_control");
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        }

        private static void OnSubmit(UIGameBindingElement e)
        {
            if (pinKey != null && e != null && e.keyBinding != null && e.keyBinding.keyCode != KeyCode.None)
                pinKey.Value = new KeyboardShortcut(e.keyBinding.keyCode);
            Lock(false);
        }

        private static void ResetPostfix()
        {
            if (pinKey == null || element == null) return;
            pinKey.Value = (KeyboardShortcut)pinKey.DefaultValue;
            element.keyBinding.keyCode = pinKey.Value.MainKey;
            element.UpdateKeyLabel();
        }

        private static void Lock(bool state)
        {
            if (window != null) LockM.Invoke(window, new object[] { state });
            // A trava nativa só alcança os elementos do jogo: o nosso segue o mesmo estado.
            if (element != null) element.ChangeIsAvailable(!state);
        }
    }
}
