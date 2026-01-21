# FF3 Screen Reader - Technical Reference

## IL2CPP Constraints

| Approach | Result |
|----------|--------|
| `[HarmonyPatch]` attributes | Crashes |
| Manual Harmony patches | Works |
| Methods with string/enum params | Crashes |

```csharp
// WRONG - .NET reflection
var value = obj.GetType().GetProperty("Prop").GetValue(obj);  // Always null!

// CORRECT - IL2CPP
var value = obj.TryCast<TargetType>().Property;
```

- Use `AccessTools.Method()` for patching (not `Type.GetMethod()`)
- Read fields via pointer offsets when properties fail

---

## Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Last.Message` | Dialogue/message windows |
| `Last.UI.KeyInput` | Keyboard/gamepad UI |
| `Last.Data.User` | Player/character data |
| `Last.Data.Master` | Master data (items, jobs) |
| `Last.Battle` | Battle utilities |
| `Serial.FF3.UI.KeyInput` | FF3-specific UI (jobs, abilities) |

---

## Utility Classes

| Class | Purpose |
|-------|---------|
| `AnnouncementDeduplicator` | Exact-match dedup: `ShouldAnnounce(context, text)` |
| `LocationMessageTracker` | Map transition dedup (containment check) |
| `TextUtils` | Strip icon markup, formatting |
| `MoveStateHelper` | Vehicle/movement state |
| `GameObjectCache` | Component caching |
| `CoroutineManager` | Frame-delayed operations |

---

## Memory Offsets

### State Machines
```
ItemWindowController.stateMachine: 0x70
EquipmentWindowController.stateMachine: 0x60
BattleCommandSelectController.stateMachine: 0x48
AbilityWindowController.stateMachine: 0x88
ShopController.stateMachine: 0x98
StateMachine<T>.current: 0x10
State<T>.Tag: 0x10
```

### Shop
```
ShopTradeWindowController.view: 0x30
ShopTradeWindowController.selectedCount: 0x3C
ShopTradeWindowView.totarlPriceText: 0x70
```

### Magic
```
AbilityContentListController.dataList: 0x38
AbilityContentListController.targetCharacterData: 0x98
```

### Popups
| Type | commandList Offset |
|------|-------------------|
| CommonPopup | 0x70 |
| JobChangePopup | 0x50 |
| ChangeMagicStonePopup | 0x58 |
| GameOverSelectPopup | 0x40 |
| SavePopup (message) | 0x40 |
| SavePopup (commandList) | 0x60 |

### Save/Load
| Controller | Offset |
|------------|--------|
| LoadGameWindowController.savePopup | 0x58 |
| LoadWindowController.savePopup | 0x28 |
| SaveWindowController.savePopup | 0x28 |
| SaveWindowController.commonPopup | 0x38 |

### Battle Pause
| Field | Offset |
|-------|--------|
| BattleUIManager.pauseController | 0x90 |
| BattlePauseController.isActivePauseMenu | 0x71 |
| BattlePauseController.selectCommandCursor | 0x40 |
| BattlePauseController.commandMessageIdList | 0x30 |

### Battle Popup
| Field | Offset |
|-------|--------|
| CommonPopup.selectCursor | 0x68 |
| CommonPopup.commandList | 0x70 |
| CommonCommand.text | 0x18 |

### Vehicles
| Field | Offset |
|-------|--------|
| TransportationController.infoData | 0x18 |
| Transportation.modelList | 0x18 |
| TransportationInfo.MapObject | 0x28 |
| TransportationInfo.Type | 0x6C |
| TransportationInfo.Enable | 0x48 |

### NPC Item Selection
| Field | Offset |
|-------|--------|
| SelectFieldContentManager.controller | 0x40 |
| SelectFieldContentControllerBase.contentDataList | 0x28 |
| SelectFieldContentControllerBase.selectCursor | 0x30 |
| SelectFieldContentController.view | 0x60 |
| SelectFieldContentData.NameMessageId | 0x18 |
| SelectFieldContentData.DescriptionMessageId | 0x20 |

---

## State Machine Values

### ItemWindowController.State
```
None=0, CommandSelect=1, UseSelect=2, ImportantSelect=3
OrganizeSelect=4, TargetSelect=5, InterChangeSelect=6, Equipment=7
```

### EquipmentWindowController.State
```
None=0, Command=1, Info=2, Select=3
```

### BattleCommandSelectController.State
```
None=0, Normal=1, Extra=2, Manipulate=3
```

### AbilityWindowController.State
```
None=0, UseList=1, UseTarget=2, MemorizeList=3, RemoveList=4
Exchange=5, Forget=6, Command=7, Popup=8, MemorizePopup=9
RemovePopup=10, ExchangePopup=11
```
**Key:** State 3 = Learn mode (abilityItemList), States 1/4 = Use/Remove (contentList)

### ShopController.State
```
None=0, SelectCommand=1, SelectProduct=2, SelectSellItem=3
SelectAbilityTarget=4, SelectEquipment=5, ConfirmationBuyItem=6
```

### TransportationType (FF3)
```
0=None, 1=Player, 2=Ship, 3=Plane, 4=Symbol, 5=Content
6=Submarine, 7=LowFlying, 8=SpecialPlane
```

---

## Key Types

