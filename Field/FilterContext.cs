using UnityEngine;
using Il2CppLast.Map;
using Il2CppLast.Entity.Field;
using FFIII_ScreenReader.Utils;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFIII_ScreenReader.Field
{
    /// <summary>
    /// Shared context for filter evaluation containing player and map information.
    /// Passed to filters to avoid repeated lookups.
    /// Auto-populates from the current game state when constructed.
    /// Uses FieldPlayerController (like FF5) for direct access to mapHandle and fieldPlayer.
    /// </summary>
    public class FilterContext
    {
        /// <summary>
        /// Reference to the FieldPlayerController (provides mapHandle and fieldPlayer).
        /// </summary>
        public FieldPlayerController PlayerController { get; set; }

        /// <summary>
        /// Reference to the field player entity.
        /// </summary>
        public FieldPlayer FieldPlayer { get; set; }

        /// <summary>
        /// Handle to the current map for pathfinding.
        /// </summary>
        public IMapAccessor MapHandle { get; set; }

        /// <summary>
        /// Current player position (uses localPosition for pathfinding like FF5).
        /// </summary>
        public Vector3 PlayerPosition { get; set; }

        /// <summary>
        /// Default constructor that auto-populates from current game state.
        /// Uses FieldPlayerController like FF5 does for direct access to mapHandle and fieldPlayer.
        /// </summary>
        public FilterContext()
        {
            // Use FieldPlayerController like FF5 does
            PlayerController = GameObjectCache.Get<FieldPlayerController>();

            if (PlayerController == null)
            {
                PlayerPosition = Vector3.zero;
                return;
            }

            // Get fieldPlayer and mapHandle directly from controller
            FieldPlayer = PlayerController.fieldPlayer;
            MapHandle = PlayerController.mapHandle;

            if (FieldPlayer?.transform != null)
            {
                // Use localPosition like FF5 does for pathfinding
                PlayerPosition = FieldPlayer.transform.localPosition;
            }
            else
            {
                PlayerPosition = Vector3.zero;
            }
        }
    }
}
