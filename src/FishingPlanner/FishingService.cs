using System.Globalization;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Locations;
using StardewValley.Tools;

namespace FishingPlanner;

/// <summary>Read-only adapter: never calls getFish, changes a Farmer, or creates a mine floor.</summary>
public sealed class FishingService
{
    private readonly IMonitor monitor;
    private readonly Dictionary<GameLocation, List<WaterTile>> geometry = new();
    private readonly HashSet<string> warnings = new();
    private Dictionary<string, FishRule> fish = new();
    private HashSet<string> caught = new();
    private readonly Random queryRandom = new(1831); // independent of Game1.random
    private readonly Dictionary<string, bool> conditions = new();
    private readonly Dictionary<string, List<FishingSpot>> found = new();
    private IEnumerator<int>? work;
    private ModConfig config = new();
    private int scanTime;
    private readonly SuggestionSnapshot snapshot = new();
    public IReadOnlyList<Suggestion> Suggestions => snapshot.Suggestions;
    public bool HasPublished => snapshot.HasPublished;
    public bool Scanning => work != null;
    public bool Fallback => snapshot.Fallback;
    public int UnvisitedSkipped { get; private set; }
    public int FailedLocations { get; private set; }
    public int Remaining { get; private set; }

    private static readonly HashSet<string> OpenVanilla = new(StringComparer.OrdinalIgnoreCase)
    { "Farm", "Town", "Forest", "Mountain", "Beach", "BeachNightMarket", "Desert", "Railroad" };

    public FishingService(IMonitor monitor) => this.monitor = monitor;
    public void Reset()
    {
        work?.Dispose(); work = null; geometry.Clear(); conditions.Clear(); warnings.Clear();
        snapshot.Reset();
    }
    public void Reprioritize(GameLocation current) => snapshot.Reprioritize(current.NameOrUniqueName, current.Name);
    public void InvalidateMaps() => geometry.Clear();

    public void Start(ModConfig settings)
    {
        work?.Dispose(); config = settings; scanTime = Game1.timeOfDay;
        snapshot.BeginRefresh(); found.Clear(); conditions.Clear();
        UnvisitedSkipped = 0; FailedLocations = 0;
        caught = Game1.player.fishCaught.Keys.Select(FishingRules.Unqualify).ToHashSet(StringComparer.Ordinal);
        fish = DataLoader.Fish(Game1.content)
            .Select(p => (p.Key, Rule: FishingRules.Parse(p.Value)))
            .Where(p => p.Rule != null && Game1.objectData.TryGetValue(p.Key, out var obj)
                && obj.Type == "Fish" && !obj.ExcludeFromFishingCollection)
            .ToDictionary(p => p.Key, p => p.Rule!);
        Remaining = fish.Keys.Count(id => !caught.Contains(id));
        work = Scan().GetEnumerator();
    }

    public void Step()
    {
        if (work == null) return;
        try
        {
            if (work.MoveNext()) return;
            work.Dispose(); work = null;
            snapshot.Publish(found.Select(p => new Suggestion(p.Key,
                ItemRegistry.GetDataOrErrorItem(FishingRules.Qualify(p.Key)).DisplayName, p.Value.ToArray())),
                Game1.currentLocation.NameOrUniqueName, FailedLocations == 0);
        }
        catch (Exception ex)
        {
            work?.Dispose(); work = null; FailedLocations++;
            monitor.Log($"Fishing scan failed; will retry on the next refresh: {ex}", LogLevel.Warn);
        }
    }

