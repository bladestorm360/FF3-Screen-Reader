using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
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
    /// All of these are game events, so they never interrupt speech. Multi-line scroll messages are
    /// spoken line by line, paced by the game's own scrollTime so speech follows the visual scroll.
    /// </summary>
    internal static class ScrollMessagePatches
    {
        // The same text arriving again within this window is the same message (ScrollMessageClient
        // calls into ScrollMessageManager.Play, so both postfixes see it). Outside the window a repeat
        // is a new event (e.g. "Back Attack!" in a later battle) and is spoken again.
        private const float DUPLICATE_WINDOW_SECONDS = 2f;

        private static string lastScrollMessage = "";
        private static float lastScrollMessageTime = -100f;
        private static IEnumerator activeScrollCoroutine = null;

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

                if (IsDuplicate(message))
                {
                    return;
                }

                string cleanMessage = CollapseWhitespace(message);

                // Check for duplicate location announcement
                // E.g., skip "Altar Cave" if "Entering Altar Cave" was just announced
                if (!LocationMessageTracker.ShouldAnnounceFadeMessage(cleanMessage))
                {
                    return;
                }

                FFIII_ScreenReaderMod.SpeakText(cleanMessage, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ScrollMessageManager.Play - captures the message parameter.
        /// ScrollMessageManager.Play(ScrollMessageClient.ScrollType type, string message, float scrollTime, int fontSize, Color32 color, TextAnchor anchor, Rect margin)
        /// __1 = message, __2 = scrollTime.
        /// </summary>
        public static void ScrollManagerPlay_Postfix(object __1, float __2)
        {
            try
            {
                // __1 is the second parameter (message string, first is ScrollType)
                string message = __1?.ToString();
                if (string.IsNullOrEmpty(message) || IsDuplicate(message))
                {
                    return;
                }

                SpeakScrollMessage(message, __2);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ScrollManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// True if this text was just announced (see DUPLICATE_WINDOW_SECONDS). Records it either way,
        /// so a message re-sent continuously stays suppressed.
        /// </summary>
        private static bool IsDuplicate(string message)
        {
            float now = Time.realtimeSinceStartup;
            bool duplicate = message == lastScrollMessage && now - lastScrollMessageTime < DUPLICATE_WINDOW_SECONDS;
            lastScrollMessage = message;
            lastScrollMessageTime = now;
            return duplicate;
        }

        private static string CollapseWhitespace(string message)
        {
            string clean = message.Replace("\n", " ").Replace("\r", " ");
            while (clean.Contains("  "))
            {
                clean = clean.Replace("  ", " ");
            }
            return clean.Trim();
        }

        /// <summary>
        /// Speaks a scroll message without interrupting. A single line is spoken at once; several lines
        /// are spoken one at a time, spread across the game's scrollTime (the visual scroll is linear).
        /// A new scroll message replaces one still being read.
        /// </summary>
        private static void SpeakScrollMessage(string message, float scrollTime)
        {
            if (activeScrollCoroutine != null)
            {
                CoroutineManager.StopManaged(activeScrollCoroutine);
                activeScrollCoroutine = null;
            }

            string[] lines = message.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
            {
                string single = CollapseWhitespace(message);
                if (single.Length > 0)
                    FFIII_ScreenReaderMod.SpeakText(single, interrupt: false);
                return;
            }

            activeScrollCoroutine = SpeakScrollLinesWithTiming(lines, scrollTime);
            CoroutineManager.StartManaged(activeScrollCoroutine);
        }

        private static IEnumerator SpeakScrollLinesWithTiming(string[] lines, float totalScrollTime)
        {
            float delayPerLine = totalScrollTime > 0f ? totalScrollTime / (lines.Length + 1) : 0f;
            bool first = true;

            foreach (string line in lines)
            {
                string cleanLine = line.Trim();
                if (cleanLine.Length == 0) continue;

                if (!first && delayPerLine > 0f)
                    yield return new WaitForSeconds(delayPerLine);
                first = false;

                FFIII_ScreenReaderMod.SpeakText(cleanLine, interrupt: false);
            }

            activeScrollCoroutine = null;
        }

        /// <summary>
        /// Postfix for ScrollMessageClient.PlayMessageId - catches battle messages by ID.
        /// This catches messages like "Back Attack!", "Preemptive Strike!", "The party escaped!" etc.
        /// ScrollMessageClient.PlayMessageId(ScrollType type, string messageId, ...)
        /// </summary>
        public static void ScrollClientPlayMessageId_Postfix(object __1, float __2)
        {
            try
            {
                // __1 is the second parameter (messageId string, first is ScrollType)
                string messageId = __1?.ToString();
                if (string.IsNullOrEmpty(messageId))
                {
                    return;
                }

                // Clear flee flag if this is an escape result message. Done before the duplicate check:
                // the nested ScrollMessageManager.Play postfix has usually spoken the text already.
                if (messageId.IndexOf("ESCAPE", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    GlobalBattleMessageTracker.ClearFleeInProgress();
                }

                // Look up the localized message
                var messageManager = MessageManager.Instance;
                if (messageManager != null)
                {
                    string message = messageManager.GetMessage(messageId);
                    if (!string.IsNullOrWhiteSpace(message) && !IsDuplicate(message))
                    {
                        SpeakScrollMessage(message, __2);
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
        public static void ScrollClientPlayMessageValue_Postfix(object __1, float __2)
        {
            try
            {
                // __1 is the second parameter (messageValue string, first is ScrollType)
                string messageValue = __1?.ToString();
                if (string.IsNullOrEmpty(messageValue) || IsDuplicate(messageValue))
                {
                    return;
                }

                SpeakScrollMessage(messageValue, __2);
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
