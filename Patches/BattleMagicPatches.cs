using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using BattleFrequencyAbilityInfomationController = Il2CppSerial.FF3.UI.KeyInput.BattleFrequencyAbilityInfomationController;
using BattleAbilityInfomationContentController = Il2CppLast.UI.KeyInput.BattleAbilityInfomationContentController;
using BattleCommandSelectController = Il2CppLast.UI.KeyInput.BattleCommandSelectController;
using OwnedAbility = Il2CppLast.Data.User.OwnedAbility;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using GameCursor = Il2CppLast.UI.Cursor;
using MessageManager = Il2CppLast.Management.MessageManager;
using AbilityLevelType = Il2CppLast.Defaine.Master.AbilityLevelType;
using PlayerCharacterParameter = Il2CppLast.Data.PlayerCharacterParameter;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patch application for battle magic menu.
    /// </summary>
    public static class BattleMagicPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Magic] Applying battle magic menu patches...");

                var controllerType = typeof(BattleFrequencyAbilityInfomationController);

                // Find SelectContent(List<BattleAbilityInfomationContentController>, int, Cursor)
                MethodInfo selectContentMethod = null;
                var methods = controllerType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                foreach (var m in methods)
                {
                    if (m.Name == "SelectContent")
                    {
                        var parameters = m.GetParameters();
                        if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(int))
                        {
                            selectContentMethod = m;
                            MelonLogger.Msg($"[Battle Magic] Found SelectContent method");
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleMagicSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Magic] Patched SelectContent successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Magic] SelectContent method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error applying patches: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// State tracking for battle magic menu.
    /// </summary>
    public static class BattleMagicMenuState
    {
        /// <summary>
        /// True when battle magic menu is active and handling announcements.
        /// </summary>
        public static bool IsActive { get; set; } = false;

        // State machine offsets for BattleCommandSelectController
        private const int OFFSET_STATE_MACHINE = 0x48;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // BattleCommandSelectController.State values
        private const int STATE_NORMAL = 1;  // Command menu active
        private const int STATE_EXTRA = 2;   // Sub-command menu active

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Validates controller is active AND we're not back at command selection.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive) return false;

            try
            {
                // First check if battle magic controller is still active
                var magicController = UnityEngine.Object.FindObjectOfType<BattleFrequencyAbilityInfomationController>();
                if (magicController == null || !magicController.gameObject.activeInHierarchy)
                {
                    Reset();
                    return false;
                }

                // Also check if command select controller is active and in command state
                // If so, we've returned to command menu - clear magic state
                var cmdController = UnityEngine.Object.FindObjectOfType<BattleCommandSelectController>();
                if (cmdController != null && cmdController.gameObject.activeInHierarchy)
                {
                    int state = GetCommandState(cmdController);
                    if (state == STATE_NORMAL || state == STATE_EXTRA)
                    {
                        // Command menu is active, we're no longer in magic selection
                        Reset();
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        /// <summary>
        /// Read current state from BattleCommandSelectController's state machine.
        /// </summary>
        private static int GetCommandState(BattleCommandSelectController controller)
        {
            try
            {
                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return -1;

                unsafe
                {
                    IntPtr smPtr = *(IntPtr*)((byte*)ptr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (smPtr == IntPtr.Zero) return -1;

                    IntPtr currentPtr = *(IntPtr*)((byte*)smPtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentPtr == IntPtr.Zero) return -1;

                    return *(int*)((byte*)currentPtr.ToPointer() + OFFSET_STATE_TAG);
                }
            }
            catch { return -1; }
        }

        private static string lastAnnouncement = "";
        private static float lastAnnouncementTime = 0f;

        public static bool ShouldAnnounce(string announcement)
        {
            float currentTime = UnityEngine.Time.time;
            if (announcement == lastAnnouncement && (currentTime - lastAnnouncementTime) < 0.15f)
                return false;

            lastAnnouncement = announcement;
            lastAnnouncementTime = currentTime;
            return true;
        }

        public static void Reset()
        {
            IsActive = false;
            lastAnnouncement = "";
            lastAnnouncementTime = 0f;
            CurrentPlayer = null;
        }

        // Cache the current player data for charge lookup
        public static BattlePlayerData CurrentPlayer { get; set; } = null;
    }

    /// <summary>
    /// Patch for battle magic selection.
    /// Announces spell name, charges, and description when navigating magic in battle.
    /// SelectContent signature: SelectContent(List&lt;BattleAbilityInfomationContentController&gt; contents, int index, Cursor targetCursor)
    /// </summary>
    public static class BattleMagicSelectContent_Patch
    {
        public static void Postfix(
            object __instance,
            Il2CppSystem.Collections.Generic.List<BattleAbilityInfomationContentController> contents,
            int index,
            GameCursor targetCursor)
        {
            try
            {
                // NOTE: Don't set IsActive here - wait until after validation
                // Setting it early causes suppression during menu transitions

                if (__instance == null || contents == null)
                    return;

                MelonLogger.Msg($"[Battle Magic] SelectContent called, index: {index}, contents count: {contents.Count}");

                // Validate index
                if (index < 0 || index >= contents.Count)
                {
                    MelonLogger.Msg($"[Battle Magic] Index {index} out of range");
                    return;
                }

                // Get content at index
                var contentController = contents[index];
                if (contentController == null)
                {
                    // Empty slot
                    if (BattleMagicMenuState.ShouldAnnounce("Empty"))
                    {
                        FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                        BattleMagicMenuState.IsActive = true;
                        MelonLogger.Msg("[Battle Magic] Announcing: Empty");
                        FFIII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
                    }
                    return;
                }

                // Get ability data
                var ability = contentController.Data;
                if (ability == null)
                {
                    // Empty slot
                    if (BattleMagicMenuState.ShouldAnnounce("Empty"))
                    {
                        FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                        BattleMagicMenuState.IsActive = true;
                        MelonLogger.Msg("[Battle Magic] Announcing: Empty");
                        FFIII_ScreenReaderMod.SpeakText("Empty", interrupt: true);
                    }
                    return;
                }

                // Format and announce
                string announcement = FormatAbilityAnnouncement(ability);
                if (string.IsNullOrEmpty(announcement))
                    return;

                // Skip duplicates
                if (!BattleMagicMenuState.ShouldAnnounce(announcement))
                    return;

                // Set active state AFTER validation - menu is confirmed open and we have valid data
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("BattleMagic");
                BattleMagicMenuState.IsActive = true;

                MelonLogger.Msg($"[Battle Magic] Announcing: {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Format ability data into announcement string.
        /// Format: "Spell Name: X/Y charges. Description"
        /// </summary>
        private static string FormatAbilityAnnouncement(OwnedAbility ability)
        {
            try
            {
                // Get spell name
                string name = null;
                string mesIdName = ability.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        name = messageManager.GetMessage(mesIdName, false);
                    }
                }

                if (string.IsNullOrEmpty(name))
                    return null;

                name = TextUtils.StripIconMarkup(name);
                if (string.IsNullOrEmpty(name))
                    return null;

                string announcement = name;

                // Try to get spell level and charges
                try
                {
                    var abilityData = ability.Ability;
                    if (abilityData != null)
                    {
                        int spellLevel = abilityData.AbilityLv;
                        if (spellLevel > 0 && spellLevel <= 8)
                        {
                            var charges = GetChargesForLevel(spellLevel);
                            if (charges.max > 0)
                            {
                                announcement += $": {charges.current}/{charges.max} charges";
                            }
                        }
                    }
                }
                catch { }

                // Try to get description
                try
                {
                    string mesIdDesc = ability.MesIdDescription;
                    if (!string.IsNullOrEmpty(mesIdDesc))
                    {
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null)
                        {
                            string description = messageManager.GetMessage(mesIdDesc, false);
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                description = TextUtils.StripIconMarkup(description);
                                if (!string.IsNullOrWhiteSpace(description))
                                {
                                    announcement += ". " + description;
                                }
                            }
                        }
                    }
                }
                catch { }

                return announcement;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error formatting announcement: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets current and max charges for a given spell level.
        /// Tries to get character data from the cached player.
        /// </summary>
        private static (int current, int max) GetChargesForLevel(int level)
        {
            try
            {
                var player = BattleMagicMenuState.CurrentPlayer;
                if (player == null)
                {
                    // Try to find from scene
                    var controller = UnityEngine.Object.FindObjectOfType<BattleFrequencyAbilityInfomationController>();
                    if (controller != null)
                    {
                        // Read selectedBattlePlayerData from base class at offset 0x28 (KeyInput variant)
                        IntPtr controllerPtr = controller.Pointer;
                        if (controllerPtr != IntPtr.Zero)
                        {
                            unsafe
                            {
                                IntPtr playerPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + 0x30);
                                if (playerPtr != IntPtr.Zero)
                                {
                                    player = new BattlePlayerData(playerPtr);
                                    BattleMagicMenuState.CurrentPlayer = player;
                                }
                            }
                        }
                    }
                }

                if (player == null)
                    return (0, 0);

                var ownedCharData = player.ownedCharacterData;
                if (ownedCharData == null)
                    return (0, 0);

                var param = ownedCharData.Parameter as PlayerCharacterParameter;
                if (param == null)
                    return (0, 0);

                // Get current charges
                int current = 0;
                var currentList = param.CurrentMpCountList;
                if (currentList != null && currentList.ContainsKey(level))
                {
                    current = currentList[level];
                }

                // Get max charges
                int max = 0;
                try
                {
                    max = param.ConfirmedMaxMpCount((AbilityLevelType)level);
                }
                catch
                {
                    var baseMaxList = param.BaseMaxMpCountList;
                    if (baseMaxList != null && baseMaxList.ContainsKey(level))
                    {
                        max = baseMaxList[level];
                    }
                }

                return (current, max);
            }
            catch
            {
                return (0, 0);
            }
        }
    }
}
