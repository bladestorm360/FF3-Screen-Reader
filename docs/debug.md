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
`BattleCommandState.ShouldSuppress()` checks for `BattleMenuWindowController` - if gone, battle has ended.

### NPC Detection
Use `TryCast<FieldNonPlayer>()` - NPCs don't contain "Chara" in type name.

---

## Build Command

```cmd
cd /d D:\Games\Dev\Unity\FFPR\ff3\ff3-screen-reader
build_and_deploy.bat
```

Check `build_log.txt` for errors if build fails.
