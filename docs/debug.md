# FF3 Screen Reader - Debug Log

## Summary

Porting screen reader accessibility features to FF3 (Final Fantasy III Pixel Remaster).
Primary reference: `ff5-screen-reader` (FF5 shares more similarities with FF3 than FFVI_MOD).

**Current Phase:** Polish & Bug Fixes - All core features verified working

## CRITICAL: FF3 Harmony Patching Constraints

| Approach | FF4/FF5/FF6 | FF3 |
|----------|-------------|-----|
| `[HarmonyPatch]` attributes | Works | Crashes on startup |
| Manual Harmony patches | Works | Works (with caveats) |
| Methods with string params | Works | Crashes even with manual patches |

**Root Cause:** Methods with string parameters cause IL2CPP marshaling crashes in FF3.

**Solution:**
1. Use manual `HarmonyLib.Harmony` patching (not attributes)
2. Only patch methods WITHOUT string parameters
3. Read data from instance fields/properties in postfixes
4. Use `object __instance` and cast to IL2CPP type

**Safe:** `SetFocus(bool)`, `SetCursor(int)`, `SelectContent(...)`, `SetContent(List<BaseContent>)`
**Crashes:** `SetMessage(string)`, `SetSpeker(string)`

## CRITICAL: IL2CPP Reflection Rules

**NEVER use .NET reflection on IL2CPP types.** Always use:
```csharp
// WRONG - .NET reflection doesn't work
var prop = gotoMapEvent.GetType().GetProperty("Property");
var obj = prop.GetValue(gotoMapEvent);  // Always null!

// CORRECT - Direct IL2CPP access
var fieldEntity = gotoMapEvent.TryCast<FieldEntity>();
PropertyEntity property = fieldEntity.Property;  // Works!
```

## Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Last.Message` | Dialogue/message windows |
| `Last.UI.KeyInput` | Keyboard/gamepad UI controllers |
| `Last.UI.Touch` | Touch UI controllers |
| `Last.Entity` | Game data entities |
| `Last.Battle` | Battle system |
| `Last.Data.User` | Player/character data |
| `Last.Data.Master` | Master data (items, jobs, etc.) |
| `Serial.FF3.UI.KeyInput` | FF3-specific UI (jobs, abilities) |

## Architecture: GenericCursor + Specific Patches

### Default Behavior
- `CursorNavigation_Postfix` handles ALL cursor navigation
- `MenuTextDiscovery` reads menu text via multiple strategies
- Built-in readers: `SaveSlotReader`, `CharacterSelectionReader`, `ShopCommandReader`

### When to Add Specific Patches
Only when MenuTextDiscovery cannot get required data:
- Item/Equipment lists (needs description/stats)
- Shop items (needs price), Job (needs job level)
- Battle items, Shop quantity, Config settings

### Suppression Pattern (CRITICAL)
Each state class needs `ShouldSuppress()` that validates controller is active:

```csharp
public static bool ShouldSuppress()
{
    if (!IsActive) return false;  // Fast path
    try {
        var controller = FindObjectOfType<ControllerType>();
        if (controller == null || !controller.gameObject.activeInHierarchy) {
            IsActive = false;  // Auto-reset stuck flag
            return false;
        }
        return true;
    } catch { IsActive = false; return false; }
}
```

### State Machine Validation (BEST PRACTICE)
For menus with sub-menus, validate state machine instead of relying on flags alone:

```csharp
public static bool ShouldSuppress()
{
    if (!IsActive) return false;
    var windowController = FindObjectOfType<WindowController>();
    int currentState = GetCurrentState(windowController);

    if (currentState == STATE_COMMAND) { ClearState(); return false; }
    if (currentState == STATE_LIST) return true;

    ClearState();
    return false;
}
```

**Why State Machine > Event-Based:** IL2CPP postfixes sometimes don't fire. State machine validates actual game state at point of use.

## Key Types Reference

