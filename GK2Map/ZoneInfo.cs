using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using HarmonyLib;
using LazyBearTechnology;
using UnityEngine;
using UnityEngine.UI;

namespace GK2Map
{
    /// <summary>
    /// O texto que a placa nativa do HUD mostra para uma zona ("Morgue 5/7"), montado só com leitura. O caminho nativo
    /// (<c>WorldZoneData.GetQualityString</c>/<c>GetTotalQuality</c>, <c>WZQual</c>) grava <c>wz_*</c> no save e pode
    /// soltar conquista; aqui a qualidade é somada à mão e os recursos do jogador são lidos crus. Formato que usa algo
    /// que este leitor não conhece mostra só o nome.
    /// </summary>
    internal static class ZoneInfo
    {
        private static readonly AccessTools.FieldRef<PlayerData, GameRes> ResRef = AccessTools.FieldRefAccess<PlayerData, GameRes>("res");
        private static readonly AccessTools.FieldRef<WorldZoneWidget, TextStyle> EnoughRef = AccessTools.FieldRefAccess<WorldZoneWidget, TextStyle>("qualityEnoughStyle");
        private static readonly AccessTools.FieldRef<WorldZoneWidget, TextStyle> NotEnoughRef = AccessTools.FieldRefAccess<WorldZoneWidget, TextStyle>("qualityNotEnoughStyle");
        private static readonly Regex ResToken = new Regex(@"\{\$([a-zA-Z0-9_]+):([^\}]+)\}");
        private static readonly Regex ExprToken = new Regex(@"LE\{([^\}]+)\}");
        private static readonly Regex Call = new Regex("^\\s*([A-Za-z]+)\\(\\s*(?:\"([^\"]*)\")?\\s*\\)\\s*$");

        public static string Label(WorldZone zone, WorldZoneWidget hud)
        {
            WorldZoneData wz = zone != null ? zone.Data : null;
            if (wz == null || wz.Definition == null) return string.Empty;
            string key = "wz_" + wz.id;
            string name = LLBase.L(key);
            if (string.IsNullOrEmpty(name) || name == key) return string.Empty;
            WorldZoneDef.DisplayType display = wz.Definition.displayType;
            if (display == WorldZoneDef.DisplayType.Hidden) return string.Empty;
            if (!wz.IsContainer || display == WorldZoneDef.DisplayType.None) return name;
            if (wz.id == "resurrection" && Mathf.Approximately(Raw("zombies_limit_mechanic"), 0f)) return name;
            float total = TotalQuality(wz);
            string info = Quality(wz, total);
            if (info == null) return name;
            TextStyle style = hud == null ? null : total >= 0f ? EnoughRef(hud) : NotEnoughRef(hud);
            return name + " " + (style != null ? style.ApplyStyleToString(info) : info);
        }

        private static string Quality(WorldZoneData wz, float total)
        {
            string text = wz.Definition.stringFormat;
            if (string.IsNullOrEmpty(text) || text.IndexOf('%') >= 0) return null;
            text = text.Replace("@", string.IsNullOrEmpty(wz.Definition.qualityIcon) ? string.Empty : wz.Definition.qualityIcon.FontIcon());
            bool ok = true;
            text = ResToken.Replace(text, m => string.Format(CultureInfo.CurrentCulture, "{0:" + m.Groups[2].Value + "}", Raw(m.Groups[1].Value)));
            text = ExprToken.Replace(text, m =>
            {
                float? v = Evaluate(m.Groups[1].Value, wz);
                if (v == null) { ok = false; return string.Empty; }
                return v.Value.ToString(CultureInfo.CurrentCulture);
            });
            if (!ok) return null;
            try { return string.Format(CultureInfo.CurrentCulture, text, total); }
            catch (System.FormatException) { return null; }
        }

        /// <summary>As funções que os formatos de zona usam hoje, sem os efeitos colaterais das originais.</summary>
        private static float? Evaluate(string expr, WorldZoneData wz)
        {
            Match m = Call.Match(expr);
            if (!m.Success) return null;
            string arg = m.Groups[2].Success ? m.Groups[2].Value : null;
            switch (m.Groups[1].Value)
            {
                case "PPar":
                    return arg == null ? (float?)null : Raw(arg);
                case "WZQual":
                    WorldZoneData other = arg == null ? null : MainGame.Instance.GameSave.worldData.GetWorldZoneDataById(arg);
                    return other == null ? (float?)null : TotalQuality(other);
                case "WZQualPowerSource":
                    return wz.GetTotalQuality(WorldZoneWgoQualityType.ConveyorPowerSource);
                case "WZQualConveyorCells":
                    return wz.GetTotalQuality(WorldZoneWgoQualityType.ConveyorCells);
                default:
                    return null;
            }
        }

        private static float Raw(string res)
        {
            PlayerData p = MainGame.PlayerData;
            return p == null ? 0f : ResRef(p).GetWithoutSystemsCheck(res);
        }

        /// <summary>A soma de <c>WorldZoneData.GetTotalQuality()</c> sem gravar <c>wz_*</c> nem testar conquista.</summary>
        private static float TotalQuality(WorldZoneData wz)
        {
            if (!wz.IsContainer) return 0f;
            List<SGuid> ids = wz.Definition != null && wz.Definition.hasCustomQualityZones ? wz.customQualityWgoDataList : wz.wgoDataList;
            float total = wz.additionalQuality;
            foreach (SGuid g in ids)
            {
                WgoData w = MainGame.WorldData.GetWgoData(g);
                if (w != null) total += w.Quality;
            }
            return total;
        }
    }
}
