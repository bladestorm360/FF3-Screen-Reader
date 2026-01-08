using System;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patches for the New Game character naming screen.
    /// FF3 has multiple suggested names per character that players can cycle through.
    /// Uses manual Harmony patching to avoid IL2CPP string parameter crashes.
    /// </summary>
    public static class NewGameNamingPatches
    {
        private static string lastAnnouncedName = "";
        private static int lastAutoNameIndex = -1;
        private static int lastSelectedIndex = -1;

        /// <summary>
        /// Applies all naming screen patches using manual Harmony patching.
        /// Uses AccessTools for IL2CPP-compatible method lookup.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                MelonLogger.Msg("Applying New Game naming screen patches...");

                // Find NewGameWindowController type (KeyInput version for gamepad/keyboard)
                Type controllerType = FindType("Il2CppSerial.FF3.UI.KeyInput.NewGameWindowController");
                if (controllerType == null)
                {
                    MelonLogger.Warning("NewGameWindowController type not found");
                    return;
                }

                MelonLogger.Msg($"Found NewGameWindowController: {controllerType.FullName}");

                // Log available methods for debugging
                LogAvailableMethods(controllerType);

                // Patch InitNameSelect - called when entering name selection state
                // Using AccessTools for IL2CPP compatibility
                var initNameSelectMethod = AccessTools.Method(controllerType, "InitNameSelect");
                if (initNameSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitNameSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initNameSelectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched InitNameSelect");
                }
                else
                {
                    MelonLogger.Warning("InitNameSelect method not found via AccessTools");
                }

                // Patch UpdateNameSelect - called each frame during name selection
                var updateNameSelectMethod = AccessTools.Method(controllerType, "UpdateNameSelect");
                if (updateNameSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("UpdateNameSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateNameSelectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched UpdateNameSelect");
                }
                else
                {
                    MelonLogger.Warning("UpdateNameSelect method not found via AccessTools");
                }

                // Patch InitNameInput - called when entering keyboard input mode
                var initNameInputMethod = AccessTools.Method(controllerType, "InitNameInput");
                if (initNameInputMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitNameInput_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initNameInputMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched InitNameInput");
                }
                else
                {
                    MelonLogger.Warning("InitNameInput method not found via AccessTools");
                }

                // Patch InitSelect - called when entering character selection
                var initSelectMethod = AccessTools.Method(controllerType, "InitSelect");
                if (initSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("InitSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(initSelectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched InitSelect");
                }
                else
                {
                    MelonLogger.Warning("InitSelect method not found via AccessTools");
                }

                // Patch UpdateSelect - called each frame during character selection
                // Used to detect when selectedIndex changes (navigating between slots)
                var updateSelectMethod = AccessTools.Method(controllerType, "UpdateSelect");
                if (updateSelectMethod != null)
                {
                    var postfix = typeof(NewGameNamingPatches).GetMethod("UpdateSelect_Postfix",
                        BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(updateSelectMethod, postfix: new HarmonyMethod(postfix));
                    MelonLogger.Msg("Patched UpdateSelect");
                }
                else
                {
                    MelonLogger.Warning("UpdateSelect method not found via AccessTools");
                }

                MelonLogger.Msg("New Game naming patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error applying naming patches: {ex.Message}");
                MelonLogger.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Logs available methods on a type for debugging IL2CPP reflection issues.
        /// Only logs during initial setup, not during gameplay.
        /// </summary>
        private static void LogAvailableMethods(Type type)
        {
            // Debug logging removed - type discovery working correctly
        }

        /// <summary>
        /// Finds a type by name across all loaded assemblies.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.FullName == fullName)
                        {
                            return type;
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Postfix for InitSelect - announces entering character selection and current slot.
        /// </summary>
        public static void InitSelect_Postfix(object __instance)
        {
            try
            {
                // Reset tracking when entering character selection
                lastAnnouncedName = "";
                lastAutoNameIndex = -1;
                lastSelectedIndex = -1;

                // Get current selected index and announce the slot
                int selectedIndex = GetSelectedIndex(__instance);
                string slotInfo = GetCharacterSlotInfo(__instance, selectedIndex);

                string announcement = "Character selection";
                if (!string.IsNullOrEmpty(slotInfo))
                {
                    announcement += $". {slotInfo}";
                    lastSelectedIndex = selectedIndex;
                }

                MelonLogger.Msg($"[New Game] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UpdateSelect - tracks when user navigates between character slots.
        /// </summary>
        public static void UpdateSelect_Postfix(object __instance)
        {
            try
            {
                int currentIndex = GetSelectedIndex(__instance);

                // Only announce if index changed
                if (currentIndex != lastSelectedIndex && currentIndex >= 0)
                {
                    string slotInfo = GetCharacterSlotInfo(__instance, currentIndex);

                    if (!string.IsNullOrEmpty(slotInfo))
                    {
                        MelonLogger.Msg($"[New Game] {slotInfo}");
                        FFIII_ScreenReaderMod.SpeakText(slotInfo);
                    }

                    lastSelectedIndex = currentIndex;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UpdateSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the selected index from the controller.
        /// Tries both field and property access for IL2CPP compatibility.
        /// </summary>
        private static int GetSelectedIndex(object controller)
        {
            try
            {
                var controllerType = controller.GetType();

                // Try property first (IL2CPP often exposes fields as properties)
                var prop = AccessTools.Property(controllerType, "selectedIndex");
                if (prop != null)
                {
                    return (int)prop.GetValue(controller);
                }

                // Try field
                var field = AccessTools.Field(controllerType, "selectedIndex");
                if (field != null)
                {
                    return (int)field.GetValue(controller);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting selectedIndex: {ex.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Gets character slot info in format "Onion Knight {n}: {name or unnamed}".
        /// </summary>
        private static string GetCharacterSlotInfo(object controller, int index)
        {
            // Ensure index is valid for display (1-based)
            int displayIndex = index >= 0 ? index + 1 : 1;

            try
            {
                // Get SelectedDataList property
                var listProp = AccessTools.Property(controller.GetType(), "SelectedDataList");
                if (listProp == null)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                var list = listProp.GetValue(controller);
                if (list == null)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                // Get count and item at index
                var countProp = list.GetType().GetProperty("Count");
                if (countProp == null)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                int count = (int)countProp.GetValue(list);
                if (index < 0 || index >= count)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                // Get item at index using indexer
                var indexer = list.GetType().GetProperty("Item");
                if (indexer == null)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                var item = indexer.GetValue(list, new object[] { index });
                if (item == null)
                {
                    return $"Onion Knight {displayIndex}: unnamed";
                }

                // Get CharacterName from NewGameSelectData
                var nameProp = AccessTools.Property(item.GetType(), "CharacterName");
                if (nameProp != null)
                {
                    string name = nameProp.GetValue(item) as string;
                    if (string.IsNullOrEmpty(name))
                    {
                        return $"Onion Knight {displayIndex}: unnamed";
                    }
                    return $"Onion Knight {displayIndex}: {name}";
                }

                return $"Onion Knight {displayIndex}: unnamed";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character slot info: {ex.Message}");
                return $"Onion Knight {displayIndex}: unnamed";
            }
        }

        /// <summary>
        /// Postfix for InitNameSelect - announces entering name selection mode.
        /// Reads the current character name from the controller instance.
        /// </summary>
        public static void InitNameSelect_Postfix(object __instance)
        {
            try
            {
                // Reset tracking
                lastAnnouncedName = "";
                lastAutoNameIndex = -1;

                // Try to get CurrentData property which has CharacterName
                string characterName = GetCurrentCharacterName(__instance);

                // Get the current suggested name
                string suggestedName = GetCurrentSuggestedName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Select name for {characterName}";
                }
                else
                {
                    announcement = "Select name";
                }

                if (!string.IsNullOrEmpty(suggestedName))
                {
                    announcement += $". Current: {suggestedName}";
                    lastAnnouncedName = suggestedName;
                }

                MelonLogger.Msg($"[New Game] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for UpdateNameSelect - tracks name changes when cycling through suggestions.
        /// </summary>
        public static void UpdateNameSelect_Postfix(object __instance)
        {
            try
            {
                // Get current autoNameIndex to detect cycling
                var autoNameIndexField = __instance.GetType().GetField("autoNameIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (autoNameIndexField != null)
                {
                    int currentIndex = (int)autoNameIndexField.GetValue(__instance);

                    if (currentIndex != lastAutoNameIndex && lastAutoNameIndex != -1)
                    {
                        // Index changed, announce new name
                        string newName = GetAutoNameByIndex(__instance, currentIndex);
                        if (!string.IsNullOrEmpty(newName) && newName != lastAnnouncedName)
                        {
                            lastAnnouncedName = newName;
                            MelonLogger.Msg($"[New Game] Name: {newName}");
                            FFIII_ScreenReaderMod.SpeakText(newName);
                        }
                    }
                    lastAutoNameIndex = currentIndex;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in UpdateNameSelect_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for InitNameInput - announces entering keyboard input mode.
        /// </summary>
        public static void InitNameInput_Postfix(object __instance)
        {
            try
            {
                string characterName = GetCurrentCharacterName(__instance);

                string announcement;
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = $"Enter name for {characterName}. Type using keyboard.";
                }
                else
                {
                    announcement = "Enter name using keyboard";
                }

                MelonLogger.Msg($"[New Game] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in InitNameInput_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current character name from CurrentData property.
        /// </summary>
        private static string GetCurrentCharacterName(object controller)
        {
            try
            {
                // Get CurrentData property (NewGameSelectData)
                var currentDataProp = controller.GetType().GetProperty("CurrentData",
                    BindingFlags.Public | BindingFlags.Instance);

                if (currentDataProp != null)
                {
                    var currentData = currentDataProp.GetValue(controller);
                    if (currentData != null)
                    {
                        // Get CharacterName from NewGameSelectData
                        var charNameProp = currentData.GetType().GetProperty("CharacterName",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (charNameProp != null)
                        {
                            return charNameProp.GetValue(currentData) as string;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets the current suggested name using GetAutoName method or field access.
        /// </summary>
        private static string GetCurrentSuggestedName(object controller)
        {
            try
            {
                // Try to get autoNameIndex field
                var autoNameIndexField = controller.GetType().GetField("autoNameIndex",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (autoNameIndexField != null)
                {
                    int index = (int)autoNameIndexField.GetValue(controller);
                    lastAutoNameIndex = index;
                    return GetAutoNameByIndex(controller, index);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting suggested name: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Gets an auto-generated name by index using the GetAutoName method.
        /// </summary>
        private static string GetAutoNameByIndex(object controller, int index)
        {
            try
            {
                // Try calling GetAutoName(int index) method
                var getAutoNameMethod = controller.GetType().GetMethod("GetAutoName",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (getAutoNameMethod != null)
                {
                    var result = getAutoNameMethod.Invoke(controller, new object[] { index });
                    return result as string;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error calling GetAutoName: {ex.Message}");
            }
            return null;
        }
    }
}
