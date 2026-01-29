using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using FadeMessageManager = Il2CppLast.Message.FadeMessageManager;
using LineFadeMessageManager = Il2CppLast.Message.LineFadeMessageManager;
using ScrollMessageManager = Il2CppLast.Message.ScrollMessageManager;
using ScrollMessageClient = Il2CppLast.Management.ScrollMessageClient;
using MessageManager = Il2CppLast.Management.MessageManager;
using LineFadeMessageWindowController = Il2CppLast.UI.Message.LineFadeMessageWindowController;

namespace FFIII_ScreenReader.Patches
{
    // ============================================================
    // LineFade Per-Line Announcement System
    // Announces each line of story text as it appears on screen,
    // using the game's internal timing via PlayInit hook.
    // ============================================================

    /// <summary>
    /// Tracks LineFade message state for per-line announcements.
    /// Used for auto-scrolling story text, credits, etc.
    /// </summary>
    public static class LineFadeMessageTracker
    {
        private static string[] storedMessages = null;
        private static int currentLineIndex = 0;

        /// <summary>
        /// Store messages when SetData is called.
        /// </summary>
        public static void SetMessages(object messagesObj)
        {
            if (messagesObj == null)
            {
                storedMessages = null;
                currentLineIndex = 0;
                return;
            }

            try
            {
                var countProp = messagesObj.GetType().GetProperty("Count");
                if (countProp == null) return;

                int count = (int)countProp.GetValue(messagesObj);
                if (count == 0)
                {
                    storedMessages = null;
                    currentLineIndex = 0;
                    return;
                }

                var indexer = messagesObj.GetType().GetProperty("Item");
                if (indexer == null) return;

                storedMessages = new string[count];
                for (int i = 0; i < count; i++)
                {
                    storedMessages[i] = indexer.GetValue(messagesObj, new object[] { i }) as string;
                }
                currentLineIndex = 0;

                MelonLogger.Msg($"[LineFadeTracker] Stored {count} lines");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error storing LineFade messages: {ex.Message}");
                storedMessages = null;
                currentLineIndex = 0;
            }
        }

        /// <summary>
        /// Get and announce the next line. Called when PlayInit fires.
        /// </summary>
        public static void AnnounceNextLine()
        {
            if (storedMessages == null || currentLineIndex >= storedMessages.Length)
            {
                return;
            }

            string line = storedMessages[currentLineIndex];
            if (!string.IsNullOrWhiteSpace(line))
            {
                string cleanLine = line.Trim();
                MelonLogger.Msg($"[LineFade Line {currentLineIndex + 1}/{storedMessages.Length}] {cleanLine}");
                FFIII_ScreenReaderMod.SpeakText(cleanLine, interrupt: false);
            }

            currentLineIndex++;
        }

        /// <summary>
        /// Reset the tracker.
        /// </summary>
        public static void Reset()
        {
            storedMessages = null;
            currentLineIndex = 0;
        }
    }

    /// <summary>
    /// Patches for scrolling intro/outro messages.
    /// The intro uses ScrollMessageWindowController which displays scrolling text.
    /// LineFadeMessageWindowController provides per-line announcements for story text.
    /// </summary>
    public static class ScrollMessagePatches
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
                MelonLogger.Msg("Applying scroll/fade message patches...");

                // Use typeof() directly - much faster than assembly scanning
                Type fadeManagerType = typeof(FadeMessageManager);
                MelonLogger.Msg($"Found FadeMessageManager: {fadeManagerType.FullName}");

                var playMethod = AccessTools.Method(fadeManagerType, "Play");
                if (playMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched FadeMessageManager.Play");
                }

                // Use typeof() directly - much faster than assembly scanning
                Type lineFadeManagerType = typeof(LineFadeMessageManager);
                MelonLogger.Msg($"Found LineFadeMessageManager: {lineFadeManagerType.FullName}");

