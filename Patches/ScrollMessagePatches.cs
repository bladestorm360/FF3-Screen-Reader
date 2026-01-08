using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for scrolling intro/outro messages.
    /// The intro uses ScrollMessageWindowController which displays scrolling text.
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

                // Patch FadeMessageManager.Play - receives single message string
                Type fadeManagerType = FindType("Il2CppLast.Message.FadeMessageManager");
                if (fadeManagerType != null)
                {
                    MelonLogger.Msg($"Found FadeMessageManager: {fadeManagerType.FullName}");

                    var playMethod = AccessTools.Method(fadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("FadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched FadeMessageManager.Play");
                    }
                }
                else
                {
                    MelonLogger.Warning("FadeMessageManager type not found");
                }

                // Patch LineFadeMessageManager.Play and AsyncPlay - receives List<string> messages
                Type lineFadeManagerType = FindType("Il2CppLast.Message.LineFadeMessageManager");
                if (lineFadeManagerType != null)
                {
                    MelonLogger.Msg($"Found LineFadeMessageManager: {lineFadeManagerType.FullName}");

                    // Patch Play method
                    var playMethod = AccessTools.Method(lineFadeManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("LineFadeManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
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
                }
                else
                {
                    MelonLogger.Warning("LineFadeMessageManager type not found");
                }

                // Patch ScrollMessageManager.Play - receives scroll message string
                Type scrollManagerType = FindType("Il2CppLast.Message.ScrollMessageManager");
                if (scrollManagerType != null)
                {
                    MelonLogger.Msg($"Found ScrollMessageManager: {scrollManagerType.FullName}");

                    var playMethod = AccessTools.Method(scrollManagerType, "Play");
                    if (playMethod != null)
                    {
                        var postfix = typeof(ScrollMessagePatches).GetMethod("ScrollManagerPlay_Postfix",
                            BindingFlags.Public | BindingFlags.Static);
                        harmony.Patch(playMethod, postfix: new HarmonyMethod(postfix));
                        MelonLogger.Msg("Patched ScrollMessageManager.Play");
                    }
                }
                else
                {
                    MelonLogger.Warning("ScrollMessageManager type not found");
                }

                MelonLogger.Msg("Scroll/Fade message patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying scroll message patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
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
        /// Reads message from controller's executionData and announces it.
        /// </summary>
        private static void ReadAndAnnounceMessage(object controller)
        {
            // Get executionData field which contains the message
            var execDataField = AccessTools.Field(controller.GetType(), "executionData");
            if (execDataField == null)
            {
                MelonLogger.Msg("[DEBUG] executionData field not found");
                return;
            }

            var execData = execDataField.GetValue(controller);
            if (execData == null)
            {
                MelonLogger.Msg("[DEBUG] executionData is null");
                return;
            }

            // Get Message property from ExecutionData
            var messageProp = AccessTools.Property(execData.GetType(), "Message");
            if (messageProp == null)
            {
                MelonLogger.Msg("[DEBUG] Message property not found on ExecutionData");
                return;
            }

            string message = messageProp.GetValue(execData) as string;
            if (string.IsNullOrEmpty(message))
            {
                MelonLogger.Msg("[DEBUG] Message is null or empty");
                return;
            }

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
            {
                MelonLogger.Msg("[DEBUG] FadeMessageWindowController.view field not found");
                return;
            }

            var view = viewField.GetValue(controller);
            if (view == null)
            {
                MelonLogger.Msg("[DEBUG] FadeMessageWindowController.view is null");
                return;
            }

            // Get messageText field from FadeMessageWindowView
            var messageTextField = AccessTools.Field(view.GetType(), "messageText");
            if (messageTextField == null)
            {
                MelonLogger.Msg("[DEBUG] FadeMessageWindowView.messageText field not found");
                return;
            }

            var messageText = messageTextField.GetValue(view);
            if (messageText == null)
            {
                MelonLogger.Msg("[DEBUG] FadeMessageWindowView.messageText is null");
                return;
            }

            // Get text property from UnityEngine.UI.Text
            var textProp = AccessTools.Property(messageText.GetType(), "text");
            if (textProp == null)
            {
                MelonLogger.Msg("[DEBUG] Text.text property not found");
                return;
            }

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
            {
                MelonLogger.Msg("[DEBUG] LineFadeMessageWindowController.view field not found");
                return;
            }

            var view = viewField.GetValue(controller);
            if (view == null)
            {
                MelonLogger.Msg("[DEBUG] LineFadeMessageWindowController.view is null");
                return;
            }

            // Get LineTexts property from LineFadeMessageWindowView (it's a public property)
            var lineTextsProp = AccessTools.Property(view.GetType(), "LineTexts");
            if (lineTextsProp == null)
            {
                // Try the field directly
                var lineTextsField = AccessTools.Field(view.GetType(), "lineTexts");
                if (lineTextsField == null)
                {
                    MelonLogger.Msg("[DEBUG] LineFadeMessageWindowView.lineTexts not found");
                    return;
                }

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

            MelonLogger.Msg("[DEBUG] Could not read line texts from LineFadeMessageWindowView");
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
