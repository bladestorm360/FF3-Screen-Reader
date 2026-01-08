using System.Collections;
using FFIII_ScreenReader.Core;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Shared speech helper utilities for all patches.
    /// </summary>
    internal static class SpeechHelper
    {
        /// <summary>
        /// Coroutine that speaks text after one frame delay.
        /// Use with CoroutineManager.StartManaged().
        /// This prevents race conditions when multiple patches fire in sequence.
        /// </summary>
        internal static IEnumerator DelayedSpeech(string text)
        {
            yield return null; // Wait one frame
            FFIII_ScreenReaderMod.SpeakText(text);
        }

        /// <summary>
        /// Coroutine that speaks text after one frame delay without interrupting.
        /// Use for queued announcements that shouldn't cut off previous speech.
        /// </summary>
        internal static IEnumerator DelayedSpeechNoInterrupt(string text)
        {
            yield return null; // Wait one frame
            FFIII_ScreenReaderMod.SpeakText(text, interrupt: false);
        }
    }
}
