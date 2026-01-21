# FF3 Screen Reader - Project Plan

Screen reader accessibility mod for Final Fantasy III Pixel Remaster. Provides TTS announcements for menus, navigation, dialogue, and battle for blind/low-vision players.

**Primary Reference:** `ff5-screen-reader` (FF5 shares more similarities with FF3 than FFVI_MOD)

---

## Implementation Status

| Phase | Component | Status |
|-------|-----------|--------|
| 1 | Project Setup & Core | DONE |
| 2 | Dialogue/Messages | DONE |
| 3.1 | Title Menu | DONE |
| 3.2 | Config Menu | DONE |
| 3.3 | Status Menu | DONE |
| 3.4 | Item Menu | DONE |
| 3.4b | Magic Menu | DONE |
| 3.5 | Job Menu (FF3-specific) | DONE |
| 3.6 | Save/Load Menu | DONE |
| 3.8 | New Game Naming | DONE |
| 4 | Battle System | DONE |
| 4.1 | Battle Pause Menu | DONE |
| 5 | Field Navigation | DONE |
| 6 | Input System | DONE |
| 7.1 | Shop System | DONE |
| 7.2 | Vehicles | DONE |
| 7.3 | Status Details Navigation | DONE |
| 7.4 | Confirmation Popups | DONE |
| 7.5 | Map Transition Announcements | DONE |
| 7.6 | Entity Scanner Map Guard | DONE |
| 7.7 | Vehicle Category Tracking | DONE |

---

## FF3-Specific Features

- **Job Menu**: Announces job name, job level, and "Equipped" indicator for current job
- **Magic Menu**: Per-level MP (8 levels shared across magic types), format: "Spell: MP: X/Y. Description"
- **Status Details**: Job Level stat via `BattleUtility.GetJobLevel()`
- **Vehicle Transitions**: Patches `GetOn()`/`GetOff()` for boarding announcements
- **Landing Detection**: Patches `ShowLandingGuide(bool)` for "Can land" announcements
- **Vehicle Category**: Vehicles tracked via `VehicleTypeMap` from `Transportation.ModelList`, filtered from ResidentChara
- **Confirmation Popups**: Patches base `Popup.Open()` with IL2CPP `TryCast<T>()` for type detection
- **Map Transitions**: Automatic "Entering {mapName}" announcements with deduplication via `LocationMessageTracker`

---

## Hotkeys

| Key | Action |
|-----|--------|
| J / [ | Previous entity |
| L / ] | Next entity |
| K | Repeat current entity |
| P / \ | Pathfind to entity |
| Shift+J/L | Cycle category |
| M | Announce map name |
| G | Announce gil |
| I | Announce details (tooltips, stats, job requirements) |
| V | Announce current vehicle/movement mode |
| Up/Down | Navigate stats (status details screen) |
| Shift+Up/Down | Jump to next/previous stat group |
| Ctrl+Up/Down | Jump to first/last stat |
| R | Repeat current stat |

---

## Known Issues

| Issue | Description |
|-------|-------------|
| Title Screen Timing | "Press any button" speaks ~1 second before input is available |

---

## Resolved Issues

### NPC Dialogue Deduplication Fix (RESOLVED 2026-01-20)

**Problem:** When talking to the same NPC twice with identical dialogue, the second interaction was silent.

**Solution:** Patch `MessageWindowManager.Close()` to clear `lastDialogueMessage` and `pendingDialogueText` when dialogue window closes. See `debug.md` for technical details.

### Entity Scanner Event-Driven Refresh (RESOLVED 2026-01-20)

**Problem:** Treasure chests showed as unopened in entity scanner until 5-second periodic rescan.

**Solution:** Replaced timer-based scanning with event-driven hooks:
- `FieldTresureBox.Open()` → triggers refresh when chest opened
- `MessageWindowManager.Close()` → triggers refresh when dialogue ends (NPC interactions)

Both hooks use 1-frame delayed coroutine to ensure game state updates before rescan. See `debug.md` for technical details.

### Battle Pause Menu Reading (RESOLVED 2026-01-21)

**Problem:** Battle pause menu (spacebar during battle) with Resume/Controls/Return to Title options was not being read. `BattleCommandState` suppression was blocking cursor navigation.

**Solution:**
- Detect pause menu by cursor path containing `curosr_parent` (game typo)
- Special case in `CursorNavigation_Postfix` handles pause menu BEFORE suppression check
- `MenuTextDiscovery` reads the menu items via generic text discovery strategies

**Files:** `Core/FFIII_ScreenReaderMod.cs` (CursorNavigation_Postfix)

### Entity Scanner World Map Refresh (RESOLVED 2026-01-21)

**Problem:** Entity scanner showed stale entities from previous map after transitioning to world map (e.g., dungeon exits still shown after leaving dungeon).

**Solution:** Call `entityScanner.ForceRescan()` in `CheckMapTransition()` when map ID changes. This clears the entity cache and performs a fresh scan with the new map's entities.

### "Not on Map" Guard (RESOLVED 2026-01-21)

**Problem:** Entity navigation could be attempted from title screen, menus, or loading screens.

**Solution:** Added `EnsureFieldContext()` guard (ported from FF4) that checks `FieldMap.activeInHierarchy` and `FieldPlayerController.fieldPlayer`. Returns "Not on map" if not on a valid field. Applied to all entity navigation methods.

### Vehicle Category Tracking (RESOLVED 2026-01-21)

**Problem:** Vehicles not tracked in entity scanner. Were either not detected, detected as NPCs, or announced as "ResidentCharaEntity".

**Solution:** Ported FF2's `VehicleTypeMap` system:
- `FieldNavigationHelper.VehicleTypeMap` populated from `Transportation.ModelList` via pointer offsets
- `ConvertToNavigableEntity()` checks VehicleTypeMap FIRST, before other type detection
- ResidentChara entities filtered AFTER vehicle detection
- `VehicleEntity.GetVehicleName()` fixed to use correct TransportationType enum values

**Files:** `Field/FieldNavigationHelper.cs`, `Field/EntityScanner.cs`, `Field/NavigableEntity.cs`, `Core/FFIII_ScreenReaderMod.cs`

---

## Dependencies

- MelonLoader (net6.0)
- Tolk.dll + screen reader (NVDA recommended)
- FF3 game assemblies: `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR`

---

## Build

```cmd
cd /d D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader
build_and_deploy.bat
```

See `debug.md` for architecture and implementation details.
