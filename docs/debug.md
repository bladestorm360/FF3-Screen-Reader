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
| `SoundPlayer` | Utils/ | Playback facade over `AudioEngine` (SDL3 audio streams) and `ToneGenerator` |
| `ModTextTranslator` | Utils/ | `T(key)`: mod strings from embedded `mod_text.json` in the game's language, English fallback |
| `MenuPosition` | Utils/ | `Format(text, index, count)` appends "(X of Y)" when position announcements are on |
| `BattleStateHelper` | Patches/BattleStatePatches.cs | Single in-battle flag: `OnBattleStart`, `TryClearOnBattleEnd`, `IsInBattle` |
| `NavigationBuffer` | Core/ | Virtual list with groups (status screen, bestiary entry, controls pop-up): next/prev, group jumps, top/bottom |
| `UsableByAnnouncer` | Menus/ | U key: unlocked jobs that can equip the focused equipment in the current context |
| `GameToggleAnnouncer` | Patches/ | Speaks walk/run and encounter state from the game's own setters (F1/F3/R3/L3, any input) |
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
ShopListMainContentController (KeyInput).selectCursor: 0x48, productContentList: 0x68
```

### Battle Target / Items
```
BattleTargetSelectController.playerDataList: 0x30, enemyDataList: 0x38
BattleTargetSelectController.TargetPlayerList: 0x98, TargetEnamyList: 0xA0
BattleTargetSelectController.selectCursor: 0xD0, stateMachine: 0xD8 (1=Players, 3=Enemys)
BattleItemInfomationController.displayDataList: 0xE0
```

### Title / Config
```
TitleWindowController.view: 0x48, TitleWindowView.startText: 0x30
TitleMenuCommandController.activeContents: 0x28
KeyInput ConfigController.detailsController: 0x48 (read by pointer: cheatSettingsController has the same type)
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
| Field main menu | `MainMenuController` | `Show`, `InitNone`; `ItemWindowController.CommandSelectInit`, `EquipmentWindowController.CommandInit`, `AbilityWindowController.CommandInit` (read focused command on entry) |
| Title menu | `TitleWindowController` | `InitSelect`, `InitializeOption`, `InitializeExtra` |
| Save list | `SaveListController` | `SetActive(bool)` |
| Shop | `ShopController` | `InitSelectCommand`, `Close`; `ShopCommandMenuController.SetCursor`; `ShopInfoController.SetDescription` (list focus) |
| Shop Quantity | `ShopTradeWindowController` | `Show`, `AddCount`, `TakeCount` (each call is one key press) |
| Magic | `AbilityContentListController` | `SetCursor`, state machine |
| Magic Target | `AbilityUseContentListController` | `SetCursor(Cursor)` |
| Item Target | `ItemUseController` | `SelectContent(...)`; `ItemWindowController.TargetSelectInit` |
| Config row | `KeyInput.ConfigActualDetailsControllerBase` | `SelectCommand` (focused row + position); `OptionController.ShowConfig` / `InitSelectLanguage` (first row on open) |
| Controls pop-up | `ConfigKeysSettingController` | `GamePadHelpInit` / `KeyboardHelpInit` (build buffer), `*SelectInit` / `Close` (clear) |

### Battle
| Feature | Controller | Method |
|---------|------------|--------|
| Battle state | `BattleController` | `EndWinFadeOutCallback`, `EndLoseFadeOutCallback`, `EndEscapeFadeOut`, `EndFadeOutCallback` (postfix), `Exit(bool)` (prefix) |
| Commands / turn | `BattleCommandSelectController` | `SetCursor(int)`; "{0}'s turn" once per turn window |
| Targets | `BattleTargetSelectController` | `EnemysInit`, `PlayerInit` (bounded initial read), `SelectContent` |
| Start messages | `BattleController.StartPreeMptiveMes`; KeyInput `SetMessage`, Touch `SetSystemMessage` / `SetCommandMessage` | action names filtered via `LastActionName` |
| System messages | `BattleUtility.SetSystemMessageAtKey`, `SystemMessageView/Controller/Manager.SetMessage` | `GlobalBattleMessageTracker` |
| Items | `BattleItemInfomationController` | `SelectContent(...)` |
| Magic | `BattleFrequencyAbilityInfomationController` | `SelectContent(...)` |
| Results | `ResultMenuController` (KeyInput/Touch) | `ShowPointsInit` (points + job level-ups), `ShowGetItemsInit`, `ShowStatusUpInit`; KeyInput only: `ShowGetAbilitysInit`, `ShowLevelUpAbilitysInit`, `Close` (EXP-tone catch-all, replaces the shared `EndWaitInit`); Touch only: `ShowSkillLevelsInit` (all manual) |
| Level-ups | `ResultSkillController` (KeyInput/Touch) | `ShowLevelUp`. `ShowJobProficiencyLevelUp` is not hooked: no callers, its body is inlined into `ShowPointsInit` |

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
`BattleResultPatches.ClearAllBattleMenuFlags()` at victory. Submenus validate `BattleCommandSelectController` state machine. `BattleStateHelper` is the only in-battle flag: set by the battle-start hooks, cleared by the `BattleController` fade-out callbacks / `Exit(bool)`, the title menu, a non-battle scene load, and Tab (opening the main menu proves the battle ended) — Tab only when `FindObjectOfType<BattleController>()` finds no live battle, since Tab is also pressed mid-battle.

