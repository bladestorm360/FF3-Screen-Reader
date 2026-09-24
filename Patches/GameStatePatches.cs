using System;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Field;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
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

                PatchMenuResumeAfterLibrary(harmony);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GameState] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Returning from the config-menu bestiary: MenuExtraLibraryUi.GotoMenu passes
        /// FieldMap.CreateReturnMonsterLibraryArguemtns (its only caller), so FieldMap.InitMenu (0x34D800,
        /// the Menu-state entry) takes its library-return branch: FadeManager.FadeIn, then the fade-in
        /// completion callback FieldMap.&lt;InitMenu&gt;b__90_0 (0x352170, unique; the only &lt;InitMenu&gt;
        /// lambda, stored in the FadeManager callback field 0x58 in that branch only), which switches
        /// FieldMap's menu state machine (0xD0) to state 1. So its postfix marks exactly "the config menu is
        /// back on screen after the bestiary".
        /// </summary>
        private static void PatchMenuResumeAfterLibrary(HarmonyLib.Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Il2Cpp.FieldMap), "_InitMenu_b__90_0");
                if (target == null)
                {
                    // Il2CppInterop's name for a compiler-generated lambda: find it by its parts
                    foreach (var m in typeof(Il2Cpp.FieldMap).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        if (m.Name.Contains("InitMenu") && m.Name.Contains("b__") && m.ReturnType == typeof(void)
                            && m.GetParameters().Length == 0)
                        {
                            target = m;
                            break;
                        }
                    }
                }
                if (target == null)
                {
                    MelonLogger.Warning("[GameState] FieldMap InitMenu fade-in callback not found: no config re-read after the bestiary");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(GameStatePatches), nameof(MenuResumedAfterLibrary_Postfix))));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[GameState] Error patching the library-return callback: {ex.Message}");
            }
        }

        public static void MenuResumedAfterLibrary_Postfix()
        {
            try { ConfigActualDetails_SelectCommand_Patch.OnMenuResumedAfterLibrary(); }
            catch (Exception ex) { MelonLogger.Warning($"[GameState] Error in library-return postfix: {ex.Message}"); }
        }

        /// <summary>
        /// Called when game state changes (field, battle, menu, etc.).
        /// Handles map transition announcements, battle state clearing,
        /// and config menu bestiary dispatch (states 17/18).
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
                    BattleStateHelper.TryClearOnBattleEnd();
                    ClearAllBattleState();

                    // If we were in config bestiary, handle exit
                    if (BestiaryPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                    {
                        BestiaryPatches.ConfigBestiaryStateHandler.HandleExit();
                        // Returning from the bestiary lands back on the config menu, which resumes
                        // without re-firing SelectCommand: arm the re-read; the library-return fade-in
                        // callback (below) does it, so an exit to the field stays silent.
                        ConfigActualDetails_SelectCommand_Patch.ArmReannounceAfterLibrary();
                    }

                    // Check for map transition
                    CheckMapTransition();
                }
                // Battle lifecycle start (StartPreeMptiveMes marks it too; both are idempotent)
                else if (stateValue == IL2CppOffsets.GameState.STATE_BATTLE)
                {
                    BattleStateHelper.OnBattleStart();
                }
                // Config menu bestiary states
                else if (stateValue == 17 || stateValue == 18)
                {
                    BestiaryPatches.ConfigBestiaryStateHandler.HandleStateChange(stateValue);
                }
                // Exiting config bestiary to another non-field state
                else if (BestiaryPatches.ConfigBestiaryStateHandler.WasInConfigBestiary)
                {
                    BestiaryPatches.ConfigBestiaryStateHandler.HandleExit();
                    ConfigActualDetails_SelectCommand_Patch.ArmReannounceAfterLibrary();
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
                    // Map actually changed, so the transition is over (no menu can be open during
                    // one): clear any menu flag stuck by a missed close, which would otherwise
                    // silently block field context on the new map.
                    FFIII_ScreenReaderMod.ClearMenuFlagsForMapTransition();

                    // Map has changed - announce new map
                    string mapName = MapNameResolver.GetCurrentMapName();
                    string fullMessage = string.Format(T("Entering {0}"), mapName);

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
