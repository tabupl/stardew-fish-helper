# Version 1.0.2 — 7 October 2026

- 260 offline assertions passed; release build succeeded with zero warnings/errors.
- Regression coverage now includes a warp while the previous scan remains visible:
  the fish and its preferred location reprioritize immediately. HUD presentation also
  resolves the live location on each draw, including a map's base/unique name.
- Location counts and tooltip lists deduplicate spawn entries by map. Current map
  comes first and is marked "here"; other eligible locations are all listed.
- Removed example water-tile coordinates from the generated fishing details. Required
  player/bobber rectangles remain available where they affect catching the fish.
- The scan includes the actual current location instance first, even if it is absent
  from the client's top-level location list. No game instance is created to do this.
- ZIP integrity and installation hashes checked; personal config preserved. Package
  files are replaced atomically for the next restart.

No in-game test was performed. The user reported 1.0.1 works better; actual 1.0.2
rendering/gameplay and Windows/LAN checks remain for the user after restarting.

---

# Version 1.0.1 — 7 October 2026

The user confirmed version 1.0.0 works in their game. Version 1.0.1 was checked offline
only; no game process was launched, controlled, restarted, or used for testing.

- Reproduced the old clear-on-refresh behavior and ordering in a deterministic test of
  the same snapshot code now used by FishingService. The new tests failed before the fix.
- All 252 offline assertions now pass. New regressions cover current-location priority,
  descending closing times, local versus remote time windows, keeping the exact previous
  list through refresh/restart, retaining fallback and empty-state labels, replacing the
  list on publication, clearing it on save changes, and upgrading old fallback configs.
- The HUD's loading label is limited to the first scan. Subsequent scans retain the
  completed list and its label until the replacement is published.
- Confirmed Goddess Spring's map ID from the installed Mineral Town translation:
  Rexstrife20.MineralTownCP_GoddessHill. Default and existing configurations gain it
  alongside Ridge Pond, while custom fallback entries and other settings are preserved.
- Release build: zero warnings/errors, with warnings treated as errors. ZIP integrity
  checked and file hashes generated. The offline test project uses the dotnet runtime
  directly because macOS killed the regenerated native test app host (exit 137); the
  same test DLL passed directly, then the full build passed with UseAppHost=false.
- The installer atomically replaces package files instead of truncating the existing
  DLL, so an already-running process can retain its original assembly until restart.

Actual 1.0.1 rendering, in-game behavior and Windows/LAN verification remain for the user.

---

# Offline verification — 7 October 2026

No game was launched, controlled, restarted, or used as a test process. No save was loaded
or modified. Adam is playing and will do the first runtime check after restarting.

- 239 independent-core assertions passed: time-window endpoints and split windows,
  rain/dry weather, minimum fishing level, explicit requirement bypass, training-rod
  exceptions, tutorial catches, crab-pot/malformed-data exclusion, normalized modded
  catch IDs, strict normal-before-magical fallback (including one/two normal fish),
  no-result handling, catch-all classification, and destination query rewriting.
- Release build passed against the installed Stardew Valley 1.6.15 and SMAPI assemblies,
  with warnings treated as errors: zero warnings and zero errors.
- The ZIP contains only the portable mod DLL, manifest, README, install instructions,
  and license. It includes no game DLLs, saves, source tests, configuration or SDK files.
  ZIP CRC and SHA-256 file hashes are checked by scripts/build.py.
- Read the installed game methods for ordinary fishing, mine catches, farm overrides,
  location queries and perfection membership. Availability checks don't call getFish.
- Read live Ridgeside 2.5.17 content: Ridge Pond map ID and ten fish entries; its map
  entrance is gated by host mail flag LinkedFishesLetter. The fallback list explicitly
  names this map, independently of automatic catch-all detection.
- Audited the existing location-data export (historical, not regenerated during play):
  642 location definitions, 391 distinct spawn item IDs, and 17 LOCATION_FISH queries.
  No other dynamic item-query types appeared in that export. The local pack-audit.json
  report is excluded from the source repository.
- Confirmed the suggested toggle doesn't exactly duplicate a live config binding.
  GMCM can rebind it if another mod implements an undocumented shortcut.

Remaining verification: actual HUD layout/rendering and performance, real patched game
state after loading, custom third-party fishing overrides, Lookup Anything/GMCM behavior,
Windows, and LAN host/guest sessions. Compilation/offline tests don't prove those passed.
Conservative visited-area filtering deliberately omits unlocked but unvisited extra maps;
this can be disabled in GMCM. The README documents the scope of data-driven compatibility.

Suggested first user check: after restarting, confirm the HUD shows three uncaught fish
when available; click one to see its location/time restrictions; catch it and check that
it disappears; check during a time/weather transition. Each spouse should see their own
catch ledger. No automatic in-game verification commands are run by this package.
