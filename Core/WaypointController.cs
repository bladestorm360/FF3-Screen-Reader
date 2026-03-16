using System;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Field;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoints on this map"));
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
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
                FFIII_ScreenReaderMod.SpeakText(T("Cannot get player position"));
                return;
            }

            string mapId = mod.GetCurrentMapIdString();
            string defaultName = waypointManager.GetNextWaypointName(mapId);

            TextInputWindow.Open(
                T("Enter waypoint name"),
                "",
                onConfirm: (name) =>
                {
                    waypointManager.AddWaypoint(name, playerPos.Value, mapId);
                    waypointNavigator.RefreshList(mapId);
                    FFIII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint added: {0}"), name));
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            string mapId = mod.GetCurrentMapIdString();

            TextInputWindow.Open(
                T("Enter new waypoint name"),
                waypoint.Name,
                onConfirm: (newName) =>
                {
                    if (waypointManager.RenameWaypoint(waypoint.WaypointId, newName))
                    {
                        waypointNavigator.RefreshList(mapId);
                        FFIII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint renamed to: {0}"), newName));
                    }
                    else
                    {
                        FFIII_ScreenReaderMod.SpeakText(T("Failed to rename waypoint"));
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoint selected"));
                return;
            }

            string mapId = mod.GetCurrentMapIdString();
            string waypointName = waypoint.Name;

            ConfirmationDialog.Open(
                string.Format(T("Delete waypoint {0}?"), waypointName),
                onYes: () =>
                {
                    if (waypointManager.RemoveWaypoint(waypoint.WaypointId))
                    {
                        waypointNavigator.RefreshList(mapId);
                        waypointNavigator.ClearSelection();
                        FFIII_ScreenReaderMod.SpeakText(string.Format(T("Waypoint deleted: {0}"), waypointName));
                    }
                    else
                    {
                        FFIII_ScreenReaderMod.SpeakText(T("Failed to delete waypoint"));
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
                FFIII_ScreenReaderMod.SpeakText(T("No waypoints to clear on this map"));
                return;
            }

            string plural = count == 1 ? T("waypoint") : T("waypoints");

            ConfirmationDialog.Open(
                string.Format(T("Clear all {0} {1} from this map?"), count, plural),
                onYes: () =>
                {
                    int cleared = waypointManager.ClearMapWaypoints(mapId);
                    waypointNavigator.RefreshList(mapId);
                    waypointNavigator.ClearSelection();
                    FFIII_ScreenReaderMod.SpeakText(string.Format(T("Cleared {0} {1}"), cleared, plural));
                },
                onNo: () => { }
            );
        }
    }
}
