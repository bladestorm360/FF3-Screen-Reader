using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

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
    /// Shared state for the EXP-counter tick sound across the battle-result patch classes.
    /// </summary>
    internal static class BattleResultState
    {
        // True only while the EXP counter sound is actually playing.
        internal static bool ExpCounterPlaying;

        /// <summary>
        /// Stops the EXP counter sound if it is currently playing.
        /// Safe to call from any phase-init postfix; the flag ensures it only fires once.
        /// </summary>
        internal static void StopExpCounterIfPlaying()
        {
            if (!ExpCounterPlaying) return;
            ExpCounterPlaying = false;
            SoundPlayer.StopExpCounter();
            MelonLogger.Msg("[BattleResult] EXP counter stopped");
        }
    }

    /// <summary>
    /// Patches for battle result announcements (XP, gil, items, level ups)
    /// Implements phased announcements that sync with on-screen text boxes
    /// </summary>
    internal static class BattleResultPatches
    {
        private const string CONTEXT_DATA = AnnouncementContexts.BATTLE_RESULT_DATA;

        // ResultPointController.characterListConteroller field offset differs between the two
        // UI variants: the KeyInput ResultPointController has an extra keyIconController field
        // at 0x20 that shifts the character-list reference down to 0x30, while the Touch variant
        // keeps it at 0x28. (Confirmed against the Il2Cpp dump — see MonitorExpCounterAnimation.)
        internal const int CHARLIST_OFFSET_KEYINPUT = 0x30;
        internal const int CHARLIST_OFFSET_TOUCH = 0x28;

        // Track what we've announced to prevent duplicates
        private static bool announcedPoints = false;
        private static bool announcedItems = false;
        private static HashSet<string> announcedLevelUps = new HashSet<string>();

        /// <summary>
        /// Reset tracking when a new battle result starts
        /// </summary>
        public static void ResetTracking(BattleResultData data)
        {
            // Always clear tracking state first to prevent HashSet memory leak
            announcedPoints = false;
            announcedItems = false;
            announcedLevelUps.Clear();

            // Then track for deduplication
            AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_DATA, data);
        }

        /// <summary>
        /// Clear all battle menu state flags when battle ends.
        /// Called from ShowPointsInit when victory screen appears.
        /// </summary>
        public static void ClearAllBattleMenuFlags()
        {
            BattleCommandState.IsActive = false;
            BattleTargetPatches.SetTargetSelectionActive(false);
            BattleItemMenuState.IsActive = false;
            BattleMagicMenuState.IsActive = false;
            BattlePausePatches.Reset();
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
                string gilAnnouncement = string.Format(T("Gained {0} gil"), gil.ToString("N0"));
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
                        string expAnnouncement = string.Format(T("{0} gained {1} XP"), charName, charExp.ToString("N0"));
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
                    itemName = TextUtils.StripIconMarkup(itemName);
                    if (string.IsNullOrEmpty(itemName)) continue;

                    string announcement;
                    int count = itemContent.Count;
                    if (count > 1)
                    {
                        announcement = string.Format(T("Found {0} x{1}"), itemName, count);
                    }
                    else
                    {
                        announcement = string.Format(T("Found {0}"), itemName);
                    }

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
            parts.Add(string.Format(T("{0} leveled up to level {1}"), charName, newLevel));

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
                    jobName = LocalizationHelper.GetJobName(ownedJob.Id) ?? "";
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting job info: {ex.Message}");
            }

            string announcement;
            if (!string.IsNullOrEmpty(jobName) && jobLevel > 0)
            {
                announcement = string.Format(T("{0} {1} job level up to {2}"), charName, jobName, jobLevel);
            }
            else if (jobLevel > 0)
            {
                announcement = string.Format(T("{0} job level up to {1}"), charName, jobLevel);
            }
            else
            {
                announcement = string.Format(T("{0} job level up"), charName);
            }
            FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
        }

        /// <summary>
        /// Get list of stat gains between before and after parameters
        /// </summary>
        private static List<string> GetStatGains(CharacterParameterBase before, CharacterParameterBase after)
        {
            var gains = new List<string>();

            int hpGain = after.BaseMaxHp - before.BaseMaxHp;
            if (hpGain > 0) gains.Add(string.Format(T("HP +{0}"), hpGain));

            int powerGain = after.BasePower - before.BasePower;
            if (powerGain > 0) gains.Add(string.Format(T("Strength +{0}"), powerGain));

            int vitalityGain = after.BaseVitality - before.BaseVitality;
            if (vitalityGain > 0) gains.Add(string.Format(T("Vitality +{0}"), vitalityGain));

            int agilityGain = after.BaseAgility - before.BaseAgility;
            if (agilityGain > 0) gains.Add(string.Format(T("Agility +{0}"), agilityGain));

            int intelligenceGain = after.BaseIntelligence - before.BaseIntelligence;
            if (intelligenceGain > 0) gains.Add(string.Format(T("Intelligence +{0}"), intelligenceGain));

            int spiritGain = after.BaseSpirit - before.BaseSpirit;
            if (spiritGain > 0) gains.Add(string.Format(T("Spirit +{0}"), spiritGain));

            return gains;
        }

        /// <summary>
        /// Process all level ups and job level ups for all characters in the result data.
        /// Shared helper used by multiple patch classes.
        /// </summary>
        public static void ProcessAllLevelUps(BattleResultData data)
        {
            if (data?.CharacterList == null) return;

            foreach (var charResult in data.CharacterList)
            {
                if (charResult == null) continue;

                if (charResult.IsLevelUp)
                {
                    AnnounceLevelUp(charResult);
                }

                if (charResult.IsJobLevelUp)
                {
                    AnnounceJobLevelUp(charResult);
                }
            }
        }

        // ================================================================
        //  EXP counter tick sound
        // ================================================================

        /// <summary>
        /// Starts the EXP-counter tick sound (if enabled and there is EXP to tally) and launches
        /// the monitor coroutine that stops it when the on-screen EXP bar animation completes.
        /// Mirrors FF5's ShowPointsInit behaviour; when the preference is OFF, this is a no-op so
        /// runtime behaviour is byte-identical to before the feature existed.
        /// </summary>
        /// <param name="instancePtr">Pointer to the ResultMenuController instance.</param>
        /// <param name="data">The battle-result data (used only to gate on total EXP > 0).</param>
        /// <param name="charListOffset">
        /// ResultPointController→ResultCharacterListController field offset for this UI variant
        /// (CHARLIST_OFFSET_KEYINPUT or CHARLIST_OFFSET_TOUCH).
        /// </param>
        internal static void StartExpCounterIfEnabled(IntPtr instancePtr, BattleResultData data, int charListOffset)
        {
            try
            {
                if (data == null) return;
                if (!FFIII_ScreenReaderMod.ExpCounterEnabled) return;

                int totalExp = data.GetExp;
                if (totalExp <= 0) return;

                SoundPlayer.PlayExpCounter();
                BattleResultState.ExpCounterPlaying = true;

                // Launch coroutine to stop the counter when the counting animation finishes.
                CoroutineManager.StartUntracked(MonitorExpCounterAnimation(instancePtr, charListOffset));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[BattleResult] StartExpCounter error: {ex.Message}");
            }
        }

        /// <summary>
        /// Polls the unsafe pointer chain from ResultMenuController to detect when the EXP
        /// counting animation finishes, then stops the counter sound. Mirrors FF5.
        /// Chain: instance -> +0x20 (pointController) -> +charListOffset (characterListConteroller)
        ///   -> +0x20 (contentList; List.Count/_size at +0x18)
        ///   -> +0x30 (perormanceEndCount, on the characterListController).
        /// Animation done when: perormanceEndCount >= contentList.Count &amp;&amp; Count > 0.
        /// If any pointer is invalid the coroutine bails silently — the next-phase Init
        /// stop hooks are the safety net, so the tone is never left stuck.
        /// </summary>
        private static IEnumerator MonitorExpCounterAnimation(IntPtr instancePtr, int charListOffset)
        {
            var wait = new WaitForSeconds(0.1f);
            bool loggedOnce = false;

            if (instancePtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: instancePtr is null");
                yield break;
            }

            IntPtr pointControllerPtr = Marshal.ReadIntPtr(instancePtr, 0x20);
            if (pointControllerPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: pointController is null");
                yield break;
            }

            IntPtr charListCtrlPtr = Marshal.ReadIntPtr(pointControllerPtr, charListOffset);
            if (charListCtrlPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: characterListController is null");
                yield break;
            }

            IntPtr contentListPtr = Marshal.ReadIntPtr(charListCtrlPtr, 0x20);
            if (contentListPtr == IntPtr.Zero)
            {
                MelonLogger.Warning("[BattleResult] MonitorExp: contentList is null");
                yield break;
            }

            // contentList.Count (List._size) at contentListPtr + 0x18
            int contentCount = Marshal.ReadInt32(contentListPtr, 0x18);
            if (contentCount <= 0)
            {
                MelonLogger.Warning($"[BattleResult] MonitorExp: contentCount={contentCount}, aborting");
                yield break;
            }

            MelonLogger.Msg($"[BattleResult] MonitorExp: chain OK. charListCtrl=0x{charListCtrlPtr:X}, contentCount={contentCount}");

            // Poll until animation finishes or the counter was already stopped by a safety net.
            while (BattleResultState.ExpCounterPlaying)
            {
                yield return wait;

                // Keep the SDL Counter stream fed so the loop never drains between ticks.
                SoundPlayer.TopUpExpCounter();

                try
                {
                    int endCount = Marshal.ReadInt32(charListCtrlPtr, 0x30);

                    if (!loggedOnce)
                    {
                        MelonLogger.Msg($"[BattleResult] MonitorExp: first poll endCount={endCount}/{contentCount}");
                        loggedOnce = true;
                    }

                    if (endCount >= contentCount)
                    {
                        MelonLogger.Msg($"[BattleResult] MonitorExp: animation done (endCount={endCount} >= contentCount={contentCount})");
                        BattleResultState.StopExpCounterIfPlaying();
                        yield break;
                    }
                }
                catch
                {
                    // Pointer became invalid -- bail out; the next-phase Init stop hooks handle it.
                    yield break;
                }
            }
        }
    }

    // ========================================
    // Phase 1: Experience & Gil (ShowPointsInit)
    // ========================================

    [HarmonyPatch(typeof(ResultMenuController_KeyInput), "ShowPointsInit")]
    internal static class ResultMenuController_KeyInput_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                // Clear all battle menu flags when victory screen appears
                BattleResultPatches.ClearAllBattleMenuFlags();

                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnouncePointsGained(data);
                    BattleResultPatches.StartExpCounterIfEnabled(
                        __instance.Pointer, data, BattleResultPatches.CHARLIST_OFFSET_KEYINPUT);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowPointsInit patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowPointsInit")]
    internal static class ResultMenuController_Touch_ShowPointsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                // Clear all battle menu flags when victory screen appears
                BattleResultPatches.ClearAllBattleMenuFlags();

                var data = __instance.targetData;
                if (data != null)
                {
                    BattleResultPatches.AnnouncePointsGained(data);
                    BattleResultPatches.StartExpCounterIfEnabled(
                        __instance.Pointer, data, BattleResultPatches.CHARLIST_OFFSET_TOUCH);
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
    internal static class ResultMenuController_KeyInput_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                // Safety net: EXP tally is definitely over by the item-drop phase.
                BattleResultState.StopExpCounterIfPlaying();

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
    internal static class ResultMenuController_Touch_ShowGetItemsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                // Safety net: EXP tally is definitely over by the item-drop phase.
                BattleResultState.StopExpCounterIfPlaying();

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
    internal static class ResultMenuController_KeyInput_ShowStatusUpInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_KeyInput __instance)
        {
            try
            {
                // EXP tally is over once we advance to the status-up phase.
                BattleResultState.StopExpCounterIfPlaying();
                BattleResultPatches.ProcessAllLevelUps(__instance.targetData);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowStatusUpInit patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowStatusUpInit")]
    internal static class ResultMenuController_Touch_ShowStatusUpInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(ResultMenuController_Touch __instance)
        {
            try
            {
                // EXP tally is over once we advance to the status-up phase.
                BattleResultState.StopExpCounterIfPlaying();
                BattleResultPatches.ProcessAllLevelUps(__instance.targetData);
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
    internal static class ResultSkillController_KeyInput_ShowLevelUp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isNext)
        {
            try
            {
                BattleResultPatches.ProcessAllLevelUps(data);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ResultSkillController.ShowLevelUp patch (KeyInput): {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ResultSkillController_Touch), "ShowLevelUp")]
    internal static class ResultSkillController_Touch_ShowLevelUp_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleResultData data, bool isNext)
        {
            try
            {
                BattleResultPatches.ProcessAllLevelUps(data);
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
    internal static class ResultMenuController_KeyInput_Show_Patch
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
    internal static class ResultMenuController_Touch_Show_Patch
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

    // ========================================
    // EXP counter STOP safety nets: subsequent result phases
    // ========================================

    // Touch's phase directly after points is the skill/job level list — stop there too.
    [HarmonyPatch(typeof(ResultMenuController_Touch), "ShowSkillLevelsInit")]
    internal static class ResultMenuController_Touch_ShowSkillLevelsInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { BattleResultState.StopExpCounterIfPlaying(); }
            catch (Exception ex) { MelonLogger.Warning($"Error in ShowSkillLevelsInit patch (Touch): {ex.Message}"); }
        }
    }

    // EndWaitInit fires when the results sequence closes — guaranteed final catch-all so the
    // tone can never be left stuck even if completion detection and the other phases are missed.
    [HarmonyPatch(typeof(ResultMenuController_KeyInput), "EndWaitInit")]
    internal static class ResultMenuController_KeyInput_EndWaitInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { BattleResultState.StopExpCounterIfPlaying(); }
            catch (Exception ex) { MelonLogger.Warning($"Error in EndWaitInit patch (KeyInput): {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ResultMenuController_Touch), "EndWaitInit")]
    internal static class ResultMenuController_Touch_EndWaitInit_Patch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { BattleResultState.StopExpCounterIfPlaying(); }
            catch (Exception ex) { MelonLogger.Warning($"Error in EndWaitInit patch (Touch): {ex.Message}"); }
        }
    }
}
