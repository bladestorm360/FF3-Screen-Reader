using System;
using UnityEngine;

namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Shared direction and distance formatting helpers.
    /// Used by NavigableEntity and WaypointEntity.
    /// </summary>
    internal static class DirectionHelper
    {
        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another.
        /// </summary>
        public static string GetDirection(Vector3 from, Vector3 to)
        {
            Vector3 diff = to - from;
            float angle = Mathf.Atan2(diff.x, diff.y) * Mathf.Rad2Deg;

            // Normalize to 0-360
            if (angle < 0) angle += 360;

            // Convert to cardinal/intercardinal directions
            if (angle >= 337.5 || angle < 22.5) return "North";
            else if (angle >= 22.5 && angle < 67.5) return "Northeast";
            else if (angle >= 67.5 && angle < 112.5) return "East";
            else if (angle >= 112.5 && angle < 157.5) return "Southeast";
            else if (angle >= 157.5 && angle < 202.5) return "South";
            else if (angle >= 202.5 && angle < 247.5) return "Southwest";
            else if (angle >= 247.5 && angle < 292.5) return "West";
            else if (angle >= 292.5 && angle < 337.5) return "Northwest";
            else return "Unknown";
        }

        /// <summary>
        /// Formats distance in steps (1 step = 16 world units).
        /// </summary>
        public static string FormatSteps(float distance)
        {
            float steps = distance / 16f;
            string stepLabel = Math.Abs(steps - 1f) < 0.1f ? "step" : "steps";
            return $"{steps:F1} {stepLabel}";
        }
    }
}
