namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for building announcement strings with consistent formatting.
    /// Consolidates name+description and item formatting patterns across multiple files.
    /// </summary>
    internal static class AnnouncementBuilder
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
    }
}
