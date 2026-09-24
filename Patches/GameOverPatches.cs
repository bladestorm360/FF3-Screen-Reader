using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine.UI;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

using GameCursor = Il2CppLast.UI.Cursor;
using KeyInputGameOverLoadPopup = Il2CppLast.UI.KeyInput.GameOverLoadPopup;
using KeyInputGameOverPopupController = Il2CppLast.UI.KeyInput.GameOverPopupController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for game over load popup navigation.
    /// Handles the "Start from recent save data?" popup and button navigation.
    /// Event-driven: GameOverPopupController.InitSaveLoadPopup opens it (message + focused button read one
    /// frame later), and the buttons are read from the popup's own cursor moves (Cursor.NextIndex /
    /// PrevIndex postfix → TryReadLoadPopupCursor). GameOverLoadPopup.UpdateFocus (0x7C7100) is not
    /// hooked: UpdateSelect calls it unconditionally every frame; the popup moves its cursor only through
    /// Cursor.NextIndex / PrevIndex (&lt;UpdateSelect&gt;b__32_0).
    /// </summary>
    internal static class GameOverPatches
    {
        // The load popup opened by InitSaveLoadPopup (cleared on scene load)
        private static IntPtr activeLoadPopupPtr = IntPtr.Zero;

        /// <summary>Scene change (title, loaded save): the popup is gone.</summary>
        public static void ResetState()
        {
            activeLoadPopupPtr = IntPtr.Zero;
        }
        // GameOverLoadPopup offsets
        private const int GAMEOVERLOAD_MESSAGE_OFFSET = 0x40;
        private const int GAMEOVERLOAD_SELECT_CURSOR_OFFSET = 0x58;
        private const int GAMEOVERLOAD_CMDLIST_OFFSET = 0x60;

        // GameOverPopupController -> GameOverPopupView -> GameOverLoadPopup
        private const int GAMEOVERPOPUPCTRL_VIEW_OFFSET = 0x30;
        private const int GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET = 0x18;

        // CommonCommand.text offset
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;

        /// <summary>
        /// Apply game over popup patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch GameOverPopupController.InitSaveLoadPopup to announce the popup message
                Type controllerType = typeof(KeyInputGameOverPopupController);
                var initMethod = AccessTools.Method(controllerType, "InitSaveLoadPopup");

                if (initMethod != null)
                {
                    var postfix = typeof(GameOverPatches).GetMethod(nameof(GameOverPopupController_InitSaveLoadPopup_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[GameOver] GameOverPopupController.InitSaveLoadPopup method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error applying patches: {ex.Message}");
            }
        }

        private static string ReadTextFromPointer(IntPtr textPtr)
        {
            if (textPtr == IntPtr.Zero) return null;
            try
            {
                var text = new Text(textPtr);
                return text?.text;
            }
            catch { return null; }
        }

        /// <summary>
        /// Label of the load popup's button at the given index, or null.
        /// </summary>
        private static string ReadButton(IntPtr popupPtr, int cursorIndex)
        {
            IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + GAMEOVERLOAD_CMDLIST_OFFSET);
            if (listPtr == IntPtr.Zero) return null;

            int size = Marshal.ReadInt32(listPtr + 0x18);
            if (cursorIndex < 0 || cursorIndex >= size) return null;

            IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
            if (itemsPtr == IntPtr.Zero) return null;

            IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
            if (commandPtr == IntPtr.Zero) return null;

            IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
            string buttonText = ReadTextFromPointer(textPtr);
            return string.IsNullOrWhiteSpace(buttonText) ? null : TextUtils.StripIconMarkup(buttonText.Trim());
        }

        /// <summary>
        /// Cursor.NextIndex / PrevIndex postfix hook: if the moved cursor is the game-over load popup's
        /// selectCursor, reads the focused button and returns true (the generic cursor reader must not
        /// run). Returns false for any other cursor.
        /// </summary>
        public static bool TryReadLoadPopupCursor(GameCursor cursor)
        {
            try
            {
                IntPtr popupPtr = activeLoadPopupPtr;
                if (popupPtr == IntPtr.Zero || cursor == null) return false;

                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero || cursorPtr != cursor.Pointer) return false;

                int cursorIndex = cursor.Index;
                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON, cursorIndex))
                    return true;

                string buttonText = ReadButton(popupPtr, cursorIndex);
                if (buttonText != null)
                    FFIII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error reading load popup cursor: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Postfix for GameOverPopupController.InitSaveLoadPopup.
        /// </summary>
        public static void GameOverPopupController_InitSaveLoadPopup_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var controller = __instance as KeyInputGameOverPopupController;
                if (controller == null) return;

                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero) return;

                // A new popup: its first focused button is spoken with the message
                AnnouncementDeduplicator.Reset(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON);
                activeLoadPopupPtr = GetLoadPopupPtr(controllerPtr);

                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(controllerPtr));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error in InitSaveLoadPopup postfix: {ex.Message}");
            }
        }

        private static IntPtr GetLoadPopupPtr(IntPtr controllerPtr)
        {
            if (controllerPtr == IntPtr.Zero) return IntPtr.Zero;
            IntPtr viewPtr = Marshal.ReadIntPtr(controllerPtr + GAMEOVERPOPUPCTRL_VIEW_OFFSET);
            if (viewPtr == IntPtr.Zero) return IntPtr.Zero;
            return Marshal.ReadIntPtr(viewPtr + GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET);
        }

        /// <summary>
        /// Open-read one frame after InitSaveLoadPopup: the message first, then the focused button
        /// ("Start from recent save data? Yes"), priming the cursor reader so that button isn't repeated.
        /// </summary>
        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr controllerPtr)
        {
            yield return null;

            try
            {
                IntPtr loadPopupPtr = GetLoadPopupPtr(controllerPtr);
                if (loadPopupPtr == IntPtr.Zero) yield break;
                activeLoadPopupPtr = loadPopupPtr;

                IntPtr messagePtr = Marshal.ReadIntPtr(loadPopupPtr + GAMEOVERLOAD_MESSAGE_OFFSET);
                string message = ReadTextFromPointer(messagePtr);
                message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

                string button = null;
                IntPtr cursorPtr = Marshal.ReadIntPtr(loadPopupPtr + GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr != IntPtr.Zero)
                {
                    int index = new GameCursor(cursorPtr).Index;
                    button = ReadButton(loadPopupPtr, index);
                    if (button != null)
                        AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON, index);
                }

                string announcement = message != null && button != null ? $"{message} {button}" : message ?? button;
                if (!string.IsNullOrWhiteSpace(announcement))
                    FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error in delayed read: {ex.Message}");
            }
        }
    }
}
