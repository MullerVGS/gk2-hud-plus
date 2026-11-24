using System.IO;
using System.Linq;
using UnityEngine;
// Só leitura (green, perto do Folio): renderiza o Folio do mundo (objeto 3D) numa câmera própria, 1 px de arte = 1 px, e salva em PASTA;
// tools/art/folio-livro.py recorta o livro para GK2Crafting/art/folio_book.png.
public static class Snippet {
  const string PASTA = @"D:\GK2-green\artdump\folio";
  public static object Run() {
    Directory.CreateDirectory(PASTA);
    var w = Object.FindObjectsByType<Wgo>(FindObjectsSortMode.None).FirstOrDefault(x => x.HasData && x.Data.id == "alchemy_recipe_book");
    if (w == null) return "livro não carregado";
    Camera wc = CameraSystem.Instance.WorldCamera;
    var wrt = wc.targetTexture;
    string info = "world cam ortho=" + wc.orthographic + " size=" + wc.orthographicSize + " rt=" + (wrt ? wrt.width + "x" + wrt.height : "tela " + Screen.width + "x" + Screen.height) + " rot=" + wc.transform.eulerAngles;
    int rtH = wrt ? wrt.height : Screen.height;
    float ppu = rtH / (2f * wc.orthographicSize) / 2f;
    var rends = w.GetComponentsInChildren<Renderer>(true).Where(r => !(r is SpriteRenderer)).ToList();
    var layers = rends.ToDictionary(r => r, r => r.gameObject.layer);
    foreach (var r in rends) r.gameObject.layer = 31;
    Bounds b = rends[0].bounds; foreach (var r in rends) b.Encapsulate(r.bounds);
    var go = new GameObject("gk2-folio-cam"); var cam = go.AddComponent<Camera>();
    cam.CopyFrom(wc);
    int size = 64;
    var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
    cam.targetTexture = rt; cam.cullingMask = 1 << 31; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
    cam.orthographicSize = size / (2f * ppu);
    Vector3 off = b.center - wc.transform.position;
    cam.transform.position = wc.transform.position + wc.transform.right * Vector3.Dot(off, wc.transform.right) + wc.transform.up * Vector3.Dot(off, wc.transform.up);
    cam.Render();
    var prev = RenderTexture.active; RenderTexture.active = rt;
    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false); tex.ReadPixels(new Rect(0, 0, size, size), 0, 0); tex.Apply();
    RenderTexture.active = prev;
    File.WriteAllBytes(Path.Combine(PASTA, "folio-render.png"), tex.EncodeToPNG());
    foreach (var kv in layers) kv.Key.gameObject.layer = kv.Value;
    Object.Destroy(go); rt.Release(); Object.Destroy(rt); Object.Destroy(tex);
    return info + " ppu=" + ppu + " bounds=" + b.size;
  }
}
