using System;
using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Field;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Manages audio feedback loops: wall tones and audio beacons.
    /// Extracted from FFIII_ScreenReaderMod to reduce file size.
    /// </summary>
    public class AudioLoopManager
    {
        private readonly FFIII_ScreenReaderMod mod;

        private IEnumerator wallToneCoroutine = null;
        private IEnumerator beaconCoroutine = null;
        private const float BEACON_INTERVAL = 2.0f;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        internal float wallToneSuppressedUntil = 0f;

        // Beacon suppression and debounce
        internal float beaconSuppressedUntil = 0f;
        private float lastBeaconPlayedAt = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Static direction vectors (avoid allocations)
        private static readonly Vector3 DirNorth = new Vector3(0, 16, 0);
        private static readonly Vector3 DirSouth = new Vector3(0, -16, 0);
        private static readonly Vector3 DirEast = new Vector3(16, 0, 0);
        private static readonly Vector3 DirWest = new Vector3(-16, 0, 0);

        public AudioLoopManager(FFIII_ScreenReaderMod mod)
        {
            this.mod = mod;
        }

        public void StartWallToneLoop()
        {
            if (wallToneCoroutine != null) return;
            wallToneCoroutine = WallToneLoop();
            CoroutineManager.StartManaged(wallToneCoroutine);
        }

        public void StopWallToneLoop()
        {
            if (wallToneCoroutine != null)
            {
                CoroutineManager.StopManaged(wallToneCoroutine);
                wallToneCoroutine = null;
            }
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        public void StartBeaconLoop()
        {
            if (beaconCoroutine != null) return;
            lastBeaconPlayedAt = Time.time;
            beaconCoroutine = BeaconLoop();
            CoroutineManager.StartManaged(beaconCoroutine);
        }

        public void StopBeaconLoop()
        {
            if (beaconCoroutine != null)
            {
                CoroutineManager.StopManaged(beaconCoroutine);
                beaconCoroutine = null;
            }
        }

        private IEnumerator WallToneLoop()
        {
            float startTime = Time.time;
            while (Time.time - startTime < 0.3f)
                yield return null;

            float lastCheckTime = Time.time;

            while (PreferencesManager.WallTonesEnabled)
            {
                while (Time.time - lastCheckTime < WALL_TONE_LOOP_INTERVAL)
                    yield return null;
                lastCheckTime = Time.time;

                try
                {
                    float currentTime = Time.time;

                    if (Patches.MapTransitionPatches.IsScreenFading)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    int currentMapId = mod.GetCurrentMapId();
                    if (currentMapId > 0 && wallToneMapId > 0 && currentMapId != wallToneMapId)
                    {
                        wallToneSuppressedUntil = currentTime + 1.0f;
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                    }
                    if (currentMapId > 0)
                        wallToneMapId = currentMapId;

                    if (currentTime < wallToneSuppressedUntil)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var player = mod.GetFieldPlayer();
                    if (player == null)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);
                    var mapExitPositions = mod.EntityScanner?.GetMapExitPositions();
                    Vector3 playerPos = player.transform.localPosition;

                    wallDirectionsBuffer.Clear();

                    if (walls.NorthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirNorth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.North);

                    if (walls.SouthDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirSouth, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.South);

                    if (walls.EastDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirEast, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.East);

                    if (walls.WestDist == 0 &&
                        !FieldNavigationHelper.IsDirectionNearMapExit(playerPos, DirWest, mapExitPositions))
                        wallDirectionsBuffer.Add(SoundPlayer.Direction.West);

                    SoundPlayer.PlayWallTonesLooped(wallDirectionsBuffer);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
                }
            }

            wallToneCoroutine = null;
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        private IEnumerator BeaconLoop()
        {
            float startTime = Time.time;
            while (Time.time - startTime < 0.3f)
                yield return null;

            float lastCheckTime = Time.time;

            while (PreferencesManager.AudioBeaconsEnabled)
            {
                while (Time.time - lastCheckTime < BEACON_INTERVAL)
                    yield return null;
                lastCheckTime = Time.time;

                try
                {
                    float currentTime = Time.time;

                    if (currentTime < beaconSuppressedUntil)
                        continue;

                    if (currentTime - lastBeaconPlayedAt < BEACON_INTERVAL * 0.8f)
                        continue;

                    var entity = mod.EntityScanner?.CurrentEntity;
                    if (entity == null) continue;

                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null) continue;

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                    Vector3 entityPos = entity.Position;

                    if (float.IsNaN(playerPos.x) || float.IsNaN(playerPos.y) ||
                        float.IsNaN(entityPos.x) || float.IsNaN(entityPos.y))
                        continue;

                    if (Mathf.Abs(playerPos.x) > 10000f || Mathf.Abs(playerPos.y) > 10000f ||
                        Mathf.Abs(entityPos.x) > 10000f || Mathf.Abs(entityPos.y) > 10000f)
                        continue;

                    float distance = Vector3.Distance(playerPos, entityPos);
                    float maxDist = 500f;
                    float volumeScale = Mathf.Clamp(1f - (distance / maxDist), 0.15f, 0.60f);

                    float deltaX = entityPos.x - playerPos.x;
                    float pan = Mathf.Clamp(deltaX / 100f, -1f, 1f) * 0.5f + 0.5f;

                    bool isSouth = entityPos.y < playerPos.y - 8f;

                    SoundPlayer.PlayBeacon(isSouth, pan, volumeScale);
                    lastBeaconPlayedAt = currentTime;
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[Beacon] Error: {ex.Message}");
                }
            }

            // Clean up when exiting
            beaconCoroutine = null;
        }
    }
}
