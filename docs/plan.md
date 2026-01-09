# FF3 Screen Reader Porting Plan

## Overview

Port screen reader accessibility features to FF3 (Final Fantasy III Pixel Remaster). Provides blind/low-vision players with TTS announcements for menus, navigation, dialogue, and battle.

**Primary Reference:** Use `ff5-screen-reader` as the primary source for porting code. FF5 shares more similarities with FF3 than FFVI_MOD.

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
| 5 | Field Navigation | DONE |
| 6 | Input System | DONE |
| 7.1 | Shop System | DONE |
| 7.2 | Vehicles | DONE |
| 7.3 | Status Details Navigation | DONE |

---

## CRITICAL: FF3 Harmony Patching Constraints

See `debug.md` for full details.

| Approach | Result |
|----------|--------|
| `[HarmonyPatch]` attributes | Crashes on startup |
| Manual Harmony patches | Works |
| Methods with string params | Crashes even with manual patches |

**Solution:**
1. Use manual `HarmonyLib.Harmony` patching (not attributes)
2. Only patch methods WITHOUT string parameters
3. Read data from instance fields/properties in postfixes
4. Use `object __instance` and cast to IL2CPP type

---

## FF3-Specific Features

### Job Menu
- Patched `UpdateJobInfo(OwnedCharacterData, Job)`
- Announces "Job Name, Job Level X"
- Job level from `OwnedCharacterData.OwnedJobDataList`

### Magic Menu (Spell Charges)
FF3 uses per-level charges shared across magic types:
```
Level 1: 8/10 charges (shared by Cure, Fire, Poisona, etc.)
Level 2: 5/8 charges (shared by Cura, Fira, etc.)
...
```

**Implementation:** See `debug.md` - Magic Menu Implementation section
- Uses state machine validation in `ShouldSuppress()`
- Reads `AbilityWindowController.stateMachine.Current` to detect Command state

---

## Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Last.Message` | Dialogue/message windows |
| `Last.UI.KeyInput` | Keyboard/gamepad UI controllers |
| `Last.Data.User` | Player/character data |
| `Last.Data.Master` | Master data (items, jobs, etc.) |
| `Serial.FF3.UI.KeyInput` | FF3-specific UI (jobs, abilities) |

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
| I | Announce details (config tooltip, shop stats, equipment job requirements) |
| V | Announce current vehicle/movement mode |
| Up/Down | Navigate stats (in status details screen) |
| Shift+Up/Down | Jump to next/previous stat group |
| Ctrl+Up/Down | Jump to first/last stat |
| R | Repeat current stat (in status details screen) |

---

## Key Technical Notes

### IL2CPP
- Use `TryCast<T>()` for type conversions
- Never use .NET reflection on IL2CPP types
- Read fields via pointer offsets when properties don't work

### Suppression Pattern
Each menu state class needs `ShouldSuppress()` that validates controller is `activeInHierarchy`:
```csharp
public static bool ShouldSuppress()
{
    if (!IsActive) return false;
    var controller = FindObjectOfType<ControllerType>();
    if (controller == null || !controller.gameObject.activeInHierarchy) {
        IsActive = false;
        return false;
    }
    return true;
}
```

### Localization
- Always use `MessageManager.GetMessage()` for localized strings
- Use `MesIdName` identifiers from master data

---

## Not Yet Implemented

- Confirmation popup announcements (crashes with string access)
- Vehicle/Airship navigation

---

## Dependencies

- MelonLoader (net6.0)
- Tolk.dll + screen reader (NVDA recommended)
- FF3 game assemblies from: `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR`

---

## Build

```cmd
cd /d D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader
build_and_deploy.bat
```

See `debug.md` for detailed implementation notes and version history.
