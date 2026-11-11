using HarmonyLib;
using LazyBearTechnology;

// GK2Hud (muda o save do green): prepara a viagem do burro. ESTADO = "corpos=<n>" (cur_bodies_count no necrotério),
// "muitos=<0|1>" (nun_many_body_drop: a Freira ouviu "muitos corpos"), "trava=<0|1>" (global_lock_on_drop_bodies) ou
// "chance=<0..100>" (donkey_body_drop_chance). Vários separados por espaço. Depois: gk2 save reset.
public static class Snippet
{
    static string ESTADO = "corpos=0";

    public static object Run()
    {
        var res = (GameRes)AccessTools.Field(typeof(PlayerData), "res").GetValue(MainGame.PlayerData);
        var keys = new System.Collections.Generic.Dictionary<string, string>
        {
            ["corpos"] = "cur_bodies_count", ["muitos"] = "nun_many_body_drop", ["trava"] = "global_lock_on_drop_bodies", ["chance"] = "donkey_body_drop_chance",
        };
        foreach (string kv in ESTADO.Split(' '))
        {
            string[] a = kv.Split('=');
            res.SetWithoutSystemsCheck(keys[a[0]], float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
        }
        string R(string k) => k + "=" + res.GetWithoutSystemsCheck(k);
        return string.Join(" ", new[] { R("cur_bodies_count"), R("nun_many_body_drop"), R("global_lock_on_drop_bodies"), R("donkey_body_drop_chance") });
    }
}
