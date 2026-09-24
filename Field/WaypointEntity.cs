using System;
using UnityEngine;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

namespace FFIII_ScreenReader.Field
{
    /// <summary>
    /// Categories for waypoints to allow filtering
    /// </summary>
    internal enum WaypointCategory
    {
        All = 0,           // Filter only - shows all waypoints
        Docks = 1,         // Ship/boat docking locations
        Landmarks = 2,     // Towns, dungeons, notable locations
        AirshipLandings = 3, // Airship landing zones
        Miscellaneous = 4  // Default category for new waypoints
    }

    /// <summary>
    /// Represents a user-defined waypoint for navigation.
    /// Standalone class - not part of the entity navigation system.
    /// Waypoints have their own separate category system.
    /// </summary>
    internal class WaypointEntity
    {
        private readonly string waypointId;
        private readonly string waypointName;
        private readonly Vector3 position;
        private readonly WaypointCategory waypointCategory;
        private readonly string mapId;

        public string WaypointId => waypointId;
        public string WaypointName => waypointName;
        public WaypointCategory WaypointCategoryType => waypointCategory;
        public string MapId => mapId;

        public Vector3 Position => position;

        public string Name => waypointName;

        public WaypointEntity(string id, string name, Vector3 pos, string mapId, WaypointCategory category)
        {
            this.waypointId = id;
            this.waypointName = name;
            this.position = pos;
            this.mapId = mapId;
            this.waypointCategory = category;
        }

        /// <summary>
        /// Gets a display-friendly name for the waypoint category
        /// </summary>
        public static string GetCategoryDisplayName(WaypointCategory category)
        {
            switch (category)
            {
                case WaypointCategory.Docks:
                    return T("Dock");
                case WaypointCategory.Landmarks:
                    return T("Landmark");
                case WaypointCategory.AirshipLandings:
                    return T("Airship Landing");
                default:
                    return T("Waypoint");
            }
        }

        /// <summary>
        /// Gets the (plural) category name for cycling announcements
        /// </summary>
        public static string GetCategoryName(WaypointCategory category)
        {
            switch (category)
            {
                case WaypointCategory.All:
                    return T("All");
                case WaypointCategory.Docks:
                    return T("Docks");
                case WaypointCategory.Landmarks:
                    return T("Landmarks");
                case WaypointCategory.AirshipLandings:
                    return T("Airship Landings");
                default:
                    return T("Miscellaneous");
            }
        }

        /// <summary>
        /// Formats this waypoint for screen reader announcement
        /// </summary>
        public string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = GetDirection(playerPos, Position);
            string categoryName = GetCategoryDisplayName(waypointCategory);
            return $"{waypointName} ({categoryName}) ({FormatSteps(distance)} {direction})";
        }

        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another
        /// </summary>
        private string GetDirection(Vector3 from, Vector3 to)
        {
            return DirectionHelper.GetDirection(from, to);
        }

        /// <summary>
        /// Helper to format distance in steps
        /// </summary>
        private string FormatSteps(float distance)
        {
            return DirectionHelper.FormatSteps(distance);
        }
    }
}
