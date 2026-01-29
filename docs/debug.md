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
| `DialogueTracker` | Per-page dialogue state, speaker tracking |
| `LineFadeMessageTracker` | Per-line story text announcements |
| `TextUtils` | Strip icon markup, formatting |
| `MoveStateHelper` | Vehicle/movement state |
| `GameObjectCache` | Component caching |
| `CoroutineManager` | Frame-delayed operations |
| `SoundPlayer` | Windows waveOut API, 4-channel concurrent playback |
| `EntityTranslator` | Japanese→English entity names via JSON dictionary |

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

### Message Window
| Field | Offset |
|-------|--------|
| MessageWindowManager.messageList | 0x88 |
| MessageWindowManager.newPageLineList | 0xA0 |
| MessageWindowManager.spekerValue | 0xA8 |
| MessageWindowManager.messageLineIndex | 0xB0 |
| MessageWindowManager.currentPageNumber | 0xF8 |

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

---

## Game Code Typos
`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`, `SetCommadnMessage`, `curosr_parent`
