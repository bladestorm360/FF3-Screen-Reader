using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;

// Type aliases for FF3
using BattleResultData = Il2CppLast.Data.BattleResultData;
using BattleResultCharacterData = Il2CppLast.Data.BattleResultData.BattleResultCharacterData;
using ResultMenuController_KeyInput = Il2CppLast.UI.KeyInput.ResultMenuController;
using ResultMenuController_Touch = Il2CppLast.UI.Touch.ResultMenuController;
using ResultSkillController_KeyInput = Il2CppLast.UI.KeyInput.ResultSkillController;
using ResultSkillController_Touch = Il2CppLast.UI.Touch.ResultSkillController;
using ListItemFormatter = Il2CppLast.Management.ListItemFormatter;
using MessageManager = Il2CppLast.Management.MessageManager;
using ItemListContentData = Il2CppLast.UI.ItemListContentData;
using CharacterParameterBase = Il2CppLast.Data.CharacterParameterBase;
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Job = Il2CppLast.Data.Master.Job;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for battle result announcements (XP, gil, items, level ups)
    /// Implements phased announcements that sync with on-screen text boxes
    /// </summary>
    public static class BattleResultPatches
    {
        // Track what we've announced to prevent duplicates
        private static BattleResultData lastAnnouncedData = null;
        private static bool announcedPoints = false;
        private static bool announcedItems = false;
        private static HashSet<string> announcedLevelUps = new HashSet<string>();

        /// <summary>
        /// Reset tracking when a new battle result starts
        /// </summary>
        public static void ResetTracking(BattleResultData data)
        {
            if (data != lastAnnouncedData)
            {
                lastAnnouncedData = data;
                announcedPoints = false;
                announcedItems = false;
                announcedLevelUps.Clear();
            }
        }

        /// <summary>
        /// Announce experience and gil gained - per character like FF5
        /// </summary>
        public static void AnnouncePointsGained(BattleResultData data)
        {
            if (data == null || announcedPoints) return;

            ResetTracking(data);
            announcedPoints = true;

            // Gil gained (announce first)
            int gil = data.GetGil;
            if (gil > 0)
            {
                string gilAnnouncement = $"Gained {gil:N0} gil";
                MelonLogger.Msg($"[Victory] {gilAnnouncement}");
                FFIII_ScreenReaderMod.SpeakText(gilAnnouncement, interrupt: true);
            }

            // Per-character experience announcements (like FF5)
            var characterList = data.CharacterList;
            if (characterList != null)
            {
                foreach (var charResult in characterList)
                {
                    if (charResult == null) continue;

                    var afterData = charResult.AfterData;
                    if (afterData == null) continue;

                    string charName = afterData.Name;
                    if (string.IsNullOrEmpty(charName)) continue;

                    int charExp = charResult.GetExp;
                    if (charExp > 0)
                    {
                        string expAnnouncement = $"{charName} gained {charExp:N0} XP";
                        MelonLogger.Msg($"[Victory] {expAnnouncement}");
                        FFIII_ScreenReaderMod.SpeakText(expAnnouncement, interrupt: false);
                    }
                }
            }
        }

        /// <summary>
        /// Announce items dropped
        /// </summary>
        public static void AnnounceItemsDropped(BattleResultData data)
        {
            if (data == null || announcedItems) return;

            ResetTracking(data);
            announcedItems = true;

            var itemList = data.ItemList;
            if (itemList == null || itemList.Count == 0) return;

            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                // Convert drop items to localized content data
                var contentDataList = ListItemFormatter.GetContentDataList(itemList, messageManager);
                if (contentDataList == null || contentDataList.Count == 0) return;

                foreach (var itemContent in contentDataList)
                {
                    if (itemContent == null) continue;

                    string itemName = itemContent.Name;
                    if (string.IsNullOrEmpty(itemName)) continue;

                    // Strip any icon markup (e.g., <ic_item>)
                    itemName = StripIconMarkup(itemName);
                    if (string.IsNullOrEmpty(itemName)) continue;

                    string announcement;
                    int count = itemContent.Count;
                    if (count > 1)
                    {
                        announcement = $"Found {itemName} x{count}";
                    }
                    else
                    {
                        announcement = $"Found {itemName}";
                    }

                    MelonLogger.Msg($"[Victory] {announcement}");
                    FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing items: {ex.Message}");
            }
        }

        /// <summary>
        /// Announce level ups with stat gains for a specific character
        /// </summary>
        public static void AnnounceLevelUp(BattleResultCharacterData charResult)
        {
            if (charResult == null) return;

            var afterData = charResult.AfterData;
            var beforeData = charResult.BeforData;  // Note: typo in game code - "BeforData" not "BeforeData"
            if (afterData == null) return;

            string charName = afterData.Name;
            if (string.IsNullOrEmpty(charName)) return;

            // Check if already announced this character's level up
            string key = $"{charName}_levelup";
            if (announcedLevelUps.Contains(key)) return;
            announcedLevelUps.Add(key);

            if (!charResult.IsLevelUp) return;

            var parts = new List<string>();

            // Get new level using ConfirmedLevel() instead of BaseLevel
            var afterParam = afterData.Parameter;
            int newLevel = 0;
            if (afterParam != null)
            {
                try
                {
                    newLevel = afterParam.ConfirmedLevel();
                }
                catch
                {
                    // Fallback to BaseLevel if ConfirmedLevel fails
                    newLevel = afterParam.BaseLevel;
                }
            }
            parts.Add($"{charName} leveled up to level {newLevel}");

            // Calculate stat gains if we have before data
            if (beforeData?.Parameter != null && afterParam != null)
            {
                var statGains = GetStatGains(beforeData.Parameter, afterParam);
                if (statGains.Count > 0)
                {
                    parts.AddRange(statGains);
                }
            }

            string announcement = string.Join(", ", parts);
            MelonLogger.Msg($"[Victory] {announcement}");
            FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>
        /// Announce job level up for a specific character with job level number
        /// </summary>
        public static void AnnounceJobLevelUp(BattleResultCharacterData charResult)
        {
            if (charResult == null) return;

            var afterData = charResult.AfterData;
            if (afterData == null) return;

            string charName = afterData.Name;
            if (string.IsNullOrEmpty(charName)) return;

            // Check if already announced this character's job level up
            string key = $"{charName}_joblevelup";
            if (announcedLevelUps.Contains(key)) return;
            announcedLevelUps.Add(key);

            if (!charResult.IsJobLevelUp) return;

            // Try to get job level from OwnedJob
            int jobLevel = 0;
            string jobName = "";
            try
            {
                var ownedJob = afterData.OwnedJob;
                if (ownedJob != null)
                {
                    jobLevel = ownedJob.Level;
                    int jobId = ownedJob.Id;

                    // Look up the Job master data using MasterManager
                    var masterManager = MasterManager.Instance;
                    if (masterManager != null)
                    {
                        var jobList = masterManager.GetList<Job>();
                        if (jobList != null && jobList.ContainsKey(jobId))
                        {
                            var job = jobList[jobId];
                            if (job != null)
                            {
                                var messageManager = MessageManager.Instance;
                                if (messageManager != null)
                                {
                                    string mesId = job.MesIdName;
                                    if (!string.IsNullOrEmpty(mesId))
                                    {
                                        jobName = messageManager.GetMessage(mesId, false);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting job info: {ex.Message}");
            }

            string announcement;
            if (!string.IsNullOrEmpty(jobName) && jobLevel > 0)
            {
                announcement = $"{charName} {jobName} job level up to {jobLevel}";
            }
            else if (jobLevel > 0)
            {
                announcement = $"{charName} job level up to {jobLevel}";
            }
            else
            {
                announcement = $"{charName} job level up";
            }
            MelonLogger.Msg($"[Victory] {announcement}");
            FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>
        /// Get list of stat gains between before and after parameters
        /// </summary>
        private static List<string> GetStatGains(CharacterParameterBase before, CharacterParameterBase after)
        {
            var gains = new List<string>();

            int hpGain = after.BaseMaxHp - before.BaseMaxHp;
            if (hpGain > 0) gains.Add($"HP +{hpGain}");

            int powerGain = after.BasePower - before.BasePower;
            if (powerGain > 0) gains.Add($"Strength +{powerGain}");

            int vitalityGain = after.BaseVitality - before.BaseVitality;
            if (vitalityGain > 0) gains.Add($"Vitality +{vitalityGain}");

            int agilityGain = after.BaseAgility - before.BaseAgility;
            if (agilityGain > 0) gains.Add($"Agility +{agilityGain}");

            int intelligenceGain = after.BaseIntelligence - before.BaseIntelligence;
            if (intelligenceGain > 0) gains.Add($"Intelligence +{intelligenceGain}");

            int spiritGain = after.BaseSpirit - before.BaseSpirit;
            if (spiritGain > 0) gains.Add($"Spirit +{spiritGain}");

            return gains;
        }

        /// <summary>
        /// Strip icon markup tags from text (e.g., removes <ic_item> tags)
        /// </summary>
        private static string StripIconMarkup(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Remove <ic_XXX> style tags
            int startIdx;
            while ((startIdx = text.IndexOf("<ic_", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int endIdx = text.IndexOf(">", startIdx);
                if (endIdx > startIdx)
                {
                    text = text.Remove(startIdx, endIdx - startIdx + 1);
                }
                else
                {
                    break;
                }
            }

            // Also handle <IC_XXX> uppercase variant
            while ((startIdx = text.IndexOf("<IC_", StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int endIdx = text.IndexOf(">", startIdx);
                if (endIdx > startIdx)
                {
                    text = text.Remove(startIdx, endIdx - startIdx + 1);
                }
                else
                {
                    break;
                }
            }

            return text.Trim();
        }
    }

    // ========================================
    // Phase 1: Experience & Gil (ShowPointsInit)
    // ========================================

    [HarmonyPatch(typeof(ResultMenuController_KeyInput), "ShowPointsInit")]
    public static class ResultMenuController_KeyInput_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnouncePointsGained(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowPointsInit patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowPointsInit")]
    public static class ResultMenuController_Touch_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnouncePointsGained(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowPointsInit patch (Touch): {ex.Message}");
            }
        }
    }

    // ========================================
    // Phase 2: Item Drops (ShowGetItemsInit)
    // ========================================

    [HarmonyPatch(typeof(ResultMenuController_KeyInput), "ShowGetItemsInit")]
    public static class ResultMenuController_KeyInput_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnounceItemsDropped(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowGetItemsInit patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowGetItemsInit")]
    public static class ResultMenuController_Touch_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnounceItemsDropped(data);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowGetItemsInit patch (Touch): {ex.Message}");
            }
        }
    }

    // ========================================
    // Phase 3: Level Ups (ShowStatusUpInit)
    // ========================================

    [HarmonyPatch(typeof(ResultMenuController_KeyInput), "ShowStatusUpInit")]
    public static class ResultMenuController_KeyInput_ShowStatusUpInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data?.CharacterList == null) return;

                foreach (var charResult in data.CharacterList)
                {
                    if (charResult == null) continue;

                    // Announce level up with stats
                    if (charResult.IsLevelUp)
                    {
                        BattleResultPatches.AnnounceLevelUp(charResult);
                    }

                    // Announce job level up
                    if (charResult.IsJobLevelUp)
                    {
                        BattleResultPatches.AnnounceJobLevelUp(charResult);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowStatusUpInit patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowStatusUpInit")]
    public static class ResultMenuController_Touch_ShowStatusUpInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                var data = __instance.targetData;
                if (data?.CharacterList == null) return;

                foreach (var charResult in data.CharacterList)
                {
                    if (charResult == null) continue;

                    // Announce level up with stats
                    if (charResult.IsLevelUp)
                    {
                        BattleResultPatches.AnnounceLevelUp(charResult);
                    }

                    // Announce job level up
                    if (charResult.IsJobLevelUp)
                    {
                        BattleResultPatches.AnnounceJobLevelUp(charResult);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowStatusUpInit patch (Touch): {ex.Message}");
            }
        }
    }

    // ========================================
    // Alternative: Patch ResultSkillController for level ups
    // In case ShowStatusUpInit doesn't fire for level ups
    // ========================================

    [HarmonyPatch(typeof(ResultSkillController_KeyInput), "ShowLevelUp")]
    public static class ResultSkillController_KeyInput_ShowLevelUp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isNext)
        {
            try
            {
                if (data?.CharacterList == null) return;

                foreach (var charResult in data.CharacterList)
                {
                    if (charResult == null) continue;

                    if (charResult.IsLevelUp)
                    {
                        BattleResultPatches.AnnounceLevelUp(charResult);
                    }

                    if (charResult.IsJobLevelUp)
                    {
                        BattleResultPatches.AnnounceJobLevelUp(charResult);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultSkillController.ShowLevelUp patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultSkillController_Touch), "ShowLevelUp")]
    public static class ResultSkillController_Touch_ShowLevelUp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isNext)
        {
            try
            {
                if (data?.CharacterList == null) return;

                foreach (var charResult in data.CharacterList)
                {
                    if (charResult == null) continue;

                    if (charResult.IsLevelUp)
                    {
                        BattleResultPatches.AnnounceLevelUp(charResult);
                    }

                    if (charResult.IsJobLevelUp)
                    {
                        BattleResultPatches.AnnounceJobLevelUp(charResult);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultSkillController.ShowLevelUp patch (Touch): {ex.Message}");
            }
        }
    }

    // ========================================
    // Fallback: Patch Show method as backup
    // In case the Init methods are private/inaccessible
    // ========================================

    [HarmonyPatch(typeof(ResultMenuController_KeyInput), nameof(ResultMenuController_KeyInput.Show))]
    public static class ResultMenuController_KeyInput_Show_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isReverse)
        {
            try
            {
                if (data == null || isReverse) return;

                // Reset tracking for new battle result
                BattleResultPatches.ResetTracking(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.Show patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), nameof(ResultMenuController_Touch.Show))]
    public static class ResultMenuController_Touch_Show_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isReverse)
        {
            try
            {
                if (data == null || isReverse) return;

                // Reset tracking for new battle result
                BattleResultPatches.ResetTracking(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultMenuController.Show patch (Touch): {ex.Message}");
            }
        }
    }
}
