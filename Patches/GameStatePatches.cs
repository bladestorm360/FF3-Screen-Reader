using System;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Field;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for game state transitions (field, battle, menu, etc.).
    /// Hooks SubSceneManagerMainGame.ChangeState for event-driven map transition
    /// and battle state management instead of per-frame polling.
    /// </summary>
    internal static class GameStatePatches
    {

        private static int lastAnnouncedMapId = -1;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch SubSceneManagerMainGame.ChangeState(State state)
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerMainGame),
                    "ChangeState",
                    new Type[] { typeof(SubSceneManagerMainGame.State) }
                );

                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GameStatePatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[GameState] Patches applied");
                }
                else
                {
                    MelonLogger.Warning("[GameState] Could not find SubSceneManagerMainGame.ChangeState method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements and battle state clearing.
        /// </summary>
        public static void ChangeState_Postfix(SubSceneManagerMainGame.State state)
        {
            try
            {
                int stateValue = (int)state;

                // When transitioning to field states, check for map changes and clear battle state
                if (stateValue == IL2CppOffsets.GameState.STATE_FIELD_READY || stateValue == IL2CppOffsets.GameState.STATE_PLAYER || stateValue == IL2CppOffsets.GameState.STATE_CHANGE_MAP)
                {
                    // Clear battle state when returning to field
                    ClearAllBattleState();

                    // Check for map transition
                    CheckMapTransition();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in ChangeState_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks for map transitions and triggers entity rescan when map changes.
        /// Announces new map name and clears stale entity cache.
        /// </summary>
        private static void CheckMapTransition()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return;

                int currentMapId = userDataManager.CurrentMapId;

                if (currentMapId != lastAnnouncedMapId && lastAnnouncedMapId != -1)
                {
                    // Map has changed - announce new map
                    string mapName = MapNameResolver.GetCurrentMapName();
                    string fullMessage = $"Entering {mapName}";

                    FFIII_ScreenReaderMod.SpeakText(fullMessage, interrupt: false);
                    lastAnnouncedMapId = currentMapId;

                    // Record for deduplication with FadeMessage
                    LocationMessageTracker.SetLastMapTransition(fullMessage);

                    // Check if entering interior map - if so, switch to on-foot state
                    bool isWorldMap = FFIII_ScreenReaderMod.Instance?.IsCurrentMapWorldMap() ?? false;
                    MoveStateHelper.OnMapTransition(isWorldMap);

                    // Clear vehicle type map so it gets repopulated with new map's vehicles
                    FieldNavigationHelper.ResetTransportationDebug();

                    // Force entity rescan to clear stale entities from previous map
                    FFIII_ScreenReaderMod.Instance?.ForceEntityRescan();
                }
                else if (lastAnnouncedMapId == -1)
                {
                    // First run - store current map without announcing
                    lastAnnouncedMapId = currentMapId;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error in CheckMapTransition: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all battle-related state when transitioning out of battle.
        /// </summary>
        private static void ClearAllBattleState()
        {
            BattleCommandState.IsActive = false;
            BattleTargetPatches.SetTargetSelectionActive(false);
            BattleItemMenuState.IsActive = false;
            BattleMagicMenuState.IsActive = false;
        }

        /// <summary>
        /// Reset the map tracking state (called on game reset/title screen).
        /// </summary>
        public static void ResetMapTracking()
        {
            lastAnnouncedMapId = -1;
        }
    }
}