                // Patch Play method
                var lineFadePlayMethod = AccessTools.Method(lineFadeManagerType, "Play");
                if (lineFadePlayMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(lineFadePlayMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched LineFadeMessageManager.Play");
                }

                // Patch AsyncPlay method
                var asyncPlayMethod = AccessTools.Method(lineFadeManagerType, "AsyncPlay");
                if (asyncPlayMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(asyncPlayMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched LineFadeMessageManager.AsyncPlay");
                }

                // Use typeof() directly - much faster than assembly scanning
                Type scrollManagerType = typeof(ScrollMessageManager);
                MelonLogger.Msg($"Found ScrollMessageManager: {scrollManagerType.FullName}");

                var scrollPlayMethod = AccessTools.Method(scrollManagerType, "Play");
                if (scrollPlayMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(scrollPlayMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched ScrollMessageManager.Play");
                }

                // Patch ScrollMessageClient.PlayMessageId - catches battle messages by ID
                // (Back Attack!, Preemptive!, The party escaped!, etc.)
                Type scrollClientType = typeof(ScrollMessageClient);
                MelonLogger.Msg($"Found ScrollMessageClient: {scrollClientType.FullName}");

                var playMessageIdMethod = AccessTools.Method(scrollClientType, "PlayMessageId");
                if (playMessageIdMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollClientPlayMessageId_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(playMessageIdMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched ScrollMessageClient.PlayMessageId");
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
                    MelonLogger.Msg("Patched ScrollMessageClient.PlayMessageValue");
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageClient.PlayMessageValue method not found");
                }

                // Patch LineFadeMessageWindowController for per-line announcements
                Type lineFadeControllerType = typeof(LineFadeMessageWindowController);
                MelonLogger.Msg($"Found LineFadeMessageWindowController: {lineFadeControllerType.FullName}");

                // Patch SetData to store messages
                var setDataMethod = AccessTools.Method(lineFadeControllerType, "SetData");
                if (setDataMethod != null)
                {
                    var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeController_SetData_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(setDataMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched LineFadeMessageWindowController.SetData");
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
                    MelonLogger.Msg("Patched LineFadeMessageWindowController.PlayInit");
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
                    MelonLogger.Msg($"[Fade Message] Skipped (duplicate of map transition): {cleanMessage}");
                    return;
                }

                MelonLogger.Msg($"[Fade Message] {cleanMessage}");
                FFIII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in FadeManagerPlay_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for LineFadeMessageManager.Play and AsyncPlay - captures the messages list parameter.
        /// LineFadeMessageManager.Play(List<string> messages, Color32 color, float fadeinTime, float fadeoutTime, float waitTime)
        /// </summary>
        public static void LineFadeManagerPlay_Postfix(object __0)
        {
            try
            {
                // __0 is the first parameter (List<string> messages)
                if (__0 == null)
                {
                    return;
                }

                string combinedMessage = "";

                // Try to iterate the list
                if (__0 is System.Collections.IEnumerable enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        if (item != null)
                        {
                            string line = item.ToString();
                            if (!string.IsNullOrEmpty(line))
                            {
                                if (combinedMessage.Length > 0)
                                    combinedMessage += " ";
                                combinedMessage += line;
                            }
                        }
                    }
                }

                if (string.IsNullOrEmpty(combinedMessage))
                {
                    return;
                }

                // Avoid duplicate announcements
                if (combinedMessage == lastScrollMessage)
                {
                    return;
                }

                lastScrollMessage = combinedMessage;

                // Clean up the message
                string cleanMessage = combinedMessage.Replace("\n", " ").Replace("\r", " ");
                while (cleanMessage.Contains("  "))
                {
                    cleanMessage = cleanMessage.Replace("  ", " ");
                }
                cleanMessage = cleanMessage.Trim();

                MelonLogger.Msg($"[Line Fade Message] {cleanMessage}");
                FFIII_ScreenReaderMod.SpeakText(cleanMessage);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in LineFadeManagerPlay_Postfix: {ex.Message}");
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

                MelonLogger.Msg($"[Scroll Message] {cleanMessage}");
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
                        MelonLogger.Msg($"[ScrollClient MessageId] {messageId} -> {cleanMessage}");

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
                MelonLogger.Msg($"[ScrollClient MessageValue] {cleanMessage}");
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

        /// <summary>
        /// Reads message from controller's executionData and announces it.
        /// </summary>
        private static void ReadAndAnnounceMessage(object controller)
        {
            // Get executionData field which contains the message
            var execDataField = AccessTools.Field(controller.GetType(), "executionData");
            if (execDataField == null)
                return;

            var execData = execDataField.GetValue(controller);
            if (execData == null)
                return;

            // Get Message property from ExecutionData
            var messageProp = AccessTools.Property(execData.GetType(), "Message");
            if (messageProp == null)
                return;

            string message = messageProp.GetValue(execData) as string;
            if (string.IsNullOrEmpty(message))
                return;

            // Avoid duplicate announcements
            if (message == lastScrollMessage)
            {
                return;
            }

            lastScrollMessage = message;

            // Clean up the message (remove line breaks, extra spaces)
            string cleanMessage = message.Replace("\n", " ").Replace("\r", " ");
            while (cleanMessage.Contains("  "))
            {
                cleanMessage = cleanMessage.Replace("  ", " ");
            }
            cleanMessage = cleanMessage.Trim();

            MelonLogger.Msg($"[Scroll Message] {cleanMessage}");
            FFIII_ScreenReaderMod.SpeakText(cleanMessage);
        }

        /// <summary>
        /// Reads messages from LineFadeMessageWindowController's executionData.Messages array.
        /// </summary>
        private static void ReadLineFadeMessages(object controller)
        {
            // Get executionData field which contains the messages
            var execDataField = AccessTools.Field(controller.GetType(), "executionData");
            if (execDataField == null)
            {
                // Try the single Message approach as fallback
                ReadAndAnnounceMessage(controller);
                return;
            }

            var execData = execDataField.GetValue(controller);
            if (execData == null)
            {
                return;
            }

            // Try to get Messages property (string array) first
            var messagesProp = AccessTools.Property(execData.GetType(), "Messages");
            if (messagesProp != null)
            {
                var messagesObj = messagesProp.GetValue(execData);
                if (messagesObj != null)
                {
                    // Handle Il2CppStringArray or regular string array
                    string combinedMessage = "";

                    // Try to get as IEnumerable and iterate
                    if (messagesObj is System.Collections.IEnumerable enumerable)
                    {
                        foreach (var item in enumerable)
                        {
                            if (item != null)
                            {
                                string line = item.ToString();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    if (combinedMessage.Length > 0)
                                        combinedMessage += " ";
                                    combinedMessage += line;
                                }
                            }
                        }
                    }

                    if (!string.IsNullOrEmpty(combinedMessage))
                    {
                        // Avoid duplicate announcements
                        if (combinedMessage == lastScrollMessage)
                        {
                            return;
                        }

                        lastScrollMessage = combinedMessage;

                        // Clean up the message
                        string cleanMessage = combinedMessage.Replace("\n", " ").Replace("\r", " ");
                        while (cleanMessage.Contains("  "))
                        {
                            cleanMessage = cleanMessage.Replace("  ", " ");
                        }
                        cleanMessage = cleanMessage.Trim();

                        MelonLogger.Msg($"[Line Fade Message] {cleanMessage}");
                        FFIII_ScreenReaderMod.SpeakText(cleanMessage);
                        return;
                    }
                }
            }

            // Fallback to single Message property
            ReadAndAnnounceMessage(controller);
        }

        /// <summary>
        /// Reads message from FadeMessageWindowController's view.messageText component.
        /// </summary>
        private static void ReadFadeMessageFromView(object controller)
        {
            // Get view field (FadeMessageWindowView)
            var viewField = AccessTools.Field(controller.GetType(), "view");
            if (viewField == null)
                return;

            var view = viewField.GetValue(controller);
            if (view == null)
                return;

            // Get messageText field from FadeMessageWindowView
            var messageTextField = AccessTools.Field(view.GetType(), "messageText");
            if (messageTextField == null)
                return;

            var messageText = messageTextField.GetValue(view);
            if (messageText == null)
                return;

            // Get text property from UnityEngine.UI.Text
            var textProp = AccessTools.Property(messageText.GetType(), "text");
            if (textProp == null)
                return;

            string message = textProp.GetValue(messageText) as string;
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

            MelonLogger.Msg($"[Fade Message] {cleanMessage}");
            FFIII_ScreenReaderMod.SpeakText(cleanMessage);
        }

        /// <summary>
        /// Reads messages from LineFadeMessageWindowController's view.lineTexts components.
        /// </summary>
        private static void ReadLineFadeMessagesFromView(object controller)
        {
            // Get view field (LineFadeMessageWindowView)
            var viewField = AccessTools.Field(controller.GetType(), "view");
            if (viewField == null)
                return;

            var view = viewField.GetValue(controller);
            if (view == null)
                return;

            // Get LineTexts property from LineFadeMessageWindowView (it's a public property)
            var lineTextsProp = AccessTools.Property(view.GetType(), "LineTexts");
            if (lineTextsProp == null)
            {
                // Try the field directly
                var lineTextsField = AccessTools.Field(view.GetType(), "lineTexts");
                if (lineTextsField == null)
                    return;

                var lineTextsFromField = lineTextsField.GetValue(view);
                if (lineTextsFromField != null)
                {
                    ReadTextListAndAnnounce(lineTextsFromField);
                    return;
                }
            }
            else
            {
                var lineTexts = lineTextsProp.GetValue(view);
                if (lineTexts != null)
                {
                    ReadTextListAndAnnounce(lineTexts);
                    return;
                }
            }
        }

        /// <summary>
        /// Reads text from a list of Text components and announces them.
        /// </summary>
        private static void ReadTextListAndAnnounce(object textList)
        {
            string combinedMessage = "";

            if (textList is System.Collections.IEnumerable enumerable)
            {
                foreach (var textComponent in enumerable)
                {
                    if (textComponent == null)
                        continue;

                    // Get text property from Text component
                    var textProp = AccessTools.Property(textComponent.GetType(), "text");
                    if (textProp != null)
                    {
                        string line = textProp.GetValue(textComponent) as string;
                        if (!string.IsNullOrEmpty(line))
                        {
                            if (combinedMessage.Length > 0)
                                combinedMessage += " ";
                            combinedMessage += line;
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(combinedMessage))
            {
                return;
            }

            // Avoid duplicate announcements
            if (combinedMessage == lastScrollMessage)
            {
                return;
            }

            lastScrollMessage = combinedMessage;

            // Clean up the message
            string cleanMessage = combinedMessage.Replace("\n", " ").Replace("\r", " ");
            while (cleanMessage.Contains("  "))
            {
                cleanMessage = cleanMessage.Replace("  ", " ");
            }
            cleanMessage = cleanMessage.Trim();

            MelonLogger.Msg($"[Line Fade Message] {cleanMessage}");
            FFIII_ScreenReaderMod.SpeakText(cleanMessage);
        }
    }
}
