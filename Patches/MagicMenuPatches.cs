using System;
using System.Linq;
using System.Reflection;
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
        // Track if spell list has focus (SetFocus(true) was called)
        private static bool _isSpellListFocused = false;

        // Track last announced spell ID to prevent duplicates
        private static int lastSpellId = -1;

        // Cache the current character for charge lookup
        private static OwnedCharacterData _currentCharacter = null;

        /// <summary>
        /// True when spell list has focus (SetFocus(true) was called).
        /// Only suppresses GenericCursor when spell list is actually focused.
        /// </summary>
        public static bool IsSpellListActive
        {
            get => _isSpellListFocused;
        }

        /// <summary>
        /// Called when SetFocus(true) is received - spell list gained focus.
        /// </summary>
        public static void OnSpellListFocused()
        {
            // Clear other menu states to prevent conflicts
            FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Magic");
            _isSpellListFocused = true;
            lastSpellId = -1; // Reset to announce first spell
        }

        /// <summary>
        /// Called when SetFocus(false) is received - spell list lost focus.
        /// </summary>
        public static void OnSpellListUnfocused()
        {
            _isSpellListFocused = false;
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
        /// Check if GenericCursor should be suppressed.
        /// Only suppresses when spell list is actually focused AND we're in a list state.
        /// Uses state machine validation to check actual game state.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!_isSpellListFocused)
                return false;

            try
            {
                // Check the AbilityWindowController's state machine
                // If we're in Command state (7), don't suppress even if flag is set
                var windowController = UnityEngine.Object.FindObjectOfType<AbilityWindowController>();
                if (windowController != null)
                {
                    int currentState = GetCurrentState(windowController);
                    if (currentState == STATE_COMMAND)
                    {
                        // We're in command menu, not spell list - clear flag and don't suppress
                        ResetState();
                        return false;
                    }
                }

                // Validate the spell list controller is actually active
                var controller = UnityEngine.Object.FindObjectOfType<AbilityContentListController>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    // Controller gone - reset state
                    ResetState();
                    return false;
                }
                return true;
            }
            catch
            {
                ResetState();
                return false;
            }
        }

        // Offsets for reading state machine (from dump.cs)
        private const int OFFSET_WINDOW_STATE_MACHINE = 0x88;  // AbilityWindowController.stateMachine
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10; // StateMachine<T>.current (after Il2CppObject header)
        private const int OFFSET_STATE_TAG = 0x10;             // State<T>.Tag (after Il2CppObject header)
        private const int STATE_COMMAND = 7;                   // AbilityWindowController.State.Command

        /// <summary>
        /// Reads the current state from AbilityWindowController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(AbilityWindowController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x88
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_WINDOW_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
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
            _isSpellListFocused = false;
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

            try
            {
                string mesId = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedName))
                            return TextUtils.StripIconMarkup(localizedName);
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Gets localized spell description from OwnedAbility.
        /// </summary>
        public static string GetSpellDescription(OwnedAbility ability)
        {
            if (ability == null)
                return null;

            try
            {
                string mesId = ability.MesIdDescription;
                if (!string.IsNullOrEmpty(mesId))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedDesc = messageManager.GetMessage(mesId, false);
                        if (!string.IsNullOrWhiteSpace(localizedDesc))
                            return TextUtils.StripIconMarkup(localizedDesc);
                    }
                }
            }
            catch { }

            return null;
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
        private const int OFFSET_IS_ITEM_LIST_CHECK = 0x29; // bool IsItemListCheck (true = Learn mode)
        private const int OFFSET_TARGET_CHARACTER = 0x98;  // OwnedCharacterData targetCharacterData

        // AbilityWindowController.State enum values (from dump.cs line 281758)
        private const int STATE_COMMAND = 7;  // Command menu state

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

                // Find UpdateController(bool, bool, bool)
                MethodInfo updateControllerMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "UpdateController")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1)
                        {
                            updateControllerMethod = method;
                            break;
                        }
                    }
                }

                if (updateControllerMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(UpdateController_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateControllerMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
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

                // Find SetCursor(Cursor, bool, WithinRangeType)
                MethodInfo setCursorMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetCursor")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            setCursorMethod = method;
                            break;
                        }
                    }
                }

                if (setCursorMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetCursor_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setCursorMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
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

                // Find SetNextState method (private method that takes State enum)
                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name == "SetNextState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            setNextStateMethod = method;
                            break;
                        }
                    }
                }

                if (setNextStateMethod != null)
                {
                    var postfix = typeof(MagicMenuPatches).GetMethod(nameof(SetNextState_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for AbilityWindowController.SetNextState - detects state transitions.
        /// When transitioning to Command state (7), clear the spell list flag.
        /// </summary>
        public static void SetNextState_Postfix(object __instance, int state)
        {
            try
            {
                // Check if transitioning to Command state
                if (state == STATE_COMMAND && MagicMenuState.IsSpellListActive)
                {
                    MagicMenuState.OnSpellListUnfocused();
                }
            }
            catch { }
        }

        /// <summary>
        /// Postfix for UpdateController - called when spell list is actively handling input.
        /// This fires every frame while the spell list is active, so we use it to set the active flag.
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

                // Check if we're in Learn mode (spell tomes) or Use/Remove mode (spells)
                bool isLearnMode = false;
                unsafe
                {
                    isLearnMode = *(bool*)((byte*)controllerPtr.ToPointer() + OFFSET_IS_ITEM_LIST_CHECK);
                }

                if (isLearnMode)
                {
                    // Learn mode - read from abilityItemList (spell tomes)
                    AnnounceSpellTome(controllerPtr, cursorIndex);
                }
                else
                {
                    // Use/Remove mode - read from contentList (spells)
                    AnnounceSpellAtIndex(controllerPtr, cursorIndex);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Magic Menu] Error in SetCursor_Postfix: {ex.Message}");
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
                string itemName = null;
                string description = null;

                try
                {
                    itemName = itemData.Name;
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        itemName = TextUtils.StripIconMarkup(itemName);
                    }

                    description = itemData.Deiscription; // Game code typo
                    if (!string.IsNullOrEmpty(description))
                    {
                        description = TextUtils.StripIconMarkup(description);
                    }
                }
                catch { }

                if (string.IsNullOrEmpty(itemName))
                {
                    AnnounceEmpty();
                    return;
                }

                // Build announcement: "Spell Tome Name: Description"
                string announcement = itemName;
                if (!string.IsNullOrEmpty(description))
                {
                    announcement += ": " + description;
                }

                // Check for duplicate
                if (!MagicMenuState.ShouldAnnounceSpell(itemName.GetHashCode()))
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
        /// Announces a spell with name, charges, and description.
        /// Format: "Spell Name: charges/max charges. Description"
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
                        announcement += $": {current}/{max} charges";
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