### Menus
| Menu | Controller | Patch Method |
|------|------------|--------------|
| Items | `ItemListController` | `SelectContent(...)` |
| Equipment | `EquipContentListController` | `SelectContent(...)` |
| Job | `JobChangeWindowController` | `UpdateJobInfo(...)` |
| Shop Items | `ShopListItemContentController` | `SetFocus(bool)` |
| Shop Quantity | `ShopTradeWindowController` | `UpdateCotroller(bool)` |
| Magic | `AbilityContentListController` | `SetCursor`, state machine |
| Magic Target | `AbilityUseContentListController` | `SetCursor(Cursor)` |
| Item Target | `ItemUseController` | `SelectContent(...)` |

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
OwnedCharacterData.Parameter.ConfirmedLevel()  // NOT BaseLevel
OwnedCharacterData.Parameter.CurrentConditionList
BattleUtility.GetJobLevel(OwnedCharacterData)
```

### FF3 Spell System
```csharp
int spellLevel = ability.Ability.AbilityLv;  // 1-8
int current = param.CurrentMpCountList[spellLevel];
int max = param.ConfirmedMaxMpCount((AbilityLevelType)spellLevel);
```

---

## Architecture Patterns

### Suppression Pattern
```csharp
public static bool ShouldSuppress() => IsActive;

public static void SetActive_Postfix(bool isActive) {
    if (!isActive) MyMenuState.ClearState();
}
```
Add `activeInHierarchy` validation when state might persist.

### Popup Detection
Patch base `Popup.Open()`, use `TryCast<T>()` for type (GetType().Name returns "Popup" in IL2CPP).

### Battle State Clearing
`BattleResultPatches.ClearAllBattleMenuFlags()` at victory. Submenus validate `BattleCommandSelectController` state machine.

---

## Working Solutions

### Title Screen
`SplashController.InitializeTitle()` stores text, `SystemIndicator.Hide()` speaks when loading hides.

### Save/Load Popups
Patch `SetPopupActive(bool)` - enum params crash like strings.

### Config Menu
Validate via `activeInHierarchy`. `SetFocus` handles navigation, `SwitchArrowSelectTypeProcess` handles value changes.

### Equipment Job Requirements (I Key)
`UserDataManager.ReleasedJobs` → `Weapon/Armor.EquipJobGroupId` → `JobGroup.Job{N}Accept` → `Job.MesIdName`

### Vehicle Transitions
Patch `FieldPlayer.GetOn(int typeId, ...)` and `GetOff(int typeId, ...)`.

### Map Transitions
Poll `UserDataManager.CurrentMapId`. Use `LocationMessageTracker` for dedup. En-dash separator matches `MSG_LOCATION_STICK`.

---

## Resolved Issues (Key Solutions)

### Battle System Messages
**Hook:** `BattleUIManager.SetCommadnMessage(string)` - announces escape, back attack, etc.

### Vehicle Interior Map Names
**Fix:** `MapNameResolver.cs:148` - skip redundant mapTitle when equals areaName.

### New Game Naming (KeyInput)
**Controllers:** `CharacterContentListController.SetTargetSelectContent(int)`, `NameContentListController.SetFocus(int)`
**Suggested Name:** Hook `UpdateView(List<NewGameSelectData>)`, track name changes per slot.
**Start Popup:** `NewGamePopup` extends MonoBehaviour (not Popup) - patch `InitStartPopup()`.

### Battle Action Dedup
**Fix:** Use object-based dedup (`BattleActData` reference) not text-based.

### Magic Menu Target Selection
**Hook:** `AbilityUseContentListController.SetCursor(Cursor)`, read from contentList at 0x50.

### Dialogue Dedup Reset
**Hook:** `MessageWindowManager.Close()` - clear `lastDialogueMessage` and `pendingDialogueText`.

### Entity Scanner Refresh
**Event-driven:** `FieldTresureBox.Open()` and `MessageWindowManager.Close()` trigger 1-frame delayed rescan.

### Battle Pause Menu
**Detection:** Read `isActivePauseMenu` at offset 0x71. Cursor path contains `curosr_parent` (game typo).
**Suppression order:** Check battle submenus BEFORE pause state. Add `uiManager.Initialized` check.

### Entity Scanner World Map
**Fix:** Call `entityScanner.ForceRescan()` in `CheckMapTransition()` on map ID change.

### Not on Map Guard
**Check:** `FieldMap.activeInHierarchy` and `FieldPlayerController.fieldPlayer` exist.

### Vehicle Tracking
**VehicleTypeMap:** Populate from `Transportation.ModelList` via pointer offsets. Check BEFORE other type detection. Filter ResidentChara AFTER.

### Magic Menu States
**Fix:** Use `AccessTools.Method()` for private IL2CPP methods. Use state machine (not boolean flags) to determine Learn vs Use/Remove mode.

### Battle State on Return to Title
**Hook:** `TitleMenuCommandController.SetEnableMainMenu(bool)` - call `MenuStateRegistry.ResetAll()`.

### Battle Popup Buttons
**Hook:** `KeyInput.CommonPopup.UpdateFocus` - read cursor/commandList directly.

### Duplicate Map Announcements
**Fix:** Use en-dash (U+2013) separator in `MapNameResolver` to match `MSG_LOCATION_STICK` format.

### NPC Event Item Selection
**Hook:** `SelectFieldContentController.SelectContent(int)` - read from contentDataList at 0x28.

---

## Game Code Typos
`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`, `SetCommadnMessage`, `curosr_parent`
