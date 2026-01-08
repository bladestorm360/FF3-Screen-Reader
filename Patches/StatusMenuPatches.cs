using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputStatusWindowController = Il2CppLast.UI.KeyInput.StatusWindowController;
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
                // Set active state - this postfix firing means character selection is active
                StatusMenuState.IsActive = true;

                // Only announce when menu is actually open
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
}
