using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using BattlePauseController = Il2CppLast.UI.KeyInput.BattlePauseController;
using BattleUIManager = Il2CppLast.UI.BattleUIManager;
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks battle pause menu state by reading game memory directly.
    /// When active, bypasses BattleCommandState suppression so MenuTextDiscovery can read.
    /// </summary>
    internal static class BattlePauseState
    {

        /// <summary>
        /// Checks if battle pause menu is active by reading game memory directly.
        /// This avoids needing to hook methods that don't fire at runtime.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                try
                {
                    // Get BattleUIManager singleton
                    var uiManager = BattleUIManager.Instance;
                    if (uiManager == null) return false;

                    // Must be initialized (actually in battle) before reading pause state
                    // Without this check, garbage memory values outside battle can cause false positives
                    if (!uiManager.Initialized) return false;

                    // Read pauseController pointer at offset 0x90
                    IntPtr uiManagerPtr = uiManager.Pointer;
                    IntPtr pauseControllerPtr = Marshal.ReadIntPtr(uiManagerPtr + IL2CppOffsets.BattlePause.OFFSET_PAUSE_CONTROLLER);
                    if (pauseControllerPtr == IntPtr.Zero) return false;

                    // Read isActivePauseMenu bool at offset 0x71
                    byte isActive = Marshal.ReadByte(pauseControllerPtr + IL2CppOffsets.BattlePause.OFFSET_IS_ACTIVE_PAUSE_MENU);
                    return isActive != 0;
                }
                catch
                {
                    // If anything fails, assume not active
                    return false;
                }
            }
        }

        public static void Reset()
        {
            // No-op - state is read directly from game memory
        }
    }

    /// <summary>
    /// CommonPopup (KeyInput) button reading, used in and out of battle. State of the battle pause menu
    /// itself is read from memory (BattlePauseState).
    /// Event-driven: the popup's buttons are read from the game's own cursor moves (Cursor.NextIndex /
    /// PrevIndex, whose postfix calls TryReadCommonPopupCursor). CommonPopup.UpdateFocus (0x2FCC20) is
    /// not hooked: UpdateSelect calls it unconditionally every frame while the popup is open, and the
    /// popup moves its cursor only through Cursor.NextIndex / PrevIndex (&lt;UpdateSelect&gt;b__1).
    /// </summary>
    internal static class BattlePausePatches
    {

        // Track last announced button to avoid duplicates (reset per popup)
        private static int lastAnnouncedButtonIndex = -1;

        // True from a CommonPopup's open until its message + focused button have been read together,
        // so a cursor move doesn't speak the button before the message.
        private static bool commonPopupReadPending = false;

        // The open KeyInput CommonPopup (set by PopupPatches on Popup.Open, cleared on Popup.Close)
        private static IntPtr activeCommonPopupPtr = IntPtr.Zero;

        /// <summary>A KeyInput CommonPopup opened: its cursor moves are read by TryReadCommonPopupCursor.</summary>
        public static void OnCommonPopupOpened(IntPtr popupPtr)
        {
            activeCommonPopupPtr = popupPtr;
            lastAnnouncedButtonIndex = -1;
        }

        /// <summary>
        /// Cursor.NextIndex / PrevIndex postfix hook: if the moved cursor is the open CommonPopup's
        /// selectCursor, reads the focused button and returns true (the generic cursor reader must not
        /// run). Returns false for any other cursor.
        /// </summary>
        public static bool TryReadCommonPopupCursor(GameCursor cursor)
        {
            try
            {
                IntPtr popupPtr = activeCommonPopupPtr;
                if (popupPtr == IntPtr.Zero || cursor == null) return false;

                // Read selectCursor at offset 0x68
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.BattlePause.OFFSET_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero || cursorPtr != cursor.Pointer) return false;

                // The open-read speaks the message and the focused button together
                if (commonPopupReadPending) return true;

                int cursorIndex = cursor.Index;

                // Skip if same button as last announced
                if (cursorIndex == lastAnnouncedButtonIndex)
                    return true;

                lastAnnouncedButtonIndex = cursorIndex;
                SpeakButton(popupPtr, cursorIndex);
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error reading CommonPopup cursor: {ex.Message}");
                return false;
            }
        }

        private static void SpeakButton(IntPtr popupPtr, int cursorIndex)
        {
            try
            {
                // Read commandList at offset 0x70
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.BattlePause.OFFSET_COMMAND_LIST);
                if (listPtr == IntPtr.Zero) return;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (cursorIndex < 0 || cursorIndex >= size) return;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (cursorIndex * 8));
                if (commandPtr == IntPtr.Zero) return;

                // Read text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + IL2CppOffsets.BattlePause.OFFSET_COMMAND_TEXT);
                if (textPtr == IntPtr.Zero) return;

                var textComponent = new UnityEngine.UI.Text(textPtr);
                string buttonText = textComponent.text;

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText.Trim());
                    FFIII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error reading CommonPopup button: {ex.Message}");
            }
        }

        /// <summary>
        /// A CommonPopup opened: hold the focus reader until PopupPatches has read the message and the
        /// focused button together.
        /// </summary>
        public static void BeginCommonPopupRead()
        {
            lastAnnouncedButtonIndex = -1;
            commonPopupReadPending = true;
        }

        /// <summary>
        /// The open-read spoke the button at focusedIndex (-1 if none): resume the focus reader without
        /// repeating that button.
        /// </summary>
        public static void EndCommonPopupRead(int focusedIndex)
        {
            lastAnnouncedButtonIndex = focusedIndex;
            commonPopupReadPending = false;
        }

        /// <summary>
        /// Reset state (called when battle ends or popup closes).
        /// </summary>
        public static void Reset()
        {
            BattlePauseState.Reset();
            lastAnnouncedButtonIndex = -1;
            commonPopupReadPending = false;
            activeCommonPopupPtr = IntPtr.Zero;
        }
    }
}
