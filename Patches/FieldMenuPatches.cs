using System;
using System.Collections;
using MelonLoader;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;

using GameCursor = Il2CppLast.UI.Cursor;
using MenuManager = Il2CppLast.UI.MenuManager;
using CommandMenuController = Il2CppLast.UI.CommandMenuController;
using MainMenuController = Il2CppLast.UI.KeyInput.MainMenuController;
using ItemWindowController = Il2CppLast.UI.KeyInput.ItemWindowController;
using EquipmentWindowController = Il2CppLast.UI.KeyInput.EquipmentWindowController;
using AbilityWindowController = Il2CppSerial.FF3.UI.KeyInput.AbilityWindowController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Reads the focused entry of the field (main) menu and of the Item / Equipment / Magic command bars
    /// when they open and when the player backs out to them. Navigation already flows through the generic
    /// cursor reader, but placing the cursor on entry never fires Cursor.NextIndex, so nothing was
    /// announced. Hooks the state-entry methods (MainMenuController.Show/InitNone, the windows'
    /// command-state Init) and reuses the reader navigation uses, one frame later.
    ///
    /// Gated on MenuManager.IsOpen so nothing is read during the scene-construction flurry of a map load,
    /// when these methods can also fire.
    /// </summary>
    internal static class FieldMenuPatches
    {
        private const string LOG = "[FieldMenu]";

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(MainMenuController), "Show", typeof(FieldMenuPatches),
                nameof(MainMenu_Postfix), LOG, new Type[] { typeof(bool) });
            // Every sub-menu (and the quicksave popup) returns to the command-select state on cancel.
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(MainMenuController), "InitNone", typeof(FieldMenuPatches),
                nameof(MainMenu_Postfix), LOG);

            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ItemWindowController), "CommandSelectInit", typeof(FieldMenuPatches),
                nameof(ItemCommand_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(EquipmentWindowController), "CommandInit", typeof(FieldMenuPatches),
                nameof(EquipCommand_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(AbilityWindowController), "CommandInit", typeof(FieldMenuPatches),
                nameof(MagicCommand_Postfix), LOG);
        }

        // Bumped on every field-menu read request; on open both Show and InitNone fire within a frame
        // or two, and the latch collapses them into one read (the later one wins).
        private static int fieldMenuGen;

        // The field menu's command controller, kept from its open hook so the per-move command count
        // (TryGetFieldCommandCount) never searches the scene
        private static CommandMenuController fieldCommandController;

        public static void MainMenu_Postfix(MainMenuController __instance)
        {
            if (__instance == null) return;
            try { fieldCommandController = __instance.commandMenuController; } catch { }
            int gen = ++fieldMenuGen;
            CoroutineManager.StartManaged(ReadFocusWhenOpen(__instance, () =>
                gen == fieldMenuGen ? __instance.commandMenuController?.selectCursor : null));
        }

        public static void ItemCommand_Postfix(ItemWindowController __instance)
        {
            if (__instance == null) return;
            CoroutineManager.StartManaged(ReadFocusWhenOpen(__instance, () => __instance.commandController?.selectCursor));
        }

        public static void EquipCommand_Postfix(EquipmentWindowController __instance)
        {
            if (__instance == null) return;
            CoroutineManager.StartManaged(ReadFocusWhenOpen(__instance, () => __instance.commandController?.selectCursor));
        }

        public static void MagicCommand_Postfix(AbilityWindowController __instance)
        {
            if (__instance == null) return;
            CoroutineManager.StartManaged(ReadFocusWhenOpen(__instance, () => __instance.commandController?.selectCursor));
        }

        /// <summary>
        /// Waits a frame (cursor settles, MenuManager.IsOpen flips true), then hands the focused cursor to
        /// the navigation reader if the owning window is still shown inside an open menu.
        /// </summary>
        private static IEnumerator ReadFocusWhenOpen(UnityEngine.Component owner, Func<GameCursor> getCursor)
        {
            yield return null;

            GameCursor cursor = null;
            try
            {
                if (owner != null && owner.gameObject != null && owner.gameObject.activeInHierarchy && IsMenuOpen())
                    cursor = getCursor();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{LOG} Error reading menu focus: {ex.Message}");
            }

            if (cursor != null)
                CoroutineManager.StartManaged(MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
        }

        /// <summary>
        /// True while an in-game menu is open. Reliably false during a map/asset load.
        /// </summary>
        internal static bool IsMenuOpen()
        {
            try
            {
                var mm = MenuManager.Instance;
                return mm != null && mm.IsOpen;
            }
            catch { return false; } // MenuManager not constructed yet during early load
        }

        /// <summary>
        /// Field-menu command count for the "(X of Y)" suffix, or -1 when <paramref name="cursor"/> is not
        /// the field menu's cursor (so it's inert on every other menu). The field menu keeps its commands
        /// in a C# list (CommandMenuController.contents), not a Content transform, so the generic reader
        /// can't count them.
        /// </summary>
        internal static int TryGetFieldCommandCount(GameCursor cursor)
        {
            try
            {
                if (cursor == null) return -1;
                // Cached at menu open (MainMenu_Postfix): runs on every generic cursor move, so no scan
                var cmd = fieldCommandController;
                if (cmd == null || !cmd.gameObject.activeInHierarchy) return -1;
                var fieldCursor = cmd.selectCursor;
                if (fieldCursor == null || fieldCursor.Pointer != cursor.Pointer) return -1;
                var contents = cmd.contents;
                return contents != null ? contents.Count : -1;
            }
            catch { return -1; }
        }
    }
}
