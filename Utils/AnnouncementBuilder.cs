namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for building announcement strings with consistent formatting.
    /// Consolidates name+description and item formatting patterns across multiple files.
    /// </summary>
    public static class AnnouncementBuilder
    {
        /// <summary>
        /// Formats name and optional description into an announcement string.
        /// Handles null/empty checks and icon markup stripping.
        /// </summary>
        /// <param name="name">The item/spell/ability name</param>
        /// <param name="description">Optional description text</param>
        /// <param name="separator">Separator between name and description (default ": ")</param>
        /// <returns>Formatted string, or null if name is empty</returns>
        public static string FormatWithDescription(string name, string description, string separator = ": ")
        {
            if (string.IsNullOrEmpty(name))
                return null;

            name = TextUtils.StripIconMarkup(name);
            if (string.IsNullOrEmpty(name))
                return null;

            if (string.IsNullOrWhiteSpace(description))
                return name;

            description = TextUtils.StripIconMarkup(description);
            if (string.IsNullOrWhiteSpace(description))
                return name;

            return name + separator + description;
        }

        /// <summary>
        /// Appends a description to an existing announcement.
        /// Useful when the announcement has already been built with other info (e.g., charges).
        /// </summary>
        /// <param name="announcement">The existing announcement string</param>
        /// <param name="description">Optional description to append</param>
        /// <param name="separator">Separator before description (default ". ")</param>
        /// <returns>Announcement with description appended, or original if description is empty</returns>
        public static string AppendDescription(string announcement, string description, string separator = ". ")
        {
            if (string.IsNullOrWhiteSpace(announcement))
                return null;

            if (string.IsNullOrWhiteSpace(description))
                return announcement;

            description = TextUtils.StripIconMarkup(description);
            if (string.IsNullOrWhiteSpace(description))
                return announcement;

            return announcement + separator + description;
        }

        /// <summary>
        /// Formats item name with quantity (e.g., "Potion x5").
        /// </summary>
        /// <param name="name">The item name</param>
        /// <param name="quantity">The quantity (returns just name if &lt;= 1)</param>
        /// <param name="format">Format string with {0} for name and {1} for quantity</param>
        /// <returns>Formatted string, or null if name is empty</returns>
        public static string FormatWithQuantity(string name, int quantity, string format = "{0} x{1}")
        {
            if (string.IsNullOrEmpty(name))
                return null;

            name = TextUtils.StripIconMarkup(name);
            if (string.IsNullOrEmpty(name))
                return null;

            if (quantity <= 1)
                return name;

            return string.Format(format, name, quantity);
        }

        /// <summary>
        /// Formats a stat value (e.g., "HP 100/200").
        /// </summary>
        /// <param name="label">The stat label (HP, MP, etc.)</param>
        /// <param name="current">Current value</param>
        /// <param name="max">Maximum value</param>
        /// <returns>Formatted stat string</returns>
        public static string FormatStat(string label, int current, int max)
        {
            return $"{label} {current}/{max}";
        }

        /// <summary>
        /// Formats equipment with optional stat changes (e.g., "Iron Sword: ATK +5").
        /// </summary>
        /// <param name="name">The equipment name</param>
        /// <param name="statChanges">Optional stat change string</param>
        /// <returns>Formatted string, or null if name is empty</returns>
        public static string FormatEquipment(string name, string statChanges)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            name = TextUtils.StripIconMarkup(name);
            if (string.IsNullOrEmpty(name))
                return null;

            if (string.IsNullOrWhiteSpace(statChanges))
                return name;

            return name + ": " + statChanges;
        }

        /// <summary>
        /// Formats target selection (e.g., "Goblin A" or "Luneth, HP 100/200").
        /// </summary>
        /// <param name="name">Target name</param>
        /// <param name="suffix">Optional letter suffix (A, B, C)</param>
        /// <param name="status">Optional status info</param>
        /// <returns>Formatted string, or null if name is empty</returns>
        public static string FormatTarget(string name, string suffix = null, string status = null)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            name = TextUtils.StripIconMarkup(name);
            if (string.IsNullOrEmpty(name))
                return null;

            string result = name;

            if (!string.IsNullOrEmpty(suffix))
                result += " " + suffix;

            if (!string.IsNullOrEmpty(status))
                result += ", " + status;

            return result;
        }
    }
}
