namespace FishingPlanner;

public sealed record FishingSpot(string Location, string Label, string Detail, bool Fallback, int EndTime);
public sealed record Suggestion(string Id, string Name, IReadOnlyList<FishingSpot> Spots);

/// <summary>The completed recommendation list, separate from the scan being built.</summary>
public sealed class SuggestionSnapshot
{
    public IReadOnlyList<Suggestion> Suggestions { get; private set; } = Array.Empty<Suggestion>();
    public bool Fallback { get; private set; }
    public bool HasPublished { get; private set; }

    public void Reset()
    {
        Suggestions = Array.Empty<Suggestion>(); Fallback = false; HasPublished = false;
    }

    public void BeginRefresh()
    {
        // Keep the last completed snapshot, including an empty result or fallback label.
        // Publish replaces it only after the entire next scan has completed.
    }

    public void Reprioritize(string currentLocation, string? currentName = null)
    {
        if (!HasPublished) return;
        Suggestions = Suggestions.Select(p => SuggestionPresentation.ForLocation(p, currentLocation, currentName))
            .OrderByDescending(p => SuggestionPresentation.IsCurrent(p.Spots[0], currentLocation, currentName))
            .ThenByDescending(p => p.Spots[0].EndTime)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public void Publish(IEnumerable<Suggestion> candidates, string currentLocation, bool allowFallback)
    {
        Suggestion[] all = candidates.ToArray();
        List<Suggestion> Pool(bool fallback) => all
            .Select(p => p with { Spots = p.Spots.Where(s => s.Fallback == fallback).Distinct()
                .OrderByDescending(s => s.Location == currentLocation)
                .ThenByDescending(s => s.EndTime).ThenBy(s => s.Label).ToArray() })
            .Where(p => p.Spots.Count > 0)
            .OrderByDescending(p => p.Spots.Any(s => s.Location == currentLocation))
            .ThenByDescending(p => p.Spots[0].EndTime)
            .ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var normal = Pool(false);
        var selected = FishingRules.ChoosePool(normal, allowFallback ? Pool(true) : new List<Suggestion>());
        Suggestions = selected;
        Fallback = normal.Count == 0 && selected.Count > 0;
        HasPublished = true;
    }
}

/// <summary>Present the existing results against the player's live location, including during refreshes.</summary>
public static class SuggestionPresentation
{
    public static bool IsCurrent(FishingSpot spot, string currentLocation, string? currentName = null) =>
        string.Equals(spot.Location, currentLocation, StringComparison.OrdinalIgnoreCase)
        || currentName != null && string.Equals(spot.Location, currentName, StringComparison.OrdinalIgnoreCase);

    public static Suggestion ForLocation(Suggestion fish, string currentLocation, string? currentName = null) => fish with
    {
        Spots = fish.Spots.OrderByDescending(s => IsCurrent(s, currentLocation, currentName))
            .ThenByDescending(s => s.EndTime).ThenBy(s => s.Label).ToArray()
    };

    public static IReadOnlyList<FishingSpot> Locations(Suggestion fish) => fish.Spots
        .GroupBy(s => s.Location, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();

    public static string LocationLine(Suggestion fish)
    {
        var locations = Locations(fish);
        return locations[0].Label + (locations.Count > 1 ? $" (+{locations.Count - 1})" : "");
    }

    public static string Tooltip(Suggestion fish, string currentLocation, string? currentName = null)
    {
        fish = ForLocation(fish, currentLocation, currentName);
        string locations = string.Join(", ", Locations(fish).Select(s => s.Label
            + (IsCurrent(s, currentLocation, currentName) ? " (here)" : "")));
        return fish.Name + "\n" + fish.Spots[0].Detail + "\n\nAvailable in:\n" + locations
            + "\n\nClick for fishing details and F1 lookup.";
    }
}
