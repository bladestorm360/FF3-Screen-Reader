using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// FF3 types
using MessageManager = Il2CppLast.Management.MessageManager;
using BattleController = Il2CppLast.Battle.BattleController;
using BattlePopPlug = Il2CppLast.Battle.BattlePopPlug;
using BattlePlugManager = Il2CppLast.Battle.BattlePlugManager;
using BattleUIManager = Il2CppLast.UI.BattleUIManager;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Battle start patches for announcing battle conditions.
    /// Handles: "Preemptive Attack!", "Back Attack!", "Ambush!", etc.
    /// </summary>
    internal static class BattleStartPatches
    {

        private static bool announcedBattleStart = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch StartPreeMptiveMes for battle condition announcements
                PatchStartPreeMptiveMes(harmony);

                // Patch ExitPreeMptive to reset state
                PatchExitPreeMptive(harmony);

                // Debug: Patch StartEscape to trace escape flow
                PatchStartEscape(harmony);

                // Debug: Patch UpdateEscape to trace escape flow
                PatchUpdateEscape(harmony);

                // Debug: Patch BattleUIManager.SetSystemMessage to find system message display
                PatchBattleUIManagerSetSystemMessage(harmony);

                // Debug: Patch EndEscapeFadeOut to trace escape completion
                PatchEndEscapeFadeOut(harmony);

                // Patch BattleCommandMessageController.SetMessage for battle system messages (defeat, escape, etc.)
                PatchBattleCommandMessage(harmony);

                MelonLogger.Msg("[Battle Start] Battle start patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Battle Start] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch StartPreeMptiveMes to announce battle start condition.
        /// </summary>
        private static void PatchStartPreeMptiveMes(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);

                // Use AccessTools.Method for IL2CPP compatibility (not Type.GetMethod)
                var method = AccessTools.Method(controllerType, "StartPreeMptiveMes");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(StartPreeMptiveMes_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] StartPreeMptiveMes method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching StartPreeMptiveMes: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ExitPreeMptive to reset announcement state.
        /// </summary>
        private static void PatchExitPreeMptive(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);

                // Use AccessTools.Method for IL2CPP compatibility (not Type.GetMethod)
                var method = AccessTools.Method(controllerType, "ExitPreeMptive");

                if (method != null)
                {
                    var prefix = typeof(BattleStartPatches).GetMethod(
                        nameof(ExitPreeMptive_Prefix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] ExitPreeMptive method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching ExitPreeMptive: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for StartPreeMptiveMes - announces battle start condition.
        /// </summary>
        public static void StartPreeMptiveMes_Postfix(BattleController __instance)
        {
            try
            {
                if (announcedBattleStart) return;
                announcedBattleStart = true;

                // Get the preemptive state
                int state = GetPreeMptiveState(__instance);

                // Get announcement based on state
                string announcement = GetBattleStartAnnouncement(state);
                if (string.IsNullOrEmpty(announcement)) return;

                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in StartPreeMptiveMes_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Prefix for ExitPreeMptive - resets announcement state.
        /// </summary>
        public static void ExitPreeMptive_Prefix()
        {
            // Reset for next battle
            announcedBattleStart = false;
        }

        /// <summary>
        /// Get the preemptive state from BattlePlugManager (singleton).
        /// Uses direct IL2CPP access instead of .NET reflection.
        /// </summary>
        private static int GetPreeMptiveState(BattleController controller)
        {
            try
            {
                // BattlePopPlug is stored in BattlePlugManager singleton, not BattleController
                var plugManager = BattlePlugManager.Instance();
                if (plugManager == null)
                {
                    MelonLogger.Warning("[Battle Start] BattlePlugManager.Instance() is null");
                    return IL2CppOffsets.BattleStart.STATE_NORMAL;
                }

                // Direct IL2CPP property access (not .NET reflection)
                var battlePopPlug = plugManager.BattlePopPlug;
                if (battlePopPlug == null)
                {
                    MelonLogger.Warning("[Battle Start] BattlePopPlug is null");
                    return IL2CppOffsets.BattleStart.STATE_NORMAL;
                }

                // Direct method call on IL2CPP type
                var result = battlePopPlug.GetResult();
                return (int)result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error getting PreeMptiveState: {ex.Message}");
            }

            return IL2CppOffsets.BattleStart.STATE_NORMAL;
        }

        /// <summary>
        /// Get announcement text for battle start condition.
        /// </summary>
        private static string GetBattleStartAnnouncement(int state)
        {
            switch (state)
            {
                case IL2CppOffsets.BattleStart.STATE_PREEMPTIVE:
                    return "Preemptive attack!";
                case IL2CppOffsets.BattleStart.STATE_BACK_ATTACK:
                    return "Back attack!";
                case IL2CppOffsets.BattleStart.STATE_ENEMY_PREEMPTIVE:
                    return "Ambush!";
                case IL2CppOffsets.BattleStart.STATE_ENEMY_SIDE_ATTACK:
                    return "Enemy side attack!";
                case IL2CppOffsets.BattleStart.STATE_SIDE_ATTACK:
                    return "Side attack!";
                case IL2CppOffsets.BattleStart.STATE_NORMAL:
                case IL2CppOffsets.BattleStart.STATE_NON:
                default:
                    return null; // No announcement for normal battles
            }
        }

        /// <summary>
        /// Reset state (call at battle end).
        /// </summary>
        public static void ResetState()
        {
            announcedBattleStart = false;
        }

        /// <summary>
        /// Debug: Patch StartEscape to trace escape flow.
        /// </summary>
        private static void PatchStartEscape(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);
                var method = AccessTools.Method(controllerType, "StartEscape");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(StartEscape_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] StartEscape method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching StartEscape: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug: Patch UpdateEscape to trace escape flow.
        /// </summary>
        private static void PatchUpdateEscape(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);
                var method = AccessTools.Method(controllerType, "UpdateEscape");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(UpdateEscape_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] UpdateEscape method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching UpdateEscape: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug postfix for StartEscape.
        /// </summary>
        public static void StartEscape_Postfix()
        {
        }

        /// <summary>
        /// Debug postfix for UpdateEscape - called each frame during escape.
        /// Only log once to avoid spam.
        /// </summary>
        public static void UpdateEscape_Postfix()
        {
        }

        /// <summary>
        /// Debug: Patch BattleUIManager.SetSystemMessage to find system message display.
        /// </summary>
        private static void PatchBattleUIManagerSetSystemMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                var uiManagerType = typeof(BattleUIManager);
                var method = AccessTools.Method(uiManagerType, "SetSystemMessage");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(BattleUIManager_SetSystemMessage_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] BattleUIManager.SetSystemMessage method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching BattleUIManager.SetSystemMessage: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug postfix for BattleUIManager.SetSystemMessage - logs when system messages are displayed.
        /// </summary>
        public static void BattleUIManager_SetSystemMessage_Postfix(string messageId)
        {
        }

        /// <summary>
        /// Debug: Patch EndEscapeFadeOut to trace escape completion.
        /// </summary>
        private static void PatchEndEscapeFadeOut(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(BattleController);
                var method = AccessTools.Method(controllerType, "EndEscapeFadeOut");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(EndEscapeFadeOut_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] EndEscapeFadeOut method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching EndEscapeFadeOut: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug postfix for EndEscapeFadeOut.
        /// </summary>
        public static void EndEscapeFadeOut_Postfix()
        {
        }

        /// <summary>
        /// Patch BattleCommandMessageController.SetMessage for system messages like "The party was defeated".
        /// Matches FF1 implementation.
        /// </summary>
        private static void PatchBattleCommandMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                // KeyInput version (keyboard/gamepad)
                var keyInputType = FindType("Il2CppLast.UI.KeyInput.BattleCommandMessageController");
                if (keyInputType != null)
                {
                    var setMessageMethod = AccessTools.Method(keyInputType, "SetMessage");
                    if (setMessageMethod != null)
                    {
                        var postfix = typeof(BattleStartPatches).GetMethod(
                            nameof(BattleCommandMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setMessageMethod, postfix: new HarmonyMethod(postfix));
                    }
                }

                // Touch version (SetSystemMessage and SetCommandMessage)
                var touchType = FindType("Il2CppLast.UI.Touch.BattleCommandMessageController");
                if (touchType != null)
                {
                    var setSystemMsgMethod = AccessTools.Method(touchType, "SetSystemMessage");
                    if (setSystemMsgMethod != null)
                    {
                        var postfix = typeof(BattleStartPatches).GetMethod(
                            nameof(BattleCommandMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setSystemMsgMethod, postfix: new HarmonyMethod(postfix));
                    }

                    var setCommandMsgMethod = AccessTools.Method(touchType, "SetCommandMessage");
                    if (setCommandMsgMethod != null)
                    {
                        var postfix = typeof(BattleStartPatches).GetMethod(
                            nameof(BattleCommandMessage_Postfix), BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(setCommandMsgMethod, postfix: new HarmonyMethod(postfix));
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching BattleCommandMessageController: {ex.Message}");
            }
        }

        private static string lastBattleCommandMessage = "";

        /// <summary>
        /// Postfix for BattleCommandMessageController.SetMessage/SetSystemMessage.
        /// Uses __0 instead of named string param to avoid IL2CPP crash.
        /// Whitelist filter: only announces battle conclusion messages, not spell/ability names.
        /// </summary>
        public static void BattleCommandMessage_Postfix(object __0)
        {
            try
            {
                // __0 is the message string (using __0 to avoid IL2CPP string param crash)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message)) return;

                // WHITELIST: Only announce battle conclusion/system messages
                // Skip spell/ability names (already announced by CreateActFunction)
                bool isSystemMessage =
                    message.Contains("defeated", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("victory", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("escape", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("fled", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("annihilat", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("wiped", StringComparison.OrdinalIgnoreCase);

                if (!isSystemMessage) return;

                // Deduplicate
                if (message == lastBattleCommandMessage) return;
                lastBattleCommandMessage = message;

                // Clean up the message
                string cleanMessage = TextUtils.StripIconMarkup(message);
                cleanMessage = cleanMessage.Replace("\n", " ").Replace("\r", " ").Trim();
                while (cleanMessage.Contains("  "))
                    cleanMessage = cleanMessage.Replace("  ", " ");

                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Use interrupt for defeat message
                bool isDefeatMessage = cleanMessage.Contains("defeated", StringComparison.OrdinalIgnoreCase);

                FFIII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: isDefeatMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in BattleCommandMessage_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
