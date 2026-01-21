using System;
using System.Runtime.InteropServices;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.UI;
using Il2CppLast.UI.Common;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Use the KeyInput version of the controller
using SelectFieldContentController = Il2CppLast.UI.KeyInput.SelectFieldContentController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracking for NPC event item selection menu.
    /// This menu appears when NPCs request the player to use/select an item or key item.
    /// </summary>
    public static class EventItemSelectState
    {
        /// <summary>
        /// True when NPC item selection menu is active.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.EVENT_ITEM_SELECT);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.EVENT_ITEM_SELECT, value);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// The dedicated patch handles item announcements with descriptions.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate the manager is actually open
            var manager = SelectFieldContentManager.Instance;
            if (manager == null)
            {
                ClearState();
                return false;
            }

            return true;
        }

        /// <summary>
        /// Clears the event item select state.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
        }
    }

    /// <summary>
    /// Patches for NPC event item selection menu.
    /// Handles item selection with descriptions when NPCs request item use.
    /// </summary>
    public static class EventItemSelectPatches
    {
        // Memory offsets from SelectFieldContentControllerBase
        private const int OFFSET_CONTENT_DATA_LIST = 0x28;  // List<SelectFieldContentData>
        private const int OFFSET_SELECT_CURSOR = 0x30;      // Cursor

        // Memory offsets from SelectFieldContentData
        private const int OFFSET_NAME_MESSAGE_ID = 0x18;        // string
        private const int OFFSET_DESCRIPTION_MESSAGE_ID = 0x20; // string

        private static string lastAnnouncement = "";

        /// <summary>
        /// Applies all event item selection patches.
        /// </summary>
        public static void Apply(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Event Item Select] Applying patches...");

                // Patch SelectContent on KeyInput.SelectFieldContentController
                PatchSelectContent(harmony);

                // Patch Close on SelectFieldContentManager to clear state
                PatchManagerClose(harmony);

                MelonLogger.Msg("[Event Item Select] Patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Event Item Select] Failed to apply patches: {ex.Message}");
            }
        }

        private static void PatchSelectContent(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(SelectFieldContentController);

                // SelectContent(int index, CustomScrollView.WithinRangeType scrollType = 0)
                // The enum parameter has a default value, we can match by name
                MethodInfo selectContentMethod = AccessTools.Method(controllerType, "SelectContent");

                if (selectContentMethod == null)
                {
                    MelonLogger.Warning("[Event Item Select] SelectContent method not found");
                    return;
                }

                var postfix = typeof(EventItemSelectPatches).GetMethod(nameof(SelectContent_Postfix),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg("[Event Item Select] Patched SelectContent successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Event Item Select] Failed to patch SelectContent: {ex.Message}");
            }
        }

        private static void PatchManagerClose(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type managerType = typeof(SelectFieldContentManager);
                MethodInfo closeMethod = AccessTools.Method(managerType, "Close");

                if (closeMethod == null)
                {
                    MelonLogger.Warning("[Event Item Select] SelectFieldContentManager.Close method not found");
                    return;
                }

                var postfix = typeof(EventItemSelectPatches).GetMethod(nameof(Manager_Close_Postfix),
                    BindingFlags.Public | BindingFlags.Static);

                harmony.Patch(closeMethod, postfix: new HarmonyMethod(postfix));
                MelonLogger.Msg("[Event Item Select] Patched SelectFieldContentManager.Close successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Event Item Select] Failed to patch Close: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent - announces the selected item with description.
        /// </summary>
        public static void SelectContent_Postfix(object __instance, int index)
        {
            try
            {
                if (__instance == null || index < 0)
                    return;

                // Set state active
                EventItemSelectState.IsActive = true;

                IntPtr controllerPtr = ((Il2CppSystem.Object)__instance).Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return;

                // Read contentDataList from offset 0x28
                IntPtr dataListPtr = Marshal.ReadIntPtr(controllerPtr + OFFSET_CONTENT_DATA_LIST);
                if (dataListPtr == IntPtr.Zero)
                    return;

                // Cast to the IL2CPP list type
                var dataList = new Il2CppSystem.Collections.Generic.List<SelectFieldContentManager.SelectFieldContentData>(dataListPtr);
                if (dataList == null || dataList.Count == 0)
                    return;

                if (index >= dataList.Count)
                    return;

                var itemData = dataList[index];
                if (itemData == null)
                    return;

                // Get the message IDs
                string nameMessageId = itemData.NameMessageId;
                string descriptionMessageId = itemData.DescriptionMessageId;

                if (string.IsNullOrEmpty(nameMessageId))
                    return;

                // Resolve message IDs to actual text
                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return;

                string itemName = messageManager.GetMessage(nameMessageId);
                if (string.IsNullOrEmpty(itemName))
                    return;

                itemName = TextUtils.StripIconMarkup(itemName).Trim();

                // Build announcement
                string announcement;
                if (!string.IsNullOrEmpty(descriptionMessageId))
                {
                    string description = messageManager.GetMessage(descriptionMessageId);
                    if (!string.IsNullOrEmpty(description))
                    {
                        description = TextUtils.StripIconMarkup(description).Trim();
                        announcement = $"{itemName}: {description}";
                    }
                    else
                    {
                        announcement = itemName;
                    }
                }
                else
                {
                    announcement = itemName;
                }

                // Deduplicate
                if (announcement == lastAnnouncement)
                    return;
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Event Item Select] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Event Item Select] Error in SelectContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectFieldContentManager.Close - clears the state.
        /// </summary>
        public static void Manager_Close_Postfix()
        {
            EventItemSelectState.ClearState();
            lastAnnouncement = "";
            MelonLogger.Msg("[Event Item Select] Menu closed, state cleared");
        }
    }
}
