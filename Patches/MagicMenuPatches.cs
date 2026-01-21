using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;
using Il2CppInterop.Runtime;

// Type aliases for IL2CPP types
using AbilityCommandController = Il2CppSerial.FF3.UI.KeyInput.AbilityCommandController;
using AbilityContentListController = Il2CppSerial.FF3.UI.KeyInput.AbilityContentListController;
using AbilityWindowController = Il2CppSerial.FF3.UI.KeyInput.AbilityWindowController;
using AbilityUseContentListController = Il2CppSerial.FF3.UI.KeyInput.AbilityUseContentListController;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using AbilityCommandId = Il2CppLast.Defaine.UI.AbilityCommandId;
using AbilityType = Il2CppLast.Defaine.Master.AbilityType;
using AbilityLevelType = Il2CppLast.Defaine.Master.AbilityLevelType;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;
using GameCursor = Il2CppLast.UI.Cursor;
using WithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for magic menu with proper suppression pattern.
    /// </summary>
    public static class MagicMenuState
    {
        // Track last announced spell ID to prevent duplicates
        private static int lastSpellId = -1;

        // Cache the current character for charge lookup
        private static OwnedCharacterData _currentCharacter = null;

        /// <summary>
        /// True when spell list has focus (SetFocus(true) was called).
        /// Only suppresses GenericCursor when spell list is actually focused.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsSpellListActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.MAGIC_MENU);
            private set => MenuStateRegistry.SetActive(MenuStateRegistry.MAGIC_MENU, value);
        }

        /// <summary>
        /// Called when SetFocus(true) is received - spell list gained focus.
        /// </summary>
        public static void OnSpellListFocused()
        {
            // Clear other menu states to prevent conflicts
            FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Magic");
            IsSpellListActive = true;
            lastSpellId = -1; // Reset to announce first spell
        }

        /// <summary>
        /// Called when SetFocus(false) is received - spell list lost focus.
        /// </summary>
        public static void OnSpellListUnfocused()
        {
            IsSpellListActive = false;
            lastSpellId = -1;
            _currentCharacter = null;
        }

        /// <summary>
        /// The current character for charge lookups.
        /// </summary>
        public static OwnedCharacterData CurrentCharacter
        {
            get => _currentCharacter;
            set => _currentCharacter = value;
        }

        /// <summary>
        /// Returns true when spell list should suppress generic cursor reading.
        /// Validates state machine at runtime to detect return to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsSpellListActive)
                return false;

            // Validate we're actually in a submenu, not command bar
            int state = GetWindowControllerState();
            if (state == STATE_COMMAND || state == 0)
            {
                // At command bar or menu closing - clear state and don't suppress
                ResetState();
                return false;
            }

            return true;
        }

        // AbilityWindowController.State enum values (from dump.cs line 281758)
        public const int STATE_USE_LIST = 1;      // Use mode spell list
        public const int STATE_MEMORIZE_LIST = 3; // Learn mode spell list
        public const int STATE_REMOVE_LIST = 4;   // Remove mode spell list
        private const int STATE_COMMAND = 7;       // Command menu state

        // State machine offset for AbilityWindowController
        private const int OFFSET_STATE_MACHINE = 0x88;

        /// <summary>
        /// Gets the current state from AbilityWindowController's state machine.
        /// Returns -1 if unable to read state.
        /// </summary>
        public static int GetWindowControllerState()
        {
            var windowController = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
                return -1;
            return StateReaderHelper.ReadStateTag(windowController.Pointer, OFFSET_STATE_MACHINE);
        }

        /// <summary>
        /// Checks if spell should be announced (changed from last).
        /// </summary>
        public static bool ShouldAnnounceSpell(int spellId)
        {
            if (spellId == lastSpellId)
                return false;
            lastSpellId = spellId;
            return true;
        }

        /// <summary>
        /// Resets all tracking state.
        /// </summary>
        public static void ResetState()
        {
            IsSpellListActive = false;
            lastSpellId = -1;
            _currentCharacter = null;
        }

        /// <summary>
        /// Gets localized spell name from OwnedAbility.
        /// </summary>
        public static string GetSpellName(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            return LocalizationHelper.GetText(ability.MesIdName);
        }

        /// <summary>
        /// Gets localized spell description from OwnedAbility.
        /// </summary>
        public static string GetSpellDescription(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            return LocalizationHelper.GetText(ability.MesIdDescription);
        }

        /// <summary>
        /// Gets spell level (1-8) from OwnedAbility.
        /// </summary>
        public static int GetSpellLevel(OwnedAbility ability)
        {
            if (ability == null)
                return 0;

            try
            {
                var abilityData = ability.Ability;
                if (abilityData != null)
                {
                    return abilityData.AbilityLv;
                }
            }
            catch { }

            return 0;
        }

        /// <summary>
        /// Gets current and max charges for a given spell level.
        /// FF3 uses per-level charges shared across all magic types.
        /// </summary>
        public static (int current, int max) GetChargesForLevel(OwnedCharacterData character, int level)
        {
            if (character == null || level <= 0 || level > 8)
                return (0, 0);

            try
            {
                var param = character.Parameter as PlayerCharacterParameter;
                if (param == null)
                    return (0, 0);

                // Get current charges from dictionary
                int current = 0;
                var currentList = param.CurrentMpCountList;
                if (currentList != null && currentList.ContainsKey(level))
                {
                    current = currentList[level];
                }

                // Get max charges using the method that includes bonuses
                int max = 0;
                try
                {
                    max = param.ConfirmedMaxMpCount((AbilityLevelType)level);
                }
                catch
                {
                    // Fallback to base max if ConfirmedMaxMpCount fails
                    var baseMaxList = param.BaseMaxMpCountList;
                    if (baseMaxList != null && baseMaxList.ContainsKey(level))
                    {
                        max = baseMaxList[level];
                    }
                }

                return (current, max);
            }
            catch { }

            return (0, 0);
        }
    }

    /// <summary>
    /// Patches for magic menu using manual Harmony patching.
    /// Uses SetCursor to detect navigation, then finds focused content by iterating.
    /// </summary>
    public static class MagicMenuPatches
    {
        private static bool isPatched = false;

        // Memory offsets from dump.cs (Serial.FF3.UI.KeyInput.AbilityContentListController)
        private const int OFFSET_DATA_LIST = 0x38;         // List<OwnedAbility> dataList (spells for Use/Remove)
        private const int OFFSET_ABILITY_ITEM_LIST = 0x40; // List<OwnedItemData> abilityItemList (spell tomes for Learn)
        private const int OFFSET_CONTENT_LIST = 0x60;      // List<BattleAbilityInfomationContentController> contentList
        private const int OFFSET_TARGET_CHARACTER = 0x98;  // OwnedCharacterData targetCharacterData

        // Memory offset from dump.cs (Serial.FF3.UI.KeyInput.AbilityWindowController)
        // Note: "isLearnigItem" is a typo in the game code
        private const int OFFSET_IS_LEARNING_ITEM = 0xA8;  // bool isLearnigItem (true = Learn mode)

        // AbilityWindowController.State enum values (from dump.cs line 281758)
        private const int STATE_COMMAND = 7;  // Command menu state (used by SetNextState_Postfix)

        /// <summary>
        /// Apply manual Harmony patches for magic menu.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                // Patch UpdateController to track when spell list is actively handling input
                TryPatchUpdateController(harmony);

                // Patch SetCursor for navigation detection (only announces when focused)
                TryPatchSetCursor(harmony);

                // Patch window controller to clear flag when transitioning to command menu
                TryPatchWindowController(harmony);

                // Patch SetActive for menu close detection
                TryPatchSetActive(harmony);

                // Patch target selection for spell use (character selection when casting)
                TryPatchTargetSelect(harmony);

                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Magic Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityContentListController.UpdateController to track when spell list is active.
        /// Called when spell list starts handling input.
        /// Signature: UpdateController(bool isCheckAbility, bool isBrowseOnly, bool canPageSkip)
        /// </summary>
        private static void TryPatchUpdateController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                // Use AccessTools for consistent IL2CPP method access
                MethodInfo updateControllerMethod = AccessTools.Method(controllerType, "UpdateController",
                    new[] { typeof(bool), typeof(bool), typeof(bool) });

                if (updateControllerMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(UpdateController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateControllerMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityContentListController.UpdateController for state tracking");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] UpdateController method not found on AbilityContentListController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch UpdateController: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityContentListController.SetCursor - called during navigation.
        /// We'll find the focused content by iterating through contentList.
        /// </summary>
        private static void TryPatchSetCursor(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityContentListController);

                // Use AccessTools for IL2CPP private method access
                MethodInfo setCursorMethod = AccessTools.Method(controllerType, "SetCursor",
                    new[] { typeof(GameCursor), typeof(bool), typeof(WithinRangeType) });

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityContentListController.SetCursor for spell navigation");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] SetCursor method not found on AbilityContentListController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetCursor: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityWindowController.SetNextState to detect state transitions.
        /// When transitioning to Command state, clear the spell list flag.
        /// </summary>
        private static void TryPatchWindowController(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityWindowController);

                // Use AccessTools for IL2CPP private method access
                // SetNextState takes State enum but Harmony handles enum-to-int in postfix
                MethodInfo setNextStateMethod = AccessTools.Method(controllerType, "SetNextState");

                if (setNextStateMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityWindowController.SetNextState for state transitions");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] SetNextState method not found on AbilityWindowController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch SetNextState: {ex.Message}");
            }
        }

        /// <summary>
        /// Patches AbilityWindowController.SetActive for menu close detection.
        /// </summary>
        private static void TryPatchSetActive(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(AbilityWindowController),
                typeof(MagicMenuPatches), logPrefix: "[Magic Menu]");
        }

        /// <summary>
        /// Patches AbilityUseContentListController.SetCursor for target selection.
        /// Called when navigating between characters when casting a spell.
        /// </summary>
        private static void TryPatchTargetSelect(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(AbilityUseContentListController);

                // Find SetCursor(Cursor) - private method called during navigation
                MethodInfo setCursorMethod = AccessTools.Method(controllerType, "SetCursor", new[] { typeof(GameCursor) });

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(AbilityUseSetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Magic Menu] Patched AbilityUseContentListController.SetCursor for target selection");
                }
                else
                {
                    MelonLogger.Warning("[Magic Menu] SetCursor method not found on AbilityUseContentListController");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Failed to patch target selection: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                MagicMenuState.ResetState();
            }
        }

        /// <summary>
        /// Postfix for AbilityUseContentListController.SetCursor.
        /// Announces character name, HP, and status when selecting spell target.
        /// </summary>
        public static unsafe void AbilityUseSetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                if (__instance == null || targetCursor == null)
                    return;

                var controller = ((Il2CppSystem.Object)__instance).TryCast<AbilityUseContentListController>();
                if (controller == null)
                    return;

                int index = targetCursor.Index;
                if (index < 0)
                    return;

                // Read contentList from offset 0x50 (List<ItemTargetSelectContentController>)
                IntPtr instancePtr = controller.Pointer;
                IntPtr contentListPtr = *(IntPtr*)((byte*)instancePtr + 0x50);
                if (contentListPtr == IntPtr.Zero)
                    return;

                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(contentListPtr);
                if (contentList == null || contentList.Count == 0)
                    return;

                if (index >= contentList.Count)
                    return;

                var content = contentList[index];
                if (content == null)
                    return;

                // Get character data from the content controller
                var characterData = content.CurrentData;
                if (characterData == null)
                    return;

                // Build announcement: "Character Name, HP current/max, Status effects"
                // Note: Level is NOT included for target selection - only for pre-menus
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                // Add HP and status information (no level for target selection)
                var parameter = characterData.Parameter;
                if (parameter != null)
                {
                    // Add HP and status conditions via helper
                    string statusInfo = CharacterStatusHelper.GetFullStatus(parameter);
                    if (!string.IsNullOrEmpty(statusInfo))
                    {
                        announcement += statusInfo;
                    }
                }

                // Skip duplicates using deduplicator
                if (!AnnouncementDeduplicator.ShouldAnnounce("MagicTarget", announcement))
                    return;

                MelonLogger.Msg($"[Magic Target] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AbilityUseSetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for AbilityWindowController.SetNextState - detects state transitions.
        /// Clears spell list flag when:
        /// - Transitioning to Command state (7) = back to command bar
        /// - Transitioning to None state (0) = menu closing
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                MelonLogger.Msg($"[Magic Menu] SetNextState called with state={state}, IsSpellListActive={MagicMenuState.IsSpellListActive}");

                // Clear state when returning to command bar (7) or menu closing (0)
                if ((state == STATE_COMMAND || state == 0) && MagicMenuState.IsSpellListActive)
                {
                    MelonLogger.Msg($"[Magic Menu] Clearing state - transitioning to command bar or closing");
                    MagicMenuState.OnSpellListUnfocused();
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for UpdateController - called when spell list is actively handling input.
        /// This fires every frame while the spell list is active, so we use it to set the active flag.
        /// State clearing is now handled by SetNextState_Postfix instead of state machine validation.
        /// </summary>
        public static void UpdateController_Postfix(object __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                // COMMENTED OUT: State machine validation - clearing now handled by SetNextState_Postfix
                // The raw pointer state machine reading was unreliable.
                // int currentState = GetWindowControllerState();
                // if (currentState == STATE_COMMAND || currentState == 0)
                // {
                //     // At command bar or menu closing - ensure flag is cleared
                //     if (MagicMenuState.IsSpellListActive)
                //     {
                //         MagicMenuState.OnSpellListUnfocused();
                //     }
                //     return;
                // }

                // Spell list is actively handling input - enable spell reading
                if (!MagicMenuState.IsSpellListActive)
                {
                    MagicMenuState.OnSpellListFocused();
                }

                // Cache character data for charge lookups
                try
                {
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr != IntPtr.Zero)
                    {
                        unsafe
                        {
                            IntPtr charPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TARGET_CHARACTER);
                            if (charPtr != IntPtr.Zero)
                            {
                                MagicMenuState.CurrentCharacter = new OwnedCharacterData(charPtr);
                            }
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for SetCursor - fires during navigation.
        /// Uses cursor index to get correct spell/item from content list.
        /// Handles both Use/Remove (spells) and Learn (spell tomes) modes.
        /// </summary>
        public static void SetCursor_Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                // Only process if spell list is focused
                if (!MagicMenuState.IsSpellListActive)
                    return;

                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as AbilityContentListController;
                if (controller == null || !controller.gameObject.activeInHierarchy)
                    return;

                int cursorIndex = targetCursor.Index;
                MelonLogger.Msg($"[Magic Menu] SetCursor called, index: {cursorIndex}");

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                // Use state machine to determine which list to read from
                // State 3 (MemorizeList) = Learn mode → abilityItemList
                // State 1 (UseList) or 4 (RemoveList) = Use/Remove mode → contentList
                int state = MagicMenuState.GetWindowControllerState();
                MelonLogger.Msg($"[Magic Menu] Current state: {state}");

                if (state == MagicMenuState.STATE_MEMORIZE_LIST)
                {
                    // Learn mode - use abilityItemList (spell tomes)
                    if (TryAnnounceFromItemList(controllerPtr, cursorIndex))
                    {
                        MelonLogger.Msg("[Magic Menu] Announced from abilityItemList (Learn mode)");
                    }
                    else
                    {
                        MelonLogger.Msg("[Magic Menu] Learn mode: abilityItemList empty or invalid");
                    }
                }
                else if (state == MagicMenuState.STATE_USE_LIST || state == MagicMenuState.STATE_REMOVE_LIST)
                {
                    // Use/Remove mode - use contentList (owned spells)
                    if (TryAnnounceFromContentList(controllerPtr, cursorIndex))
                    {
                        MelonLogger.Msg("[Magic Menu] Announced from contentList (Use/Remove mode)");
                    }
                    else
                    {
                        MelonLogger.Msg("[Magic Menu] Use/Remove mode: contentList empty or invalid");
                    }
                }
                else
                {
                    MelonLogger.Msg($"[Magic Menu] Unknown state {state}, trying both lists");
                    // Fallback: try contentList first, then abilityItemList
                    if (!TryAnnounceFromContentList(controllerPtr, cursorIndex))
                    {
                        TryAnnounceFromItemList(controllerPtr, cursorIndex);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in SetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Tries to announce spell from contentList (Use/Remove mode).
        /// Returns true if contentList has valid data at the index.
        /// </summary>
        private static bool TryAnnounceFromContentList(IntPtr controllerPtr, int index)
        {
            try
            {
                // Read contentList pointer at offset 0x60
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                    return false;

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (contentList.Count == 0)
                    return false;

                if (index < 0 || index >= contentList.Count)
                    return false;

                var contentController = contentList[index];
                if (contentController == null)
                {
                    AnnounceEmpty();
                    return true; // Valid list, just empty slot
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    AnnounceEmpty();
                    return true; // Valid list, just empty slot
                }

                AnnounceSpell(ability);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in TryAnnounceFromContentList: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tries to announce spell tome from abilityItemList (Learn mode).
        /// Returns true if abilityItemList has valid data at the index.
        /// </summary>
        private static bool TryAnnounceFromItemList(IntPtr controllerPtr, int index)
        {
            try
            {
                // Read abilityItemList pointer at offset 0x40
                IntPtr itemListPtr;
                unsafe
                {
                    itemListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_ABILITY_ITEM_LIST);
                }

                if (itemListPtr == IntPtr.Zero)
                    return false;

                var itemList = new Il2CppSystem.Collections.Generic.List<OwnedItemData>(itemListPtr);

                if (itemList.Count == 0)
                    return false;

                if (index < 0 || index >= itemList.Count)
                    return false;

                var itemData = itemList[index];
                if (itemData == null)
                {
                    AnnounceEmpty();
                    return true; // Valid list, just empty slot
                }

                // Get item name and description directly from OwnedItemData
                // Note: "Deiscription" is a typo in the game code
                string description = null;
                try
                {
                    description = itemData.Deiscription;
                }
                catch { }

                string announcement = AnnouncementBuilder.FormatWithDescription(itemData.Name, description);
                if (string.IsNullOrEmpty(announcement))
                {
                    AnnounceEmpty();
                    return true;
                }

                if (!MagicMenuState.ShouldAnnounceSpell(announcement.GetHashCode()))
                    return true; // Valid but deduplicated

                MelonLogger.Msg($"[Magic Menu] Learn announcing: {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in TryAnnounceFromItemList: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Announces spell at the given index from contentList (Use/Remove mode).
        /// </summary>
        private static void AnnounceSpellAtIndex(IntPtr controllerPtr, int index)
        {
            try
            {
                // Read contentList pointer at offset 0x60
                IntPtr contentListPtr;
                unsafe
                {
                    contentListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_CONTENT_LIST);
                }

                if (contentListPtr == IntPtr.Zero)
                {
                    MelonLogger.Msg("[Magic Menu] contentList is null");
                    return;
                }

                var contentList = new Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController>(contentListPtr);

                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Msg($"[Magic Menu] Index {index} out of range (count={contentList.Count})");
                    return;
                }

                var contentController = contentList[index];
                if (contentController == null)
                {
                    // Empty slot
                    AnnounceEmpty();
                    return;
                }

                var ability = contentController.Data;
                if (ability == null)
                {
                    // Empty slot
                    AnnounceEmpty();
                    return;
                }

                AnnounceSpell(ability);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpellAtIndex: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces spell tome at the given index from abilityItemList (Learn mode).
        /// Format: "Spell Name: Description" (like items menu)
        /// </summary>
        private static void AnnounceSpellTome(IntPtr controllerPtr, int index)
        {
            try
            {
                // Read abilityItemList pointer at offset 0x40
                IntPtr itemListPtr;
                unsafe
                {
                    itemListPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_ABILITY_ITEM_LIST);
                }

                if (itemListPtr == IntPtr.Zero)
                {
                    MelonLogger.Msg("[Magic Menu] abilityItemList is null");
                    return;
                }

                var itemList = new Il2CppSystem.Collections.Generic.List<OwnedItemData>(itemListPtr);

                if (index < 0 || index >= itemList.Count)
                {
                    MelonLogger.Msg($"[Magic Menu] Learn index {index} out of range (count={itemList.Count})");
                    return;
                }

                var itemData = itemList[index];
                if (itemData == null)
                {
                    AnnounceEmpty();
                    return;
                }

                // Get item name and description directly from OwnedItemData
                // Note: "Deiscription" is a typo in the game code
                string description = null;
                try
                {
                    description = itemData.Deiscription; // Game code typo
                }
                catch { }

                // Build announcement: "Spell Tome Name: Description"
                string announcement = AnnouncementBuilder.FormatWithDescription(itemData.Name, description);
                if (string.IsNullOrEmpty(announcement))
                {
                    AnnounceEmpty();
                    return;
                }

                // Check for duplicate
                if (!MagicMenuState.ShouldAnnounceSpell(announcement.GetHashCode()))
                    return;

                MelonLogger.Msg($"[Magic Menu] Learn announcing: {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in AnnounceSpellTome: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces "Empty" for empty spell slots.
        /// </summary>
        private static void AnnounceEmpty()
        {
            if (MagicMenuState.ShouldAnnounceSpell(-1)) // -1 as ID for empty
            {
                MelonLogger.Msg("[Magic Menu] Announcing: Empty");
                FFIII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
            }
        }

        /// <summary>
        /// Announces a spell with name, MP, and description.
        /// Format: "Spell Name: MP: current/max. Description"
        /// </summary>
        private static void AnnounceSpell(OwnedAbility ability)
        {
            try
            {
                // Get spell ID for deduplication
                int spellId = 0;
                int spellLevel = 0;
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        spellId = abilityData.Id;
                        spellLevel = abilityData.AbilityLv;
                    }
                }
                catch
                {
                    return;
                }

                // Check if spell changed (ID-based deduplication)
                if (!MagicMenuState.ShouldAnnounceSpell(spellId))
                    return;

                // Get spell name
                string spellName = MagicMenuState.GetSpellName(ability);
                if (string.IsNullOrEmpty(spellName))
                    return;

                // Build announcement: "Spell Name: charges/max. Description"
                string announcement = spellName;

                // Add charges if we have character data
                if (MagicMenuState.CurrentCharacter != null && spellLevel > 0)
                {
                    var (current, max) = MagicMenuState.GetChargesForLevel(MagicMenuState.CurrentCharacter, spellLevel);
                    if (max > 0)
                    {
                        announcement += $": MP: {current}/{max}";
                    }
                }

                // Add description
                string description = MagicMenuState.GetSpellDescription(ability);
                if (!string.IsNullOrEmpty(description))
                {
                    announcement += $". {description}";
                }

                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch { }
        }
    }
}