### Menus
| Menu | Controller | Key Method |
|------|------------|------------|
| Items | `ItemListController` | `SelectContent(...)` |
| Equipment | `EquipContentListController` | `SelectContent(...)` |
| Job | `JobChangeWindowController` | `UpdateJobInfo(...)` |
| Shop Items | `ShopListItemContentController` | `SetFocus(bool)` |
| Shop Quantity | `ShopTradeWindowController` | `UpdateCotroller(bool)` |
| Magic | `AbilityContentListController` | `SetCursor`, state machine |

### Battle
| Feature | Controller | Method |
|---------|------------|--------|
| Commands | `BattleCommandSelectController` | `SetCursor(int)` |
| Targets | `BattleTargetSelectController` | `OnChangeTarget` |
| Items | `BattleItemInfomationController` | `SelectContent(...)` |
| Magic | `BattleFrequencyAbilityInfomationController` | `SelectContent(...)` |
| Results | `BattleResultProvider` | `Genelate()` |

### Character Data
```csharp
OwnedCharacterData.Parameter.currentHP
OwnedCharacterData.Parameter.ConfirmedMaxHp()
OwnedCharacterData.Parameter.CurrentConditionList
OwnedCharacterData.OwnedJobDataList

// Row detection
UserDataManager.Instance().GetCorpsListClone()
corps.Id == CorpsId.Front  // Front=1, Back=2
```

### FF3 Spell System
FF3 uses spell charges per level (not MP):
```csharp
int spellLevel = ability.Ability.AbilityLv;  // 1-8
int current = param.CurrentMpCountList[spellLevel];
int max = param.ConfirmedMaxMpCount((AbilityLevelType)spellLevel);
```

## Memory Offsets

```
// State Machines
KeyInput.ItemWindowController.stateMachine: 0x70
KeyInput.EquipmentWindowController.stateMachine: 0x60
KeyInput.BattleCommandSelectController.stateMachine: 0x48
AbilityWindowController.stateMachine: 0x88
ShopController.stateMachine: 0x98
StateMachine<T>.current: 0x10
State<T>.Tag: 0x10

// Shop
ShopTradeWindowController.view: 0x30
ShopTradeWindowController.selectedCount: 0x3C
ShopTradeWindowView.totarlPriceText: 0x70

// Magic
AbilityContentListController.dataList: 0x38
AbilityContentListController.targetCharacterData: 0x98
```

## State Machine Values

```csharp
// ItemWindowController
STATE_COMMAND_SELECT = 1, STATE_USE_SELECT = 2, STATE_IMPORTANT_SELECT = 3
STATE_ORGANIZE_SELECT = 4, STATE_TARGET_SELECT = 5

// EquipmentWindowController
STATE_NONE = 0, STATE_COMMAND = 1, STATE_INFO = 2, STATE_SELECT = 3

// BattleCommandSelectController
STATE_NONE = 0, STATE_NORMAL = 1, STATE_EXTRA = 2, STATE_MANIPULATE = 3

// AbilityWindowController
STATE_COMMAND = 7
```

## Working Features

- [x] Menu cursor navigation (title, battle, main menu)
- [x] Dialogue text and speaker reading
- [x] Battle damage/result announcements
- [x] Field entity navigation (NPCs, chests, exits, events)
- [x] Pathfinding directions
- [x] Map name announcements
- [x] Item menu (list, commands, target selection, 'I' key job requirements)
- [x] Equipment menu (command bar, slot/item selection)
- [x] Job menu with job level
- [x] Config menu
- [x] Shop menu (commands, items with 'I' key stats, quantity)
- [x] Battle item menu
- [x] Magic menu spell list with charges
- [x] Character row status (Front/Back Row)
- [x] Save/Load slot reading

## Not Yet Implemented

- [ ] Confirmation popup announcements (crashes with string access)

## Known Game Code Typos

`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`

## Key Implementation Notes

### Equipment Job Requirements ('I' Key)
- Uses `UserDataManager.ReleasedJobs` for unlocked jobs only (no spoilers)
- Flow: `ItemListContentData.ItemType` → `Weapon/Armor.EquipJobGroupId` → `JobGroup.Job{N}Accept` → `Job.MesIdName`

### Shop Content System
- `ContentId` is NOT master data ID - it's a content system ID
- `Content.TypeId` = ContentType (1=Item, 2=Weapon, 3=Armor)
- `Content.TypeValue` = actual ID for master data lookup

