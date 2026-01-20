using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for applying common Harmony patches.
    /// Consolidates the repeated SetActive/SetNextState patching boilerplate across multiple files.
    /// </summary>
    public static class HarmonyPatchHelper
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// Patches SetActive(bool) method with a postfix.
        /// Used to detect menu open/close events.
        /// </summary>
        /// <param name="harmony">Harmony instance</param>
        /// <param name="controllerType">The controller type to patch</param>
        /// <param name="patchType">The type containing the postfix method</param>
        /// <param name="postfixName">Name of the postfix method (default "SetActive_Postfix")</param>
        /// <param name="logPrefix">Prefix for log messages (e.g., "[Item Menu]")</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchSetActive(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            string postfixName = "SetActive_Postfix", string logPrefix = null)
        {
            try
            {
                var method = controllerType.GetMethod("SetActive", PublicInstance, null, new[] { typeof(bool) }, null);
                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} SetActive method not found");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {postfixName} method not found");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                if (logPrefix != null)
                    MelonLogger.Msg($"{logPrefix} Patched SetActive for menu state tracking");
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch SetActive: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches SetNextState method with a postfix.
        /// Used to detect state transitions within menus.
        /// Note: SetNextState typically has a State enum parameter, so we find it by name and parameter count.
        /// </summary>
        /// <param name="harmony">Harmony instance</param>
        /// <param name="controllerType">The controller type to patch</param>
        /// <param name="patchType">The type containing the postfix method</param>
        /// <param name="postfixName">Name of the postfix method (default "SetNextState_Postfix")</param>
        /// <param name="logPrefix">Prefix for log messages</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchSetNextState(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            string postfixName = "SetNextState_Postfix", string logPrefix = null)
        {
            try
            {
                // Find SetNextState by iterating methods - it has a State enum parameter
                MethodInfo setNextStateMethod = null;
                foreach (var method in controllerType.GetMethods(AllInstance))
                {
                    if (method.Name == "SetNextState")
                    {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1)
                        {
                            setNextStateMethod = method;
                            break;
                        }
                    }
                }

                if (setNextStateMethod == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} SetNextState method not found");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {postfixName} method not found");
                    return false;
                }

                harmony.Patch(setNextStateMethod, postfix: new HarmonyMethod(postfix));
                if (logPrefix != null)
                    MelonLogger.Msg($"{logPrefix} Patched SetNextState for state transition detection");
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch SetNextState: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches SetCursor(int) method with a postfix.
        /// Used to track cursor position changes.
        /// </summary>
        /// <param name="harmony">Harmony instance</param>
        /// <param name="controllerType">The controller type to patch</param>
        /// <param name="patchType">The type containing the postfix method</param>
        /// <param name="postfixName">Name of the postfix method (default "SetCursor_Postfix")</param>
        /// <param name="logPrefix">Prefix for log messages</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchSetCursor(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            string postfixName = "SetCursor_Postfix", string logPrefix = null)
        {
            try
            {
                var method = controllerType.GetMethod("SetCursor", PublicInstance, null, new[] { typeof(int) }, null);
                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} SetCursor method not found");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {postfixName} method not found");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                if (logPrefix != null)
                    MelonLogger.Msg($"{logPrefix} Patched SetCursor for cursor tracking");
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch SetCursor: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches SelectContent method with a postfix.
        /// Common in list controllers for item selection.
        /// </summary>
        /// <param name="harmony">Harmony instance</param>
        /// <param name="controllerType">The controller type to patch</param>
        /// <param name="patchType">The type containing the postfix method</param>
        /// <param name="paramTypes">Parameter types for SelectContent (default: int, bool)</param>
        /// <param name="postfixName">Name of the postfix method (default "SelectContent_Postfix")</param>
        /// <param name="logPrefix">Prefix for log messages</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchSelectContent(HarmonyLib.Harmony harmony, Type controllerType, Type patchType,
            Type[] paramTypes = null, string postfixName = "SelectContent_Postfix", string logPrefix = null)
        {
            try
            {
                paramTypes ??= new[] { typeof(int), typeof(bool) };

                var method = controllerType.GetMethod("SelectContent", PublicInstance, null, paramTypes, null);
                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} SelectContent method not found");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {postfixName} method not found");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                if (logPrefix != null)
                    MelonLogger.Msg($"{logPrefix} Patched SelectContent for selection tracking");
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch SelectContent: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generic method patcher for any method by name.
        /// </summary>
        /// <param name="harmony">Harmony instance</param>
        /// <param name="targetType">The type containing the target method</param>
        /// <param name="methodName">Name of the method to patch</param>
        /// <param name="patchType">The type containing the postfix method</param>
        /// <param name="postfixName">Name of the postfix method</param>
        /// <param name="paramTypes">Parameter types (null for any)</param>
        /// <param name="logPrefix">Prefix for log messages</param>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchMethod(HarmonyLib.Harmony harmony, Type targetType, string methodName, Type patchType,
            string postfixName, Type[] paramTypes = null, string logPrefix = null)
        {
            try
            {
                MethodInfo method;
                if (paramTypes != null)
                {
                    method = targetType.GetMethod(methodName, AllInstance, null, paramTypes, null);
                }
                else
                {
                    method = targetType.GetMethod(methodName, AllInstance);
                }

                if (method == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {methodName} method not found");
                    return false;
                }

                var postfix = patchType.GetMethod(postfixName, PublicStatic);
                if (postfix == null)
                {
                    if (logPrefix != null)
                        MelonLogger.Warning($"{logPrefix} {postfixName} method not found");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                if (logPrefix != null)
                    MelonLogger.Msg($"{logPrefix} Patched {methodName}");
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch {methodName}: {ex.Message}");
                return false;
            }
        }
    }
}
