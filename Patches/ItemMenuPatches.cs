using System;
using System.Collections;
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
        private const string DEDUP_CONTEXT = "ItemMenu.Select";
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.ITEM_MENU, DEDUP_CONTEXT);

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

        /// <summary>
        /// SetNextState(State state), positional. Its body (0x40BAA0) is shared with 11 other int setters,
        /// so the native class is checked first.
        /// </summary>
        public static void SetNextState_Postfix(KeyInputItemWindowController __instance, int __0)
        {
            if (!HarmonyPatchHelper.IsNativeInstanceOf<KeyInputItemWindowController>(__instance)) return;
            if ((__0 == IL2CppOffsets.Item.STATE_NONE || __0 == IL2CppOffsets.Item.STATE_COMMAND_SELECT) && IsItemMenuActive)
                IsItemMenuActive = false;
        }

        public static bool ShouldAnnounce(string announcement) => _helper.ShouldAnnounce(announcement);

        /// <summary>Clears the announce dedup so the next item/target read speaks (list re-entry).</summary>
        public static void ResetAnnouncementDedup() => AnnouncementDeduplicator.Reset(DEDUP_CONTEXT);

        /// <summary>
        /// Announces an item-list row: "Name, quantity", plus ": Description" with Auto Detail, then the
        /// "(X of Y)" position; with Auto Detail, queues the equip-job info (U key) after it. Shared by the
        /// SelectContent navigation patch and the on-(re)entry read. Deduped on the announcement text.
        /// </summary>
        public static void AnnounceItemListData(ItemListContentData itemData, int index, int count)
        {
            // Store selected item for 'I' (description) and 'U' (equip requirements) lookup
            LastSelectedItem = itemData;

            string itemName = TextUtils.StripIconMarkup(itemData.Name);
            if (string.IsNullOrEmpty(itemName))
                return;

            int quantity = itemData.Count;
            string announcement = quantity > 0 ? $"{itemName}, {quantity}" : itemName;

            if (PreferencesManager.AutoDetailEnabled)
            {
                string description = TextUtils.StripIconMarkup(itemData.Description);
                if (!string.IsNullOrWhiteSpace(description))
                    announcement += $": {description}";
            }

            // Skip duplicates
            if (!ShouldAnnounce(announcement))
                return;

            // Set active state AFTER validation - menu is confirmed open and we have valid data
            // Also clear other menu states to prevent conflicts
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.ITEM_MENU);

            // Append cursor position (N of M) LAST, after the item name/quantity/description.
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(announcement, index, count), interrupt: true);

            // Auto Detail: queue the same equip-job info the 'U' key reads AFTER the name
            // announce (interrupt:false). Placed past the ShouldAnnounce dedup above, so it
            // only fires on a genuinely new item — cursoring to the same row won't restack it.
            if (PreferencesManager.AutoDetailEnabled)
            {
                ItemDetailsAnnouncer.AnnounceEquipRequirements(interrupt: false, announceIfEmpty: false);
            }
        }

        /// <summary>
        /// Announces an item-use target character: "Name, HP current/max, status effects" plus position.
        /// Level and row are intentionally omitted for item targeting. Deduped on the announcement text.
        /// </summary>
        public static void AnnounceItemUseTarget(ItemTargetSelectContentController content, int index, int count)
        {
            var characterData = content.CurrentData;
            if (characterData == null)
                return;

            string charName = characterData.Name;
            if (string.IsNullOrWhiteSpace(charName))
                return;

            string announcement = charName;

            // Add HP and status conditions via helper
            var parameter = characterData.Parameter;
            if (parameter != null)
                announcement += CharacterStatusHelper.GetFullStatus(parameter);

            // Skip duplicates
            if (!ShouldAnnounce(announcement))
                return;

            // Set active state AFTER validation - menu is confirmed open and we have valid data
            // Also clear other menu states to prevent conflicts
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.ITEM_MENU);

            // Append cursor position (N of M) among the target characters.
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(announcement, index, count), interrupt: true);
        }

        /// <summary>
        /// Gets the localized row (Front/Back) for a character.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
            => CharacterDataHelper.GetCharacterRow(characterData);

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
    /// Manual registration (attribute patches crash on IL2CPP) of the field item list and item-use target
    /// navigation hooks. KeyInput ItemListController.SelectContent(IEnumerable, int, Cursor,
    /// WithinRangeType) 0x7BC180 is unique; KeyInput ItemUseController.SelectContent(IEnumerable, Cursor)
    /// 0x982E80 shares its body only with the same class's SetCursor(IEnumerable, Cursor) (same arguments,
    /// same job: focus a target), and the announce is deduped on its text.
    /// </summary>
    internal static class ItemMenuSelectPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputItemListController), "SelectContent",
                typeof(ItemListController_SelectContent_Patch), nameof(ItemListController_SelectContent_Patch.Postfix),
                "[Item Menu]", new[]
                {
                    typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>),
                    typeof(int),
                    typeof(GameCursor),
                    typeof(CustomScrollViewWithinRangeType)
                });
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputItemUseController), "SelectContent",
                typeof(ItemUseController_SelectContent_Patch), nameof(ItemUseController_SelectContent_Patch.Postfix),
                "[Item Menu]", new[]
                {
                    typeof(Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController>),
                    typeof(GameCursor)
                });
        }
    }

    /// <summary>
    /// Item list selection: announces item name: description when navigating items in the menu.
    /// </summary>
    internal static class ItemListController_SelectContent_Patch
    {
        /// <summary>SelectContent(IEnumerable targets, int index, Cursor targetCursor, ...), positional.</summary>
        public static void Postfix(
            KeyInputItemListController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData> __0,
            int __1)
        {
            try
            {
                var targets = __0;
                int index = __1;
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

                ItemMenuState.AnnounceItemListData(itemData, index, targetList.Count);
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
    internal static class ItemUseController_SelectContent_Patch
    {
        /// <summary>SelectContent(IEnumerable targetContents, Cursor targetCursor), positional.</summary>
        public static void Postfix(
            KeyInputItemUseController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<ItemTargetSelectContentController> __0,
            GameCursor __1)
        {
            try
            {
                var targetContents = __0;
                GameCursor targetCursor = __1;
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

                ItemMenuState.AnnounceItemUseTarget(content, index, contentList.Count);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ItemUseController.SelectContent patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Re-announces the focused row when the item LIST or the item-use TARGET (re)gains focus — on entry
    /// and on back-out from a deeper screen. The SelectContent patches only fire on cursor movement, so
    /// they're silent on (re)entry. ItemWindowController's state-entry Init methods fire in both cases:
    /// they clear the announce dedup (so whichever of this read and a SelectContent fires first speaks,
    /// and the other is deduped) and schedule a bounded read once the list and cursor are built.
    /// </summary>
    internal static class FieldItemReannouncePatches
    {
        private const string LOG = "[ItemMenu]";

        // ItemListController (KeyInput)
        private const int ITEM_LIST_SELECT_CURSOR = 0x60;
        private const int ITEM_LIST_DATA_LIST = 0x78;     // IEnumerable<ItemListContentData>
        // ItemUseController (KeyInput)
        private const int ITEM_USE_CONTENT_LIST = 0x40;   // List<ItemTargetSelectContentController>
        private const int ITEM_USE_SELECT_CURSOR = 0x50;

        // Frames to retry until the list/cursor is built after the state entry
        private const int MAX_READ_FRAMES = 30;

        private static int readGen;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            foreach (var method in new[] { "UseSelectInit", "ImportantSelectInit", "OrganizeSelectInit" })
                HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputItemWindowController), method,
                    typeof(FieldItemReannouncePatches), nameof(ItemList_Init_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(KeyInputItemWindowController), "TargetSelectInit",
                typeof(FieldItemReannouncePatches), nameof(ItemTarget_Init_Postfix), LOG);
        }

        public static void ItemList_Init_Postfix(KeyInputItemWindowController __instance)
        {
            if (__instance == null) return;
            ItemMenuState.ResetAnnouncementDedup();
            CoroutineManager.StartManaged(ReadWhenReady(++readGen, () => TryAnnounceItemList(__instance.itemListController)));
        }

        public static void ItemTarget_Init_Postfix(KeyInputItemWindowController __instance)
        {
            if (__instance == null) return;
            ItemMenuState.ResetAnnouncementDedup();
            CoroutineManager.StartManaged(ReadWhenReady(++readGen, () => TryAnnounceItemTarget(__instance.itemUseController)));
        }

        private static IEnumerator ReadWhenReady(int gen, Func<bool> tryRead)
        {
            for (int frame = 0; frame < MAX_READ_FRAMES; frame++)
            {
                yield return null;
                if (gen != readGen) yield break; // a newer state entry superseded this read
                if (!FieldMenuPatches.IsMenuOpen()) continue;
                if (tryRead()) yield break;
            }
        }

        private static bool TryAnnounceItemList(KeyInputItemListController controller)
        {
            try
            {
                if (controller == null || !controller.gameObject.activeInHierarchy) return false;
                IntPtr ptr = controller.Pointer;

                IntPtr dataListPtr = StateReaderHelper.ReadPointerField(ptr, ITEM_LIST_DATA_LIST);
                if (dataListPtr == IntPtr.Zero) return false;
                var enumerable = new Il2CppSystem.Object(dataListPtr)
                    .TryCast<Il2CppSystem.Collections.Generic.IEnumerable<ItemListContentData>>();
                if (enumerable == null) return false;
                var list = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(enumerable);

                IntPtr cursorPtr = StateReaderHelper.ReadPointerField(ptr, ITEM_LIST_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) return false;
                int index = new GameCursor(cursorPtr).Index;

                var itemData = SelectContentHelper.TryGetItem(list, index);
                if (itemData == null) return false;

                ItemMenuState.AnnounceItemListData(itemData, index, list.Count);
                return true; // spoken, or deduped because SelectContent already read it
            }
            catch { return false; }
        }

        private static bool TryAnnounceItemTarget(KeyInputItemUseController controller)
        {
            try
            {
                if (controller == null || !controller.gameObject.activeInHierarchy) return false;
                IntPtr ptr = controller.Pointer;

                IntPtr listPtr = StateReaderHelper.ReadPointerField(ptr, ITEM_USE_CONTENT_LIST);
                if (listPtr == IntPtr.Zero) return false;
                var list = new Il2CppSystem.Collections.Generic.List<ItemTargetSelectContentController>(listPtr);

                IntPtr cursorPtr = StateReaderHelper.ReadPointerField(ptr, ITEM_USE_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) return false;
                int index = new GameCursor(cursorPtr).Index;

                var content = SelectContentHelper.TryGetItem(list, index);
                if (content?.CurrentData == null) return false;

                ItemMenuState.AnnounceItemUseTarget(content, index, list.Count);
                return true;
            }
            catch { return false; }
        }
    }

}