### Language-Independent Battle Filtering
Never match English text. Action messages are dropped by comparing against `ParameterActFunctionManagment_CreateActFunction_Patch.LastActionName` (the action already announced as "Actor: Action"), deferred one frame so the comparison sees the current action.

### State-Entry Hooks + Bounded Waits (no per-frame patches)
Screens are read from their `*Init` / `Show` / `SetActive(true)` entry methods. `StateMachine<T>.Change` (0x114A3A0) runs the old state's Exit and the new state's Init synchronously, so an `*Init` postfix runs inside whatever called `Change`. When the UI is not populated yet in the postfix, a managed coroutine retries for a bounded number of frames (`yield return null`, no timer) and stops as soon as it reads something: the field item list / item target re-read (`ItemMenuPatches.ReadWhenReady`), and the battle target initial read as a fallback only (it reads in the Init postfix first). `MenuTextDiscovery.WaitAndReadCursor` is a single one-frame deferral after the cursor event, not a retry. The title "Press any button" is read one frame after the game's own `SystemIndicator.Hide` call that precedes the prompt (see "Open-issues pass (2026-09-23, session 2)"). Every remaining timer and poll is listed in "Round 2 (2026-09-24)".

### Double-Read Prevention
Reset the menu's dedup at its Init hook; popups with their own focus reader set `HasOwnFocusReader` so the generic cursor path stays quiet; generation counters (`reannounceGen`, `initialReadGen` in `BattleCommandPatches`) cancel a deferred read when a newer event arrives; target state entry resets the target dedup unless a target was already spoken that frame (`lastTargetSpokenFrame`); `BattlePausePatches.Begin/EndCommonPopupRead` holds the CommonPopup cursor reader (`TryReadCommonPopupCursor`, driven by `Cursor.NextIndex/PrevIndex`) until the open-read has spoken the message and focused button, then resumes it without repeating that button; the game-over load popup works the same way (`GameOverPatches.TryReadLoadPopupCursor`). Deferred config row re-reads give up when `SelectCommand` read a row in or after the frame they were armed (`ConfigActualDetails_SelectCommand_Patch.RowSpokenSince`).

### RVA Sharing Check
IL2CPP folds identical method bodies: patching one body fires for every method sharing its RVA. Every new hook's RVA was checked unique in `dump.cs`. Avoided: `CheatSettingsData.set_IsEnableEncount` (shared by 23 methods) — hook `CheatSettingsClient.SetIsEnableEncount` instead; `ShopTradeWindowController.Close` (shared by 12); Touch `ResultMenuController.EndWaitInit` (0x26D8F0, the empty stub shared by ~2,500 methods, one called inside `SetCommandData` — removed 2026-09-23); KeyInput `ResultMenuController.EndWaitInit` (0x53E420, shared with Touch `WarehousePopupController.Close` — replaced by `ResultMenuController.Close`, session 2). Kept but class-checked: `SystemMessageWindowView.SetMessage` (0x3C5AE0, shared by 47 string setters); since round 2 also KeyInput `BattleTargetSelectController.ShowWindow` (0x2F2990, 10), the `SetNextState` of KeyInput `EquipmentWindowController` (0x40BA70, 13), KeyInput `ItemWindowController` (0x40BAA0, 12) and Serial KeyInput `AbilityWindowController` (0x2A0B70, 9), and Serial KeyInput `StatusDetailsController.ExitDisplay` (0x2BCA70, 10), all through `HarmonyPatchHelper.IsNativeInstanceOf<T>`. Removed: `Last.OutGame.Library.FieldKeyController.SetDashFlag` (0x2BD2A0, 17). The reverse trap: a method whose body the compiler inlined into its caller has no callers of its own, so a hook on it never fires — check for direct calls to the RVA in `GameAssembly.dll` (`ResultSkillController.ShowJobProficiencyLevelUp`: none).

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
| Title Screen | `TitleWindowController.Initialize` keeps the window; one frame after `SystemIndicator.Hide()`, if the window is in the None state and `TitleWindowView.startText` went from hidden to shown, it is spoken (fallback: mod text "Press any button"). The old Hide trigger was ~1 s early because it spoke on the first Hide (from `SceneTitleScreen.CreateInstance`, before the fade-in); the visibility check skips that one |
| Save/Load Popups | Patch `SetPopupActive(bool)` - enum params crash |
| Config Menu | Validate via `activeInHierarchy`. `ConfigActualDetailsControllerBase.SelectCommand` for nav (replaced the `SetFocus` attribute patch), `SwitchArrowSelectTypeProcess` for values |
| Scroll Messages | `ScrollMessageManager.Play` / `ScrollMessageClient.PlayMessageId/Value`: one line spoken at once, several lines paced by `scrollTime / (lines + 1)` with `WaitForSeconds`; never interrupts. Identical text within 2 s is the nested Client→Manager call and is skipped |
| Mod Text | Every `T("...")` key must exist in `mod_text.json` (12 languages, CRLF, 2-space indent, UTF-8 without BOM). The embedded parser is hand-written: keep values plain strings |
| Equipment Job Reqs | `UserDataManager.ReleasedJobs` → `Weapon/Armor.EquipJobGroupId` → `JobGroup.Job{N}Accept` → `Job.MesIdName` |
| Vehicle Transitions | Patch `FieldPlayer.GetOn(int)` and `GetOff(int)` |
| Map Transitions | Poll `UserDataManager.CurrentMapId`. `LocationMessageTracker` for dedup. En-dash matches `MSG_LOCATION_STICK` |

