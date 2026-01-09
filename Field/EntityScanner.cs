using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Core.Filters;
using FFIII_ScreenReader.Utils;
using Il2CppLast.Entity.Field;
using Il2CppLast.Map;
using Il2CppLast.Management;
using FieldMap = Il2Cpp.FieldMap;
using GotoMapEventEntity = Il2CppLast.Entity.Field.GotoMapEventEntity;
using EventTriggerEntity = Il2CppLast.Entity.Field.EventTriggerEntity;
using SavePointEventEntity = Il2CppLast.Entity.Field.SavePointEventEntity;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;
using FieldNonPlayer = Il2CppLast.Entity.Field.FieldNonPlayer;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;
using PropertyEntity = Il2CppLast.Map.PropertyEntity;
using PropertyGotoMap = Il2CppLast.Map.PropertyGotoMap;
using PropertyTresureBox = Il2CppLast.Map.PropertyTresureBox;

namespace FFIII_ScreenReader.Field
{
    /// <summary>
    /// Scans the field map for navigable entities and maintains a list of them.
    /// </summary>
    public class EntityScanner
    {
        private List<NavigableEntity> entities = new List<NavigableEntity>();
        private int currentIndex = 0;
        private EntityCategory currentCategory = EntityCategory.All;
        private List<NavigableEntity> filteredEntities = new List<NavigableEntity>();
        private PathfindingFilter pathfindingFilter = new PathfindingFilter();

        // Track selected entity by identifier to maintain focus across re-sorts
        private Vector3? selectedEntityPosition = null;
        private EntityCategory? selectedEntityCategory = null;
        private string selectedEntityName = null;

        /// <summary>
        /// Whether to filter entities by pathfinding accessibility.
        /// When enabled, only entities with a valid path from the player are shown.
        /// </summary>
        public bool FilterByPathfinding
        {
            get => pathfindingFilter.IsEnabled;
            set => pathfindingFilter.IsEnabled = value;
        }

        /// <summary>
        /// Current list of entities (filtered by category)
        /// </summary>
        public List<NavigableEntity> Entities => filteredEntities;

        /// <summary>
        /// Current entity index
        /// </summary>
        public int CurrentIndex
        {
            get => currentIndex;
            set
            {
                currentIndex = value;
                SaveSelectedEntityIdentifier();
            }
        }

        /// <summary>
        /// Saves the current entity's identifier for focus restoration after re-sorting.
        /// </summary>
        private void SaveSelectedEntityIdentifier()
        {
            var entity = CurrentEntity;
            if (entity != null)
            {
                selectedEntityPosition = entity.Position;
                selectedEntityCategory = entity.Category;
                selectedEntityName = entity.Name;
            }
        }

        /// <summary>
        /// Clears the saved entity identifier (used when explicitly resetting selection).
        /// </summary>
        public void ClearSelectedEntityIdentifier()
        {
            selectedEntityPosition = null;
            selectedEntityCategory = null;
            selectedEntityName = null;
        }

