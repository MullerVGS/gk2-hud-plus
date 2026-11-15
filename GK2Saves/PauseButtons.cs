using System;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace GK2Saves
{
    /// <summary>
    /// Botões Save e Load no topo do menu de pausa, clonados do botão Settings (mesmo jeito que o jogo cria o "Report a bug"),
    /// com um espaçador nativo separando do resto. Save fica desabilitado (estilo nativo) com o motivo no tooltip do jogo.
    /// </summary>
    internal sealed class PauseButtons : IDisposable
    {
        private readonly Action onSave;
        private readonly Action onLoad;
        private UIGamePauseWindow window;
        private GameObject saveGo;
        private GameObject loadGo;
        private GameObject spacingGo;
        private LazyButton saveButton;
        private string blockReason;

        public PauseButtons(Action onSave, Action onLoad)
        {
            this.onSave = onSave;
            this.onLoad = onLoad;
        }

        public void Refresh(UIGamePauseWindow win)
        {
            if (win == null) return;
            if (saveGo == null || window != win) Build(win);
            if (saveButton == null) return;
            blockReason = SaveGuard.BlockReason();
            saveButton.interactable = blockReason == null;
            ((RectTransform)win.transform).RefreshContentFitter();
        }

        private void Build(UIGamePauseWindow win)
        {
            DestroyObjects();
            window = win;
            var settings = Traverse.Create(win).Field("settingsBtn").GetValue<LazyButton>();
            var spacing = Traverse.Create(win).Field("goToMenuSpacing").GetValue<GameObject>();
            if (settings == null) { SavesModule.Log.LogWarning("settingsBtn não encontrado no menu de pausa"); return; }
            saveGo = Clone(settings, "GK2Saves_Save", "ui_save", () => onSave());
            loadGo = Clone(settings, "GK2Saves_Load", "ui_load_save_btn", () => onLoad());
            saveButton = saveGo.GetComponent<LazyButton>();
            saveGo.transform.SetSiblingIndex(0);
            loadGo.transform.SetSiblingIndex(1);
            if (spacing != null)
            {
                spacingGo = UnityEngine.Object.Instantiate(spacing, settings.transform.parent);
                spacingGo.name = "GK2Saves_Spacing";
                spacingGo.SetActive(true);
                spacingGo.transform.SetSiblingIndex(2);
            }
            AddHint(saveGo);
        }

        private static GameObject Clone(LazyButton template, string name, string langToken, UnityAction click)
        {
            GameObject go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = name;
            LazyButton button = go.GetComponent<LazyButton>();
            button.onClick.RemoveAllListeners();
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++) button.onClick.SetPersistentListenerState(i, UnityEventCallState.Off);
            button.onClick.AddListener(click);
            button.SetCallbacksIntoGamepadNavigationItem();
            foreach (LocalizedLabel label in go.GetComponentsInChildren<LocalizedLabel>(includeInactive: true))
            {
                label.langToken = langToken;
                label.Localize();
            }
            go.SetActive(true);
            return go;
        }

        private void AddHint(GameObject go)
        {
            EventTrigger trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ =>
            {
                if (blockReason != null) UITooltip.ShowSimpleInfo(go.transform, L10n.T(blockReason));
            });
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener(_ => HideHint());
            trigger.triggers.Add(enter);
            trigger.triggers.Add(exit);
        }

        public void HideHint()
        {
            if (saveGo != null && UITooltip.IsTooltipShowingAtTarget((RectTransform)saveGo.transform)) UITooltip.HideImmediately();
        }

        private void DestroyObjects()
        {
            HideHint();
            if (saveGo != null) UnityEngine.Object.Destroy(saveGo);
            if (loadGo != null) UnityEngine.Object.Destroy(loadGo);
            if (spacingGo != null) UnityEngine.Object.Destroy(spacingGo);
            saveGo = loadGo = spacingGo = null;
            saveButton = null;
        }

        public void Dispose()
        {
            DestroyObjects();
            if (window != null && window.IsShown) ((RectTransform)window.transform).RefreshContentFitter();
            window = null;
        }
    }
}
