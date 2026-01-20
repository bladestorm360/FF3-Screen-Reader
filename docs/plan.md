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

---

## FF3-Specific Features

- **Job Menu**: Announces job name, job level, and "Equipped" indicator for current job
- **Magic Menu**: Per-level MP (8 levels shared across magic types), format: "Spell: MP: X/Y. Description"
- **Status Details**: Job Level stat via `BattleUtility.GetJobLevel()`
- **Vehicle Transitions**: Patches `GetOn()`/`GetOff()` for boarding announcements
- **Landing Detection**: Patches `ShowLandingGuide(bool)` for "Can land" announcements
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

### Battle Pause Menu Reading (RESOLVED 2026-01-20)

**Problem:** Battle pause menu (spacebar during battle) with Resume/Controls/Return to Title options was not being read. `BattleCommandState` suppression was blocking cursor navigation.

**Solution:**
- `BattlePauseState.IsActive` reads game memory directly (no hookable method fires reliably)
- Reads `BattleUIManager.Instance` → offset 0x90 (`pauseController`) → offset 0x71 (`isActivePauseMenu`)
- `CursorSuppressionCheck` bypasses all battle suppression when `BattlePauseState.IsActive` is true
- `MenuTextDiscovery` handles reading via generic cursor navigation

**Note:** May revisit later to find a hookable method instead of direct memory reading. Multiple methods were tried (`SetEnablePauseMenu`, `SetEnablePauseRoot`, `UpdateSelect`, `UpdateFocus`) but none fired at runtime.

**Files:** `Patches/BattlePausePatches.cs`, `Core/CursorSuppressionCheck.cs`

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