        /// <summary>
        /// Finds the index of an entity matching the saved identifier.
        /// Returns -1 if not found.
        /// </summary>
        private int FindEntityByIdentifier()
        {
            if (!selectedEntityPosition.HasValue || !selectedEntityCategory.HasValue)
                return -1;

            for (int i = 0; i < filteredEntities.Count; i++)
            {
                var entity = filteredEntities[i];
                // Match by position (with small tolerance) and category
                if (entity.Category == selectedEntityCategory.Value &&
                    Vector3.Distance(entity.Position, selectedEntityPosition.Value) < 0.5f)
                {
                    return i;
                }
            }

            // Fallback: try matching by name if position changed slightly
            if (!string.IsNullOrEmpty(selectedEntityName))
            {
                for (int i = 0; i < filteredEntities.Count; i++)
                {
                    var entity = filteredEntities[i];
                    if (entity.Category == selectedEntityCategory.Value &&
                        entity.Name == selectedEntityName)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Current category filter
        /// </summary>
        public EntityCategory CurrentCategory
        {
            get => currentCategory;
            set
            {
                if (currentCategory != value)
                {
                    currentCategory = value;
                    ClearSelectedEntityIdentifier(); // Clear since we're changing category
                    ApplyFilter();
                    currentIndex = 0;
                }
            }
        }

        /// <summary>
        /// Currently selected entity
        /// </summary>
        public NavigableEntity CurrentEntity
        {
            get
            {
                if (filteredEntities.Count == 0 || currentIndex < 0 || currentIndex >= filteredEntities.Count)
                    return null;
                return filteredEntities[currentIndex];
            }
        }

        /// <summary>
        /// Scans the field for all navigable entities.
        /// </summary>
        public void ScanEntities()
        {
            entities.Clear();

            try
            {
                var fieldEntities = FieldNavigationHelper.GetAllFieldEntities();
                MelonLogger.Msg($"[EntityScanner] Found {fieldEntities.Count} field entities");

                foreach (var fieldEntity in fieldEntities)
                {
                    try
                    {
                        var navigable = ConvertToNavigableEntity(fieldEntity);
                        if (navigable != null)
                        {
                            entities.Add(navigable);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"Error converting entity: {ex.Message}");
                    }
                }

                MelonLogger.Msg($"[EntityScanner] Converted {entities.Count} navigable entities");

                // Re-apply filter after scanning
                ApplyFilter();

                // Reset index if out of bounds
                if (currentIndex >= filteredEntities.Count)
                    currentIndex = 0;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[EntityScanner] Error scanning entities: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies the current category filter.
        /// </summary>
        private void ApplyFilter()
        {
            if (currentCategory == EntityCategory.All)
            {
                filteredEntities = new List<NavigableEntity>(entities);
            }
            else
            {
                filteredEntities = entities.Where(e => e.Category == currentCategory).ToList();
            }

            // Sort by distance from player
            var playerPos = GetPlayerPosition();
            if (playerPos.HasValue)
            {
                filteredEntities = filteredEntities.OrderBy(e => Vector3.Distance(e.Position, playerPos.Value)).ToList();
            }

            // Restore focus to previously selected entity after re-sorting
            int restoredIndex = FindEntityByIdentifier();
            if (restoredIndex >= 0)
            {
                currentIndex = restoredIndex;
            }
        }

        /// <summary>
        /// Gets the current player position.
        /// </summary>
        private Vector3? GetPlayerPosition()
        {
            try
            {
                // Use FieldPlayerController like FF5 does
                var playerController = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
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
        /// Gets the FieldPlayer from the FieldController using reflection.
        /// The player field is private in the main game's FieldController.
        /// </summary>
        private FieldPlayer GetFieldPlayer()
        {
            try
            {
                var fieldMap = GameObjectCache.Get<FieldMap>();
                if (fieldMap?.fieldController == null)
                    return null;

                // Access private 'player' field using reflection
                var fieldType = fieldMap.fieldController.GetType();
                var playerField = fieldType.GetField("player", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (playerField != null)
                {
                    return playerField.GetValue(fieldMap.fieldController) as FieldPlayer;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Moves to the next entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void NextEntity()
        {
            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                SaveSelectedEntityIdentifier();
                return;
            }

            // With pathfinding filter, find next entity with valid path
            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex + 1) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return; // Found a valid entity
                }
            }

            // No reachable entities found, stay at original position
            currentIndex = startIndex;
            MelonLogger.Msg("[EntityScanner] No reachable entities found with pathfinding filter");
        }

        /// <summary>
        /// Moves to the previous entity.
        /// If pathfinding filter is enabled, skips entities without valid paths.
        /// </summary>
        public void PreviousEntity()
        {
            if (filteredEntities.Count == 0)
            {
                ScanEntities();
                if (filteredEntities.Count == 0)
                    return;
            }

            if (!pathfindingFilter.IsEnabled)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                SaveSelectedEntityIdentifier();
                return;
            }

            // With pathfinding filter, find previous entity with valid path
            var context = new FilterContext();
            int attempts = 0;
            int startIndex = currentIndex;

            while (attempts < filteredEntities.Count)
            {
                currentIndex = (currentIndex - 1 + filteredEntities.Count) % filteredEntities.Count;
                attempts++;

                var entity = filteredEntities[currentIndex];
                if (pathfindingFilter.PassesFilter(entity, context))
                {
                    SaveSelectedEntityIdentifier();
                    return; // Found a valid entity
                }
            }

            // No reachable entities found, stay at original position
            currentIndex = startIndex;
            MelonLogger.Msg("[EntityScanner] No reachable entities found with pathfinding filter");
        }

        /// <summary>
        /// Converts a FieldEntity to a NavigableEntity.
        /// </summary>
        private NavigableEntity ConvertToNavigableEntity(FieldEntity fieldEntity)
        {
            if (fieldEntity == null)
                return null;

            // Use localPosition like FF5 does for pathfinding compatibility
            Vector3 position = fieldEntity.transform.localPosition;
            string typeName = fieldEntity.GetType().Name;
            string goName = "";

            try
            {
                goName = fieldEntity.gameObject.name ?? "";
            }
            catch { }

            string goNameLower = goName.ToLower();

            // Skip the player entity
            if (typeName.Contains("FieldPlayer") || goNameLower.Contains("player"))
                return null;

            // Skip inactive objects
            try
            {
                if (!fieldEntity.gameObject.activeInHierarchy)
                    return null;
            }
            catch { }

            // Check for GotoMapEventEntity (map exits) by type casting first - most reliable
            var gotoMapEvent = fieldEntity.TryCast<GotoMapEventEntity>();
            if (gotoMapEvent != null)
            {
                string destName = GetGotoMapDestination(gotoMapEvent);
                MelonLogger.Msg($"[EntityScanner] Found GotoMapEventEntity: '{goName}' -> '{destName}'");
                return new MapExitEntity(fieldEntity, position, "Exit", 0, destName);
            }

            // Check for FieldTresureBox (treasure chests) by type casting
            var treasureBox = fieldEntity.TryCast<FieldTresureBox>();
            if (treasureBox != null)
            {
                bool isOpened = GetTreasureBoxOpenedState(treasureBox);
                string name = CleanObjectName(goName, "Treasure Chest");
                MelonLogger.Msg($"[EntityScanner] Found FieldTresureBox: '{goName}' isOpen={isOpened}");
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // Check for SavePointEventEntity by type casting
            var savePointEvent = fieldEntity.TryCast<SavePointEventEntity>();
            if (savePointEvent != null)
            {
                return new SavePointEntity(fieldEntity, position, "Save Point");
            }

            // Check for FieldNonPlayer (NPCs) by type casting
            var fieldNonPlayer = fieldEntity.TryCast<FieldNonPlayer>();
            if (fieldNonPlayer != null)
            {
                // Try to get localized name from PropertyEntity first
                string name = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(name))
                {
                    name = CleanObjectName(goName, "NPC");
                }
                bool isShop = goNameLower.Contains("shop") || goNameLower.Contains("merchant");
                MelonLogger.Msg($"[EntityScanner] Found FieldNonPlayer: '{name}' isShop={isShop}");
                return new NPCEntity(fieldEntity, position, name, "", isShop);
            }

            // Check for treasure chest by name/type
            if (typeName.Contains("Treasure") || goNameLower.Contains("treasure") ||
                goNameLower.Contains("chest") || goNameLower.Contains("box"))
            {
                string name = CleanObjectName(goName, "Treasure Chest");
                bool isOpened = CheckIfTreasureOpened(fieldEntity);
                return new TreasureChestEntity(fieldEntity, position, name, isOpened);
            }

            // Fallback: Check for map exit/door by name patterns
            if (typeName.Contains("MapChange") || typeName.Contains("Door") ||
                goNameLower.Contains("exit") || goNameLower.Contains("door") ||
                goNameLower.Contains("entrance") || goNameLower.Contains("gate") ||
                goNameLower.Contains("stairs") || goNameLower.Contains("ladder"))
            {
                string destName = GetMapExitDestinationFromProperty(fieldEntity);
                return new MapExitEntity(fieldEntity, position, "Exit", 0, destName);
            }

            // Fallback: Check for save point by name patterns
            if (typeName.Contains("Save") || goNameLower.Contains("save"))
            {
                return new SavePointEntity(fieldEntity, position, "Save Point");
            }

            // Check for interactive objects / event triggers
            var eventTrigger = fieldEntity.TryCast<EventTriggerEntity>();
            if (eventTrigger != null)
            {
                // Try to get name from PropertyEntity.Name
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                {
                    entityName = CleanObjectName(goName, "Interactive Object");
                }
                return new EventEntity(fieldEntity, position, entityName, "Event");
            }

            var interactiveEntity = fieldEntity.TryCast<IInteractiveEntity>();
            if (interactiveEntity != null)
            {
                // Try to get name from PropertyEntity.Name
                string entityName = GetEntityNameFromProperty(fieldEntity);
                if (string.IsNullOrEmpty(entityName))
                {
                    entityName = CleanObjectName(goName, "Interactive Object");
                }
                return new EventEntity(fieldEntity, position, entityName, "Interactive");
            }

            // Check for transportation by type name
            if (typeName.Contains("Transport") || goNameLower.Contains("ship") ||
                goNameLower.Contains("canoe") || goNameLower.Contains("airship"))
            {
                string vehicleName = CleanObjectName(goName, "Vehicle");
                return new VehicleEntity(fieldEntity, position, vehicleName, 0);
            }

            // Skip unidentifiable entities
            return null;
        }

        /// <summary>
        /// Cleans up an object name for display.
        /// </summary>
        private string CleanObjectName(string name, string defaultName)
        {
            if (string.IsNullOrWhiteSpace(name))
                return defaultName;

            // Remove common suffixes
            name = name.Replace("(Clone)", "").Trim();

            // If name is just numbers or very short, use default
            if (name.Length < 2 || name.All(c => char.IsDigit(c) || c == '_'))
                return defaultName;

            // If name starts with underscore, use default
            if (name.StartsWith("_"))
                return defaultName;

            // Handle generic field object names - use the default name instead
            string nameLower = name.ToLower();
            if (nameLower.StartsWith("field") &&
                (nameLower.Contains("object") || nameLower.Contains("default") ||
                 nameLower.Contains("anim") || nameLower.Contains("sprite")))
            {
                return defaultName;
            }

            // Handle "GotoMap" - these are map exits
            if (nameLower.StartsWith("gotomap"))
                return "Exit";

            // Handle generic prefab names
            if (nameLower.Contains("prefab") || nameLower.Contains("instance"))
                return defaultName;

            return name;
        }

        /// <summary>
        /// Gets the opened state from a FieldTresureBox entity.
        /// Reads the private isOpen field directly.
        /// </summary>
        private bool GetTreasureBoxOpenedState(FieldTresureBox treasureBox)
        {
            try
            {
                // FieldTresureBox has a private "isOpen" field at offset 0x169
                var isOpenField = treasureBox.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (isOpenField != null)
                {
                    return (bool)isOpenField.GetValue(treasureBox);
                }

                // Fallback: try property access
                var isOpenProp = treasureBox.GetType().GetProperty("isOpen");
                if (isOpenProp != null)
                {
                    return (bool)isOpenProp.GetValue(treasureBox);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[TreasureBox] Error getting isOpen: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Checks if a treasure entity has been opened (fallback for non-FieldTresureBox entities).
        /// </summary>
        private bool CheckIfTreasureOpened(FieldEntity fieldEntity)
        {
            try
            {
                // Try to check if it has an "isOpened" or "isOpen" property
                var prop = fieldEntity.GetType().GetProperty("isOpened") ??
                           fieldEntity.GetType().GetProperty("isOpen");
                if (prop != null)
                {
                    return (bool)prop.GetValue(fieldEntity);
                }

                // Try field access
                var field = fieldEntity.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null)
                {
                    return (bool)field.GetValue(fieldEntity);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Gets the destination map name for a GotoMapEventEntity.
        /// Reads PropertyGotoMap.MapId and resolves to localized area name.
        /// </summary>
        private string GetGotoMapDestination(GotoMapEventEntity gotoMapEvent)
        {
            try
            {
                // GotoMapEventEntity inherits from FieldEntity which has Property
                // Cast to FieldEntity to access the Property directly (IL2CPP doesn't work with reflection)
                var fieldEntity = gotoMapEvent.TryCast<FieldEntity>();
                if (fieldEntity == null)
                {
                    MelonLogger.Msg("[MapExit] Failed to cast to FieldEntity");
                    return "";
                }

                // Access the Property directly on the IL2CPP object
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                {
                    MelonLogger.Msg("[MapExit] Property is null");
                    return "";
                }

                // Log the actual type for debugging
                string actualType = property.GetType().FullName;
                MelonLogger.Msg($"[MapExit] Property type: {actualType}");

                // Try to cast to PropertyGotoMap which has MapId
                var gotoMapProperty = property.TryCast<PropertyGotoMap>();
                if (gotoMapProperty != null)
                {
                    int mapId = gotoMapProperty.MapId;
                    string assetName = gotoMapProperty.AssetName;
                    MelonLogger.Msg($"[MapExit] PropertyGotoMap - MapId={mapId}, AssetName={assetName}");

                    if (mapId > 0)
                    {
                        string mapName = MapNameResolver.GetMapExitName(mapId);
                        if (!string.IsNullOrEmpty(mapName))
                        {
                            return mapName;
                        }
                        return $"Map {mapId}";
                    }

                    // If MapId is 0 but we have AssetName, use that
                    if (!string.IsNullOrEmpty(assetName))
                    {
                        return FormatAssetNameAsMapName(assetName);
                    }
                }
                else
                {
                    MelonLogger.Msg("[MapExit] Failed to cast Property to PropertyGotoMap");

                    // Fallback: try to read TmeId from PropertyEntity
                    int tmeId = property.TmeId;
                    MelonLogger.Msg($"[MapExit] Fallback TmeId = {tmeId}");
                    if (tmeId > 0)
                    {
                        string mapName = MapNameResolver.GetMapExitName(tmeId);
                        if (!string.IsNullOrEmpty(mapName))
                        {
                            return mapName;
                        }
                        return $"Map {tmeId}";
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapExit] Error getting destination: {ex.Message}");
            }

            return "";
        }

        /// <summary>
        /// Gets the entity name from PropertyEntity.Name for event/interactive entities.
        /// Falls back to trying to resolve via MessageManager if name looks like a message ID.
        /// </summary>
        private string GetEntityNameFromProperty(Il2CppLast.Entity.Field.FieldEntity fieldEntity)
        {
            try
            {
                // Access Property directly on the IL2CPP object
                PropertyEntity property = fieldEntity.Property;
                if (property == null)
                {
                    return null;
                }

                // Get the Name property
                string name = property.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                MelonLogger.Msg($"[EntityScanner] PropertyEntity.Name = '{name}'");

                // Check if name looks like a message ID (e.g., starts with "mes_" or similar patterns)
                if (name.StartsWith("mes_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("sys_", StringComparison.OrdinalIgnoreCase) ||
                    name.StartsWith("field_", StringComparison.OrdinalIgnoreCase))
                {
                    // Try to resolve via MessageManager
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null)
                    {
                        string localizedName = messageManager.GetMessage(name, false);
                        if (!string.IsNullOrWhiteSpace(localizedName) && localizedName != name)
                        {
                            MelonLogger.Msg($"[EntityScanner] Resolved '{name}' -> '{localizedName}'");
                            return localizedName;
                        }
                    }
                }

                // If name looks like a readable name (not a code), use it directly
                if (!name.Contains("_") && !name.All(c => char.IsLower(c)))
                {
                    return name;
                }

                // Try to format underscore-separated names into readable form
                // e.g., "recovery_spring" -> "Recovery Spring"
                if (name.Contains("_"))
                {
                    string formatted = FormatAssetNameAsReadable(name);
                    if (!string.IsNullOrEmpty(formatted))
                    {
                        return formatted;
                    }
                }

                return name;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[EntityScanner] Error getting entity name: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Formats an underscore-separated name into a readable form.
        /// e.g., "recovery_spring" -> "Recovery Spring"
        /// </summary>
        private string FormatAssetNameAsReadable(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "";

            // Replace underscores with spaces and title case
            string[] parts = name.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Formats an asset name into a readable map name.
        /// e.g., "altar_cave_1f" -> "Altar Cave 1F"
        /// </summary>
        private string FormatAssetNameAsMapName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
                return "";

            // Replace underscores with spaces and title case
            string[] parts = assetName.Split('_');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    // Check if it's a floor indicator (1f, 2f, b1, etc.)
                    if (parts[i].Length <= 3 && (parts[i].EndsWith("f") || parts[i].StartsWith("b")))
                    {
                        parts[i] = parts[i].ToUpper();
                    }
                    else
                    {
                        // Title case
                        parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLower();
                    }
                }
            }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// Gets the destination name for a map exit from its Property (fallback method).
        /// </summary>
        private string GetMapExitDestinationFromProperty(FieldEntity fieldEntity)
        {
            int destMapId = -1;

            try
            {
                var entityType = fieldEntity.GetType();

                // Try MapId from Property object (as PropertyGotoMap)
                var propertyProp = entityType.GetProperty("Property");
                if (propertyProp != null)
                {
                    var propertyObj = propertyProp.GetValue(fieldEntity);
                    if (propertyObj != null)
                    {
                        // Try MapId first (PropertyGotoMap)
                        var mapIdProp = propertyObj.GetType().GetProperty("MapId");
                        if (mapIdProp != null)
                        {
                            destMapId = Convert.ToInt32(mapIdProp.GetValue(propertyObj));
                        }
                        // Fallback to TmeId (PropertyEntity)
                        else
                        {
                            var tmeIdProp = propertyObj.GetType().GetProperty("TmeId");
                            if (tmeIdProp != null)
                            {
                                destMapId = Convert.ToInt32(tmeIdProp.GetValue(propertyObj));
                            }
                        }
                    }
                }

                // Resolve map ID to name using MapNameResolver
                if (destMapId > 0)
                {
                    string mapName = MapNameResolver.GetMapExitName(destMapId);
                    if (!string.IsNullOrEmpty(mapName))
                    {
                        MelonLogger.Msg($"[MapExit] Resolved MapId {destMapId} -> '{mapName}'");
                        return mapName;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MapExit] Error getting destination: {ex.Message}");
            }

            return "";
        }
    }
}
