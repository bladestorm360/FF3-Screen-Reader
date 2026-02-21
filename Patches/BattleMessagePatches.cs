using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;
using ContentUtitlity = Il2CppLast.Systems.ContentUtitlity;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Global message deduplication to prevent the same message being spoken by multiple patches.
    /// Also tracks flee-in-progress state to suppress command menu during flee execution.
    /// </summary>
    internal static class GlobalBattleMessageTracker
    {
        private const string CONTEXT = AnnouncementContexts.BATTLE_MESSAGE;

        // Flee-in-progress flag to suppress command menu announcements during flee
        public static bool IsFleeInProgress { get; private set; } = false;

        /// <summary>
        /// Try to announce a message, returning false if it was recently announced.
        /// Uses AnnouncementDeduplicator for simple equality-based deduplication.
        /// </summary>
        public static bool TryAnnounce(string message, string source)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            string cleanMessage = message.Trim();

            // Skip if same message (exact duplicate)
            if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT, cleanMessage))
            {
                return false;
            }

            // Battle actions don't interrupt - they queue
            FFIII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            return true;
        }

        /// <summary>
        /// Sets the flee-in-progress flag to suppress command menu announcements.
        /// </summary>
        public static void SetFleeInProgress(bool inProgress)
        {
            IsFleeInProgress = inProgress;
        }

        /// <summary>
        /// Clears the flee flag. Called when battle ends or new turn starts.
        /// </summary>
        public static void ClearFleeInProgress()
        {
            if (IsFleeInProgress)
            {
                IsFleeInProgress = false;
            }
        }

        /// <summary>
        /// Reset tracking (e.g., when battle ends).
        /// </summary>
        public static void Reset()
        {
            AnnouncementDeduplicator.Reset(CONTEXT);
            IsFleeInProgress = false;
        }
    }

    /// <summary>
    /// Patch ParameterActFunctionManagment.CreateActFunction to announce actor names with their actions.
    /// </summary>
    [HarmonyPatch(typeof(ParameterActFunctionManagment), nameof(ParameterActFunctionManagment.CreateActFunction))]
    internal static class ParameterActFunctionManagment_CreateActFunction_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleActData battleActData)
        {
            try
            {
                if (battleActData == null) return;

                string actorName = GetActorName(battleActData);
                string actionName = GetActionName(battleActData);

                if (string.IsNullOrEmpty(actorName)) return;

                // Check if this is a flee/escape command
                bool isFlee = IsFleeCommand(battleActData);

                string announcement;
                if (isFlee)
                {
                    // Set flee flag to suppress command menu announcements
                    GlobalBattleMessageTracker.SetFleeInProgress(true);
                    announcement = $"{actorName} flees";
                }
                else if (!string.IsNullOrEmpty(actionName))
                {
                    string actionLower = actionName.ToLower();
                    if (actionLower == "attack" || actionLower == "fight")
                    {
                        announcement = $"{actorName} attacks";
                    }
                    else if (actionLower == "defend" || actionLower == "guard")
                    {
                        announcement = $"{actorName} defends";
                    }
                    else if (actionLower == "item")
                    {
                        announcement = $"{actorName} uses item";
                    }
                    else
                    {
                        string cleanActionName = TextUtils.StripIconMarkup(actionName);
                        announcement = $"{actorName}, {cleanActionName}";
                    }
                }
                else
                {
                    announcement = $"{actorName} attacks";
                }
                // Use object-based deduplication (not text-based) so different enemies
                // with the same name attacking in succession are both announced
                if (AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.BATTLE_ACTION, battleActData))
                {
                    FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateActFunction patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if this action is a Flee/Escape command.
        /// </summary>
        private static bool IsFleeCommand(BattleActData actData)
        {
            if (actData?.Command == null)
                return false;

            try
            {
                // Check command MesIdName for escape-related IDs
                string mesIdName = actData.Command.MesIdName;
                if (!string.IsNullOrEmpty(mesIdName))
                {
                    // Common escape command message IDs
                    if (mesIdName.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mesIdName.IndexOf("FLEE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mesIdName.IndexOf("RUN", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }

                // Also check localized command name
                var messageManager = MessageManager.Instance;
                if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                {
                    string commandName = messageManager.GetMessage(mesIdName);
                    if (!string.IsNullOrEmpty(commandName))
                    {
                        if (commandName.Equals("Flee", StringComparison.OrdinalIgnoreCase) ||
                            commandName.Equals("Escape", StringComparison.OrdinalIgnoreCase) ||
                            commandName.Equals("Run", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception) { }

            return false;
        }

        private static string GetActorName(BattleActData battleActData)
        {
            try
            {
                var attackUnit = battleActData.AttackUnitData;
                return BattleUnitHelper.GetUnitName(attackUnit);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting actor name: {ex.Message}");
            }
            return null;
        }

        private static string GetActionName(BattleActData battleActData)
        {
            try
            {
                // Try to get item name first (for Item command)
                var itemList = battleActData.itemList;
                if (itemList != null && itemList.Count > 0)
                {
                    var ownedItem = itemList[0];
                    if (ownedItem != null)
                    {
                        string itemName = GetItemName(ownedItem);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            return itemName;
                        }
                    }
                }

                // Try to get the ability name (spells, skills)
                var abilityList = battleActData.abilityList;
                if (abilityList != null && abilityList.Count > 0)
                {
                    var ability = abilityList[0];
                    if (ability != null)
                    {
                        string abilityName = ContentUtitlity.GetAbilityName(ability);
                        if (!string.IsNullOrEmpty(abilityName))
                        {
                            return abilityName;
                        }
                    }
                }

                // Fall back to command name (Attack, Defend, etc.)
                var command = battleActData.Command;
                if (command != null)
                {
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string commandMesId = command.MesIdName;
                        if (!string.IsNullOrEmpty(commandMesId))
                        {
                            string localizedName = messageManager.GetMessage(commandMesId);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                return localizedName;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting action name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the localized name of an item from OwnedItemData.
        /// </summary>
        private static string GetItemName(OwnedItemData ownedItem)
        {
            try
            {
                // OwnedItemData has a Name property that returns the localized name
                string itemName = ownedItem.Name;
                if (!string.IsNullOrEmpty(itemName))
                {
                    // Strip icon markup (e.g., "[ICB]") from item name
                    itemName = TextUtils.StripIconMarkup(itemName);
                    if (!string.IsNullOrEmpty(itemName))
                    {
                        return itemName;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting item name: {ex.Message}");
            }
            return null;
        }
    }

    /// <summary>
    /// Resets message tracking state.
    /// </summary>
    internal static class BattleMessageReset
    {
        public static void ResetState()
        {
            GlobalBattleMessageTracker.Reset();
        }
    }
}
