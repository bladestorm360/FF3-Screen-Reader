using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Utils;

// FF3 types
using BattleController = Il2CppLast.Battle.BattleController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Battle-start lifecycle hook and the battle command-message window.
    /// The start condition itself ("Preemptive strike!", "Back attack!", ...) is the game's own localized
    /// message and is spoken where the game displays it (BattleSystemMessagePatches or the command-message
    /// window below). No synthetic announcement is made here — that would duplicate it, and in English only.
    /// </summary>
    internal static class BattleStartPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch StartPreeMptiveMes as the battle-start lifecycle hook
                PatchStartPreeMptiveMes(harmony);

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
        /// Patch StartPreeMptiveMes to mark the battle start.
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
        /// Postfix for StartPreeMptiveMes - marks the in-battle lifecycle.
        /// </summary>
        public static void StartPreeMptiveMes_Postfix()
        {
            try
            {
                BattleStateHelper.OnBattleStart();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in StartPreeMptiveMes_Postfix: {ex.Message}");
            }
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

        /// <summary>
        /// Postfix for BattleCommandMessageController.SetMessage/SetSystemMessage.
        /// Uses __0 instead of named string param to avoid IL2CPP crash.
        /// The same window also shows the name of the executing ability/item, which the CreateActFunction
        /// patch already speaks as "Actor: Action"; those are filtered by comparing against the recorded
        /// action name (language-independent), everything else is announced.
        /// </summary>
        public static void BattleCommandMessage_Postfix(object __0)
        {
            try
            {
                // __0 is the message string (using __0 to avoid IL2CPP string param crash)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message)) return;

                string cleanMessage = TextUtils.NormalizeWhitespace(TextUtils.StripIconMarkup(message));
                if (string.IsNullOrEmpty(cleanMessage)) return;

                // Decide one frame later so the action name is recorded whichever hook fires first.
                CoroutineManager.StartManaged(AnnounceUnlessActionName(cleanMessage));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Start] Error in BattleCommandMessage_Postfix: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceUnlessActionName(string message)
        {
            yield return null;

            if (message == ParameterActFunctionManagment_CreateActFunction_Patch.LastActionName)
                yield break;

            GlobalBattleMessageTracker.TryAnnounce(message, "BattleCommandMessage");
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
