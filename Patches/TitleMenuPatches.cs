using System;
using MelonLoader;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;

using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;
using TitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Reads the initially focused item of the title menus on entry: the main menu (InitSelect — first
    /// entry and back-out), the Options list (InitializeOption) and Extras (InitializeExtra). All are
    /// rendered by TitleWindowController.commandController; navigation already flows through the generic
    /// cursor reader, but the initial cursor placement doesn't fire Cursor.NextIndex.
    /// (The Options config screen itself is OptionController.ShowConfig, handled in ConfigMenuPatches.)
    /// </summary>
    internal static class TitleMenuPatches
    {
        private const string LOG = "[TitleMenu]";

        // TitleMenuCommandController.activeContents (List<TitleCommandContentView>) — the visible commands
        private const int OFFSET_ACTIVE_CONTENTS = 0x28;
        // List<T>._size
        private const int OFFSET_LIST_SIZE = 0x18;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            foreach (var method in new[] { "InitSelect", "InitializeOption", "InitializeExtra" })
                HarmonyPatchHelper.PatchPostfix(harmony, typeof(TitleWindowController), method,
                    typeof(TitleMenuPatches), nameof(AnnounceFocus_Postfix), LOG);
        }

        // The title menu's command controller, kept from the menu-entry hooks so the per-move command
        // count (TryGetActiveCommandCount) never searches the scene
        private static TitleMenuCommandController titleCommandController;

        public static void AnnounceFocus_Postfix(TitleWindowController __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null || !__instance.gameObject.activeInHierarchy)
                    return;
                titleCommandController = __instance.commandController;
                var cursor = titleCommandController?.selectCursor;
                if (cursor != null)
                    CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{LOG} Error reading title menu focus: {ex.Message}");
            }
        }

        /// <summary>
        /// Visible title-command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is
        /// not the title menu's cursor. Counts activeContents so disabled commands (e.g. Continue with no
        /// save) are excluded, matching what the cursor navigates.
        /// </summary>
        internal static int TryGetActiveCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                // Cached at menu entry (AnnounceFocus_Postfix): runs on every generic cursor move, so no scan
                var cmd = titleCommandController;
                if (cmd == null || !cmd.gameObject.activeInHierarchy) return -1;
                var titleCursor = cmd.selectCursor;
                if (titleCursor == null || titleCursor.Pointer != cursor.Pointer) return -1;
                IntPtr listPtr = StateReaderHelper.ReadPointerField(cmd.Pointer, OFFSET_ACTIVE_CONTENTS);
                if (listPtr == IntPtr.Zero) return -1;
                return System.Runtime.InteropServices.Marshal.ReadInt32(listPtr, OFFSET_LIST_SIZE);
            }
            catch { return -1; }
        }
    }
}
