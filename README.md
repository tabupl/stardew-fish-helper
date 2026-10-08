# Fishing Planner

A Stardew Valley 1.6 SMAPI mod for completing your fishing collection.
**Current version: 1.0.2.**

[Source repository](https://github.com/tabupl/stardew-fish-helper) ·
[Report an issue](https://github.com/tabupl/stardew-fish-helper/issues) ·
[MIT license](LICENSE)

A personal SMAPI HUD for the fish still missing from your farmer's fishing collection.
Shows three available fish by default, with up to eight rows configurable in Generic
Mod Config Menu. Read-only; adds no save data, game content or multiplayer messages.
Each player installs the same ZIP if they want the helper, and sees their own catches.

## Controls

- The panel appears automatically after loading a farm, while the normal HUD is visible.
- **Left Ctrl + Left Shift + F** toggles it. Rebind in Generic Mod Config Menu.
- Drag the title to move it. Use the arrows or mouse wheel over the panel to page.
- The list names your current map first when the fish is available there, even while refreshing.
- Hover a fish for its available locations (each map listed once, current map marked "here").
- Click a fish for time windows, water areas, and special casting rules.
- In that details screen, hover the fish icon and press F1 for Lookup Anything.
- SMAPI console: `fishing_planner`, `fishing_planner refresh`, `fishing_planner report`.
  The report writes local diagnostics in this mod's `verification/latest-report.json`.

## Selection

Reads Content Patcher's live Data/Fish, Data/Locations and Data/Objects, and the local
farmer's actual catch ledger. Includes vanilla and modded fish counted in the fishing
collection. Excludes crab-pot entries (`trap`), trash, seaweed, stocked building ponds,
and objects excluded from fishing collection/perfection by their content mod.

Checks destination season and rain, current time, fishing level, training-rod limits,
first-catch rules, spawn game-state queries (including quest/mail requirements), fish
areas, bobber/player rectangles, and shore-distance constraints. It checks natural water
and a plausible casting tile, including inherited LOCATION_FISH lists and farm overrides.
Required casting rectangles use tile coordinates; they are not a route/pathfinding promise.
Example water-tile coordinates are not displayed.
Mines floor 20/60/100 fish have explicit support without creating/loading those floors;
Night Market submarine recommendations require its winter date and boarding hours.

The recommendations use **regular bait conditions**. Magic Bait does not expand the
list out of season; spawns requiring Magic Bait are excluded. Special bait conditions
provided by content mods use their registered game-state query handlers and may require
you to hold the equipped rod before those special suggestions appear.

Ridge Pond and Goddess Spring are explicitly fallback locations, including when upgrading
from an existing config. Custom fallback locations are preserved. Broad catch-all locations whose data
contains at least 60 species, at least 75% of the rod-fish catalog, and at least 80%
unrestricted species are also fallback locations. Normal fantasy habitats (like a
volcano or a region with its own exclusive fish) are ordinary fishing locations.
Add other exceptional map IDs to `FallbackLocations` in config.json if needed.

**Fallback is global:** while even one ordinary uncaught eligible fish exists, no
fallback fish appear. If only one or two normal fish qualify, the panel shows that
number rather than inventing a third. It shows a clear empty state if none qualify.
When all ordinary opportunities are exhausted, eligible uncaught fallback fish appear
with a "Magical locations" label. Scan failures prevent claiming fallback is clear.
Fish available in the current location appear first. Within each group, fish with
more time remaining appear first (20:00 before 19:00), then name. For local fish this
uses the local closing time; for remote fish it uses their longest available window.

## Unlocked areas and compatibility

By default, modded locations and gated vanilla interiors/dungeons must have been visited
by your farmer. Main vanilla outdoor maps don't require a previous visit; their built-in
access gates still apply. This conservative option avoids recommending story-locked maps
created at save load. An unlocked but unvisited extra area appears after your first visit.
Disable **Only visited extra areas** in GMCM to include unvisited areas (some may be locked).
This setting is visible as "Known areas" in the HUD footer. Ridge Pond additionally checks
the host's `LinkedFishesLetter` unlock flag. `LocationConditions` accepts additional
map-ID → game-state-query access rules; `ExcludedLocations` can hide specific maps.

Content mods can implement custom fishing code or custom access logic which isn't
represented in these data assets. Those overrides can't be inferred universally.
Automatic catch-all classification detects broad unrestricted data lists; custom code
that invents fish needs its map added to `FallbackLocations`. Random probability isn't
a promise of the next catch. Terrain checks don't prove that every shore tile is
reachable past transient obstacles, festival barriers, or a mod's custom entrance gate.

The scan is spread over update ticks. During refreshes the previous completed list
and label stay visible until the new results are ready, including when no fish qualify.
"Checking current conditions" appears only before the first completed scan after loading. Catches, time changes, warps, skill/equipment changes and content
invalidations trigger updates. No getFish simulation, world RNG consumption by this
mod, warps, save edits, network messages, or hidden game process are used.
Registered third-party condition handlers remain the responsibility of those mods.

## Requirements and installation

Requires **Stardew Valley 1.6.15+** and **SMAPI 4.3.1+**. Generic Mod Config Menu and
Lookup Anything are optional. The mod does not require any particular content expansion.

1. Obtain an installable `FishingPlanner-1.0.2.zip` by building the source below.
2. Extract its `FishingPlanner` folder into your game's `Mods` folder.
   The manifest should be at `Mods/FishingPlanner/manifest.json`.
3. Start or restart the game through SMAPI, then load your farm.

The generated ZIP is the same for macOS, Windows and Linux. No host companion is
required. Each player who wants the HUD installs it and sees their own catch history.
Personal placement and shortcuts can differ. For updates, preserve `config.json`.
See [INSTALL.txt](INSTALL.txt) for installation details.

## Build from source

Install Python 3, a .NET SDK capable of targeting .NET 6, and Stardew Valley with SMAPI.
The projects reference your installed game assemblies; those assemblies aren't included
in this repository or the generated package. No third-party NuGet packages are required.

```sh
git clone git@github.com:tabupl/stardew-fish-helper.git
cd stardew-fish-helper
python3 scripts/build.py
```

If the SDK or game isn't in its default location:

```sh
python3 scripts/build.py --dotnet /path/to/dotnet --game-path "/path/to/game/executable/directory"
```

The script runs the offline regression tests, compiles a Release build, and creates
`dist/FishingPlanner-1.0.2.zip`, an unpacked mod folder, and SHA-256 checksums. It never
launches the game. Build outputs, personal settings, and local diagnostic reports are
excluded from Git. The source-code ZIP from GitHub must be built before installation.

To run only the pure C# tests (no game installation required):

```sh
dotnet run --project tests/FishingPlanner.Tests.csproj -c Release
```

`src/FishingPlanner/` contains the mod and availability rules, `tests/` contains the
offline regressions, and `scripts/` contains the build and optional local installer.
The latest build passed 260 offline assertions. Earlier versions received user gameplay
feedback; automated in-game, Windows, and LAN tests have not been run. See the
[verification notes](verification/RESULTS.md) for the recorded scope and limitations.

Implementation references: installed Stardew Valley 1.6.15 assemblies and GameData XML;
[location data](https://stardewvalleywiki.com/Modding:Location_data),
[fish data](https://stardewvalleywiki.com/Modding:Fish_data), and the installed Ridgeside
Village 2.5.17 content data for Ridge Pond and its host unlock flag.
