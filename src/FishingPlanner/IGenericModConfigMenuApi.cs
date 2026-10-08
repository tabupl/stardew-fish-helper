using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace FishingPlanner;

/// <summary>The small subset of GMCM used by the planner.</summary>
public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null);
}
