# FF3 Screen Reader - Architecture & Reference

## Critical Constraints

### Harmony Patching (FF3-Specific)
| Approach | Result |
|----------|--------|
| `[HarmonyPatch]` attributes | Crashes on startup |
| Manual Harmony patches | Works |
| Methods with string params | Crashes (IL2CPP marshaling) |

**Solution:** Use manual `HarmonyLib.Harmony` patching only. Patch methods WITHOUT string parameters. Read data from instance fields in postfixes using `object __instance` cast to IL2CPP type.

### IL2CPP Rules
```csharp
// WRONG - .NET reflection doesn't work
var prop = obj.GetType().GetProperty("Prop");
var value = prop.GetValue(obj);  // Always null!

// CORRECT - Direct IL2CPP access
var cast = obj.TryCast<TargetType>();
var value = cast.Property;  // Works
```

- Use `TryCast<T>()` for type conversions
- **ALWAYS** use `AccessTools.Method()` for patching methods (not `Type.GetMethod()`) - standard reflection fails on private IL2CPP methods
- Read fields via pointer offsets when properties don't work

---

## Utility Classes

Use the appropriate utility for each scenario to avoid code duplication:

| Class | Purpose | When to Use |
|-------|---------|-------------|
| `AnnouncementDeduplicator` | Exact-match deduplication | Menu items, battle commands, any case where the exact same text shouldn't repeat |
| `LocationMessageTracker` | Containment-based deduplication | Map transitions only - checks if "Altar Cave" is in "Entering Altar Cave" |
| `TextUtils` | Text manipulation | Stripping icon markup, formatting text |
| `MoveStateHelper` | Vehicle/movement state | Tracking on-foot vs ship/airship/chocobo state |
| `GameObjectCache` | Component caching | Avoiding expensive FindObjectOfType calls |
| `CoroutineManager` | Managed coroutines | Frame-delayed operations with cleanup |

### Deduplication Guidelines

1. **Exact-match (same text twice)** → Use `AnnouncementDeduplicator.ShouldAnnounce(context, text)`
2. **Map transition ("Entering X" followed by "X")** → Use `LocationMessageTracker`
3. **Cross-type deduplication** → Use local `lastX` variable (e.g., `lastScrollMessage` in ScrollMessagePatches dedupes across fade/line fade/scroll)

**Never add new `lastAnnounced*` variables** - use `AnnouncementDeduplicator` with a unique context key instead.

---

## Key Namespaces

| Namespace | Purpose |
|-----------|---------|
| `Last.Message` | Dialogue/message windows |
| `Last.UI.KeyInput` | Keyboard/gamepad UI controllers |
| `Last.Data.User` | Player/character data |
| `Last.Data.Master` | Master data (items, jobs, etc.) |
| `Last.Battle` | Battle utilities (includes `BattleUtility.GetJobLevel()`) |
| `Serial.FF3.UI.KeyInput` | FF3-specific UI (jobs, abilities) |

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

### Save/Load Controllers
| Controller | savePopup/commonPopup Offset |
|------------|------------------------------|
| LoadGameWindowController | 0x58 |
| LoadWindowController | 0x28 |
| SaveWindowController | 0x28 (commonPopup: 0x38) |

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
Command=7
```

### ShopController.State
```
None=0, SelectCommand=1, SelectProduct=2, SelectSellItem=3
SelectAbilityTarget=4, SelectEquipment=5, ConfirmationBuyItem=6
```

---

## Key Types Reference

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
OwnedCharacterData.OwnedJobDataList
BattleUtility.GetJobLevel(OwnedCharacterData)  // For job level calculation
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
Each menu state class has `ShouldSuppress()` returning the flag. State clearing via `SetActive(bool)` transition patches:
```csharp
public static bool ShouldSuppress() => IsActive;

