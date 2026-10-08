using FishingPlanner;

int checks = 0;
void Check(bool condition, string description)
{
    checks++;
    if (!condition) throw new Exception("FAIL: " + description);
}
FishRule Rule(string times = "600 1900", string weather = "both", int level = 0, int difficulty = 30, string tutorial = "true") =>
    FishingRules.Parse($"Test/{difficulty}/mixed/10/30/{times}/spring/{weather}/0/3/0.5/0.1/{level}/{tutorial}")!;

var dayFish = Rule();
Check(dayFish != null, "valid rod fish parses");
Check(FishingRules.Parse("Lobster/trap/.05/1/4/ocean/1/20") == null, "crab-pot species excluded");
Check(FishingRules.Parse(null) == null, "missing fish metadata excluded");
Check(FishingRules.Parse("bad/data") == null, "malformed metadata excluded");
Check(FishingRules.Parse("Bad/20/mixed/10/30/600/spring/both/0/3/.5/.1/0") == null, "odd time span excluded");
Check(FishingRules.Parse("Bad/20/mixed/10/30/1900 600/spring/both/0/3/.5/.1/0") == null, "reversed time excluded");
foreach (int time in new[] { 500, 600, 610, 1850, 1900, 2000, 2600 })
    Check(dayFish!.Available(time, false, 0, false, false, null, false) == (time >= 600 && time < 1900), "exclusive end: " + time);
var nightFish = Rule("600 900 1800 2600");
foreach (int time in new[] { 600, 850, 900, 1200, 1750, 1800, 2400, 2550, 2600 })
    Check(nightFish.Available(time, false, 0, false, false, null, false) == (time < 900 || time >= 1800 && time < 2600), "multiple windows: " + time);
foreach (string weather in new[] { "both", "rainy", "sunny" })
foreach (bool rain in new[] { false, true })
foreach (int level in new[] { 0, 4, 5, 10 })
foreach (int time in new[] { 500, 600, 1850, 1900 })
{
    FishRule r = Rule(weather: weather, level: 5);
    bool expected = time >= 600 && time < 1900 && level >= 5 && (weather == "both" || weather == "rainy" == rain);
    Check(r.Available(time, rain, level, false, false, null, false) == expected, $"weather/level/time {weather}/{rain}/{level}/{time}");
    Check(r.Available(time, rain, level, true, false, null, false), "explicit spawn bypass is respected");
}
Check(!Rule(difficulty: 50).Available(1200, false, 10, false, true, null, false), "training rod excludes difficulty 50");
Check(Rule(difficulty: 49).Available(1200, false, 10, false, true, null, false), "training rod accepts difficulty 49");
Check(Rule(difficulty: 90).Available(1200, false, 10, false, true, true, false), "explicit training permission");
Check(!Rule().Available(1200, false, 10, true, true, false, false), "ignore-data does not override training restriction");
Check(!Rule(tutorial: "false").Available(1200, false, 10, true, false, null, true), "first catch remains tutorial-only");
Check(Rule().Available(1200, false, 0, false, false, null, true), "tutorial fish allowed");
Check(FishingRules.IsCaught("128", new[] { "(O)128" }), "qualified catch suppresses unqualified spawn");
Check(FishingRules.IsCaught("(O)Mod.Fish", new[] { "Mod.Fish" }), "mod IDs normalized");
Check(!FishingRules.IsCaught("128", new[] { "(O)129" }), "other fish doesn't suppress target");
Check(!FishingRules.IsCaught("128", Array.Empty<string>()), "one player's empty ledger remains independent");
Check(FishingRules.ChoosePool(new[] { "A" }, new[] { "MagicB", "MagicC", "MagicD" }).SequenceEqual(new[] { "A" }), "never fill two empty slots with magical fish");
Check(FishingRules.ChoosePool(new[] { "A", "B" }, new[] { "MagicC" }).Count == 2, "two normal fish stay two");
Check(FishingRules.ChoosePool(Array.Empty<string>(), new[] { "MagicA", "MagicB", "MagicC" }).Count == 3, "magic unlocks only with zero normal");
Check(FishingRules.ChoosePool(Array.Empty<string>(), Array.Empty<string>()).Count == 0, "empty availability honest");
Check(FishingRules.ChoosePool(new[] { "A", "B", "C", "D" }, new[] { "MagicE" }).Count == 4, "all normal suggestions available for paging");
Check(!FishingRules.IsCatchAll(49, 400, 49), "ordinary broad ocean not a magic pond");
Check(!FishingRules.IsCatchAll(80, 400, 80), "large regional habitat not catch-all");
Check(!FishingRules.IsCatchAll(400, 400, 5), "many species alone doesn't imply catch-all");
Check(FishingRules.IsCatchAll(350, 400, 340), "broad unrestricted catch-all detected");
Check(FishingRules.ForDestination("LOCATION_Season Here winter, WEATHER Here Rain") == "LOCATION_Season Target winter, WEATHER Target Rain", "remote map weather and season");
Check(FishingRules.ForDestination("ANY \"LOCATION_SEASON Here spring\" \"WEATHER Here Rain\"") == "ANY \"LOCATION_SEASON Target spring\" \"WEATHER Target Rain\"", "nested ANY remote conditions");
Check(FishingRules.ForDestination("PLAYER_HAS_MAIL Current Here, LOCATION_NAME Target Forest") == "PLAYER_HAS_MAIL Current Here, LOCATION_NAME Target Forest", "unrelated Here mail flag not changed");
Check(FishingRules.ForDestination("SEASON summer, TIME 1200 1600") == "SEASON summer, TIME 1200 1600", "global season and time preserved");
Check(nightFish.EndsAt(700, false) == 900 && nightFish.EndsAt(1800, false) == 2600, "urgency follows active window");
Check(nightFish.EndsAt(1200, true) == 2600, "ignored windows not used as expiration");
// Refresh and ranking tests exercise the same snapshot used by FishingService.
var failures = new List<string>();
void Regression(bool condition, string description)
{
    checks++;
    if (!condition) failures.Add(description);
}
Suggestion Fish(string id, params FishingSpot[] spots) => new(id, id, spots);
FishingSpot Spot(string location, int end, bool magic = false) => new(location, location, "", magic, end);
var state = new SuggestionSnapshot();
state.Publish(new[] {
    Fish("Remote late", Spot("Beach", 2600)),
    Fish("Local early", Spot("Town", 1900)),
    Fish("Local late", Spot("Town", 2000)),
    Fish("Remote early", Spot("Beach", 1800)),
    Fish("Magic", Spot("Pond", 2600, true))
}, "Town", true);
Regression(state.Suggestions.Select(f => f.Id).SequenceEqual(new[] { "Local late", "Local early", "Remote late", "Remote early" }),
    "current map first, then more remaining time; magical entries still excluded");
