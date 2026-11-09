using System.Collections;
using System.Reflection;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

// GK2Saves: ESC → Load → lixeira do save com o rótulo ROTULO → Yes → devolve a lista que sobrou.
public static class Snippet
{
    const string ROTULO = "Backup 2";

    public static IEnumerator Run()
    {
        var win = LazyUI.GetWindow<UISaveSlotsWindow>();
        if (win.IsShown) { win.Close(); yield return new WaitForSecondsRealtime(0.4f); }
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        if (pause.IsShown) pause.Close();
        yield return new WaitForSecondsRealtime(0.3f);
        pause.Open(null);
        yield return new WaitForSecondsRealtime(0.5f);
        GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Load").GetComponent<LazyButton>().onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.8f);
        UISaveSlot alvo = null;
        foreach (var slot in win.GetComponentsInChildren<UISaveSlot>())
        {
            var src = (TextMeshProUGUI)typeof(UISaveSlot).GetField("sourceLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(slot);
            if (src != null && src.gameObject.activeSelf && src.text == ROTULO) { alvo = slot; break; }
        }
        if (alvo == null) { yield return "nenhum save com rótulo " + ROTULO; yield break; }
        string nome = alvo.LinkedSaveSlot.slotName;
        typeof(UISaveSlot).GetMethod("OnDelete", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(alvo, null);
        yield return new WaitForSecondsRealtime(0.6f);
        var dialog = LazyUI.GetWindow<UIDialogWindow>();
        LazyButton yes = null;
        foreach (var b in dialog.GetComponentsInChildren<LazyButton>())
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null && t.text == LLBase.L("btn_yes")) { yes = b; break; }
        }
        if (yes == null) { yield return "botão Yes não encontrado"; yield break; }
        yes.onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.8f);
        var sb = new System.Text.StringBuilder("apagado " + nome + "; sobrou: ");
        foreach (var s in win.GetComponentsInChildren<UISaveSlot>()) if (s.LinkedSaveSlot != null) sb.Append(s.LinkedSaveSlot.slotName).Append(" | ");
        yield return sb.ToString();
    }
}
