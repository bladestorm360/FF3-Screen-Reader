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
    internal static class HarmonyPatchHelper
    {
        private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags PublicStatic = BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// True when the native object's IL2CPP class is (or derives from) T. Needed in a patch on a folded
        /// method body (an RVA shared with other methods): the managed wrapper type of __instance is the
        /// declaring type whatever object the shared body actually ran on.
        /// </summary>
        public static bool IsNativeInstanceOf<T>(Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase instance)
            where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
        {
            if ((object)instance == null || instance.Pointer == IntPtr.Zero)
                return false;
            IntPtr cls = Il2CppInterop.Runtime.Il2CppClassPointerStore<T>.NativeClassPtr;
            return cls != IntPtr.Zero
                && Il2CppInterop.Runtime.IL2CPP.il2cpp_class_is_assignable_from(
                    cls, Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(instance.Pointer));
        }

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
                return true;
            }
            catch (Exception ex)
            {
                if (logPrefix != null)
                    MelonLogger.Warning($"{logPrefix} Failed to patch {methodName}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Patches a method (resolved with AccessTools.Method, as IL2CPP requires) with a postfix.
        /// Pass paramTypes to disambiguate overloads. Always logs a warning when the target is missing.
        /// </summary>
        /// <returns>True if patch was applied successfully</returns>
        public static bool PatchPostfix(HarmonyLib.Harmony harmony, Type targetType, string methodName, Type patchType,
            string postfixName, string logPrefix, Type[] paramTypes = null)
        {
            try
            {
                var method = paramTypes != null
                    ? AccessTools.Method(targetType, methodName, paramTypes)
                    : AccessTools.Method(targetType, methodName);
                if (method == null)
                {
                    MelonLogger.Warning($"{logPrefix} {targetType.Name}.{methodName} not found");
                    return false;
                }

                harmony.Patch(method, postfix: new HarmonyMethod(AccessTools.Method(patchType, postfixName)));
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{logPrefix} Failed to patch {targetType.Name}.{methodName}: {ex.Message}");
                return false;
            }
        }
    }
}
