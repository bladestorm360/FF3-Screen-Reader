using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
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
            AnnouncementContexts.CONFIG_ARROW,
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

            // The details controller last read is still shown (title Options or in-game Config): no
            // scene search. This runs on every generic cursor move, and the title Options screen has
            // no ConfigController, so the lookup below would otherwise scan the scene each time.
            if (ConfigActualDetails_SelectCommand_Patch.IsLastDetailsControllerShown())
                return true;

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
    /// Config menu row announcements. Hooks ConfigActualDetailsControllerBase.SelectCommand — the
    /// private method the game invokes once per up/down cursor move (ConfigCommandController.SetFocus,
    /// by contrast, is re-asserted every frame). Event-driven, so no dedup is needed. The in-game
    /// ConfigController also fires it for the first row on open; the title Options screen does not,
    /// so it gets a one-frame-delayed initial read from OptionController.ShowConfig / InitSelectLanguage.
    /// </summary>
    internal static class ConfigActualDetails_SelectCommand_Patch
    {
        // detailsController on the KeyInput ConfigController (typed read avoids FindObjectOfType picking
        // the cheatSettingsController, which is the same type)
        private const int OFFSET_DETAILS_CONTROLLER = 0x48;

        // Bestiary-return re-announce: frames to retry after the library-return fade-in callback, until
        // the focused row is active
        private const int LIBRARY_RETURN_MAX_FRAMES = 10;

        public static void Postfix(ConfigActualDetailsControllerBase_KeyInput __instance)
        {
            try
            {
                // Gate on a real config menu being open (ConfigController / OptionController.SetActive):
                // the base class is shared with the load-game flow and other screens.
                if (!ConfigMenuState.IsActive)
                    return;

                AnnounceSelectedConfigCommand(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ConfigActualDetails.SelectCommand patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces the focused config row as "Name: Value" plus its "(X of Y)" position within the
        /// config list. Returns the spoken text, or null if nothing was spoken.
        /// </summary>
        internal static string AnnounceSelectedConfigCommand(ConfigActualDetailsControllerBase_KeyInput instance)
        {
            if (instance == null)
                return null;

            var selected = instance.SelectedCommand;
            if (selected == null)
                return null;

            // A slider row: listen to its value changes (once per slider)
            ConfigSliderValueListener.Attach(selected);

            if (!selected.gameObject.activeInHierarchy)
                return null;

            var view = selected.view;
            var nameText = view?.NameText;
            if (nameText == null || string.IsNullOrWhiteSpace(nameText.text))
                return null;

            string menuText = nameText.text.Trim();

            // Filter out template/placeholder values
            if (menuText == "NewText" || menuText == "Text" || menuText == "Name" || menuText == "Label")
                return null;

            string configValue = ConfigMenuReader.FindConfigValueFromController(selected);
            string announcement = string.IsNullOrWhiteSpace(configValue)
                ? menuText
                : $"{menuText}: {configValue}";

            // Position of the focused row within the list the cursor navigates (best-effort)
            int index = -1, count = -1;
            try
            {
                var list = instance.CommandList;
                if (list != null)
                {
                    count = list.Count;
                    for (int i = 0; i < count; i++)
                    {
                        var c = list[i];
                        if (c != null && c.Pointer == selected.Pointer) { index = i; break; }
                    }
                }
            }
            catch { }

            announcement = MenuPosition.Format(announcement, index, count);
            FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            lastDetailsController = instance;
            lastRowSpokenFrame = Time.frameCount;
            return announcement;
        }

        // Frame of the last row read. The deferred reads below (title Options open, popup close, bestiary
        // return) are fallbacks for when SelectCommand does not fire: each is armed with the current frame
        // and gives up if a row was read in or after that frame, because the game's own SelectCommand
        // (e.g. ShowConfig → InitConfig → ResetCursor, or a popup's "No" callback re-entering the Config
        // state) already spoke it.
        private static int lastRowSpokenFrame = -1;

        private static bool RowSpokenSince(int armedFrame) => lastRowSpokenFrame >= armedFrame;

        // The details controller of the config screen being navigated (title Options or in-game Config),
        // from the latest row read: lets the popup-close re-read find it without a scene search.
        private static ConfigActualDetailsControllerBase_KeyInput lastDetailsController;

        internal static bool IsLastDetailsControllerShown()
        {
            try
            {
                var details = lastDetailsController;
                return details != null && details.gameObject != null && details.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        /// <summary>The focused row of the config screen last read (null if none).</summary>
        internal static ConfigCommandController FocusedCommand
        {
            get
            {
                try { return lastDetailsController?.SelectedCommand; }
                catch { return null; }
            }
        }

        /// <summary>
        /// A popup over the config screen closed (Quit / Return to Title answered No): SelectCommand does
        /// not re-fire, so re-read the focused row one frame later through the controller last read.
        /// The config screen is already on screen, so there is nothing to wait for and nothing to search
        /// (the title Options screen has no ConfigController, which made the bounded wait below search
        /// the scene for its full 10 s).
        /// </summary>
        internal static void ReannounceAfterPopup()
        {
            CoroutineManager.StartManaged(ReannounceNextFrame(++reannounceGen, Time.frameCount));
        }

        private static System.Collections.IEnumerator ReannounceNextFrame(int gen, int armedFrame)
        {
            yield return null;
            if (gen != reannounceGen) yield break;
            if (RowSpokenSince(armedFrame)) yield break; // the popup's callback re-read the row
            try
            {
                var details = lastDetailsController;
                if (ConfigMenuState.IsActive && details != null && details.gameObject.activeInHierarchy)
                    AnnounceSelectedConfigCommand(details);
            }
            catch { }
        }

        // Frame the pending initial-focus read was armed (-1 = none). A frame stamp, not a bool: if
        // CoroutineManager evicts the coroutine before it runs, a stamp older than a few frames is
        // treated as free instead of blocking the read forever.
        private static int initialFocusPendingFrame = -1;
        private const int INITIAL_FOCUS_STALE_FRAMES = 10;

        /// <summary>
        /// One-frame-delayed initial-focus read for the title-screen Options config, whose
        /// OptionController doesn't fire SelectCommand on open. The pending guard collapses a same-frame
        /// ShowConfig + InitSelectLanguage pair into one read.
        /// </summary>
        internal static void AnnounceInitialFocusDelayed(Il2CppLast.UI.KeyInput.OptionController inst)
        {
            if (inst == null) return;
            if (initialFocusPendingFrame >= 0 && Time.frameCount - initialFocusPendingFrame < INITIAL_FOCUS_STALE_FRAMES) return;
            initialFocusPendingFrame = Time.frameCount;
            CoroutineManager.StartManaged(InitialFocusCoroutine(inst, Time.frameCount));
        }

        private static System.Collections.IEnumerator InitialFocusCoroutine(Il2CppLast.UI.KeyInput.OptionController inst, int armedFrame)
        {
            yield return null;
            initialFocusPendingFrame = -1;
            if (RowSpokenSince(armedFrame)) yield break; // InitConfig's own SelectCommand spoke the row
            try
            {
                if (inst != null && ConfigMenuState.IsActive)
                {
                    // The Options screen keeps several details controllers active; use the one it is
                    // showing (a blind FindObjectOfType can return the language section).
                    var ctrl = inst.configActualDetailsController
                        ?? GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_KeyInput>();
                    AnnounceSelectedConfigCommand(ctrl);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing initial config focus: {ex.Message}");
            }
        }

        private static int reannounceGen;

        // Bestiary exit armed the re-read (frame it was armed; -1 = not armed). Consumed by the
        // library-return fade-in callback, cleared when the config menu closes.
        private static int libraryReturnArmedFrame = -1;

        /// <summary>
        /// Leaving the config-menu bestiary: arm the focused-row re-read for the moment the config menu is
        /// back on screen (the menu resumes without firing SelectCommand).
        /// </summary>
        internal static void ArmReannounceAfterLibrary()
        {
            libraryReturnArmedFrame = Time.frameCount;
        }

        /// <summary>Config menu closed: a pending library-return re-read must not leak into the next open.</summary>
        internal static void CancelReannounceAfterLibrary()
        {
            libraryReturnArmedFrame = -1;
            reannounceGen++;
        }

        /// <summary>
        /// Library-return fade-in finished (FieldMap.&lt;InitMenu&gt;b__90_0, see GameStatePatches): the
        /// config menu is back on screen. Consumes the arm and reads the focused row, retrying a few frames
        /// until it is active. Replaces a WaitForSeconds(0.1) wait of up to 10 s.
        /// </summary>
        internal static void OnMenuResumedAfterLibrary()
        {
            int armedFrame = libraryReturnArmedFrame;
            if (armedFrame < 0) return;
            libraryReturnArmedFrame = -1;
            CoroutineManager.StartManaged(ReannounceWhenConfigReady(++reannounceGen, armedFrame));
        }

        private static System.Collections.IEnumerator ReannounceWhenConfigReady(int gen, int armedFrame)
        {
            for (int frame = 0; frame < LIBRARY_RETURN_MAX_FRAMES; frame++)
            {
                yield return null;
                if (gen != reannounceGen) yield break;
                if (RowSpokenSince(armedFrame)) yield break; // the menu's own SelectCommand read the row

                // The details controller read before entering the bestiary, if its screen is shown again
                if (IsLastDetailsControllerShown())
                {
                    try
                    {
                        if (AnnounceSelectedConfigCommand(lastDetailsController) != null)
                        {
                            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
                            yield break;
                        }
                    }
                    catch { }
                }

                try
                {
                    var config = GameObjectCache.GetOrFind<Il2CppLast.UI.KeyInput.ConfigController>();
                    if (config == null || !config.gameObject.activeInHierarchy)
                        continue;

                    IntPtr detailsPtr = StateReaderHelper.ReadPointerField(config.Pointer, OFFSET_DETAILS_CONTROLLER);
                    if (detailsPtr == IntPtr.Zero)
                        continue;

                    if (AnnounceSelectedConfigCommand(new ConfigActualDetailsControllerBase_KeyInput(detailsPtr)) != null)
                    {
                        // A scene change may have reset the config state; the menu is open again.
                        MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
                        yield break;
                    }
                }
                catch { } // not ready yet — retry
            }
        }
    }

    /// <summary>
    /// Postfix on KeyInput SwitchArrowSelectTypeProcess (0x309430) - called when left/right arrows change
    /// toggle options: only from the input lambda of UpdateController (&lt;UpdateController&gt;b__0, 0x633F80),
    /// with the pressed Key. Only announces when the value actually changes. Registered manually in
    /// ConfigMenuPatches.ApplyPatches.
    /// </summary>
    internal static class ConfigActualDetails_SwitchArrowSelectType_Patch
    {
        private const string CONTEXT_ARROW = AnnouncementContexts.CONFIG_ARROW;

        /// <summary>SwitchArrowSelectTypeProcess(ConfigCommandController controller, Key key), positional.</summary>
        public static void Postfix(ConfigCommandController __0)
        {
            try
            {
                ConfigCommandController controller = __0;
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
    /// Slider value changes in the config menu (volumes, brightness), from Unity's own
    /// Slider.onValueChanged event. Replaces a hook on KeyInput SwitchSliderTypeProcess (0x30A740),
    /// which the game calls every frame from the tail of UpdateController (key null) while a slider row
    /// is focused; every value-writing method on that path runs every frame with it (SetSliderValue
    /// 0x6231B0 → Slider.set_value, ConfigClient.SetVolume / SetBrightness). Only the left/right path
    /// (the input lambda &lt;UpdateController&gt;b__0, 0x633F80) changes the value, and Slider.onValueChanged
    /// fires only when the value really changes, so it is the one change-only signal: at either end of
    /// the range the value does not move and nothing is spoken. A listener is added once per slider, when
    /// its row first gains focus (AnnounceSelectedConfigCommand). The read waits one frame, so the value
    /// (and the text SetSliderValue writes after it) has settled.
    /// </summary>
    internal static class ConfigSliderValueListener
    {
        // Every slider a listener was added to. Holding the wrappers keeps the objects alive, so a
        // pointer is never reused by a new slider that would then be skipped.
        private static readonly System.Collections.Generic.List<UnityEngine.UI.Slider> sliders =
            new System.Collections.Generic.List<UnityEngine.UI.Slider>();

        private static IntPtr lastSliderPtr;
        private static string lastValue;
        private static bool readPending;
        private static bool warned;

        /// <summary>Forgets the last spoken value (config menu closed).</summary>
        public static void Reset()
        {
            lastSliderPtr = IntPtr.Zero;
            lastValue = null;
        }

        /// <summary>Adds the value listener to the row's slider, once per slider.</summary>
        internal static void Attach(ConfigCommandController command)
        {
            try
            {
                var slider = command?.view?.Slider;
                if (slider == null) return;

                IntPtr ptr = slider.Pointer;
                if (ptr == IntPtr.Zero) return;
                for (int i = 0; i < sliders.Count; i++)
                    if (sliders[i].Pointer == ptr) return;

                System.Action<float> handler = _ => OnValueChanged(ptr);
                slider.onValueChanged.AddListener(handler);
                sliders.Add(slider);
            }
            catch (Exception ex)
            {
                if (!warned)
                {
                    warned = true;
                    MelonLogger.Warning($"[Config Menu] Could not listen to a config slider: {ex.Message}");
                }
            }
        }

        private static void OnValueChanged(IntPtr sliderPtr)
        {
            if (readPending) return; // several changes in one frame are read once
            readPending = true;
            CoroutineManager.StartManaged(ReadNextFrame(sliderPtr));
        }

        private static System.Collections.IEnumerator ReadNextFrame(IntPtr sliderPtr)
        {
            yield return null;
            readPending = false;

            try
            {
                if (!ConfigMenuState.IsActive) yield break;

                // Only the focused row's slider speaks (the one left/right just moved)
                var slider = ConfigActualDetails_SelectCommand_Patch.FocusedCommand?.view?.Slider;
                if (slider == null || slider.Pointer != sliderPtr) yield break;

                // Percentage over the slider's own min/max range
                string percentage = ConfigMenuReader.GetSliderPercentage(slider);
                if (string.IsNullOrEmpty(percentage)) yield break;
                if (sliderPtr == lastSliderPtr && percentage == lastValue) yield break;
                lastSliderPtr = sliderPtr;
                lastValue = percentage;

                MelonLogger.Msg($"[ConfigMenu] Slider value changed: {percentage}");
                FFIII_ScreenReaderMod.SpeakText(percentage, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Config Menu] Error reading slider value: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Touch mode arrow button handling (Touch SwitchArrowTypeProcess 0x8908D0, a UI button callback).
    /// Only announces when the value actually changes. Registered manually in ConfigMenuPatches.
    /// </summary>
    internal static class ConfigActualDetailsTouch_SwitchArrowType_Patch
    {
        private const string CONTEXT_TOUCH_ARROW = AnnouncementContexts.CONFIG_TOUCH_ARROW;

        /// <summary>SwitchArrowTypeProcess(ConfigCommandController controller, int value), positional.</summary>
        public static void Postfix(Il2CppLast.UI.Touch.ConfigCommandController __0)
        {
            try
            {
                var controller = __0;
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
    /// Touch mode slider handling (Touch SwitchSliderTypeProcess 0x890F20, the slider's OnSlideType
    /// callback). Only announces when the value actually changes for the SAME option. Registered manually
    /// in ConfigMenuPatches.
    /// </summary>
    internal static class ConfigActualDetailsTouch_SwitchSliderType_Patch
    {
        private const string CONTEXT_TOUCH_SLIDER = AnnouncementContexts.CONFIG_TOUCH_SLIDER;
        private const string CONTEXT_TOUCH_SLIDER_CONTROLLER = AnnouncementContexts.CONFIG_TOUCH_SLIDER_CONTROLLER;

        /// <summary>SwitchSliderTypeProcess(ConfigCommandController controller, float value), positional.</summary>
        public static void Postfix(Il2CppLast.UI.Touch.ConfigCommandController __0)
        {
            try
            {
                var controller = __0;
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

            // Config row navigation (once per cursor move) + title Options initial focus
            try
            {
                var selectCommand = AccessTools.Method(typeof(ConfigActualDetailsControllerBase_KeyInput), "SelectCommand");
                if (selectCommand != null)
                    harmony.Patch(selectCommand, postfix: new HarmonyMethod(
                        AccessTools.Method(typeof(ConfigActualDetails_SelectCommand_Patch), nameof(ConfigActualDetails_SelectCommand_Patch.Postfix))));
                else
                    MelonLogger.Warning("[Config Menu] ConfigActualDetailsControllerBase.SelectCommand not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching SelectCommand: {ex.Message}");
            }
            PatchOption(harmony, "ShowConfig", nameof(OptionController_InitialFocus_Postfix));
            PatchOption(harmony, "InitSelectLanguage", nameof(OptionController_InitialFocus_Postfix));

            // Value changes on arrow / slider rows (manual: attribute patches crash on IL2CPP).
            // KeyInput SwitchArrowSelectTypeProcess 0x309430; Touch SwitchArrowTypeProcess 0x8908D0,
            // SwitchSliderTypeProcess 0x890F20. All unique in dump.cs. KeyInput slider values come from
            // Slider.onValueChanged (ConfigSliderValueListener), not from the per-frame
            // SwitchSliderTypeProcess.
            PatchValueChange(harmony, typeof(Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase), "SwitchArrowSelectTypeProcess",
                null, typeof(ConfigActualDetails_SwitchArrowSelectType_Patch));
            PatchValueChange(harmony, typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchArrowTypeProcess",
                null, typeof(ConfigActualDetailsTouch_SwitchArrowType_Patch));
            PatchValueChange(harmony, typeof(Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase), "SwitchSliderTypeProcess",
                null, typeof(ConfigActualDetailsTouch_SwitchSliderType_Patch));

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

            // Gamepad/Keyboard "Controls" pop-up (read-only list of every control) → navigation buffer.
            // Entering the GamePad/Keyboard Help state shows HelpContentList/KeyboardHelpContentList;
            // we render it once and hand it to KeyHelpReader so arrows/WASD/D-pad step the entries.
            PatchKeysSetting(harmony, "GamePadHelpInit", nameof(GamePadHelpInit_Postfix));
            PatchKeysSetting(harmony, "KeyboardHelpInit", nameof(KeyboardHelpInit_Postfix));
            // Leaving the help state (back to the select list, or closing the controls screen) clears it.
            PatchKeysSetting(harmony, "GamePadSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting(harmony, "KeyboardSelectInit", nameof(ControlsHelpClose_Postfix));
            PatchKeysSetting(harmony, "Close", nameof(ControlsHelpClose_Postfix));

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

        /// <summary>
        /// Registers a value-change hook: the patch class's "Prefix" (when prefixType is given) and
        /// "Postfix". Each target name has a single overload in its class.
        /// </summary>
        private static void PatchValueChange(HarmonyLib.Harmony harmony, Type target, string method, Type prefixType, Type postfixType)
        {
            try
            {
                var m = AccessTools.Method(target, method);
                if (m == null)
                {
                    MelonLogger.Warning($"[Config Menu] {target.FullName}.{method} not found");
                    return;
                }
                harmony.Patch(m,
                    prefix: prefixType != null ? new HarmonyMethod(AccessTools.Method(prefixType, "Prefix")) : null,
                    postfix: new HarmonyMethod(AccessTools.Method(postfixType, "Postfix")));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Config Menu] Error patching {target.Name}.{method}: {ex.Message}");
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
        /// Postfix for ConfigController.SetActive - drives ConfigMenuState from the menu's lifecycle, so
        /// the SelectCommand announcer only speaks inside the real config menu.
        /// </summary>
        public static void SetActive_Postfix(bool isActive)
        {
            if (isActive)
            {
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.CONFIG_MENU);
            }
            else
            {
                ConfigMenuState.ResetState();
                ConfigSliderValueListener.Reset();
                ConfigActualDetails_SelectCommand_Patch.CancelReannounceAfterLibrary();
            }
        }

        /// <summary>
        /// Postfix for OptionController.ShowConfig / InitSelectLanguage (title-screen Options): reads the
        /// initially focused row, which the title screen doesn't announce through SelectCommand.
        /// </summary>
        public static void OptionController_InitialFocus_Postfix(Il2CppLast.UI.KeyInput.OptionController __instance)
        {
            ConfigActualDetails_SelectCommand_Patch.AnnounceInitialFocusDelayed(__instance);
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
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command,
            bool isHelpList = false)
        {
            if (command == null) return null;

            var textParts = new System.Collections.Generic.List<string>();

            if (isHelpList)
            {
                // Help rows (the read-only Controls pop-up) leave view.nameTexts as a placeholder and
                // render the real name into the controller's own messageTexts; fall back to MessageId.
                AppendHelpCommandName(textParts, command);
            }
            else if (command.view != null && command.view.nameTexts != null && command.view.nameTexts.Count > 0)
            {
                // Action name from the view's nameTexts
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
            if (!IconHasContent(command.keyboardIconController))
            {
                string btn = ResolveGamepadButtonText(owner, command);
                // Help rows also list the fixed (non-remappable) buttons, which the live remap read
                // can't resolve — map their rendered glyph sprite instead.
                if (string.IsNullOrEmpty(btn) && isHelpList)
                    btn = GetGamepadGlyphLabel(command);
                if (!string.IsNullOrEmpty(btn))
                    textParts.Add($"({btn})");
            }

            return textParts.Count == 0 ? null : string.Join(" ", textParts);
        }

        /// <summary>Appends a help row's action name from messageTexts, falling back to its MessageId.</summary>
        private static void AppendHelpCommandName(System.Collections.Generic.List<string> textParts,
            Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            var msgTexts = command.messageTexts;
            if (msgTexts != null)
            {
                for (int i = 0; i < msgTexts.Count; i++)
                {
                    var t = msgTexts[i];
                    if (t != null && IsRealName(t.text))
                    {
                        string s = t.text.Trim();
                        if (!textParts.Contains(s)) textParts.Add(s);
                    }
                }
            }

            // Fallback: the rendered text wasn't ready — resolve the message id directly.
            if (textParts.Count == 0)
            {
                string loc = LocalizationHelper.GetText(command.MessageId);
                if (IsRealName(loc)) textParts.Add(loc.Trim());
            }
        }

        /// <summary>True if the text is a usable name (not blank or an editor placeholder).</summary>
        private static bool IsRealName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            return t != "New Text" && t != "NewText" && t != "Text" && t != "Name" && t != "Label";
        }

        /// <summary>
        /// Reads a help row's rendered gamepad glyph sprite (under view.gamePadIconsRoot) and maps it to a
        /// controller-aware label. Used only for the fixed buttons the live remap read can't resolve.
        /// </summary>
        private static string GetGamepadGlyphLabel(Il2CppLast.UI.KeyInput.ConfigControllCommandController command)
        {
            try
            {
                var gpRoot = command?.view != null ? command.view.gamePadIconsRoot : null;
                if (gpRoot == null || !gpRoot.activeSelf) return null;
                var images = gpRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
                if (images == null) return null;
                for (int i = 0; i < images.Length; i++)
                {
                    var img = images[i];
                    if (img == null || !img.gameObject.activeInHierarchy) continue;
                    var sp = img.sprite;
                    if (sp == null) continue;
                    string label = GamepadGlyphSpriteToLabel(sp.name);
                    if (!string.IsNullOrEmpty(label)) return label;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Maps a controls-screen glyph sprite name ("UI_Common_&lt;Button&gt;button01") to a controller-aware
        /// label. Covers ONLY the fixed buttons; the remappable face buttons and unknowns return null.
        /// </summary>
        private static string GamepadGlyphSpriteToLabel(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName)) return null;

            if (Has(spriteName, "LBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
            if (Has(spriteName, "RBbutton")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);
            if (Has(spriteName, "LTbutton")) return ControllerLabels.GetLeftTriggerLabel();
            if (Has(spriteName, "RTbutton")) return ControllerLabels.GetRightTriggerLabel();
            if (Has(spriteName, "L3button")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_LEFT_STICK);
            if (Has(spriteName, "R3button")) return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_RIGHT_STICK);
            // Start opens the mod menu, so the game never sees it.
            if (Has(spriteName, "Menubutton")) return T("used for mod menu");
            if (Has(spriteName, "Backbutton") || Has(spriteName, "Selectbutton") || Has(spriteName, "Viewbutton"))
                return ControllerLabels.GetButtonLabel(SDL3.SDL_GAMEPAD_BUTTON_BACK);
            // The D-pad and right stick drive mod navigation on the field, so only the left stick moves.
            if (Has(spriteName, "Tenkeybutton") || Has(spriteName, "Dpadbutton")
                || Has(spriteName, "Crossbutton") || Has(spriteName, "Directionbutton"))
                return T("Left Stick");

            return null;
        }

        private static bool Has(string s, string token)
            => s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

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
                FFIII_ScreenReaderMod.SpeakText(gamepad ? T("Press a button.") : T("Press a key."), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in assign-prompt patch: {ex.Message}");
            }
        }

        // ── Gamepad/Keyboard Controls pop-up (read-only controls list) → navigation buffer ──
        // The pop-up is ConfigKeysSettingController entering its GamePad/Keyboard Help state (NOT the
        // always-present KeyHelpController hint bar). On state entry we render the help list once and
        // hand the strings to KeyHelpReader, which drives KeyContext.KeyHelp.

        public static void GamePadHelpInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: true);

        public static void KeyboardHelpInit_Postfix(Il2CppLast.UI.KeyInput.ConfigKeysSettingController __instance)
            => OpenControlsHelp(__instance, gamepad: false);

        /// <summary>Returning to the controls list (Select state) or closing the screen tears the buffer down.</summary>
        public static void ControlsHelpClose_Postfix() => KeyHelpReader.CloseControlsHelp();

        private static void OpenControlsHelp(Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            try
            {
                if (inst == null) return;
                // One-frame delay so each row's binding text is populated before we render it.
                CoroutineManager.StartManaged(DelayedOpenControlsHelp(inst, gamepad));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error opening controls help: {ex.Message}");
            }
        }

        private static System.Collections.IEnumerator DelayedOpenControlsHelp(
            Il2CppLast.UI.KeyInput.ConfigKeysSettingController inst, bool gamepad)
        {
            yield return null;
            System.Collections.Generic.List<string> entries = null;
            try
            {
                // The state machine can cycle its Help-state Init during scene construction; only
                // build/announce while the controls screen is genuinely shown.
                if (inst == null || inst.gameObject == null || !inst.gameObject.activeInHierarchy)
                {
                    KeyHelpReader.CloseControlsHelp();
                    yield break;
                }

                var list = gamepad ? inst.HelpContentList : inst.KeyboardHelpContentList;
                entries = new System.Collections.Generic.List<string>();
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        string ann = BuildCommandAnnouncement(inst, list[i], isHelpList: true);
                        if (!string.IsNullOrWhiteSpace(ann)) entries.Add(ann);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading controls help list: {ex.Message}");
            }
            if (entries != null && entries.Count > 0)
                KeyHelpReader.OpenControlsHelp(inst, entries);
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
