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
            if (angle >= 337.5 || angle < 22.5) return ModTextTranslator.T("North");
            else if (angle >= 22.5 && angle < 67.5) return ModTextTranslator.T("Northeast");
            else if (angle >= 67.5 && angle < 112.5) return ModTextTranslator.T("East");
            else if (angle >= 112.5 && angle < 157.5) return ModTextTranslator.T("Southeast");
            else if (angle >= 157.5 && angle < 202.5) return ModTextTranslator.T("South");
            else if (angle >= 202.5 && angle < 247.5) return ModTextTranslator.T("Southwest");
            else if (angle >= 247.5 && angle < 292.5) return ModTextTranslator.T("West");
            else if (angle >= 292.5 && angle < 337.5) return ModTextTranslator.T("Northwest");
            else return ModTextTranslator.T("Unknown");
        }

        /// <summary>
        /// Formats distance in steps (1 step = 16 world units).
        /// </summary>
        public static string FormatSteps(float distance)
        {
            float steps = distance / 16f;
            string format = Math.Abs(steps - 1f) < 0.1f ? ModTextTranslator.T("{0} step") : ModTextTranslator.T("{0} steps");
            return string.Format(format, steps.ToString("F1"));
        }
    }
}
