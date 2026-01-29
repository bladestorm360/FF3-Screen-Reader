using MelonLoader;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using MessageWindowManager = Il2CppLast.Message.MessageWindowManager;
using FieldMap = Il2Cpp.FieldMap;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using FieldMapProvisionInformation = Il2CppLast.Map.FieldMapProvisionInformation;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using SubSceneManagerMainGame = Il2CppLast.Management.SubSceneManagerMainGame;

[assembly: MelonInfo(typeof(FFIII_ScreenReader.Core.FFIII_ScreenReaderMod), "FFIII Screen Reader", "1.0.0", "Author")]
[assembly: MelonGame("SQUARE ENIX, Inc.", "FINAL FANTASY III")]

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Entity category for filtering navigation targets
    /// </summary>
    public enum EntityCategory
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
    /// Provides screen reader accessibility support for Final Fantasy III Pixel Remaster.
    /// </summary>
    public class FFIII_ScreenReaderMod : MelonMod
    {
        private static TolkWrapper tolk;
        private InputManager inputManager;
        private EntityScanner entityScanner;

        // Static instance for access from ManualPatches
        internal static FFIII_ScreenReaderMod Instance { get; private set; }

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;

        // Current category
        private EntityCategory currentCategory = EntityCategory.All;

        // Pathfinding filter toggle
        private bool filterByPathfinding = false;

        // Map exit filter toggle
        private bool filterMapExits = false;

        // Audio feedback toggles
        private bool enableWallTones = false;
        private bool enableFootsteps = false;
        private bool enableAudioBeacons = false;

        // Coroutine-based audio loops (replace per-frame polling)
        private IEnumerator wallToneCoroutine = null;
        private IEnumerator beaconCoroutine = null;
        private const float BEACON_INTERVAL = 2.0f;
        private const float WALL_TONE_LOOP_INTERVAL = 0.1f;

        // Map transition suppression for wall tones
        private int wallToneMapId = -1;
        private float wallToneSuppressedUntil = 0f;

        // Reusable direction list buffer to avoid per-cycle allocations
        private static readonly List<SoundPlayer.Direction> wallDirectionsBuffer = new List<SoundPlayer.Direction>(4);

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;
        private static MelonPreferences_Entry<bool> prefWallTones;
        private static MelonPreferences_Entry<bool> prefFootsteps;
        private static MelonPreferences_Entry<bool> prefAudioBeacons;
        private static MelonPreferences_Entry<int> prefWallBumpVolume;
        private static MelonPreferences_Entry<int> prefFootstepVolume;
        private static MelonPreferences_Entry<int> prefWallToneVolume;
        private static MelonPreferences_Entry<int> prefBeaconVolume;
        private static MelonPreferences_Entry<int> prefEnemyHPDisplay;

        // Static volume properties (0-100, default 50)
        public static int WallBumpVolume { get; private set; } = 50;
        public static int FootstepVolume { get; private set; } = 50;
        public static int WallToneVolume { get; private set; } = 50;
        public static int BeaconVolume { get; private set; } = 50;

        // Enemy HP display mode (0=Numbers, 1=Percentage, 2=Hidden)
        public static int EnemyHPDisplay { get; private set; } = 0;

        // Volume setters (save to preferences)
        public static void SetWallBumpVolume(int value)
        {
            WallBumpVolume = Math.Clamp(value, 0, 100);
            prefWallBumpVolume.Value = WallBumpVolume;
            prefsCategory.SaveToFile(false);
        }

        public static void SetFootstepVolume(int value)
        {
            FootstepVolume = Math.Clamp(value, 0, 100);
            prefFootstepVolume.Value = FootstepVolume;
            prefsCategory.SaveToFile(false);
        }

        public static void SetWallToneVolume(int value)
        {
            WallToneVolume = Math.Clamp(value, 0, 100);
            prefWallToneVolume.Value = WallToneVolume;
            prefsCategory.SaveToFile(false);
        }

        public static void SetBeaconVolume(int value)
        {
            BeaconVolume = Math.Clamp(value, 0, 100);
            prefBeaconVolume.Value = BeaconVolume;
            prefsCategory.SaveToFile(false);
        }

        public static void SetEnemyHPDisplay(int value)
        {
            EnemyHPDisplay = Math.Clamp(value, 0, 2);
            prefEnemyHPDisplay.Value = EnemyHPDisplay;
            prefsCategory.SaveToFile(false);
        }

        // Static property accessors for ModMenu and other classes
        public static bool WallTonesEnabled => Instance?.enableWallTones ?? false;
        public static bool FootstepsEnabled => Instance?.enableFootsteps ?? false;
        public static bool AudioBeaconsEnabled => Instance?.enableAudioBeacons ?? false;
        public static bool PathfindingFilterEnabled => Instance?.filterByPathfinding ?? false;
        public static bool MapExitFilterEnabled => Instance?.filterMapExits ?? false;

        // Beacon suppression timestamp
        private float beaconSuppressedUntil = 0f;

        // Debounce timestamp for beacon
        private float lastBeaconPlayedAt = 0f;

        // Static direction vectors (avoid allocations)
        private static readonly UnityEngine.Vector3 DirNorth = new UnityEngine.Vector3(0, 16, 0);
        private static readonly UnityEngine.Vector3 DirSouth = new UnityEngine.Vector3(0, -16, 0);
        private static readonly UnityEngine.Vector3 DirEast = new UnityEngine.Vector3(16, 0, 0);
        private static readonly UnityEngine.Vector3 DirWest = new UnityEngine.Vector3(-16, 0, 0);

        public override void OnInitializeMelon()
        {
            Instance = this;
            LoggerInstance.Msg("FFIII Screen Reader Mod loaded!");

            // Subscribe to scene load events for automatic component caching
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFIII_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");
            prefWallTones = prefsCategory.CreateEntry<bool>("WallTones", false, "Wall Tones", "Play directional tones when approaching walls");
            prefFootsteps = prefsCategory.CreateEntry<bool>("Footsteps", false, "Footsteps", "Play click sound on each tile movement");
            prefAudioBeacons = prefsCategory.CreateEntry<bool>("AudioBeacons", false, "Audio Beacons", "Play ping toward selected entity");
            prefWallBumpVolume = prefsCategory.CreateEntry<int>("WallBumpVolume", 50, "Wall Bump Volume", "Volume for wall bump sounds (0-100)");
            prefFootstepVolume = prefsCategory.CreateEntry<int>("FootstepVolume", 50, "Footstep Volume", "Volume for footstep sounds (0-100)");
            prefWallToneVolume = prefsCategory.CreateEntry<int>("WallToneVolume", 50, "Wall Tone Volume", "Volume for wall proximity tones (0-100)");
            prefBeaconVolume = prefsCategory.CreateEntry<int>("BeaconVolume", 50, "Beacon Volume", "Volume for audio beacon pings (0-100)");
            prefEnemyHPDisplay = prefsCategory.CreateEntry<int>("EnemyHPDisplay", 0, "Enemy HP Display", "0=Numbers, 1=Percentage, 2=Hidden");

            // Load saved preferences
            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;
            enableWallTones = prefWallTones.Value;
            enableFootsteps = prefFootsteps.Value;
            enableAudioBeacons = prefAudioBeacons.Value;
            WallBumpVolume = Math.Clamp(prefWallBumpVolume.Value, 0, 100);
            FootstepVolume = Math.Clamp(prefFootstepVolume.Value, 0, 100);
            WallToneVolume = Math.Clamp(prefWallToneVolume.Value, 0, 100);
            BeaconVolume = Math.Clamp(prefBeaconVolume.Value, 0, 100);
            EnemyHPDisplay = Math.Clamp(prefEnemyHPDisplay.Value, 0, 2);

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
            entityScanner.FilterByPathfinding = filterByPathfinding;

            // Try manual patching with error handling
            TryManualPatching();

            // Initialize ModMenu
            ModMenu.Initialize();

            // CRITICAL: Do NOT start audio loops here during initialization.
            // Audio loops start from DelayedInitialScan() after scene loads.
            // Starting them here causes lag/unresponsiveness because WallToneLoop
            // runs before the scene is ready, resulting in repeated null checks
            // and pathfinding queries on uninitialized data.
        }

        /// <summary>
        /// Attempts to manually apply Harmony patches with detailed error logging.
        /// </summary>
        private void TryManualPatching()
        {
            LoggerInstance.Msg("Attempting manual Harmony patching...");

            var harmony = new HarmonyLib.Harmony("com.ffiii.screenreader.manual");

            // Patch cursor navigation methods (menus and battle)
            TryPatchCursorNavigation(harmony);

            // Patch dialogue methods via MessageWindowPatches (per-page announcements)
            MessageWindowPatches.ApplyPatches(harmony);

            // Patch New Game naming screen
            NewGameNamingPatches.ApplyPatches(harmony);

            // Patch scroll messages (intro/outro text)
            ScrollMessagePatches.ApplyPatches(harmony);

            // Patch config menu (volume sliders, options)
            ConfigMenuPatches.ApplyPatches(harmony);

            // Patch battle item menu
            BattleItemPatchesApplier.ApplyPatches(harmony);

            // Patch battle magic menu
            BattleMagicPatchesApplier.ApplyPatches(harmony);

            // Patch battle pause menu (spacebar menu)
            BattlePausePatches.ApplyPatches(harmony);

            // Patch battle target ShowWindow (attribute-based patch doesn't work reliably in FF3)
            TryPatchBattleTargetShowWindow(harmony);

            // Patch job menu (job selection with job name and level)
            JobMenuPatches.ApplyPatches(harmony);

            // NOTE: StatusMenuPatches disabled - CharacterSelectionReader in MenuTextDiscovery handles it
            // StatusMenuPatches.ApplyPatches(harmony);

            // Patch shop menu (item selection, quantity, command menu)
            ShopPatches.ApplyPatches(harmony);

            // Patch magic menu (command selection and spell list)
            MagicMenuPatches.ApplyPatches(harmony);

            // Patch status menu (character selection)
            StatusMenuPatches.ApplyPatches(harmony);

            // Patch equipment menu transitions
            EquipMenuState.ApplyTransitionPatches(harmony);

            // Patch item menu transitions
            ItemMenuState.ApplyTransitionPatches(harmony);

            // Patch status details (stat navigation with arrow keys)
            StatusDetailsPatches.ApplyPatches(harmony);

            // Patch movement state changes (vehicle announcements)
            MovementSpeechPatches.ApplyPatches(harmony);

            // Patch landing zone detection ("Can land" announcements)
            VehicleLandingPatches.ApplyPatches(harmony);

            // Patch battle start conditions (back attack, preemptive, ambush)
            BattleStartPatches.ApplyPatches(harmony);

            // Patch battle system messages (escape, back attack display, etc.)
            BattleSystemMessagePatches.ApplyPatches(harmony);

            // Patch save/load menus (confirmation popups use persistent CommonPopup instances)
            SaveLoadPatches.ApplyPatches(harmony);

            // Patch confirmation popups (job change, save/load, spell learn/forget, etc.)
            // Uses pointer-based field reading to avoid IL2CPP string property crashes
            PopupPatches.ApplyPatches(harmony);

            // Patch NPC event item selection menu (when NPCs request item use)
            EventItemSelectPatches.Apply(harmony);

            // Patch game state transitions (map changes, battle exit) - event-driven, no polling
            GameStatePatches.ApplyPatches(harmony);

            // Initialize map transition fade detection (cached reflection for FadeManager)
            MapTransitionPatches.ApplyPatches(harmony);

            // Patch entity interactions (treasure chest opened) for entity scanner refresh
            TryPatchEntityInteractions(harmony);

            // Note: Battle patches (BattleCommandPatches, BattleMessagePatches, BattlePausePatches) use
            // HarmonyPatch attributes which MelonLoader auto-applies from the assembly.
            // Do NOT call harmony.PatchAll() here - it would double-apply the patches.
        }

        /// <summary>
        /// Manually patches BattleTargetSelectController.ShowWindow to track target selection state.
        /// The attribute-based patch doesn't work reliably in FF3.
        /// </summary>
        private void TryPatchBattleTargetShowWindow(HarmonyLib.Harmony harmony)
        {
            try
            {
                LoggerInstance.Msg("[Battle Target] Applying manual ShowWindow patch...");

                var controllerType = typeof(Il2CppLast.UI.KeyInput.BattleTargetSelectController);
                var showWindowMethod = controllerType.GetMethod("ShowWindow",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

                if (showWindowMethod != null)
                {
                    var prefix = typeof(BattleTargetShowWindowManualPatch)
                        .GetMethod("Prefix", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    harmony.Patch(showWindowMethod, prefix: new HarmonyLib.HarmonyMethod(prefix));
                    LoggerInstance.Msg("[Battle Target] ShowWindow patch applied successfully");
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

        /// <summary>
        /// Patches entity interaction methods for entity scanner refresh.
        /// Triggers rescan when treasure chests are opened to update their state.
        /// </summary>
        private void TryPatchEntityInteractions(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch FieldTresureBox.Open() - triggers entity refresh when chest is opened
                Type treasureBoxType = typeof(FieldTresureBox);
                var openMethod = treasureBoxType.GetMethod("Open", BindingFlags.Public | BindingFlags.Instance);
                var openPostfix = typeof(ManualPatches).GetMethod("TreasureBox_Open_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (openMethod != null && openPostfix != null)
                {
                    harmony.Patch(openMethod, postfix: new HarmonyMethod(openPostfix));
                    LoggerInstance.Msg("Patched FieldTresureBox.Open for entity refresh");
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

        /// <summary>
        /// Patches cursor navigation methods for menu reading.
        /// </summary>
        private void TryPatchCursorNavigation(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Use typeof() directly - much faster than assembly scanning
                Type cursorType = typeof(GameCursor);
                LoggerInstance.Msg($"Found Cursor type: {cursorType.FullName}");

                // Get postfix methods
                var nextIndexPostfix = typeof(ManualPatches).GetMethod("CursorNavigation_Postfix", BindingFlags.Public | BindingFlags.Static);

                if (nextIndexPostfix == null)
                {
                    LoggerInstance.Error("Could not find postfix method");
                    return;
                }

                // Patch NextIndex
                var nextIndexMethod = cursorType.GetMethod("NextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (nextIndexMethod != null)
                {
                    harmony.Patch(nextIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched NextIndex");
                }

                // Patch PrevIndex
                var prevIndexMethod = cursorType.GetMethod("PrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (prevIndexMethod != null)
                {
                    harmony.Patch(prevIndexMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched PrevIndex");
                }

                // Patch SkipNextIndex
                var skipNextMethod = cursorType.GetMethod("SkipNextIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipNextMethod != null)
                {
                    harmony.Patch(skipNextMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched SkipNextIndex");
                }

                // Patch SkipPrevIndex
                var skipPrevMethod = cursorType.GetMethod("SkipPrevIndex", BindingFlags.Public | BindingFlags.Instance);
                if (skipPrevMethod != null)
                {
                    harmony.Patch(skipPrevMethod, postfix: new HarmonyMethod(nextIndexPostfix));
                    LoggerInstance.Msg("Patched SkipPrevIndex");
                }

                LoggerInstance.Msg("Cursor navigation patches applied successfully");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching cursor navigation: {ex.Message}");
                LoggerInstance.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        public override void OnDeinitializeMelon()
        {
            // Unsubscribe from scene load events
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Stop audio loops
            StopWallToneLoop();
            StopBeaconLoop();

            // Shutdown sound player (closes waveOut handles, frees unmanaged memory)
            SoundPlayer.Shutdown();

            CoroutineManager.CleanupAll();
            tolk?.Unload();
        }

        /// <summary>
        /// Called when a new scene is loaded.
        /// Automatically caches commonly-used Unity components to avoid expensive FindObjectOfType calls.
        /// </summary>
        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            try
            {
                LoggerInstance.Msg($"[ComponentCache] Scene loaded: {scene.name}");

                // Debug: Log all MonoBehaviour types for title-related scenes
                if (scene.name == "Splash" || scene.name == "Title" || scene.name == "TitleScreen")
                {
                    CoroutineManager.StartManaged(DebugLogSceneComponents(scene.name));
                }

                // Clear old cache
                GameObjectCache.Clear<FieldMap>();

                // Stop audio loops during scene transition and suppress wall tones/beacons briefly
                StopWallToneLoop();
                StopBeaconLoop();
                wallToneSuppressedUntil = Time.time + 1.0f;
                beaconSuppressedUntil = Time.time + 1.0f;

                // Reset movement state for new map
                MovementSoundPatches.ResetState();

                // Delay entity scan to allow scene to fully initialize
                CoroutineManager.StartManaged(DelayedInitialScan());
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Debug coroutine to log specific IL2CPP title-related components in a scene.
        /// Waits a frame to ensure components are initialized.
        /// </summary>
        private System.Collections.IEnumerator DebugLogSceneComponents(string sceneName)
        {
            // Wait 2 frames to ensure scene is fully loaded
            yield return null;
            yield return null;

            try
            {
                LoggerInstance.Msg($"[TitleDebug] ========== Checking IL2CPP types in {sceneName} ==========");

                // Check for specific IL2CPP title-related types
                CheckIL2CppType<Il2CppLast.UI.Touch.TitleWindowController>("Touch.TitleWindowController");
                CheckIL2CppType<Il2CppLast.UI.KeyInput.TitleWindowController>("KeyInput.TitleWindowController");
                CheckIL2CppType<Il2CppLast.UI.Touch.TitleWindowView>("Touch.TitleWindowView");
                CheckIL2CppType<Il2CppLast.UI.KeyInput.TitleWindowView>("KeyInput.TitleWindowView");
                CheckIL2CppType<Il2CppLast.UI.SplashController>("SplashController");
                CheckIL2CppType<Il2CppLast.UI.Touch.TitleMenuCommandController>("Touch.TitleMenuCommandController");
                CheckIL2CppType<Il2CppLast.UI.KeyInput.TitleMenuCommandController>("KeyInput.TitleMenuCommandController");
                CheckIL2CppType<Il2CppLast.Scene.SceneTitleScreen>("SceneTitleScreen");

                LoggerInstance.Msg($"[TitleDebug] ========== End {sceneName} ==========");
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[TitleDebug] Error logging components: {ex.Message}");
            }
        }

        /// <summary>
        /// Helper to check if an IL2CPP type exists in the scene and log its state.
        /// </summary>
        private void CheckIL2CppType<T>(string typeName) where T : UnityEngine.Object
        {
            try
            {
                var instance = UnityEngine.Object.FindObjectOfType<T>();
                if (instance != null)
                {
                    LoggerInstance.Msg($"[TitleDebug] FOUND: {typeName} at 0x{instance.Pointer.ToInt64():X}");
                }
                else
                {
                    LoggerInstance.Msg($"[TitleDebug] NOT FOUND: {typeName}");
                }
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Msg($"[TitleDebug] ERROR checking {typeName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that delays entity scanning to allow scene to fully initialize.
        /// </summary>
        private System.Collections.IEnumerator DelayedInitialScan()
        {
            // Wait 0.5 seconds for scene to fully initialize and entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Try to find and cache FieldMap and FieldPlayerController
            try
            {
                var fieldMap = UnityEngine.Object.FindObjectOfType<FieldMap>();
                if (fieldMap != null)
                {
                    GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg("[ComponentCache] Cached FieldMap");

                    // Initial entity scan
                    entityScanner.ScanEntities();
                    LoggerInstance.Msg($"[ComponentCache] Found {entityScanner.Entities.Count} entities");
                }

                // Also cache FieldPlayerController for EnsureFieldContext() checks
                var playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                if (playerController != null)
                {
                    GameObjectCache.Register(playerController);
                    LoggerInstance.Msg("[ComponentCache] Cached FieldPlayerController");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[ComponentCache] Error caching FieldMap: {ex.Message}");
            }

            // Restart audio loops after scene has settled
            if (enableWallTones) StartWallToneLoop();
            if (enableAudioBeacons) StartBeaconLoop();
        }

        public override void OnUpdate()
        {
            // Handle all input
            inputManager.Update();
        }

        /// <summary>
        /// Forces an entity rescan. Called from GameStatePatches on map transitions.
        /// </summary>
        public void ForceEntityRescan()
        {
            entityScanner?.ForceRescan();
        }

        /// <summary>
        /// Check if the current map is a world map (overworld).
        /// </summary>
        public bool IsCurrentMapWorldMap()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController?.mapManager?.CurrentMapModel != null)
                {
                    return fieldMap.fieldController.mapManager.CurrentMapModel.IsAreaTypeWorld;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error checking world map: {ex.Message}");
            }
            return false;
        }

        internal void AnnounceCurrentEntity()
        {
            if (!EnsureFieldContext())
                return;

            RefreshEntitiesIfNeeded();

            var entity = entityScanner.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entities found");
                return;
            }

            var playerPos = GetPlayerPosition();
            if (!playerPos.HasValue)
            {
                SpeakText(entity.Name);
                return;
            }

            // Get pathfinding info
            string pathDescription = GetPathToEntity(entity, playerPos.Value);

            string announcement;
            if (!string.IsNullOrEmpty(pathDescription))
            {
                // Only announce directions, not entity name (already known from J/L selection)
                announcement = pathDescription;
            }
            else
            {
                // Fallback to simple direction/distance if pathfinding failed
                announcement = FieldNavigationHelper.GetSimplePathDescription(playerPos.Value, entity.Position);
            }

            SpeakText(announcement);
        }

        /// <summary>
        /// Gets pathfinding directions to an entity.
        /// Uses FieldPlayerController for map handle and player access (like FF5).
        /// </summary>
        private string GetPathToEntity(NavigableEntity entity, Vector3 playerPos)
        {
            try
            {
                // Use FieldPlayerController like FF5 does - it has direct access to mapHandle and fieldPlayer
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.fieldPlayer == null || playerController.mapHandle == null)
                    return null;

                // Get target position using localPosition (like FF5)
                Vector3 targetPos = entity.Position;

                // Get path using the controller's mapHandle and fieldPlayer
                var pathInfo = FieldNavigationHelper.FindPathTo(
                    playerPos,
                    targetPos,
                    playerController.mapHandle,
                    playerController.fieldPlayer
                );

                if (pathInfo.Success && !string.IsNullOrEmpty(pathInfo.Description))
                {
                    return pathInfo.Description;
                }
            }
            catch { }

            return null;
        }

        internal void CycleNext()
        {
            if (!EnsureFieldContext())
                return;

            RefreshEntitiesIfNeeded();

            if (entityScanner.Entities.Count == 0)
            {
                SpeakText("No entities found");
                return;
            }

            entityScanner.NextEntity();
            AnnounceEntityOnly();
        }

        internal void CyclePrevious()
        {
            if (!EnsureFieldContext())
                return;

            RefreshEntitiesIfNeeded();

            if (entityScanner.Entities.Count == 0)
            {
                SpeakText("No entities found");
                return;
            }

            entityScanner.PreviousEntity();
            AnnounceEntityOnly();
        }

        internal void AnnounceEntityOnly()
        {
            if (!EnsureFieldContext())
                return;

            var entity = entityScanner.CurrentEntity;
            if (entity == null)
            {
                SpeakText("No entity selected");
                return;
            }

            var playerPos = GetPlayerPosition();
            if (!playerPos.HasValue)
            {
                SpeakText(entity.Name);
                return;
            }

            string announcement = entity.FormatDescription(playerPos.Value);
            int index = entityScanner.CurrentIndex + 1;
            int total = entityScanner.Entities.Count;
            announcement = $"{announcement}, {index} of {total}";

            SpeakText(announcement);
        }

        private void RefreshEntitiesIfNeeded()
        {
            // Only scan if entity list is empty - event-driven hooks handle state updates
            if (entityScanner.Entities.Count == 0)
            {
                entityScanner.ScanEntities();
            }
        }

        /// <summary>
        /// Schedules an entity refresh after a 1-frame delay.
        /// Called by interaction hooks (treasure chest, dialogue end) to update entity states.
        /// </summary>
        internal void ScheduleEntityRefresh()
        {
            CoroutineManager.StartManaged(EntityRefreshCoroutine());
        }

        private IEnumerator EntityRefreshCoroutine()
        {
            // Wait one frame for game state to fully update
            yield return null;

            // Rescan entities to pick up state changes (e.g., chest opened)
            entityScanner.ScanEntities();
            LoggerInstance.Msg("[EntityRefresh] Rescanned entities after interaction");
        }

        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Try to get FieldPlayerController - first from cache, then find it
                var playerController = GameObjectCache.Get<FieldPlayerController>();

                // If not in cache, try to find it
                if (playerController == null)
                {
                    playerController = UnityEngine.Object.FindObjectOfType<FieldPlayerController>();
                    if (playerController != null)
                    {
                        GameObjectCache.Register(playerController);
                    }
                }

                if (playerController?.fieldPlayer != null)
                {
                    // Use localPosition like FF5 does for pathfinding
                    return playerController.fieldPlayer.transform.localPosition;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the FieldPlayer from the FieldPlayerController.
        /// Uses direct IL2CPP access (not reflection, which doesn't work on IL2CPP types).
        /// </summary>
        private Il2CppLast.Entity.Field.FieldPlayer GetFieldPlayer()
        {
            try
            {
                // Use FieldPlayerController - same pattern used in GetPlayerPosition() and pathfinding
                var playerController = GameObjectCache.Get<FieldPlayerController>();
                if (playerController?.fieldPlayer != null)
                {
                    return playerController.fieldPlayer;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting field player: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Checks if player is on an active field map.
        /// Returns true if on valid map (ready for entity navigation), false otherwise.
        /// Ported from FF4 to prevent entity navigation on title screen, menus, loading screens.
        /// </summary>
        internal bool EnsureFieldContext()
        {
            // Check if FieldMap exists and is active
            var fieldMap = GameObjectCache.Get<FieldMap>();
            if (fieldMap == null || !fieldMap.gameObject.activeInHierarchy)
            {
                SpeakText("Not on map");
                return false;
            }

            // Check if player controller exists
            var playerController = GameObjectCache.Get<FieldPlayerController>();
            if (playerController?.fieldPlayer == null)
            {
                SpeakText("Not on map");
                return false;
            }

            return true;
        }

        internal void CycleNextCategory()
        {
            if (!EnsureFieldContext())
                return;

            // Cycle to next category
            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            entityScanner.CurrentCategory = currentCategory;

            // Announce new category
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
            if (!EnsureFieldContext())
                return;

            // Cycle to previous category
            int prevCategory = (int)currentCategory - 1;
            if (prevCategory < 0)
                prevCategory = CategoryCount - 1;

            currentCategory = (EntityCategory)prevCategory;
            entityScanner.CurrentCategory = currentCategory;

            // Announce new category
            AnnounceCategoryChange();
        }

        internal void ResetToAllCategory()
        {
            if (!EnsureFieldContext())
                return;

            if (currentCategory == EntityCategory.All)
            {
                SpeakText("Already in All category");
                return;
            }

            currentCategory = EntityCategory.All;
            entityScanner.CurrentCategory = currentCategory;
            AnnounceCategoryChange();
        }

        internal void TogglePathfindingFilter()
        {
            filterByPathfinding = !filterByPathfinding;

            // Update entity scanner's pathfinding filter
            entityScanner.FilterByPathfinding = filterByPathfinding;

            // Save to preferences
            prefPathfindingFilter.Value = filterByPathfinding;
            prefsCategory.SaveToFile(false);

            string status = filterByPathfinding ? "on" : "off";
            SpeakText($"Pathfinding filter {status}");
        }

        internal void ToggleMapExitFilter()
        {
            filterMapExits = !filterMapExits;

            // Save to preferences
            prefMapExitFilter.Value = filterMapExits;
            prefsCategory.SaveToFile(false);

            string status = filterMapExits ? "on" : "off";
            SpeakText($"Map exit filter {status}");
        }

        #region Audio Loop Management

        /// <summary>
        /// Starts the wall tone coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartWallToneLoop()
        {
            if (wallToneCoroutine != null) return;
            wallToneCoroutine = WallToneLoop();
            CoroutineManager.StartManaged(wallToneCoroutine);
        }

        /// <summary>
        /// Stops the wall tone coroutine loop and silences any playing tone.
        /// </summary>
        private void StopWallToneLoop()
        {
            if (wallToneCoroutine != null)
            {
                try { MelonCoroutines.Stop(wallToneCoroutine); }
                catch { }
                wallToneCoroutine = null;
            }
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Starts the audio beacon coroutine loop. Safe to call if already running (no-op).
        /// </summary>
        private void StartBeaconLoop()
        {
            if (beaconCoroutine != null) return;
            lastBeaconPlayedAt = Time.time;  // Prevent immediate beacon on first enable
            beaconCoroutine = BeaconLoop();
            CoroutineManager.StartManaged(beaconCoroutine);
        }

        /// <summary>
        /// Stops the audio beacon coroutine loop.
        /// </summary>
        private void StopBeaconLoop()
        {
            if (beaconCoroutine != null)
            {
                try { MelonCoroutines.Stop(beaconCoroutine); }
                catch { }
                beaconCoroutine = null;
            }
        }

        /// <summary>
        /// Coroutine loop that checks for adjacent walls every 100ms and plays looping tones.
        /// Uses manual time-based waiting (IL2CPP compatible) instead of WaitForSeconds.
        /// </summary>
        private IEnumerator WallToneLoop()
        {
            // Initial delay for scene stability
            float startTime = Time.time;
            while (Time.time - startTime < 0.3f)
                yield return null;

            float lastCheckTime = Time.time;

            // Clean exit condition - loop while feature is enabled
            while (enableWallTones)
            {
                // Manual time-based waiting (IL2CPP compatible)
                while (Time.time - lastCheckTime < WALL_TONE_LOOP_INTERVAL)
                    yield return null;
                lastCheckTime = Time.time;

                try
                {
                    float currentTime = Time.time;

                    // Check if screen is fading (map transition)
                    if (Patches.MapTransitionPatches.IsScreenFading)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    // Detect sub-map transitions and suppress tones briefly
                    int currentMapId = GetCurrentMapId();
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

                    var player = GetFieldPlayer();
                    if (player == null)
                    {
                        if (SoundPlayer.IsWallTonePlaying())
                            SoundPlayer.StopWallTone();
                        continue;
                    }

                    var walls = FieldNavigationHelper.GetNearbyWallsWithDistance(player);
                    var mapExitPositions = entityScanner?.GetMapExitPositions();
                    Vector3 playerPos = player.transform.localPosition;

                    // Reuse static buffer to avoid per-cycle allocations
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

                    // Pass buffer directly (no ToArray() - IList<Direction> parameter)
                    SoundPlayer.PlayWallTonesLooped(wallDirectionsBuffer);
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[WallTones] Error: {ex.Message}");
                }
            }

            // Clean up when loop exits
            if (SoundPlayer.IsWallTonePlaying())
                SoundPlayer.StopWallTone();
        }

        /// <summary>
        /// Coroutine loop that plays audio beacon pings every 2 seconds.
        /// Uses manual time-based waiting (IL2CPP compatible) instead of WaitForSeconds.
        /// </summary>
        private IEnumerator BeaconLoop()
        {
            // Initial delay for scene stability
            float startTime = Time.time;
            while (Time.time - startTime < 0.3f)
                yield return null;

            float lastCheckTime = Time.time;

            // Clean exit condition - loop while feature is enabled
            while (enableAudioBeacons)
            {
                // Manual time-based waiting (IL2CPP compatible)
                while (Time.time - lastCheckTime < BEACON_INTERVAL)
                    yield return null;
                lastCheckTime = Time.time;

                try
                {
                    float currentTime = Time.time;

                    // Check suppression
                    if (currentTime < beaconSuppressedUntil)
                        continue;

                    // Debounce check
                    if (currentTime - lastBeaconPlayedAt < BEACON_INTERVAL * 0.8f)
                        continue;

                    var entity = entityScanner?.CurrentEntity;
                    if (entity == null) continue;

                    var playerController = GameObjectCache.Get<FieldPlayerController>();
                    if (playerController?.fieldPlayer == null) continue;

                    Vector3 playerPos = playerController.fieldPlayer.transform.localPosition;
                    Vector3 entityPos = entity.Position;

                    // Sanity checks for NaN and extreme positions
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
        }

        /// <summary>
        /// Gets the current map ID from FieldMapProvisionInformation.
        /// Returns -1 if unable to retrieve.
        /// </summary>
        private int GetCurrentMapId()
        {
            try
            {
                var fieldMapInfo = FieldMapProvisionInformation.Instance;
                if (fieldMapInfo != null)
                {
                    return fieldMapInfo.CurrentMapId;
                }
            }
            catch { }
            return -1;
        }

        internal void ToggleWallTones()
        {
            enableWallTones = !enableWallTones;

            if (enableWallTones)
                StartWallToneLoop();
            else
                StopWallToneLoop();

            // Save to preferences
            prefWallTones.Value = enableWallTones;
            prefsCategory.SaveToFile(false);

            string status = enableWallTones ? "on" : "off";
            SpeakText($"Wall tones {status}");
        }

        internal void ToggleFootsteps()
        {
            enableFootsteps = !enableFootsteps;

            // Save to preferences
            prefFootsteps.Value = enableFootsteps;
            prefsCategory.SaveToFile(false);

            string status = enableFootsteps ? "on" : "off";
            SpeakText($"Footsteps {status}");
        }

        internal void ToggleAudioBeacons()
        {
            enableAudioBeacons = !enableAudioBeacons;

            if (enableAudioBeacons)
                StartBeaconLoop();
            else
                StopBeaconLoop();

            // Save to preferences
            prefAudioBeacons.Value = enableAudioBeacons;
            prefsCategory.SaveToFile(false);

            string status = enableAudioBeacons ? "on" : "off";
            SpeakText($"Audio beacons {status}");
        }

        internal bool IsFootstepsEnabled() => enableFootsteps;

        #endregion

        private void AnnounceCategoryChange()
        {
            string categoryName = GetCategoryName(currentCategory);
            string announcement = $"Category: {categoryName}";
            SpeakText(announcement);
        }

        public static string GetCategoryName(EntityCategory category)
        {
            switch (category)
            {
                case EntityCategory.All: return "All";
                case EntityCategory.Chests: return "Treasure Chests";
                case EntityCategory.NPCs: return "NPCs";
                case EntityCategory.MapExits: return "Map Exits";
                case EntityCategory.Events: return "Events";
                case EntityCategory.Vehicles: return "Vehicles";
                default: return "Unknown";
            }
        }

        internal void TeleportInDirection(Vector2 offset)
        {
            try
            {
                var player = GetFieldPlayer();
                if (player == null)
                {
                    SpeakText("Not on field map");
                    return;
                }

                // Get the currently selected entity
                var entity = entityScanner.CurrentEntity;
                if (entity == null)
                {
                    SpeakText("No entity selected");
                    return;
                }

                // Calculate target position: entity position + offset
                Vector3 entityPos = entity.Position;
                Vector3 targetPos = entityPos + new Vector3(offset.x, offset.y, 0);

                // Teleport player to target position
                player.transform.localPosition = targetPos;

                // Announce with direction relative to entity and entity name
                string direction = GetDirectionFromOffset(offset);
                string entityName = entity.Name;
                SpeakText($"Teleported to {direction} of {entityName}");
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error teleporting: {ex.Message}");
                SpeakText("Teleport failed");
            }
        }

        private string GetDirectionFromOffset(Vector2 offset)
        {
            if (Math.Abs(offset.x) > Math.Abs(offset.y))
            {
                return offset.x > 0 ? "east" : "west";
            }
            else
            {
                return offset.y > 0 ? "north" : "south";
            }
        }

        internal void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int gil = userDataManager.OwendGil;
                    SpeakText($"{gil} Gil");
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting gil: {ex.Message}");
            }
            SpeakText("Gil not available");
        }

        internal void AnnounceCurrentMap()
        {
            try
            {
                // Use MapNameResolver for proper area name + floor resolution
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                if (!string.IsNullOrEmpty(mapName) && mapName != "Unknown")
                {
                    SpeakText(mapName);
                    return;
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting map name: {ex.Message}");
            }
            SpeakText("Map name not available");
        }

        internal void AnnounceCharacterStatus()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    SpeakText("Character data not available");
                    return;
                }

                // Get party characters using GetOwnedCharactersClone
                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null || partyList.Count == 0)
                {
                    SpeakText("No party members");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var charData in partyList)
                {
                    try
                    {
                        if (charData != null)
                        {
                            string name = charData.Name;
                            var param = charData.Parameter;
                            if (param != null)
                            {
                                int currentHp = param.CurrentHP;
                                int maxHp = param.ConfirmedMaxHp();
                                int currentMp = param.CurrentMP;
                                int maxMp = param.ConfirmedMaxMp();

                                sb.AppendLine($"{name}: HP {currentHp}/{maxHp}, MP {currentMp}/{maxMp}");
                            }
                        }
                    }
                    catch { }
                }

                string status = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                {
                    SpeakText(status);
                }
                else
                {
                    SpeakText("No character status available");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"Error getting character status: {ex.Message}");
                SpeakText("Character status not available");
            }
        }

        /// <summary>
        /// Clears all menu states except the specified one.
        /// Called by patches when a menu activates to ensure only one menu is active at a time.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            ManualPatches.ClearOtherMenuStates(exceptMenu);
        }

        /// <summary>
        /// Speak text through the screen reader.
        /// Thread-safe: TolkWrapper uses locking to prevent concurrent native calls.
        /// </summary>
        /// <param name="text">Text to speak</param>
        /// <param name="interrupt">Whether to interrupt current speech (true for user actions, false for game events)</param>
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

        /// <summary>
        /// Postfix for cursor navigation methods (NextIndex, PrevIndex, etc.)
        /// Reads the menu text at the current cursor position.
        /// Only suppressed when a specific patch is actively handling that menu.
        /// </summary>
        public static void CursorNavigation_Postfix(object __instance)
        {
            try
            {
                var cursor = __instance as GameCursor;
                if (cursor == null) return;

                // Get cursor path for pause menu detection
                string cursorPath = "";
                try
                {
                    var t = cursor.transform;
                    cursorPath = t?.name ?? "null";
                    if (t?.parent != null) cursorPath = t.parent.name + "/" + cursorPath;
                    if (t?.parent?.parent != null) cursorPath = t.parent.parent.name + "/" + cursorPath;
                }
                catch { cursorPath = "error"; }

                // === BATTLE PAUSE MENU SPECIAL CASE ===
                // Must be checked BEFORE suppression because battle states would suppress it.
                // Cursor path contains "curosr_parent" (game typo) when in pause menu.
                if (cursorPath.Contains("curosr_parent"))
                {
                    MelonLogger.Msg("[CursorNav] Battle pause menu detected - reading directly");
                    CoroutineManager.StartManaged(
                        MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                    );
                    return;
                }

                // === CENTRALIZED STATE CHECKS via CursorSuppressionCheck ===
                // Checks all menu states and returns which one should suppress cursor reading
                var suppressionResult = CursorSuppressionCheck.Check();
                if (suppressionResult.ShouldSuppress)
                {
                    // Special case: popups need button reading
                    if (suppressionResult.IsPopup)
                    {
                        Patches.PopupPatches.ReadCurrentButton(cursor);
                    }
                    // Other states handle their own announcements via patches
                    return;
                }

                // === DEFAULT: Read via MenuTextDiscovery ===
                CoroutineManager.StartManaged(
                    MenuTextDiscovery.WaitAndReadCursor(cursor, "Navigate", 0, false)
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CursorNavigation_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears all menu state flags. Called by transition patches when menus close.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            Patches.EquipMenuState.ClearState();
            Patches.StatusMenuState.ResetState();
            Patches.StatusDetailsState.ResetState();
            Patches.ItemMenuState.ClearState();
            Patches.JobMenuState.ResetState();
            Patches.MagicMenuState.ResetState();
            Patches.ShopMenuTracker.ClearState();
            Patches.ConfigMenuState.ResetState();
            Patches.BattleCommandState.ClearState();
            Patches.BattleTargetPatches.ResetState();
            Patches.BattleItemMenuState.Reset();
            Patches.BattleMagicMenuState.Reset();
            Patches.SaveLoadMenuState.ResetState();
            Patches.PopupState.Clear();
            Patches.EventItemSelectState.ClearState();
        }

        /// <summary>
        /// Clears all menu states except the specified one. Called when a menu activates.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            if (exceptMenu != "Equip") Patches.EquipMenuState.ClearState();
            if (exceptMenu != "Status") Patches.StatusMenuState.ResetState();
            if (exceptMenu != "StatusDetails") Patches.StatusDetailsState.ResetState();
            if (exceptMenu != "Item") Patches.ItemMenuState.ClearState();
            if (exceptMenu != "Job") Patches.JobMenuState.ResetState();
            if (exceptMenu != "Magic") Patches.MagicMenuState.ResetState();
            if (exceptMenu != "Shop") Patches.ShopMenuTracker.ClearState();
            if (exceptMenu != "Config") Patches.ConfigMenuState.ResetState();
            if (exceptMenu != "BattleCommand") Patches.BattleCommandState.ClearState();
            if (exceptMenu != "BattleTarget") Patches.BattleTargetPatches.ResetState();
            if (exceptMenu != "BattleItem") Patches.BattleItemMenuState.Reset();
            if (exceptMenu != "BattleMagic") Patches.BattleMagicMenuState.Reset();
            if (exceptMenu != "SaveLoad") Patches.SaveLoadMenuState.ResetState();
            if (exceptMenu != "Popup") Patches.PopupState.Clear();
            if (exceptMenu != "EventItemSelect") Patches.EventItemSelectState.ClearState();
        }

        /// <summary>
        /// Postfix for FieldTresureBox.Open - triggers entity refresh when chest is opened.
        /// Updates the entity scanner to reflect the chest's new opened state.
        /// </summary>
        public static void TreasureBox_Open_Postfix()
        {
            MelonLogger.Msg("[TreasureBox] Chest opened, scheduling entity refresh");
            FFIII_ScreenReaderMod.Instance?.ScheduleEntityRefresh();
        }
    }
}
