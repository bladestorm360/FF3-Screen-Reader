using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputStatusWindowController = Il2CppLast.UI.KeyInput.StatusWindowController;
using KeyInputStatusDetailsController = Il2CppSerial.FF3.UI.KeyInput.StatusDetailsController;
using StatusWindowContentControllerBase = Il2CppSerial.Template.UI.StatusWindowContentControllerBase;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using GameCursor = Il2CppLast.UI.Cursor;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;
using Corps = Il2CppLast.Data.User.Corps;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using Job = Il2CppLast.Data.Master.Job;
using MasterManager = Il2CppLast.Data.Master.MasterManager;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Helper state for status menu announcements.
    /// </summary>
    public static class StatusMenuState
    {
        /// <summary>
        /// True when status/character selection menu is active and handling announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.STATUS_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.STATUS_MENU, value);
        }

        private const string CONTEXT = "StatusMenu.Select";

        public static void ResetState()
        {
            IsActive = false;
            AnnouncementDeduplicator.Reset(CONTEXT);
        }

        public static bool ShouldAnnounce(string announcement)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT, announcement);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Called by CursorNavigation_Postfix to prevent double-reading.
        /// State is cleared by transition patches when menu closes.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        /// <summary>
        /// Gets the row (Front/Back) for a character.
        /// Delegates to CharacterDataHelper.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
            => CharacterDataHelper.GetCharacterRow(characterData);

        /// <summary>
        /// Gets the localized job name for a character's current job.
        /// Delegates to CharacterDataHelper.
        /// </summary>
        public static string GetCurrentJobName(OwnedCharacterData characterData)
            => CharacterDataHelper.GetCurrentJobName(characterData);
    }

    /// <summary>
    /// Helper state for status details screen (individual character stats view).
    /// Separate from StatusMenuState to allow proper state transitions.
    /// </summary>
    public static class StatusDetailsState
    {
        /// <summary>
        /// True when status details screen is active and handling announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.STATUS_DETAILS);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.STATUS_DETAILS, value);
        }

        public static void ResetState()
        {
            IsActive = false;
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates that status details screen is actually visible.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            // Validate that status details controller is actually visible
            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<KeyInputStatusDetailsController>();
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                {
                    // Controller not visible, clear state
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
    }

    /// <summary>
    /// Patches for status menu - announces character with row information.
    /// Uses manual Harmony patching due to FF3's IL2CPP constraints.
    /// </summary>
    public static class StatusMenuPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for status menu.
        /// Called from FFIII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchSelectContent(harmony);
                TryPatchSetActive(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SetActive - clears state when status menu closes.
        /// </summary>
        private static void TryPatchSetActive(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(KeyInputStatusWindowController),
                typeof(StatusMenuPatches), logPrefix: "[Status Menu]");
        }

        /// <summary>
        /// Postfix for SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                StatusMenuState.ResetState();
            }
        }

        /// <summary>
        /// Patch SelectContent - called when navigating character list in status menu.
        /// </summary>
        private static void TryPatchSelectContent(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputStatusWindowController);

                // Find SelectContent(List<StatusWindowContentControllerBase>, int, Cursor)
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "SelectContent")
                    {
                        var parameters = method.GetParameters();
                        // Looking for (List<StatusWindowContentControllerBase>, int, Cursor)
                        if (parameters.Length >= 2)
                        {
                            string param0Type = parameters[0].ParameterType.Name;
                            string param1Type = parameters[1].ParameterType.Name;

                            MelonLogger.Msg($"[Status Menu] Found SelectContent: params[0]={param0Type}, params[1]={param1Type}");

                            if (param0Type.Contains("List") && param1Type == "Int32")
                            {
                                targetMethod = method;
                                break;
                            }
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(StatusMenuPatches).GetMethod(nameof(SelectContent_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Status Menu] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Status Menu] Could not find SelectContent method");

                    // Log available methods for debugging
                    foreach (var method in methods)
                    {
                        if (method.Name.Contains("Select") || method.Name.Contains("Content"))
                        {
                            MelonLogger.Msg($"[Status Menu] Available method: {method.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error patching SelectContent: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent - announces character name with row.
        /// Format: "Character Name, Job, Row, HP X/Y"
        /// </summary>
        public static void SelectContent_Postfix(
            object __instance,
            Il2CppSystem.Collections.Generic.List<StatusWindowContentControllerBase> contents,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                // Only announce when menu is actually open
                // IMPORTANT: Don't set IsActive until after validation to prevent
                // suppression during game load when SelectContent fires but menu isn't open
                Il2CppLast.UI.MenuManager menuManager = null;
                try
                {
                    menuManager = Il2CppLast.UI.MenuManager.Instance;
                }
                catch
                {
                    // MenuManager not available
                    return;
                }
                if (menuManager == null || !menuManager.IsOpen)
                {
                    return;
                }

                if (contents == null || contents.Count == 0)
                {
                    return;
                }

                if (index < 0 || index >= contents.Count)
                {
                    MelonLogger.Msg($"[Status Menu] SelectContent: index {index} out of range (count={contents.Count})");
                    return;
                }

                var content = contents[index];
                if (content == null)
                {
                    MelonLogger.Msg("[Status Menu] SelectContent: content at index is null");
                    return;
                }

                // Get character data from content controller
                var characterData = content.CharacterData;
                if (characterData == null)
                {
                    MelonLogger.Msg("[Status Menu] SelectContent: CharacterData is null");
                    return;
                }

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Status");
                StatusMenuState.IsActive = true;

                // Build announcement
                string charName = characterData.Name;
                if (string.IsNullOrWhiteSpace(charName))
                    return;

                string announcement = charName;

                // Add job name
                string jobName = StatusMenuState.GetCurrentJobName(characterData);
                if (!string.IsNullOrEmpty(jobName))
                {
                    announcement += $", {jobName}";
                }

                // Add level (pre-menu character selection includes level)
                var parameter = characterData.Parameter;
                if (parameter != null)
                {
                    int level = parameter.ConfirmedLevel();
                    if (level > 0)
                    {
                        announcement += $", Level {level}";
                    }
                }

                // Add row information
                string row = StatusMenuState.GetCharacterRow(characterData);
                if (!string.IsNullOrEmpty(row))
                {
                    announcement += $", {row}";
                }

                // Add HP information using CharacterStatusHelper
                string hpString = CharacterStatusHelper.GetHPString(parameter);
                if (!string.IsNullOrEmpty(hpString))
                {
                    announcement += $", {hpString}";
                }

                // Skip duplicates
                if (!StatusMenuState.ShouldAnnounce(announcement))
                    return;

                MelonLogger.Msg($"[Status Menu] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error in SelectContent postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patches for status details screen - enables stat navigation.
    /// Uses manual Harmony patching due to FF3's IL2CPP constraints.
    /// </summary>
    public static class StatusDetailsPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for status details.
        /// Called from FFIII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchInitDisplay(harmony);
                TryPatchExitDisplay(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch InitDisplay - called when entering status details view.
        /// </summary>
        private static void TryPatchInitDisplay(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputStatusDetailsController);

                // Find InitDisplay()
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "InitDisplay")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 0)
                        {
                            targetMethod = method;
                            MelonLogger.Msg($"[Status Details] Found InitDisplay()");
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(StatusDetailsPatches).GetMethod(nameof(InitDisplay_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Status Details] Patched InitDisplay successfully");
                }
                else
                {
                    MelonLogger.Warning("[Status Details] Could not find InitDisplay method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error patching InitDisplay: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ExitDisplay - called when leaving status details view.
        /// </summary>
        private static void TryPatchExitDisplay(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputStatusDetailsController);

                // Find ExitDisplay()
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "ExitDisplay")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 0)
                        {
                            targetMethod = method;
                            MelonLogger.Msg($"[Status Details] Found ExitDisplay()");
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(StatusDetailsPatches).GetMethod(nameof(ExitDisplay_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Status Details] Patched ExitDisplay successfully");
                }
                else
                {
                    MelonLogger.Warning("[Status Details] Could not find ExitDisplay method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error patching ExitDisplay: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitDisplay - initializes stat navigation.
        /// </summary>
        public static void InitDisplay_Postfix(object __instance)
        {
            try
            {
                var controller = __instance as KeyInputStatusDetailsController;
                if (controller == null)
                    return;

                // Use coroutine for one-frame delay to ensure UI has updated
                CoroutineManager.StartManaged(DelayedStatusInit(controller));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error in InitDisplay postfix: {ex.Message}");
            }
        }

        private static IEnumerator DelayedStatusInit(KeyInputStatusDetailsController controller)
        {
            // Wait one frame for UI to update
            yield return null;

            try
            {
                if (controller == null)
                    yield break;

                // Only initialize if status screen is actually visible
                if (controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                    yield break;

                // Clear all other menu states to prevent suppression conflicts
                // This ensures only StatusDetailsState is active when viewing status details
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("StatusDetails");

                // Get character data
                var characterData = StatusDetailsHelpers.GetCharacterDataFromController(controller);
                if (characterData == null)
                {
                    MelonLogger.Warning("[Status Details] Could not get character data");
                    yield break;
                }

                // Initialize navigation state
                var tracker = StatusNavigationTracker.Instance;
                tracker.IsNavigationActive = true;
                tracker.CurrentStatIndex = 0;
                tracker.ActiveController = controller;
                tracker.CurrentCharacterData = characterData;

                // Also set for existing stat reading methods
                StatusDetailsReader.SetCurrentCharacterData(characterData);

                // Initialize the stat list
                StatusNavigationReader.InitializeStatList();

                // Set status details state active for proper suppression
                StatusDetailsState.IsActive = true;

                // Announce basic status info
                string statusText = StatusDetailsReader.ReadStatusDetails(controller);
                if (!string.IsNullOrWhiteSpace(statusText))
                {
                    MelonLogger.Msg($"[Status Details] {statusText}");
                    FFIII_ScreenReaderMod.SpeakText(statusText);
                }

                MelonLogger.Msg("[Status Details] Navigation initialized - use Up/Down arrows to browse stats");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error in delayed status init: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ExitDisplay - clears stat navigation.
        /// </summary>
        public static void ExitDisplay_Postfix()
        {
            try
            {
                // Clear character data
                StatusDetailsReader.ClearCurrentCharacterData();

                // Reset navigation state
                StatusNavigationTracker.Instance.Reset();

                // Clear status details state
                StatusDetailsState.ResetState();

                // Clear user-opened flag (also clear StatusMenuState since we're exiting details)
                StatusMenuState.IsActive = false;
                MelonLogger.Msg("[Status Details] Menu exited, state cleared");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error in ExitDisplay postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Helper methods for status screen patches
    /// </summary>
    public static class StatusDetailsHelpers
    {
        /// <summary>
        /// Extract character data from the StatusDetailsController.
        /// Uses pointer offsets to access statusController.targetData.
        /// StatusDetailsController.statusController (0x78) -> AbilityCharaStatusController.targetData (0x48)
        /// </summary>
        public static OwnedCharacterData GetCharacterDataFromController(KeyInputStatusDetailsController controller)
        {
            try
            {
                if (controller == null)
                    return null;

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return null;

                // Read statusController pointer at offset 0x78
                // StatusDetailsController has: statusController: AbilityCharaStatusController (0x78)
                IntPtr statusControllerPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(controllerPtr, 0x78);
                if (statusControllerPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[Status Details] statusController is null at 0x78");
                    return null;
                }

                // Read targetData pointer at offset 0x48 from AbilityCharaStatusController
                // AbilityCharaStatusController has: targetData: OwnedCharacterData (0x48)
                IntPtr targetDataPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(statusControllerPtr, 0x48);
                if (targetDataPtr == IntPtr.Zero)
                {
                    MelonLogger.Warning("[Status Details] targetData is null at 0x48");
                    return null;
                }

                // Create OwnedCharacterData from pointer
                var characterData = new OwnedCharacterData(targetDataPtr);
                if (characterData != null)
                {
                    MelonLogger.Msg($"[Status Details] Got character data: {characterData.Name}");
                    return characterData;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error getting character data via pointer: {ex.Message}");
            }

            // Fallback: try to get from first party member (should rarely be needed now)
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    var charData = userDataManager.GetMemberData(0);
                    if (charData != null)
                    {
                        MelonLogger.Msg("[Status Details] Got character data from GetMemberData fallback");
                        return charData;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Details] Error in fallback character data: {ex.Message}");
            }

            return null;
        }
    }
}
