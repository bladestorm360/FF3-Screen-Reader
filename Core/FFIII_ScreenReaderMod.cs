using MelonLoader;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Field;
using UnityEngine;
using HarmonyLib;
using System;
using System.Reflection;
using GameCursor = Il2CppLast.UI.Cursor;
using FieldMap = Il2Cpp.FieldMap;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using FieldMapProvisionInformation = Il2CppLast.Map.FieldMapProvisionInformation;
using FieldPlayerController = Il2CppLast.Map.FieldPlayerController;

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

        // Entity scanning
        private const float ENTITY_SCAN_INTERVAL = 5f;
        private float lastEntityScanTime = 0f;

        // Category count derived from enum for safe cycling
        private static readonly int CategoryCount = System.Enum.GetValues(typeof(EntityCategory)).Length;

        // Current category
        private EntityCategory currentCategory = EntityCategory.All;

        // Pathfinding filter toggle
        private bool filterByPathfinding = false;

        // Map exit filter toggle
        private bool filterMapExits = false;

        // Preferences
        private static MelonPreferences_Category prefsCategory;
        private static MelonPreferences_Entry<bool> prefPathfindingFilter;
        private static MelonPreferences_Entry<bool> prefMapExitFilter;

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("FFIII Screen Reader Mod loaded!");

            // Subscribe to scene load events for automatic component caching
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode>)OnSceneLoaded;

            // Initialize preferences
            prefsCategory = MelonPreferences.CreateCategory("FFIII_ScreenReader");
            prefPathfindingFilter = prefsCategory.CreateEntry<bool>("PathfindingFilter", false, "Pathfinding Filter", "Only show entities with valid paths when cycling");
            prefMapExitFilter = prefsCategory.CreateEntry<bool>("MapExitFilter", false, "Map Exit Filter", "Filter multiple map exits to the same destination, showing only the closest one");

            // Load saved preferences
            filterByPathfinding = prefPathfindingFilter.Value;
            filterMapExits = prefMapExitFilter.Value;

            // Initialize Tolk for screen reader support
            tolk = new TolkWrapper();
            tolk.Load();

            // Initialize input manager
            inputManager = new InputManager(this);

            // Initialize entity scanner with saved preferences
            entityScanner = new EntityScanner();
            entityScanner.FilterByPathfinding = filterByPathfinding;

            // Try manual patching with error handling
            TryManualPatching();
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

            // Patch dialogue methods via MessageWindowManager
            TryPatchDialogue(harmony);

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

            // Patch status details (stat navigation with arrow keys)
            StatusDetailsPatches.ApplyPatches(harmony);

            // Patch movement state changes (vehicle announcements)
            MovementSpeechPatches.ApplyPatches(harmony);

            // Patch landing zone detection ("Can land" announcements)
            VehicleLandingPatches.ApplyPatches(harmony);

            // NOTE: Popup patches disabled - CommonPopup.Open causes crashes in FF3
            // due to string property access issues. Need alternative approach.
            // PopupPatches.ApplyPatches(harmony);

            // Note: Battle patches (BattleCommandPatches, BattleMessagePatches) use
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
        /// Patches dialogue methods using MessageWindowManager.
        /// Avoids string parameters by patching SetContent(List) and Play(bool).
        /// </summary>
        private void TryPatchDialogue(HarmonyLib.Harmony harmony)
        {
            try
            {
                LoggerInstance.Msg("Searching for MessageWindowManager type...");

                // Find the MessageWindowManager type
                Type managerType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.FullName == "Il2CppLast.Message.MessageWindowManager")
                            {
                                LoggerInstance.Msg($"Found MessageWindowManager type: {type.FullName}");
                                managerType = type;
                                break;
                            }
                        }
                        if (managerType != null) break;
                    }
                    catch { }
                }

                if (managerType == null)
                {
                    LoggerInstance.Warning("MessageWindowManager type not found");
                    return;
                }

                // Get postfix methods
                var setContentPostfix = typeof(ManualPatches).GetMethod("SetContent_Postfix", BindingFlags.Public | BindingFlags.Static);
                var playPostfix = typeof(ManualPatches).GetMethod("Play_Postfix", BindingFlags.Public | BindingFlags.Static);

                // Patch SetContent(List<BaseContent>) - for dialogue text
                var setContentMethod = managerType.GetMethod("SetContent", BindingFlags.Public | BindingFlags.Instance);
                if (setContentMethod != null && setContentPostfix != null)
                {
                    harmony.Patch(setContentMethod, postfix: new HarmonyMethod(setContentPostfix));
                    LoggerInstance.Msg("Patched SetContent");
                }
                else
                {
                    LoggerInstance.Warning($"SetContent method or postfix not found. Method: {setContentMethod != null}, Postfix: {setContentPostfix != null}");
                }

                // Patch Play(bool) - for speaker name (read from instance)
                var playMethod = managerType.GetMethod("Play", BindingFlags.Public | BindingFlags.Instance);
                if (playMethod != null && playPostfix != null)
                {
                    harmony.Patch(playMethod, postfix: new HarmonyMethod(playPostfix));
                    LoggerInstance.Msg("Patched Play");
                }
                else
                {
                    LoggerInstance.Warning($"Play method or postfix not found. Method: {playMethod != null}, Postfix: {playPostfix != null}");
                }

                LoggerInstance.Msg("Dialogue patches applied successfully");
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Error patching dialogue: {ex.Message}");
                LoggerInstance.Error($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Patches cursor navigation methods for menu reading.
        /// </summary>
        private void TryPatchCursorNavigation(HarmonyLib.Harmony harmony)
        {
            try
            {
                LoggerInstance.Msg("Searching for Cursor type...");

                // Find the Cursor type
                Type cursorType = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            if (type.FullName == "Il2CppLast.UI.Cursor")
                            {
                                LoggerInstance.Msg($"Found Cursor type: {type.FullName}");
                                cursorType = type;
                                break;
                            }
                        }
                        if (cursorType != null) break;
                    }
                    catch { }
                }

                if (cursorType == null)
                {
                    LoggerInstance.Warning("Cursor type not found");
                    return;
                }

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

                // Clear old cache
                GameObjectCache.Clear<FieldMap>();

                // Delay entity scan to allow scene to fully initialize
                CoroutineManager.StartManaged(DelayedInitialScan());
            }
            catch (System.Exception ex)
            {
                LoggerInstance.Error($"[ComponentCache] Error in OnSceneLoaded: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine that delays entity scanning to allow scene to fully initialize.
        /// </summary>
        private System.Collections.IEnumerator DelayedInitialScan()
        {
            // Wait 0.5 seconds for scene to fully initialize and entities to spawn
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Try to find and cache FieldMap
            try
            {
                var fieldMap = UnityEngine.Object.FindObjectOfType<FieldMap>();
                if (fieldMap != null)
                {
                    GameObjectCache.Register(fieldMap);
                    LoggerInstance.Msg("[ComponentCache] Cached FieldMap");

                    // Initial entity scan
                    entityScanner.ScanEntities();
                    lastEntityScanTime = Time.time;
                    LoggerInstance.Msg($"[ComponentCache] Found {entityScanner.Entities.Count} entities");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Warning($"[ComponentCache] Error caching FieldMap: {ex.Message}");
            }
        }

        public override void OnUpdate()
        {
            // Handle all input
            inputManager.Update();
        }

        internal void AnnounceCurrentEntity()
        {
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
            float currentTime = Time.time;
            if (currentTime - lastEntityScanTime > ENTITY_SCAN_INTERVAL || entityScanner.Entities.Count == 0)
            {
                entityScanner.ScanEntities();
                lastEntityScanTime = currentTime;
            }
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

        internal void CycleNextCategory()
        {
            // Cycle to next category
            int nextCategory = ((int)currentCategory + 1) % CategoryCount;
            currentCategory = (EntityCategory)nextCategory;
            entityScanner.CurrentCategory = currentCategory;

            // Announce new category
            AnnounceCategoryChange();
        }

        internal void CyclePreviousCategory()
        {
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
        public static void SpeakText(string text, bool interrupt = true,
            [System.Runtime.CompilerServices.CallerMemberName] string callerMember = "",
            [System.Runtime.CompilerServices.CallerFilePath] string callerFile = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int callerLine = 0)
        {
            // DEBUG: Log every speech call with source
            string fileName = System.IO.Path.GetFileName(callerFile);
            MelonLogger.Msg($"[SPEECH] \"{text}\" from {fileName}:{callerLine} ({callerMember})");

            tolk?.Speak(text, interrupt);
        }
    }

    /// <summary>
    /// Manual patch methods for Harmony.
    /// </summary>
    public static class ManualPatches
    {
        private static string lastDialogueMessage = "";
        private static string lastSpeaker = "";
        private static string pendingDialogueText = "";  // Stores dialogue until speaker is announced

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

                // === MAIN MENU FALLBACK ===
                // If any menu state flag is set AND we're at main menu level, clear all flags
                // This prevents stuck flags from suppressing main menu speech
                // Short-circuit: only do expensive IsAtMainMenuLevel check if a flag is actually set
                if (AnyMenuStateActive() && IsAtMainMenuLevel())
                {
                    ClearAllMenuStates();
                }

                // === ACTIVE STATE CHECKS ===
                // Only suppress when specific patches are actively handling announcements
                // ShouldSuppress() validates controller is still active (auto-resets stuck flags)

                // Shop menus (buy/sell item lists) - needs price data
                if (Patches.ShopMenuTracker.ValidateState()) return;

                // Item menu (item list) - needs item description
                if (Patches.ItemMenuState.ShouldSuppress()) return;

                // Battle command menu - SetCursor patch handles command announcements
                if (Patches.BattleCommandState.ShouldSuppress()) return;

                // Battle target selection - needs target HP/status
                if (Patches.BattleTargetPatches.ShouldSuppress()) return;

                // Job menu (job list) - needs job level data
                if (Patches.JobMenuState.ShouldSuppress()) return;

                // Status menu character selection - SelectContent_Postfix handles announcements
                if (Patches.StatusMenuState.ShouldSuppress()) return;

                // Equipment menus (slot and item list) - needs stat comparison
                if (Patches.EquipMenuState.ShouldSuppress()) return;

                // Battle item menu - needs item data in battle
                if (Patches.BattleItemMenuState.ShouldSuppress()) return;

                // Battle magic menu - needs spell data with charges in battle
                if (Patches.BattleMagicMenuState.ShouldSuppress()) return;

                // Config menu - needs current setting values
                if (Patches.ConfigMenuState.ShouldSuppress()) return;

                // Magic menu spell list - needs spell data with charges
                if (Patches.MagicMenuState.ShouldSuppress()) return;

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
        /// Fast check if any menu state flag is currently set.
        /// Used to short-circuit the expensive IsAtMainMenuLevel check.
        /// </summary>
        private static bool AnyMenuStateActive()
        {
            return Patches.EquipMenuState.IsActive ||
                   Patches.StatusMenuState.IsActive ||
                   Patches.ItemMenuState.IsItemMenuActive ||
                   Patches.JobMenuState.IsActive ||
                   Patches.MagicMenuState.IsSpellListActive ||
                   Patches.ShopMenuTracker.IsShopMenuActive ||
                   Patches.ConfigMenuState.IsActive ||
                   Patches.BattleCommandState.IsActive ||
                   Patches.BattleTargetPatches.IsTargetSelectionActive ||
                   Patches.BattleItemMenuState.IsActive ||
                   Patches.BattleMagicMenuState.IsActive;
        }

        /// <summary>
        /// Checks if we're at the main menu level (no specialized list controllers active).
        /// Returns true for main menu and command bars (Item/Magic/Equip selection, pre-item menu, etc.)
        /// </summary>
        private static bool IsAtMainMenuLevel()
        {
            try
            {
                // Check if menu is open at all
                Il2CppLast.UI.MenuManager menuManager = null;
                try { menuManager = Il2CppLast.UI.MenuManager.Instance; }
                catch { return false; }

                if (menuManager == null || !menuManager.IsOpen)
                    return false;

                // Check if any specialized list controllers are active
                // If any are active, we're NOT at main menu level

                // Equipment lists
                var equipInfo = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.EquipmentInfoWindowController>();
                if (equipInfo != null && equipInfo.gameObject.activeInHierarchy)
                    return false;

                var equipSelect = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.EquipmentSelectWindowController>();
                if (equipSelect != null && equipSelect.gameObject.activeInHierarchy)
                    return false;

                // Item list
                var itemList = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ItemListController>();
                if (itemList != null && itemList.gameObject.activeInHierarchy)
                    return false;

                // Job list
                var jobList = UnityEngine.Object.FindObjectOfType<Il2CppSerial.FF3.UI.KeyInput.JobChangeWindowController>();
                if (jobList != null && jobList.gameObject.activeInHierarchy)
                    return false;

                // Status character selection
                var statusCharSelect = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.StatusWindowController>();
                if (statusCharSelect != null && statusCharSelect.gameObject.activeInHierarchy)
                    return false;

                // Magic spell list
                var magicList = UnityEngine.Object.FindObjectOfType<Il2CppSerial.FF3.UI.KeyInput.AbilityContentListController>();
                if (magicList != null && magicList.gameObject.activeInHierarchy)
                    return false;

                // Shop item list
                var shopList = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ShopListItemContentController>();
                if (shopList != null && shopList.gameObject.activeInHierarchy)
                    return false;

                // Config menu
                var configMenu = UnityEngine.Object.FindObjectOfType<Il2CppLast.UI.KeyInput.ConfigController>();
                if (configMenu != null && configMenu.gameObject.activeInHierarchy)
                    return false;

                // No specialized controllers active - we're at main menu level
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Clears all menu state flags. Called when returning to main menu level.
        /// </summary>
        public static void ClearAllMenuStates()
        {
            Patches.EquipMenuState.ClearState();
            Patches.StatusMenuState.ResetState();
            Patches.ItemMenuState.ClearState();
            Patches.JobMenuState.ResetState();
            Patches.MagicMenuState.ResetState();
            Patches.ShopMenuTracker.ClearState();
            Patches.ConfigMenuState.ResetState();
            Patches.BattleCommandState.ClearState();
            Patches.BattleTargetPatches.ResetState();
            Patches.BattleItemMenuState.Reset();
            Patches.BattleMagicMenuState.Reset();
        }

        /// <summary>
        /// Clears all menu states except the specified one. Called when a menu activates.
        /// </summary>
        public static void ClearOtherMenuStates(string exceptMenu)
        {
            if (exceptMenu != "Equip") Patches.EquipMenuState.ClearState();
            if (exceptMenu != "Status") Patches.StatusMenuState.ResetState();
            if (exceptMenu != "Item") Patches.ItemMenuState.ClearState();
            if (exceptMenu != "Job") Patches.JobMenuState.ResetState();
            if (exceptMenu != "Magic") Patches.MagicMenuState.ResetState();
            if (exceptMenu != "Shop") Patches.ShopMenuTracker.ClearState();
            if (exceptMenu != "Config") Patches.ConfigMenuState.ResetState();
            if (exceptMenu != "BattleCommand") Patches.BattleCommandState.ClearState();
            if (exceptMenu != "BattleTarget") Patches.BattleTargetPatches.ResetState();
            if (exceptMenu != "BattleItem") Patches.BattleItemMenuState.Reset();
            if (exceptMenu != "BattleMagic") Patches.BattleMagicMenuState.Reset();
        }

        /// <summary>
        /// Postfix for MessageWindowManager.SetContent - reads dialogue text.
        /// Parameter is List of BaseContent, not string, so it should work.
        /// </summary>
        public static void SetContent_Postfix(object __instance)
        {
            try
            {
                // Use AccessTools for IL2CPP compatibility
                var managerType = __instance.GetType();

                // Try to get messageList using AccessTools
                var messageListField = AccessTools.Field(managerType, "messageList");
                if (messageListField == null)
                {
                    // Try property instead
                    var messageListProp = AccessTools.Property(managerType, "messageList");
                    if (messageListProp == null)
                    {
                        return;
                    }
                    var listObj = messageListProp.GetValue(__instance);
                    ReadMessageList(listObj);
                    return;
                }

                var messageList = messageListField.GetValue(__instance);
                ReadMessageList(messageList);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetContent_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads and announces messages from a message list object.
        /// </summary>
        private static void ReadMessageList(object messageListObj)
        {
            if (messageListObj == null)
                return;

            try
            {
                // Get Count property
                var countProp = messageListObj.GetType().GetProperty("Count");
                if (countProp == null)
                    return;

                int count = (int)countProp.GetValue(messageListObj);
                if (count == 0)
                    return;

                // Get indexer
                var indexer = messageListObj.GetType().GetProperty("Item");
                if (indexer == null)
                    return;

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    var msg = indexer.GetValue(messageListObj, new object[] { i }) as string;
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        sb.AppendLine(msg.Trim());
                    }
                }

                string fullText = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(fullText) && fullText != lastDialogueMessage)
                {
                    lastDialogueMessage = fullText;
                    string cleanMessage = TextUtils.StripIconMarkup(fullText);
                    if (!string.IsNullOrWhiteSpace(cleanMessage))
                    {
                        MelonLogger.Msg($"[Dialogue] {cleanMessage}");
                        // Store pending dialogue - will be spoken after speaker name in Play_Postfix
                        pendingDialogueText = cleanMessage;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading message list: {ex.Message}");
            }
        }

        /// <summary>
        /// Postfix for MessageWindowManager.Play - reads speaker name from instance.
        /// Parameter is bool, not string, so it should work.
        /// </summary>
        public static void Play_Postfix(object __instance)
        {
            try
            {
                // Use AccessTools for IL2CPP compatibility
                var managerType = __instance.GetType();

                // Try to access the spekerValue field (note: typo in game code - "speker" not "speaker")
                var spekerField = AccessTools.Field(managerType, "spekerValue");
                if (spekerField == null)
                {
                    // Try property instead
                    var spekerProp = AccessTools.Property(managerType, "spekerValue");
                    if (spekerProp != null)
                    {
                        var spekerValue = spekerProp.GetValue(__instance) as string;
                        AnnounceSpeaker(spekerValue);
                    }
                    return;
                }

                var speaker = spekerField.GetValue(__instance) as string;
                AnnounceSpeaker(speaker);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in Play_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Announces a speaker name if it's new, then announces any pending dialogue.
        /// This ensures speaker name is always spoken before dialogue text.
        /// </summary>
        private static void AnnounceSpeaker(string spekerValue)
        {
            // First, announce speaker if it's new
            if (!string.IsNullOrWhiteSpace(spekerValue) && spekerValue != lastSpeaker)
            {
                lastSpeaker = spekerValue;
                string cleanSpeaker = TextUtils.StripIconMarkup(spekerValue);
                if (!string.IsNullOrWhiteSpace(cleanSpeaker))
                {
                    MelonLogger.Msg($"[Speaker] {cleanSpeaker}");
                    FFIII_ScreenReaderMod.SpeakText(cleanSpeaker, interrupt: false);
                }
            }

            // Then, announce any pending dialogue text
            if (!string.IsNullOrWhiteSpace(pendingDialogueText))
            {
                FFIII_ScreenReaderMod.SpeakText(pendingDialogueText, interrupt: false);
                pendingDialogueText = "";  // Clear after speaking
            }
        }
    }
}
