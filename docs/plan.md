# FF3 Screen Reader - Project Plan

Screen reader accessibility mod for Final Fantasy III Pixel Remaster. TTS announcements for menus, navigation, dialogue, and battle.

**Reference:** `ff5-screen-reader` (similar architecture)

---

## Status

| Component | Status |
|-----------|--------|
| Core & Dialogue | DONE |
| Title/Config/Status Menu | DONE |
| Item/Magic/Job Menu | DONE |
| Save/Load & New Game Naming | DONE |
| Battle System & Pause Menu | DONE |
| Field Navigation & Entity Scanner | DONE |
| Shops & Vehicles | DONE |
| Popups & Map Transitions | DONE |
| NPC Event Item Selection | DONE |
| External Sound Player | DONE |
| Entity Translation | DONE |

---

## FF3-Specific Features

- **Dialogue:** Per-page via `PlayingInit`, multi-line pages, speaker tracking
- **Story Text:** Per-line via `LineFadeMessageWindowController`
- **Job Menu:** Job name, level, "Equipped" indicator
- **Magic:** 8-level MP system: "Spell: MP: X/Y. Description"
- **Vehicles:** `GetOn()`/`GetOff()` patches, landing via `ShowLandingGuide(bool)`
- **Entity Scanner:** VehicleTypeMap from `Transportation.ModelList`, event-driven
- **Entity Translation:** Embedded Japanese→11-language dictionary with prefix stripping, untranslated dump. All 670 map labels covered via the offline extractor (`tools/`, 2026-09-23); proper nouns use the game's official names per language
- **Sound Player:** SDL3 audio (`AudioEngine`): one device, several bound `SDL_AudioStream`s (movement, wall bump, four wall-tone directions, beacon) mixed by SDL
- **Wall Tones:** Looping directional tones, suppressed at exits/doors and map transitions
- **Footsteps:** Click on each tile change, polled from `InputManager.Update` (`MovementSoundPatches.PollFootsteps`), so the cadence follows walk/dash; silent in vehicles and off the field. Wall bumps stay coroutine-based
- **Audio Beacons:** Periodic pings with distance-based volume/panning

---

## Hotkeys

Field-only keys are silent off the field (menus, battle). Full player-facing list: `readme.md`.

