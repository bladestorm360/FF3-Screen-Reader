using System;
using Il2CppLast.UI;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Helper for SelectContent/SetCursor patch boilerplate.
    /// Consolidates common list validation and item retrieval patterns.
    /// </summary>
    internal static class SelectContentHelper
    {
        /// <summary>
        /// Validates index bounds and retrieves item from an IL2CPP List.
        /// Returns null if list is null, empty, index out of bounds, or item is null.
        /// </summary>
        /// <typeparam name="T">The item type (must be a reference type)</typeparam>
        /// <param name="list">The IL2CPP list to retrieve from</param>
        /// <param name="index">The index to retrieve</param>
        /// <returns>The item at index, or null if validation fails</returns>
        public static T TryGetItem<T>(Il2CppSystem.Collections.Generic.List<T> list, int index) where T : class
        {
            if (list == null || list.Count == 0)
                return null;
            if (index < 0 || index >= list.Count)
                return null;
            return list[index];
        }


        /// <summary>
        /// Validates that an instance and cursor are not null, then extracts index from cursor.
        /// Returns -1 if validation fails.
        /// </summary>
        public static int GetCursorIndex(object instance, Cursor cursor)
        {
            if (instance == null || cursor == null)
                return -1;
            return cursor.Index;
        }

        /// <summary>
        /// Validates instance is not null and index is non-negative.
        /// Returns false if validation fails.
        /// </summary>
        public static bool ValidateBasic(object instance, int index)
        {
            return instance != null && index >= 0;
        }
    }
}
