// Só visual (green): abre a janela de escolher item dos baús como se fosse a tela TELA (o callback é um método real dela,
// num objeto vazio, para o HUD+ reconhecer a mesa), com o filtro FILTRO. Não clique em nada: o callback não tem estado.
// TELA: UIEmbalmWindowData, UIAutopsyWindowData, UIGardenBedWindow, UIPrayWindowData, ZombieEquipmentInventoryWidgetData...
// FILTRO: orgao, embalm, semente, sermao, ferramenta, tudo.
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using LazyBearTechnology;
using UnityEngine;

public static class Snippet
{
    const string TELA = "UIEmbalmWindowData";
    const string FILTRO = "orgao";

    public static IEnumerator Run()
    {
        Type t = typeof(UIAlchemyWindow).Assembly.GetType(TELA);
        if (t == null) { yield return "tipo " + TELA + " não existe"; yield break; }
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        MethodInfo m = new[] { t }.Concat(t.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
            .SelectMany(x => x.GetMethods(all)).FirstOrDefault(x => !x.IsStatic && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(UIItemCell) && x.ReturnType == typeof(void));
        if (m == null) { yield return "sem método (UIItemCell) em " + TELA; yield break; }
        object target = typeof(MonoBehaviour).IsAssignableFrom(m.DeclaringType)
            ? typeof(LazyUI).GetMethod("GetWindow", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)?.MakeGenericMethod(m.DeclaringType).Invoke(null, null)
            : FormatterServices.GetUninitializedObject(m.DeclaringType);
        if (target == null) { yield return "sem instância de " + m.DeclaringType; yield break; }
        var clicked = (Action<UIItemCell>)Delegate.CreateDelegate(typeof(Action<UIItemCell>), target, m);
        Func<Item, bool> cond;
        switch (FILTRO)
        {
            case "orgao": cond = i => i != null && !i.IsEmpty && i.Definition != null && i.Definition.isMainOrgan; break;
            case "embalm": cond = i => i != null && !i.IsEmpty && i.Definition != null && i.Definition.type == ItemType.Embalm; break;
            case "semente": cond = i => i != null && !i.IsEmpty && i.Definition != null && (i.Definition.isSeed || i.Definition.isFertilizer); break;
            case "sermao": cond = i => i != null && !i.IsEmpty && i.Definition != null && i.Definition.type == ItemType.Preach; break;
            case "ferramenta": cond = i => i != null && !i.IsEmpty && i.Definition != null && (i.Definition.isTool || i.Definition.type == ItemType.BodyArmor); break;
            default: cond = i => i != null && !i.IsEmpty; break;
        }
        var win = LazyUI.GetWindow<UIMultiInventoryWindow>();
        if (win.IsShown) { win.Close(); yield return null; }
        win.Open(new UIMultiInventoryWindowData(MainGame.PlayerData, clicked, cond));
        for (int i = 0; i < 20; i++) yield return null;
        yield return "aberta como " + m.DeclaringType.Name + "." + m.Name;
    }
}
