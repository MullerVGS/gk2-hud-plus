using System;
using System.Collections.Generic;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Crafting
{
    /// <summary>
    /// Célula com o ícone do Folio no canto do painel de ingredientes da mesa de alquimia (<c>UIAlchemyWindow</c>). O
    /// clique abre a janela nativa do Folio como o livro do mundo faz (<c>Flow_OpenFolioWindow</c>), por cima da mesa; a
    /// pilha de janelas do jogo devolve a mesa ao fechar. Só aparece se o Folio (<c>alchemy_recipe_book</c>, uma
    /// construção que precisa ser liberada) já existe no mundo: o atalho poupa a caminhada, não a progressão.
    /// </summary>
    internal sealed class FolioButton : IDisposable
    {
        internal const string BookId = "alchemy_recipe_book";
        private const string CellSprite = "comm-item_cell-dark";
        private const string IconSprite = "i_recipe_bag_alchemy";

        /// <summary>Em unidades do canvas (1 = 1 pixel de arte = 2 px de tela), a partir do canto de cima à direita do
        /// conteúdo da janela (o pai do botão Craft).</summary>
        private static readonly Vector2 Corner = new Vector2(6f, 0f);
        private const float Cell = 44f, Icon = 48f;

        private static FolioButton current;
        private GameObject root;

        public FolioButton(Harmony h)
        {
            current = this;
            h.Patch(AccessTools.Method(typeof(UIAlchemyWindow), nameof(UIAlchemyWindow.Open), new[] { typeof(UIAlchemyWindowData) }),
                postfix: new HarmonyMethod(typeof(FolioButton), nameof(OpenPostfix)));
            UIAlchemyWindow win = LazyUI.IsInitialized ? LazyUI.GetWindow<UIAlchemyWindow>() : null;
            if (win != null && win.IsShown) Refresh(win);
        }

        public void Dispose()
        {
            if (current == this) current = null;
            if (root != null) Object.Destroy(root);
            root = null;
        }

        private static void OpenPostfix(UIAlchemyWindow __instance)
        {
            try { current?.Refresh(__instance); }
            catch (Exception e) { CraftingModule.Fail("FolioButton", e, "UIAlchemyWindow.Open"); }
        }

        /// <summary>O Folio construído (o primeiro, se houver mais de um); nulo se ainda não existe.</summary>
        internal static WgoData Book()
        {
            List<WgoData> list = MainGame.Instance?.GameSave?.WorldData?.GetWgoDataList(BookId);
            return list != null && list.Count > 0 ? list[0] : null;
        }

        private void Refresh(UIAlchemyWindow win)
        {
            if (root == null) root = Build(win);
            if (root == null) return;
            root.SetActive(Book() != null && !LazyInput.IsGamepadActive);
        }

        private static GameObject Build(UIAlchemyWindow win)
        {
            var createBtn = AccessTools.Field(typeof(UIAlchemyWindow), "createBtn").GetValue(win) as LazyButton;
            var result = AccessTools.Field(typeof(UIAlchemyWindow), "result").GetValue(win) as UIItemCell;
            if (createBtn == null || result == null) return null;
            var refIcon = AccessTools.Field(typeof(UIItemCell), "icon").GetValue(result) as Image;
            var colors = AccessTools.Field(typeof(UIItemCell), "colors").GetValue(result) as ImageColors;
            var refButton = result.GetComponent<LazyButton>();

            Sprite cell = FindSprite(win.transform, CellSprite);
            Sprite icon = LazySingletonSO<EasySpritesCollection>.Instance.GetSprite(IconSprite);
            if (cell == null || icon == null) return null;

            var go = new GameObject("GK2Crafting.FolioButton", typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(createBtn.transform.parent, false);
            rt.SetAsLastSibling();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(Cell, Cell);
            rt.anchoredPosition = Corner;
            var bg = go.AddComponent<Image>();
            bg.sprite = cell;
            bg.raycastTarget = true;

            var iconGo = new GameObject("Icon", typeof(RectTransform));
            var irt = (RectTransform)iconGo.transform;
            irt.SetParent(rt, false);
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(Icon, Icon);
            irt.anchoredPosition = Vector2.zero;
            var img = iconGo.AddComponent<Image>();
            img.sprite = icon;
            img.raycastTarget = false;
            if (refIcon != null && refIcon.material != null) img.material = refIcon.material;

            var handler = go.AddComponent<Handler>();
            handler.Icon = img;
            handler.Colors = colors;
            handler.Sounds = refButton;
            if (colors != null) img.BlueColorReplace(colors.NormalColor);
            UIMouseTooltip.Attach(go, BookId + "_d", headerLngId: BookId);
            return go;
        }

        private static Sprite FindSprite(Transform under, string name)
        {
            foreach (Image i in under.GetComponentsInChildren<Image>(true))
                if (i.sprite != null && i.sprite.name == name) return i.sprite;
            return null;
        }

        /// <summary>Hover como o de uma célula de item (contorno do ícone na cor de destaque) e o som dos botões da janela.</summary>
        private sealed class Handler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Image Icon;
            public ImageColors Colors;
            public LazyButton Sounds;

            public void OnPointerEnter(PointerEventData e)
            {
                try
                {
                    if (Colors != null) Icon.BlueColorReplace(Colors.HighlightedColorMouse);
                    if (Sounds != null && !string.IsNullOrEmpty(Sounds.onEnterSound)) LazyAudio.PlayAndForget(Sounds.onEnterSound);
                }
                catch (Exception ex) { CraftingModule.Fail("FolioButton", ex, "hover"); }
            }

            public void OnPointerExit(PointerEventData e)
            {
                try { if (Colors != null) Icon.BlueColorReplace(Colors.NormalColor); }
                catch (Exception ex) { CraftingModule.Fail("FolioButton", ex, "hover"); }
            }

            public void OnPointerClick(PointerEventData e)
            {
                if (e.button != PointerEventData.InputButton.Left) return;
                try
                {
                    WgoData book = Book();
                    if (book == null) return;
                    if (Sounds != null && !string.IsNullOrEmpty(Sounds.onClickSound)) LazyAudio.PlayAndForget(Sounds.onClickSound);
                    UIMouseTooltip.HideIfShowingAt((RectTransform)transform, immediately: true);
                    OnPointerExit(e);
                    var data = new UIAlchemyFolioWindowData();
                    data.FillFromGaveSave(book);
                    LazyUI.GetWindow<UIAlchemyFolioWindow>().Open(data);
                }
                catch (Exception ex) { CraftingModule.Fail("FolioButton", ex, "click"); }
            }
        }
    }
}
