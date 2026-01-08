using MelonLoader;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Reads character status details from the status menu.
    /// </summary>
    public static class StatusDetailsReader
    {
        /// <summary>
        /// Reads physical stats from the status screen.
        /// Returns stats like Strength, Vitality, Intellect, Spirit.
        /// </summary>
        public static string ReadPhysicalStats()
        {
            try
            {
                // TODO: Find FF3's StatusDetailsController or equivalent
                // Read physical stat values from the UI

                // FF3 stats typically include:
                // - Strength
                // - Agility
                // - Vitality
                // - Intellect
                // - Mind

                return "Physical stats not yet implemented for FF3";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error reading physical stats: {ex.Message}");
                return "Error reading stats";
            }
        }

        /// <summary>
        /// Reads magical stats from the status screen.
        /// Returns stats like Defense, Magic Defense, Speed.
        /// </summary>
        public static string ReadMagicalStats()
        {
            try
            {
                // TODO: Find FF3's StatusDetailsController or equivalent
                // Read defensive/magical stat values from the UI

                // FF3 stats typically include:
                // - Attack
                // - Defense
                // - Magic Attack
                // - Magic Defense
                // - Accuracy
                // - Evasion

                return "Magical stats not yet implemented for FF3";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error reading magical stats: {ex.Message}");
                return "Error reading stats";
            }
        }

        /// <summary>
        /// Reads basic character info (HP, MP, Level, Job).
        /// </summary>
        public static string ReadBasicInfo()
        {
            try
            {
                // TODO: Read character name, level, HP, MP, Job from status screen
                return "Basic info not yet implemented for FF3";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error reading basic info: {ex.Message}");
                return "Error reading info";
            }
        }

        /// <summary>
        /// Reads equipment information from the status screen.
        /// </summary>
        public static string ReadEquipment()
        {
            try
            {
                // TODO: Read equipped weapon, shield, helmet, armor, accessories
                return "Equipment info not yet implemented for FF3";
            }
            catch (System.Exception ex)
            {
                MelonLogger.Warning($"Error reading equipment: {ex.Message}");
                return "Error reading equipment";
            }
        }
    }
}
