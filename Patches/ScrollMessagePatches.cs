using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using FadeMessageManager = Il2CppLast.Message.FadeMessageManager;
using ScrollMessageManager = Il2CppLast.Message.ScrollMessageManager;
using ScrollMessageClient = Il2CppLast.Management.ScrollMessageClient;
using MessageManager = Il2CppLast.Management.MessageManager;
using LineFadeMessageWindowController = Il2CppLast.UI.Message.LineFadeMessageWindowController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for scrolling intro/outro messages.
    /// The intro uses ScrollMessageWindowController which displays scrolling text.
    /// LineFadeMessageWindowController provides per-line announcements for story text.
    /// </summary>
    internal static class ScrollMessagePatches
    {
        private static string lastScrollMessage = "";

        /// <summary>
        /// Applies scroll message patches using manual Harmony patching.
        /// Patches the Manager classes which receive the actual message text as parameters.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use typeof() directly - much faster than assembly scanning
                Type fadeManagerType = typeof(FadeMessageManager);

                var playMethod = AccessTools.Method(fadeManagerType, "Play");
                if (playMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                }

                // Use typeof() directly - much faster than assembly scanning
                Type scrollManagerType = typeof(ScrollMessageManager);

                var scrollPlayMethod = AccessTools.Method(scrollManagerType, "Play");
                if (scrollPlayMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(scrollPlayMethod, postfix: new HarmonyMethod(postfix));
                }

                // Patch ScrollMessageClient.PlayMessageId - catches battle messages by ID
                // (Back Attack!, Preemptive!, The party escaped!, etc.)
                Type scrollClientType = typeof(ScrollMessageClient);

                var playMessageIdMethod = AccessTools.Method(scrollClientType, "PlayMessageId");
                if (playMessageIdMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollClientPlayMessageId_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMessageIdMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageClient.PlayMessageId method not found");
                }

                var playMessageValueMethod = AccessTools.Method(scrollClientType, "PlayMessageValue");
                if (playMessageValueMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollClientPlayMessageValue_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMessageValueMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageClient.PlayMessageValue method not found");
                }

                // Patch LineFadeMessageWindowController for per-line announcements
                Type lineFadeControllerType = typeof(LineFadeMessageWindowController);

                // Patch SetData to store messages
                var setDataMethod = AccessTools.Method(lineFadeControllerType, "SetData");
                if (setDataMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeController_SetData_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setDataMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageWindowController.SetData not found");
                }

                // Patch PlayInit to announce each line
                var playInitMethod = AccessTools.Method(lineFadeControllerType, "PlayInit");
                if (playInitMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeController_PlayInit_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playInitMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageWindowController.PlayInit not found");
                }

                MelonLogger.Msg("Scroll/Fade message patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying scroll message patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        // FindType method removed - using typeof() directly is much faster

        /// <summary>
        /// Postfix for FadeMessageManager.Play - captures the message parameter directly.
        /// FadeMessageManager.Play(string message, int fontSize, Color32 color, float fadeinTime, float fadeoutTime, float waitTime, bool isCenterAnchor, float postionX, float postionY)
        /// </summary>
        public static void FadeManagerPlay_Postfix(object __0)
        {
            try
            {
                // __0 is the first parameter (message string)
                string message = __0?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Clean up the message
                string cleanMessage = message.Replace("\n", " ").Replace("\r", " ");
                while (cleanMessage.Contains("  "))
                {
                    cleanMessage = cleanMessage.Replace("  ", " ");
                }
                cleanMessage = cleanMessage.Trim();

                // Check for duplicate location announcement
                // E.g., skip "Altar Cave" if "Entering Altar Cave" was just announced
                if (!LocationMessageTracker.ShouldAnnounceFadeMessage(cleanMessage))
                {
                    return;
                }

                FFIII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageManager.Play - captures the message parameter.
        /// ScrollMessageManager.Play(ScrollMessageClient.ScrollType type, string message, float scrollTime, int fontSize, Color32 color, TextAnchor anchor, Rect margin)
        /// </summary>
        public static void ScrollManagerPlay_Postfix(object __1)
        {
            try
            {
                // __1 is the second parameter (message string, first is ScrollType)
                string message = __1?.ToString();
                if (string.IsNullOrEmpty(message))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (message == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = message;

                // Clean up the message
                string cleanMessage = message.Replace("\n", " ").Replace("\r", " ");
                while (cleanMessage.Contains("  "))
                {
                    cleanMessage = cleanMessage.Replace("  ", " ");
                }
                cleanMessage = cleanMessage.Trim();

                FFIII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageClient.PlayMessageId - catches battle messages by ID.
        /// This catches messages like "Back Attack!", "Preemptive Strike!", "The party escaped!" etc.
        /// ScrollMessageClient.PlayMessageId(ScrollType type, string messageId, ...)
        /// </summary>
        public static void ScrollClientPlayMessageId_Postfix(object __1)
        {
            try
            {
                // __1 is the second parameter (messageId string, first is ScrollType)
                string messageId = __1?.ToString();
                if (string.IsNullOrEmpty(messageId))
                {
                    return;
                }

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        // Avoid duplicate announcements
                        if (message == lastScrollMessage)
                        {
                            return;
                        }

                        lastScrollMessage = message;

                        string cleanMessage = message.Trim();

                        // Clear flee flag if this is an escape result message
                        if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            GlobalBattleMessageTracker.ClearFleeInProgress();
                        }

                        FFIII_ScreenReaderMod.SpeakText(cleanMessage);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollClientPlayMessageId_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageClient.PlayMessageValue - catches direct message display.
        /// ScrollMessageClient.PlayMessageValue(ScrollType type, string messageValue, ...)
        /// </summary>
        public static void ScrollClientPlayMessageValue_Postfix(object __1)
        {
            try
            {
                // __1 is the second parameter (messageValue string, first is ScrollType)
                string messageValue = __1?.ToString();
                if (string.IsNullOrEmpty(messageValue))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (messageValue == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = messageValue;

                string cleanMessage = messageValue.Trim();
                FFIII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollClientPlayMessageValue_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageWindowController.SetData - stores messages for per-line announcement.
        /// </summary>
        public static void LineFadeController_SetData_Postfix(object __0)
        {
            try
            {
                // __0 is the messages parameter (List<string>)
                LineFadeMessageTracker.SetMessages(__0);

                // Clear speaker context so next regular dialogue re-announces the speaker
                // This re-establishes context after auto-scrolling text events
                DialogueTracker.ClearLastAnnouncedSpeaker();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController_SetData_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageWindowController.PlayInit - announces each line as it appears.
        /// PlayInit is called once per line by the game's internal state machine.
        /// </summary>
        public static void LineFadeController_PlayInit_Postfix()
        {
            try
            {
                LineFadeMessageTracker.AnnounceNextLine();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeController_PlayInit_Postfix: {ex.Message}");
            }
        }

    }
}
