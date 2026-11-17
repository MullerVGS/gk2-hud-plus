using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

// Só leitura: exporta para PASTA a arte de dia do jogo (centro do medalhão por dia, dia e noite; sigilos; ícones de dia
// do mapa, recolhido e expandido) e lista quais centros o save já liberou (unlockedCustomHudDaySprites).
public static class Snippet
{
    const string PASTA = @"D:\GK2-green\artdump";

    static string Save(Sprite s, string prefix)
    {
        if (s == null) return prefix + ": (nulo)";
        Rect r = s.textureRect;
        Texture2D src = s.texture;
        var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        Graphics.Blit(src, rt);
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(r.x, r.y, r.width, r.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(PASTA, prefix + "__" + s.name + ".png"), tex.EncodeToPNG());
        Object.Destroy(tex);
        return prefix + ": " + s.name + " " + (int)r.width + "x" + (int)r.height + " ppu " + s.pixelsPerUnit + " (" + src.name + ")";
    }

    static T F<T>(object o, string n) => (T)AccessTools.Field(o.GetType(), n).GetValue(o);

    public static object Run()
    {
        Directory.CreateDirectory(PASTA);
        var sb = new StringBuilder();
        var wheel = Resources.FindObjectsOfTypeAll<UIHUDWheel>().FirstOrDefault(w => w.gameObject.scene.IsValid());
        if (wheel != null)
        {
            foreach (Sprite s in F<List<Sprite>>(wheel, "dayIcons")) sb.AppendLine(Save(s, "sigilo"));
            foreach (Sprite s in F<List<Sprite>>(wheel, "dayCustomWheelCenterIconDay")) sb.AppendLine(Save(s, "centro-dia"));
            foreach (Sprite s in F<List<Sprite>>(wheel, "dayCustomWheelCenterIconNight")) sb.AppendLine(Save(s, "centro-noite"));
            sb.AppendLine(Save(F<Sprite>(wheel, "centerWheelIconDaySpriteDefault"), "centro-padrao-dia"));
            sb.AppendLine(Save(F<Sprite>(wheel, "centerWheelIconNightSpriteDefault"), "centro-padrao-noite"));
            sb.AppendLine(Save(F<Image>(wheel, "housesNight").sprite, "casas-noite"));
            sb.AppendLine(Save(F<Image>(wheel, "sky").sprite, "ceu"));
            sb.AppendLine(Save(F<Image>(wheel, "horizon").sprite, "horizonte"));
            var c = F<RectTransform>(wheel, "center");
            sb.AppendLine("centro rect " + c.rect.size + "; imagem dia " + F<Image>(wheel, "centerWheelIconDay").rectTransform.rect.size);
        }
        foreach (var ic in Resources.FindObjectsOfTypeAll<UIMapDayActivityIcon>())
        {
            sb.AppendLine("mapa " + ic.Id + " dia=" + F<string>(ic, "dayId") + " tipo=" + F<MapDayType>(ic, "dayType"));
            sb.AppendLine("  " + Save(F<Sprite>(ic, "expandedSprite"), "mapa-exp-" + ic.Id));
            sb.AppendLine("  " + Save(F<Sprite>(ic, "collapsedSprite"), "mapa-rec-" + ic.Id));
        }
        var ks = MainGame.Instance.GameSave.knowledgeSystem;
        sb.AppendLine("liberados no save: " + string.Join(", ", ks.unlockedCustomHudDaySprites));
        return sb.ToString();
    }
}
