using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Hud.Tooltips
{
    /// <summary>Dado de um widget próprio dentro do tooltip nativo; a assinatura decide quando redesenhar.</summary>
    internal abstract class TipData : LazyWidgetDataBase
    {
        public abstract string Sig { get; }
    }

    /// <summary>Uma célula da barra de progresso, como a janela de craft/canteiro desenha (ProgressCellCraft).</summary>
    internal struct TipCell
    {
        public const int Empty = 0, Good = 1, Failed = 2;
        public int State;
        public int Star;      // estrela de craft (1..3) ou -1
        public int Garden;    // qualidade de canteiro (0 = +1, 1..3 = estrela) ou -1
    }

    /// <summary>Linha "ícone do item + barra de células": o progresso com a arte da janela nativa.</summary>
    internal sealed class ProgressRowData : TipData
    {
        public string IconId;
        public List<TipCell> Cells = new List<TipCell>();
        public int More;      // células além das mostradas (a janela nativa também corta em 18)

        public override string Sig
        {
            get
            {
                var sb = new StringBuilder("row:").Append(IconId).Append(':').Append(More).Append(':');
                foreach (TipCell c in Cells) sb.Append(c.State).Append(c.Star > 0 ? "s" + c.Star : "").Append(c.Garden >= 0 ? "g" + c.Garden : "").Append(',');
                return sb.ToString();
            }
        }
    }

    /// <summary>Dica discreta no canto da bolha: a tecla que fixa e abre o detalhe.</summary>
    internal sealed class PinHintData : TipData
    {
        public string Text;
        public override string Sig => "hint:" + Text;
    }

    internal sealed class ProgressRowWidget : LazyWidget<ProgressRowData>
    {
        private const float Icon = 48f, IconBox = 24f, Gap = 3f, CellW = 7f, CellStep = 6f, CellH = 10f, StarRoom = 7f, RowH = 24f;

        public override void Redraw()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            if (data == null) return;
            EasySpritesCollection sprites = LazySingletonSO<EasySpritesCollection>.Instance;
            bool icon = !string.IsNullOrEmpty(data.IconId) && sprites.HasSprite(data.IconId);
            int n = data.Cells.Count;
            float cellsW = n > 0 ? (n - 1) * CellStep + CellW : 0f;
            float w = (icon ? IconBox : 0f) + (icon && n > 0 ? Gap : 0f) + cellsW;

            LayoutElement le = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            le.minWidth = le.preferredWidth = w;
            le.minHeight = le.preferredHeight = RowH;

            RectTransform content = Child("Content", transform);
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
            content.sizeDelta = new Vector2(w, RowH);
            content.anchoredPosition = Vector2.zero;

            float x = 0f;
            if (icon)
            {
                RectTransform irt = Child("Icon", content);
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
                irt.pivot = new Vector2(0.5f, 0.5f);
                irt.sizeDelta = new Vector2(Icon, Icon);
                irt.anchoredPosition = new Vector2(IconBox / 2f, 0f);
                Image img = irt.gameObject.AddComponent<Image>();
                img.sprite = sprites.GetSprite(data.IconId);
                img.raycastTarget = false;
                Material m = TipWidgets.ItemIconMaterial(content);
                if (m != null) img.material = m;
                x = IconBox + Gap;
            }
            if (n == 0) return;
            UIPrefabsPooler pool = UIPrefabsPooler.Instance;
            ProgressCellCraft proto = pool != null ? pool.GetElementFromPool<ProgressCellCraft>(content) : null;
            if (proto == null) return;
            // Cópias próprias do prefab (o do pool volta na hora): destruir a bolha não mexe no pool do jogo.
            float top = -(RowH - CellH - StarRoom) / 2f - StarRoom;
            for (int i = 0; i < n; i++)
            {
                TipCell c = data.Cells[i];
                ProgressCellCraft cell = Instantiate(proto, content);
                cell.gameObject.SetActive(true);
                var crt = (RectTransform)cell.transform;
                crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(0f, 1f);
                crt.anchoredPosition = new Vector2(x + i * CellStep, top);
                cell.Show(i, n, c.State == TipCell.Empty, c.State == TipCell.Failed, c.Star, -1, c.Garden);
                foreach (Graphic g in cell.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            }
            proto.Hide();
            pool.ReleaseElementToPool(proto);
        }

        private static RectTransform Child(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        protected override void TestDraw() { }
    }

    internal sealed class PinHintWidget : LazyWidget<PinHintData>
    {
        private const float RowH = 3f, Inset = 9f;
        private readonly Vector3[] corners = new Vector3[4];

        /// <summary>
        /// A linha da dica tem largura zero e fica centralizada pelo layout: o texto é levado à borda direita da bolha
        /// depois do layout de cada frame (a bolha muda de largura com o conteúdo).
        /// </summary>
        private void LateUpdate()
        {
            if (transform.childCount == 0 || !(transform.parent is RectTransform bubble)) return;
            var label = (RectTransform)transform.GetChild(0);
            bubble.GetWorldCorners(corners);
            float x = corners[2].x - Inset * bubble.lossyScale.x;
            if (Mathf.Abs(label.position.x - x) > 0.01f) label.position = new Vector3(x, label.position.y, label.position.z);
        }

        public override void Redraw()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            if (data == null) return;
            Styles s = Styles.Read();
            if (s == null) return;
            // Uma linha baixa no fim do conteúdo: o texto desce para a margem da moldura e avança para a direita, então
            // fica no canto sem nunca encostar na última linha do tooltip.
            LayoutElement le = GetComponent<LayoutElement>() ?? gameObject.AddComponent<LayoutElement>();
            le.minHeight = le.preferredHeight = RowH;
            le.minWidth = 0f;
            le.preferredWidth = 0f;
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var lrt = (RectTransform)go.transform;
            lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 1f);
            lrt.sizeDelta = new Vector2(120f, 10f);
            lrt.anchoredPosition = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.fontSize = 16;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.TopRight;
            tmp.raycastTarget = false;
            // O estilo secundário do tooltip (tiny_font, cor clara) já é a menor fonte do jogo. Menor que isso sem borrar
            // só na metade do tamanho (1 pixel da fonte = 1 px de tela), e aí em negrito para continuar legível.
            s.Small.ApplyStyle(tmp);
            tmp.fontSize = 8;
            tmp.fontStyle = FontStyles.Bold;
            tmp.text = data.Text;
        }

        protected override void TestDraw() { }
    }

    /// <summary>
    /// Widgets próprios no contêiner do tooltip nativo (e nos clones fixados): o LazyWidgetContainer casa dado e widget
    /// pelo tipo exato e, sem widget livre, pede um prefab ao jogo. Antes de montar, garante um widget nosso por dado.
    /// </summary>
    internal static class TipWidgets
    {
        private static readonly FieldInfo UsedField = Native.UsedWidgetsField;
        private static readonly FieldInfo DisplayedField = AccessTools.Field(typeof(LazyWidgetContainer), "displayedWidgets");
        private static readonly FieldInfo ContainerGoField = AccessTools.Field(typeof(LazyWidgetContainer), "widgetContainerGO");
        private static readonly List<KeyValuePair<LazyWidgetContainer, LazyWidgetBase>> made = new List<KeyValuePair<LazyWidgetContainer, LazyWidgetBase>>();
        private static int users;

        /// <summary>
        /// Tooltips e World usam os widgets e ligam e desligam separados. Cada uma entra com o seu método de prefixo:
        /// soltar os patches de uma tira o método dela, e o mesmo método serviria às duas.
        /// </summary>
        public static void Acquire(Harmony harmony, bool world)
        {
            users++;
            harmony.Patch(AccessTools.Method(typeof(LazyWidgetContainer), nameof(LazyWidgetContainer.ConstructWidgets)),
                prefix: new HarmonyMethod(typeof(TipWidgets), world ? nameof(EnsureForWorld) : nameof(EnsureForTooltips)));
        }

        private static void EnsureForTooltips(LazyWidgetContainer __instance, List<LazyWidgetDataBase> dataList, Transform parent) => Ensure(__instance, dataList, parent);

        private static void EnsureForWorld(LazyWidgetContainer __instance, List<LazyWidgetDataBase> dataList, Transform parent) => Ensure(__instance, dataList, parent);

        public static void Release()
        {
            if (--users > 0) return;
            users = 0;
            foreach (KeyValuePair<LazyWidgetContainer, LazyWidgetBase> kv in made)
            {
                if (kv.Value == null) continue;
                if (kv.Key != null)
                {
                    (UsedField.GetValue(kv.Key) as List<LazyWidgetBase>)?.Remove(kv.Value);
                    (DisplayedField.GetValue(kv.Key) as List<LazyWidgetBase>)?.Remove(kv.Value);
                }
                Object.Destroy(kv.Value.gameObject);
            }
            made.Clear();
        }

        private static void Ensure(LazyWidgetContainer __instance, List<LazyWidgetDataBase> dataList, Transform parent)
        {
            if (dataList == null) return;
            int rows = 0, hints = 0;
            foreach (LazyWidgetDataBase d in dataList)
            {
                if (d is ProgressRowData) rows++;
                else if (d is PinHintData) hints++;
            }
            if (rows == 0 && hints == 0) return;
            var used = UsedField.GetValue(__instance) as List<LazyWidgetBase>;
            if (used == null) return;
            foreach (LazyWidgetBase w in used)
            {
                if (w is ProgressRowWidget) rows--;
                else if (w is PinHintWidget) hints--;
            }
            Transform into = parent ? parent : (ContainerGoField.GetValue(__instance) as GameObject)?.transform ?? __instance.transform;
            for (; rows > 0; rows--) Add<ProgressRowWidget>(__instance, used, into, "ProgressRow");
            for (; hints > 0; hints--) Add<PinHintWidget>(__instance, used, into, "PinHint");
        }

        private static void Add<T>(LazyWidgetContainer c, List<LazyWidgetBase> used, Transform into, string name) where T : LazyWidgetBase
        {
            var go = new GameObject(PinStack.Prefix + name, typeof(RectTransform));
            go.transform.SetParent(into, false);
            go.SetActive(false);
            T w = go.AddComponent<T>();
            used.Add(w);
            made.Add(new KeyValuePair<LazyWidgetContainer, LazyWidgetBase>(c, w));
            if (made.Count > 256) made.RemoveAll(kv => kv.Value == null);
        }

        private static Material iconMaterial;

        /// <summary>
        /// Material dos ícones de item nas células do tooltip (shader que troca o contorno azul da arte pela cor da
        /// célula); sem ele o ícone sai com borda azul. Lido uma vez de uma célula do pool, devolvida em seguida sem desenhar.
        /// </summary>
        public static Material ItemIconMaterial(Transform scratch)
        {
            if (iconMaterial != null) return iconMaterial;
            UIPrefabsPooler pool = UIPrefabsPooler.Instance;
            UITooltipCraftItemCell cell = pool != null ? pool.GetElementFromPool<UITooltipCraftItemCell>(scratch) : null;
            if (cell == null) return null;
            UIItemCell ic = cell.GetComponentInChildren<UIItemCell>(true);
            var icon = ic != null ? AccessTools.Field(typeof(UIItemCell), "icon").GetValue(ic) as Image : null;
            if (icon != null) iconMaterial = icon.material;
            pool.ReleaseElementToPool(cell);
            return iconMaterial;
        }

        /// <summary>Texto da tecla como o jogo escreve as dele no PC ("[E]").</summary>
        public static string KeyText(BepInEx.Configuration.KeyboardShortcut key)
        {
            KeyCode k = key.MainKey;
            string name;
            switch (k)
            {
                case KeyCode.Mouse0: name = "LMB"; break;
                case KeyCode.Mouse1: name = "RMB"; break;
                case KeyCode.Mouse2: name = "MMB"; break;
                default:
                    ControllerIconLibrary lib = LazySingletonSO<ControllerIconLibrary>.Instance;
                    name = lib != null ? lib.GetKeycodeString(k) : k.ToString();
                    break;
            }
            return "[" + name + "]";
        }
    }
}
