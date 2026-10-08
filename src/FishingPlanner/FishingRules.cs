using System.Globalization;
using System.Text.RegularExpressions;

namespace FishingPlanner;

/// <summary>Pure rules shared by the runtime adapter and offline tests.</summary>
public static class FishingRules
{
    public static string[] WithRequiredFallbackLocations(IEnumerable<string>? configured) =>
        (configured ?? Array.Empty<string>()).Append("Custom_Ridgeside_RidgePond")
            .Append("Rexstrife20.MineralTownCP_GoddessHill")
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    public static string Unqualify(string id) => id.StartsWith("(O)", StringComparison.Ordinal) ? id[3..] : id;
    public static string Qualify(string id) => "(O)" + Unqualify(id);

    // Here means the player's actual map in GSQ, even when a context is supplied.
    // Evaluate remote weather/season against the destination without warping the player.
    public static string ForDestination(string? query) => Regex.Replace(query ?? "",
        @"(\b(?:LOCATION_\w+|WEATHER)\s+)Here\b", "${1}Target", RegexOptions.IgnoreCase);

    public static bool IsCaught(string id, IEnumerable<string> caught) => caught.Any(p => Unqualify(p) == Unqualify(id));

    public static IReadOnlyList<T> ChoosePool<T>(IEnumerable<T> ordinary, IEnumerable<T> fallback)
    {
        T[] normal = ordinary.ToArray();
        return normal.Length > 0 ? normal : fallback.ToArray();
    }

    public static bool IsCatchAll(int distinctRodFish, int catalogRodFish, int unrestrictedFish) =>
        distinctRodFish >= 60 && catalogRodFish > 0 && distinctRodFish >= catalogRodFish * .75
        && unrestrictedFish >= distinctRodFish * .8;

    public static FishRule? Parse(string? raw)
    {
        if (raw == null) return null;
        string[] f = raw.Split('/');
        if (f.Length < 13 || f[1] == "trap" || !int.TryParse(f[1], out int difficulty)
            || !int.TryParse(f[12], out int level) || !int.TryParse(f[9], out int depth)
            || !float.TryParse(f[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float chance)
            || !float.TryParse(f[11], NumberStyles.Float, CultureInfo.InvariantCulture, out float multiplier)) return null;
        string[] times = f[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (times.Length == 0 || times.Length % 2 != 0) return null;
        var windows = new List<(int Start, int End)>();
        for (int i = 0; i < times.Length; i += 2)
        {
            if (!int.TryParse(times[i], out int start) || !int.TryParse(times[i+1], out int end) || start >= end) return null;
            windows.Add((start, end));
        }
        return new FishRule(difficulty, level, f[7], windows, depth, chance, multiplier,
            f.Length > 13 && bool.TryParse(f[13], out bool tutorial) && tutorial);
    }
}

public sealed record FishRule(int Difficulty, int MinLevel, string Weather, List<(int Start, int End)> Windows,
    int MaxDepth, float Chance, float DepthMultiplier, bool Tutorial)
{
    public bool Available(int time, bool raining, int level, bool ignoreData, bool trainingRod, bool? trainingAllowed, bool firstCatch)
    {
        if (trainingRod && !(trainingAllowed ?? Difficulty < 50)) return false;
        if (firstCatch && !Tutorial) return false;
        return ignoreData || (level >= MinLevel && Windows.Any(w => time >= w.Start && time < w.End)
            && (Weather != "rainy" || raining) && (Weather != "sunny" || !raining));
    }
    public int EndsAt(int now, bool ignoreData) => ignoreData ? 2600 : Windows.FirstOrDefault(w => now >= w.Start && now < w.End).End;
    public float BaseChance(int depth, int level, bool training) => Math.Min(.9f,
        (Chance - Math.Max(0, MaxDepth - depth) * DepthMultiplier * Chance + level / 50f) * (training ? 1.1f : 1f));
}