public static void SetActive_Postfix(bool isActive) {
    if (!isActive) MyMenuState.ClearState();
}
```

Add `activeInHierarchy` validation when state might persist after menu closes.

### Popup Detection
Patch base `Popup.Open()`, use `TryCast<T>()` for type detection (GetType().Name returns "Popup" in IL2CPP).

### Battle State Clearing
Battle menu flags cleared centrally via `BattleResultPatches.ClearAllBattleMenuFlags()` at victory screen. Submenu states validate `BattleCommandSelectController` state machine to detect return to command menu.

---

## Working Solutions

### Title Screen "Press Any Button"
`SplashController.InitializeTitle()` stores text, `SystemIndicator.Hide()` speaks it when loading indicator hides.

### Save/Load Popups
Patch `SetPopupActive(bool)` on `LoadGameWindowController`, `LoadWindowController`, `SaveWindowController`. Enum parameters crash like strings.

### Config Menu State
Validate UI visibility via `activeInHierarchy` - title screen uses different controller than main menu.

**Value Change Announcements:** Two patches handle config menu:
- `SetFocus` - announces "Setting: Value" on up/down navigation (setting selection)
- `SwitchArrowSelectTypeProcess` - announces just "Value" on left/right (value change)

When left/right changes a value, `SetFocus` also fires (same setting re-focused). To prevent duplicate announcements, `SetFocus` tracks the last setting name and returns early if unchanged - letting `SwitchArrowSelectTypeProcess` handle value-only announcements. (Fixed 2026-01-19)

### Equipment Job Requirements ('I' Key)
`UserDataManager.ReleasedJobs` → `Weapon/Armor.EquipJobGroupId` → `JobGroup.Job{N}Accept` → `Job.MesIdName`

### Shop Content System
`Content.TypeId` = ContentType (1=Item, 2=Weapon, 3=Armor), `Content.TypeValue` = master data ID.

### Vehicle Transitions
Patch `FieldPlayer.GetOn(int typeId, ...)` and `GetOff(int typeId, ...)`. Parameter is `MapConstants.TransportationType` enum.

### NPC Detection
Use `TryCast<FieldNonPlayer>()` - NPCs don't contain "Chara" in type name.

### Map Transition Announcements
`CheckMapTransition()` in main mod polls `UserDataManager.CurrentMapId` each frame. When map ID changes, announces "Entering {mapName}" using `MapNameResolver.GetCurrentMapName()`. `LocationMessageTracker` prevents duplicate announcements by checking if the subsequent `FadeMessageManager.Play()` location text (e.g., "Altar Cave") is contained in the transition message (e.g., "Entering Altar Cave"). Also suppresses location-like text (1-4 words, no punctuation) when opening menus.

---

## Known Game Code Typos

`SetSpeker`, `Deiscription`, `FieldTresureBox`, `totarlPriceText`, `UpdateCotroller`, `Genelate`, `Infomation`, `SetCommadnMessage`

---

## RESOLVED: Battle System Messages (2026-01-19)

### Problem
After PerformanceIssues.md Phase 1 & 2 restructuring, battle system messages stopped being announced:
- "The party escaped!"
- "Back Attack!"
- "Preemptive Attack!"
- "Ambush!"

### Solution
**Hook:** `BattleUIManager.SetCommadnMessage(string messageId)` (note game code typo)

**Implementation:** `Patches/BattleStartPatches.cs:BattleUIManager_SetCommadnMessage_Postfix`
```csharp
var messageManager = MessageManager.Instance;
string message = messageManager.GetMessage(messageId);
string clean = TextUtils.StripIconMarkup(message);
if (AnnouncementDeduplicator.ShouldAnnounce("BattleSystemMessage", clean))
    FFIII_ScreenReaderMod.SpeakText(clean, interrupt: false);