    private IEnumerable<int> Scan()
    {
        // Only existing client-visible instances; no dynamic map/mine creation or warping.
        foreach (GameLocation location in new[] { Game1.currentLocation }.Concat(Game1.locations)
            .GroupBy(p => p.NameOrUniqueName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray())
        {
            LocationData? data = null;
            try
            {
                if (location is MineShaft || !Accessible(location)) continue;
                data = location.GetData();
                if (data?.Fish?.Count is not > 0) continue;
            }
            catch (Exception ex) { Failed(location, ex); }
            if (data == null) continue;
            // Geometry is prepared in small pieces, so large expansion maps don't monopolize a frame.
            if (location is not Submarine && !geometry.ContainsKey(location))
            {
                using var prepare = PrepareWater(location).GetEnumerator();
                while (true)
                {
                    bool more;
                    try { more = prepare.MoveNext(); }
                    catch (Exception ex) { Failed(location, ex); break; }
                    if (!more) break;
                    yield return 0;
                }
            }
            try { ScanLocation(location, data); }
            catch (Exception ex) { Failed(location, ex); }
            yield return 0;
        }
        AddMines();
    }

    private void Failed(GameLocation location, Exception ex)
    {
        FailedLocations++;
        Warn(location.NameOrUniqueName, $"Could not inspect {location.NameOrUniqueName}: {ex.Message}");
    }

    private bool Accessible(GameLocation location)
    {
        string name = location.NameOrUniqueName;
        if (config.ExcludedLocations.Contains(name, StringComparer.OrdinalIgnoreCase)) return false;
        if (config.LocationConditions.TryGetValue(name, out string? condition) && !Check(condition, location)) return false;
        if (name == "Submarine")
            return Game1.currentSeason == "winter" && Game1.dayOfMonth is >= 15 and <= 17
                && (scanTime is >= 1700 and < 2300 || Game1.currentLocation == location);
        if (name == "BeachNightMarket" && !(Game1.currentSeason == "winter" && Game1.dayOfMonth is >= 15 and <= 17)) return false;
        if (name == "Beach" && Game1.currentSeason == "winter" && Game1.dayOfMonth is >= 15 and <= 17) return false;
        if (name == "DesertFestival") return Game1.currentLocation == location;
        if (!Game1.isLocationAccessible(name)) return false;
        if (OpenVanilla.Contains(name) || Game1.currentLocation == location) return true;
        if (Game1.player.locationsVisited.Contains(name)) return true;
        // Remaining vanilla interiors/dungeons and modded maps can be story-locked.
        // Visits are the reliable common API across content mods; users may opt out.
        if (config.OnlyVisitedModLocations) { UnvisitedSkipped++; return false; }
        return true;
    }

    private bool IsFallback(GameLocation location, LocationData data)
    {
        if (config.FallbackLocations.Contains(location.NameOrUniqueName, StringComparer.OrdinalIgnoreCase)) return true;
        var entries = (data.Fish ?? new()).Where(p => p.ItemId != null && fish.ContainsKey(FishingRules.Unqualify(p.ItemId))).ToArray();
        int distinct = entries.Select(p => FishingRules.Unqualify(p.ItemId)).Distinct().Count();
        int unrestricted = entries.Where(p => p.IgnoreFishDataRequirements && p.Season == null && string.IsNullOrEmpty(p.Condition))
            .Select(p => FishingRules.Unqualify(p.ItemId)).Distinct().Count();
        return FishingRules.IsCatchAll(distinct, fish.Count, unrestricted);
    }

    private void ScanLocation(GameLocation location, LocationData data)
    {
        bool fallback = IsFallback(location, data);
        string label = location.DisplayName;
        if (string.IsNullOrWhiteSpace(label)) label = location.Name;
        if (location is Submarine) label = "Night Market submarine";
        if (location is not Submarine && !location.canFishHere()) return;
        List<SpawnFishData> defaults = Game1.locationData.TryGetValue("Default", out var common) ? common.Fish : new();
        foreach (var spawn in (data.Fish ?? new()).Concat(defaults ?? new()))
            Evaluate(location, spawn, fallback, label, new HashSet<string>(), false);
        // Farm maps may route some or all catches to another location via this property.
        if (location is Farm)
        {
            string[] parts = location.GetMapPropertySplitBySpaces("FarmFishLocationOverride");
            if (parts.Length >= 2 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float chance) && chance > 0)
            {
                if (chance >= 1) RemoveLocation(location.NameOrUniqueName);
                GameLocation? source = Game1.locations.FirstOrDefault(p => p.Name == parts[0]);
                if (source?.GetData() is LocationData sourceData)
                    foreach (var spawn in (sourceData.Fish ?? new()).Concat(defaults ?? new()))
                        Evaluate(source, spawn, fallback || IsFallback(source, sourceData), label, new(), false, fishingLocation: location);
            }
        }
    }

