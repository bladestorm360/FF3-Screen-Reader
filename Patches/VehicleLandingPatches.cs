using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Map;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Core;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Announces when player enters a zone where vehicle can land.
    /// Patches MapUIManager.SwitchLandable which is called by the game
    /// when the landing state changes based on terrain under the vehicle.
    /// Uses manual Harmony patching (FF3 requirement - attribute patches crash).
    /// Ported from FF5 screen reader.
    /// </summary>
    public static class VehicleLandingPatches
    {
        private static bool isPatched = false;
        private static bool lastLandableState = false;

        /// <summary>
        /// Apply manual Harmony patches for landing zone detection.
        /// Called from FFIII_ScreenReaderMod initialization.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            if (isPatched)
                return;

            try
            {
                TryPatchSwitchLandable(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SwitchLandable - called when terrain under vehicle changes landing validity.
        /// </summary>
        private static void TryPatchSwitchLandable(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mapUIManagerType = typeof(MapUIManager);

                // Find SwitchLandable(bool)
                var methods = mapUIManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                MethodInfo targetMethod = null;

                foreach (var method in methods)
                {
                    if (method.Name == "SwitchLandable")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            MelonLogger.Msg($"[Landing] Found SwitchLandable(bool)");
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(VehicleLandingPatches).GetMethod(nameof(SwitchLandable_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Landing] Patched SwitchLandable successfully");
                }
                else
                {
                    MelonLogger.Warning("[Landing] Could not find SwitchLandable method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error patching SwitchLandable: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SwitchLandable - announces when entering landable zone.
        /// </summary>
        public static void SwitchLandable_Postfix(bool landable)
        {
            try
            {
                // Only announce when in a vehicle (not on foot)
                if (MoveStateHelper.IsOnFoot())
                    return;

                // Only announce when entering landable zone (false -> true)
                if (landable && !lastLandableState)
                {
                    FFIII_ScreenReaderMod.SpeakText("Can land", interrupt: false);
                }

                lastLandableState = landable;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Landing] Error in SwitchLandable patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Reset state when leaving vehicle or changing maps.
        /// </summary>
        public static void ResetState()
        {
            lastLandableState = false;
        }
    }
}
