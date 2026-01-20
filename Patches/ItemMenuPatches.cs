using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputItemListController = Il2CppLast.UI.KeyInput.ItemListController;
using KeyInputItemUseController = Il2CppLast.UI.KeyInput.ItemUseController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using ItemTargetSelectContentController = Il2CppLast.UI.KeyInput.ItemTargetSelectContentController;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using Condition = Il2CppLast.Data.Master.Condition;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;
using Corps = Il2CppLast.Data.User.Corps;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using KeyInputItemCommandController = Il2CppLast.UI.KeyInput.ItemCommandController;
using KeyInputItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using ItemCommandId = Il2CppLast.Defaine.UI.ItemCommandId;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for item menu announcements.
    /// </summary>
    public static class ItemMenuState
    {
        private const string CONTEXT = "ItemMenu.Select";

        /// <summary>
        /// True when item list or item target selection is active.
        /// Used to suppress generic cursor navigation announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsItemMenuActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.ITEM_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.ITEM_MENU, value);
        }

        /// <summary>
        /// Stores the currently selected item data for 'I' key lookup.
        /// </summary>
        public static ItemListContentData LastSelectedItem { get; set; } = null;

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates state machine at runtime to detect return to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsItemMenuActive)
                return false;

            // Validate we're actually in a submenu, not command bar
            int state = GetWindowControllerState();
            if (state == STATE_COMMAND_SELECT || state == STATE_NONE)
            {
                // At command bar or menu closing - clear state and don't suppress
                ClearState();
                return false;
            }

            return true;
        }

        // State machine offset for ItemWindowController (from debug.md)
        private const int OFFSET_STATE_MACHINE = 0x70;

        /// <summary>
        /// Gets the current state from ItemWindowController's state machine.
        /// Returns -1 if unable to read state.
        /// </summary>
        private static int GetWindowControllerState()
        {
            var windowController = UnityEngine.Object.FindObjectOfType<KeyInputItemWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
                return -1;
            return StateReaderHelper.ReadStateTag(windowController.Pointer, OFFSET_STATE_MACHINE);
        }

        /// <summary>
        /// Clears item menu state when menu is closed.
        /// </summary>
        public static void ClearState()
        {
            IsItemMenuActive = false;
            LastSelectedItem = null;
            AnnouncementDeduplicator.Reset(CONTEXT);
        }

        private static bool transitionPatchApplied = false;

        // ItemWindowController.State enum values (from dump.cs line 456894 - KeyInput version)
        // NOTE: Touch version has different values - make sure to use KeyInput values!
        private const int STATE_NONE = 0;             // Menu closed
        private const int STATE_COMMAND_SELECT = 1;   // Command bar (Use/Key Items/Sort)
        private const int STATE_USE_SELECT = 2;       // Regular item list
        private const int STATE_IMPORTANT_SELECT = 3; // Key items list
        private const int STATE_ORGANIZE_SELECT = 4;  // Organize/Sort mode
        private const int STATE_TARGET_SELECT = 5;    // Character target selection

        /// <summary>
        /// Apply manual Harmony patches for item menu transitions.
        /// </summary>
        public static void ApplyTransitionPatches(HarmonyLib.Harmony harmony)
        {
            if (transitionPatchApplied) return;

            try
            {
                Type controllerType = typeof(KeyInputItemWindowController);

                // Patch SetActive for menu close detection
                HarmonyPatchHelper.PatchSetActive(harmony, controllerType, typeof(ItemMenuState),
                    logPrefix: "[Item Menu]");

                // Patch SetNextState for state transition detection (back to command bar)
                HarmonyPatchHelper.PatchSetNextState(harmony, controllerType, typeof(ItemMenuState),
                    logPrefix: "[Item Menu]");

                transitionPatchApplied = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Item Menu] Failed to patch transitions: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ClearState();
            }
        }

        /// <summary>
        /// Postfix for SetNextState - clears state when returning to command bar.
        /// KeyInput States: None=0 (closed), CommandSelect=1 (command bar), UseSelect=2, ImportantSelect=3, etc.
        /// </summary>
        public static void SetNextState_Postfix(int state)
        {
            MelonLogger.Msg($"[Item Menu] SetNextState called with state={state}, IsItemMenuActive={IsItemMenuActive}");

            // Clear state when returning to command bar (1) or menu closing (0)
            if ((state == STATE_NONE || state == STATE_COMMAND_SELECT) && IsItemMenuActive)
            {
                MelonLogger.Msg($"[Item Menu] Clearing state - transitioning to command bar or closing");
                ClearState();
            }
        }

        public static bool ShouldAnnounce(string announcement)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT, announcement);
        }

        /// <summary>
        /// Gets the row (Front/Back) for a character.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    MelonLogger.Msg($"[ItemMenu] UserDataManager is null");
                    return null;
                }

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                {
                    MelonLogger.Msg($"[ItemMenu] CorpsList is null");
                    return null;
                }

                int characterId = characterData.Id;
                MelonLogger.Msg($"[ItemMenu] Looking for character {characterData.Name} (ID={characterId}) in {corpsList.Count} corps entries");

                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        MelonLogger.Msg($"[ItemMenu] Corps entry: CharId={corps.CharacterId}, CorpsId={corps.Id}");
                        if (corps.CharacterId == characterId)
                        {
                            string row = corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                            MelonLogger.Msg($"[ItemMenu] Found match! {characterData.Name} is in {row}");
                            return row;
                        }
                    }
                }
                MelonLogger.Msg($"[ItemMenu] No matching Corps found for {characterData.Name}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[ItemMenu] Error getting character row: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name for an ItemCommandId.
        /// </summary>
        public static string GetItemCommandName(ItemCommandId commandId)
        {
            switch (commandId)
            {
                case ItemCommandId.Use:
                    return GetLocalizedCommand("$menu_item_use") ?? "Use";
                case ItemCommandId.Organize:
                    return GetLocalizedCommand("$menu_item_organize") ?? "Sort";
                case ItemCommandId.Important:
                    return GetLocalizedCommand("$menu_item_important") ?? "Key Items";
                default:
                    return null;
            }
        }

        private static string GetLocalizedCommand(string mesId)
        {
            return LocalizationHelper.GetText(mesId);
        }

        /// <summary>
        /// Gets a localized condition/status effect name from a Condition object.
        /// </summary>
        public static string GetConditionName(Condition condition)
        {
            if (condition == null)
                return null;

            return LocalizationHelper.GetText(condition.MesIdName, stripMarkup: false);
        }
    }

    /// <summary>
    /// Patch for item list selection.
    /// Announces item name: description when navigating items in the menu.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputItemListController), "SelectContent",
        new Type[] {
            typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>),
            typeof(int),
            typeof(GameCursor),
            typeof(CustomScrollViewWithinRangeType)
        })]
    public static class ItemListController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            KeyInputItemListController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData> targets,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                if (targets == null)
                    return;

                // NOTE: Don't set IsItemMenuActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                // Convert IEnumerable to List for indexed access
                var targetList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(targets);
                if (targetList == null || targetList.Count == 0)
                    return;

                if (index < 0 || index >= targetList.Count)
                    return;

                var itemData = targetList[index];
                if (itemData == null)
                    return;

                // Store selected item for 'I' key lookup
                ItemMenuState.LastSelectedItem = itemData;

                // Build announcement: "Item Name: Description"
                string announcement = AnnouncementBuilder.FormatWithDescription(itemData.Name, itemData.Description);
                if (string.IsNullOrEmpty(announcement))
                    return;

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsItemMenuActive = true;

                MelonLogger.Msg($"[Item Menu] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemListController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for character target selection when using an item.
    /// Announces character name, HP, and status effects.
    /// </summary>
    [HarmonyPatch(typeof(KeyInputItemUseController), "SelectContent",
        new Type[] {
            typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController>),
            typeof(GameCursor)
        })]
    public static class ItemUseController_SelectContent_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(
            KeyInputItemUseController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> targetContents,
            GameCursor targetCursor)
        {
            try
            {
                if (targetCursor == null || targetContents == null)
                    return;

                // NOTE: Don't set IsItemMenuActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                int index = targetCursor.Index;

                // Convert to list for indexed access
                var contentList = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(targetContents);
                if (contentList == null || contentList.Count == 0)
                    return;

                if (index < 0 || index >= contentList.Count)
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
                // Note: Row is intentionally NOT included for item targeting - only for status/equip menus
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

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Item");
                ItemMenuState.IsItemMenuActive = true;

                MelonLogger.Msg($"[Item Target] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }

}
