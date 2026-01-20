using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Management;

// Type aliases for IL2CPP types
using KeyInputJobChangeWindowController = Il2CppSerial.FF3.UI.KeyInput.JobChangeWindowController;
using TouchJobChangeWindowController = Il2CppSerial.FF3.UI.Touch.JobChangeWindowController;
using KeyInputJobChangeWindowView = Il2CppSerial.FF3.UI.KeyInput.JobChangeWindowView;
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
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.JOB_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.JOB_MENU, value);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// State is cleared by transition patches when menu closes.
        /// </summary>
        public static bool ShouldSuppress() => IsActive;

        private const string CONTEXT = "JobMenu.Select";
        private const string CONTEXT_INDEX = "JobMenu.Index";

        public static bool ShouldAnnounce(string announcement)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT, announcement);
        }

        public static bool IsNewJobIndex(int index)
        {
            return AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_INDEX, index);
        }

        public static void ResetState()
        {
            IsActive = false;
            AnnouncementDeduplicator.Reset(CONTEXT, CONTEXT_INDEX);
        }

        /// <summary>
        /// Gets the localized job name from a Job master data object.
        /// </summary>
        public static string GetJobName(Job job)
        {
            if (job == null)
                return null;

            return LocalizationHelper.GetText(job.MesIdName);
        }

        /// <summary>
        /// Reads job level directly from the Job Change Window UI.
        /// Returns the level as displayed by the game, or -1 if not found.
        /// </summary>
        public static int TryReadJobLevelFromUI()
        {
            try
            {
                var jobView = UnityEngine.Object.FindObjectOfType<KeyInputJobChangeWindowView>();
                if (jobView == null)
                {
                    MelonLogger.Msg("[Job Debug] JobChangeWindowView not found");
                    return -1;
                }

                // Try InfoSkillLevelValueText first (the info panel showing selected job)
                // Offset 0x80 in KeyInput version
                try
                {
                    IntPtr viewPtr = jobView.Pointer;
                    IntPtr textPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(viewPtr + 0x80);
                    if (textPtr != IntPtr.Zero)
                    {
                        var textComponent = new UnityEngine.UI.Text(textPtr);
                        string levelText = textComponent.text;
                        MelonLogger.Msg($"[Job Debug] InfoSkillLevelValueText.text = '{levelText}'");

                        if (!string.IsNullOrEmpty(levelText) && int.TryParse(levelText, out int level))
                        {
                            return level;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Job Debug] Failed to read InfoSkillLevelValueText: {ex.Message}");
                }

                // Fallback: try SkillLevelValueText (offset 0x68)
                try
                {
                    IntPtr viewPtr = jobView.Pointer;
                    IntPtr textPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(viewPtr + 0x68);
                    if (textPtr != IntPtr.Zero)
                    {
                        var textComponent = new UnityEngine.UI.Text(textPtr);
                        string levelText = textComponent.text;
                        MelonLogger.Msg($"[Job Debug] SkillLevelValueText.text = '{levelText}'");

                        if (!string.IsNullOrEmpty(levelText) && int.TryParse(levelText, out int level))
                        {
                            return level;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[Job Debug] Failed to read SkillLevelValueText: {ex.Message}");
                }

                return -1;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Debug] TryReadJobLevelFromUI error: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Gets the job level for a specific job ID.
        /// First tries to read from UI (most accurate), falls back to data accessor.
        /// </summary>
        public static int GetJobLevel(OwnedCharacterData characterData, int jobId)
        {
            // First try reading from UI - this is the most accurate
            int uiLevel = TryReadJobLevelFromUI();
            if (uiLevel > 0)
            {
                return uiLevel;
            }

            // Fallback to data accessor (may return base level instead of calculated)
            if (characterData == null)
                return 1;

            try
            {
                var jobDataList = characterData.OwnedJobDataList;
                if (jobDataList != null)
                {
                    foreach (var jobData in jobDataList)
                    {
                        if (jobData != null && jobData.Id == jobId)
                        {
                            return jobData.Level > 0 ? jobData.Level : 1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error getting job level: {ex.Message}");
            }

            return 1;
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
                TryPatchSetActive(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Job Menu] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SetActive - clears state when menu closes.
        /// </summary>
        private static void TryPatchSetActive(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(KeyInputJobChangeWindowController),
                typeof(JobMenuPatches), logPrefix: "[Job Menu]");
        }

        /// <summary>
        /// Postfix for SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                JobMenuState.ResetState();
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
        /// Adds "Equipped" indicator when viewing the currently equipped job.
        /// </summary>
        public static void UpdateJobInfo_Postfix(object __instance, OwnedCharacterData characterData, Job targetJob)
        {
            try
            {
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

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

                // Check if this is the currently equipped job
                bool isEquipped = (targetJob.Id == characterData.JobId);

                // Build announcement: "Job Name, Job Level X" or "Job Name, Job Level X. Equipped."
                string announcement = isEquipped
                    ? $"{jobName}, Job Level {jobLevel}. Equipped."
                    : $"{jobName}, Job Level {jobLevel}";

                // Skip duplicates
                if (!JobMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Job");
                JobMenuState.IsActive = true;

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
