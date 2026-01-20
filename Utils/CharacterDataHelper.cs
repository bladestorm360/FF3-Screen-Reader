using System;
using MelonLoader;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for accessing party and character data.
    /// Consolidates character lookup patterns across multiple files.
    /// </summary>
    public static class CharacterDataHelper
    {
        /// <summary>
        /// Gets the row position (Front/Back) for a character.
        /// </summary>
        /// <param name="characterData">The character to check</param>
        /// <returns>"Front Row" or "Back Row", or null if unable to determine</returns>
        public static string GetCharacterRow(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                    return null;

                int characterId = characterData.Id;

                foreach (var corps in corpsList)
                {
                    if (corps != null && corps.CharacterId == characterId)
                    {
                        return corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CharacterDataHelper] Error getting character row: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets a character's current job name (localized).
        /// </summary>
        /// <param name="characterData">The character to check</param>
        /// <returns>Localized job name, or null if not found</returns>
        public static string GetCurrentJobName(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                int jobId = characterData.JobId;
                if (jobId <= 0)
                    return null;

                return LocalizationHelper.GetJobName(jobId);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CharacterDataHelper] Error getting job name: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets a character by party index (0-based).
        /// </summary>
        /// <param name="index">The party index (0-3)</param>
        /// <returns>The character at that index, or null if invalid</returns>
        public static OwnedCharacterData GetCharacterByIndex(int index)
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var party = userDataManager.GetOwnedCharactersClone(false);
                if (party == null || index < 0 || index >= party.Count)
                    return null;

                return party[index];
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CharacterDataHelper] Error getting character by index: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets a character's display name.
        /// </summary>
        /// <param name="characterData">The character</param>
        /// <returns>The character's name, or null if not available</returns>
        public static string GetCharacterName(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                return characterData.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets full character status string for announcements.
        /// Format: "Name, Job, Front/Back Row"
        /// </summary>
        /// <param name="characterData">The character</param>
        /// <returns>Formatted status string, or null if unable to build</returns>
        public static string GetCharacterSummary(OwnedCharacterData characterData)
        {
            if (characterData == null)
                return null;

            try
            {
                string name = GetCharacterName(characterData);
                if (string.IsNullOrEmpty(name))
                    return null;

                string job = GetCurrentJobName(characterData);
                string row = GetCharacterRow(characterData);

                string result = name;
                if (!string.IsNullOrEmpty(job))
                    result += ", " + job;
                if (!string.IsNullOrEmpty(row))
                    result += ", " + row;

                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[CharacterDataHelper] Error building character summary: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the number of characters in the party.
        /// </summary>
        /// <returns>Party size, or 0 if unable to determine</returns>
        public static int GetPartySize()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return 0;

                var party = userDataManager.GetOwnedCharactersClone(false);
                return party?.Count ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }
}
