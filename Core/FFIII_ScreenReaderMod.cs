using MelonLoader;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using FieldMap = Il2Cpp.FieldMap;
using FieldMapProvisionInformation = Il2CppLast.Map.FieldMapProvisionInformation;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;

[assembly: MelonInfo(typeof(FFIII_ScreenReader.Core.FFIII_ScreenReaderMod), "FFIII Screen Reader", "1.0.0", "Author")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY III")]

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets
    /// </summary>
    internal enum EntityCategory
    {
        All = 0,
        Chests = 1,
        NPCs = 2,
        MapExits = 3,
        Events = 4,
        Vehicles = 5
    }

    /// <summary>
    /// Main mod class for FFIII Screen Reader.
    /// Delegates to specialized managers for audio, preferences, waypoints, and game info.
    /// </summary>
    public class FFIII_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private InputManager inputManager;
        private EntityScanner entityScanner;
        private AudioLoopManager audioLoopManager;
        private WaypointController waypointController;

        internal static FFIII_ScreenReaderMod Instance { get; private set; }
        internal EntityScanner EntityScanner => entityScanner;

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;
        private EntityCategory currentCategory = EntityCategory.All;

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("FFIII Screen Reader Mod loaded!");

            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            PreferencesManager.Initialize();

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize external sound player for distinct audio feedback
            SoundPlayer.Initialize();

            // Initialize entity name translator (Japanese -> English)
            EntityTranslator.Initialize();

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize entity scanner with saved preferences
            entityScanner = new EntityScanner();
            entityScanner.FilterByPathfinding = PreferencesManager.PathfindingFilterEnabled;
            entityScanner.FilterToLayer = PreferencesManager.ToLayerFilterEnabled;

            // Initialize managers
            audioLoopManager = new AudioLoopManager(this);
            var waypointManager = new WaypointManager();
            var waypointNavigator = new WaypointNavigator(waypointManager);
            waypointController = new WaypointController(this, waypointManager, waypointNavigator);

            // Try manual patching with error handling
            TryManualPatching();

            // Initialize ModMenu
            ModMenu.Initialize();
        }

        private void TryManualPatching()
        {
            LoggerInstance.Msg("Attempting manual Harmony patching...");

            var harmony = new HarmonyLib.Harmony("com.ffiii.screenreader.manual");

            TryPatchCursorNavigation(harmony);
            MessageWindowPatches.ApplyPatches(harmony);
            NewGameNamingPatches.ApplyPatches(harmony);
            ScrollMessagePatches.ApplyPatches(harmony);
            ConfigMenuPatches.ApplyPatches(harmony);
            BattleItemPatchesApplier.ApplyPatches(harmony);
            BattleMagicPatchesApplier.ApplyPatches(harmony);
            BattlePausePatches.ApplyPatches(harmony);
            TryPatchBattleTargetShowWindow(harmony);
            JobMenuPatches.ApplyPatches(harmony);
            ShopPatches.ApplyPatches(harmony);
            MagicMenuPatches.ApplyPatches(harmony);
            StatusMenuPatches.ApplyPatches(harmony);
            EquipMenuState.ApplyTransitionPatches(harmony);
            ItemMenuState.ApplyTransitionPatches(harmony);
            StatusDetailsPatches.ApplyPatches(harmony);
            MovementSpeechPatches.ApplyPatches(harmony);
            VehicleLandingPatches.ApplyPatches(harmony);
            BattleStartPatches.ApplyPatches(harmony);
            BattleSystemMessagePatches.ApplyPatches(harmony);
            SaveLoadPatches.ApplyPatches(harmony);
            PopupPatches.ApplyPatches(harmony);
            EventItemSelectPatches.Apply(harmony);
            GameStatePatches.ApplyPatches(harmony);
            MapTransitionPatches.ApplyPatches(harmony);
            TryPatchEntityInteractions(harmony);
        }

        private void TryPatchBattleTargetShowWindow(HarmonyLib.Harmony harmony)
        {
            try
            {
                var controllerType = typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController);
                var showWindowMethod = controllerType.GetMethod("ShowWindow",
                    BindingFlags.Public | BindingFlags.Instance);

                if (showWindowMethod != null)
                {
                    var prefix = typeof(BattleTargetShowWindowManualPatch)
                        .GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                    harmony.Patch(showWindowMethod, prefix: new HarmonyLib.HarmonyMethod(prefix));
                }
                else
                {
                    LoggerInstance.Warning("[Battle Target] ShowWindow method not found");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[Battle Target] Error applying ShowWindow patch: {ex.Message}");
            }
        }

        private void TryPatchEntityInteractions(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type treasureBoxType = typeof(FieldTresureBox);
                var openMethod = treasureBoxType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance);
                var openPostfix = typeof(ManualPatches).GetMethod("TreasureBox_Open_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (openMethod != null && openPostfix != null)
                {
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                }
                else
                {
                    LoggerInstance.Warning($"FieldTresureBox.Open patch failed. Method: {openMethod != null}, Postfix: {openPostfix != null}");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching entity interactions: {ex.Message}");
            }
        }

        private void TryPatchCursorNavigation(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type cursorType = typeof(GameCursor);
                var nextIndexPostfix = typeof(ManualPatches).GetMethod("CursorNavigation_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (nextIndexPostfix == null)
                {
                    LoggerInstance.Error("Could not find postfix method");
                    return;
                }

                var nextIndexMethod = cursorType.GetMethod("NextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (nextIndexMethod != null)
                    harmony.Patch(nextIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));

                var prevIndexMethod = cursorType.GetMethod("PrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (prevIndexMethod != null)
                    harmony.Patch(prevIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));

                var skipNextMethod = cursorType.GetMethod("SkipNextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipNextMethod != null)
                    harmony.Patch(skipNextMethod, postfix: new HarmonyMethod(nextIndexPostfix));

                var skipPrevMethod = cursorType.GetMethod("SkipPrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipPrevMethod != null)
                    harmony.Patch(skipPrevMethod, postfix: new HarmonyMethod(nextIndexPostfix));
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching cursor navigation: {ex.Message}");
                LoggerInstance.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        #region Lifecycle

        public override void OnDeinitializeMelon()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            audioLoopManager.StopWallToneLoop();
            audioLoopManager.StopBeaconLoop();
            SoundPlayer.Shutdown();
            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                GameObjectCache.ClearAll();

                audioLoopManager.StopWallToneLoop();
                audioLoopManager.StopBeaconLoop();
                audioLoopManager.wallToneSuppressedUntil = Time.time + 1.0f;
                audioLoopManager.beaconSuppressedUntil = Time.time + 1.0f;

                MovementSoundPatches.ResetState();
                CoroutineManager.StartManaged(DelayedInitialScan());
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        private IEnumerator DelayedInitialScan()
        {
            yield return new WaitForSeconds(0.5f);

            try
            {
                var fieldMap = GameObjectCache.GetOrFind<FieldMap>();
                if (fieldMap != null)
                {
                    entityScanner.ForceRescan();
                }

                GameObjectCache.GetOrFind<FieldPlayerController>();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[ComponentCache] Error caching FieldMap: {ex.Message}");
            }

            if (PreferencesManager.WallTonesEnabled) audioLoopManager.StartWallToneLoop();
            if (PreferencesManager.AudioBeaconsEnabled) audioLoopManager.StartBeaconLoop();
        }

        public override void OnUpdate()
        {
            inputManager.Update();
        }

        #endregion

        #region Entity Navigation

        public void ForceEntityRescan()
        {
            entityScanner?.ForceRescan();
        }

        public bool IsCurrentMapWorldMap()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController?.mapManager?.CurrentMapModel != null)
                    return fieldMap.fieldController.mapManager.CurrentMapModel.IsAreaTypeWorld;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error checking world map: {ex.Message}");
            }
            return false;
        }

        internal void AnnounceCurrentEntity()
        {
            if (!EnsureFieldContext()) return;
            RefreshEntitiesIfNeeded();

            var entity = entityScanner.CurrentEntity;
            if (entity == null) { SpeakText("No entities found"); return; }

            var playerPos = GetPlayerPosition();
            if (!playerPos.HasValue) { SpeakText(entity.Name); return; }

            string pathDescription = FieldNavigationHelper.GetPathDescription(entity.Position);
            string announcement = !string.IsNullOrEmpty(pathDescription)
                ? pathDescription
                : FieldNavigationHelper.GetSimplePathDescription(playerPos.Value, entity.Position);

            SpeakText(announcement);
        }

        internal void CycleNext()
        {
            if (!EnsureFieldContext()) return;
            RefreshEntitiesIfNeeded();
            if (entityScanner.Entities.Count == 0) { SpeakText("No entities found"); return; }
            entityScanner.NextEntity();
            AnnounceEntityOnly();
        }

        internal void CyclePrevious()
        {
            if (!EnsureFieldContext()) return;
            RefreshEntitiesIfNeeded();
            if (entityScanner.Entities.Count == 0) { SpeakText("No entities found"); return; }
            entityScanner.PreviousEntity();
            AnnounceEntityOnly();
        }

        internal void AnnounceEntityOnly()
        {
            if (!EnsureFieldContext()) return;

            var entity = entityScanner.CurrentEntity;
            if (entity == null) { SpeakText("No entity selected"); return; }

            var playerPos = GetPlayerPosition();
            if (!playerPos.HasValue) { SpeakText(entity.Name); return; }

            string announcement = entity.FormatDescription(playerPos.Value);
            int index = entityScanner.CurrentIndex + 1;
            int total = entityScanner.Entities.Count;
            SpeakText($"{announcement}, {index} of {total}");
        }

        private void RefreshEntitiesIfNeeded()
        {
            if (entityScanner.Entities.Count == 0)
                entityScanner.ScanEntities();
        }

        internal void ScheduleEntityRefresh()
        {
            CoroutineManager.StartManaged(EntityRefreshCoroutine());
        }

        private IEnumerator EntityRefreshCoroutine()
        {
            yield return null;
            entityScanner.ScanEntities();
        }

        internal void CycleNextCategory()
        {
            if (!EnsureFieldContext()) return;
            currentCategory = (EntityCategory)(((int)currentCategory + 1) % CategoryCount);
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            if (!EnsureFieldContext()) return;
            int prev = (int)currentCategory - 1;
            if (prev < 0) prev = CategoryCount - 1;
            currentCategory = (EntityCategory)prev;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
            if (!EnsureFieldContext()) return;
            if (currentCategory == EntityCategory.All) { SpeakText("Already in All category"); return; }
            currentCategory = EntityCategory.All;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        private void AnnounceCategoryChange()
        {
            SpeakText($"Category: {GetCategoryName(currentCategory)}");
        }

        internal static string GetCategoryName(EntityCategory category)
        {
            return category switch
            {
                EntityCategory.All => "All",
                EntityCategory.Chests => "Treasure Chests",
                EntityCategory.NPCs => "NPCs",
                EntityCategory.MapExits => "Map Exits",
                EntityCategory.Events => "Events",
                EntityCategory.Vehicles => "Vehicles",
                _ => "Unknown"
            };
        }

        internal void TeleportInDirection(Vector2 offset)
        {
            try
            {
                var player = GetFieldPlayer();
                if (player == null) { SpeakText("Not on field map"); return; }

                var entity = entityScanner.CurrentEntity;
                if (entity == null) { SpeakText("No entity selected"); return; }

                Vector3 targetPos = entity.Position + new Vector3(offset.x, offset.y, 0);
                player.transform.localPosition = targetPos;

                string direction = Math.Abs(offset.x) > Math.Abs(offset.y)
                    ? (offset.x > 0 ? "east" : "west")
                    : (offset.y > 0 ? "north" : "south");
                SpeakText($"Teleported to {direction} of {entity.Name}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error teleporting: {ex.Message}");
                SpeakText("Teleport failed");
            }
        }

        #endregion

        #region Toggle Methods

        internal void TogglePathfindingFilter()
        {
            bool newVal = !PreferencesManager.PathfindingFilterEnabled;
            PreferencesManager.SaveToggle("PathfindingFilter", newVal);
            entityScanner.FilterByPathfinding = newVal;
            SpeakText($"Pathfinding filter {(newVal ? "on" : "off")}");
        }

        internal void ToggleMapExitFilter()
        {
            bool newVal = !PreferencesManager.MapExitFilterEnabled;
            PreferencesManager.SaveToggle("MapExitFilter", newVal);
            SpeakText($"Map exit filter {(newVal ? "on" : "off")}");
        }

        internal void ToggleToLayerFilter()
        {
            bool newVal = !PreferencesManager.ToLayerFilterEnabled;
            PreferencesManager.SaveToggle("ToLayerFilter", newVal);
            entityScanner.FilterToLayer = newVal;
            SpeakText($"Layer transition filter {(newVal ? "on" : "off")}");
        }

        internal void ToggleWallTones()
        {
            bool newVal = !PreferencesManager.WallTonesEnabled;
            PreferencesManager.SaveToggle("WallTones", newVal);
            if (newVal) audioLoopManager.StartWallToneLoop();
            else audioLoopManager.StopWallToneLoop();
            SpeakText($"Wall tones {(newVal ? "on" : "off")}");
        }

        internal void ToggleFootsteps()
        {
            bool newVal = !PreferencesManager.FootstepsEnabled;
            PreferencesManager.SaveToggle("Footsteps", newVal);
            SpeakText($"Footsteps {(newVal ? "on" : "off")}");
        }

        internal void ToggleAudioBeacons()
        {
            bool newVal = !PreferencesManager.AudioBeaconsEnabled;
            PreferencesManager.SaveToggle("AudioBeacons", newVal);
            if (newVal) audioLoopManager.StartBeaconLoop();
            else audioLoopManager.StopBeaconLoop();
            SpeakText($"Audio beacons {(newVal ? "on" : "off")}");
        }

        #endregion

        #region Delegated Methods (Game Info, Waypoints)

        internal void AnnounceGilAmount() => GameInfoAnnouncer.AnnounceGilAmount();
        internal void AnnounceCurrentMap() => GameInfoAnnouncer.AnnounceCurrentMap();
        internal void AnnounceCharacterStatus() => GameInfoAnnouncer.AnnounceCharacterStatus();

        internal void CycleNextWaypoint() => waypointController.CycleNext();
        internal void CyclePreviousWaypoint() => waypointController.CyclePrevious();
        internal void CycleNextWaypointCategory() => waypointController.CycleNextCategory();
        internal void CyclePreviousWaypointCategory() => waypointController.CyclePreviousCategory();
        internal void PathfindToCurrentWaypoint() => waypointController.PathfindToCurrentWaypoint();
        internal void AddNewWaypointWithNaming() => waypointController.AddNewWaypointWithNaming();
        internal void RenameCurrentWaypoint() => waypointController.RenameCurrentWaypoint();
        internal void RemoveCurrentWaypoint() => waypointController.RemoveCurrentWaypoint();
        internal void ClearAllWaypointsForMap() => waypointController.ClearAllWaypointsForMap();

        #endregion

        #region Helpers (used by managers)

        internal Vector3? GetPlayerPosition()
        {
            try
            {
                var playerController = GameObjectCache.GetOrFind<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer.transform.localPosition;
            }
            catch { }
            return null;
        }

        internal Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                    return playerController.fieldPlayer;
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        internal bool EnsureFieldContext()
        {
            var fieldMap = GameObjectCache.Get<FieldMap>();
            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                SpeakText("Not on map");
                return false;
            }

            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not on map");
                return false;
            }

            return true;
        }

        internal int GetCurrentMapId()
        {
            try
            {
                var fieldMapInfo = FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                    return fieldMapInfo.CurrentMapId;
            }
            catch { }
            return -1;
        }

        internal string GetCurrentMapIdString()
        {
            try
            {
                var fieldMapInfo = FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                    return fieldMapInfo.CurrentMapId.ToString();
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting map ID: {ex.Message}");
            }
            return "0";
        }

        #endregion

        /// <summary>
        /// Speak text through the screen reader.
        /// </summary>
        public static void SpeakText(string text, bool interrupt = true)
        {
            tolk?.Speak(text, interrupt);
        }
    }

    /// <summary>
    /// Manual patch methods for Harmony.
    /// </summary>
    public static class ManualPatches
    {
        public static void CursorNavigation_Postfix(object __instance)
        {
            try
            {
                var cursor = __instance as GameCursor;
                if (cursor == null) return;

                string cursorPath = "";
                try
                {
                    var t = cursor.transform;
                    cursorPath = t?.name ?? "null";
                    if (t?.parent != null) cursorPath = t.parent.name + "/" + cursorPath;
                    if (t?.parent?.parent != null) cursorPath = t.parent.parent.name + "/" + cursorPath;
                }
                catch { cursorPath = "error"; }

                if (cursorPath.Contains("curosr_parent"))
                {
                    CoroutineManager.StartManaged(
                        MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
                    return;
                }

                var suppressionResult = CursorSuppressionCheck.Check();
                if (suppressionResult.ShouldSuppress)
                {
                    if (suppressionResult.IsPopup)
                        Patches.PopupPatches.ReadCurrentButton(cursor);
                    return;
                }

                CoroutineManager.StartManaged(
                    MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CursorNavigation_Postfix: {ex.Message}");
            }
        }

        public static void TreasureBox_Open_Postfix()
        {
            FFIII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
