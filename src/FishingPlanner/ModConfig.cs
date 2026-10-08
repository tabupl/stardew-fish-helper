using StardewModdingAPI.Utilities;
namespace FishingPlanner;
public sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public KeybindList Toggle { get; set; } = KeybindList.Parse("LeftControl + LeftShift + F");
    public int Rows { get; set; } = 3;
    public int X { get; set; } = 20;
    public int Y { get; set; } = 200;
    public int Width { get; set; } = 370;
    public bool OnlyVisitedModLocations { get; set; } = true;
    public string[] FallbackLocations { get; set; } = FishingRules.WithRequiredFallbackLocations(null);
    public string[] ExcludedLocations { get; set; } = Array.Empty<string>();
    public Dictionary<string, string> LocationConditions { get; set; } = new()
    {
        ["Custom_Ridgeside_RidgePond"] = "PLAYER_HAS_MAIL Host LinkedFishesLetter",
    };
}
