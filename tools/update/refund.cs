// Integração do Refund no green: FASE=hover|direito escolhe um perk comprado do save e usa a UI nativa;
// nao|sim responde ao diálogo já aberto. sim devolve o perk de verdade; restaurar o save após a bateria.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;
using LazyBearTechnology;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class Snippet
{
    static string FASE = "hover";
    const string Marker = "GK2Update_RefundTarget:";

    public static IEnumerator Run()
    {
        Require(Application.productName == "GK2 Green Instance", "cenário exclusivo do green");
        Require(new[] { "hover", "direito", "nao", "sim" }.Contains(FASE), "FASE inválida: " + FASE);
        Require(MainGame.PlayerData != null, "carregue o save de teste");
        var win = LazyUI.GetWindow<UIZombieWorkerWindow>();
        var dialog = LazyUI.GetWindow<UIDialogWindow>();
        TalentLevelUpWidget node = null;
        Transform marker = null;
        bool answer = FASE == "nao" || FASE == "sim";

        if (answer)
        {
            Require(win.IsShown && dialog.IsShown, "resposta exige a janela do zumbi e o diálogo já abertos");
            marker = win.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name.StartsWith(Marker, StringComparison.Ordinal));
            Require(marker != null, "sem seleção da fase direito");
            node = marker.parent.GetComponent<TalentLevelUpWidget>();
            Require(node != null && node.Data?.ZombieWgoData != null && node.Data.Def != null, "nó selecionado deixou de existir");
            Require(marker.name == Marker + Key(node), "o nó selecionado foi reutilizado por outra árvore");
        }
        else
        {
            Require(!dialog.IsShown, "feche ou responda o diálogo anterior antes de selecionar outro perk");
            foreach (var old in win.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Marker, StringComparison.Ordinal)))
                Object.Destroy(old.gameObject);
            var character = LazyUI.GetWindow<CharacterWindow>();
            if (character.IsShown) character.Close();
            if (win.IsShown) win.Close();
            yield return null;

            object plugin = Chainloader.PluginInfos["mullervgs.gk2.hudplus"].Instance;
            Type refund = plugin.GetType().Assembly.GetType("GK2ZombieRefund.Refund", true);
            MethodInfo canRefund = AccessTools.Method(refund, "CanRefund");
            Require(canRefund != null, "Refund.CanRefund indisponível para selecionar o alvo");
            bool Eligible(ZombieWgoData z, TalentLevelUpDef def) => def != null && z.IsTalentLevelUpStudied(def.id)
                && (bool)canRefund.Invoke(null, new object[] { z, def });
            var system = MainGame.ZombieSystemData;
            var zombies = system.zombieOnSceneWgoIds.Select(system.GetZombie).Concat(system.zombieDrops)
                .Where(z => z != null).GroupBy(z => z.UniqueId.Guid).Select(g => g.First())
                .OrderBy(z => z.Name, StringComparer.Ordinal).ThenBy(z => z.UniqueId.Guid).ToList();
            foreach (var zombie in zombies)
            {
                var branches = zombie.talentData.Where(t => t != null && t.studiedLevelUps != null
                    && MainGame.Instance.GameSave.knowledgeSystem.IsTalentBranchUnlocked(t.id)
                    && t.studiedLevelUps.Any(id => Eligible(zombie, GameBalance.Me.GetDataOrNull<TalentLevelUpDef>(id))))
                    .OrderBy(t => t.id, StringComparer.Ordinal).ToList();
                foreach (var branch in branches)
                {
                    if (win.IsShown) { win.Close(); yield return null; }
                    AccessTools.Field(typeof(UIZombieWorkerWindow), "lastOpenedPerksTab").SetValue(null, true);
                    var data = new UIZombieWorkerWindowData(zombie);
                    data.ZombieProgressionWidgetData.SwitchTalent(branch.id);
                    win.Open(data);
                    yield return new WaitForSecondsRealtime(0.8f);
                    node = win.GetComponentsInChildren<TalentLevelUpWidget>()
                        .Where(w => w.isActiveAndEnabled && w.Data?.ZombieWgoData == zombie && Eligible(zombie, w.Data.Def))
                        .OrderBy(w => w.Data.Def.id, StringComparer.Ordinal).FirstOrDefault();
                    if (node == null) continue;
                    CenterInScroll(node);
                    yield return null;
                    yield return null;
                    Require(Visible(node), "perk elegível não ficou visível na árvore: " + node.Data.Def.id);
                    break;
                }
                if (node != null) break;
            }
            Require(node != null, "nenhum perk comprado e devolvível disponível nas árvores liberadas do save");
            var tag = new GameObject(Marker + Key(node));
            tag.transform.SetParent(node.transform, false);
            marker = tag.transform;
        }

        ZombieWgoData z = node.Data.ZombieWgoData;
        TalentLevelUpDef def = node.Data.Def;
        Require(z.IsTalentLevelUpStudied(def.id) && !def.availableAtStart, "o alvo precisa ser um perk comprado");
        var before = new State(z);
        string target = Key(node);
        if (answer)
        {
            string label = LLBase.L(FASE == "sim" ? "btn_yes" : "btn_no");
            var button = dialog.GetComponentsInChildren<UIDialogWindowButton>()
                .SingleOrDefault(b => b.GetComponentsInChildren<TextMeshProUGUI>(true).Any(t => t.text == label));
            Require(button != null && button.LazyButton.interactable, "botão nativo de resposta indisponível: " + label);
            button.LazyButton.onClick.Invoke();
        }
        else if (FASE == "hover")
        {
            // O hover do nó vem do LazyButton filho; o clique direito pertence ao componente no nó.
            var button = AccessTools.Field(typeof(TalentLevelUpWidget), "button").GetValue(node) as LazyButton;
            Require(button != null, "botão nativo do perk indisponível");
            ExecuteEvents.Execute(button.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        }
        else
        {
            ExecuteEvents.Execute(node.gameObject, new PointerEventData(EventSystem.current)
                { button = PointerEventData.InputButton.Right }, ExecuteEvents.pointerClickHandler);
        }
        yield return new WaitForSecondsRealtime(0.6f);

        var after = new State(z);
        if (FASE == "sim")
        {
            var expected = before.Bought.ToList();
            Require(expected.Remove(def.id), "perk ausente no estado anterior");
            Require(expected.SequenceEqual(after.Bought), "a confirmação não removeu exatamente o perk selecionado");
            Require(after.Red == before.Red + def.techRed && after.Green == before.Green + def.techGreen
                && after.Blue == before.Blue + def.techBlue, "restituição de pontos diferente do custo nativo do perk");
            Require(after.Used == before.Used - 1 && after.Skulls == before.Skulls, "a confirmação não liberou exatamente uma caveira");
        }
        else Require(before.Same(after), "hover, abertura ou cancelamento alterou os recursos/perks do zumbi");
        Require(dialog.IsShown == (FASE == "direito"), "estado incorreto do diálogo após " + FASE);
        if (FASE == "hover")
        {
            var tip = AccessTools.Field(typeof(UITooltip), "instance").GetValue(null) as UITooltip;
            Require(tip != null && tip.gameObject.activeInHierarchy, "hover não abriu o tooltip nativo");
            string hint = LLBase.CurrentLang.StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? "Clique direito: devolver" : "Right-click: refund";
            Require(tip.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text.Contains(hint)), "tooltip sem dica de devolução");
        }
        if (answer && marker != null) Object.Destroy(marker.gameObject);
        yield return FASE + ": " + target + " · " + before + " → " + after + " · diálogo " + (dialog.IsShown ? "aberto" : "fechado") + " · validado";
    }

    static string Key(TalentLevelUpWidget node) => node.Data.ZombieWgoData.UniqueId.Guid + "/" + node.Data.Def.id;
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException("Refund: " + message); }

    static void CenterInScroll(TalentLevelUpWidget node)
    {
        var scroll = node.GetComponentInParent<ScrollRect>();
        if (scroll == null) return;
        Canvas.ForceUpdateCanvases();
        scroll.StopMovement();
        var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, node.transform);
        Vector2 delta = viewport.rect.center - (Vector2)bounds.center;
        if (!scroll.horizontal) delta.x = 0;
        if (!scroll.vertical) delta.y = 0;
        scroll.content.anchoredPosition += (Vector2)scroll.content.parent.InverseTransformVector(viewport.TransformVector(delta));
    }

    static bool Visible(TalentLevelUpWidget node)
    {
        if (!node.isActiveAndEnabled) return false;
        var corners = new Vector3[4];
        ((RectTransform)node.transform).GetWorldCorners(corners);
        Vector2 center = (corners[0] + corners[2]) / 2f;
        if (!new Rect(0, 0, Screen.width, Screen.height).Contains(center)) return false;
        foreach (var scroll in node.GetComponentsInParent<ScrollRect>())
        {
            var viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, center)) return false;
        }
        return true;
    }

    sealed class State
    {
        public readonly int Red, Green, Blue, Used, Skulls;
        public readonly string[] Bought;
        readonly string talents, disabled;

        public State(ZombieWgoData z)
        {
            Red = z.techRed; Green = z.techGreen; Blue = z.techBlue;
            Used = z.GetUsedPerksCount(); Skulls = z.RedSkulls;
            Bought = z.talentData.SelectMany(t => t.studiedLevelUps).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            talents = string.Join("|", z.talentData.OrderBy(t => t.id, StringComparer.Ordinal).Select(t => t.id + "=" + t.curTalentValue));
            disabled = string.Join("|", z.disabledTalentLevelUps.OrderBy(id => id, StringComparer.Ordinal));
        }

        public bool Same(State other) => Red == other.Red && Green == other.Green && Blue == other.Blue && Used == other.Used
            && Skulls == other.Skulls && Bought.SequenceEqual(other.Bought) && talents == other.talents && disabled == other.disabled;
        public override string ToString() => "perks " + Used + "/" + Skulls + " R" + Red + " G" + Green + " B" + Blue;
    }
}
