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
    internal static class BattleMagicPatchesApplier
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
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
                            break;
                        }
                    }
                }

                if (selectContentMethod != null)
                {
                    var postfix = typeof(BattleMagicSelectContent_Patch)
                        .GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Magic] Patches applied");
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
    internal static class BattleMagicMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.BATTLE_MAGIC, "BattleMagic.Select");

        static BattleMagicMenuState()
        {
            _helper.RegisterResetHandler(() => { CurrentPlayer = null; });
        }

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }

        public static bool ShouldSuppress() => IsActive;
        public static bool ShouldAnnounce(string announcement) => _helper.ShouldAnnounce(announcement);

        // Cache the current player data for charge lookup
        public static BattlePlayerData CurrentPlayer { get; set; } = null;
    }

    /// <summary>
    /// Patch for battle magic selection.
    /// Announces spell name, charges, and description when navigating magic in battle.
    /// SelectContent signature: SelectContent(List&lt;BattleAbilityInfomationContentController&gt; contents, int index, Cursor targetCursor)
    /// </summary>
    internal static class BattleMagicSelectContent_Patch
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

                // Validate index
                if (index < 0 || index >= contents.Count)
                    return;

                // Get content at index
                var contentController = contents[index];
                if (contentController == null)
                {
                    // Empty slot
                    if (BattleMagicMenuState.ShouldAnnounce("Empty"))
                    {
                        MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_MAGIC);
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
                        MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_MAGIC);
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
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_MAGIC);

                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Magic] Error in SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Format ability data into announcement string.
        /// Format: "Spell Name: MP: X/Y. Description"
        /// </summary>
        private static string FormatAbilityAnnouncement(OwnedAbility ability)
        {
            try
            {
                // Get spell name
                string name = LocalizationHelper.GetText(ability.MesIdName);
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
                                announcement += $": MP: {charges.current}/{charges.max}";
                            }
                        }
                    }
                }
                catch { }

                // Try to get description
                try
                {
                    string description = LocalizationHelper.GetText(ability.MesIdDescription, stripMarkup: false);
                    announcement = AnnouncementBuilder.AppendDescription(announcement, description);
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
                    var controller = GameObjectCache.GetOrFind<BattleFrequencyAbilityInfomationController>();
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
