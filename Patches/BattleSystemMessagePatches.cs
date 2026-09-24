using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using FFIII_ScreenReader.Utils;
using BattleUtility = Il2CppLast.Battle.BattleUtility;
using BattleUIManager = Il2CppLast.UI.BattleUIManager;
using SystemMessageWindowView = Il2CppLast.UI.SystemMessageWindowView;
using SystemMessageWindowController = Il2CppLast.UI.SystemMessageWindowController;
using SystemMessageWindowManager = Il2CppLast.Management.SystemMessageWindowManager;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Manual patches for battle system messages (escape, back attack, etc.)
    /// These use BattleUtility.SetSystemMessageAtKey which displays messages in battle.
    /// </summary>
    internal static class BattleSystemMessagePatches
    {
        /// <summary>
        /// Applies manual Harmony patches for battle system messages.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type battleUtilityType = typeof(BattleUtility);

                // Patch 1-parameter overload
                var setSystemMessageMethod = AccessTools.Method(battleUtilityType, "SetSystemMessageAtKey", new Type[] { typeof(string) });
                if (setSystemMessageMethod != null)
                {
                    var postfix = typeof(BattleSystemMessagePatches).GetMethod("SetSystemMessageAtKey_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSystemMessageMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SetSystemMessageAtKey(string) method not found");
                }

                // Patch 3-parameter overload
                var setSystemMessage3ParamMethod = AccessTools.Method(battleUtilityType, "SetSystemMessageAtKey",
                    new Type[] { typeof(BattleUIManager), typeof(MessageManager), typeof(string) });
                if (setSystemMessage3ParamMethod != null)
                {
                    var postfix3 = typeof(BattleSystemMessagePatches).GetMethod("SetSystemMessageAtKey3_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setSystemMessage3ParamMethod, postfix: new HarmonyMethod(postfix3));
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SetSystemMessageAtKey(3-param) method not found");
                }

                // Patch SystemMessageWindowView.SetMessage
                Type systemMessageViewType = typeof(SystemMessageWindowView);
                var setMessageViewMethod = AccessTools.Method(systemMessageViewType, "SetMessage", new Type[] { typeof(string) });
                if (setMessageViewMethod != null)
                {
                    var viewPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageView_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageViewMethod, postfix: new HarmonyMethod(viewPostfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SystemMessageWindowView.SetMessage method not found");
                }

                // Patch SystemMessageWindowController.SetMessage
                Type systemMessageControllerType = typeof(SystemMessageWindowController);
                var setMessageControllerMethod = AccessTools.Method(systemMessageControllerType, "SetMessage");
                if (setMessageControllerMethod != null)
                {
                    var controllerPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageController_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageControllerMethod, postfix: new HarmonyMethod(controllerPostfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle System Message] SystemMessageWindowController.SetMessage method not found");
                }

                // Patch SystemMessageWindowManager.SetMessage
                Type systemMessageManagerType = typeof(SystemMessageWindowManager);
                var setMessageManagerMethod = AccessTools.Method(systemMessageManagerType, "SetMessage");
                if (setMessageManagerMethod != null)
                {
                    var managerPostfix = typeof(BattleSystemMessagePatches).GetMethod("SystemMessageManager_SetMessage_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setMessageManagerMethod, postfix: new HarmonyMethod(managerPostfix));
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

        // IL2CPP rule: string parameters are taken positionally (__N), never by name.

        /// <summary>Postfix for BattleUtility.SetSystemMessageAtKey(string messageConclusionKey).</summary>
        public static void SetSystemMessageAtKey_Postfix(string __0)
        {
            string messageConclusionKey = __0;
            try
            {
                if (string.IsNullOrWhiteSpace(messageConclusionKey))
                    return;

                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageConclusionKey);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();

                        if (messageConclusionKey.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

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
        /// Postfix for BattleUtility.SetSystemMessageAtKey(BattleUIManager, MessageManager, string
        /// messageConclusionKey): the key is the third parameter.
        /// </summary>
        public static void SetSystemMessageAtKey3_Postfix(string __2)
        {
            string messageConclusionKey = __2;
            try
            {
                if (string.IsNullOrWhiteSpace(messageConclusionKey))
                    return;

                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageConclusionKey);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();

                        if (messageConclusionKey.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "BattleSystemMessage");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle System Message] Error in SetSystemMessageAtKey3 postfix: {ex.Message}");
            }
        }

        /// <summary>Postfix for SystemMessageWindowManager.SetMessage(string messageId).</summary>
        public static void SystemMessageManager_SetMessage_Postfix(string __0)
        {
            string messageId = __0;
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                    return;

                // Skip location messages
                if (messageId.StartsWith("MSG_LOCATION_", StringComparison.OrdinalIgnoreCase))
                {
                    var msgMgr = MessageManager.Instance;
                    if (msgMgr != null)
                    {
                        string locMessage = msgMgr.GetMessage(messageId);
                        if (!LocationMessageTracker.ShouldAnnounceFadeMessage(locMessage))
                        {
                            return;
                        }
                    }
                }

                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();

                        if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        GlobalBattleMessageTracker.TryAnnounce(cleanMessage, "SystemMessageManager");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[System Message Manager] Error in SetMessage postfix: {ex.Message}");
            }
        }

        /// <summary>Postfix for SystemMessageWindowController.SetMessage(string messageId, TextAnchor).</summary>
        public static void SystemMessageController_SetMessage_Postfix(string __0)
        {
            string messageId = __0;
            try
            {
                if (string.IsNullOrWhiteSpace(messageId))
                    return;

                // Skip location messages
                if (messageId.StartsWith("MSG_LOCATION_", StringComparison.OrdinalIgnoreCase))
                {
                    var msgMgr = MessageManager.Instance;
                    if (msgMgr != null)
                    {
                        string locMessage = msgMgr.GetMessage(messageId);
                        if (!LocationMessageTracker.ShouldAnnounceFadeMessage(locMessage))
                        {
                            return;
                        }
                    }
                }

                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        string cleanMessage = message.Trim();

                        if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

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
        /// Postfix for SystemMessageWindowView.SetMessage(string message). Its native body (RVA
        /// 0x3C5AE0) is shared with 46 other string setters (SetName, SetNameText, SetShopNameText,
        /// SetDescriptionText, SetConditionText, ...), so this detour fires for all of them: act only
        /// when the object really is a SystemMessageWindowView.
        /// </summary>
        public static void SystemMessageView_SetMessage_Postfix(SystemMessageWindowView __instance, string __0)
        {
            string message = __0;
            try
            {
                if (!IsSystemMessageView(__instance))
                    return;
                if (string.IsNullOrWhiteSpace(message))
                    return;

                // The view only receives display text, so the flee flag is left to the message-id
                // hooks (ESCAPE keys) and the next turn's SetCommandData.
                GlobalBattleMessageTracker.TryAnnounce(message.Trim(), "SystemMessageView");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[System Message View] Error in SetMessage postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the native object's IL2CPP class is (or derives from) SystemMessageWindowView; the
        /// managed wrapper type proves nothing on a shared body.
        /// </summary>
        private static bool IsSystemMessageView(SystemMessageWindowView instance)
        {
            if ((object)instance == null || instance.Pointer == IntPtr.Zero)
                return false;
            IntPtr cls = Il2CppInterop.Runtime.Il2CppClassPointerStore<SystemMessageWindowView>.NativeClassPtr;
            return cls != IntPtr.Zero
                && Il2CppInterop.Runtime.IL2CPP.il2cpp_class_is_assignable_from(
                    cls, Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance.Pointer));
        }
    }
}