---

## Resolved Issues

| Issue | Solution |
|-------|----------|
| Battle System Messages | Hook `BattleUtility.SetSystemMessageAtKey` and the `SystemMessageView/Controller/Manager.SetMessage` chain. (`BattleUIManager.SetCommadnMessage` was documented here but never patched) |
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
| Battle Popup Buttons | CommonPopup buttons read from the popup's own cursor moves (`Cursor.NextIndex/PrevIndex` postfix, matched by `selectCursor` identity), cursor/commandList read directly. `CommonPopup.UpdateFocus` was a per-frame hook (round 2) |
| Duplicate Map Announcements | Use en-dash (U+2013) separator to match `MSG_LOCATION_STICK` format |
| NPC Event Item Selection | Hook `SelectFieldContentController.SelectContent(int)`, read contentDataList at 0x28 |
| Walk/Run (F1) | `GameToggleAnnouncer`: `Config.set_IsAutoDash` / `MapUIManager.AutoDashOperationSwitch` speak the new state (effective run = dash flag XOR `isAutoDash`); replaces the old F1 coroutine |
| Encounters (F3) | `GameToggleAnnouncer`: `CheatSettingsClient.SetIsEnableEncount` speaks the new state; seeded on the first field scan so the initial value is silent. Replaces the old F3 coroutine |
| Enemy HP Display (F5) | `FFIII_ScreenReaderMod.EnemyHPDisplay` property (0=Numbers, 1=Percentage, 2=Hidden). Block toggle during battle via `MenuStateRegistry` |
| Placeholder Entities | `IsPlaceholderEntity()` filters decorative/non-interactive overworld entities (stone statues, vehicle spawns, barrier markers, location markers tracked via map exits). 浮遊大陸 (Floating Continent) NOT filtered |
| Item Quantity Display | `ItemMenuPatches.cs` and `BattleItemPatches.cs` format items as "Name, quantity: Description" using `ItemListContentData.Count`; "(X of Y)" is appended last, as in FF1 |
| Waypoint Name Default | New waypoint text field starts blank (not pre-filled with "Waypoint N"). Rename still shows current name |

### Review fixes (2026-09-23)
Adversarial review of the FF1 parity pass. Not yet verified in game.
- **Job level-ups:** announced from the `ShowPointsInit` postfixes (KeyInput + Touch). `ResultSkillController.ShowJobProficiencyLevelUp` (KeyInput 0x61EF70, Touch 0x490140) has no callers — `ShowPointsInit` inlines it (both call `SetJobProficiencyLevelUpList` directly) — so its hooks were removed. The per-character `_joblevelup` key dedupes.
- **Target re-entry:** `EnemysInit`/`PlayerInit` postfix clears the target index dedup, so Attack → cancel → Attack (or a lone survivor) is read again. `EnemysInit` calls `SelectContent(enemies)` itself (0x89DB09), which may already have spoken the target inside the Init body; `lastTargetSpokenFrame` skips the reset in that frame so it is not read twice. `PlayerInit` does not call `SelectContent`.
- **Off-field scan:** `InputManager.IsOnValidMap` rescans for `FieldPlayerController` on a cache miss at most once per 30 frames (it runs every frame; off-field there is nothing to find).
- **Tab:** clears the in-battle flag only when no `BattleController` exists (see Battle State Clearing).
- **Touch `EndWaitInit`:** patch removed (shared-stub RVA, see RVA Sharing Check).
- **Manual patches:** the attribute patches added by the parity pass were converted — `BattleResultPatches.ApplyPatches` (`ShowGetAbilitysInit`, `ShowLevelUpAbilitysInit`) and `BattleCommandManualPatches` (`SetCommandData` prefix). Older attribute classes are unchanged.
- **Coroutine eviction:** `CoroutineManager` stops the oldest managed coroutine past 20 without running its `finally`. The title prompt latch is now free once older than its 60 s timeout + 1 s, with a generation so only the current wait polls and clears it; the config initial-focus latch is a frame stamp that expires after 10 frames.

### Open-issues pass (2026-09-23, session 2)
The FF3 items of `OPEN_ISSUES.md`. Not yet verified in game. Every new hook's RVA was checked in `dump.cs` (count 1 unless stated) and its callers with `tools\hitscan.py` / capstone.

**Status screen position.** `StatusNavigationReader` now drives a `NavigationBuffer` built with the group starts {0, 6, 16, 21}, as FF1 does; "(X of Y)" comes from `CurrentGroupPosition()` (e.g. Strength "1 of 5"). Group jumps still speak no group name.

