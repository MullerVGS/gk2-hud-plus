using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GK2Hud.Calendar
{
    /// <summary>
    /// Aba "Calendário" na janela do TAB (<c>CharacterWindow</c>), depois de "Mapa". O botão é uma cópia do prefab de
    /// aba do próprio jogo (<c>CharPageTabButton</c>) e a página é nossa (<see cref="CalendarPage"/>). A janela só conhece
    /// as páginas do enum <c>CharPage</c> e grava a última aberta no save (<c>PlayerData.lastOpenedPage</c>): por isso
    /// <c>LastOpenedPage</c> nunca recebe valor nosso. Ao abrir o calendário escondemos a página nativa atual; ao clicar
    /// numa aba nativa (ou fechar a janela) o patch em <c>SwitchPage</c> fecha o calendário e força o redesenho, mesmo
    /// que a aba clicada seja a <c>LastOpenedPage</c>.
    /// </summary>
    internal sealed class CalendarTab : IDisposable
    {
        internal static CalendarTab Current;

        private const string ButtonName = "GK2Hud.CalendarPage_Button";
        private const float AttachRetry = 1f;
        private const float RefreshInterval = 0.25f;

        private CharacterWindow window;
        private CharPageTabButton button;
        private CharPageTabButton lastNativeButton;
        private CalendarPage page;
        private bool open;
        private string lang;
        private float nextAttach, nextRefresh;
        private bool loggedError;

        public CalendarTab(Harmony harmony)
        {
            Current = this;
            harmony.Patch(AccessTools.Method(typeof(CharacterWindow), "SwitchPage"), prefix: new HarmonyMethod(typeof(CalendarTab), nameof(SwitchPagePrefix)));
            harmony.Patch(AccessTools.Method(typeof(CharacterWindow), nameof(CharacterWindow.Hide)), prefix: new HarmonyMethod(typeof(CalendarTab), nameof(HidePrefix)));
            harmony.Patch(AccessTools.Method(typeof(CharacterWindow), nameof(CharacterWindow.Redraw)), postfix: new HarmonyMethod(typeof(CalendarTab), nameof(RedrawPostfix)));
        }

        private static readonly AccessTools.FieldRef<Dictionary<Type, LazyWidgetBase>> WindowsCache =
            AccessTools.StaticFieldRefAccess<Dictionary<Type, LazyWidgetBase>>(AccessTools.Field(typeof(LazyUI), "windowsCache"));

        /// <summary>
        /// A janela sem procurar na cena: <c>LazyUI.GetWindow</c> faz <c>FindObjectsOfType</c> e loga erro quando ela
        /// não existe (menu principal). Fonte: o cache do LazyUI (preenchido quando o jogo abre a janela) ou o postfix do Redraw.
        /// </summary>
        private CharacterWindow FindWindow()
        {
            if (seenWindow != null) return seenWindow;
            try
            {
                Dictionary<Type, LazyWidgetBase> cache = WindowsCache();
                if (cache != null && cache.TryGetValue(typeof(CharacterWindow), out LazyWidgetBase w) && w != null) return (CharacterWindow)w;
            }
            catch (Exception) { }
            return null;
        }

        private CharacterWindow seenWindow;

        private static void RedrawPostfix(CharacterWindow __instance)
        {
            CalendarTab t = Current;
            if (t == null || __instance == null) return;
            t.seenWindow = __instance;
            if (t.window != __instance)
            {
                t.nextAttach = 0f;
                try { t.EnsureAttached(); }   // abre já com a aba (sem esperar o próximo Update)
                catch (Exception ex) { Plugin.Log.LogWarning("calendário: montar no Redraw: " + ex.Message); }
            }
        }

        public bool IsOpen => open && page != null;

        public void Dispose()
        {
            try
            {
                if (open && window != null && window.IsShown)
                {
                    // Descarregado com o calendário aberto: devolve a janela à página nativa em que estava.
                    Close();
                    AccessTools.Method(typeof(CharacterWindow), "SwitchPage").Invoke(window, new object[] { window.LastOpenedPage, false });
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("calendário: restaurar a página nativa: " + ex.Message); }
            Detach();
            if (Current == this) Current = null;
        }

        public void Update()
        {
            try
            {
                float now = Time.unscaledTime;
                if (!EnsureAttached()) return;
                if (LLBase.CurrentLang != lang)
                {
                    lang = LLBase.CurrentLang;
                    SetLabel();
                    page.Restyle();
                }
                if (open && now >= nextRefresh && window.IsShown)
                {
                    nextRefresh = now + RefreshInterval;
                    page.Refresh();
                }
            }
            catch (Exception ex)
            {
                if (!loggedError) Plugin.Log.LogError("calendário: " + ex);
                loggedError = true;
            }
        }

        // ---------------- patches ----------------

        private static void SwitchPagePrefix(CharacterWindow __instance, ref bool checkCurrent)
        {
            CalendarTab t = Current;
            if (t == null || !t.open || t.window != __instance) return;
            t.Close();
            checkCurrent = false;   // a aba clicada pode ser a LastOpenedPage, que estava escondida
        }

        private static void HidePrefix(CharacterWindow __instance)
        {
            CalendarTab t = Current;
            if (t != null && t.open && t.window == __instance) t.Close();
        }

        // ---------------- montagem ----------------

        private bool EnsureAttached()
        {
            if (seenWindow == null && !ReferenceEquals(seenWindow, null)) seenWindow = null;   // destruída (troca de cena)
            if (window != null && window == FindWindow() && button != null && page != null) return true;
            float now = Time.unscaledTime;
            if (now < nextAttach) return false;
            nextAttach = now + AttachRetry;
            CharacterWindow w = FindWindow();
            if (window != null || button != null || page != null) Detach();
            if (w == null || !CalendarModel.Ready) return false;
            Traverse t = Traverse.Create(w);
            CharPageTabButton prefab = t.Field("charPageTabButtonPrefab").GetValue<CharPageTabButton>();
            GameObject parent = t.Field("charPageTabButtonParent").GetValue<GameObject>();
            var buttons = t.Field("pagesButtons").GetValue<Dictionary<CharacterWindowData.CharPage, CharPageTabButton>>();
            Transform pages = w.transform.Find("Content/Pages");
            if (prefab == null || parent == null || buttons == null || buttons.Count == 0 || pages == null)
            {
                if (!loggedError) Plugin.Log.LogWarning("calendário: estrutura do CharacterWindow não encontrada.");
                loggedError = true;
                return false;
            }
            for (int i = parent.transform.childCount - 1; i >= 0; i--)
                if (parent.transform.GetChild(i).name == ButtonName) Object.DestroyImmediate(parent.transform.GetChild(i).gameObject);
            CalendarPage.DestroyOrphans(pages);

            window = w;
            lang = LLBase.CurrentLang;
            page = CalendarPage.Create(w, pages);
            if (page == null) { Detach(); return false; }

            GameObject go = Object.Instantiate(prefab.gameObject, parent.transform, false);
            go.name = ButtonName;
            go.SetActive(true);
            button = go.GetComponent<CharPageTabButton>();
            button.Init(CalendarL10n.T("tab"), OnClick, isLastTab: true);
            button.UpdateState(false);
            // A última aba nativa (Mapa) não tem o separador à direita: agora tem.
            lastNativeButton = LastNative(buttons);
            SetSeparator(lastNativeButton, true);
            w.RefreshCharPageTabButtonParentContentFitter();
            loggedError = false;
            return true;
        }

        private static CharPageTabButton LastNative(Dictionary<CharacterWindowData.CharPage, CharPageTabButton> buttons)
        {
            CharPageTabButton last = null;
            int idx = -1;
            foreach (CharPageTabButton b in buttons.Values)
                if (b != null && b.transform.GetSiblingIndex() > idx) { idx = b.transform.GetSiblingIndex(); last = b; }
            return last;
        }

        private static void SetSeparator(CharPageTabButton b, bool on)
        {
            GameObject sep = b != null ? Traverse.Create(b).Field("rightSeparator").GetValue<GameObject>() : null;
            if (sep != null) sep.SetActive(on);
        }

        private void SetLabel()
        {
            if (button == null) return;
            Traverse.Create(button).Field("tabName").SetValue(CalendarL10n.T("tab"));
            button.SetLabel();
            window?.RefreshCharPageTabButtonParentContentFitter();
        }

        private void Detach()
        {
            open = false;
            if (button != null) Object.Destroy(button.gameObject);
            SetSeparator(lastNativeButton, false);
            if (page != null) page.Destroy();
            if (window != null) { try { window.RefreshCharPageTabButtonParentContentFitter(); } catch (Exception) { } }
            button = null;
            lastNativeButton = null;
            page = null;
            window = null;
        }

        // ---------------- abrir/fechar ----------------

        private void OnClick()
        {
            if (open || window == null || page == null) return;
            Traverse t = Traverse.Create(window);
            var pages = t.Field("pages").GetValue<Dictionary<CharacterWindowData.CharPage, LazyWidgetBase>>();
            var buttons = t.Field("pagesButtons").GetValue<Dictionary<CharacterWindowData.CharPage, CharPageTabButton>>();
            if (pages != null && pages.TryGetValue(window.LastOpenedPage, out LazyWidgetBase current) && current != null) current.Hide();
            if (buttons != null) foreach (CharPageTabButton b in buttons.Values) b?.UpdateState(false);
            CloseContextMenu();
            button.UpdateState(true);
            open = true;
            page.Show();
            nextRefresh = Time.unscaledTime + RefreshInterval;
            window.RefreshCharPageTabButtonParentContentFitter();
        }

        private void Close()
        {
            if (!open) return;
            open = false;
            if (page != null) page.Hide();
            if (button != null) button.UpdateState(false);
        }

        private static void CloseContextMenu()
        {
            UIContextMenuWindow menu = LazyUI.GetWindow<UIContextMenuWindow>();
            if (menu != null && menu.IsShown) menu.Close();
        }

        /// <summary>Para os cenários: abre o calendário como um clique na aba.</summary>
        internal void TestOpen(int? selectDay = null)
        {
            OnClick();
            if (selectDay.HasValue) page?.Select(selectDay.Value);
        }

        internal void TestHover(int? day) => page?.Select(day ?? -1);

        internal void TestOffset(int offset) => page?.SelectOffset(offset);

        internal string Describe() => $"window={(window != null ? window.name : "-")} shown={(window != null && window.IsShown)} open={open} " +
                                      $"last={(window != null ? window.LastOpenedPage.ToString() : "-")} page={(page != null ? page.Describe() : "-")}";
    }
}