    private void RemoveLocation(string name)
    {
        foreach (var spots in found.Values) spots.RemoveAll(p => p.Location == name);
    }

    private void Evaluate(GameLocation location, SpawnFishData spawn, bool fallback, string label, HashSet<string> chain,
        bool inherited, List<Restriction>? parents = null, bool mine = false, GameLocation? fishingLocation = null)
    {
        fishingLocation ??= location;
        Farmer player = Game1.player;
        if (inherited && !spawn.CanBeInherited || spawn.RequireMagicBait || spawn.Season.HasValue && spawn.Season != location.GetSeason()
            || player.FishingLevel < spawn.MinFishingLevel || spawn.CatchLimit == 0 || !Check(spawn.Condition, location)) return;
        bool lure = (player.CurrentTool as FishingRod)?.HasCuriosityLure() == true;
        float spawnChance = spawn.GetChance(lure, player.DailyLuck, player.LuckLevel,
            (v, modifiers, mode) => Utility.ApplyQuantityModifiers(v, modifiers, mode, location, player, random: queryRandom), false);
        if (spawnChance <= 0) return;
        if (spawn.UseFishCaughtSeededRandom && Utility.CreateRandom(Game1.uniqueIDForThisGame, player.stats.Get("PreciseFishCaught") * 859).NextDouble() >= spawnChance) return;
        IEnumerable<string> ids = spawn.RandomItemId?.Count > 0 ? spawn.RandomItemId : new[] { spawn.ItemId };
        foreach (string? rawId in ids)
        {
            if (string.IsNullOrWhiteSpace(rawId)) continue;
            if (rawId.StartsWith("LOCATION_FISH ", StringComparison.Ordinal))
            {
                string[] parts = rawId.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 5 || parts[2] != "BOBBER_X" || parts[3] != "BOBBER_Y" || parts[4] != "WATER_DEPTH")
                { Warn(rawId, $"Skipping unsupported fishing query: {rawId}"); continue; }
                if (!chain.Add(parts[1])) continue;
                GameLocation? source = Game1.locations.FirstOrDefault(p => p.Name == parts[1]);
                LocationData? sourceData = source?.GetData();
                // LOCATION_FISH evaluates the source's season/weather/area with the destination's cast coordinates.
                if (source != null && sourceData?.Fish != null)
                    foreach (var child in sourceData.Fish.Concat(Game1.locationData["Default"].Fish))
                        Evaluate(source, child, fallback || IsFallback(source, sourceData), label, chain, true,
                            (parents ?? new()).Append(new Restriction(spawn, location)).ToList(), mine, fishingLocation);
                chain.Remove(parts[1]); continue;
            }
            string id = FishingRules.Unqualify(rawId);
            if (caught.Contains(id) || !fish.TryGetValue(id, out FishRule? rule)) continue;
            bool training = player.CurrentTool?.QualifiedItemId == "(T)TrainingRod";
            if (!rule.Available(scanTime, location.IsRainingHere(), player.FishingLevel, spawn.IgnoreFishDataRequirements,
                training, spawn.CanUseTrainingRod, player.fishCaught.Length == 0)) continue;
            Item item = ItemRegistry.Create(FishingRules.Qualify(id));
            if (!Check(spawn.PerItemCondition, location, item)
                || parents?.Any(p => !Check(p.Spawn.PerItemCondition, p.DataLocation, item)) == true) continue;
            List<Restriction> restrictions = (parents ?? new()).Append(new Restriction(spawn, location)).ToList();
            WaterTile? water = mine || fishingLocation is Submarine ? null : FindWater(fishingLocation, restrictions,
                depth => spawn.IgnoreFishDataRequirements || GenericChance(rule, spawn, location, depth, training, lure) > 0);
            if (water == null && !mine && fishingLocation is not Submarine) continue;
            int depth = water?.Depth ?? 5;
            if (!spawn.IgnoreFishDataRequirements && GenericChance(rule, spawn, location, depth, training, lure) <= 0) continue;
            Add(id, new FishingSpot(fishingLocation.NameOrUniqueName, label,
                Detail(location, spawn, rule), fallback, rule.EndsAt(scanTime, spawn.IgnoreFishDataRequirements)));
        }
    }

    private float GenericChance(FishRule rule, SpawnFishData spawn, GameLocation location, int depth, bool training, bool lure)
    {
        float chance = rule.BaseChance(depth, Game1.player.FishingLevel, training);
        if (lure && chance < .25f)
            chance = spawn.CuriosityLureBuff >= 0 ? chance + spawn.CuriosityLureBuff : .68f * chance + .085f;
        if (spawn.ApplyDailyLuck) chance += (float)Game1.player.DailyLuck;
        return spawn.ChanceModifiers?.Count > 0
            ? Utility.ApplyQuantityModifiers(chance, spawn.ChanceModifiers, spawn.ChanceModifierMode, location, Game1.player, random: queryRandom)
            : chance;
    }

    private bool Check(string? condition, GameLocation location, Item? item = null)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        string query = FishingRules.ForDestination(condition);
        string key = location.NameOrUniqueName + "|" + item?.QualifiedItemId + "|" + query;
        if (!conditions.TryGetValue(key, out bool result))
            conditions[key] = result = GameStateQuery.CheckConditions(query, location, Game1.player, item, random: queryRandom);
        return result;
    }

    private void Add(string id, FishingSpot spot)
    {
        if (!found.TryGetValue(id, out var spots)) found[id] = spots = new();
        if (!spots.Contains(spot)) spots.Add(spot);
    }

    private void AddMines()
    {
        Farmer p = Game1.player;
        if (p.CurrentTool?.QualifiedItemId == "(T)TrainingRod") return;
        foreach ((int floor, string id) in new[] { (20, "158"), (60, "161"), (100, "162") })
            if (p.deepestMineLevel >= floor && !caught.Contains(id) && fish.ContainsKey(id))
                Add(id, new FishingSpot("UndergroundMine" + floor, $"Mines · floor {floor}", "Fish in the lake on this floor. Available all day.", false, 2600));
        GameLocation? mine = Game1.locations.FirstOrDefault(l => l.Name == "Mine");
        if (p.deepestMineLevel >= 20 && mine != null && Game1.locationData.TryGetValue("UndergroundMine", out var data))
            foreach (var spawn in data.Fish.Concat(Game1.locationData["Default"].Fish))
                Evaluate(mine, spawn, false, "Mines · floors 20 / 60", new(), false, mine: true);
    }

    private IEnumerable<int> PrepareWater(GameLocation location)
    {
        var points = new List<WaterTile>();
        var layer = location.Map?.Layers.FirstOrDefault();
        if (layer == null) { geometry[location] = points; yield break; }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int y = 0; y < layer.LayerHeight; y++)
        {
            for (int x = 0; x < layer.LayerWidth; x++)
            {
                if (!location.isWaterTile(x, y) || !location.isTileFishable(x, y)) continue;
                Vector2 tile = new(x, y);
                location.TryGetFishAreaForTile(tile, out string area, out _);
                points.Add(new WaterTile(tile, area));
            }
            if (timer.ElapsedMilliseconds >= 3) { yield return 0; timer.Restart(); }
        }
        geometry[location] = points;
    }

    private WaterTile? FindWater(GameLocation location, List<Restriction> restrictions, Func<int, bool> chancePossible)
    {
        if (!geometry.TryGetValue(location, out var points)) return null;
        foreach (WaterTile point in points)
        {
            if (restrictions.Any(r => !MatchesWater(r, point.Tile))) continue;
            point.Depth ??= FishingRod.distanceToLand((int)point.Tile.X, (int)point.Tile.Y, location);
            if (!chancePossible(point.Depth.Value) || restrictions.Any(r => point.Depth < r.Spawn.MinDistanceFromShore
                || r.Spawn.MaxDistanceFromShore >= 0 && point.Depth > r.Spawn.MaxDistanceFromShore)) continue;
            bool boundedPlayer = restrictions.Any(r => r.Spawn.PlayerPosition.HasValue);
            if (!boundedPlayer && point.CheckedLevel == Game1.player.FishingLevel)
            {
                if (point.Castable) return point;
                continue;
            }
            bool castable = HasCastingTile(location, point.Tile, restrictions.Select(r => r.Spawn).ToList());
            if (!boundedPlayer) { point.CheckedLevel = Game1.player.FishingLevel; point.Castable = castable; }
            if (castable) return point;
        }
        return null;
    }

    private static bool MatchesWater(Restriction restriction, Vector2 tile)
    {
        SpawnFishData spawn = restriction.Spawn;
        if (spawn.BobberPosition.HasValue && !spawn.BobberPosition.Value.Contains(tile)) return false;
        if (spawn.FishAreaId == null) return true;
        restriction.DataLocation.TryGetFishAreaForTile(tile, out string id, out _);
        return spawn.FishAreaId == id;
    }

    private sealed record Restriction(SpawnFishData Spawn, GameLocation DataLocation);

    private static bool HasCastingTile(GameLocation location, Vector2 water, List<SpawnFishData> restrictions)
    {
        int level = Game1.player.FishingLevel;
        int extra = level >= 15 ? 4 : level >= 8 ? 3 : level >= 4 ? 2 : level >= 1 ? 1 : 0;
        for (int d = 1; d <= 4 + extra; d++)
            for (int drift = -1; drift <= 1; drift++)
                foreach (Vector2 offset in new[] { new Vector2(d, drift), new Vector2(-d, drift), new Vector2(drift, d), new Vector2(drift, -d) })
                {
                    Vector2 tile = water + offset;
                    if (!location.isTileOnMap(tile) || location.isWaterTile((int)tile.X, (int)tile.Y) || !location.isTilePassable(tile)) continue;
                    if (restrictions.All(s => !s.PlayerPosition.HasValue || s.PlayerPosition.Value.Contains(tile))) return true;
                }
        return false;
    }

    private static string Detail(GameLocation location, SpawnFishData spawn, FishRule rule)
    {
        var details = new List<string>();
        if (spawn.FishAreaId != null) details.Add("Water: " + (location.GetFishingAreaDisplayName(spawn.FishAreaId) ?? spawn.FishAreaId));
        if (spawn.PlayerPosition is Rectangle stand) details.Add($"Stand at tiles {stand.X},{stand.Y}–{stand.Right-1},{stand.Bottom-1}");
        if (spawn.BobberPosition is Rectangle cast) details.Add($"Cast into tiles {cast.X},{cast.Y}–{cast.Right-1},{cast.Bottom-1}");
        if (spawn.MinDistanceFromShore > 0) details.Add($"At least {spawn.MinDistanceFromShore} tiles from shore");
        if (spawn.MaxDistanceFromShore >= 0) details.Add($"At most {spawn.MaxDistanceFromShore} tiles from shore");
        if (!spawn.IgnoreFishDataRequirements)
        {
            details.Add(string.Join(", ", rule.Windows.Select(w => $"{Time(w.Start)}–{Time(w.End)}")));
            if (rule.Weather != "both") details.Add(rule.Weather == "rainy" ? "Rain" : "Dry weather");
        }
        return string.Join("\n", details);
    }
    public static string Time(int time) => $"{time / 100:00}:{time % 100:00}";
    private void Warn(string key, string message) { if (warnings.Add(key)) monitor.Log(message, LogLevel.Trace); }
    private sealed record WaterTile(Vector2 Tile, string? Area)
    {
        public int? Depth { get; set; }
        public int CheckedLevel { get; set; } = -1;
        public bool Castable { get; set; }
    }
}
