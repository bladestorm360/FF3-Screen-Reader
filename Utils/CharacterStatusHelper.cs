using System;
using System.Collections.Generic;
using Il2CppLast.Data;
using Il2CppLast.Management;
using MelonLoader;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Utility methods for reading character HP/MP and status conditions.
    /// Consolidates duplicate logic from ItemMenuPatches, StatusMenuPatches, and BattleMessagePatches.
    /// </summary>
    public static class CharacterStatusHelper
    {
        /// <summary>
        /// Gets the HP string only for a character parameter.
        /// FF3 uses spell charges per level instead of MP, so HP-only is common.
        /// </summary>
        /// <param name="parameter">The character's parameter data</param>
        /// <returns>Formatted string like "HP 100/200" or empty string if parameter is null</returns>
        public static string GetHPString(CharacterParameterBase parameter)
        {
            if (parameter == null)
                return string.Empty;

            try
            {
                int currentHP = parameter.CurrentHP;
                int maxHP = parameter.ConfirmedMaxHp();
                return $"HP {currentHP}/{maxHP}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CharacterStatusHelper.GetHPString error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the HP and MP string for a character parameter.
        /// </summary>
        /// <param name="parameter">The character's parameter data</param>
        /// <returns>Formatted string like "HP 100/200, MP 50/100" or empty string if parameter is null</returns>
        public static string GetVitalsString(CharacterParameterBase parameter)
        {
            if (parameter == null)
                return string.Empty;

            try
            {
                int currentHP = parameter.CurrentHP;
                int maxHP = parameter.ConfirmedMaxHp();
                int currentMP = parameter.CurrentMP;
                int maxMP = parameter.ConfirmedMaxMp();

                return $"HP {currentHP}/{maxHP}, MP {currentMP}/{maxMP}";
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CharacterStatusHelper.GetVitalsString error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the status conditions for a character parameter.
        /// Uses CurrentConditionList property to match existing FF3 patterns.
        /// </summary>
        /// <param name="parameter">The character's parameter data</param>
        /// <returns>Comma-separated status conditions like "Poison, Blind" or empty string if none</returns>
        public static string GetStatusConditions(CharacterParameterBase parameter)
        {
            if (parameter == null)
                return string.Empty;

            try
            {
                var conditionList = parameter.CurrentConditionList;
                if (conditionList == null || conditionList.Count == 0)
                    return string.Empty;

                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return string.Empty;

                var statusNames = new List<string>();

                foreach (var condition in conditionList)
                {
                    if (condition == null)
                        continue;

                    string conditionMesId = condition.MesIdName;

                    // Skip conditions with no message ID (internal/hidden statuses)
                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                        continue;

                    string localizedConditionName = messageManager.GetMessage(conditionMesId);
                    if (!string.IsNullOrEmpty(localizedConditionName))
                    {
                        statusNames.Add(localizedConditionName);
                    }
                }

                return statusNames.Count > 0 ? string.Join(", ", statusNames) : string.Empty;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CharacterStatusHelper.GetStatusConditions error: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the full status string for a character, including HP and any status conditions.
        /// Uses HP only (not MP) since FF3 uses spell charges instead of MP.
        /// </summary>
        /// <param name="parameter">The character's parameter data</param>
        /// <returns>Formatted string like ", HP 100/200, Poison, Blind" with leading comma, or empty string</returns>
        public static string GetFullStatus(CharacterParameterBase parameter)
        {
            if (parameter == null)
                return string.Empty;

            string hp = GetHPString(parameter);
            if (string.IsNullOrEmpty(hp))
                return string.Empty;

            string result = $", {hp}";

            string conditions = GetStatusConditions(parameter);
            if (!string.IsNullOrEmpty(conditions))
            {
                result += $", {conditions}";
            }

            return result;
        }

        /// <summary>
        /// Gets HP and status conditions as a suffix string (with leading comma).
        /// Commonly used when announcing character targets in menus.
        /// </summary>
        /// <param name="parameter">The character's parameter data</param>
        /// <returns>Formatted string like ", HP 100/200, Poison" or empty string</returns>
        public static string GetHPAndConditions(CharacterParameterBase parameter)
        {
            return GetFullStatus(parameter);
        }
    }
}
