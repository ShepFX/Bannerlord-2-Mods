using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TaleWorlds.Library;

namespace AlternateStart;

/// <summary>
/// Player-tunable values read from Modules/AlternateStart/settings.txt when character creation opens. Global keys are
/// plain (<c>skill_bonuses=1</c>); per-start keys are prefixed with the start's id (<c>vassal.gold=20000</c>).
/// A missing file or key means the default.
/// </summary>
internal sealed class StartSettings
{
    public bool SkillBonuses = true;
    public bool AllowStoryMode;
    public bool Debug;
    public int MerchantWorkshops = 2;
    public bool MerchantCaravan = true;
    public int OutlawCrime = 30;
    public int MilitiaRelation = 15;
    public int CaptiveHealthPercent = 30;
    public bool RulerOldRulerDies = true;
    public bool RulerKingdomTakesYourBanner = true;
    public bool RebelNamePrompt = true;

    private readonly Dictionary<string, string> _startKeys = new(StringComparer.OrdinalIgnoreCase);

    public static StartSettings Current = new();

    public static string FilePath => System.IO.Path.Combine(BasePath.Name, "Modules", "AlternateStart", "settings.txt");

    public static void Reload() => Current = Load();

    private static StartSettings Load()
    {
        var settings = new StartSettings();
        try
        {
            if (!File.Exists(FilePath)) return settings;
            foreach (string raw in File.ReadAllLines(FilePath))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();
                if (key.Contains("."))
                {
                    settings._startKeys[key] = value;
                    continue;
                }
                switch (key)
                {
                    case "skill_bonuses": settings.SkillBonuses = Flag(value); break;
                    case "allow_story_mode": settings.AllowStoryMode = Flag(value); break;
                    case "debug": settings.Debug = Flag(value); break;
                    case "merchant_workshops": settings.MerchantWorkshops = Int(value, settings.MerchantWorkshops, 10); break;
                    case "merchant_caravan": settings.MerchantCaravan = Flag(value); break;
                    case "outlaw_crime": settings.OutlawCrime = Int(value, settings.OutlawCrime, 100); break;
                    case "militia_relation": settings.MilitiaRelation = Int(value, settings.MilitiaRelation, 100); break;
                    case "captive_health_percent": settings.CaptiveHealthPercent = Math.Max(1, Int(value, settings.CaptiveHealthPercent, 100)); break;
                    case "ruler_old_ruler_dies": settings.RulerOldRulerDies = Flag(value); break;
                    case "ruler_kingdom_takes_your_banner": settings.RulerKingdomTakesYourBanner = Flag(value); break;
                    case "rebel_name_prompt": settings.RebelNamePrompt = Flag(value); break;
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Could not read {FilePath}: {ex.Message}");
        }
        return settings;
    }

    /// <summary>Overwrites a start's defaults with any <c>&lt;id&gt;.&lt;field&gt;</c> keys from the file.</summary>
    public void ApplyOverrides(StartDef start)
    {
        string? Get(string field) => _startKeys.TryGetValue(start.Id + "." + field, out string v) ? v : null;
        int GetInt(string field, int fallback, int max = int.MaxValue)
        {
            string? v = Get(field);
            return v == null ? fallback : Int(v, fallback, max);
        }

        string? enabled = Get("enabled");
        if (enabled != null) start.Enabled = Flag(enabled);
        start.Gold = GetInt("gold", start.Gold);
        start.Troops = GetInt("troops", start.Troops, 500);
        start.MinTier = GetInt("min_tier", start.MinTier, 6);
        start.MaxTier = Math.Max(start.MinTier, GetInt("max_tier", start.MaxTier, 6));
        start.ClanTier = GetInt("clan_tier", start.ClanTier, 6);
        start.Influence = GetInt("influence", start.Influence);
        start.Companions = GetInt("companions", start.Companions, 10);
        start.RulerRelation = GetInt("ruler_relation", start.RulerRelation, 100);
    }

    private static bool Flag(string value) =>
        value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase) || value.Equals("yes", StringComparison.OrdinalIgnoreCase);

    private static int Int(string value, int fallback, int max) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= 0 ? Math.Min(n, max) : fallback;
}

/// <summary>Writes problems to Modules/AlternateStart/errors.txt. Nothing is written when all is well.</summary>
internal static class ErrorLog
{
    private static string Dir => System.IO.Path.Combine(BasePath.Name, "Modules", "AlternateStart");

    public static void Write(string text) => Append("errors.txt", text);

    /// <summary>Only written when debug=1 in settings.txt; for working out why something did or did not happen.</summary>
    public static void Debug(string text)
    {
        if (StartSettings.Current.Debug) Append("debug.txt", text);
    }

    private static void Append(string file, string text)
    {
        try { File.AppendAllText(System.IO.Path.Combine(Dir, file), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text + Environment.NewLine); }
        catch { /* logging must never break the game */ }
    }
}