var previous = state.Suggestions;
state.BeginRefresh();
Regression(ReferenceEquals(previous, state.Suggestions), "refresh preserves the visible selection until publication");
Regression(state.HasPublished && !state.Fallback, "published status persists during refresh");
state.BeginRefresh();
Regression(ReferenceEquals(previous, state.Suggestions), "restarted/interrupted refresh also preserves selection");
state.Publish(new[] { Fish("Replacement", Spot("Town", 2200)) }, "Town", true);
Regression(state.Suggestions.Count == 1 && state.Suggestions[0].Id == "Replacement", "completed refresh replaces old list");
state.Publish(new[] {
    Fish("A", Spot("Town", 1800), Spot("Beach", 2600)),
    Fish("B", Spot("Town", 1900)),
    Fish("C", Spot("Mountain", 1800), Spot("Beach", 2000))
}, "Town", true);
Regression(state.Suggestions.Select(f => f.Id).SequenceEqual(new[] { "B", "A", "C" }),
    "local fish use their local closing time, not a later remote window");
Regression(state.Suggestions.Last().Spots[0].Location == "Beach", "remote fish lead with their longest window");
state.Publish(new[] { Fish("Magic", Spot("Pond", 2300, true)) }, "Town", true);
var magic = state.Suggestions;
state.BeginRefresh();
Regression(state.Fallback && ReferenceEquals(magic, state.Suggestions), "fallback list and label survive refresh together");
state.Publish(Array.Empty<Suggestion>(), "Town", true);
state.BeginRefresh();
Regression(state.HasPublished && state.Suggestions.Count == 0 && !state.Fallback, "completed empty state persists during refresh");
state.Reset();
Regression(!state.HasPublished && state.Suggestions.Count == 0 && !state.Fallback, "changing saves clears old farmer's recommendations");
var fallbackMaps = FishingRules.WithRequiredFallbackLocations(new[] { "Custom_Ridgeside_RidgePond", "My.CustomPond" });
Regression(fallbackMaps.Contains("Rexstrife20.MineralTownCP_GoddessHill"), "existing config gains Goddess Spring fallback");
Regression(fallbackMaps.Contains("My.CustomPond") && fallbackMaps.Count(m => m == "Custom_Ridgeside_RidgePond") == 1,
    "fallback migration preserves custom maps without duplicates");
Regression(FishingRules.WithRequiredFallbackLocations(null).Contains("Rexstrife20.MineralTownCP_GoddessHill"),
    "fresh config includes Goddess Spring");
// The farmer moves while the previous completed scan is still on screen.
state.Publish(new[] {
    Fish("Multi-map", Spot("Town", 2600), Spot("Beach", 2000), Spot("Beach", 1900)),
    Fish("Town only", Spot("Town", 2600))
}, "Town", true);
state.BeginRefresh();
state.Reprioritize("Beach");
Regression(state.Suggestions[0].Id == "Multi-map" && state.Suggestions[0].Spots[0].Location == "Beach",
    "warp reprioritizes both fish and its displayed location before refresh finishes");
var presented = SuggestionPresentation.ForLocation(state.Suggestions[0], "Town");
Regression(presented.Spots[0].Location == "Town", "display uses live location rather than snapshot's previous preference");
Regression(SuggestionPresentation.LocationLine(presented) == "Town (+1)", "row counts unique maps, not duplicate spawn rules");
string hover = SuggestionPresentation.Tooltip(presented, "Beach");
Regression(hover.Contains("Available in:\nBeach (here), Town"), "hover lists every unique available map, current first");
Regression(!hover.Contains("Example water tile"), "tooltip contains no sample casting tile");
var alias = SuggestionPresentation.ForLocation(Fish("Alias", Spot("Other", 2600), Spot("Map", 1900)), "Map_Instance", "Map");
Regression(alias.Spots[0].Location == "Map", "location's base name also matches instanced location");
Regression(SuggestionPresentation.LocationLine(Fish("Single", Spot("Town", 1900))) == "Town", "single-location row has no extra count");
Regression(state.Suggestions.Count == 2 && state.HasPublished, "warp keeps complete list visible");
if (failures.Count > 0) throw new Exception("Regression failures:\n" + string.Join("\n", failures));
Console.WriteLine($"PASS: {checks} offline assertions (no game process or save loaded).");