### Battle State Suppression
Battle menu flags are cleared centrally via `BattleResultPatches.ClearAllBattleMenuFlags()` when victory screen appears.

State classes use state machine validation to detect return to command menu:
- `BattleItemMenuState.ShouldSuppress()` checks `BattleCommandSelectController` state
- `BattleMagicMenuState.ShouldSuppress()` checks `BattleCommandSelectController` state
- If state is `STATE_NORMAL` or `STATE_EXTRA`, we're back at command menu - reset flag

### NPC Detection
Use `TryCast<FieldNonPlayer>()` - NPCs don't contain "Chara" in type name.

---

## Version History

### 2026-01-09 (Part 11)
- **Fix: EquipMenuState suppression when backing to main menu** - Was suppressing after leaving equip menu
  - Root cause: `ShouldSuppress()` found controller but didn't check `activeInHierarchy`
  - Controller exists in scene but isn't visible at main menu
  - Fix: Added `activeInHierarchy` check before state machine validation
  - File: `Patches/EquipMenuPatches.cs`
- **Fix: Attack count (simplified)** - Previous weapon ID check wasn't working
  - "Empty" pseudo-weapon has valid ID > 0, so weapon ID check failed
  - Fix: Removed weapon ID check, now only checks item name for "Empty"
  - File: `Menus/StatusDetailsReader.cs`

### 2026-01-09 (Part 10)
- **Debug: Attack count logging** - Added debug logs to identify equipment slot contents
  - Revealed: `Slot 2: Empty, Weapon=True` - "Empty" item has Weapon property set
  - File: `Menus/StatusDetailsReader.cs`
- **Cleanup: Removed suppression debug logging** - Bug was fixed in Part 9
  - File: `Core/FFIII_ScreenReaderMod.cs`

### 2026-01-09 (Part 9)
- **Fix: StatusMenuState suppression at game load** - Main menu not read after game load
  - Root cause: `SelectContent_Postfix` set `IsActive = true` BEFORE validation checks
  - During game load, `SelectContent` fires but menu isn't open → IsActive set, then early return
  - Fix: Moved `StatusMenuState.IsActive = true` to AFTER all validation passes
  - File: `Patches/StatusMenuPatches.cs` line 352

### 2026-01-09 (Part 7)
- **Fix: StatusMenuState suppression flag stuck** - Main menu cursor was being suppressed after leaving Status
  - Root cause: `IsActive` set when entering character selection, only cleared when exiting details view
  - If user backs out before entering details, flag stayed true forever
  - Added state machine validation to `ShouldSuppress()`:
    - Finds `StatusWindowController` and checks `activeInHierarchy`
    - Reads state machine at offset 0x20, current state at 0x10, tag at 0x10
    - If state is None (0) or controller gone, auto-resets `IsActive`
  - File: `Patches/StatusMenuPatches.cs`
- **Fix: Attack count calculation (again)** - Simplified based on user research
  - Monk (JobId=4) and BlackBelt (JobId=10): Always 2 attacks (bare-hand counts even with weapon)
  - Other jobs: 2 attacks only if 2 weapons equipped, otherwise 1 attack
  - Weapon + shield = 1 attack, weapon + empty hand = 1 attack
  - File: `Menus/StatusDetailsReader.cs`

### 2026-01-09 (Part 6)
- **Fix: Evasion stat reading 0** - Changed from wrong method to correct one
  - Previous: `ConfirmedDefenseCount()` (number of defense attempts, not evasion rate)
  - Now: `ConfirmedEvasionRate(false)` (actual evasion percentage)
- **Fix: Magic Evasion stat reading 0** - Changed from wrong method to correct one
  - Previous: `ConfirmedMagicDefenseCount()` (wrong stat)
  - Now: `ConfirmedAbilityEvasionRate(false)` (actual magic evasion percentage)
- File: `Menus/StatusDetailsReader.cs`

