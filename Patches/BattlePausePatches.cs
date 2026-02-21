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
    /// Patches for battle pause menu (spacebar during battle).
    /// State is tracked via direct memory read, not patches.
    /// Also handles popup button reading during battle.
    /// </summary>
    internal static class BattlePausePatches
    {

        // Track last announced button to avoid duplicates
        private static int lastAnnouncedButtonIndex = -1;

        /// <summary>
        /// Apply battle pause menu patches.
        /// Note: State clearing for Return to Title is handled by TitleMenuCommandController.SetEnableMainMenu
        /// in PopupPatches.cs, which fires when title menu becomes active.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch CommonPopup.UpdateFocus for popup button reading during battle
                TryPatchCommonPopupUpdateFocus(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch CommonPopup.UpdateFocus to read popup buttons during battle.
        /// </summary>
        private static void TryPatchCommonPopupUpdateFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputCommonPopup);
                var updateFocusMethod = AccessTools.Method(popupType, "UpdateFocus");

                if (updateFocusMethod != null)
                {
                    var postfix = typeof(BattlePausePatches).GetMethod(nameof(CommonPopup_UpdateFocus_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateFocusMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Pause] Patches applied");
                }
                else
                {
                    MelonLogger.Warning("[Battle Pause] CommonPopup.UpdateFocus method not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error patching CommonPopup.UpdateFocus: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for CommonPopup.UpdateFocus - reads and announces current button.
        /// </summary>
        public static void CommonPopup_UpdateFocus_Postfix(object __instance)
        {
            try
            {
                if (__instance == null) return;

                var popup = __instance as KeyInputCommonPopup;
                if (popup == null) return;

                IntPtr popupPtr = popup.Pointer;
                if (popupPtr == IntPtr.Zero) return;

                // Read selectCursor at offset 0x68
                IntPtr cursorPtr = Marshal.ReadIntPtr(popupPtr + IL2CppOffsets.BattlePause.OFFSET_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) return;

                var cursor = new GameCursor(cursorPtr);
                int cursorIndex = cursor.Index;

                // Skip if same button as last announced
                if (cursorIndex == lastAnnouncedButtonIndex)
                    return;

                lastAnnouncedButtonIndex = cursorIndex;

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
                MelonLogger.Warning($"[Battle Pause] Error in UpdateFocus postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state (called when battle ends or popup closes).
        /// </summary>
        public static void Reset()
        {
            BattlePauseState.Reset();
            lastAnnouncedButtonIndex = -1;
        }
    }
}
