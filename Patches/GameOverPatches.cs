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
    /// </summary>
    internal static class GameOverPatches
    {
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
                // Patch GameOverLoadPopup.UpdateFocus for button navigation
                Type loadPopupType = typeof(KeyInputGameOverLoadPopup);
                var updateFocusMethod = AccessTools.Method(loadPopupType, "UpdateFocus");

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(GameOverPatches).GetMethod(nameof(GameOverLoadPopup_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[GameOver] GameOverLoadPopup.UpdateFocus method not found");
                }

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
        /// Postfix for GameOverLoadPopup.UpdateFocus - reads and announces current button.
        /// </summary>
        public static void GameOverLoadPopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputGameOverLoadPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + GAMEOVERLOAD_SELECT_CURSOR_OFFSET);
                if (cursorPtr == IntPtr.Zero) return;

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON, cursorIndex))
                    return;

                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + GAMEOVERLOAD_CMDLIST_OFFSET);
                if (listPtr == IntPtr.Zero) return;

                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size) return;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFIII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error in UpdateFocus postfix: {ex.Message}");
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

                CoroutineManager.StartManaged(DelayedGameOverLoadPopupRead(controllerPtr));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error in InitSaveLoadPopup postfix: {ex.Message}");
            }
        }

        private static IEnumerator DelayedGameOverLoadPopupRead(IntPtr controllerPtr)
        {
            yield return null;

            try
            {
                if (controllerPtr == IntPtr.Zero) yield break;

                IntPtr viewPtr = Marshal.ReadIntPtr(controllerPtr + GAMEOVERPOPUPCTRL_VIEW_OFFSET);
                if (viewPtr == IntPtr.Zero) yield break;

                IntPtr loadPopupPtr = Marshal.ReadIntPtr(viewPtr + GAMEOVERPOPUPVIEW_LOADPOPUP_OFFSET);
                if (loadPopupPtr == IntPtr.Zero) yield break;

                IntPtr messagePtr = Marshal.ReadIntPtr(loadPopupPtr + GAMEOVERLOAD_MESSAGE_OFFSET);
                string message = ReadTextFromPointer(messagePtr);

                if (!string.IsNullOrWhiteSpace(message))
                {
                    message = TextUtils.StripIconMarkup(message.Trim());
                    FFIII_ScreenReaderMod.SpeakText(message, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameOver] Error in delayed read: {ex.Message}");
            }
        }
    }
}
