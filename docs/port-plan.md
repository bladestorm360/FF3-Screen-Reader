# Port Plan: StatusDetailsReader, MoveStateHelper & VehicleLandingPatches

## Overview

Port three components from ff5-screen-reader to ff3:
1. **StatusDetailsReader** - Full stat navigation for Status screen with arrow keys
2. **MoveStateHelper** - Vehicle/movement state tracking and announcements
3. **VehicleLandingPatches** - "Can land" announcements when airship/ship over valid terrain

---

## 1. StatusDetailsReader Port

### What FF5 Has
- `StatusDetailsReader.cs` - Basic stat reading methods
- `StatusNavigationReader.cs` - Arrow-key navigation through 17 stats in 5 groups
- `StatusNavigationTracker.cs` - Singleton for tracking navigation state
- `StatusDetailsPatches.cs` - Patches for `InitDisplay`, `ExitDisplay`, `SelectContent`

### FF3 Differences

| Aspect | FF5 | FF3 |
|--------|-----|-----|
| MP System | Standard MP (current/max) | Spell charges per level (8 levels) |
| ABP | Yes (job ability points) | No |
| Job Level | Yes | Yes |
| Namespace | `Il2CppSerial.FF5.UI.KeyInput` | `Il2CppSerial.FF3.UI.KeyInput` |
| StatusDetailsController | Has `InitDisplay`, `ExitDisplay` | Has `InitDisplay`, `ExitDisplay` (same) |
| StatusDetailsView | `ExpText`, `NextExpText`, `CurrentAbpText` | `ExpText`, `NextExpText`, `currentJobExpText` |
| AbilityCharaStatusView | `currentMpText`, `maxMpText` | `currentLvMpTextList` (8 levels) |

### FF3 Available Stats

**Character Info Group:**
- Name (from CharacterData)
- Job (via MesIdName localization)
- Level (Parameter.BaseLevel)
- Experience (ExpText from view)
- Job Exp (currentJobExpText from view)

**Vitals Group:**
- HP (currentHP / ConfirmedMaxHp)
- Spell Charges Level 1-8 (currentLvMpTextList)

**Attributes Group:**
- Strength (ConfirmedPower)
- Agility (ConfirmedAgility)
- Vitality (ConfirmedVitality)
- Intellect/Magic (ConfirmedMagic)

**Combat Stats Group:**
- Attack (ConfirmedAttack)
- Defense (ConfirmedDefense)
- Evasion (ConfirmedDefenseCount)
- Magic Defense (ConfirmedAbilityDefense)

### Implementation Plan

**File: `Menus/StatusDetailsReader.cs`** (replace existing stub)
- Port `StatusDetailsReader` class with `SetCurrentCharacterData()`, `ClearCurrentCharacterData()`
- Port `StatusNavigationReader` class for arrow-key navigation
- Port `StatusNavigationTracker` singleton
- Port `StatGroup` enum and `StatusStatDefinition` class
- **Adapt for FF3:**
  - Remove ABP stat (FF5-only)
  - Replace MP reading with Spell Charges summary (e.g., "L1: 5/10, L2: 3/8...")
  - Change namespace references to `Il2CppSerial.FF3.UI.KeyInput`
  - Use FF3's `currentJobExpText` instead of ABP

**File: `Patches/StatusMenuPatches.cs`** (extend existing)
- Add `InitDisplay` and `ExitDisplay` patches to `StatusDetailsController`
- Initialize `StatusNavigationTracker` when entering status details
- Clear tracker when exiting
- Use manual Harmony patching (FF3 requirement)

**File: `Core/FFIII_ScreenReaderMod.cs`** (extend)
- Add hotkey handlers for stat navigation:
  - Up/Down Arrow: Navigate stats (when status details active)
  - Shift+Up/Down: Jump to next/previous stat group

### Key Types (from dump.cs)

```csharp
// Namespace: Serial.FF3.UI.KeyInput
StatusDetailsController : StatusDetailsControllerBase
  - view: StatusDetailsView (0x70)
  - statusController: AbilityCharaStatusController (0x78)
  - InitDisplay() - virtual, slot 21
  - ExitDisplay() - virtual, slot 23
  - SetParameter(OwnedCharacterData data) - virtual, slot 39

StatusDetailsView
  - expText: Text (0x30)
  - nextExpText: Text (0x40)
  - currentJobExpText: Text (0x50)

AbilityCharaStatusView
  - jobNameText: Text (0x20)
  - nameText: Text (0x30)
  - currentHpText: Text (0x48)
  - maxHpText: Text (0x50)
  - currentLvMpTextList: List<Text> (0x70) - 8 spell charge levels
```

---

## 2. MoveStateHelper Port

### What FF5 Has
- `MoveStateHelper.cs` - Static helper for movement state tracking
- `MovementSpeechPatches.cs` - Patches `FieldPlayer.ChangeMoveState` for announcements
- `MoveStateMonitor` - Coroutine for proactive state monitoring

