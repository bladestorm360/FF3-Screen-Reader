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
using GameCursor = Il2CppLast.UI.Cursor;
using CustomScrollViewType = Il2CppLast.UI.CustomScrollView;
using CustomScrollViewWithinRangeType = Il2CppLast.UI.CustomScrollView.WithinRangeType;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracking for config menu.
    /// </summary>
    internal static class ConfigMenuState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.CONFIG_MENU,
            AnnouncementContexts.CONFIG_TEXT, AnnouncementContexts.CONFIG_SETTING, AnnouncementContexts.CONFIG_ARROW,
            AnnouncementContexts.CONFIG_SLIDER, AnnouncementContexts.CONFIG_SLIDER_CONTROLLER,
            AnnouncementContexts.CONFIG_TOUCH_ARROW, AnnouncementContexts.CONFIG_TOUCH_SLIDER, AnnouncementContexts.CONFIG_TOUCH_SLIDER_CONTROLLER,
            AnnouncementContexts.CONFIG_KEYS_SETTING);

        static ConfigMenuState()
        {
            _helper.RegisterResetHandler();
        }

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
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
            var configController = GameObjectCache.GetOrFind<Il2CppLast.UI.KeyInput.ConfigController>();
            if (configController != null && configController.gameObject.activeInHierarchy)
                return true;

            var commandController = GameObjectCache.GetOrFind<Il2CppLast.UI.KeyInput.ConfigCommandController>();
            if (commandController != null && commandController.gameObject.activeInHierarchy)
                return true;

            // Config UI not visible - clear state
            ResetState();
            return false;
        }

        public static void ResetState()
        {
            _helper.IsActive = false;
        }
    }

    /// <summary>
    /// Controller-based patches for config menu navigation.
    /// Announces menu items directly from ConfigCommandController when navigating with up/down arrows.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigCommandController), nameof(Il2CppLast.UI.KeyInput.ConfigCommandController.SetFocus))]
    internal static class ConfigCommandController_SetFocus_Patch
    {
        private const string CONTEXT_TEXT = AnnouncementContexts.CONFIG_TEXT;
        private const string CONTEXT_SETTING = AnnouncementContexts.CONFIG_SETTING;

        [HarmonyPostfix]
        public static void Postfix(Il2CppLast.UI.KeyInput.ConfigCommandController __instance, bool isFocus)
        {
            try
            {
                // Set active state when config menu is in use
                if (isFocus)
                {
                    MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
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
                var configDetailsController = GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_KeyInput>();
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

    }

    /// <summary>
    /// Patch for SwitchArrowSelectTypeProcess - called when left/right arrows change toggle options.
    /// Only announces when the value actually changes.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchArrowSelectTypeProcess")]
    internal static class ConfigActualDetails_SwitchArrowSelectType_Patch
    {
        private const string CONTEXT_ARROW = AnnouncementContexts.CONFIG_ARROW;

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
    internal static class ConfigActualDetails_SwitchSliderType_Patch
    {
        private const string CONTEXT_SLIDER = AnnouncementContexts.CONFIG_SLIDER;
        private const string CONTEXT_SLIDER_CONTROLLER = AnnouncementContexts.CONFIG_SLIDER_CONTROLLER;

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
    internal static class ConfigActualDetailsTouch_SwitchArrowType_Patch
    {
        private const string CONTEXT_TOUCH_ARROW = AnnouncementContexts.CONFIG_TOUCH_ARROW;

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
    internal static class ConfigActualDetailsTouch_SwitchSliderType_Patch
    {
        private const string CONTEXT_TOUCH_SLIDER = AnnouncementContexts.CONFIG_TOUCH_SLIDER;
        private const string CONTEXT_TOUCH_SLIDER_CONTROLLER = AnnouncementContexts.CONFIG_TOUCH_SLIDER_CONTROLLER;

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
    /// Manual patch application for config menu.
    /// Handles menu close detection and controls reading.
    /// </summary>
    internal static class ConfigMenuPatches
    {
        /// <summary>
        /// Applies config menu patches using manual Harmony patching.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(Il2CppLast.UI.KeyInput.ConfigController),
                typeof(ConfigMenuPatches), logPrefix: "[Config Menu]");

            // Title-screen options menu drives ConfigMenuState the same way as the in-game
            // ConfigController, so the SetDropDownItemFocus language announcement (gated below) only
            // fires while the menu is genuinely open — not during the title-screen load that
            // instantiates the language OptionController before the menu is shown.
            HarmonyPatchHelper.PatchSetActive(harmony, typeof(Il2CppLast.UI.KeyInput.OptionController),
                typeof(ConfigMenuPatches), postfixName: nameof(OptionController_SetActive_Postfix),
                logPrefix: "[Config Menu]");

            // Patch controls/keys settings navigation
            try
            {
                // ConfigKeysSettingController has TWO SelectContent overloads (the 5-arg navigation
                // method and a 2-arg variant), so AccessTools.Method without an explicit Type[] throws
                // AmbiguousMatchException — disambiguate to the navigation overload: SelectContent(int,
                // CustomScrollView, Cursor, IEnumerable<ConfigControllCommandController>,
                // CustomScrollView.WithinRangeType).
                var selectContentMethod = AccessTools.Method(
                    typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController), "SelectContent",
                    new Type[]
                    {
                        typeof(int),
                        typeof(CustomScrollViewType),
                        typeof(GameCursor),
                        typeof(Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ConfigControllCommandController>),
                        typeof(CustomScrollViewWithinRangeType)
                    });
                if (selectContentMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(ConfigMenuPatches), nameof(SelectContent_Postfix));
                    harmony.Patch(selectContentMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Config Menu] Controls SelectContent patch applied");
                }
                else
                {
                    MelonLogger.Warning("[Config Menu] ConfigKeysSettingController.SelectContent not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error applying controls patch: {ex.Message}");
            }

            // Title-screen Language dropdown (keyboard/gamepad uses a Unity Dropdown driven by the
            // KeyInput OptionController). SetDropDownItemFocus is the discrete, event-driven hook —
            // it announces the focused language directly (no dedup), gated on the config menu being open.
            // DO NOT hook OptionController.UpdateSelectLanguage — it is an EMPTY method whose body is
            // the shared empty stub (RVA 0x26D8F0); detouring it corrupts every method sharing it → crash.
            PatchOption(harmony, "SetDropDownItemFocus", nameof(SetDropDownItemFocus_Postfix));

            // Remap assign-flow speaking (ConfigKeysSettingController, all real-bodied methods).
            // KeyboardSettingInit / GamePadSettingInit fire on entering assign mode → "press a
            // key/button" prompt. ChangeKeySetting (overloaded keyboard + gamepad) fires when the
            // binding is applied → announce the new mapping.
            PatchKeysSetting(harmony, "KeyboardSettingInit", nameof(KeyboardSettingInit_Postfix));
            PatchKeysSetting(harmony, "GamePadSettingInit", nameof(GamePadSettingInit_Postfix));

            // ChangeKeySetting is overloaded — patch every overload with the same __instance-only
            // postfix (avoids AmbiguousMatchException without needing an exact Type[]).
            try
            {
                var changePostfix = new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), nameof(ChangeKeySetting_Postfix)));
                int changeCount = 0;
                foreach (var m in typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController).GetMethods(
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                {
                    if (m.Name == "ChangeKeySetting") { harmony.Patch(m, postfix: changePostfix); changeCount++; }
                }
                MelonLogger.Msg($"[Config Menu] ConfigKeysSettingController.ChangeKeySetting patched ({changeCount} overload(s))");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching ChangeKeySetting: {ex.Message}");
            }
        }

        private static void PatchOption(HarmonyLib.Harmony harmony, string method, string postfixName)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.OptionController), method);
                if (m != null)
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                    MelonLogger.Msg($"[Config Menu] OptionController.{method} patch applied");
                }
                else
                {
                    MelonLogger.Warning($"[Config Menu] OptionController.{method} not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching OptionController.{method}: {ex.Message}");
            }
        }

        private static void PatchKeysSetting(HarmonyLib.Harmony harmony, string method, string postfixName)
        {
            try
            {
                var m = AccessTools.Method(typeof(Il2CppLast.UI.KeyInput.ConfigKeysSettingController), method);
                if (m != null)
                {
                    harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(ConfigMenuPatches), postfixName)));
                    MelonLogger.Msg($"[Config Menu] ConfigKeysSettingController.{method} patch applied");
                }
                else
                {
                    MelonLogger.Warning($"[Config Menu] ConfigKeysSettingController.{method} not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching ConfigKeysSettingController.{method}: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigController.SetActive - clears state when menu closes.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (!isActive)
            {
                ConfigMenuState.ResetState();
            }
        }

        /// <summary>
        /// Postfix for OptionController.SetActive (title-screen options menu). Drives ConfigMenuState
        /// so the language-dropdown announcement is gated on the menu being genuinely open.
        /// </summary>
        public static void OptionController_SetActive_Postfix(bool isActive)
        {
            if (isActive)
            {
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
            }
            else
            {
                ConfigMenuState.ResetState();
            }
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.SelectContent.
        /// Announces action name and key bindings when navigating controls settings.
        /// </summary>
        public static void SelectContent_Postfix(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance,
            int index,
            Il2CppSystem.Collections.Generic.IEnumerable<Il2CppLast.UI.KeyInput.ConfigControllCommandController> contentList)
        {
            try
            {
                if (__instance == null || contentList == null) return;

                var list = contentList.TryCast<Il2CppSystem.Collections.Generic.List<Il2CppLast.UI.KeyInput.ConfigControllCommandController>>();
                var command = SelectContentHelper.TryGetItem(list, index);

                string announcement = BuildCommandAnnouncement(__instance, command);
                if (string.IsNullOrWhiteSpace(announcement)) return;

                if (!AnnouncementDeduplicator.ShouldAnnounce(AnnouncementContexts.CONFIG_KEYS_SETTING, announcement))
                    return;

                // Append cursor position (N of M) among the control bindings.
                announcement = MenuPosition.Format(announcement, index, list != null ? list.Count : 0);
                MelonLogger.Msg($"[Config Menu] Controls: {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigKeysSettingController.SelectContent patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the controls-screen announcement for one command: action name + keyboard binding
        /// (readable key names) + gamepad binding. The gamepad icon is an unreadable controller glyph,
        /// so we translate the LIVE bound button (from the screen's KeyConfigData) to family-aware text
        /// via ControllerLabels, falling back to the raw glyph only if that can't be resolved.
        /// Shared by the navigation read (SelectContent) and the rebind read (ChangeKeySetting).
        /// </summary>
        private static string BuildCommandAnnouncement(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController owner,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            // Action name from the view's nameTexts
            if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
            {
                foreach (var textComp in command.view.nameTexts)
                {
                    if (textComp != null && !string.IsNullOrWhiteSpace(textComp.text))
                    {
                        string text = textComp.text.Trim();
                        if (!text.StartsWith("MENU_") && !textParts.Contains(text))
                            textParts.Add(text);
                    }
                }
            }

            // Keyboard binding — already readable key names.
            AppendIconTexts(textParts, command.keyboardIconController);

            // Gamepad binding — the icon is a sprite glyph carrying NO readable text (iconTextList is
            // empty), so reading it never worked. The keyboard and gamepad remap sections are mutually
            // exclusive per row: keyboard rows carry a key name, gamepad rows don't. So when the keyboard
            // icon is empty we're on the gamepad section — translate the LIVE bound button via
            // ControllerLabels (the keyboard binding above already handled keyboard-section rows).
            if (ResolveGamepadButtonText(owner, command) is string btn && !string.IsNullOrEmpty(btn)
                && !IconHasContent(command.keyboardIconController))
            {
                textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>True if an icon controller is currently showing readable binding text.</summary>
        private static bool IconHasContent(ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return false;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var t = iconView.iconTextList[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves a remap row's CURRENT (remappable) gamepad button to family-aware text. Reads the
        /// live binding from the screen's KeyConfigData (GameKey → Unity KeyCode), maps the KeyCode to
        /// an SDL button index, and lets ControllerLabels pick the text for the connected controller.
        /// Returns null if it can't be resolved (caller falls back to the raw glyph).
        /// </summary>
        private static string ResolveGamepadButtonText(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController owner,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            try
            {
                if (owner == null || command == null) return null;
                var kd = owner.keydata;
                if (kd == null) return null;
                var dict = kd.GetGamePadKeyConfigtDictionary();
                if (dict == null || !dict.ContainsKey(command.key)) return null;
                int sdl = JoystickKeyCodeToSdlButton((int)dict[command.key]);
                if (sdl < 0) return null;
                return ControllerLabels.GetButtonLabel(sdl);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Maps a Unity legacy KeyCode.JoystickButtonN (330+) to an SDL gamepad button index using the
        /// XInput-standard layout (correct for Xbox + most PC controllers). ControllerLabels then yields
        /// the right family text for whichever controller is connected. Returns -1 if not a mapped button.
        /// </summary>
        private static int JoystickKeyCodeToSdlButton(int keyCode)
        {
            switch (keyCode)
            {
                // FFPR keeps the bottom/right face buttons in the Japanese arrangement: Confirm is
                // stored on JoystickButton1 and Cancel on JoystickButton0. The American build confirms
                // with the BOTTOM button (A/Cross), so JB1→SOUTH and JB0→EAST — i.e. swapped from
                // Unity's XInput default. (X/Y below are unaffected.)
                case 330: return SDL3.SDL_GAMEPAD_BUTTON_EAST;           // JoystickButton0 — Cancel (B/Circle)
                case 331: return SDL3.SDL_GAMEPAD_BUTTON_SOUTH;          // JoystickButton1 — Confirm (A/Cross)
                case 332: return SDL3.SDL_GAMEPAD_BUTTON_WEST;           // JoystickButton2 — X/Square
                case 333: return SDL3.SDL_GAMEPAD_BUTTON_NORTH;          // JoystickButton3 — Y/Triangle
                case 334: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER;  // JoystickButton4 — LB
                case 335: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER; // JoystickButton5 — RB
                case 336: return SDL3.SDL_GAMEPAD_BUTTON_BACK;           // JoystickButton6 — Back/View
                case 337: return SDL3.SDL_GAMEPAD_BUTTON_START;          // JoystickButton7 — Start/Menu
                case 338: return SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK;     // JoystickButton8 — LS
                case 339: return SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK;    // JoystickButton9 — RS
                default: return -1;
            }
        }

        /// <summary>Appends an icon controller's binding labels (iconTextList) to textParts, deduped.</summary>
        private static void AppendIconTexts(System.Collections.Generic.List<string> textParts, ConfigKeyIconController icon)
        {
            var iconView = icon?.view;
            if (iconView == null || iconView.iconTextList == null) return;
            for (int i = 0; i < iconView.iconTextList.Count; i++)
            {
                var iconText = iconView.iconTextList[i];
                if (iconText != null && !string.IsNullOrWhiteSpace(iconText.text))
                {
                    string text = iconText.text.Trim();
                    if (!textParts.Contains(text))
                        textParts.Add(text);
                }
            }
        }

        // ── Title-screen Language dropdown ──────────────────────────────────────────────
        // SetDropDownItemFocus is the EVENT-DRIVEN announce (no dedup), gated to when the config menu
        // is actually open so it cannot speak the current language at the title "Press any button."

        /// <summary>Focused language label: prefer the tracked dropdown item, else the dropdown value.</summary>
        private static string GetFocusedLanguageLabel(Il2CppLast.UI.KeyInput.OptionController inst)
        {
            var item = inst.selectedItem;
            if (item == null) return null;

            if (item.view != null && item.view.LabelText != null)
            {
                string t = item.view.LabelText.text;
                if (!string.IsNullOrWhiteSpace(t)) return t;
            }
            var dd = inst.selectedDoropDown;
            if (dd != null && dd.options != null && dd.value >= 0 && dd.value < dd.options.Count)
            {
                var opt = dd.options[dd.value];
                if (opt != null && !string.IsNullOrWhiteSpace(opt.text)) return opt.text;
            }
            // The CURRENT language's item has an empty LabelText (its native name is a sprite; the
            // parenthetical English name is omitted for the current language). A focused item with no
            // readable label is therefore the current language → name it from MessageManager.
            return ConfigMenuReader.GetCurrentLanguageDisplayName();
        }

        /// <summary>
        /// EVENT-DRIVEN announce. OptionController.SetDropDownItemFocus (KeyInput) is the discrete
        /// "focused dropdown item changed" hook — speaks the focused language directly, NO dedup.
        /// </summary>
        public static void SetDropDownItemFocus_Postfix(Il2CppLast.UI.KeyInput.OptionController __instance)
        {
            try
            {
                if (__instance == null) return;
                string label = GetFocusedLanguageLabel(__instance);
                // Gate on the config menu being genuinely OPEN — the same lifecycle flag driven by
                // ConfigController/OptionController.SetActive. The title screen instantiates the
                // language OptionController during load and fires SetDropDownItemFocus BEFORE the menu
                // is opened (IsActive=false), which would speak the current language over "Press any
                // button." Real dropdown navigation happens with the menu open (IsActive=true).
                if (!ConfigMenuState.IsActive) return;
                if (string.IsNullOrWhiteSpace(label)) return;
                // Append cursor position (N of M) among the language options.
                string spoken = label.Trim();
                var dd = __instance.selectedDoropDown;
                if (dd != null && dd.options != null)
                    spoken = MenuPosition.Format(spoken, dd.value, dd.options.Count);
                FFIII_ScreenReaderMod.SpeakText(spoken, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetDropDownItemFocus patch: {ex.Message}");
            }
        }

        // ── Remap assign-flow (ConfigKeysSettingController) ──────────────────────────────
        // Entering assign mode → announce the "press a key/button" prompt. Init methods fire once on
        // state entry → event-driven, no dedup.
        public static void KeyboardSettingInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: false);

        public static void GamePadSettingInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => AnnounceAssignPrompt(__instance, gamepad: true);

        private static void AnnounceAssignPrompt(Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            try
            {
                if (inst == null) return;
                FFIII_ScreenReaderMod.SpeakText(gamepad ? "Press a button." : "Press a key.", interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for ConfigKeysSettingController.ChangeKeySetting (all overloads). Fires when a
        /// binding is applied — re-reads the just-edited command and announces the new mapping.
        /// Event-driven, no dedup.
        /// </summary>
        public static void ChangeKeySetting_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
        {
            try
            {
                if (__instance == null) return;
                string announcement = BuildCommandAnnouncement(__instance, __instance.selectedCommand);
                if (string.IsNullOrWhiteSpace(announcement)) return;
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ChangeKeySetting patch: {ex.Message}");
            }
        }
    }
}
