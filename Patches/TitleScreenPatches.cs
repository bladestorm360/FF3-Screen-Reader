using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

// Title screen
using TitleWindowController = Il2CppLast.UI.KeyInput.TitleWindowController;
using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Title screen "Press any button" prompt and title-menu state clearing. Event hooks only, no wait:
    /// 1. KeyInput TitleWindowController.Initialize(GameObject) postfix keeps the title window. Its only
    ///    caller is SceneTitleScreen.CreateTitleWindow (from CreateInstance), which builds the title
    ///    screen on boot AND on every return to title.
    /// 2. SystemIndicator.Hide postfix. The prompt is shown in one place only: the None state's
    ///    TitleWindowController.UpdateNone calls SystemIndicator.Hide (0x8CD798) and, straight after it
    ///    returns, activates view.startParent (0x8CD7AF); input is accepted from that frame. So one frame
    ///    after a Hide, if the kept window is in the None state and its startText went from hidden to
    ///    shown, the prompt has just appeared: speak it. Hide's other callers (CreateInstance before the
    ///    fade-in, field map loads, the config font switch) leave the prompt hidden or have no title
    ///    window, and stay silent. InitShortcutCommand (return from the Extras) shows startParent
    ///    without a Hide and outside the None state, and is correctly ignored.
    /// 3. TitleMenuCommandController.SetEnableMainMenu clears state when the title menu activates.
    /// </summary>
    internal static class TitleScreenPatches
    {
        // KeyInput TitleWindowController.stateMachine / .view, TitleWindowView.startText
        private const int OFFSET_STATE_MACHINE = 0x18;
        private const int OFFSET_TITLE_VIEW = 0x48;
        private const int OFFSET_START_TEXT = 0x30;
        private const int STATE_NONE = 0; // TitleWindowController.State.None: the press-any-button state

        // The live title window (null off the title; a destroyed instance compares equal to null)
        private static TitleWindowController titleWindow;

        // Only the check scheduled by the latest Hide runs
        private static int promptCheckGen;

        /// <summary>
        /// Apply title screen patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                var initMethod = AccessTools.Method(typeof(TitleWindowController), "Initialize", new[] { typeof(GameObject) });
                if (initMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(TitleWindowController_Initialize_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] KeyInput.TitleWindowController.Initialize not found");
                }

                // SystemIndicator is internal in the game assembly: look it up at runtime
                Type indicatorType = AccessTools.TypeByName("Il2CppLast.Systems.Indicator.SystemIndicator");
                var hideMethod = indicatorType != null ? AccessTools.Method(indicatorType, "Hide") : null;
                if (hideMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(hideMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] SystemIndicator.Hide not found");
                }

                TryPatchTitleMenuCommand(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch TitleMenuCommandController.SetEnableMainMenu(bool) in both KeyInput and Touch namespaces.
        /// </summary>
        private static void TryPatchTitleMenuCommand(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch KeyInput version
                Type keyInputType = typeof(KeyInputTitleMenuCommandController);
                var keyInputMethod = AccessTools.Method(keyInputType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (keyInputMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(keyInputMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] KeyInput.TitleMenuCommandController.SetEnableMainMenu not found");
                }

                // Patch Touch version
                Type touchType = typeof(TouchTitleMenuCommandController);
                var touchMethod = AccessTools.Method(touchType, "SetEnableMainMenu", new[] { typeof(bool) });
                if (touchMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(TitleMenuCommand_SetEnableMainMenu_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(touchMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] Touch.TitleMenuCommandController.SetEnableMainMenu not found");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error patching TitleMenuCommandController: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for KeyInput TitleWindowController.Initialize(GameObject): the title screen was built
        /// (boot or return to title). Keeps the instance so the Hide postfix needs no scene search.
        /// </summary>
        public static void TitleWindowController_Initialize_Postfix(TitleWindowController __instance)
        {
            titleWindow = __instance;
        }

        /// <summary>
        /// Postfix for SystemIndicator.Hide. Runs before UpdateNone activates the prompt, so it records
        /// whether the prompt is visible now and checks again one frame later.
        /// </summary>
        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                var window = titleWindow;
                if (window == null) return; // not on the title screen
                bool visibleBefore = TryGetVisiblePrompt(window, out _);
                CoroutineManager.StartManaged(CheckPromptNextFrame(window, visibleBefore, ++promptCheckGen));
            }
            catch
            {
                titleWindow = null; // stale instance
            }
        }

        private static IEnumerator CheckPromptNextFrame(TitleWindowController window, bool visibleBefore, int gen)
        {
            yield return null; // one frame: UpdateNone activates startParent right after Hide returns
            if (gen != promptCheckGen) yield break;
            SpeakPromptIfJustShown(window, visibleBefore);
        }

        /// <summary>
        /// Speaks the prompt if it was hidden at the Hide call, is on screen now, and the title is still in
        /// the press-any-button state (a key pressed in the same frame has already moved it to Select).
        /// </summary>
        private static void SpeakPromptIfJustShown(TitleWindowController window, bool visibleBefore)
        {
            try
            {
                if (visibleBefore || window == null) return;
                if (StateReaderHelper.ReadStateTag(window.Pointer, OFFSET_STATE_MACHINE) != STATE_NONE) return;
                if (!TryGetVisiblePrompt(window, out string prompt)) return;

                MelonLogger.Msg("[TitleScreen] Press-any-button prompt shown");
                FFIII_ScreenReaderMod.SpeakText(prompt, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error reading the press prompt: {ex.Message}");
            }
        }

        /// <summary>
        /// True when the window's prompt Text (TitleWindowView.startText) is on screen; text is its
        /// displayed string, or the mod's "Press any button" if the label is empty.
        /// </summary>
        private static bool TryGetVisiblePrompt(TitleWindowController window, out string text)
        {
            text = null;
            IntPtr viewPtr = StateReaderHelper.ReadPointerField(window.Pointer, OFFSET_TITLE_VIEW);
            if (viewPtr == IntPtr.Zero) return false;
            IntPtr textPtr = StateReaderHelper.ReadPointerField(viewPtr, OFFSET_START_TEXT);
            if (textPtr == IntPtr.Zero) return false;

            var startText = new UnityEngine.UI.Text(textPtr);
            if (startText.gameObject == null || !startText.gameObject.activeInHierarchy) return false;

            string raw = startText.text;
            text = !string.IsNullOrWhiteSpace(raw) ? TextUtils.StripIconMarkup(raw.Trim()) : T("Press any button");
            return true;
        }

        /// <summary>
        /// Postfix for TitleMenuCommandController.SetEnableMainMenu(bool).
        /// </summary>
        public static void TitleMenuCommand_SetEnableMainMenu_Postfix(bool isEnable)
        {
            try
            {
                if (isEnable)
                {
                    BattleStateHelper.TryClearOnBattleEnd();
                    MenuStateRegistry.ResetAll();
                    BattleResultPatches.ClearAllBattleMenuFlags();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error in SetEnableMainMenu postfix: {ex.Message}");
            }
        }
    }
}