```

**Key Message IDs:**
| Message ID | Text |
|------------|------|
| `MSG_SYSTEM_020` | "The party escaped!" |
| `MSG_SYSTEM_188` | Individual flee messages ("X flees") |

**Why other hooks didn't work:** Battle system messages use `SetCommadnMessage`, not `FadeMessageManager`, `SystemMessageWindow*`, or other message display systems. The compile-time `typeof()` approach works correctly - the issue was that only debug logging existed, not actual announcement logic.

**Runtime vs Compile-time:** No difference for this case. Both `typeof(BattleUIManager)` and runtime `FindType()` successfully patch the method. The patch runs only when battle messages display (event-driven, not per-frame).

---

## RESOLVED: Vehicle Interior Map Name Duplication (2026-01-19)

### Problem
Vehicle interior maps (e.g., The Invincible airship) announced doubled names: "Entering The Invincible The Invincible" on map transition and "The Invincible The Invincible" on M key press.

### Cause
`MapNameResolver.TryResolveMapNameById()` concatenates `areaName` + `mapTitle`. For vehicle interiors, both resolve to the same localized string (e.g., "The Invincible"), causing duplication.

### Solution
**File:** `Field/MapNameResolver.cs:143-149`

Added check to skip redundant mapTitle when it equals areaName:
```csharp
if (!string.IsNullOrEmpty(areaName) && !string.IsNullOrEmpty(mapTitle))
{
    // Skip redundant mapTitle if it equals areaName (e.g., vehicle interiors)
    if (mapTitle == areaName)
        return areaName;
    return $"{areaName} {mapTitle}";
}
```

**Note:** Initial suspicion was MovementSpeechPatches having duplicate handlers, but the root cause was in map name resolution - a single handler calling a method that returned doubled text.

### Additional Fix: Duplicate Location Announcements
Vehicle interiors also triggered `SystemMessageController.SetMessage(MSG_LOCATION_*)` and `SystemMessageManager.SetMessage(MSG_LOCATION_*)` which announced the location name separately from `CheckMapTransition`'s "Entering X" announcement.

**Solution:** In `BattleMessagePatches.cs`, added check to skip `MSG_LOCATION_*` messages in both:
- `SystemMessageController_SetMessage_Postfix`
- `SystemMessageManager_SetMessage_Postfix`

Uses `LocationMessageTracker.ShouldAnnounceFadeMessage()` - the same tracker already used for `FadeMessageManager`.

---

## RESOLVED: New Game Screen Navigation (2026-01-19)

### Problem
After performance refactor to remove per-frame polling, new game character naming screen stopped reading:
- Character slot navigation (arrow keys between slots 1-4)
- Name cycling (up/down through suggested names)

### Cause
Code patched **Touch** controller versions, but keyboard navigation uses **KeyInput** controllers with different methods:

| Controller | Touch Method (patched) | KeyInput Method (needed) |
|------------|----------------------|----------------------|
| CharacterContentListController | `SetFocusContent(int)` | `SetTargetSelectContent(int)` |
| NameContentListController | `SetForcusIndex(int)` | `SetFocus(int)` |

The Touch methods were successfully patched but never called during keyboard navigation.

### Solution
**File:** `Patches/NewGameNamingPatches.cs`

1. Changed imports to KeyInput versions:
```csharp
using CharacterContentListController = Il2CppLast.UI.KeyInput.CharacterContentListController;
using NameContentListController = Il2CppLast.UI.KeyInput.NameContentListController;
```

2. Patched correct event-driven methods (both private, use AccessTools):
```csharp
// Character slot navigation
AccessTools.Method(charaListType, "SetTargetSelectContent", new[] { typeof(int) });

// Name cycling
AccessTools.Method(nameListType, "SetFocus", new[] { typeof(int) });
```

### Key Insight
When patching IL2CPP games with Touch/KeyInput controller variants:
- Touch controllers often have public methods for touch events
- KeyInput controllers have different (often private) methods for keyboard events
- Patches may succeed on Touch versions but never fire if game uses KeyInput for navigation

---

## RESOLVED: New Game Suggested Name Announcement (2026-01-19)

### Problem
When pressing the suggested name button on a character slot, the randomly assigned name was not announced.

### Cause
Initial attempts to patch `CharacterContentController.SetData` failed - the method wasn't being called when the name button was pressed. The name is set via `SetCharacterName(string, bool)` which has a string parameter (crashes in IL2CPP).

### Solution
**File:** `Patches/NewGameNamingPatches.cs`

Hook `CharacterContentListController.UpdateView(List<NewGameSelectData>)` which fires when the view refreshes after name assignment:
```csharp
// Track last known name per slot
private static string[] lastSlotNames = new string[4];
private static int lastTargetIndex = -1;

// In UpdateView_Postfix: detect name changes
string currentName = GetCharacterSlotNameOnly(__instance, currentSlot);
if (!string.IsNullOrEmpty(currentName) && currentName != lastSlotNames[currentSlot])
{
    lastSlotNames[currentSlot] = currentName;
    FFIII_ScreenReaderMod.SpeakText(currentName);
}
```

**Key:** Use `lastTargetIndex` set by `SetTargetSelectContent_Postfix` to know current slot - reading from instance memory offset was unreliable.

---

## RESOLVED: New Game Start Confirmation Popup (2026-01-19)

### Problem
"Start the game with these names?" popup didn't read when selecting Done with all characters named. Other popups (Return to title, Must name all characters) worked fine.

### Cause
`NewGamePopup` extends `MonoBehaviour`, NOT `Popup`. The base `Popup.Open()` patch doesn't catch it.

### Solution
**File:** `Patches/NewGameNamingPatches.cs`

Patch `NewGameWindowController.InitStartPopup()` which fires when entering the StartPopup state:
```csharp
// CRITICAL: Use correct namespace - Serial.FF3.UI.KeyInput, NOT Last.UI.KeyInput
using NewGamePopup = Il2CppSerial.FF3.UI.KeyInput.NewGamePopup;