### 2026-01-09 (Part 5)
- **Fix: Character selection double-reading** - Status menu character selection was being read twice
  - `StatusMenuPatches.SelectContent_Postfix` spoke the character
  - `MenuTextDiscovery.WaitAndReadCursor` also spoke (no suppression in place)
  - Added `StatusMenuState.ShouldSuppress()` method
  - Added suppression check in `CursorNavigation_Postfix` at line ~893
  - Files: `Patches/StatusMenuPatches.cs`, `Core/FFIII_ScreenReaderMod.cs`
- **Fix: Attack count calculation** - Now uses `HasTwoSwordStyle` instead of `ConfirmedAccuracyCount()`
  - `ConfirmedAccuracyCount()` is accuracy/hit chance, NOT attack count
  - `HasTwoSwordStyle` property indicates dual-wielding (2 attacks) vs single weapon (1 attack)
  - TODO: Investigate if Monks get bonus attacks when fighting bare-handed
  - File: `Menus/StatusDetailsReader.cs`

### 2026-01-09 (Part 4)
- **Change: MP as navigable section** - Each spell level is now a separate navigable stat
  - 8 entries: LV1, LV2, LV3, LV4, LV5, LV6, LV7, LV8
  - Format: "LV1: 0", "LV2: 3", etc. (current charges only, matching visual)
  - All characters show all 8 levels regardless of magic capability
  - 26 total stats in 4 groups now
- **Fix: Intellect stat** - Changed from `ConfirmedMagic()` to `ConfirmedIntelligence()`
- **Fix: Attack count** - Changed to `1 + ConfirmedAccuracyCount()` (base 1 + bonus attacks)
  - `ConfirmedAdditionalAttack()` only exists on EnemyCharacterParameter
- **Group indices updated:** CharacterInfo=0, Vitals=5, Attributes=15, CombatStats=20

### 2026-01-09 (Part 3)
- **New: MP stat in Status Details** - Added spell charge display to status screen navigation
  - Shows all 8 spell levels with current charges only (matching visual display)
- **Change: Status navigation hotkeys** - Changed first/last stat navigation to match FF5
  - Ctrl+Up: Jump to first stat (was Home)
  - Ctrl+Down: Jump to last stat (was End)
  - Home/End bindings removed
- **Files modified:** `Menus/StatusDetailsReader.cs`, `Core/InputManager.cs`

### 2026-01-09 (Part 2)
- **New: MoveStateHelper** - Vehicle/movement state tracking and announcements
  - Tracks player movement state (Walk, Ship, Chocobo, Airship, etc.)
  - Announces state changes ("On ship", "On foot", etc.)
  - Press V key to announce current movement mode
  - File: `Utils/MoveStateHelper.cs`
- **New: MovementSpeechPatches** - Patches `FieldPlayer.ChangeMoveState` for vehicle announcements
  - Manual Harmony patching (FF3 requirement)
  - Includes `MoveStateMonitor` coroutine for proactive state monitoring
  - File: `Patches/MovementSpeechPatches.cs`
- **New: VehicleLandingPatches** - "Can land" announcements for airship/ship
  - Patches `MapUIManager.SwitchLandable(bool)`
  - Announces when entering landable terrain (false → true transition)
  - Only announces when in vehicle (not on foot)
  - File: `Patches/VehicleLandingPatches.cs`
- **New: StatusDetailsReader** - Full status screen stat navigation
  - 18 stats organized in 4 groups: Character Info, Vitals, Attributes, Combat Stats
  - Arrow key navigation (Up/Down), group jumping (Shift+Up/Down)
  - Ctrl+Up/Down for top/bottom, R to repeat current stat
  - Patches `StatusDetailsController.InitDisplay`/`ExitDisplay` via manual Harmony
  - File: `Menus/StatusDetailsReader.cs`, `Patches/StatusMenuPatches.cs`
- **Key types used:**
  - `FieldPlayer.moveState` - movement state enum
  - `FieldPlayer.ChangeMoveState()` - patched for announcements
  - `MapUIManager.SwitchLandable()` - patched for landing detection
  - `StatusDetailsController.InitDisplay/ExitDisplay` - patched for stat navigation
  - `OwnedJobData.Id` (not JobId) - for job exp lookup
  - `UserDataManager.GetMemberData(int index)` - for character data fallback

