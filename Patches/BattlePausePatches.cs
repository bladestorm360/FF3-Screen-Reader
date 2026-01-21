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

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks battle pause menu state by reading game memory directly.
    /// When active, bypasses BattleCommandState suppression so MenuTextDiscovery can read.
    /// </summary>
    public static class BattlePauseState
    {
        // Memory offsets
        private const int OFFSET_PAUSE_CONTROLLER = 0x90;  // BattleUIManager.pauseController
        private const int OFFSET_IS_ACTIVE_PAUSE_MENU = 0x71;  // BattlePauseController.isActivePauseMenu

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
                    IntPtr pauseControllerPtr = Marshal.ReadIntPtr(uiManagerPtr + OFFSET_PAUSE_CONTROLLER);
                    if (pauseControllerPtr == IntPtr.Zero) return false;

                    // Read isActivePauseMenu bool at offset 0x71
                    byte isActive = Marshal.ReadByte(pauseControllerPtr + OFFSET_IS_ACTIVE_PAUSE_MENU);
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
    /// </summary>
    public static class BattlePausePatches
    {
        /// <summary>
        /// Apply battle pause menu patches.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("[Battle Pause] Applying battle pause menu patches...");

                var controllerType = typeof(BattlePauseController);

                // Patch OpenBackToTitlePopup - called when selecting "Return to Title"
                var openBackToTitleMethod = AccessTools.Method(controllerType, "OpenBackToTitlePopup");
                if (openBackToTitleMethod != null)
                {
                    var postfix = typeof(BattlePausePatches).GetMethod("OpenBackToTitlePopup_Postfix", BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(openBackToTitleMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Battle Pause] Patched OpenBackToTitlePopup successfully");
                }
                else
                {
                    MelonLogger.Warning("[Battle Pause] OpenBackToTitlePopup method not found");
                }

                MelonLogger.Msg("[Battle Pause] Using direct memory read for pause state detection");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for OpenBackToTitlePopup - clears battle state when returning to title.
        /// </summary>
        public static void OpenBackToTitlePopup_Postfix()
        {
            try
            {
                MelonLogger.Msg("[Battle Pause] Return to Title popup opened - clearing battle state");
                BattleResultPatches.ClearAllBattleMenuFlags();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Pause] Error in OpenBackToTitlePopup_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state (called when battle ends).
        /// </summary>
        public static void Reset()
        {
            BattlePauseState.Reset();
        }
    }
}
