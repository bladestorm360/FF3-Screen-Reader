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
        /// </summary>
        public static bool IsActive { get; set; } = false;

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        public static void ResetState()
        {
            IsActive = false;
            lastAnnouncement = "";
        }

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Called by CursorNavigation_Postfix to prevent double-reading.
        /// Uses state machine validation to auto-reset when menu is closed.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                // Find the StatusWindowController to validate it's still active
                var controller = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.StatusWindowController>();
                if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                {
                    // Controller is gone, clear state
                    ResetState();
                    return false;
                }

                // Read state machine to check current state
                // StatusWindowControllerBase.stateMachine at offset 0x20
                // StateMachine.current at offset 0x10
                // State.Tag at offset 0x10
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                {
                    ResetState();
                    return false;
                }

                IntPtr stateMachinePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(controllerPtr, 0x20);
                if (stateMachinePtr == IntPtr.Zero)
                {
                    ResetState();
                    return false;
                }

                IntPtr currentStatePtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(stateMachinePtr, 0x10);
                if (currentStatePtr == IntPtr.Zero)
                {
                    ResetState();
                    return false;
                }

                int stateTag = System.Runtime.InteropServices.Marshal.ReadInt32(currentStatePtr, 0x10);

                // State values: None=0, List=1, Select=2
                // If state is None, menu is closed
                if (stateTag == 0)
                {
                    ResetState();
                    return false;
                }

                // Menu is active in List or Select state
                return true;
            }
            catch
            {
                // On any error, clear state to avoid stuck suppression
                ResetState();
                return false;
            }
        }

        /// <summary>
        /// Gets the row (Front/Back) for a character.
        /// </summary>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                    return null;

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null && corps.CharacterId == characterId)
                    {
                        return corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error getting character row: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the localized job name for a character's current job.
        /// </summary>
        public static string GetCurrentJobName(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                int jobId = characterData.JobId;
                if (jobId <= 0)
                    return null;

                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                var jobList = masterManager.GetList<Job>();
                if (jobList == null || !jobList.ContainsKey(jobId))
                    return null;

                var job = jobList[jobId];
                if (job != null)
                {
                    string mesId = job.MesIdName;
                    if (!string.IsNullOrEmpty(mesId))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string localizedName = messageManager.GetMessage(mesId, false);
                            if (!string.IsNullOrWhiteSpace(localizedName))
                                return localizedName;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error getting job name: {ex.Message}");
            }

            return null;
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
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Status Menu] Error applying patches: {ex.Message}");
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

                // Add row information
                string row = StatusMenuState.GetCharacterRow(characterData);
                if (!string.IsNullOrEmpty(row))
                {
                    announcement += $", {row}";
                }

                // Add HP information
                try
                {
                    var parameter = characterData.Parameter;
                    if (parameter != null)
                    {
                        int currentHp = parameter.currentHP;
                        int maxHp = parameter.ConfirmedMaxHp();
                        announcement += $", HP {currentHp}/{maxHp}";
                    }
                }
                catch (Exception paramEx)
                {
                    MelonLogger.Warning($"[Status Menu] Error getting HP: {paramEx.Message}");
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

                // Only announce if user actively opened the status menu
                if (!StatusMenuState.IsActive)
                {
                    MelonLogger.Msg("[Status Details] Suppressed - menu not user-opened");
                    yield break;
                }

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

                // Clear user-opened flag
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