| Key | Action |
|-----|--------|
| J / [ | Previous entity |
| L / ] | Next entity |
| Shift+J/L, Shift+[/], - / = | Cycle entity category |
| Shift+K | Back to All category |
| K | Announce entity name |
| P / \ | Directions to entity (beacon on: restart beacon) |
| Shift+P / Shift+\ | Toggle pathfinding filter |
| Ctrl+P / Ctrl+\ | Toggle layer filter |
| Shift+M | Toggle map exit filter |
| ` (backtick) | Force entity rescan |
| , / . | Previous / next waypoint |
| Shift+, / Shift+. | Waypoint category |
| / | Directions to waypoint |
| Shift+/ | Add waypoint (in the browsed category) |
| Ctrl+. | Rename waypoint |
| Ctrl+/ | Delete waypoint |
| Ctrl+Shift+/ | Clear all waypoints on map |
| Ctrl+Arrows | Teleport beside selected entity |
| ; | Toggle wall tones |
| ' | Toggle footsteps |
| F6 / 9 | Toggle audio beacons |
| V | Vehicle/movement mode |
| M | Map name |
| G | Gil |
| H | Battle: active character HP + conditions |
| R | Repeat last dialogue page (status screen: repeat stat) |
| I | Details (description, stats, job requirements, config/mod-menu help) |
| Shift+I | Controls shown on screen (config controls pop-up is a navigable list) |
| U | Unlocked jobs that can equip the focused equipment |
| 0 | Dump untranslated entity names |
| Up/Down (or W/S) | Navigate stats (status screen, bestiary entry, controls pop-up) |
| Shift+Up/Down | Jump stat group |
| Ctrl+Up/Down | First/last stat |
| F1 / F3 | Game's walk/run and encounter toggles; the new state is announced |
| F5 | Enemy HP display (Numbers/Percentage/Hidden) |
| F7 | Toggle Auto Detail |
| F8 | Mod menu (field only) |
| Tab | Clears a stale battle state (opening the main menu proves the battle ended); ignored while a battle is live |

---

## FF1 Parity Pass (2026-09)

Ported from the FF1 mod; FF3-only features (job menu, event item select, game over, capacity, 0-key dump) unchanged. Items marked *unverified in game* are hooked on dump signatures only and need a runtime check.

| Area | Change |
|------|--------|
| Battle state | `BattleStateHelper` owns in-battle state: set on battle start, cleared on `BattleController` fade-out / `Exit`, title menu, scene load and Tab (only when no `BattleController` exists). *Unverified in game:* fade-out/Exit timing |
| Battle turns | "{0}'s turn" per actor turn window; action messages "Actor: Action", filtered by action name (language independent) instead of English text |
| Battle targets | Initial target read on `EnemysInit`/`PlayerInit`, also when re-entering targeting on the same target; HP by `EnemyHPDisplay`; no suppression while targeting |
| Damage | Miss / damage / multi-hit / MP damage / HP and MP recovery localized; value-0 events silent except a true zero result ("0 damage", open-issues pass) |
| Battle results | All level-ups and job level-ups announced; job level-ups read with the points screen (`ShowPointsInit`, which inlines `ShowJobProficiencyLevelUp`); stat gains fall back to Confirmed* values; EXP tone stops on ability screens |
| Battle item/magic | Descriptions follow Auto Detail; I key reads the last focused description |
| H key | Active actor HP + conditions only |
| Menus | Field main menu and title menu read the focused command on open; save list reads focused slot; popups read the focused button and re-announce the menu on close |
| Config | Focused row + position read via `ConfigActualDetails.SelectCommand`; config opens read the first row; controls pop-up is a navigable list with controller glyphs named |
| Shop | Command/list/trade window rewritten: price, stats, quantity and total; first item no longer re-read on entry/exit. `ShopMagicTargetSelectController` is unreachable in FF3 (see the open-issues pass below): spells are sold as items |
| Bestiary | Stat buffer via `NavigationBuffer` (groups, Ctrl/Shift jumps), name entry, localized labels |
| Mod menu | Every item has a description (I key); EXP counter off stops a playing tone; Auto Detail default on for new installs |
| Title | "Press any button" spoken when the prompt text is actually shown: event hooks since the open-issues pass below (was a bounded wait from `InitializeTitle` / Title scene load) |
| Scroll messages | Multi-line scroll text spoken line by line, paced by the game's scrollTime, never interrupting; same text re-spoken if it recurs after 2 s |
| Waypoints | New waypoints filed under the browsed category; category names localized |
| Entities | Map exit dedupe re-applied on toggle; unreachable entities skipped when cycling with the pathfinding filter; directions and step counts localized |
| Robustness | Field hotkeys self-heal the player cache (rescan on a miss at most every 30 frames); scene loads reset battle/config/key-help/location state; `SpeakText` strips rich-text tags and logs `[TTS]` |
| Localization | Every `T()` key present in `mod_text.json` (12 languages); row, level, HP, bestiary, new-game naming strings localized |

---

## Open-issues pass (2026-09-23, session 2)

Fixes for the FF3 items in `OPEN_ISSUES.md`. **None of this is verified in game yet.** Technical detail: `debug.md`, section of the same name.

| Area | Change | Status |
|------|--------|--------|
| Status screen | "(X of Y)" is the stat's position within its group (FF1 parity), via `NavigationBuffer.CurrentGroupPosition` | not yet verified in game |
| Title prompt | "Press any button" from event hooks: `TitleWindowController.Initialize` keeps the window, `SystemIndicator.Hide` (called by the None state right before it shows the prompt) triggers a one-frame check. No timed wait, no scene search; the return from the Extras (shortcut state) stays silent | not yet verified in game |
| Magic shop | `ShopMagicTargetSelectController` is never reached in FF3 (needs Ability-type products; FF3 sells spells as items), so spells go through the normal list and quantity window | verified offline |
| Battle value-0 views | A true zero result (`HitType.Zero`) reads "Target: 0 damage"; a value-0 view logs `[Battle] value-0 view: ...`. Status cures stay silent: FF3 reports them as Hit/0 like other effects (see debug.md) | not yet verified in game |
| Bestiary | Entry name read with interrupt (fast flipping reads only the latest); stats located through the controller, and if they can't be read the name is still spoken | not yet verified in game |
| Performance | Title/field command counts use the controller kept at menu open (no scan per cursor move); popup close on a config screen re-reads the row next frame without the 10 s search; config cursor suppression no longer scans on the title Options; entity cycling scans once | not yet verified in game |
| Rule fixes | `BattleSystemMessagePatches` takes strings positionally; its `SystemMessageWindowView.SetMessage` hook is class-checked (body shared with 46 setters); Touch `ShowSkillLevelsInit` and the EXP-tone catch-all are manual patches (catch-all moved from the shared `EndWaitInit` to `ResultMenuController.Close`) | not yet verified in game |
| Localization | Entity types and fallback names, vehicle names, equipment-slot and magic-command fallbacks, 0-key dump messages and controller button words use `T()`; map-exit "→" separator repaired | not yet verified in game |
| Controller | Unplugging in mod mode no longer locks the keyboard out; a mod menu opened with F8 takes the controller (D-pad/stick drive the menu) | not yet verified in game |

---

## Architecture

Post-refactoring file organization (~70 C# files):

| Directory | Purpose | Key Files |
|-----------|---------|-----------|
| `Core/` | Main mod, input, audio, preferences, waypoints | `FFIII_ScreenReaderMod.cs`, `InputManager.cs`, `AudioLoopManager.cs`, `PreferencesManager.cs`, `WaypointController.cs`, `GameInfoAnnouncer.cs` |
| `Core/Filters/` | Entity filter interface and implementations | `IEntityFilter.cs`, `PathfindingFilter.cs`, `ToLayerFilter.cs`, `CategoryFilter.cs` |
| `Field/` | Entity scanning, navigation, pathfinding | `EntityScanner.cs`, `NavigableEntity.cs`, `WaypointEntity.cs`, `FieldNavigationHelper.cs`, `FilterContext.cs` |
| `Menus/` | Menu text readers | `ConfigMenuReader.cs`, `ShopCommandReader.cs`, `StatusDetailsReader.cs`, `SaveSlotReader.cs` |
| `Patches/` | All Harmony patches by game system | `BattleCommandPatches.cs`, `PopupPatches.cs`, `MessageWindowPatches.cs`, `MapTransitionPatches.cs`, etc. |
| `Utils/` | Shared utilities | `IL2CppOffsets.cs`, `MenuStateRegistry.cs`, `MenuStateHelper.cs`, `GameObjectCache.cs`, `SoundPlayer.cs`, `AnnouncementDeduplicator.cs` |

---

## Known Issues

| Issue | Description |
|-------|-------------|
| Secret passages | Opened passages are not reflected by the pathfinder |
| Auto Detail default | New installs default ON; an existing saved `AutoDetail=false` is kept (F7 / mod menu to change) |
| Status cures in battle | A status cure (Antidote etc.) is silent: FF3 gives it the same hit type and value (Hit, 0) as other non-damage effects, so the damage view can't tell it apart. Needs a condition-removal hook (user decision) |
| Config language names | Shown in their own language / English by design |

---

## Dependencies

- MelonLoader (net6.0)
- Tolk.dll + screen reader (NVDA recommended)
- FF3 assemblies: `D:\Games\SteamLibrary\steamapps\common\Final Fantasy III PR`

---

## References

| Document | Contents |
|----------|----------|
| `debug.md` | Technical implementation, memory offsets, resolved issues |
| `dump.cs` | Game class/method signatures |
