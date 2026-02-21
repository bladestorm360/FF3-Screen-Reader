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

| Class | Location | Purpose |
|-------|----------|---------|
| `AnnouncementDeduplicator` | Utils/ | Exact-match dedup: `ShouldAnnounce(context, text)` |
| `LocationMessageTracker` | Utils/ | Map transition dedup (containment check) |
| `DialogueTracker` | Patches/MessageWindowPatches.cs | Per-page dialogue state, speaker tracking |
| `LineFadeMessageTracker` | Patches/LineFadeMessagePatches.cs | Per-line story text announcements |
| `TextUtils` | Utils/ | Strip icon markup, formatting |
| `MoveStateHelper` | Utils/ | Vehicle/movement state |
| `GameObjectCache` | Utils/ | Component caching via `GetOrFind<T>()` (replaces `FindObjectOfType`) |
| `CoroutineManager` | Utils/ | Frame-delayed operations |
| `SoundPlayer` | Utils/ | Windows waveOut API, 4-channel concurrent playback |
| `EntityTranslator` | Utils/ | Japanese→English entity names via JSON dictionary |
| `MenuStateRegistry` | Utils/ | Centralized menu state tracking; `SetActiveExclusive()` |
| `MenuStateHelper` | Utils/ | Boilerplate reduction for 15 state classes |
| `IL2CppOffsets` | Utils/ | Centralized IL2CPP memory offsets (nested classes by system) |
| `DirectionHelper` | Utils/ | Shared direction calculations (NavigableEntity + WaypointEntity) |
| `CollectionHelper` | Utils/ | IL2CPP collection iteration utilities |
| `PlayerPositionHelper` | Utils/ | Player position access helpers |
| `WindowsFocusHelper` | Utils/ | Windows focus state detection |

---

## Memory Offsets

> All offsets are centralized in `Utils/IL2CppOffsets.cs`. The sections below are a readable reference.

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
| GameOverLoadPopup (message) | 0x40 |
| GameOverLoadPopup (selectCursor) | 0x58 |
| GameOverLoadPopup (commandList) | 0x60 |
| GameOverPopupController.view | 0x30 |
| GameOverPopupView.loadPopup | 0x18 |
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

### Message Window
| Field | Offset |
|-------|--------|
| MessageWindowManager.messageList | 0x88 |
| MessageWindowManager.newPageLineList | 0xA0 |
| MessageWindowManager.spekerValue | 0xA8 |
| MessageWindowManager.messageLineIndex | 0xB0 |
| MessageWindowManager.currentPageNumber | 0xF8 |

### Walk/Run & Encounters
| Field | Offset |
|-------|--------|
| UserDataManager.configSaveData | 0xB8 |
| ConfigSaveData.isAutoDash | 0x40 (int: 0=off, 1=on) |
| UserDataManager.CheatSettingsData | 0xA8 |
| CheatSettingsData.isEnableEncount | 0x10 (bool) |
| FieldKeyController.dashFlag | 0x28 |

---

## State Machine Values

> Canonical source: `Utils/IL2CppOffsets.cs`. Tables below are for quick reference.

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

### Dialogue
| Feature | Controller | Patch Method |
|---------|------------|--------------|
| Dialogue pages | `MessageWindowManager` | `SetContent`, `PlayingInit` |
| Speaker name | `MessageWindowManager` | `SetSpeker` |
| Dialogue close | `MessageWindowManager` | `Close` |
| Story text | `LineFadeMessageWindowController` | `SetData`, `PlayInit` |

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

### MenuStateHelper Pattern
Boilerplate reduction for all 15 state classes:
```csharp
private static readonly MenuStateHelper _helper = new(MenuStateRegistry.X_MENU, "Context.Name");
static MyMenuState() { _helper.RegisterResetHandler(); }
public static bool IsActive { get => _helper.IsActive; set => _helper.IsActive = value; }
```

### SetActiveExclusive Pattern
Replaces manual `ClearOtherMenuStates` calls:
```csharp
MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.X_MENU);
```

### GameObjectCache Pattern
Replaces `FindObjectOfType` with cached lookups:
```csharp
var controller = GameObjectCache.GetOrFind<SomeController>();
```

### Popup Detection
Patch base `Popup.Open()`, use `TryCast<T>()` for type (GetType().Name returns "Popup" in IL2CPP).

