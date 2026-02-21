using System;
using MelonLoader;
using UnityEngine;
using AbilityCommandContentView = Il2CppSerial.FF3.UI.KeyInput.AbilityCommandContentView;
using AbilityCommandId = Il2CppLast.Defaine.UI.AbilityCommandId;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading magic menu command bar (Use/Learn/Remove/Exchange).
    /// Called by MenuTextDiscovery when navigating the ability command bar.
    /// Prevents fallback text search from finding spell names instead of command names.
    /// </summary>
    internal static class AbilityCommandReader
    {
        /// <summary>
        /// Try to read ability command menu text.
        /// Returns command name if cursor is on an ability command, null otherwise.
        /// </summary>
        public static string TryReadAbilityCommand(Transform cursorTransform, int cursorIndex)
        {
            if (cursorTransform == null)
                return null;

            try
            {
                // Walk up the hierarchy to find ability command content
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string lowerName = current.name.ToLower();

                    // Look for ability_command_content items
                    if (lowerName.Contains("ability_command_content") ||
                        lowerName.Contains("abilitycommandcontent"))
                    {
                        // Try to find the content view on this object
                        var contentView = current.GetComponent<AbilityCommandContentView>();
                        if (contentView != null)
                        {
                            return ReadFromContentView(contentView);
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"AbilityCommandReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read command from AbilityCommandContentView using Data property.
        /// </summary>
        private static string ReadFromContentView(AbilityCommandContentView contentView)
        {
            try
            {
                // Get command data from the content view
                var commandData = contentView.Data;
                if (commandData == null)
                    return null;

                // Try to get the localized name from CommandData
                string commandName = commandData.Name;
                if (!string.IsNullOrEmpty(commandName))
                {
                    return Utils.TextUtils.StripIconMarkup(commandName.Trim());
                }

                // Fallback: map command ID to name
                var commandId = commandData.Id;
                return GetCommandName(commandId);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading from AbilityCommandContentView: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Map AbilityCommandId to display name.
        /// </summary>
        private static string GetCommandName(AbilityCommandId commandId)
        {
            return commandId switch
            {
                AbilityCommandId.Use => "Use",
                AbilityCommandId.Forget => "Forget",
                AbilityCommandId.Memorize => "Learn",  // Memorize is displayed as "Learn" in UI
                AbilityCommandId.Remove => "Remove",
                AbilityCommandId.Exchange => "Exchange",
                _ => null
            };
        }
    }
}
