using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputJobChangeWindowController = Il2CppSerial.FF3.UI.KeyInput.JobChangeWindowController;
using TouchJobChangeWindowController = Il2CppSerial.FF3.UI.Touch.JobChangeWindowController;
using JobContentController = Il2CppSerial.FF3.UI.KeyInput.JobContentController;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using OwnedJobData = Il2CppLast.Data.User.OwnedJobData;
using Job = Il2CppLast.Data.Master.Job;
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Helper state for job menu announcements.
    /// </summary>
    public static class JobMenuState
    {
        /// <summary>
        /// True when job menu is active and handling announcements.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        /// <summary>
        /// Check if GenericCursor should be suppressed. Validates controller is still active.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            // Validate job menu controller is still active
            try
            {
                var controller = UnityEngine.Object.FindObjectOfType<KeyInputJobChangeWindowController>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    IsActive = false;
                    return false;
                }
                return true;
            }
            catch
            {
                IsActive = false;
                return false;
            }
        }

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;
        private static int lastJobIndex = -1;

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.1f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static bool IsNewJobIndex(int index)
        {
            if (index == lastJobIndex)
                return false;
            lastJobIndex = index;
            return true;
        }

        public static void ResetState()
        {
            IsActive = false;
            lastJobIndex = -1;
            lastAnnouncement = "";
        }

        /// <summary>
        /// Gets the localized job name from a Job master data object.
        /// </summary>
        public static string GetJobName(Job job)
        {
            if (job == null)
                return null;

            try
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
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error getting job name: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the job level for a specific job ID from character data.
        /// </summary>
        public static int GetJobLevel(OwnedCharacterData characterData, int jobId)
        {
            if (characterData == null)
                return 0;

            try
            {
                var jobDataList = characterData.OwnedJobDataList;
                if (jobDataList != null)
                {
                    foreach (var jobData in jobDataList)
                    {
                        if (jobData != null && jobData.Id == jobId)
                        {
                            return jobData.Level;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error getting job level: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Gets job level from the current owned job.
        /// </summary>
        public static int GetCurrentJobLevel(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return 0;

            try
            {
                var ownedJob = characterData.OwnedJob;
                if (ownedJob != null)
                {
                    return ownedJob.Level;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error getting current job level: {ex.Message}");
            }

            return 0;
        }
    }

    /// <summary>
    /// Patches for job menu - announces job name and level when navigating.
    /// Uses manual Harmony patching due to FF3's IL2CPP constraints.
    /// </summary>
    public static class JobMenuPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for job menu.
        /// Called from FFIII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchUpdateJobInfo(harmony);
                TryPatchSelectContent(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch UpdateJobInfo - called when job info panel updates.
        /// Has OwnedCharacterData and Job parameters.
        /// </summary>
        private static void TryPatchUpdateJobInfo(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Try KeyInput version first
                Type controllerType = typeof(KeyInputJobChangeWindowController);

                var method = controllerType.GetMethod("UpdateJobInfo",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (method != null)
                {
                    var postfix = typeof(JobMenuPatches).GetMethod(nameof(UpdateJobInfo_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Job Menu] Patched UpdateJobInfo successfully");
                }
                else
                {
                    MelonLogger.Warning("[Job Menu] Could not find UpdateJobInfo method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error patching UpdateJobInfo: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SelectContent - called when navigating job list.
        /// </summary>
        private static void TryPatchSelectContent(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(KeyInputJobChangeWindowController);

                // Find SelectContent(int index, WithinRangeType scrollType)
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "SelectContent")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(JobMenuPatches).GetMethod(nameof(SelectContent_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Job Menu] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Job Menu] Could not find SelectContent method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error patching SelectContent: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UpdateJobInfo - announces job name and level.
        /// </summary>
        public static void UpdateJobInfo_Postfix(object __instance, OwnedCharacterData characterData, Job targetJob)
        {
            try
            {
                // Set active state - this postfix firing means job menu is active
                JobMenuState.IsActive = true;

                if (characterData == null || targetJob == null)
                    return;

                // Get localized job name
                string jobName = JobMenuState.GetJobName(targetJob);
                if (string.IsNullOrEmpty(jobName))
                {
                    MelonLogger.Msg($"[Job Menu] Could not get job name for job ID {targetJob.Id}");
                    return;
                }

                // Get job level for this specific job
                int jobLevel = JobMenuState.GetJobLevel(characterData, targetJob.Id);

                // Build announcement: "Job Name, Job Level X"
                string announcement = $"{jobName}, Job Level {jobLevel}";

                // Skip duplicates
                if (!JobMenuState.ShouldAnnounce(announcement))
                    return;

                MelonLogger.Msg($"[Job Menu] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error in UpdateJobInfo postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SelectContent - tracks job selection changes.
        /// The actual announcement is done in UpdateJobInfo which has the job data.
        /// </summary>
        public static void SelectContent_Postfix(object __instance, int index)
        {
            try
            {
                // Just track the index change - UpdateJobInfo will handle the announcement
                if (JobMenuState.IsNewJobIndex(index))
                {
                    MelonLogger.Msg($"[Job Menu] SelectContent: index={index}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error in SelectContent postfix: {ex.Message}");
            }
        }
    }
}
