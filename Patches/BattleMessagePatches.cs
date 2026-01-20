using System;
using HarmonyLib;
using MelonLoader;
using System.Reflection;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using BattleUtility = Il2CppLast.Battle.BattleUtility;
using Il2CppLast.Systems;
using BattleUIManager = Il2CppLast.UI.BattleUIManager;
using SystemMessageWindowView = Il2CppLast.UI.SystemMessageWindowView;
using SystemMessageWindowController = Il2CppLast.UI.SystemMessageWindowController;
using SystemMessageWindowManager = Il2CppLast.Management.SystemMessageWindowManager;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using OwnedItemData = Il2CppLast.Data.User.OwnedItemData;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Global message deduplication to prevent the same message being spoken by multiple patches.
    /// Also tracks flee-in-progress state to suppress command menu during flee execution.
    /// </summary>
    public static class GlobalBattleMessageTracker
    {
        private const string CONTEXT = "Battle.Message";

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

            MelonLogger.Msg($"[{source}] {cleanMessage}");
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
            if (inProgress)
            {
                MelonLogger.Msg("[Battle] Flee in progress - suppressing command announcements");
            }
        }

        /// <summary>
        /// Clears the flee flag. Called when battle ends or new turn starts.
        /// </summary>
        public static void ClearFleeInProgress()
        {
            if (IsFleeInProgress)
            {
                IsFleeInProgress = false;
                MelonLogger.Msg("[Battle] Flee flag cleared");
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
    public static class ParameterActFunctionManagment_CreateActFunction_Patch
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
                        announcement = $"{actorName}, {actionName}";
                    }
                }
                else
                {
                    announcement = $"{actorName} attacks";
                }
                // Use object-based deduplication (not text-based) so different enemies
                // with the same name attacking in succession are both announced
                if (AnnouncementDeduplicator.ShouldAnnounce("BattleAction", battleActData))
                {
                    MelonLogger.Msg($"[BattleAction] {announcement}");
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
    /// Patch BattleBasicFunction.CreateDamageView for damage announcements.
    /// </summary>
    [HarmonyPatch(typeof(BattleBasicFunction), "CreateDamageView",
        new Type[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool) })]
    public static class BattleBasicFunction_CreateDamageView_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                string targetName = "Unknown";

                var playerData = data.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }

                var enemyData = data.TryCast<BattleEnemyData>();
                if (enemyData != null)
                {
                    string mesIdName = enemyData.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            targetName = localizedName;
                        }
                    }
                }

                string message;
                if (hitType == HitType.Miss || value == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (isRecovery)
                {
                    message = $"{targetName}: Recovered {value} HP";
                }
                else
                {
                    message = $"{targetName}: {value} damage";
                }

                MelonLogger.Msg($"[Damage] {message}");
                // Damage doesn't interrupt - queues after action announcement
                FFIII_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateDamageView patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch BattleConditionController.Add to announce status effects when applied.
    /// </summary>
    [HarmonyPatch(typeof(BattleConditionController), nameof(BattleConditionController.Add))]
    public static class BattleConditionController_Add_Patch
    {
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get target name
                string targetName = "Unknown";
                var playerData = battleUnitData.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }
                else
                {
                    var enemyData = battleUnitData.TryCast<BattleEnemyData>();
                    if (enemyData != null)
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                }

                // Get condition name from ID
                string conditionName = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    if (unitDataInfo?.Parameter != null)
                    {
                        var confirmedList = unitDataInfo.Parameter.ConfirmedConditionList();
                        if (confirmedList != null && confirmedList.Count > 0)
                        {
                            foreach (var condition in confirmedList)
                            {
                                if (condition != null && condition.Id == id)
                                {
                                    string conditionMesId = condition.MesIdName;

                                    // Skip conditions with no message ID (internal/hidden statuses)
                                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                                    {
                                        return;
                                    }

                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                        if (!string.IsNullOrEmpty(localizedConditionName))
                                        {
                                            conditionName = localizedConditionName;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    if (conditionName == null)
                    {
                        // Don't announce unknown statuses
                        return;
                    }
                }
                catch
                {
                    return;
                }

                string announcement = $"{targetName}: {conditionName}";

                // Skip duplicates
                if (announcement == lastAnnouncement) return;
                lastAnnouncement = announcement;

                MelonLogger.Msg($"[Status] {announcement}");
                // Status doesn't interrupt
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.Add patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Resets message tracking state.
    /// </summary>
    public static class BattleMessageReset
    {
        public static void ResetState()
        {
            GlobalBattleMessageTracker.Reset();
        }
    }

    /// <summary>
    /// Manual patches for battle system messages (escape, back attack, etc.)
    /// These use BattleUtility.SetSystemMessageAtKey which displays messages in battle.
    /// </summary>
    public static class BattleSystemMessagePatches
    {
        /// <summary>
        /// Applies manual Harmony patches for battle system messages.
        /// Must be called from main mod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle System Message] Applying patches...");

                // Patch BattleUtility.SetSystemMessageAtKey(string messageConclusionKey)
                // This is the static method that shows system messages like "The party escaped!"
                Type battleUtilityType = typeof(BattleUtility);

                // Get the single-parameter overload (just the message key)
                var setSystemMessageMethod = AccessTools.Method(battleUtilityType, "SetSystemMessageAtKey", new Type[] { typeof(string) });

                if (setSystemMessageMethod != null)
                {
                    var postfix = typeof(BattleSystemMessagePatches).GetMethod("SetSystemMessageAtKey_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSystemMessageMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle System Message] Patched BattleUtility.SetSystemMessageAtKey(string)");
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SetSystemMessageAtKey(string) method not found");
                }

                // Also patch the 3-parameter overload which may be the one actually used
                var setSystemMessage3ParamMethod = AccessTools.Method(battleUtilityType, "SetSystemMessageAtKey",
                    new Type[] { typeof(BattleUIManager), typeof(MessageManager), typeof(string) });

                if (setSystemMessage3ParamMethod != null)
                {
                    var postfix3 = typeof(BattleSystemMessagePatches).GetMethod("SetSystemMessageAtKey3_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSystemMessage3ParamMethod, postfix: new HarmonyMethod(postfix3));
                    MelonLogger.Msg("[Battle System Message] Patched BattleUtility.SetSystemMessageAtKey(BattleUIManager, MessageManager, string)");
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SetSystemMessageAtKey(3-param) method not found");
                }

                // Patch SystemMessageWindowView.SetMessage(string message) - receives actual localized text
                // This is the view that displays system messages like "The party escaped!", "Back Attack!", etc.
                Type systemMessageViewType = typeof(SystemMessageWindowView);
                var setMessageViewMethod = AccessTools.Method(systemMessageViewType, "SetMessage", new Type[] { typeof(string) });

                if (setMessageViewMethod != null)
                {
                    var viewPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageView_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageViewMethod, postfix: new HarmonyMethod(viewPostfix));
                    MelonLogger.Msg("[Battle System Message] Patched SystemMessageWindowView.SetMessage(string)");
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SystemMessageWindowView.SetMessage method not found");
                }

                // Patch SystemMessageWindowController.SetMessage(string messageId, TextAnchor) - receives message ID
                // This controller handles system messages and passes them to the view
                Type systemMessageControllerType = typeof(SystemMessageWindowController);
                var setMessageControllerMethod = AccessTools.Method(systemMessageControllerType, "SetMessage");

                if (setMessageControllerMethod != null)
                {
                    var controllerPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageController_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageControllerMethod, postfix: new HarmonyMethod(controllerPostfix));
                    MelonLogger.Msg("[Battle System Message] Patched SystemMessageWindowController.SetMessage");
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SystemMessageWindowController.SetMessage method not found");
                }

                // Patch SystemMessageWindowManager.SetMessage(string messageId) - the singleton manager
                Type systemMessageManagerType = typeof(SystemMessageWindowManager);
                var setMessageManagerMethod = AccessTools.Method(systemMessageManagerType, "SetMessage");

                if (setMessageManagerMethod != null)
                {
                    var managerPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageManager_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageManagerMethod, postfix: new HarmonyMethod(managerPostfix));
                    MelonLogger.Msg("[Battle System Message] Patched SystemMessageWindowManager.SetMessage");
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SystemMessageWindowManager.SetMessage method not found");
                }

                MelonLogger.Msg("[Battle System Message] Patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle System Message] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for BattleUtility.SetSystemMessageAtKey - announces battle system messages.
        /// </summary>
        public static void SetSystemMessageAtKey_Postfix(string messageConclusionKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageConclusionKey))
                    return;

                MelonLogger.Msg($"[Battle System Message] SetSystemMessageAtKey called with key: {messageConclusionKey}");

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageConclusionKey);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();
                        MelonLogger.Msg($"[Battle System Message] {messageConclusionKey} -> {cleanMessage}");

                        // Clear flee flag if this is an escape result message
                        if (messageConclusionKey.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        // Announce the message
                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "BattleSystemMessage");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle System Message] Error in SetSystemMessageAtKey postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for 3-parameter BattleUtility.SetSystemMessageAtKey overload.
        /// Only uses the string parameter to avoid IL2CPP marshaling issues.
        /// </summary>
        public static void SetSystemMessageAtKey3_Postfix(string messageConclusionKey)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageConclusionKey))
                    return;

                MelonLogger.Msg($"[Battle System Message] SetSystemMessageAtKey(3-param) called with key: {messageConclusionKey}");

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageConclusionKey);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();
                        MelonLogger.Msg($"[Battle System Message] {messageConclusionKey} -> {cleanMessage}");

                        // Clear flee flag if this is an escape result message
                        if (messageConclusionKey.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        // Announce the message
                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "BattleSystemMessage");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle System Message] Error in SetSystemMessageAtKey3 postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SystemMessageWindowManager.SetMessage - the singleton entry point.
        /// </summary>
        public static void SystemMessageManager_SetMessage_Postfix(string messageId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                    return;

                MelonLogger.Msg($"[System Message Manager] SetMessage called with ID: {messageId}");

                // Skip location messages - these are handled by CheckMapTransition
                if (messageId.StartsWith("MSG_LOCATION_", StringComparison.OrdinalIgnoreCase))
                {
                    var msgMgr = MessageManager.Instance;
                    if (msgMgr != null)
                    {
                        string locMessage = msgMgr.GetMessage(messageId);
                        if (!LocationMessageTracker.ShouldAnnounceFadeMessage(locMessage))
                        {
                            MelonLogger.Msg($"[System Message Manager] Skipping location message (handled by map transition)");
                            return;
                        }
                    }
                }

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();
                        MelonLogger.Msg($"[System Message Manager] {messageId} -> {cleanMessage}");

                        // Clear flee flag if this looks like an escape message
                        if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        // Announce the message
                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "SystemMessageManager");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[System Message Manager] Error in SetMessage postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SystemMessageWindowController.SetMessage - receives message ID.
        /// Looks up localized text and announces it.
        /// </summary>
        public static void SystemMessageController_SetMessage_Postfix(string messageId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                    return;

                MelonLogger.Msg($"[System Message Controller] SetMessage called with ID: {messageId}");

                // Skip location messages - these are handled by CheckMapTransition
                if (messageId.StartsWith("MSG_LOCATION_", StringComparison.OrdinalIgnoreCase))
                {
                    var msgMgr = MessageManager.Instance;
                    if (msgMgr != null)
                    {
                        string locMessage = msgMgr.GetMessage(messageId);
                        if (!LocationMessageTracker.ShouldAnnounceFadeMessage(locMessage))
                        {
                            MelonLogger.Msg($"[System Message Controller] Skipping location message (handled by map transition)");
                            return;
                        }
                    }
                }

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();
                        MelonLogger.Msg($"[System Message Controller] {messageId} -> {cleanMessage}");

                        // Clear flee flag if this looks like an escape message
                        if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        // Announce the message
                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "SystemMessageController");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[System Message Controller] Error in SetMessage postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SystemMessageWindowView.SetMessage - announces the actual displayed text.
        /// This catches all system messages including "The party escaped!", "Back Attack!", etc.
        /// </summary>
        public static void SystemMessageView_SetMessage_Postfix(string message)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(message))
                    return;

                string cleanMessage = message.Trim();
                MelonLogger.Msg($"[System Message View] SetMessage called: {cleanMessage}");

                // Clear flee flag if this looks like an escape message
                if (cleanMessage.IndexOf("escape", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    cleanMessage.IndexOf("fled", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    GlobalBattleMessageTracker.ClearFleeInProgress();
                }

                // Announce the message
                GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "SystemMessageView");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[System Message View] Error in SetMessage postfix: {ex.Message}");
            }
        }
    }
}
