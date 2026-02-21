using System;
using MelonLoader;
using FFIII_ScreenReader.Core;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks LineFade message state for per-line announcements.
    /// Used for auto-scrolling story text, credits, etc.
    /// </summary>
    internal static class LineFadeMessageTracker
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
}
