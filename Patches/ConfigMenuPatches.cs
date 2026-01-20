using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;
using UnityEngine;
using Key = Il2CppSystem.Input.Key;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracking for config menu.
    /// </summary>
    public static class ConfigMenuState
    {
        /// <summary>
        /// True when config menu is active and handling announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.CONFIG_MENU);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.CONFIG_MENU, value);
        }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates that config UI is actually visible to handle title screen config menu
        /// which uses OptionWindowController (not ConfigController) and doesn't trigger SetActive.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsActive)
                return false;

            // Validate config UI is actually visible (handles title screen config menu case)
            var configController = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ConfigController>();
            if (configController != null && configController.gameObject.activeInHierarchy)
                return true;

            var commandController = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ConfigCommandController>();
            if (commandController != null && commandController.gameObject.activeInHierarchy)
                return true;

            // Config UI not visible - clear state
            ResetState();
            return false;
        }

        public static void ResetState()
        {
            IsActive = false;
        }
    }

    /// <summary>
    /// Controller-based patches for config menu navigation.
    /// Announces menu items directly from ConfigCommandController when navigating with up/down arrows.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigCommandController), nameof(Il2CppLast.UI.KeyInput.ConfigCommandController.SetFocus))]
    public static class ConfigCommandController_SetFocus_Patch
    {
        private const string CONTEXT_TEXT = "ConfigMenu.Text";
        private const string CONTEXT_SETTING = "ConfigMenu.Setting";

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigCommandController __instance, bool isFocus)
        {
            try
            {
                // Set active state when config menu is in use
                if (isFocus)
                {
                    // Clear other menu states to prevent conflicts
                    FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("Config");
                    ConfigMenuState.IsActive = true;
                }

                // Only announce when gaining focus (not losing it)
                if (!isFocus)
                {
                    return;
                }

                // Safety checks
                if (__instance == null)
                {
                    return;
                }

                // Don't announce if controller is not active (prevents announcements during scene loading)
                if (!__instance.gameObject.activeInHierarchy)
                {
                    return;
                }

                // Verify this controller is actually the selected one by checking the parent ConfigActualDetailsControllerBase
                var configDetailsController = UnityEngine.Object.FindObjectOfType<ConfigActualDetailsControllerBase_KeyInput>();
                if (configDetailsController != null)
                {
                    var selectedCommand = configDetailsController.SelectedCommand;
                    if (selectedCommand != null && selectedCommand != __instance)
                    {
                        // This is not the selected controller, skip
                        return;
                    }
                }

                // Get the view which contains the localized text
                var view = __instance.view;
                if (view == null)
                {
                    return;
                }

                // Get the name text (localized)
                var nameText = view.NameText;
                if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                {
                    return;
                }

                string menuText = nameText.text.Trim();

                // Filter out template/placeholder values
                if (menuText == "NewText" || menuText == "Text" || menuText == "Name" || menuText == "Label")
                {
                    return;
                }

                // Also try to get the current value for this config option
                string configValue = ConfigMenuReader.FindConfigValueFromController(__instance);

                string announcement = menuText;
                if (!string.IsNullOrWhiteSpace(configValue))
                {
                    announcement = $"{menuText}: {configValue}";
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_TEXT, announcement))
                {
                    return;
                }

                // Check if this is the same setting re-focused (from arrow key value change)
                string lastSettingName = AnnouncementDeduplicator.GetLastString(CONTEXT_SETTING);
                if (menuText == lastSettingName)
                {
                    // Same setting re-focused - SwitchArrowSelectTypeProcess handles value announcements
                    return;
                }

                // Different setting - update setting name tracker
                AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SETTING, menuText);

                MelonLogger.Msg($"[Config Menu] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigCommandController.SetFocus patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the duplicate tracker (call when exiting config menu)
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(CONTEXT_TEXT, CONTEXT_SETTING);
        }
    }

    /// <summary>
    /// Patch for SwitchArrowSelectTypeProcess - called when left/right arrows change toggle options.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchArrowSelectTypeProcess")]
    public static class ConfigActualDetails_SwitchArrowSelectType_Patch
    {
        private const string CONTEXT_ARROW = "ConfigMenu.Arrow";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance,
            ConfigCommandController controller,
            Key key)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;

                // Get arrow select value
                if (view.ArrowSelectTypeRoot != null && view.ArrowSelectTypeRoot.activeSelf)
                {
                    var arrowRoot = view.ArrowSelectTypeRoot;
                    var texts = arrowRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string textValue = text.text.Trim();
                            // Filter out arrow characters and template values
                            if (IsValidConfigValue(textValue))
                            {
                                // Only announce if value changed
                                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_ARROW, textValue)) return;

                                MelonLogger.Msg($"[ConfigMenu] Arrow value changed: {textValue}");
                                FFIII_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SwitchArrowSelectTypeProcess patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a config value is valid (not a placeholder, template, or arrow character).
        /// </summary>
        private static bool IsValidConfigValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Filter out arrow characters
            if (value == "<" || value == ">" || value == "◀" || value == "▶" ||
                value == "←" || value == "→")
                return false;

            // Filter out known template/placeholder values
            if (value == "NewText" || value == "ReEquip" || value == "Text" ||
                value == "Label" || value == "Value" || value == "Name")
                return false;

            return true;
        }
    }

    /// <summary>
    /// Patch for SwitchSliderTypeProcess - called when left/right arrows change slider values.
    /// Only announces when the value actually changes for the SAME option.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchSliderTypeProcess")]
    public static class ConfigActualDetails_SwitchSliderType_Patch
    {
        private const string CONTEXT_SLIDER = "ConfigMenu.Slider";
        private const string CONTEXT_SLIDER_CONTROLLER = "ConfigMenu.SliderController";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase __instance,
            ConfigCommandController controller,
            Key key)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;
                if (view.Slider == null) return;

                // Calculate percentage using proper min/max range
                string percentage = ConfigMenuReader.GetSliderPercentage(view.Slider);
                if (string.IsNullOrEmpty(percentage)) return;

                // Check if we moved to a different controller (different option)
                // If so, don't announce - let SetFocus handle the full "Name: Value" announcement
                if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SLIDER_CONTROLLER, controller))
                {
                    // Update the percentage tracker for the new controller
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SLIDER, percentage);
                    return;
                }

                // Same controller - only announce if value changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_SLIDER, percentage))
                {
                    return;
                }

                MelonLogger.Msg($"[ConfigMenu] Slider value changed: {percentage}");
                FFIII_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SwitchSliderTypeProcess patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for Touch mode arrow button handling.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchArrowTypeProcess")]
    public static class ConfigActualDetailsTouch_SwitchArrowType_Patch
    {
        private const string CONTEXT_TOUCH_ARROW = "ConfigMenu.TouchArrow";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase __instance,
            Il2CppLast.UI.Touch.ConfigCommandController controller,
            int value)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;

                // Check arrow button type
                if (view.ArrowButtonTypeRoot != null && view.ArrowButtonTypeRoot.activeSelf)
                {
                    var texts = view.ArrowButtonTypeRoot.GetComponentsInChildren<UnityEngine.UI.Text>();
                    foreach (var text in texts)
                    {
                        // Skip inactive text components
                        if (text == null || text.gameObject == null || !text.gameObject.activeInHierarchy)
                            continue;

                        if (!string.IsNullOrWhiteSpace(text.text))
                        {
                            string textValue = text.text.Trim();
                            // Filter out arrow characters and template values
                            if (IsValidTouchConfigValue(textValue))
                            {
                                // Only announce if value changed
                                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_TOUCH_ARROW, textValue)) return;

                                MelonLogger.Msg($"[ConfigMenu] Touch arrow value changed: {textValue}");
                                FFIII_ScreenReaderMod.SpeakText(textValue, interrupt: true);
                                return;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in Touch SwitchArrowTypeProcess patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a touch config value is valid (not a placeholder, template, or arrow character).
        /// </summary>
        private static bool IsValidTouchConfigValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            // Filter out arrow characters
            if (value == "<" || value == ">" || value == "◀" || value == "▶" ||
                value == "←" || value == "→")
                return false;

            // Filter out known template/placeholder values
            if (value == "NewText" || value == "ReEquip" || value == "Text" ||
                value == "Label" || value == "Value" || value == "Name")
                return false;

            return true;
        }
    }

    /// <summary>
    /// Patch for Touch mode slider handling.
    /// Only announces when the value actually changes for the SAME option.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchSliderTypeProcess")]
    public static class ConfigActualDetailsTouch_SwitchSliderType_Patch
    {
        private const string CONTEXT_TOUCH_SLIDER = "ConfigMenu.TouchSlider";
        private const string CONTEXT_TOUCH_SLIDER_CONTROLLER = "ConfigMenu.TouchSliderController";

        [HarmonyPostfix]
        public static void Postfix(
            Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase __instance,
            Il2CppLast.UI.Touch.ConfigCommandController controller,
            float value)
        {
            try
            {
                if (controller == null || controller.view == null) return;

                var view = controller.view;
                if (view.SliderTypeRoot == null) return;

                // Find the slider in the slider root
                var slider = view.SliderTypeRoot.GetComponentInChildren<UnityEngine.UI.Slider>();
                if (slider == null) return;

                // Calculate percentage using proper min/max range
                string percentage = ConfigMenuReader.GetSliderPercentage(slider);
                if (string.IsNullOrEmpty(percentage)) return;

                // Check if we moved to a different controller (different option)
                // If so, don't announce - let SetFocus handle the full "Name: Value" announcement
                if (AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_TOUCH_SLIDER_CONTROLLER, controller))
                {
                    // Update the percentage tracker for the new controller
                    AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_TOUCH_SLIDER, percentage);
                    return;
                }

                // Same controller - only announce if value changed
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_TOUCH_SLIDER, percentage))
                {
                    return;
                }

                MelonLogger.Msg($"[ConfigMenu] Touch slider value changed: {percentage}");
                FFIII_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in Touch SwitchSliderTypeProcess patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Legacy static class for manual patch application.
    /// Now only used for registering with the main mod's harmony instance.
    /// </summary>
    public static class ConfigMenuPatches
    {
        /// <summary>
        /// Applies config menu patches using manual Harmony patching.
        /// Note: Most patches now use HarmonyPatch attributes and are auto-applied by MelonLoader.
        /// This method adds transition patches for menu close detection.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(Il2CppLast.UI.KeyInput.ConfigController),
                typeof(ConfigMenuPatches), logPrefix: "[Config Menu]");
        }

        /// <summary>
        /// Postfix for SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ConfigMenuState.ResetState();
            }
        }
    }
}
