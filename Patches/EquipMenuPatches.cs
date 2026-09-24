using System;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

// Type aliases for IL2CPP types
using KeyInputEquipmentInfoWindowController = Il2CppLast.UI.KeyInput.EquipmentInfoWindowController;
using KeyInputEquipmentSelectWindowController = Il2CppLast.UI.KeyInput.EquipmentSelectWindowController;
using KeyInputEquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using EquipmentInfoContentView = Il2CppLast.UI.KeyInput.EquipmentInfoContentView;
using EquipSlotType = Il2CppLast.Defaine.EquipSlotType;
using EquipUtility = Il2CppLast.Systems.EquipUtility;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Helper for equipment menu announcements.
    /// </summary>
    internal static class EquipMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.EQUIP_MENU, "EquipMenu.Select");

        static EquipMenuState()
        {
            _helper.RegisterResetHandler(() => { LastFocusedDescription = null; });
        }

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }

        /// <summary>
        /// Stripped description of the focused slot's equipped item or list item, refreshed on every
        /// announce regardless of Auto Detail. Read on demand by the I key / right stick up.
        /// </summary>
        public static string LastFocusedDescription { get; set; }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates state machine at runtime to detect return to command bar.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            int state = GetWindowControllerState();
            if (state == IL2CppOffsets.Equipment.STATE_COMMAND || state == IL2CppOffsets.Equipment.STATE_NONE)
            {
                IsActive = false;
                return false;
            }

            return true;
        }

        public static bool ShouldAnnounce(string announcement) => _helper.ShouldAnnounce(announcement);

        public static string GetSlotName(EquipSlotType slot)
        {
            try
            {
                string messageId = EquipUtility.GetSlotMessageId(slot);
                string localizedName = LocalizationHelper.GetText(messageId, stripMarkup: false);
                if (!string.IsNullOrEmpty(localizedName))
                    return localizedName;
            }
            catch
            {
                // Fall through to defaults
            }

            // Fallback slot names (mod text) when the game's slot message is unavailable
            return slot switch
            {
                EquipSlotType.Slot1 => T("Right Hand"),
                EquipSlotType.Slot2 => T("Left Hand"),
                EquipSlotType.Slot3 => T("Head"),
                EquipSlotType.Slot4 => T("Body"),
                EquipSlotType.Slot5 => T("Accessory"),
                EquipSlotType.Slot6 => T("Accessory 2"),
                _ => string.Format(T("Slot {0}"), (int)slot)
            };
        }

        private static bool transitionPatchApplied = false;

        public static int GetWindowControllerState()
        {
            var windowController = GameObjectCache.GetOrFind<KeyInputEquipmentWindowController>();
            if (windowController == null || !windowController.gameObject.activeInHierarchy)
                return -1;
            return StateReaderHelper.ReadStateTag(windowController.Pointer, IL2CppOffsets.Equipment.OFFSET_STATE_MACHINE);
        }

        public static void ApplyTransitionPatches(HarmonyLib.Harmony harmony)
        {
            if (transitionPatchApplied) return;

            try
            {
                Type controllerType = typeof(KeyInputEquipmentWindowController);
                HarmonyPatchHelper.PatchSetActive(harmony, controllerType, typeof(EquipMenuState),
                    logPrefix: "[Equipment Menu]");
                HarmonyPatchHelper.PatchSetNextState(harmony, controllerType, typeof(EquipMenuState),
                    logPrefix: "[Equipment Menu]");

                // Slot list and item list navigation (manual: attribute patches crash on IL2CPP).
                // KeyInput EquipmentInfoWindowController.SelectContent(Cursor) 0x56F260 and
                // EquipmentSelectWindowController.SelectContent(Cursor, WithinRangeType) 0x573C80, unique.
                HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputEquipmentInfoWindowController), "SelectContent",
                    typeof(EquipmentInfoWindowController_SelectContent_Patch), nameof(EquipmentInfoWindowController_SelectContent_Patch.Postfix),
                    "[Equipment Menu]", new[] { typeof(GameCursor) });
                HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputEquipmentSelectWindowController), "SelectContent",
                    typeof(EquipmentSelectWindowController_SelectContent_Patch), nameof(EquipmentSelectWindowController_SelectContent_Patch.Postfix),
                    "[Equipment Menu]", new[] { typeof(GameCursor), typeof(CustomScrollViewWithinRangeType) });
                transitionPatchApplied = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Equipment Menu] Failed to patch transitions: {ex.Message}");
            }
        }

        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
                IsActive = false;
        }

        /// <summary>
        /// SetNextState(State state), positional. Its body (0x40BA70) is shared with 12 other int setters,
        /// so the native class is checked first.
        /// </summary>
        public static void SetNextState_Postfix(KeyInputEquipmentWindowController __instance, int __0)
        {
            if (!HarmonyPatchHelper.IsNativeInstanceOf<KeyInputEquipmentWindowController>(__instance)) return;
            if ((__0 == IL2CppOffsets.Equipment.STATE_NONE || __0 == IL2CppOffsets.Equipment.STATE_COMMAND) && IsActive)
                IsActive = false;
        }
    }

    /// <summary>
    /// Patch for equipment slot selection (Menu 2).
    /// EquipmentInfoWindowController.SelectContent is called when navigating between equipment slots.
    /// Uses same approach as FF5 - direct contentList access. Registered in ApplyTransitionPatches.
    /// </summary>
    internal static class EquipmentInfoWindowController_SelectContent_Patch
    {
        /// <summary>SelectContent(Cursor targetCursor), positional.</summary>
        public static void Postfix(KeyInputEquipmentInfoWindowController __instance, GameCursor __0)
        {
            try
            {
                GameCursor targetCursor = __0;
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (targetCursor == null) return;

                int index = targetCursor.Index;

                // Access contentList directly (IL2CppInterop exposes private fields)
                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0)
                {
                    MelonLogger.Warning("[Equipment Slot] contentList is null or empty");
                    return;
                }

                if (index < 0 || index >= contentList.Count)
                {
                    MelonLogger.Warning($"[Equipment Slot] Index {index} out of range (count={contentList.Count})");
                    return;
                }

                var contentView = contentList[index];
                if (contentView == null)
                {
                    MelonLogger.Warning("[Equipment Slot] Content view is null");
                    return;
                }

                // Get slot name from partText (like FF5)
                string slotName = null;
                if (contentView.partText != null)
                {
                    slotName = contentView.partText.text;
                }

                // Fallback to localized slot name if partText is empty
                if (string.IsNullOrWhiteSpace(slotName))
                {
                    EquipSlotType slotType = contentView.Slot;
                    slotName = EquipMenuState.GetSlotName(slotType);
                }

                // Get equipped item from Data property
                string equippedItem = null;
                string description = null;
                var itemData = contentView.Data;
                if (itemData != null)
                {
                    try
                    {
                        equippedItem = itemData.Name;

                        // Add parameter message (ATK +12, DEF +5, etc.)
                        string paramMsg = itemData.ParameterMessage;
                        if (!string.IsNullOrWhiteSpace(paramMsg))
                        {
                            equippedItem += ", " + paramMsg;
                        }

                        description = itemData.Deiscription; // game typo
                    }
                    catch { }
                }

                // Build announcement
                string announcement = "";
                if (!string.IsNullOrWhiteSpace(slotName))
                {
                    announcement = slotName;
                }

                if (!string.IsNullOrWhiteSpace(equippedItem))
                {
                    if (!string.IsNullOrWhiteSpace(announcement))
                    {
                        announcement += ": " + equippedItem;
                    }
                    else
                    {
                        announcement = equippedItem;
                    }
                }
                else
                {
                    announcement += ": " + T("Empty");
                }

                if (string.IsNullOrWhiteSpace(announcement))
                {
                    return;
                }

                // Strip icon markup (e.g., <ic_knife>)
                announcement = TextUtils.StripIconMarkup(announcement);

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                EquipMenuState.LastFocusedDescription = TextUtils.StripIconMarkup(description);

                // Set active state - clearing is handled by SetNextState_Postfix
                // COMMENTED OUT: State validation was unreliable
                // if (EquipMenuState.IsInSubmenuState())
                // {
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.EQUIP_MENU);
                // }

                // Append cursor position (N of M) among the equipment slots.
                announcement = MenuPosition.Format(announcement, index, contentList.Count);
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentInfoWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for equipment item selection (Menu 3).
    /// EquipmentSelectWindowController.SelectContent is called when navigating the item list.
    /// Registered in EquipMenuState.ApplyTransitionPatches.
    /// </summary>
    internal static class EquipmentSelectWindowController_SelectContent_Patch
    {
        /// <summary>SelectContent(Cursor targetCursor, WithinRangeType type), positional.</summary>
        public static void Postfix(KeyInputEquipmentSelectWindowController __instance, GameCursor __0)
        {
            try
            {
                GameCursor targetCursor = __0;
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (targetCursor == null) return;

                int index = targetCursor.Index;

                // Access ContentDataList (public property)
                var contentDataList = __instance.ContentDataList;
                if (contentDataList == null || contentDataList.Count == 0)
                {
                    return; // Empty list is normal for empty slots
                }

                if (index < 0 || index >= contentDataList.Count)
                {
                    return;
                }

                var itemData = contentDataList[index];
                if (itemData == null)
                {
                    return;
                }

                // Get item name - handle empty/remove entries
                string itemName = itemData.Name;
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    // This might be a "Remove" or empty entry
                    itemName = T("Remove");
                }

                // Strip icon markup from name
                itemName = TextUtils.StripIconMarkup(itemName);

                // Build announcement with item details
                string announcement = itemName;

                // Add parameter info (ATK +15, DEF +8, etc.)
                try
                {
                    string paramMessage = itemData.ParameterMessage;
                    if (!string.IsNullOrWhiteSpace(paramMessage))
                    {
                        paramMessage = TextUtils.StripIconMarkup(paramMessage);
                        announcement += $", {paramMessage}";
                    }
                }
                catch { }

                // Add description (Auto Detail); always kept for the I key
                string description = null;
                try
                {
                    description = TextUtils.StripIconMarkup(itemData.Description);
                    if (PreferencesManager.AutoDetailEnabled)
                        announcement = AnnouncementBuilder.AppendDescription(announcement, description, ", ");
                }
                catch { }

                // Skip duplicates
                if (!EquipMenuState.ShouldAnnounce(announcement))
                    return;

                EquipMenuState.LastFocusedDescription = description;

                // Set active state - clearing is handled by SetNextState_Postfix
                // COMMENTED OUT: State validation was unreliable
                // if (EquipMenuState.IsInSubmenuState())
                // {
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.EQUIP_MENU);
                // }

                // Append cursor position (N of M) among the selectable equipment items.
                announcement = MenuPosition.Format(announcement, index, contentDataList.Count);
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in EquipmentSelectWindowController.SelectContent patch: {ex.Message}");
            }
        }
    }
}