### Battle State Clearing
`BattleResultPatches.ClearAllBattleMenuFlags()` at victory. Submenus validate `BattleCommandSelectController` state machine.

---

## File Organization

| Directory | Contents |
|-----------|----------|
| `Core/` | Main mod, input manager, audio loops, preferences, waypoints, entity navigation, game info announcer |
| `Core/Filters/` | Entity filter interface (`IEntityFilter`) and implementations: `PathfindingFilter`, `ToLayerFilter`, `CategoryFilter` |
| `Field/` | Entity scanner, navigable entities, waypoint entities, field navigation/pathfinding, filter context |
| `Menus/` | Menu text readers: config, shop, status, save, character selection, ability commands |
| `Patches/` | All Harmony patches organized by game system (battle, menus, field, popups, messages, etc.) |
| `Utils/` | Shared utilities: IL2CPP offsets, state registry, announcement dedup, sound player, text utils, etc. |

---

## Key Refactoring Patterns

| Pattern | Purpose |
|---------|---------|
| `MenuStateHelper` | Boilerplate reduction for 15 state classes via shared helper |
| `MenuStateRegistry.SetActiveExclusive()` | Replaced manual `ClearOtherMenuStates` across all state classes |
| `GameObjectCache.GetOrFind<T>()` | Replaced 19 `FindObjectOfType` calls with cached lookups |
| `IL2CppOffsets` nested classes | Centralized offsets from 12+ patch files into one source of truth |
| `FieldNavigationHelper.GetPathDescription()` | Shared path description logic (NavigableEntity + WaypointEntity) |
| `EntityScanner.ForceRescan()` | Clears `entityMap` cache on scene transitions |
| `EntityScanner.mapExitPositionBuffer` | Reusable buffer for wall tone suppression near exits |

---

## Working Solutions

| Feature | Solution |
|---------|----------|
| Title Screen | `SplashController.InitializeTitle()` stores text, `SystemIndicator.Hide()` speaks |
| Save/Load Popups | Patch `SetPopupActive(bool)` - enum params crash |
| Config Menu | Validate via `activeInHierarchy`. `SetFocus` for nav, `SwitchArrowSelectTypeProcess` for values |
| Equipment Job Reqs | `UserDataManager.ReleasedJobs` → `Weapon/Armor.EquipJobGroupId` → `JobGroup.Job{N}Accept` → `Job.MesIdName` |
| Vehicle Transitions | Patch `FieldPlayer.GetOn(int)` and `GetOff(int)` |
| Map Transitions | Poll `UserDataManager.CurrentMapId`. `LocationMessageTracker` for dedup. En-dash matches `MSG_LOCATION_STICK` |

---

## Resolved Issues