### FF3 MoveState Values (identical to FF5)
```
Walk = 0, Dush = 1, AirShip = 2, Ship = 3, LowFlying = 4, Chocobo = 5, Gimmick = 6, Unique = 7
```

### FF3 Already Has
- `MovementSoundPatches.cs` - Wall bump detection
- Access to `FieldPlayerController.fieldPlayer`
- `GameObjectCache` for component caching

### Implementation Plan

**File: `Utils/MoveStateHelper.cs`** (new)
- Port `MoveStateHelper` static class
- Constants for MoveState values
- Cached state tracking with timeout mechanism
- Methods:
  - `GetCurrentMoveState()` - reads from FieldPlayerController
  - `UpdateCachedMoveState(int)` - called from patches
  - `AnnounceStateChange(int, int)` - speaks state transitions
  - `IsControllingShip()`, `IsRidingChocobo()`, etc.
  - `GetPathfindingMultiplier()` - for EntityScanner scope adjustments

**File: `Patches/MovementSpeechPatches.cs`** (new)
- Manual Harmony patch for `FieldPlayer.ChangeMoveState`
- Postfix that calls `MoveStateHelper.UpdateCachedMoveState()`
- `MoveStateMonitor` class with coroutine for world map monitoring
- **Use manual patching** (FF3 requirement - no attributes)

### Key Types (from dump.cs)

```csharp
// Namespace: Last.Entity.Field
FieldPlayer
  - moveState: FieldPlayerConstants.MoveState (0x1D0)
  - ChangeMoveState(MoveState, bool ignoreStatusSwitchConfirm = false)

// Namespace: Last.Map
FieldPlayerController
  - fieldPlayer: FieldPlayer (0x20)
```

---

## 3. VehicleLandingPatches Port

### What FF5 Has
- `VehicleLandingPatches.cs` - Patches `MapUIManager.SwitchLandable(bool)` to announce "Can land"
- Only announces when transitioning from non-landable to landable (false -> true)
- Uses `MoveStateHelper.IsOnFoot()` to suppress when not in vehicle

### FF3 Has Same Method
```csharp
// Namespace: Last.Map
MapUIManager : SingletonMonoBehaviour<MapUIManager>
  - SwitchLandable(bool landable)
```

### Implementation Plan

**File: `Patches/VehicleLandingPatches.cs`** (new)
- Manual Harmony patch for `MapUIManager.SwitchLandable`
- Track `lastLandableState` to detect transitions
- Only announce when `landable` goes from `false` to `true`
- Use `MoveStateHelper.IsOnFoot()` to skip when walking
- `ResetState()` method to clear when changing maps/vehicles

---

## Integration Points

### EntityScanner Enhancement
- Use `MoveStateHelper.GetPathfindingMultiplier()` to adjust scan radius on world map
- Larger radius when on ship/airship

### Hotkey Additions
| Key | Context | Action |
|-----|---------|--------|
| Up/Down | Status details screen | Navigate stats |
| Shift+Up/Down | Status details screen | Jump stat group |
| V | Field | Announce current vehicle/movement mode |

---

## Implementation Order

1. **MoveStateHelper** (simpler, standalone)
   - Create `Utils/MoveStateHelper.cs`
   - Create `Patches/MovementSpeechPatches.cs`
   - Test vehicle announcements on world map

2. **StatusDetailsReader** (more complex, builds on existing)
   - Replace stub `Menus/StatusDetailsReader.cs` with full implementation
   - Extend `Patches/StatusMenuPatches.cs` with InitDisplay/ExitDisplay patches
   - Add hotkey handlers to main mod
   - Test stat navigation

---

## Risk Areas

1. **Manual Patching Required**: FF3 crashes with `[HarmonyPatch]` attributes - must use manual `harmony.Patch()` calls
2. **ChangeMoveState Signature**: Method takes enum parameter - verify IL2CPP marshaling works
3. **Spell Charges UI**: `currentLvMpTextList` is a List<Text> - need to iterate all 8 levels
4. **State Machine**: May need to validate `StatusDetailsController` state machine like other FF3 menus

---

## Testing Checklist

### MoveStateHelper
- [ ] Walking/dashing announcements work
- [ ] Ship embark/disembark announces "On ship"/"On foot"
- [ ] Chocobo mounting announces correctly
- [ ] Airship announces correctly
- [ ] State doesn't get stuck after transitions

### StatusDetailsReader
- [ ] Entering status details announces basic info
- [ ] Up/Down arrows navigate through stats
- [ ] Shift+Up/Down jumps between groups
- [ ] Exiting status details clears navigation state
- [ ] Spell charges read correctly (all 8 levels)
- [ ] Stats read correct values from Parameter

### VehicleLandingPatches
- [ ] "Can land" announced when airship over valid terrain
- [ ] Not announced when already landed
- [ ] Not announced when on foot
- [ ] State resets properly on map transitions
