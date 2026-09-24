using System;
using System.Collections;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;

using GameCursor = Il2CppLast.UI.Cursor;
using SaveListController = Il2CppLast.UI.KeyInput.SaveListController;
using LoadGameWindowController = Il2CppLast.UI.KeyInput.LoadGameWindowController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Reads the initially highlighted save slot when the save/load list opens. SaveListController is
    /// shared by every entry point (title Load, field Save and Load), so this one hook covers them all.
    /// Navigation is already read by the generic cursor reader; this is the open-read the game doesn't
    /// otherwise trigger.
    /// </summary>
    internal static class SaveListPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(SaveListController), "SetActive",
                typeof(SaveListPatches), nameof(SetActive_Postfix), "[SaveList]",
                new Type[] { typeof(bool), typeof(bool), typeof(bool) });
        }

        public static void SetActive_Postfix(SaveListController __instance, bool isActive)
        {
            if (!isActive || __instance == null) return;
            CoroutineManager.StartManaged(DelayedReadSlot(__instance));
        }

        private static IEnumerator DelayedReadSlot(SaveListController controller)
        {
            yield return null; // let the list populate + cursor settle

            string announcement = null;
            try
            {
                // The background autosave during a map load also activates a SaveListController, but no
                // save/load screen is shown then, so it's excluded by ShouldReadSaveSlot.
                if (controller != null && controller.gameObject != null && controller.gameObject.activeInHierarchy
                    && ShouldReadSaveSlot())
                {
                    GameCursor cursor = controller.selectCursor;
                    if (cursor?.transform != null)
                        announcement = SaveSlotReader.TryReadSaveSlot(cursor.transform, cursor.Index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[SaveList] Error reading slot: {ex.Message}");
            }

            if (!string.IsNullOrWhiteSpace(announcement))
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
        }

        /// <summary>
        /// True when a save/load screen is really shown: an in-game menu is open (MenuManager.IsOpen,
        /// reliably false during a map load), or the title Load screen (not a MenuManager menu) is active.
        /// </summary>
        private static bool ShouldReadSaveSlot()
        {
            if (FieldMenuPatches.IsMenuOpen()) return true;
            var load = GameObjectCache.GetOrFind<LoadGameWindowController>();
            return load != null && load.gameObject.activeInHierarchy;
        }
    }
}
