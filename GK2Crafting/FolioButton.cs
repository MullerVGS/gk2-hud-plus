using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GK2Crafting
{
    /// <summary>
    /// Célula com o livro do Folio no canto do painel de ingredientes da mesa de alquimia (<c>UIAlchemyWindow</c>). O
    /// clique abre a janela nativa do Folio como o livro do mundo faz (<c>Flow_OpenFolioWindow</c>), por cima da mesa; a
    /// pilha de janelas do jogo devolve a mesa ao fechar. Só aparece se o Folio (<c>alchemy_recipe_book</c>, uma
    /// construção que precisa ser liberada) já existe no mundo: o atalho poupa a caminhada, não a progressão.
    /// </summary>
    internal sealed class FolioButton : IDisposable
    {
        internal const string BookId = "alchemy_recipe_book";
        private const string CellSprite = "comm-item_cell-dark";
        private const string SelectionSprite = "selection";
        /// <summary>O livro do Folio do mundo, sem o atril (objeto 3D renderizado e recortado: docs/cenarios/folio-render.cs +
        /// tools/art/folio-livro.py), embutido na DLL.</summary>
        private const string BookArt = "gk2crafting.folio_book.png";

        /// <summary>Em unidades do canvas (1 = 1 pixel de arte = 2 px de tela), a partir do canto de cima à direita do
        /// conteúdo da janela (o pai do botão Craft).</summary>
        private static readonly Vector2 Corner = new Vector2(6f, 0f);
        private const float Cell = 44f;
        /// <summary>Canto de baixo à esquerda do livro (25x18) dentro da célula, em pixels de arte inteiros.</summary>
        private static readonly Vector2 BookAt = new Vector2(10f, 13f);

        private static FolioButton current;
        private GameObject root;
        private Texture2D bookTex;
        private Sprite bookSprite;

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
            if (bookSprite != null) Object.Destroy(bookSprite);
            if (bookTex != null) Object.Destroy(bookTex);
            bookSprite = null;
            bookTex = null;
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

        private Sprite LoadBook()
        {
            if (bookSprite != null) return bookSprite;
            using (var stream = typeof(FolioButton).Assembly.GetManifestResourceStream(BookArt))
            {
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                bookTex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                // ImageConversion por reflexão, como a arte do calendário (WeekStrip): a referência direta pede netstandard 2.1.
                var load = AccessTools.Method(Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule"), "LoadImage",
                    new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
                if (load == null || !(bool)load.Invoke(null, new object[] { bookTex, bytes, false })) return null;
            }
            bookSprite = Sprite.Create(bookTex, new Rect(0, 0, bookTex.width, bookTex.height), new Vector2(0f, 0f), 50f);
            bookSprite.name = "gk2crafting-folio_book";
            return bookSprite;
        }

        private GameObject Build(UIAlchemyWindow win)
        {
            var createBtn = AccessTools.Field(typeof(UIAlchemyWindow), "createBtn").GetValue(win) as LazyButton;
            var result = AccessTools.Field(typeof(UIAlchemyWindow), "result").GetValue(win) as UIItemCell;
            if (createBtn == null || result == null) return null;
            var refButton = result.GetComponent<LazyButton>();

            Sprite cell = FindSprite(win.transform, CellSprite);
            Sprite book = LoadBook();
            Sprite selection = Resources.FindObjectsOfTypeAll<Sprite>().FirstOrDefault(sp => sp.name == SelectionSprite);
            if (cell == null || book == null) return null;

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

            var irt = Child("Icon", rt, new Vector2(0f, 0f), BookAt, new Vector2(book.rect.width, book.rect.height));
            var img = irt.gameObject.AddComponent<Image>();
            img.sprite = book;
            img.raycastTarget = false;

            Image frame = null;
            if (selection != null)
            {
                // O quadro amarelo da célula de item com foco (42x42 dentro da célula de 44).
                var frt = Child("Selection", rt, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(selection.rect.width, selection.rect.height));
                frame = frt.gameObject.AddComponent<Image>();
                frame.sprite = selection;
                frame.raycastTarget = false;
                frt.gameObject.SetActive(false);
            }

            var handler = go.AddComponent<Handler>();
            handler.Frame = frame;
            handler.Sounds = refButton;
            UIMouseTooltip.Attach(go, BookId + "_d", headerLngId: BookId);
            return go;
        }

        private static RectTransform Child(string name, RectTransform parent, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        private static Sprite FindSprite(Transform under, string name)
        {
            foreach (Image i in under.GetComponentsInChildren<Image>(true))
                if (i.sprite != null && i.sprite.name == name) return i.sprite;
            return null;
        }

        /// <summary>Hover com o quadro de foco das células de item e o som dos botões da janela.</summary>
        private sealed class Handler : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
        {
            public Image Frame;
            public LazyButton Sounds;

            public void OnPointerEnter(PointerEventData e)
            {
                try
                {
                    if (Frame != null) Frame.gameObject.SetActive(true);
                    if (Sounds != null && !string.IsNullOrEmpty(Sounds.onEnterSound)) LazyAudio.PlayAndForget(Sounds.onEnterSound);
                }
                catch (Exception ex) { CraftingModule.Fail("FolioButton", ex, "hover"); }
            }

            public void OnPointerExit(PointerEventData e)
            {
                try { if (Frame != null) Frame.gameObject.SetActive(false); }
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
