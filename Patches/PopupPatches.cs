using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Type aliases for IL2CPP types
using KeyInputCommonPopup = Il2CppLast.UI.KeyInput.CommonPopup;
using TouchCommonPopup = Il2CppLast.UI.Touch.CommonPopup;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for popup dialogs - announces confirmation messages.
    /// Handles job change confirmation, load game confirmation, etc.
    /// </summary>
    public static class PopupPatches
    {
        private static bool isPatched = false;

        /// <summary>
        /// Apply manual Harmony patches for popups.
        /// Called from FFIII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchKeyInputCommonPopupOpen(harmony);
                TryPatchTouchCommonPopupOpen(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch KeyInput CommonPopup.Open - announces popup message when shown.
        /// </summary>
        private static void TryPatchKeyInputCommonPopupOpen(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(KeyInputCommonPopup);

                // Find Open method (overridden from Popup base class)
                var openMethod = popupType.GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Instance);

                if (openMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(KeyInputCommonPopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(openMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched KeyInput CommonPopup.Open successfully");
                }
                else
                {
                    MelonLogger.Warning("[Popup] Could not find KeyInput CommonPopup.Open method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching KeyInput CommonPopup.Open: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch Touch CommonPopup.Open - announces popup message when shown.
        /// </summary>
        private static void TryPatchTouchCommonPopupOpen(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type popupType = typeof(TouchCommonPopup);

                // Find Open method (inherited from Popup base class)
                var openMethod = popupType.GetMethod("Open",
                    BindingFlags.Public | BindingFlags.Instance);

                if (openMethod != null)
                {
                    var postfix = typeof(PopupPatches).GetMethod(nameof(TouchCommonPopupOpen_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(openMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Popup] Patched Touch CommonPopup.Open successfully");
                }
                else
                {
                    MelonLogger.Warning("[Popup] Could not find Touch CommonPopup.Open method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error patching Touch CommonPopup.Open: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for KeyInput CommonPopup.Open - reads the popup message.
        /// </summary>
        public static void KeyInputCommonPopupOpen_Postfix(KeyInputCommonPopup __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Read the message from the popup
                string message = __instance.Message;

                if (string.IsNullOrWhiteSpace(message))
                {
                    MelonLogger.Msg("[Popup] KeyInput Open called but message is empty");
                    return;
                }

                // Clean up the message (remove any markup)
                message = TextUtils.StripIconMarkup(message);

                if (string.IsNullOrWhiteSpace(message))
                    return;

                MelonLogger.Msg($"[Popup] {message}");
                FFIII_ScreenReaderMod.SpeakText(message, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in KeyInput CommonPopup.Open postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for Touch CommonPopup.Open - reads the popup message.
        /// </summary>
        public static void TouchCommonPopupOpen_Postfix(TouchCommonPopup __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Read the message from the popup
                string message = __instance.Message;

                if (string.IsNullOrWhiteSpace(message))
                {
                    MelonLogger.Msg("[Popup] Touch Open called but message is empty");
                    return;
                }

                // Clean up the message (remove any markup)
                message = TextUtils.StripIconMarkup(message);

                if (string.IsNullOrWhiteSpace(message))
                    return;

                MelonLogger.Msg($"[Popup] {message}");
                FFIII_ScreenReaderMod.SpeakText(message, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Popup] Error in Touch CommonPopup.Open postfix: {ex.Message}");
            }
        }
    }
}
