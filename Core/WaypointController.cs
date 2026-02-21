using System;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Field;
using FFIII_ScreenReader.Utils;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Handles waypoint operations: cycling, pathfinding, add/rename/remove.
    /// Extracted from FFIII_ScreenReaderMod to reduce file size.
    /// </summary>
    internal class WaypointController
    {
        private readonly FFIII_ScreenReaderMod mod;
        private readonly WaypointManager waypointManager;
        private readonly WaypointNavigator waypointNavigator;

        public WaypointController(FFIII_ScreenReaderMod mod, WaypointManager manager, WaypointNavigator navigator)
        {
            this.mod = mod;
            this.waypointManager = manager;
            this.waypointNavigator = navigator;
        }

        public void CycleNext()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CycleNext();
            FFIII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CyclePrevious()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.RefreshList(mapId);

            if (waypointNavigator.Count == 0)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoints on this map");
                return;
            }

            waypointNavigator.CyclePrevious();
            FFIII_ScreenReaderMod.SpeakText(waypointNavigator.FormatCurrentWaypoint());
        }

        public void CycleNextCategory()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CycleNextCategory(mapId);
            FFIII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void CyclePreviousCategory()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            waypointNavigator.CyclePreviousCategory(mapId);
            FFIII_ScreenReaderMod.SpeakText(waypointNavigator.GetCategoryAnnouncement());
        }

        public void PathfindToCurrentWaypoint()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            var playerPos = mod.GetPlayerPosition();
            if (!playerPos.HasValue)
            {
                FFIII_ScreenReaderMod.SpeakText(waypoint.Name);
                return;
            }

            string pathDescription = FieldNavigationHelper.GetPathDescription(waypoint.Position);

            string announcement;
            if (!string.IsNullOrEmpty(pathDescription))
                announcement = pathDescription;
            else
                announcement = FieldNavigationHelper.GetSimplePathDescription(playerPos.Value, waypoint.Position);

            FFIII_ScreenReaderMod.SpeakText(announcement);
        }

        public void AddNewWaypointWithNaming()
        {
            if (!mod.EnsureFieldContext())
                return;

            var playerPos = mod.GetPlayerPosition();
            if (!playerPos.HasValue)
            {
                FFIII_ScreenReaderMod.SpeakText("Cannot get player position");
                return;
            }

            string mapId = mod.GetCurrentMapIdString();
            string defaultName = waypointManager.GetNextWaypointName(mapId);

            TextInputWindow.Open(
                "Enter waypoint name",
                "",
                onConfirm: (name) =>
                {
                    waypointManager.AddWaypoint(name, playerPos.Value, mapId);
                    waypointNavigator.RefreshList(mapId);
                    FFIII_ScreenReaderMod.SpeakText($"Waypoint added: {name}");
                },
                onCancel: () => { }
            );
        }

        public void RenameCurrentWaypoint()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            string mapId = mod.GetCurrentMapIdString();

            TextInputWindow.Open(
                "Enter new waypoint name",
                waypoint.Name,
                onConfirm: (newName) =>
                {
                    if (waypointManager.RenameWaypoint(waypoint.WaypointId, newName))
                    {
                        waypointNavigator.RefreshList(mapId);
                        FFIII_ScreenReaderMod.SpeakText($"Waypoint renamed to: {newName}");
                    }
                    else
                    {
                        FFIII_ScreenReaderMod.SpeakText("Failed to rename waypoint");
                    }
                },
                onCancel: () => { }
            );
        }

        public void RemoveCurrentWaypoint()
        {
            if (!mod.EnsureFieldContext())
                return;

            var waypoint = waypointNavigator.SelectedWaypoint;
            if (waypoint == null)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoint selected");
                return;
            }

            string mapId = mod.GetCurrentMapIdString();
            string waypointName = waypoint.Name;

            ConfirmationDialog.Open(
                $"Delete waypoint {waypointName}?",
                onYes: () =>
                {
                    if (waypointManager.RemoveWaypoint(waypoint.WaypointId))
                    {
                        waypointNavigator.RefreshList(mapId);
                        waypointNavigator.ClearSelection();
                        FFIII_ScreenReaderMod.SpeakText($"Waypoint deleted: {waypointName}");
                    }
                    else
                    {
                        FFIII_ScreenReaderMod.SpeakText("Failed to delete waypoint");
                    }
                },
                onNo: () => { }
            );
        }

        public void ClearAllWaypointsForMap()
        {
            if (!mod.EnsureFieldContext())
                return;

            string mapId = mod.GetCurrentMapIdString();
            int count = waypointManager.GetWaypointCountForMap(mapId);

            if (count == 0)
            {
                FFIII_ScreenReaderMod.SpeakText("No waypoints to clear on this map");
                return;
            }

            string plural = count == 1 ? "waypoint" : "waypoints";

            ConfirmationDialog.Open(
                $"Clear all {count} {plural} from this map?",
                onYes: () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFIII_ScreenReaderMod.SpeakText($"Cleared {cleared} {plural}");
                },
                onNo: () => { }
            );
        }
    }
}
