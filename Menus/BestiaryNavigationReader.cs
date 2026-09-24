using System;
using System.Collections.Generic;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Virtual buffer navigation for bestiary detail stats. Builds a shared <see cref="NavigationBuffer"/>
    /// of the visible stats (with group boundaries + names) and delegates all navigation to it.
    /// Driven by KeyContext.BestiaryDetail (arrows / WASD / controller D-pad).
    /// </summary>
    public static class BestiaryNavigationReader
    {
        private static NavigationBuffer buffer = null;

        /// <summary>
        /// Initialize the stat buffer from the current detail view's UI elements.
        /// Called when entering the bestiary detail view.
        /// </summary>
        public static void Initialize(List<BestiaryStatEntry> entries)
        {
            var funcs = new List<Func<string>>();
            var groupStarts = new List<int>();
            var groupNames = new List<string>();

            if (entries != null && entries.Count > 0)
            {
                BestiaryStatGroup lastGroup = entries[0].Group;
                groupStarts.Add(0);
                groupNames.Add(GetGroupDisplayName(entries[0].Group));

                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i]; // capture per-iteration
                    funcs.Add(() => entry.ToString());

                    if (i > 0 && entry.Group != lastGroup)
                    {
                        groupStarts.Add(i);
                        groupNames.Add(GetGroupDisplayName(entry.Group));
                        lastGroup = entry.Group;
                    }
                }
            }

            buffer = new NavigationBuffer(funcs, groupStarts, groupNames);
        }

        /// <summary>
        /// Clear navigation state.
        /// </summary>
        public static void Reset()
        {
            buffer = null;
        }

        /// <summary>
        /// Whether navigation is currently active (populated buffer + the tracker is active).
        /// </summary>
        public static bool IsActive => buffer != null && !buffer.IsEmpty &&
                                        BestiaryNavigationTracker.Instance.IsNavigationActive;

        public static void NavigateNext() => Move(b => b.Next());
        public static void NavigatePrevious() => Move(b => b.Previous());
        public static void JumpToNextGroup() => Move(b => b.NextGroup());
        public static void JumpToPreviousGroup() => Move(b => b.PreviousGroup());
        public static void JumpToTop() => Move(b => b.JumpTop());
        public static void JumpToBottom() => Move(b => b.JumpBottom());
        public static void ReadCurrentStat() => Move(b => b.Current());

        // All wrap/group logic lives in NavigationBuffer; this just gates + speaks, with the
        // position given within the current group.
        private static void Move(Func<NavigationBuffer, string> op)
        {
            if (!IsActive) return;
            string value = op(buffer);
            if (string.IsNullOrEmpty(value)) return;

            var (localIndex, groupCount) = buffer.CurrentGroupPosition();
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(value, localIndex, groupCount), true);
        }

        /// <summary>
        /// Get display-friendly name for a stat group.
        /// </summary>
        private static string GetGroupDisplayName(BestiaryStatGroup group)
        {
            switch (group)
            {
                case BestiaryStatGroup.MonsterData: return T("Monster Data");
                case BestiaryStatGroup.Status: return T("Status");
                case BestiaryStatGroup.Options: return T("Rewards");
                case BestiaryStatGroup.Items: return T("Items");
                case BestiaryStatGroup.Properties: return T("Properties");
                default: return T("Other");
            }
        }
    }
}
