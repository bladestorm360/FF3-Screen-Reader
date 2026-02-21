namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Game-specific constants for FF3.
    /// Centralizes magic numbers and state values used across multiple files.
    /// </summary>
    public static class FF3Constants
    {
        /// <summary>
        /// Size of one map cell in world units. One step = one cell = 16 world units.
        /// </summary>
        public const float TILE_SIZE = 16f;

        /// <summary>
        /// Item content type values from ContentType enum.
        /// Used by ItemDetailsAnnouncer to distinguish weapons from armor.
        /// </summary>
        public static class ItemContentTypes
        {
            public const int WEAPON = 2;
            public const int ARMOR = 3;
        }
    }
}
