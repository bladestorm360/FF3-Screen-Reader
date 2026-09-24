using System;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Utils;

using BattleController = Il2CppLast.Battle.BattleController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Battle lifecycle state: true from the encounter start until the battle ends, including enemy
    /// turns and animations (unlike the BATTLE_* menu flags, which are only set while a battle menu is
    /// open). Drives the Battle key context, so field/mod-mode actions never fire mid-battle.
    /// </summary>
    internal static class BattleStateHelper
    {
        public static bool IsInBattle { get; private set; } = false;

        /// <summary>
        /// Marks the start of a battle and clears per-battle speech dedup so the first action, status
        /// and system message of this battle are never swallowed by the previous battle's state.
        /// Idempotent: StartPreeMptiveMes and the Battle game state both call it.
        /// </summary>
        public static void OnBattleStart()
        {
            if (IsInBattle) return;
            IsInBattle = true;

            GlobalBattleMessageTracker.Reset();
            AnnouncementDeduplicator.Reset(AnnouncementContexts.BATTLE_ACTION);
            BattleConditionController_Add_Patch.ResetState();
            BattleCommandSelectController_SetCommandData_Patch.ResetState();
            BattleResultPatches.ResetState();
        }

        /// <summary>
        /// Clears battle state on battle end, but only while a battle is actually tracked
        /// (the end hooks can also fire during game initialization).
        /// </summary>
        public static void TryClearOnBattleEnd()
        {
            if (!IsInBattle) return;
            IsInBattle = false;

            BattleResultPatches.ClearAllBattleMenuFlags();
            BattleCommandState.CurrentActor = null;
            BattleCommandSelectController_SetCursor_Patch.ResetState();
        }
    }

    /// <summary>
    /// BattleController end hooks: the win/lose/escape fade-out callbacks and Exit(bool), which runs on
    /// every battle exit path. OnDestroy is deliberately not patched (crashes during asset loading).
    /// </summary>
    internal static class BattleControllerPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            var controllerType = typeof(BattleController);
            var endPostfix = new HarmonyMethod(AccessTools.Method(typeof(BattleControllerPatches), nameof(BattleEnd_Postfix)));

            foreach (var name in new[] { "EndWinFadeOutCallback", "EndLoseFadeOutCallback", "EndEscapeFadeOut", "EndFadeOutCallback" })
            {
                try
                {
                    var method = AccessTools.Method(controllerType, name);
                    if (method != null)
                        harmony.Patch(method, postfix: endPostfix);
                    else
                        MelonLogger.Warning($"[Battle State] BattleController.{name} not found");
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Battle State] Error patching {name}: {ex.Message}");
                }
            }

            try
            {
                var exitMethod = AccessTools.Method(controllerType, "Exit", new Type[] { typeof(bool) });
                if (exitMethod != null)
                    harmony.Patch(exitMethod, prefix: new HarmonyMethod(AccessTools.Method(typeof(BattleControllerPatches), nameof(Exit_Prefix))));
                else
                    MelonLogger.Warning("[Battle State] BattleController.Exit(bool) not found");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle State] Error patching Exit: {ex.Message}");
            }
        }

        public static void BattleEnd_Postfix()
        {
            try { BattleStateHelper.TryClearOnBattleEnd(); }
            catch (Exception ex) { MelonLogger.Warning($"[Battle State] Error in battle-end postfix: {ex.Message}"); }
        }

        public static void Exit_Prefix()
        {
            try { BattleStateHelper.TryClearOnBattleEnd(); }
            catch (Exception ex) { MelonLogger.Warning($"[Battle State] Error in Exit prefix: {ex.Message}"); }
        }
    }
}
