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
    public static class BattleStartPatches
    {
        // PreeMptiveState enum values
        private const int STATE_NON = -1;
        private const int STATE_NORMAL = 0;
        private const int STATE_PREEMPTIVE = 1;
        private const int STATE_BACK_ATTACK = 2;
        private const int STATE_ENEMY_PREEMPTIVE = 3;
        private const int STATE_ENEMY_SIDE_ATTACK = 4;
        private const int STATE_SIDE_ATTACK = 5;

        private static bool announcedBattleStart = false;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Start] Applying battle start patches...");

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

                // Debug: Patch BattleUIManager.SetCommadnMessage (note typo in game code)
                PatchBattleUIManagerSetCommadnMessage(harmony);

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
                    MelonLogger.Msg("[Battle Start] Patched StartPreeMptiveMes successfully");
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
                    MelonLogger.Msg("[Battle Start] Patched ExitPreeMptive successfully");
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
                MelonLogger.Msg($"[Battle Start] PreeMptiveState = {state}");

                // Get announcement based on state
                string announcement = GetBattleStartAnnouncement(state);
                if (string.IsNullOrEmpty(announcement)) return;

                MelonLogger.Msg($"[Battle Start] Announcing: {announcement}");
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
                    return STATE_NORMAL;
                }

                // Direct IL2CPP property access (not .NET reflection)
                var battlePopPlug = plugManager.BattlePopPlug;
                if (battlePopPlug == null)
                {
                    MelonLogger.Warning("[Battle Start] BattlePopPlug is null");
                    return STATE_NORMAL;
                }

                // Direct method call on IL2CPP type
                var result = battlePopPlug.GetResult();
                return (int)result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error getting PreeMptiveState: {ex.Message}");
            }

            return STATE_NORMAL;
        }

        /// <summary>
        /// Get announcement text for battle start condition.
        /// </summary>
        private static string GetBattleStartAnnouncement(int state)
        {
            switch (state)
            {
                case STATE_PREEMPTIVE:
                    return "Preemptive attack!";
                case STATE_BACK_ATTACK:
                    return "Back attack!";
                case STATE_ENEMY_PREEMPTIVE:
                    return "Ambush!";
                case STATE_ENEMY_SIDE_ATTACK:
                    return "Enemy side attack!";
                case STATE_SIDE_ATTACK:
                    return "Side attack!";
                case STATE_NORMAL:
                case STATE_NON:
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
                    MelonLogger.Msg("[Battle Start] Patched StartEscape (debug)");
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
                    MelonLogger.Msg("[Battle Start] Patched UpdateEscape (debug)");
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
            MelonLogger.Msg("[Battle Debug] StartEscape called - party is escaping!");
        }

        /// <summary>
        /// Debug postfix for UpdateEscape - called each frame during escape.
        /// Only log once to avoid spam.
        /// </summary>
        private static bool loggedUpdateEscape = false;
        public static void UpdateEscape_Postfix()
        {
            if (!loggedUpdateEscape)
            {
                MelonLogger.Msg("[Battle Debug] UpdateEscape called - escape in progress");
                loggedUpdateEscape = true;
            }
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
                    MelonLogger.Msg("[Battle Start] Patched BattleUIManager.SetSystemMessage (debug)");
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
            MelonLogger.Msg($"[Battle Debug] BattleUIManager.SetSystemMessage called with messageId: {messageId}");
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
                    MelonLogger.Msg("[Battle Start] Patched EndEscapeFadeOut (debug)");
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
            MelonLogger.Msg("[Battle Debug] EndEscapeFadeOut called - escape sequence completing!");
        }

        /// <summary>
        /// Debug: Patch BattleUIManager.SetCommadnMessage (note typo in game code).
        /// </summary>
        private static void PatchBattleUIManagerSetCommadnMessage(HarmonyLib.Harmony harmony)
        {
            try
            {
                var uiManagerType = typeof(BattleUIManager);
                var method = AccessTools.Method(uiManagerType, "SetCommadnMessage");

                if (method != null)
                {
                    var postfix = typeof(BattleStartPatches).GetMethod(
                        nameof(BattleUIManager_SetCommadnMessage_Postfix),
                        BindingFlags.Public | BindingFlags.Static
                    );

                    harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Start] Patched BattleUIManager.SetCommadnMessage (debug)");
                }
                else
                {
                    MelonLogger.Warning("[Battle Start] BattleUIManager.SetCommadnMessage method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error patching BattleUIManager.SetCommadnMessage: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for BattleUIManager.SetCommadnMessage - announces battle system messages.
        /// Handles: "The party escaped!", individual flee messages, etc.
        /// </summary>
        public static void BattleUIManager_SetCommadnMessage_Postfix(string messageId)
        {
            try
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                string message = messageManager.GetMessage(messageId);
                if (string.IsNullOrEmpty(message)) return;

                string clean = TextUtils.StripIconMarkup(message);
                if (string.IsNullOrEmpty(clean)) return;

                // Use deduplication to prevent repeat announcements
                if (AnnouncementDeduplicator.ShouldAnnounce("BattleSystemMessage", clean))
                {
                    MelonLogger.Msg($"[Battle System] Announcing: {clean}");
                    FFIII_ScreenReaderMod.SpeakText(clean, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle System] Error in SetCommadnMessage postfix: {ex.Message}");
            }
        }
    }
}
