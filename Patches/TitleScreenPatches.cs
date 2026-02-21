using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// Splash/Title screen
using SplashController = Il2CppLast.UI.SplashController;
using KeyInputTitleMenuCommandController = Il2CppLast.UI.KeyInput.TitleMenuCommandController;
using TouchTitleMenuCommandController = Il2CppLast.UI.Touch.TitleMenuCommandController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for title screen "Press any button" using combination approach:
    /// 1. SplashController.InitializeTitle - stores text silently (fires early during loading)
    /// 2. SystemIndicator.Show - tracks when title loading starts
    /// 3. SystemIndicator.Hide - speaks stored text when loading completes (indicator hidden)
    /// 4. TitleMenuCommandController.SetEnableMainMenu - clears state on title menu activation
    /// </summary>
    internal static class TitleScreenPatches
    {
        /// <summary>
        /// Stores the "Press any button" text captured during InitializeTitle.
        /// Spoken when SystemIndicator.Hide() is called (loading indicator hidden).
        ///
        /// KNOWN ISSUE: Speech occurs ~1 second before user input is actually available.
        /// No hookable method exists that fires exactly when input becomes available.
        /// </summary>
        private static string pendingTitleText = null;

        /// <summary>
        /// Guard flag: only true when we've captured title screen text and are waiting to speak it.
        /// This ensures speech only triggers for title screen, not other loading sequences.
        /// Set true ONLY by InitializeTitle, cleared when speech occurs.
        /// </summary>
        private static bool isTitleScreenTextPending = false;

        /// <summary>
        /// Apply title screen patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Step 1: Patch SplashController.InitializeTitle to capture and store the text
                Type splashControllerType = typeof(SplashController);
                var initTitleMethod = AccessTools.Method(splashControllerType, "InitializeTitle");

                if (initTitleMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SplashController_InitializeTitle_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initTitleMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] SplashController.InitializeTitle method not found");
                }

                // Step 2 & 3: Patch SystemIndicator.Show and Hide
                // SystemIndicator is internal in Last.Systems.Indicator namespace, need runtime lookup
                Type systemIndicatorType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        systemIndicatorType = asm.GetType("Il2CppLast.Systems.Indicator.SystemIndicator");
                        if (systemIndicatorType != null)
                        {
                            break;
                        }
                    }
                    catch { }
                }

                if (systemIndicatorType == null)
                {
                    MelonLogger.Warning("[TitleScreen] SystemIndicator type not found");
                    return;
                }

                // Patch Show(Mode) to track when title loading starts
                var showMethod = AccessTools.Method(systemIndicatorType, "Show");
                if (showMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Show_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] SystemIndicator.Show method not found");
                }

                // Patch Hide() to speak when loading completes
                var hideMethod = AccessTools.Method(systemIndicatorType, "Hide");
                if (hideMethod != null)
                {
                    var postfix = typeof(TitleScreenPatches).GetMethod(nameof(SystemIndicator_Hide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(hideMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[TitleScreen] SystemIndicator.Hide method not found");
                }

                // Step 4: Patch TitleMenuCommandController.SetEnableMainMenu
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
        /// Postfix for SplashController.InitializeTitle.
        /// </summary>
        public static void SplashController_InitializeTitle_Postfix(SplashController __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // Try to read the localized "Press any button" text from UiMessageConstants
                string pressText = null;

                try
                {
                    var uiMsgType = Type.GetType("Il2CppUiMessageConstants, Assembly-CSharp")
                                 ?? Type.GetType("UiMessageConstants, Assembly-CSharp");

                    if (uiMsgType != null)
                    {
                        var field = uiMsgType.GetField("MENU_TITLE_PRESS_TEXT", BindingFlags.Public | BindingFlags.Static);
                        if (field != null)
                        {
                            pressText = field.GetValue(null) as string;
                        }
                    }
                }
                catch { }

                if (!string.IsNullOrWhiteSpace(pressText))
                {
                    pendingTitleText = TextUtils.StripIconMarkup(pressText.Trim());
                }
                else
                {
                    pendingTitleText = "Press any button";
                }

                isTitleScreenTextPending = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error in SplashController.InitializeTitle postfix: {ex.Message}");
                pendingTitleText = "Press any button";
                isTitleScreenTextPending = true;
            }
        }

        /// <summary>
        /// Postfix for SystemIndicator.Show(Mode).
        /// </summary>
        public static void SystemIndicator_Show_Postfix(int mode)
        {
            try
            {
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error in SystemIndicator.Show postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SystemIndicator.Hide().
        /// </summary>
        public static void SystemIndicator_Hide_Postfix()
        {
            try
            {
                if (isTitleScreenTextPending && !string.IsNullOrWhiteSpace(pendingTitleText))
                {
                    FFIII_ScreenReaderMod.SpeakText(pendingTitleText, interrupt: false);

                    pendingTitleText = null;
                    isTitleScreenTextPending = false;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TitleScreen] Error in SystemIndicator.Hide postfix: {ex.Message}");
            }
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
