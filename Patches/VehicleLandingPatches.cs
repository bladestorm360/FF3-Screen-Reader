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
    /// Patches MapUIManager.ShowLandingGuide which is called by the game
    /// when the landing UI should be shown/hidden based on terrain under the vehicle.
    /// Uses manual Harmony patching (FF3 requirement - attribute patches crash).
    /// </summary>
    internal static class VehicleLandingPatches
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
                // Try ShowLandingGuide first (more likely to be called for UI updates)
                TryPatchShowLandingGuide(harmony);
                // Also patch SwitchLandable as backup
                TryPatchSwitchLandable(harmony);
                isPatched = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error applying patches: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch ShowLandingGuide - called when landing UI should be shown/hidden.
        /// Signature: public void ShowLandingGuide(bool landable)
        /// </summary>
        private static void TryPatchShowLandingGuide(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mapUIManagerType = typeof(MapUIManager);
                MethodInfo targetMethod = null;

                foreach (var method in mapUIManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "ShowLandingGuide")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
                            targetMethod = method;
                            break;
                        }
                    }
                }

                if (targetMethod != null)
                {
                    var postfix = typeof(VehicleLandingPatches).GetMethod(nameof(ShowLandingGuide_Postfix),
                        BindingFlags.Public | BindingFlags.Static);

                    harmony.Patch(targetMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("[Landing] Patches applied");
                }
                else
                {
                    MelonLogger.Warning("[Landing] Could not find ShowLandingGuide method");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Landing] Error patching ShowLandingGuide: {ex.Message}");
            }
        }

        /// <summary>
        /// Patch SwitchLandable - backup hook for landing state changes.
        /// Signature: public void SwitchLandable(bool landable)
        /// </summary>
        private static void TryPatchSwitchLandable(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type mapUIManagerType = typeof(MapUIManager);
                MethodInfo targetMethod = null;

                foreach (var method in mapUIManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "SwitchLandable")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool))
                        {
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
        /// Postfix for ShowLandingGuide - announces when entering landable zone.
        /// </summary>
        public static void ShowLandingGuide_Postfix(bool landable)
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
                MelonLogger.Error($"[Landing] Error in ShowLandingGuide patch: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for SwitchLandable - backup hook for landing zone detection.
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
