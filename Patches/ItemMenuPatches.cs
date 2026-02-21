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
    internal static class ItemMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.ITEM_MENU, "ItemMenu.Select");

        static ItemMenuState()
        {
            _helper.RegisterResetHandler(() => { LastSelectedItem = null; });
        }

        public static bool IsItemMenuActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
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

            int state = GetWindowControllerState();
            if (state == IL2CppOffsets.Item.STATE_COMMAND_SELECT || state == IL2CppOffsets.Item.STATE_NONE)
            {
                IsItemMenuActive = false;
                return false;
            }

            return true;
        }

        private static int GetWindowControllerState()
        {
            var windowController = GameObjectCache.GetOrFind<KeyInputItemWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
                return -1;
            return StateReaderHelper.ReadStateTag(windowController.Pointer, IL2CppOffsets.Item.OFFSET_STATE_MACHINE);
        }

        private static bool transitionPatchApplied = false;

        public static void ApplyTransitionPatches(HarmonyLib.Harmony harmony)
        {
            if (transitionPatchApplied) return;

            try
            {
                Type controllerType = typeof(KeyInputItemWindowController);
                HarmonyPatchHelper.PatchSetActive(harmony, controllerType, typeof(ItemMenuState),
                    logPrefix: "[Item Menu]");
                HarmonyPatchHelper.PatchSetNextState(harmony, controllerType, typeof(ItemMenuState),
                    logPrefix: "[Item Menu]");
                transitionPatchApplied = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Item Menu] Failed to patch transitions: {ex.Message}");
            }
        }

        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
                IsItemMenuActive = false;
        }

        public static void SetNextState_Postfix(int state)
        {
            if ((state == IL2CppOffsets.Item.STATE_NONE || state == IL2CppOffsets.Item.STATE_COMMAND_SELECT) && IsItemMenuActive)
                IsItemMenuActive = false;
        }

        public static bool ShouldAnnounce(string announcement) => _helper.ShouldAnnounce(announcement);

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
                    return null;
                }

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                {
                    return null;
                }

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null)
                    {
                        if (corps.CharacterId == characterId)
                        {
                            string row = corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                            return row;
                        }
                    }
                }
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
    internal static class ItemListController_SelectContent_Patch
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

                // Build announcement: "Item Name quantity: Description"
                string itemName = TextUtils.StripIconMarkup(itemData.Name);
                int quantity = itemData.Count;
                string announcement = quantity > 0 ? $"{itemName} {quantity}" : itemName;

                // Add description if available
                string description = itemData.Description;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    description = TextUtils.StripIconMarkup(description);
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        announcement += $": {description}";
                    }
                }
                if (string.IsNullOrEmpty(announcement))
                    return;

                // Skip duplicates
                if (!ItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.ITEM_MENU);

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
    internal static class ItemUseController_SelectContent_Patch
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
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.ITEM_MENU);

                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }

}
