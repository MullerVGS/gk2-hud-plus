using System.Collections;
using System.Reflection;
using LazyBearTechnology;
using TMPro;
using UnityEngine;

// GK2Saves: ESC → Load → clica no save com o rótulo ROTULO → print do diálogo → Yes → espera voltar ao jogo.
public static class Snippet
{
    const string ROTULO = "Manual";

    public static IEnumerator Run()
    {
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        if (!pause.IsShown) { pause.Open(null); yield return new WaitForSecondsRealtime(0.5f); }
        GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Load").GetComponent<LazyButton>().onClick.Invoke();
        yield return new WaitForSecondsRealtime(0.8f);
        var win = LazyUI.GetWindow<UISaveSlotsWindow>();
        UISaveSlot alvo = null;
        foreach (var slot in win.GetComponentsInChildren<UISaveSlot>())
        {
            var src = (TextMeshProUGUI)typeof(UISaveSlot).GetField("sourceLabel", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(slot);
            if (src != null && src.gameObject.activeSelf && src.text == ROTULO) { alvo = slot; break; }
        }
        if (alvo == null) { yield return "nenhum save com rótulo " + ROTULO; yield break; }
        string nome = alvo.LinkedSaveSlot.slotName;
        typeof(UISaveSlot).GetMethod("OnClicked", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(alvo, null);
        yield return new WaitForSecondsRealtime(0.8f);
        Debug.Log("[cenario] diálogo aberto para " + nome);
        GK2Shot("gk2saves-dialogo");
        yield return new WaitForSecondsRealtime(0.5f);
        var dialog = LazyUI.GetWindow<UIDialogWindow>();
        LazyButton yes = null;
        foreach (var b in dialog.GetComponentsInChildren<LazyButton>())
        {
            var t = b.GetComponentInChildren<TextMeshProUGUI>();
            if (t != null && t.text == LLBase.L("btn_yes")) { yes = b; break; }
        }
        if (yes == null) { yield return "botão Yes não encontrado"; yield break; }
        yes.onClick.Invoke();
        float deadline = Time.realtimeSinceStartup + 120f;
        yield return new WaitForSecondsRealtime(3f);
        while (Time.realtimeSinceStartup < deadline)
        {
            if (MainGame.Instance.gameState == MainGame.GameState.InGame && !LazyUI.Get<UILoadingOverlay>().IsShown && MainGame.PlayerController != null) break;
            yield return new WaitForSecondsRealtime(0.5f);
        }
        yield return new WaitForSecondsRealtime(3f);
        var g = MainGame.Instance.GameSave;
        yield return "carregado " + nome + " → slot em jogo " + MainGame.Instance.SaveSlotData.slotName + " · dia " + g.environmentData.Day + " hora " + g.environmentData.TimeOfDay.ToString("0.000") + " zona " + MainGame.PlayerData.CurrentWorldZoneData?.id;
    }

    static void GK2Shot(string nome)
    {
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, nome + ".png"));
    }
}
