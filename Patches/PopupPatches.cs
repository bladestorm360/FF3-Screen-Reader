using System;
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using UnityEngine.UI;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types - Base
using BasePopup = Il2CppLast.UI.Popup;
using GameCursor = Il2CppLast.UI.Cursor;

// Type aliases for IL2CPP types - KeyInput Popups
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using KeyInputJobChangePopup = Il2CppLast.UI.KeyInput.JobChangePopup;
using KeyInputChangeMagicStonePopup = Il2CppLast.UI.KeyInput.ChangeMagicStonePopup;
using KeyInputGameOverSelectPopup = Il2CppLast.UI.KeyInput.GameOverSelectPopup;
using KeyInputInfomationPopup = Il2CppLast.UI.KeyInput.InfomationPopup;
using KeyInputInputPopup = Il2CppLast.UI.KeyInput.InputPopup;
using KeyInputChangeNamePopup = Il2CppLast.UI.KeyInput.ChangeNamePopup;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;

// Type aliases for IL2CPP types - Touch Popups
using TouchCommonPopup = Il2CppLast.UI.Touch.CommonPopup;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks popup state for handling in CursorNavigation.
    /// </summary>
    internal static class PopupState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.POPUP);

        static PopupState()
        {
            _helper.RegisterResetHandler(() =>
            {
                CurrentPopupType = null;
                ActivePopupPtr = IntPtr.Zero;
                CommandListOffset = -1;
            });
        }

        public static bool IsConfirmationPopupActive
        {
            get => _helper.IsActive;
            private set => _helper.IsActive = value;
        }

        public static string CurrentPopupType { get; private set; }
        public static IntPtr ActivePopupPtr { get; private set; }
        public static int CommandListOffset { get; private set; }

        public static void SetActive(string typeName, IntPtr ptr, int cmdListOffset)
        {
            IsConfirmationPopupActive = true;
            CurrentPopupType = typeName;
            ActivePopupPtr = ptr;
            CommandListOffset = cmdListOffset;
        }

        public static void Clear()
        {
            IsConfirmationPopupActive = false;
        }

        public static bool ShouldSuppress() => IsConfirmationPopupActive && CommandListOffset >= 0;
    }

    /// <summary>
    /// Patches for popup dialogs - handles ALL popup reading (message + buttons).
    /// Uses TryCast for IL2CPP-safe type detection.
    ///
    /// Supported popup types:
    /// - CommonPopup: General confirmations (save/load, return to title, exit game, font/language changes)
    /// - JobChangePopup: Job change confirmation
    /// - ChangeMagicStonePopup: Spell learn/remove
    /// - GameOverSelectPopup: Game over options
    /// - InfomationPopup: Info-only (no buttons)
    /// - InputPopup: Text input
    /// - ChangeNamePopup: Character renaming
    ///
    /// EXCLUDES: Shop popups (handled by ShopPatches).
    /// </summary>
    internal static class PopupPatches
    {
        private static bool isPatched = false;

        // --- Memory offsets ---

        // IconTextView.nameText offset
        private const int ICON_TEXT_VIEW_NAME_TEXT_OFFSET = 0x20;

        // CommonCommand.text offset
        private const int COMMON_COMMAND_TEXT_OFFSET = 0x18;

        // CommonPopup (KeyInput) - line 462058
        private const int COMMON_TITLE_OFFSET = 0x38;      // IconTextView
        private const int COMMON_MESSAGE_OFFSET = 0x40;    // Text
        private const int COMMON_CMDLIST_OFFSET = 0x70;    // List<CommonCommand>

        // JobChangePopup - line 462451
        private const int JOBCHANGE_NAME_OFFSET = 0x40;    // Text (job name)
        private const int JOBCHANGE_CONFIRM_OFFSET = 0x48; // Text (confirm message)
        private const int JOBCHANGE_CMDLIST_OFFSET = 0x50;

        // ChangeMagicStonePopup - line 461855
        private const int MAGICSTONE_NAME_OFFSET = 0x28;   // Text
        private const int MAGICSTONE_DESC_OFFSET = 0x30;   // Text
        private const int MAGICSTONE_CMDLIST_OFFSET = 0x58;

        // GameOverSelectPopup - line 462249
        private const int GAMEOVER_CMDLIST_OFFSET = 0x40;

        // InfomationPopup - line 462369
        private const int INFO_TITLE_OFFSET = 0x28;        // IconTextView
        private const int INFO_MESSAGE_OFFSET = 0x30;      // Text

        // InputPopup - line 462397
        private const int INPUT_DESC_OFFSET = 0x30;        // Text

        // ChangeNamePopup - line 461995
        private const int CHANGENAME_DESC_OFFSET = 0x30;   // Text

        /// <summary>
        /// Apply manual Harmony patches for popups.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchBasePopup(harmony);
                GameOverPatches.ApplyPatches(harmony);
                TitleScreenPatches.ApplyPatches(harmony);
                isPatched = true;
                MelonLogger.Msg("[Popup] All popup patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch base Popup.Open() and Popup.Close() methods.
        /// </summary>
        private static void TryPatchBasePopup(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(BasePopup);

                var openMethod = AccessTools.Method(popupType, "Open");
                if (openMethod != null)
                {
                    var openPostfix = typeof(PopupPatches).GetMethod(nameof(PopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }

                var closeMethod = AccessTools.Method(popupType, "Close");
                if (closeMethod != null)
                {
                    var closePostfix = typeof(PopupPatches).GetMethod(nameof(PopupClose_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(closeMethod, postfix: new HarmonyMethod(closePostfix));
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching base Popup: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if shop is active.
        /// </summary>
        private static bool IsShopActive()
        {
            try
            {
                var shopController = GameObjectCache.GetOrFind<KeyInputShopController>();
                return shopController != null && shopController.IsOpne;
            }
            catch
            {
                return false;
            }
        }

        #region Text Reading Helpers

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

        private static string ReadIconTextViewText(IntPtr iconTextViewPtr)
        {
            if (iconTextViewPtr == IntPtr.Zero) return null;
            try
            {
                IntPtr nameTextPtr = Marshal.ReadIntPtr(iconTextViewPtr + ICON_TEXT_VIEW_NAME_TEXT_OFFSET);
                return ReadTextFromPointer(nameTextPtr);
            }
            catch { return null; }
        }

        private static string BuildAnnouncement(string title, string message)
        {
            title = string.IsNullOrWhiteSpace(title) ? null : TextUtils.StripIconMarkup(title.Trim());
            message = string.IsNullOrWhiteSpace(message) ? null : TextUtils.StripIconMarkup(message.Trim());

            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
                return $"{title}. {message}";
            else if (!string.IsNullOrEmpty(title))
                return title;
            else if (!string.IsNullOrEmpty(message))
                return message;
            return null;
        }

        #endregion

        #region Type-Specific Readers

        private static string ReadCommonPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + COMMON_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + COMMON_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadJobChangePopup(IntPtr ptr)
        {
            IntPtr namePtr = Marshal.ReadIntPtr(ptr + JOBCHANGE_NAME_OFFSET);
            string name = ReadTextFromPointer(namePtr);
            IntPtr confirmPtr = Marshal.ReadIntPtr(ptr + JOBCHANGE_CONFIRM_OFFSET);
            string confirm = ReadTextFromPointer(confirmPtr);
            return BuildAnnouncement(name, confirm);
        }

        private static string ReadChangeMagicStonePopup(IntPtr ptr)
        {
            IntPtr namePtr = Marshal.ReadIntPtr(ptr + MAGICSTONE_NAME_OFFSET);
            string name = ReadTextFromPointer(namePtr);
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + MAGICSTONE_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return BuildAnnouncement(name, desc);
        }

        private static string ReadGameOverSelectPopup(IntPtr ptr)
        {
            // GameOverSelectPopup has no title/message, just buttons
            // Announce "Game Over" as context
            return "Game Over";
        }

        private static string ReadInfomationPopup(IntPtr ptr)
        {
            IntPtr titleViewPtr = Marshal.ReadIntPtr(ptr + INFO_TITLE_OFFSET);
            string title = ReadIconTextViewText(titleViewPtr);
            IntPtr messagePtr = Marshal.ReadIntPtr(ptr + INFO_MESSAGE_OFFSET);
            string message = ReadTextFromPointer(messagePtr);
            return BuildAnnouncement(title, message);
        }

        private static string ReadInputPopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + INPUT_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        private static string ReadChangeNamePopup(IntPtr ptr)
        {
            IntPtr descPtr = Marshal.ReadIntPtr(ptr + CHANGENAME_DESC_OFFSET);
            string desc = ReadTextFromPointer(descPtr);
            return string.IsNullOrWhiteSpace(desc) ? null : TextUtils.StripIconMarkup(desc.Trim());
        }

        #endregion

        #region Button Reading

        /// <summary>
        /// Read current button label from active popup.
        /// Called by CursorNavigation_Postfix when popup is active.
        /// </summary>
        public static void ReadCurrentButton(GameCursor cursor)
        {
            try
            {
                if (PopupState.ActivePopupPtr == IntPtr.Zero)
                {
                    return;
                }

                if (PopupState.CommandListOffset < 0)
                {
                    return;
                }

                string buttonText = ReadButtonFromCommandList(
                    PopupState.ActivePopupPtr,
                    PopupState.CommandListOffset,
                    cursor.Index);

                if (!string.IsNullOrWhiteSpace(buttonText))
                {
                    buttonText = TextUtils.StripIconMarkup(buttonText);
                    FFIII_ScreenReaderMod.SpeakText(buttonText, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading button: {ex.Message}");
            }
        }

        private static string ReadButtonFromCommandList(IntPtr popupPtr, int cmdListOffset, int index)
        {
            try
            {
                IntPtr listPtr = Marshal.ReadIntPtr(popupPtr + cmdListOffset);
                if (listPtr == IntPtr.Zero) return null;

                // IL2CPP List: _size at 0x18, _items at 0x10
                int size = Marshal.ReadInt32(listPtr + 0x18);
                if (index < 0 || index >= size) return null;

                IntPtr itemsPtr = Marshal.ReadIntPtr(listPtr + 0x10);
                if (itemsPtr == IntPtr.Zero) return null;

                // Array elements start at 0x20, 8 bytes per pointer
                IntPtr commandPtr = Marshal.ReadIntPtr(itemsPtr + 0x20 + (index * 8));
                if (commandPtr == IntPtr.Zero) return null;

                // CommonCommand.text at offset 0x18
                IntPtr textPtr = Marshal.ReadIntPtr(commandPtr + COMMON_COMMAND_TEXT_OFFSET);
                return ReadTextFromPointer(textPtr);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error reading command list: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Popup Open/Close Postfixes

        /// <summary>
        /// Postfix for base Popup.Open - uses TryCast for type detection.
        /// </summary>
        public static void PopupOpen_Postfix(BasePopup __instance)
        {
            try
            {
                if (__instance == null)
                {
                    return;
                }
                if (IsShopActive())
                {
                    return;
                }

                // Use TryCast for IL2CPP-safe type detection
                // KeyInput types first (more common for keyboard/gamepad)

                // CommonPopup - general confirmations
                var commonPopup = __instance.TryCast<KeyInputCommonPopup>();
                if (commonPopup != null)
                {
                    HandlePopupDetected("CommonPopup", commonPopup.Pointer, COMMON_CMDLIST_OFFSET,
                        () => ReadCommonPopup(commonPopup.Pointer));
                    return;
                }

                // JobChangePopup
                var jobChange = __instance.TryCast<KeyInputJobChangePopup>();
                if (jobChange != null)
                {
                    HandlePopupDetected("JobChangePopup", jobChange.Pointer, JOBCHANGE_CMDLIST_OFFSET,
                        () => ReadJobChangePopup(jobChange.Pointer));
                    return;
                }

                // ChangeMagicStonePopup
                var magicStone = __instance.TryCast<KeyInputChangeMagicStonePopup>();
                if (magicStone != null)
                {
                    HandlePopupDetected("ChangeMagicStonePopup", magicStone.Pointer, MAGICSTONE_CMDLIST_OFFSET,
                        () => ReadChangeMagicStonePopup(magicStone.Pointer));
                    return;
                }

                // GameOverSelectPopup
                var gameOver = __instance.TryCast<KeyInputGameOverSelectPopup>();
                if (gameOver != null)
                {
                    HandlePopupDetected("GameOverSelectPopup", gameOver.Pointer, GAMEOVER_CMDLIST_OFFSET,
                        () => ReadGameOverSelectPopup(gameOver.Pointer));
                    return;
                }

                // InfomationPopup (no buttons)
                var info = __instance.TryCast<KeyInputInfomationPopup>();
                if (info != null)
                {
                    HandlePopupDetected("InfomationPopup", info.Pointer, -1,
                        () => ReadInfomationPopup(info.Pointer));
                    return;
                }

                // InputPopup (no buttons - input field)
                var input = __instance.TryCast<KeyInputInputPopup>();
                if (input != null)
                {
                    HandlePopupDetected("InputPopup", input.Pointer, -1,
                        () => ReadInputPopup(input.Pointer));
                    return;
                }

                // ChangeNamePopup (no buttons - input field)
                var changeName = __instance.TryCast<KeyInputChangeNamePopup>();
                if (changeName != null)
                {
                    HandlePopupDetected("ChangeNamePopup", changeName.Pointer, -1,
                        () => ReadChangeNamePopup(changeName.Pointer));
                    return;
                }

                // Touch types (fallback)
                var touchCommon = __instance.TryCast<TouchCommonPopup>();
                if (touchCommon != null)
                {
                    // Touch CommonPopup has different offsets: title=0x28, message=0x38
                    HandlePopupDetected("TouchCommonPopup", touchCommon.Pointer, -1, // Touch uses SimpleButton, not commandList
                        () => {
                            IntPtr titlePtr = Marshal.ReadIntPtr(touchCommon.Pointer + 0x28);
                            string title = ReadTextFromPointer(titlePtr);
                            IntPtr msgPtr = Marshal.ReadIntPtr(touchCommon.Pointer + 0x38);
                            string msg = ReadTextFromPointer(msgPtr);
                            return BuildAnnouncement(title, msg);
                        });
                    return;
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Open postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Handle a detected popup - set state and start delayed read.
        /// </summary>
        private static void HandlePopupDetected(string typeName, IntPtr ptr, int cmdListOffset, Func<string> readFunc)
        {
            PopupState.SetActive(typeName, ptr, cmdListOffset);
            CoroutineManager.StartManaged(DelayedPopupRead(ptr, typeName, readFunc));
        }

        /// <summary>
        /// Coroutine to read popup text after 1 frame delay.
        /// </summary>
        private static IEnumerator DelayedPopupRead(IntPtr popupPtr, string typeName, Func<string> readFunc)
        {
            yield return null; // Wait 1 frame

            try
            {
                if (popupPtr == IntPtr.Zero) yield break;

                string announcement = readFunc();
                if (!string.IsNullOrEmpty(announcement))
                {
                    FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in delayed read: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for base Popup.Close - clears state.
        /// </summary>
        public static void PopupClose_Postfix()
        {
            try
            {
                if (PopupState.IsConfirmationPopupActive)
                {
                    PopupState.Clear();
                }

                // Reset GameOverLoadPopup button dedup state
                AnnouncementDeduplicator.Reset(AnnouncementContexts.POPUP_GAMEOVER_LOAD_BUTTON);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Close postfix: {ex.Message}");
            }
        }

        #endregion
    }
}
