using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using LazyBearTechnology;
using UnityEngine;

// Só leitura: lista os bindings de teclado ativos (GameBindings) e diz se teclas candidatas (T, Alt, Shift, Mouse2...) estão livres.
public static class Snippet
{
    static readonly KeyCode[] Candidatas = { KeyCode.T, KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Space, KeyCode.Tab, KeyCode.Q, KeyCode.R, KeyCode.F, KeyCode.G, KeyCode.Z, KeyCode.X, KeyCode.C, KeyCode.V };

    public static object Run()
    {
        var names = typeof(GameKey).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(GameKey))
            .ToDictionary(f => ((GameKey)f.GetValue(null)).value, f => f.Name);
        string N(GameKey k) => k == null ? "null" : (names.TryGetValue(k.value, out var n) ? n : k.value.ToString());
        var sb = new StringBuilder();
        var binds = LazyInput.GameBindings.keyBindings;
        var used = new Dictionary<KeyCode, List<string>>();
        foreach (var b in binds)
        {
            string combo = string.Join("+", (b.additionalKeyCodes ?? new KeyCode[0]).Select(k => k.ToString()).Concat(new[] { b.keyCode.ToString() }));
            sb.AppendLine(N(b.gameKey) + " = " + combo);
            foreach (var k in (b.additionalKeyCodes ?? new KeyCode[0]).Concat(new[] { b.keyCode }))
            {
                if (!used.ContainsKey(k)) used[k] = new List<string>();
                used[k].Add(N(b.gameKey));
            }
        }
        sb.AppendLine("--- candidatas");
        foreach (var k in Candidatas) sb.AppendLine(k + ": " + (used.TryGetValue(k, out var l) ? "USADA por " + string.Join(", ", l) : "livre nos bindings"));
        sb.AppendLine("--- aliases: " + string.Join(", ", LazyInput.GameBindings.bindingAliases.Select(a => N(a.gameKey1) + "~" + N(a.gameKey2))));
        var gs = (GameSettings)HarmonyLib.AccessTools.Field(typeof(GameSettings), "instance").GetValue(null);
        sb.AppendLine("--- GameSettings: cursorMode=" + (gs == null ? "?" : gs.cursorMode.ToString()) + " bindings salvos=" + (gs == null ? 0 : gs.keyboardKeybindings.Count) + " (customizados no blue e sincronizados)");
        sb.AppendLine("--- cursor: visible=" + Cursor.visible + " lockState=" + Cursor.lockState + " | IsGamepadActive=" + LazyInput.IsGamepadActive + " | UIMouseTooltip.IsAvailable=" + UIMouseTooltip.IsAvailable + " | movementType=" + LazyInput.GetKeyboardMovementType());
        return sb.ToString();
    }
}