**Title "Press any button" (event hooks, no wait).** Disassembly of KeyInput `TitleWindowController`:
- `UpdateNone` (0x8CD650, the None/press-any-button state, per frame) shows the prompt in one place: once `FadeManager.IsFadeFinish` and `SceneTitle.PreloadIsFinished` are true and `view.startParent` (view 0x48, startParent 0x18) is inactive, it calls `SystemIndicator.Hide` (0x8CD798) and then `SafeActiveSet(startParent, true)` (0x8CD7AF), and checks `Input.anyKey` in that same frame. `SetEnableStartObject` (0x8CCD30) has no callers: it is inlined here.
- `SystemIndicator.Hide` (0x60BBC0, unique) callers: both `UpdateNone`s, `SceneTitleScreen.CreateInstance` (0x3F55AB, right after it builds the window, before the fade-in: the "~1 s early" call the old trigger spoke on), `MainGame.FinishSetupSubScenes`, `FieldMap` loads, `ConfigActualDetailsControllerBase.<FastSwitchFont>`.
- `TitleWindowController.Initialize(GameObject)` (0x8CBEB0, unique) has one caller, `SceneTitleScreen.CreateTitleWindow` (0x3F582A). It ends in `CreateState` then `stateMachine.Change(None)`, or `Change(ShortcutCommand)` when `SceneManager.arguments` (0x48) is set.
- `InitShortcutCommand` (return from the Extras) shows `startParent` without a Hide; `ShortcutExtraCommand` hides it again and either opens the Extra menu or goes back to None (and so through `UpdateNone`'s Hide).
- The mod keeps the window from the `Initialize` postfix. The `Hide` postfix records whether `startText` is visible, then one frame later speaks it only if it was hidden, is now visible, and `stateMachine` (0x18) is still None (0). Boot and return to title both build the window through `CreateInstance`, and both show the prompt only through `UpdateNone`, so both are covered. The Hide on the field finds no live title window and returns at once. `SplashController.InitializeTitle` and the "Title" scene-load triggers were removed; the old log confirms the scene is named "Title" but it is no longer used. Log line: `[TitleScreen] Press-any-button prompt shown`.

**Magic-shop target path.** `ShopMagicTargetSelectController.Show` / `SetFocus` are called only from `ShopController.InitSelectAbilityTarget`. The product callback `<InitSelectProduct>b__40_0` sets the next state to 4 (SelectAbilityTarget) only when `ShopProductData.ContentType == Ability` and `IsShopToSelectAbilityTarget()`; otherwise 6 (ConfirmationBuyItem) or 9. FF3's `product` master data has 394 rows: content types 1 (item, 247, including every `MSG_MAGIC_NAME_*` spell), 2 (weapon) and 3 (armor) only. The path is unreachable in FF3; spells go through the list + `ShopTradeWindowController`. No code needed.

**Value-0 battle views** (`BattleBasicFunction.CreateDamageView`, hit type from `ICalcResult.GetHitType()`, value from `GetValue(false)`, `CreateViewEntity` 0x94BB30):
- `CalcResult..ctor` defaults the hit type to Non (-1). `SetStatus` (0x3EC3C0) stores it as given.
- Scan of every `SetStatus` call (direct and interface slot 0) and every `GetFixedStatus` call: Zero (3) is passed only by `DamageAggregater.CheckUndead` and `MagicAbsorptionFunction.Calc`. RecoveryCondition (7) is passed nowhere.
- Buff/debuff functions resolve to Hit (0) or Miss (2): `GetAddConditionStatus(int)` passes `CalcExecuteFF3.AddConditionExection`'s return (0 on success, 2 on failure); the `int[]` overload passes 0/2; `AddConditionMulti*`, `RandomAddCondition`, `Kill` and `UserAddCondition` use `GetFixedStatus(0/2)`.
- Status cure: `RecoveryConditionFunction.Calc` asks `BattleUtility.IsUnitHaveRecoveryCondition(target, id)` (`CurrentConditionList` 0x88 has the id). If the target HAS it, the result is `GetFixedStatus(Hit)`, value 0, no conditions, the same as other non-damage effects. If not, it is `GetRecoveryCondition` (Miss, or Non with the condition in a special case). `RecoveryConditionMultiFunction` is `GetFixedStatus(Hit)`.
- So: Zero is spoken with the damage key ("Goblin: 0 damage"). RecoveryCondition gets "{0}: cured" for parity with the other mods, but FF3 never produces it. A real cure stays silent: it can't be told apart here, and announcing it needs a condition-removal hook (user decision). Each value-0 view logs `[Battle] value-0 view: hitType=N isRecovery=B target=X`. (Round 2: the "{0}: cured" branch, its key and the log line are removed; removals are announced from `BattleConditionController.RemoveFunction`.)

**Bestiary.** `LibraryInfoContent` is read through the active controller: KeyInput `LibraryInfoController.view` 0x18 → `LibraryInfoView.windowController` 0x18 → `LibraryInfoWindowController.contentController` 0x20 → `LibraryInfoContentController.view` 0x18. `FindObjectOfType` is only the fallback. If neither finds it, the buffer holds the name only, which is still spoken. The name auto-read uses `interrupt: true`.

**Performance.**
- `TitleMenuPatches.TryGetActiveCommandCount` / `FieldMenuPatches.TryGetFieldCommandCount` use the command controller kept by the menu-entry hooks (`TitleWindowController.InitSelect/InitializeOption/InitializeExtra` → `commandController`; `MainMenuController.Show/InitNone` → `commandMenuController`). Before, a `GetOrFind` ran on every generic cursor move and scanned the scene whenever the controller didn't exist.
- A popup closing over a config screen now calls `ReannounceAfterPopup`: one frame, through the details controller last read. The 10 s `GetOrFind<ConfigController>` wait is kept only for the config-bestiary return, where the in-game `ConfigController` exists.
- `ConfigMenuState.ShouldSuppress` checks that same details controller before any lookup, so the title Options screen, which has no `ConfigController`, no longer scans on each cursor move.
- `CycleNext` / `CyclePrevious` scan once and then speak without rescanning; K still rescans.

**Rule fixes.**
- `BattleSystemMessagePatches` postfixes take `__0` (or `__2` for the 3-parameter `SetSystemMessageAtKey`).
- `SystemMessageWindowView.SetMessage` (0x3C5AE0) shares its body with 46 other string setters (`SetName` ×11, `SetNameText`, `SetShopNameText`, `SetDescriptionText`, `SetConditionText`, …). Its postfix now checks the native class (`il2cpp_class_is_assignable_from`), as FF2 does for `ShowWindow`. Before, any of those setters could be spoken as a battle message.
- Touch `ShowSkillLevelsInit` (0x48D6B0) is now a manual patch. The KeyInput `EndWaitInit` (0x53E420 = `SafeActiveSet(field 0x38, false)`, shared with Touch `WarehousePopupController.Close`) is no longer hooked. The EXP-tone catch-all is now KeyInput `ResultMenuController.Close` (0x61AEC0, unique, called by `ResultUIManager.Close`): the results screen closing, a moment after the End state is entered.
- Pre-existing attribute patches not listed in OPEN_ISSUES (`CreateDamageView`, `BattleConditionController.Add`, the other result phases, config arrow, …) are unchanged. (Converted in round 2.)

**Localization.**
- `NavigableEntity` speaks type names and the scanner's English fallback names through `T()`. The English strings stay the internal keys: `ToLayerFilter` matches "ToLayer", and selection is matched by `Name`. `SpokenName` is used where a raw name was spoken.
- Vehicles use `GetLocalizedVehicleName`; `GetVehicleName` stays the internal lookup.
- Also through `T()`: equipment-slot and magic-command fallbacks, the 0-key dump messages, and the controller word labels (the same set as FF1: face-button shapes, View/Menu, Minus/Plus, D-pad, Home, "Button {0}").
- The map-exit separator was corrupted to U+FFFD + "?" and is restored to "→" (as in FF2/FF4).

**Controller.** In `ControllerRouter.Update`:
- With no gamepad, the state is reset from ModMode, or from ModMenu once the menu is closed, so `SuppressGameInput` can't lock the keyboard out.
- A mod menu opened from the keyboard (F8) switches the state to ModMenu, so the D-pad, stick and LT drive the menu instead of field actions.
- Verified only, already done: `IsOnValidMap` throttle (30 frames) and the Tab `BattleController` guard.

**Official-name substring pass (`translation.json`).** The first alignment pass (`tools/official_fix.py`) only fixed entries whose Japanese key *exactly* matched a string in the game's own message tables. This pass also fixed keys that *contain* an official proper noun: characters, places, key items, vehicles, monsters and jobs. Only that noun was replaced with the game's form for that language, taken from `tools/extract_entities.py gamedict` (FF3's own tables only).
- **Examples:**
  - ko Invincible → 인빈서블 (in インビンシブル（山を越える用）);
  - Sylx Key → Syrcus Key, ru Хрустальным ключом;
  - zht 阿古斯王 → 阿加斯王;
  - 赤/白/黒魔導士 spellings aligned to the official job names;
  - ko 크리스탈 → 크리스털 throughout.
- **Totals:** 64 entries / 219 values, applied with the Edit tool only.
- **Left as is:** German case forms, ドワーフ族 zh 矮人 (the stem of the official 矮人族), and ノーチラス th (the name already matches).
- The rules and the full old→new list are in `D:\Games\Dev\Unity\FFPR\tools\official_substring\`.

### Round 2 (2026-09-24)
Status removal, the per-frame / polling sweep, attribute patches, and a double-fix audit. Not yet verified in game. RVAs checked in `dump.cs` (`hookcheck.py`-style scan of every hooked method: RVA, how many methods share it, direct callers); game code read with `tools\hitscan.py` / `callees.py` / capstone.

**Status removal ("{0}: {1} removed").**
- FF1 hooks `BattleConditionController.Remove`. In FF3 (`Last.Battle.BattleConditionController`, the logic class) `Remove(unit, id, isNegate)` 0x3ABFD0 has only three callers: `BattleEndRecoveryCondition` and both `InterruptRemoveCondition` overloads (used by `RampageConditionFunction.Start`, `ActSelectSpSwordSky`, ATB `UpdateAlwaysEscape`), all with `isNegate = false`. Cures and wear-off never reach it:
  - a cure (calc) or a wear-off (`BattleConditionController.Recovery(unit, untilType)` 0x3AB980 → `BattleConditionFunction.NaturalRemove` 0x3AE520, or `UpdateCondtitonRecovery`) only takes the `Condition` out of the unit's `CurrentConditionList` (parameter 0x88);
  - the next `CheckConditionFunction` (0x3A99F0, from `CheckAddCondition` after each action) diffs condition counts against the unit's `BattleConditionFunction` list (`BattleUnitDataInfo` 0x28): `ConflictCondition` first, then `RemoveConditionFunction` (0x3ABB40) → `RemoveFunction(unit, id)` for every function left without its condition, then `AddConditionFunction` → `Add` (the existing add announcement);
  - `Remove` tail-calls `RemoveFunction` too.
- So the hook is a **prefix on `RemoveFunction`** (0x3ABE20, unique): the one place a condition function is destroyed. It finds the function by `function.condition.Id == id` (the game's own `<RemoveFunction>b__0` test) and does nothing if there is none, so a status that never applied (no `Add`) is never announced as removed.
- Name: `BattleDamagePatches.GetConditionName(condition)` (`MesIdName` → `MessageManager.GetMessage`), shared with the `Add` postfix; unit name `BattleUnitHelper.GetUnitName`. Condition master (`condition` table): named statuses are ids 5 (KO, `MSG_SYSTEM_243`), 6–15, 16 Haste, 17 Protect, 18 Reflect, 19, 20, 25; ids 1–4, 22–39 have `mes_id_name` None and stay silent.
- Silent:
  - battle end: prefix on `BattleEndRecoveryCondition` (0x3A8A70, called by `BattleController.StartWinResult` and `EndEscapeFadeOut`) latches `battleEndCleanup`, cleared by `BattleStateHelper.OnBattleStart` (unconditionally, at its top);
  - KO / Stone clean-up: the unit's `CurrentConditionList` still holds another condition of type UnableFight (5) or Mineralization (11) (the diff removes before it adds, and the KO is already in the list);
  - KO removed while `CurrentHP <= 0` (clean-up, not a revive);
  - the same (unit pointer, id) twice in one frame.
- Spoken: cures, wear-off (Haste/Protect/Reflect, Sleep on hit, Confusion…), conflicts (Haste cancelling Slow is "Slow removed" then "Haste"), revive ("X: KO removed"), transformations toggled off. `interrupt: false`. A spoken removal clears the add dedup for the same line, so the status coming back is announced again.
- Removed: the value-0 "{0}: cured" branch and its key (unreachable, RecoveryCondition is never produced), and the `[Battle] value-0 view:` log line. `HitType.Zero` still reads "0 damage".

**Attribute patches → manual.** All `[HarmonyPatch]` classes are registered in `TryManualPatching`, and the assembly carries `[HarmonyDontPatchAll]`:
- `BattleDamagePatches.ApplyPatches`: `CreateDamageView` 0x94B6D0 (HitType taken as `int __2`), `DamageViewUIManager.CreateHitCount` 0x435640, `BattleConditionController.Add` 0x3A8880 (plus the two new hooks above);
- `ParameterActFunctionManagment.CreateActFunction` 0x671DA0 (`Last.Battle.Function`);
- `BattleCommandManualPatches`: KeyInput `BattleCommandSelectController.SetCommandData` 0x3C2B20 (prefix + postfix), `SetCursor(int)` 0x3C3930, `BattleTargetSelectController.SelectContent` player 0x8A16B0 / enemy 0x8A15A0;
- `BattleResultPatches.ApplyPatches`: `Show`, `ShowPointsInit`, `ShowGetItemsInit`, `ShowStatusUpInit` (KeyInput and Touch; RVAs in the code). `ResultSkillController.ShowLevelUp` (KeyInput 0x61F010, Touch 0x490190) had no direct callers: those two hooks never fired and are deleted;
- config value changes (KeyInput `SwitchArrowSelectTypeProcess` 0x309430 / `SwitchSliderTypeProcess` 0x30A740, Touch `SwitchArrowTypeProcess` 0x8908D0 / `SwitchSliderTypeProcess` 0x890F20), equipment `SelectContent` (0x56F260, 0x573C80), item list / item target `SelectContent` (0x7BC180; 0x982E80 shares its body only with the same class's `SetCursor(IEnumerable, Cursor)`), wall-bump `FieldPlayerKeyController.OnTouchPadCallback` 0x71BE50.
- Every converted patch takes its arguments positionally (`__0`, `__1`…); enums as `int`.

**Per-frame hooks converted.**
- KeyInput `CommonPopup.UpdateFocus` (0x2FCC20): `UpdateSelect` calls it unconditionally every frame. The popup moves its cursor only through `Cursor.NextIndex/PrevIndex` (`<UpdateSelect>b__1`), so `ManualPatches.CursorNavigation_Postfix` first asks `BattlePausePatches.TryReadCommonPopupCursor`: if the moved cursor is the open popup's `selectCursor` (0x68) it reads the button (index dedup, held while the open-read is pending) and returns. The popup pointer is registered on `Popup.Open` (also in shops) and cleared on `Popup.Close`.
- KeyInput `GameOverLoadPopup.UpdateFocus` (0x7C7100): same per-frame call from `UpdateSelect`; same cursor-identity routing (`selectCursor` 0x58, via `GameOverPopupController` view 0x30 → `loadPopup` 0x18). The open-read one frame after `InitSaveLoadPopup` now speaks message + focused button (it used to speak the button from `UpdateFocus` inside the Init, then the message).
- Serial KeyInput `AbilityContentListController.UpdateController` (0x685FB0, called every frame by `UseListUpdate` / `MemorizeListSelectUpdate` / `RemoveListUpdate` / `ForgetUpdate`): replaced by postfixes on those states' Inits (`UseListInit` 0x693F60, `MemorizeListSelectInit` 0x68EC50, `RemoveListInit` 0x690060, `ForgetInit` 0x68DB10, `ExchangeInit` 0x68CFB0). `UpdateController` never calls `SetCursor` on its own (only from input lambdas), so the flag being set one frame earlier changes nothing. The character is read from `AbilityWindowController.listController` (0x58) → target character (0x98).
- KeyInput `LibraryMenuController.UpdateController` (0x9D3110, per frame) polled `selectState` (0x44) for the minimap. `ChangeState` (0x9D15B0) is inlined (no callers); the input lambdas `<UpdateMonsterList>b__16_0` / `<UpdateEnlargedMap>b__17_0` call `LibraryMenuHabitatController.SetCursor(true/false)` (0x9D42A0, unique) right before writing the state (`Show` calls it with false). The postfix maps true/false to 1/0 with the same "changed since last" logic.
- `FieldPlayer.ChangeMoveState` (0xF17590): `FieldPlayerKeyController.OnTouchPadCallback` calls it every frame a direction is held (walk/dash). Hook removed; vehicle boarding/disembarking is spoken by `ChangeTransportation`, `GetOn`, `GetOff` (its `AnnounceStateChange` could say "On ship" a second time with its own dedup). Keys "On ship", "On chocobo", "On airship" removed.
- `Last.OutGame.Library.FieldKeyController.SetDashFlag` (0x2BD2A0) shared its body with 16 bool setters (`set_ProfileEvents`, `set_IsLeftActionIcon`, …): the postfix ran for all of them and cached their value as the dash flag used by the F1 walk/run announcement. Removed; `MoveStateHelper.GetDashFlag` reads `FieldPlayerKeyController.pressDashKey` (0x58) when asked, the value `OnTouchPadCallback` XORs with `Config.IsAutoDash`.

**Polling converted.**
- Battle target initial read (`StartInitialTargetRead`): reads in the `EnemysInit` / `PlayerInit` postfix (`PlayerInit` sets the cursor with `BattleCursorUtility.SetTargetPlayer` before returning; `EnemysInit` calls `SelectContent` itself, and if that already spoke the target this frame nothing more is done). The 30-frame retry only runs if that read fails.
- Gallery / Music Player entry: the 2 s per-frame poll of the cached focus pointer is gone. The header ("Gallery" / "Music Player") is spoken one frame after the View state; the entry item is spoken from the `SetFocusContent` / `SetFocus` postfix that caches it (or right after the header if it is already cached).
- Bestiary formation: the 3 s per-frame `FindObjectOfType<ArBattleTopController>` poll is gone. `ArBattleTopController.SetActive(bool)` (0x64AE50, unique; called by the ArTop scene state in `ExtraArBattleTopUi`) fills `monsterPartyList` (`InitMonsterPartyList`) before returning; its postfix reads the formation, or "Formation view" when the list is empty. `ChangeState(5)` reads at once if `SetActive(true)` already ran for this visit. `ReannounceFormation` uses that controller before any scene search.

**Kept, with the reason.**
- Config slider (KeyInput `SwitchSliderTypeProcess`): the game calls it every frame from the tail of `UpdateController` with a null `Key` while a slider row is focused, and re-asserts the value there (`SetSliderValue`, `Slider.set_value`, `ConfigClient.SetVolume` too), so no method runs only on a change. The key-driven call comes from the input lambda `<UpdateController>b__0` (0x633F80) with a static `Key`. The prefix/postfix return on their first statement for a null key; for a key they compare the percentage before and after and speak only a change.
- `MenuTextDiscovery.WaitAndReadCursor`: one frame after the cursor event (scroll-view content is laid out after `NextIndex` returns); no retry.
- `ItemMenuPatches.ReadWhenReady`: bounded frame retry started from the state Init; the list and cursor are built after the Init (`ItemUseController.Show` only sets `nextState`; the list reaches `SelectContent` through lambdas / a coroutine).
- Config-bestiary return (`ReannounceWhenConfigReady`, `WaitForSeconds(0.1)`, max 10 s): returning is a sub-scene re-activation (`SubSceneManagerMainGame` back to Menu after MenuLibraryUi / MenuLibraryInfo) and no hooked method is known to run when the config screen is shown again. It now stops early if `SelectCommand` read a row, and tries the details controller last read before `GetOrFind`.
- EXP counter monitor (`WaitForSeconds(0.1)`, also tops up the SDL stream), wall/beacon audio loops, wall-bump check (`WaitForSeconds(0.08)`, allowed movement-sound file; FF1 identical), scroll-message pacing (`WaitForSeconds(scrollTime / (lines + 1))`: the scroll has no per-line event), `DelayedInitialScan` (0.5 s after a scene load: entity cache + audio loops), the mod's own `ConfirmationDialog` / `TextInputWindow` speech delays (no game UI involved), `InputPassthroughPatches` (the game's per-frame input queries: input core), `OnTouchPadCallback` wall-bump prefix (allowed file), `InputManager.IsOnValidMap` (router context; scene scan only on a cache miss, at most every 30 frames).

**Double-fix audit (commit 9e36367 against older paths).**
- Title Options open: `OptionController.ShowConfig` → `stateMachine.Change(16)` → `InitConfig` → `ResetCursor` (0x301710) → `SelectCommand` speaks row 0 synchronously, then the `ShowConfig` initial-focus read spoke it again a frame later. Title Options confirm popup answered No: `PopupClose_Postfix` → `ReannounceAfterPopup`, and the No callback (`<InitConfirmConfig>b__86_1` 0x33ABF0) re-enters the Config state → `SelectCommand`. Fix: `lastRowSpokenFrame` (set by every row read); the initial-focus read, the popup-close re-read and the bestiary-return wait each record the frame they were armed and give up if a row was read in or after it.
- Game-over load popup: the old `UpdateFocus` reader and the generic cursor reader could both run for its cursor; the cursor-identity routing runs first and returns.
- Boarding a ship could be spoken by `ChangeTransportation` and by the `ChangeMoveState` backup (separate dedups): the backup is gone.
- Redundant but silent, kept: `MainMenuController.Show` and `InitNone` both arm the field-menu read (`Show` runs `InitNone` inside it; the `fieldMenuGen` latch keeps one read). Removing the `Show` hook would rely on `Change(0)` always re-running `InitNone`, which is not proven.
- Checked clean: title prompt, title menu reads, field command bars, save list, in-game config open/close, bestiary detail (single `SetData` announcer), shop, battle turn, entity cycling, status "(X of Y)", F1/F3, result-phase EXP stops (idempotent), battle system messages (`GlobalBattleMessageTracker` dedup).

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
SDL3 audio (`Utils/AudioEngine.cs`): one playback device with several `SDL_AudioStream`s bound to it; SDL mixes them. PCM is S16LE stereo 22050 Hz, so no resampling. Per-stream gain (`SDL_SetAudioStreamGain`) applies user volume. Tones are pre-generated at init by `ToneGenerator`; all submission is on the main thread (no audio callback).

| Stream | Purpose |
|--------|---------|
| Movement | Footsteps |
| WallBump | Collision thud |
| WallTone ×4 | One looping stream per direction, topped up from the ~100 ms audio coroutine; SDL sums them |
| Beacon | Panning ping |

**Wall Bump:** `FieldPlayerKeyController.OnTouchPadCallback` prefix captures position. Coroutine waits 0.08s, checks position delta (< 0.1 = wall). Requires 2 consecutive hits to confirm.

**Wall Tone Loop:** 100ms coroutine checks tiles via `MapRouteSearcher.Search()`. Suppresses near exits (`EntityScanner.GetMapExitPositions()`). 1s suppression on map transitions.

**Audio Beacons:** 2s coroutine pings toward entity. Volume scales with distance (max 500 units), pan from X delta. 400Hz north, 280Hz south.

---

## Entity Translation System
Embedded `translation.json` (`{ japaneseKey: { lang: value } }`, 11 languages; `ja` returns the raw name), loaded by `Utils/EntityTranslator.cs` and called from `EntityScanner.GetEntityNameFromProperty()`. 2-tier lookup: exact key, then the key with a leading `N:` / `SC N:` prefix stripped (the prefix is re-attached). There is no suffix stripping, so circled-number variants (`兵士①`) are keyed individually.

**Dump:** Hotkey `0` writes `EntityNames.json` with `{ "MapName": { "JapaneseName": "" } }` structure.

**Offline extraction (2026-09-23).** `tools/extract_entities.py` (Python + UnityPy, generalised from the FF5 mod's tool) sweeps every `map_*_assets_all_*.bundle` under `StreamingAssets/aa/StandaloneWindows64`, walks the Tiled entity JSON (`entity_default` + base64 `inline` groups in each map's `package`), and runs each Japanese label through a Python mirror of the 2-tier lookup above, so `missing` lists only what the mod would really fail on. `gamedict` builds Japanese → {lang} from the game's message tables (`story_cha` speaker names + `system`), so proper nouns follow the game's own localisation per language. `tools/apply_translations.py` validates a batch (11 languages, per-language script checks, no kana) and appends it textually, leaving existing bytes untouched.
- Result: 165 keys added (289 → 454); the sweep reports 670 of 670 unique labels covered. Labels with dev ids such as `sc_e_0010:トパパ` are keyed exactly — the prefix regex does not match the underscore form — and the id is left out of the spoken text.
- Official-name pass: 46 existing entries that are themselves game strings were aligned with the game's text (201 values) — e.g. エリア → Aria (was "Area"), 幻術師/魔界幻士 → Evoker/Summoner (the PR job names), トーザスの抜け道 → Tozus Tunnel, ギガメス → Gigametz. Deliberately left alone: shop words (their official strings are menu headers), 闇の戦士, トード.
- After a game update: `extract_entities.py missing <out>` → translate the keys → `apply_translations.py apply <batch>`.

---

## Multi-hit Damage (2026-09-23)
"Target: NxTotal damage" on weapon attacks, FF1 parity. The game draws its ×N (`BattleBasicFunction.CreateHitCount` → `DamageViewUIManager.CreateHitCount`) only when `SystemConfigData.GetBattleType()` is Command (FF1–FF3 return 1; FF4/FF5 return 0 = ATB) and the acting ability's `TypeId` is 4 (weapon; the Fight command's ability 1). FF3 is turn-based, so the captured ×N path works as in FF1. Fallback when no ×N was paired with the damage view within a frame: the `CreateDamageView` postfix reads the attack's own count from `__instance.ICalcResultDic[target].GetHitCount()` (weapon abilities only; `battleActData` is protected, read at offset 0x28). Default is now "With hit count", stored as `MultiHitDamage` (the old `DamageDisplay` entry is ignored).

---

## Game Code Typos
`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`, `SetCommadnMessage` (not hooked), `curosr_parent`, `StartPreeMptiveMes`, `EnemysInit`, `TargetEnamyList`, `ShowGetAbilitysInit`