public static void InitStartPopup_Postfix(object __instance)
{
    var controller = ((Il2CppSystem.Object)__instance).TryCast<NewGameWindowController>();
    var popupProp = AccessTools.Property(typeof(NewGameWindowController), "popup");
    var popupObj = popupProp.GetValue(controller);
    var popup = ((Il2CppSystem.Object)popupObj).TryCast<NewGamePopup>();
    string message = popup.Message;
    FFIII_ScreenReaderMod.SpeakText(message);
}
```

### Key Insight
When TryCast fails silently, check the namespace. The log showed `Il2CppSerial.FF3.UI.KeyInput.NewGamePopup` but code imported `Il2CppLast.UI.KeyInput.NewGamePopup` - two different classes in different namespaces with the same name.

---

## RESOLVED: Battle Action Deduplication Too Aggressive (2026-01-19)

### Problem
When two enemies of the same name attacked in succession (e.g., "Minotaur attacks" followed by another Minotaur attacking), only the first attack was announced. The second attack's message was deduplicated, leaving only damage values announced.

### Cause
`ParameterActFunctionManagment_CreateActFunction_Patch` used `GlobalBattleMessageTracker.TryAnnounce()` which performs **text-based exact-match deduplication**. Two Minotaurs attacking both generate "Minotaur attacks" - identical text that gets deduplicated.

### Solution
**File:** `Patches/BattleMessagePatches.cs:143`

Changed from text-based to **object-based deduplication** using `BattleActData` reference:
```csharp
// BEFORE (text-based - caused issue)
GlobalBattleMessageTracker.TryAnnounce(announcement, "BattleAction");

// AFTER (object-based - each BattleActData is unique per action)
if (AnnouncementDeduplicator.ShouldAnnounce("BattleAction", battleActData))
{
    MelonLogger.Msg($"[BattleAction] {announcement}");
    FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
}
```

### Key Insight
Battle actions should use object-based deduplication because each `BattleActData` instance is unique per game action. Text-based deduplication is appropriate for menus and UI elements where the same text appearing twice is a true duplicate, but not for battle actions where different entities can legitimately perform identical-looking actions.

---

## RESOLVED: Magic Menu Target Selection (2026-01-20)

### Problem
When using a spell outside of battle (e.g., Cure from Magic menu), navigating between character targets was silent. Item menu targeting worked correctly.

### Cause
Magic targeting uses `AbilityUseContentListController` (namespace: `Serial.FF3.UI.KeyInput`), not `ItemUseController`. Initial patch on `SelectContentByIndex(int)` never fired because that method isn't called during cursor navigation.

### Solution
**File:** `Patches/MagicMenuPatches.cs`

Patch `AbilityUseContentListController.SetCursor(Cursor)` instead:
```csharp
MethodInfo setCursorMethod = AccessTools.Method(controllerType, "SetCursor", new[] { typeof(GameCursor) });
```

Read character data from `contentList` at offset 0x50:
```csharp
IntPtr contentListPtr = *(IntPtr*)((byte*)instancePtr + 0x50);
var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(contentListPtr);
var characterData = contentList[targetCursor.Index].CurrentData;
```

### Announcement Format
| Context | Format |
|---------|--------|
| Pre-menu (Magic/Status/Equip) | "CharName, Job, Level X, Row, HP" |
| Target selection (Items/Magic) | "CharName, HP, status" (no level) |
| Spell menus (in/out of battle) | "SpellName: MP: X/Y. Description" |

**Key:** Use `ConfirmedLevel()` not `BaseLevel` for pre-menu announcements.

---

## RESOLVED: NPC Dialogue Deduplication Fix (2026-01-20)

### Problem
When talking to the same NPC twice and they speak identical dialogue, the second interaction produced no speech output. The deduplication persisted across separate NPC conversations.

### Cause
`FFIII_ScreenReaderMod.cs:1129` checks `lastDialogueMessage` to prevent duplicate announcements within a conversation, but the variable was never cleared when dialogue ended.

### Investigation Results
- **Single output path confirmed**: Only `line 1202` outputs NPC dialogue via `pendingDialogueText`
- **No double-announcement evidence**: Log shows single `[Dialogue]` and `[SPEECH]` per conversation
- **Deduplication purpose**: Guards against `SetContent` being called multiple times within a single conversation

### Solution
**File:** `Core/FFIII_ScreenReaderMod.cs`

Patch `MessageWindowManager.Close()` to clear dialogue state:
```csharp
public static void Close_Postfix()
{
    lastDialogueMessage = "";
    pendingDialogueText = "";
}
```

**Hook:** `MessageWindowManager.Close()` (no parameters, IL2CPP-safe)
**Patch:** Added via `HarmonyPatchHelper.PatchMethod()` in `TryPatchDialogue()`

---

## IMPLEMENTED: Event-Driven Entity Refresh (2026-01-20)

### Problem
When opening a treasure chest, the entity scanner's cached `NavigableEntity` retained the old `isOpen=false` state until the next periodic scan (previously every 5 seconds).

### Solution
Replaced periodic 5-second timer with event-driven refresh hooks:

| Hook | Method | Trigger |
|------|--------|---------|
| Treasure chest opened | `FieldTresureBox.Open()` | Chest interaction |
| Dialogue ends | `MessageWindowManager.Close()` | NPC interaction complete |

### Implementation
**File:** `Core/FFIII_ScreenReaderMod.cs`

```csharp
// Event-driven refresh with 1-frame delay
internal void ScheduleEntityRefresh()
{
    CoroutineManager.StartManaged(EntityRefreshCoroutine());
}

