using System.Collections;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.EventSystems;

// GK2Saves: simula diálogo (controle ByFlow tomado), abre o ESC, passa o mouse no Save desabilitado (tooltip do motivo), print, devolve o controle.
public static class Snippet
{
    public static IEnumerator Run()
    {
        var saves = LazyUI.GetWindow<UISaveSlotsWindow>();
        if (saves.IsShown) saves.Close();
        var pause = LazyUI.GetWindow<UIGamePauseWindow>();
        if (pause.IsShown) pause.Close();
        yield return new WaitForSecondsRealtime(0.3f);
        MainGame.PlayerController.SetControlTakenType(TakenControlType.ByFlow, isEnabled: false);
        pause.Open(null);
        yield return new WaitForSecondsRealtime(0.5f);
        var go = GameObject.Find("UI/UIRoot/UIGamePauseWindow/GenericWIndowLayout/Content/GK2Saves_Save");
        bool interactable = go.GetComponent<LazyButton>().interactable;
        ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
        yield return new WaitForSecondsRealtime(0.8f);
        ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "gk2saves-bloqueio.png"));
        yield return new WaitForSecondsRealtime(0.5f);
        ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
        pause.Close();
        MainGame.PlayerController.SetControlTakenType(TakenControlType.ByFlow, isEnabled: true);
        yield return new WaitForSecondsRealtime(0.3f);
        pause.Open(null);
        yield return new WaitForSecondsRealtime(0.3f);
        bool depois = go.GetComponent<LazyButton>().interactable;
        pause.Close();
        yield return "com ByFlow: interactable=" + interactable + "; depois de devolver: " + depois;
    }
}
