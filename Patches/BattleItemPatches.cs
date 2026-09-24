using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle item menu.
    /// </summary>
    internal static class BattleItemPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleItemInfomationController);

                // Find SelectContent(Cursor, WithinRangeType) - called when navigating items
                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType.Name == "Cursor")
                        {
                            selectContentMethod = m;
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleItemSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Item] Patches applied");
                }
                else
                {
                    MelonLogger.Warning("[Battle Item] SelectContent method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error applying patches: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// State tracking for battle item menu.
    /// </summary>
    internal static class BattleItemMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.BATTLE_ITEM, "BattleItem.Select");

        static BattleItemMenuState()
        {
            _helper.RegisterResetHandler(() => { LastFocusedDescription = null; });
        }

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }

        public static bool ShouldSuppress() => IsActive;
        public static bool ShouldAnnounce(string announcement) => _helper.ShouldAnnounce(announcement);

        /// <summary>
        /// Stripped description of the focused battle item, refreshed on every cursor move regardless of
        /// Auto Detail. Read on demand by the I key / right stick up.
        /// </summary>
        public static string LastFocusedDescription { get; set; }
    }

    /// <summary>
    /// Patch for battle item selection.
    /// Announces item name and description when navigating items in battle.
    /// Patches SelectContent(Cursor, WithinRangeType) which is called during navigation.
    /// </summary>
    internal static class BattleItemSelectContent_Patch
    {
        public static void Postfix(object __instance, GameCursor targetCursor)
        {
            try
            {
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (__instance == null || targetCursor == null)
                    return;

                var controller = __instance as BattleItemInfomationController;
                if (controller == null)
                    return;

                int cursorIndex = targetCursor.Index;

                var data = GetItemData(controller, cursorIndex, out int count);
                if (data == null)
                    return;

                string announcement = FormatItemAnnouncement(data);
                if (string.IsNullOrEmpty(announcement))
                    return;

                announcement = MenuPosition.Format(announcement, cursorIndex, count);

                // Skip duplicates
                if (!BattleItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_ITEM);

                // Restore the command-announce window + arm the command back-out re-announce
                BattleCommandSelectController_SetCursor_Patch.NotifyCommandSubmenuActive();

                // Immediate speech - no delay needed
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the focused item's display data. Reads displayDataList (the list the view renders, with
        /// name, count and description) by pointer, falling back to the content controllers.
        /// </summary>
        private static ItemListContentData GetItemData(BattleItemInfomationController controller, int cursorIndex, out int count)
        {
            count = 0;
            try
            {
                IntPtr listPtr = StateReaderHelper.ReadPointerField(controller.Pointer, OFFSET_DISPLAY_DATA_LIST);
                if (listPtr != IntPtr.Zero)
                {
                    var displayDataList = new Il2CppSystem.Collections.Generic.List<ItemListContentData>(listPtr);
                    var data = SelectContentHelper.TryGetItem(displayDataList, cursorIndex);
                    if (data != null)
                    {
                        count = displayDataList.Count;
                        return data;
                    }
                }

                var contentList = controller.contentList;
                var contentController = SelectContentHelper.TryGetItem(contentList, cursorIndex);
                if (contentController?.Data != null)
                {
                    count = contentList.Count;
                    return contentController.Data;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error getting item data: {ex.Message}");
            }

            return null;
        }

        // BattleItemInfomationController (KeyInput) List<ItemListContentData> displayDataList
        private const int OFFSET_DISPLAY_DATA_LIST = 0xE0;

        /// <summary>
        /// Format item data into announcement string: "Name, quantity", plus ": description" when
        /// Auto Detail is on. The description is cached for the on-demand I key either way.
        /// </summary>
        private static string FormatItemAnnouncement(ItemListContentData data)
        {
            try
            {
                string itemName = TextUtils.StripIconMarkup(data.Name);
                if (string.IsNullOrEmpty(itemName))
                    return null;

                int quantity = data.Count;
                string announcement = quantity > 0 ? $"{itemName}, {quantity}" : itemName;

                string description = null;
                try
                {
                    description = TextUtils.StripIconMarkup(data.Description);
                }
                catch
                {
                    // Description not available
                }

                BattleItemMenuState.LastFocusedDescription = string.IsNullOrWhiteSpace(description) ? null : description;
                if (PreferencesManager.AutoDetailEnabled && BattleItemMenuState.LastFocusedDescription != null)
                    announcement += $": {description}";

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error formatting announcement: {ex.Message}");
                return null;
            }
        }
    }
}