private IEnumerator EntityRefreshCoroutine()
{
    yield return null;  // Wait 1 frame for game state update
    entityScanner.ScanEntities();
}

// Postfix patches call ScheduleEntityRefresh()
public static void TreasureBox_Open_Postfix()
{
    FFIII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
}
```

### Key Points
- **No polling**: Coroutine only runs when hooks trigger
- **1-frame delay**: Ensures game state fully updates before rescan
- **One-shot**: Each trigger runs scan once, then completes
- Removed `ENTITY_SCAN_INTERVAL` (5s timer) and `lastEntityScanTime`
- Kept fallback: `RefreshEntitiesIfNeeded()` still scans if entity list is empty

---

## RESOLVED: Battle Pause Menu Reading (2026-01-20)

### Problem
Battle pause menu (spacebar during battle) with Resume/Controls/Return to Title options was not being announced. `BattleCommandState` was suppressing cursor navigation even when pause menu was open.

### Solution
**Files:** `Patches/BattlePausePatches.cs`, `Core/CursorSuppressionCheck.cs`

**Approach:** Direct memory read (no hookable method fires reliably at runtime).

Multiple patch attempts failed - methods exist but are never called:
- `BattlePauseController.SetEnablePauseMenu(bool)` - private, never fires
- `BattlePauseView.SetEnablePauseRoot(bool)` - public, never fires
- `BattlePauseController.UpdateSelect()` - public, never fires
- `BattlePauseController.UpdateFocus()` - private, never fires

1. `BattlePauseState.IsActive` reads game memory directly:
```csharp
public static bool IsActive
{
    get
    {
        var uiManager = BattleUIManager.Instance;
        if (uiManager == null) return false;

        IntPtr uiManagerPtr = uiManager.Pointer;
        IntPtr pauseControllerPtr = Marshal.ReadIntPtr(uiManagerPtr + 0x90);
        if (pauseControllerPtr == IntPtr.Zero) return false;

        byte isActive = Marshal.ReadByte(pauseControllerPtr + 0x71);
        return isActive != 0;
    }
}
```

2. `CursorSuppressionCheck.Check()` bypasses when pause menu active:
```csharp
// First check - before other battle suppressions
if (BattlePauseState.IsActive)
    return SuppressionResult.None;
```

3. Generic cursor navigation + `MenuTextDiscovery` reads the pause menu items.

### Key Offsets
| Class | Field | Offset |
|-------|-------|--------|
| BattleUIManager | pauseController | 0x90 |
| BattlePauseController | isActivePauseMenu | 0x71 |
| BattlePauseController | selectCommandCursor | 0x40 |
| BattlePauseController | commandMessageIdList | 0x30 |

### Future Consideration
May revisit to find a hookable method instead of direct memory reading. The game likely shows/hides the pause menu via Unity's `SetActive()` on GameObjects, which bypasses the controller methods we tried to patch.
