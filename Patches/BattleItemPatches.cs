using System;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using BattleItemInfomationController = Il2CppLast.UI.KeyInput.BattleItemInfomationController;
using BattleItemInfomationContentController = Il2CppLast.UI.KeyInput.BattleItemInfomationContentController;
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle item menu.
    /// </summary>
    public static class BattleItemPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Item] Applying battle item menu patches...");

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
                            MelonLogger.Msg($"[Battle Item] Found SelectContent with Cursor parameter");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleItemSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Item] Patched SelectContent successfully");
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
    public static class BattleItemMenuState
    {
        /// <summary>
        /// True when battle item menu is active and handling announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_ITEM);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.BATTLE_ITEM, value);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// State is cleared by BattleResultPatches when battle ends.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        private const string CONTEXT = "BattleItem.Select";

        public static bool ShouldAnnounce(string announcement)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT, announcement);
        }

        public static void Reset()
        {
            IsActive = false;
            AnnouncementDeduplicator.Reset(CONTEXT);
        }
    }

    /// <summary>
    /// Patch for battle item selection.
    /// Announces item name and description when navigating items in battle.
    /// Patches SelectContent(Cursor, WithinRangeType) which is called during navigation.
    /// </summary>
    public static class BattleItemSelectContent_Patch
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
                MelonLogger.Msg($"[Battle Item] SelectContent called, cursor index: {cursorIndex}");

                // Try to get item data from the content list
                string announcement = TryGetItemFromContentList(controller, cursorIndex);

                if (string.IsNullOrEmpty(announcement))
                {
                    MelonLogger.Msg("[Battle Item] Could not get item from content list");
                    return;
                }

                // Skip duplicates
                if (!BattleItemMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("BattleItem");
                BattleItemMenuState.IsActive = true;

                // Immediate speech - no delay needed
                MelonLogger.Msg($"[Battle Item] Announcing: {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to get item information from the controller's content list.
        /// </summary>
        private static string TryGetItemFromContentList(BattleItemInfomationController controller, int cursorIndex)
        {
            try
            {
                // The controller has a contentList field with BattleItemInfomationContentController instances
                // Each content controller has a Data property of type ItemListContentData

                // Use IL2CPP reflection to get the contentList field
                var controllerType = controller.GetType();

                // Try to find contentList as a property first (IL2CPP sometimes exposes fields as properties)
                var contentListProp = controllerType.GetProperty("contentList",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                Il2CppSystem.Collections.Generic.List<BattleItemInfomationContentController> contentList = null;

                if (contentListProp != null)
                {
                    var propValue = contentListProp.GetValue(controller);
                    contentList = propValue as Il2CppSystem.Collections.Generic.List<BattleItemInfomationContentController>;
                }

                if (contentList == null)
                {
                    // Try using Il2CppInterop field access
                    // Access via the boxed pointer pattern
                    MelonLogger.Msg("[Battle Item] contentList property not found, trying alternative...");

                    // Try finding all active content controllers in scene
                    var allContentControllers = UnityEngine.Object.FindObjectsOfType<BattleItemInfomationContentController>();
                    if (allContentControllers != null && allContentControllers.Length > 0)
                    {
                        MelonLogger.Msg($"[Battle Item] Found {allContentControllers.Length} content controllers in scene");

                        // Find the one at cursor index (they should be in order)
                        foreach (var cc in allContentControllers)
                        {
                            if (cc == null || !cc.gameObject.activeInHierarchy)
                                continue;

                            // Check if this content controller has data
                            var data = cc.Data;
                            if (data != null)
                            {
                                // Check if this is the focused one
                                if (data.IsFocus)
                                {
                                    return FormatItemAnnouncement(data);
                                }
                            }
                        }

                        // Fallback: try by index if no focused item found
                        if (cursorIndex >= 0 && cursorIndex < allContentControllers.Length)
                        {
                            var cc = allContentControllers[cursorIndex];
                            if (cc != null && cc.Data != null)
                            {
                                return FormatItemAnnouncement(cc.Data);
                            }
                        }
                    }
                }
                else
                {
                    MelonLogger.Msg($"[Battle Item] contentList found with {contentList.Count} items");

                    if (cursorIndex >= 0 && cursorIndex < contentList.Count)
                    {
                        var contentController = contentList[cursorIndex];
                        if (contentController != null)
                        {
                            var data = contentController.Data;
                            if (data != null)
                            {
                                return FormatItemAnnouncement(data);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error getting item from content list: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Format item data into announcement string.
        /// </summary>
        private static string FormatItemAnnouncement(ItemListContentData data)
        {
            try
            {
                string description = null;
                try
                {
                    description = data.Description;
                }
                catch
                {
                    // Description not available
                }

                return AnnouncementBuilder.FormatWithDescription(data.Name, description);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Item] Error formatting announcement: {ex.Message}");
                return null;
            }
        }
    }
}