| Issue | Solution |
|-------|----------|
| Battle System Messages | Hook `BattleUIManager.SetCommadnMessage(string)` |
| Vehicle Interior Maps | Skip mapTitle when equals areaName (`MapNameResolver.cs:148`) |
| New Game Naming | `CharacterContentListController.SetTargetSelectContent(int)`, `NameContentListController.SetFocus(int)`. `NewGamePopup` extends MonoBehaviour - patch `InitStartPopup()` |
| Battle Action Dedup | Use object-based dedup (`BattleActData` reference) not text-based |
| Magic Target Selection | Hook `AbilityUseContentListController.SetCursor(Cursor)`, read contentList at 0x50 |
| Dialogue State Reset | Hook `MessageWindowManager.Close()` → `DialogueTracker.Reset()` |
| Entity Scanner Refresh | `FieldTresureBox.Open()` and `MessageWindowManager.Close()` trigger 1-frame delayed rescan |
| Wall Bump Detection | Hook `FieldController.OnPlayerHitCollider(FieldPlayer)`, 300ms cooldown |
| Battle Pause Menu | Read `isActivePauseMenu` at 0x71. Check submenus BEFORE pause state. Add `uiManager.Initialized` check |
| Entity Scanner World Map | Call `entityScanner.ForceRescan()` in `CheckMapTransition()` on map ID change |
| Not on Map Guard | Check `FieldMap.activeInHierarchy` and `FieldPlayerController.fieldPlayer` exist |
| Vehicle Tracking | Populate VehicleTypeMap from `Transportation.ModelList`. Check BEFORE other types, filter ResidentChara AFTER |
| Magic Menu States | Use `AccessTools.Method()` for private IL2CPP methods. State machine determines Learn vs Use/Remove |
| Battle State → Title | Hook `TitleMenuCommandController.SetEnableMainMenu(bool)` → `MenuStateRegistry.ResetAll()` |
| Battle Popup Buttons | Hook `KeyInput.CommonPopup.UpdateFocus`, read cursor/commandList directly |
| Duplicate Map Announcements | Use en-dash (U+2013) separator to match `MSG_LOCATION_STICK` format |
| NPC Event Item Selection | Hook `SelectFieldContentController.SelectContent(int)`, read contentDataList at 0x28 |
| Walk/Run (F1) | Patch `FieldKeyController.SetDashFlag` to cache flag. XOR with `ConfigSaveData.isAutoDash` to get effective run state |
| Encounters (F3) | Read `CheatSettingsData.IsEnableEncount` property directly |
| Enemy HP Display (F5) | `FFIII_ScreenReaderMod.EnemyHPDisplay` property (0=Numbers, 1=Percentage, 2=Hidden). Block toggle during battle via `MenuStateRegistry` |
| Placeholder Entities | `IsPlaceholderEntity()` filters decorative/non-interactive overworld entities (stone statues, vehicle spawns, barrier markers, location markers tracked via map exits). 浮遊大陸 (Floating Continent) NOT filtered |
| Item Quantity Display | `ItemMenuPatches.cs` and `BattleItemPatches.cs` format items as "Name quantity: Description" using `ItemListContentData.Count` |
| Waypoint Name Default | New waypoint text field starts blank (not pre-filled with "Waypoint N"). Rename still shows current name |

---

## Event-Driven Map Transitions
Hook `SubSceneManagerMainGame.ChangeState`. Field states (`FieldReady=2`, `Player=3`, `ChangeMap=1`) trigger map announcements and battle state clear.

---

## Per-Page Dialogue System
Hook `PlayingInit` (fires once per page). Architecture:
```
SetContent → Read messageList + newPageLineList via pointer access
SetSpeker → Store speaker in DialogueTracker
PlayingInit → Get currentPageNumber, combine lines, announce
Close → Reset DialogueTracker state
```
Multi-line support: `messageList` (0x88) = all lines, `newPageLineList` (0xA0) = page break indices. `GetPageText()` combines lines within page boundaries.

---

## LineFade Per-Line Announcements
`LineFadeMessageTracker` stores messages from `SetData`, tracks line index. `PlayInit` fires per line, announces via `AnnounceNextLine()`.

---

## External Sound Player
Windows waveOut API with 4 channels. Pre-generates WAV tones at init.

| Channel | Purpose |
|---------|---------|
| Movement | Footsteps |
| WallBump | Collision thud |
| WallTone | Looping directional tones (hardware loop, bitmask direction change) |
| Beacon | Panning ping (unmanaged buffer writes) |

**Wall Bump:** `FieldPlayerKeyController.OnTouchPadCallback` prefix captures position. Coroutine waits 0.08s, checks position delta (< 0.1 = wall). Requires 2 consecutive hits to confirm.

**Wall Tone Loop:** 100ms coroutine checks tiles via `MapRouteSearcher.Search()`. Suppresses near exits (`EntityScanner.GetMapExitPositions()`). 1s suppression on map transitions.

**Audio Beacons:** 2s coroutine pings toward entity. Volume scales with distance (max 500 units), pan from X delta. 400Hz north, 280Hz south.

---

## Entity Translation System
JSON dictionary (`FF3_translations.json`) in `UserData/FFIII_ScreenReader/`. Called in `EntityScanner.GetEntityNameFromProperty()`. Prefix stripping via regex (removes "6:" or "SC01:" prefixes).

**Dump:** Hotkey `0` writes `EntityNames.json` with `{ "MapName": { "JapaneseName": "" } }` structure.

**Current Translations:**
| Japanese | English | Location |
|----------|---------|----------|
| 風水師 | Geomancer | Duster |
| 商人2 | Merchant 2 | Duster |
| 商人4 | Merchant 4 | Duster |
| 吟遊詩人2 | Bard 2 | Duster |

---

## Game Code Typos
`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`, `SetCommadnMessage`, `curosr_parent`