### 2026-01-09
- **Fix: Pathfinding focus jumping** - Entity focus no longer jumps to closer destinations when player moves
  - **Root cause**: `ApplyFilter()` re-sorted entities by distance every 5 seconds, `currentIndex` pointed to different entity
  - **Solution**: Track selected entity by identifier (position + category + name) instead of just index
  - After re-sorting, `FindEntityByIdentifier()` locates the previously selected entity and restores `currentIndex`
  - New methods: `SaveSelectedEntityIdentifier()`, `ClearSelectedEntityIdentifier()`, `FindEntityByIdentifier()`
  - Affects `EntityScanner.cs` - `NextEntity()`, `PreviousEntity()`, `ApplyFilter()`, `CurrentIndex` setter
- **Fix: Teleportation "Not on field map"** - Ctrl+arrow teleportation now works correctly
  - **Root cause**: `GetFieldPlayer()` used .NET reflection which doesn't work on IL2CPP types (always returns null)
  - **Solution**: Use `FieldPlayerController.fieldPlayer` directly (same pattern as pathfinding and `GetPlayerPosition()`)
  - Affects `FFIII_ScreenReaderMod.cs` - `GetFieldPlayer()` method
- **Fix: Teleportation now teleports to selected entity** - Ctrl+arrow teleports player relative to selected entity
  - **Previous behavior**: Moved player by 16 units in arrow direction from current position
  - **New behavior**: Teleports player to arrow direction side of the currently selected entity
  - Ctrl+Up = north of entity, Ctrl+Down = south, Ctrl+Left = west, Ctrl+Right = east
  - Announces "Teleported to [direction] of [entity name]"
  - Says "No entity selected" if no entity is focused
  - Affects `FFIII_ScreenReaderMod.cs` - `TeleportInDirection()` method
- **Fix: Magic menu spell navigation** - Both battle and menu magic now correctly announce focused spell
  - **Root cause**: Previous code iterated looking for `FocusCursorParent.activeInHierarchy` which always found first spell
  - **Battle Magic**: Now uses `contents` list parameter directly (passed to SelectContent method), uses `index` to get correct spell
  - **Menu Magic (Use/Remove)**: Now uses `targetCursor.Index` to get correct spell from `contentList` (offset 0x60)
  - Empty slots now announce "Empty" properly
- **New: Learn menu support** - Magic menu Learn mode now announces spell tomes
  - Detects Learn mode via `IsItemListCheck` flag (offset 0x29)
  - Reads from `abilityItemList` (offset 0x40) containing `OwnedItemData` spell tomes
  - Format: "Spell Tome Name: Description" (matches item menu pattern)
  - Uses `OwnedItemData.Name` and `OwnedItemData.Deiscription` (game typo) properties
- **Fix: Battle menu double-reading** - Battle commands no longer read by both BattleCommandPatches and MenuTextDiscovery
  - Simplified `BattleCommandState.ShouldSuppress()` to just return `IsActive` flag
  - Flag cleared centrally via `BattleResultPatches.ClearAllBattleMenuFlags()` at victory screen
- **Fix: Battle submenu flags not resetting** - Item/Magic menu flags now properly reset when returning to command menu
  - Added state machine validation to `BattleItemMenuState.ShouldSuppress()`
  - Checks `BattleCommandSelectController` state to detect return to command selection
- **New: Battle Magic menu support** - Added `BattleMagicPatches.cs` with `BattleMagicMenuState`
  - Patches `BattleFrequencyAbilityInfomationController.SelectContent`
  - Announces spell name, charges, and description
  - Same state machine validation pattern as BattleItemMenuState

### 2026-01-08
- **Fix: Dialogue speaker order** - Speaker name now announced before dialogue text
  - Previously: `SetContent_Postfix` spoke dialogue immediately, then `Play_Postfix` spoke speaker
  - Now: `SetContent_Postfix` stores text in `pendingDialogueText`, `Play_Postfix` speaks speaker first then pending dialogue
  - Affects `ManualPatches` class in `FFIII_ScreenReaderMod.cs`

---

## Build Command

```cmd
cd /d D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader
build_and_deploy.bat
```

Check `build_log.txt` for errors if build fails.
