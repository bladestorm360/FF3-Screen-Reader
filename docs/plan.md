# FF3 Screen Reader - Project Plan

Screen reader accessibility mod for Final Fantasy III Pixel Remaster. TTS announcements for menus, navigation, dialogue, and battle.

**Reference:** `ff5-screen-reader` (similar architecture)

---

## Status

| Component | Status |
|-----------|--------|
| Core & Dialogue | DONE |
| Title/Config/Status Menu | DONE |
| Item/Magic/Job Menu | DONE |
| Save/Load & New Game Naming | DONE |
| Battle System & Pause Menu | DONE |
| Field Navigation & Entity Scanner | DONE |
| Shops & Vehicles | DONE |
| Popups & Map Transitions | DONE |
| NPC Event Item Selection | DONE |
| External Sound Player | DONE |
| Entity Translation | DONE |

---

## FF3-Specific Features

- **Dialogue:** Per-page via `PlayingInit`, multi-line pages, speaker tracking
- **Story Text:** Per-line via `LineFadeMessageWindowController`
- **Job Menu:** Job name, level, "Equipped" indicator
- **Magic:** 8-level MP system: "Spell: MP: X/Y. Description"
- **Vehicles:** `GetOn()`/`GetOff()` patches, landing via `ShowLandingGuide(bool)`
- **Entity Scanner:** VehicleTypeMap from `Transportation.ModelList`, event-driven
- **Entity Translation:** JSON Japanese→English dictionary with prefix stripping, untranslated dump
- **Sound Player:** Windows waveOut API, 4 channels (Movement, WallBump, WallTone, Beacon)
- **Wall Tones:** Looping directional tones, suppressed at exits/doors and map transitions
- **Footsteps:** Click on tile change via coroutine-based wall bump detection
- **Audio Beacons:** Periodic pings with distance-based volume/panning

---

## Hotkeys

| Key | Action |
|-----|--------|
| J / [ | Previous entity |
| L / ] | Next entity |
| K | Repeat current entity |
| P / \ | Pathfind to entity |
| Shift+J/L | Cycle category |
| M | Map name |
| G | Gil |
| I | Details (tooltips, stats, job requirements) |
| V | Vehicle/movement mode |
| ; | Toggle wall tones |
| ' | Toggle footsteps |
| 0 | Dump untranslated entity names |
| 9 | Toggle audio beacons |
| Up/Down | Navigate stats (status screen) |
| Shift+Up/Down | Jump stat group |
| Ctrl+Up/Down | First/last stat |
| R | Repeat stat |
| F1 | Walk/Run toggle announcement |
| F3 | Encounters on/off announcement |
| F5 | Enemy HP display (Numbers/Percentage/Hidden) |
| ` | Cycle waypoints |
| Shift+` | Previous waypoint |
| Ctrl+` | Navigate to waypoint |
| Shift+/ | Add waypoint (blank name field) |
| Ctrl+. | Rename waypoint |
| Ctrl+/ | Delete waypoint |

---

## Architecture

Post-refactoring file organization (~70 C# files):

| Directory | Purpose | Key Files |
|-----------|---------|-----------|
| `Core/` | Main mod, input, audio, preferences, waypoints | `FFIII_ScreenReaderMod.cs`, `InputManager.cs`, `AudioLoopManager.cs`, `PreferencesManager.cs`, `WaypointController.cs`, `GameInfoAnnouncer.cs` |
| `Core/Filters/` | Entity filter interface and implementations | `IEntityFilter.cs`, `PathfindingFilter.cs`, `ToLayerFilter.cs`, `CategoryFilter.cs` |
| `Field/` | Entity scanning, navigation, pathfinding | `EntityScanner.cs`, `NavigableEntity.cs`, `WaypointEntity.cs`, `FieldNavigationHelper.cs`, `FilterContext.cs` |
| `Menus/` | Menu text readers | `ConfigMenuReader.cs`, `ShopCommandReader.cs`, `StatusDetailsReader.cs`, `SaveSlotReader.cs` |
| `Patches/` | All Harmony patches by game system | `BattleCommandPatches.cs`, `PopupPatches.cs`, `MessageWindowPatches.cs`, `MapTransitionPatches.cs`, etc. |
| `Utils/` | Shared utilities | `IL2CppOffsets.cs`, `MenuStateRegistry.cs`, `MenuStateHelper.cs`, `GameObjectCache.cs`, `SoundPlayer.cs`, `AnnouncementDeduplicator.cs` |

---

## Known Issues

| Issue | Description |
|-------|-------------|
| Title Screen Timing | "Press any button" speaks ~1 second before input available |

---

## Dependencies

- MelonLoader (net6.0)
- Tolk.dll + screen reader (NVDA recommended)
- FF3 assemblies: `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR`

---

## References

| Document | Contents |
|----------|----------|
| `debug.md` | Technical implementation, memory offsets, resolved issues |
| `dump.cs` | Game class/method signatures |
