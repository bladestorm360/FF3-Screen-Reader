using System;
using System.Collections.Generic;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
using FieldEntity = Il2CppLast.Entity.Field.FieldEntity;
using FieldTresureBox = Il2CppLast.Entity.Field.FieldTresureBox;

namespace FFIII_ScreenReader.Field
{
    /// <summary>
    /// Base class for all navigable entities on the field map.
    /// Provides common properties and behavior for entity navigation and pathfinding.
    /// </summary>
    internal abstract class NavigableEntity
    {
        /// <summary>
        /// Reference to the underlying game entity (FF3-specific FieldEntity)
        /// </summary>
        public virtual object GameEntity { get; set; }

        /// <summary>
        /// Current position in world coordinates
        /// </summary>
        public abstract Vector3 Position { get; }

        /// <summary>
        /// Entity name (localized if available)
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Category for filtering purposes
        /// </summary>
        public abstract EntityCategory Category { get; }

        /// <summary>
        /// Priority for deduplication (lower = more important)
        /// </summary>
        public abstract int Priority { get; }

        /// <summary>
        /// Whether this entity blocks pathfinding movement
        /// </summary>
        public abstract bool BlocksPathing { get; }

        /// <summary>
        /// Whether this entity is currently interactive
        /// </summary>
        public virtual bool IsInteractive => true;

        /// <summary>
        /// Whether the underlying game entity is still alive and active in the scene.
        /// Used by the delta scan to prune entities deactivated by events (opened chests'
        /// interaction collider, NPCs despawned mid-cutscene).
        /// </summary>
        public virtual bool IsAlive
        {
            get
            {
                if (GameEntity == null) return false;
                try
                {
                    var fe = GameEntity as FieldEntity;
                    if (fe == null) return false;
                    var go = fe.gameObject;
                    if (go == null) return false;
                    return go.activeInHierarchy;
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// Reads the live position from the backing FieldEntity, or returns the fallback.
        /// </summary>
        protected Vector3 ReadLivePosition(Vector3 fallback)
        {
            try
            {
                var fe = GameEntity as FieldEntity;
                if (fe?.transform != null)
                    return fe.transform.localPosition;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Reads the live isOpen state from a backing FieldTresureBox via reflection on the
        /// private isOpen field. Mirrors EntityScanner.GetTreasureBoxOpenedState.
        /// </summary>
        protected internal static bool ReadTreasureOpened(object gameEntity, bool fallback)
        {
            if (gameEntity == null) return fallback;
            try
            {
                var fe = gameEntity as FieldEntity;
                if (fe == null) return fallback;
                var treasureBox = fe.TryCast<FieldTresureBox>();
                if (treasureBox == null) return fallback;

                var isOpenField = treasureBox.GetType().GetField("isOpen",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (isOpenField != null)
                {
                    var value = isOpenField.GetValue(treasureBox);
                    if (value != null) return (bool)value;
                }

                var isOpenProp = treasureBox.GetType().GetProperty("isOpen");
                if (isOpenProp != null)
                {
                    var value = isOpenProp.GetValue(treasureBox);
                    if (value != null) return (bool)value;
                }
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Gets the display name for this entity (without distance/direction)
        /// </summary>
        protected abstract string GetDisplayName();

        /// <summary>
        /// Gets the entity type name for this entity (e.g., "Treasure Chest", "NPC")
        /// </summary>
        protected abstract string GetEntityTypeName();

        /// <summary>
        /// Formats this entity for screen reader announcement
        /// </summary>
        public virtual string FormatDescription(Vector3 playerPos)
        {
            float distance = Vector3.Distance(playerPos, Position);
            string direction = GetDirection(playerPos, Position);
            return $"{GetDisplayName()} ({FormatSteps(distance)} {direction}) - {GetEntityTypeName()}";
        }

        /// <summary>
        /// Gets cardinal/intercardinal direction from one position to another
        /// </summary>
        protected string GetDirection(Vector3 from, Vector3 to)
        {
            return DirectionHelper.GetDirection(from, to);
        }

        /// <summary>
        /// Helper to format distance in steps
        /// </summary>
        protected string FormatSteps(float distance)
        {
            return DirectionHelper.FormatSteps(distance);
        }

        /// <summary>
        /// The entity's name as spoken (fallback names localized).
        /// </summary>
        public virtual string SpokenName => LocalizeFallbackName(Name);

        /// <summary>
        /// Spoken form of an entity name. The scanner's English fallback names (used when the game gives
        /// no name) stay the internal value, so selection matching and filters are unchanged; only the
        /// announcement is localized. Any other name (the game's own, or a translated label) is returned
        /// as is.
        /// </summary>
        protected static string LocalizeFallbackName(string name)
        {
            switch (name)
            {
                case "Treasure Chest": return T("Treasure Chest");
                case "Save Point": return T("Save Point");
                case "NPC": return T("NPC");
                case "Exit": return T("Exit");
                case "Interactive Object": return T("Interactive Object");
                case "Vehicle": return T("Vehicle");
                default: return name;
            }
        }
    }

    /// <summary>
    /// Represents a treasure chest entity
    /// </summary>
    internal class TreasureChestEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private bool isOpened;

        public TreasureChestEntity(object gameEntity, Vector3 pos, string entityName, bool opened)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            isOpened = opened;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Whether this treasure chest has been opened. Reads live from the game entity
        /// so the delta scan reflects state changes without recreating the wrapper.
        /// </summary>
        public bool IsOpened => ReadTreasureOpened(GameEntity, isOpened);

        public override EntityCategory Category => EntityCategory.Chests;
        public override int Priority => 3;
        public override bool BlocksPathing => true;

        /// <summary>
        /// Opened chests are not interactive
        /// </summary>
        public override bool IsInteractive => !IsOpened;

        protected override string GetDisplayName()
        {
            return string.Format(IsOpened ? T("Opened {0}") : T("Unopened {0}"), LocalizeFallbackName(Name));
        }

        protected override string GetEntityTypeName()
        {
            return T("Treasure Chest");
        }
    }

    /// <summary>
    /// Represents an NPC entity
    /// </summary>
    internal class NPCEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private string assetName;
        private bool isShop;

        public NPCEntity(object gameEntity, Vector3 pos, string entityName, string asset = "", bool shop = false)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            assetName = asset;
            isShop = shop;
        }

        public override Vector3 Position => ReadLivePosition(position);
        public override string Name => name;

        /// <summary>
        /// Asset name used by the game
        /// </summary>
        public string AssetName => assetName;

        /// <summary>
        /// Whether this NPC is a shop
        /// </summary>
        public bool IsShop => isShop;

        public override EntityCategory Category => EntityCategory.NPCs;
        public override int Priority => 4;
        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            var details = new List<string>();

            if (IsShop)
            {
                details.Add(T("shop"));
            }

            string detailStr = details.Count > 0 ? $" ({string.Join(", ", details)})" : "";
            return $"{LocalizeFallbackName(Name)}{detailStr}";
        }

        protected override string GetEntityTypeName()
        {
            return T("NPC");
        }
    }

    /// <summary>
    /// Represents a map exit/transition
    /// </summary>
    internal class MapExitEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private int destinationMapId;
        private string destinationName;

        public MapExitEntity(object gameEntity, Vector3 pos, string entityName, int destMapId, string destName)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            destinationMapId = destMapId;
            destinationName = destName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// Destination map ID
        /// </summary>
        public int DestinationMapId => destinationMapId;

        /// <summary>
        /// Friendly name of destination map
        /// </summary>
        public string DestinationName => destinationName;

        public override EntityCategory Category => EntityCategory.MapExits;
        public override int Priority => 1;
        public override bool BlocksPathing => true;

        protected override string GetDisplayName()
        {
            // Separator restored to "→" (it had been corrupted to U+FFFD + "?")
            string name = LocalizeFallbackName(Name);
            return !string.IsNullOrEmpty(DestinationName)
                ? $"{name} → {DestinationName}"
                : name;
        }

        protected override string GetEntityTypeName()
        {
            return T("Map Exit");
        }
    }

    /// <summary>
    /// Represents a save point
    /// </summary>
    internal class SavePointEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;

        public SavePointEntity(object gameEntity, Vector3 pos, string entityName)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        public override EntityCategory Category => EntityCategory.Events;
        public override int Priority => 2;
        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return LocalizeFallbackName(Name);
        }

        protected override string GetEntityTypeName()
        {
            return T("Save Point");
        }
    }

    /// <summary>
    /// Represents a generic event (teleport, switch event, random event, etc.)
    /// </summary>
    internal class EventEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private string eventTypeName;

        public EventEntity(object gameEntity, Vector3 pos, string entityName, string typeName = "Event")
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            eventTypeName = typeName;
        }

        public override Vector3 Position => position;
        public override string Name => name;

        /// <summary>
        /// The event type name (e.g., "Event", "ToLayer", "Warp Tile")
        /// </summary>
        public string EventTypeName => eventTypeName;

        public override EntityCategory Category => EntityCategory.Events;
        public override int Priority => 8;
        public override bool BlocksPathing => false;

        protected override string GetDisplayName()
        {
            return LocalizeFallbackName(Name);
        }

        protected override string GetEntityTypeName()
        {
            // The raw English type stays the internal key (ToLayerFilter matches "ToLayer")
            switch (eventTypeName)
            {
                case "Event": return T("Event");
                case "Interactive": return T("Interactive");
                case "Interactive Object": return T("Interactive Object");
                default: return eventTypeName;
            }
        }
    }

    /// <summary>
    /// Represents a vehicle (airship, canoe, etc.)
    /// </summary>
    internal class VehicleEntity : NavigableEntity
    {
        private Vector3 position;
        private string name;
        private int transportationId;

        public VehicleEntity(object gameEntity, Vector3 pos, string entityName, int transportId)
        {
            GameEntity = gameEntity;
            position = pos;
            name = entityName;
            transportationId = transportId;
        }

        public override Vector3 Position => ReadLivePosition(position);
        public override string Name => name;

        /// <summary>
        /// Transportation type ID
        /// </summary>
        public int TransportationId => transportationId;

        public override EntityCategory Category => EntityCategory.Vehicles;
        public override int Priority => 10;
        public override bool BlocksPathing => false;

        public override string SpokenName => GetLocalizedVehicleName(TransportationId);

        protected override string GetDisplayName()
        {
            return GetLocalizedVehicleName(TransportationId);
        }

        protected override string GetEntityTypeName()
        {
            return T("Vehicle");
        }

        /// <summary>Spoken vehicle name for a TransportationType (GetVehicleName is the internal key).</summary>
        public static string GetLocalizedVehicleName(int id)
        {
            switch (id)
            {
                case 1: return T("Player");
                case 2: return T("Ship");
                case 3:
                case 7:
                case 8: return T("Airship");
                case 6: return T("Submarine");
                case 0:
                case 4:
                case 5: return T("Vehicle");
                default: return string.Format(T("Vehicle {0}"), id);
            }
        }

        public static string GetVehicleName(int id)
        {
            // FF3 TransportationType enum values (from MapConstants.TransportationType)
            // Note: The enum contains values for all PR games, but FF3 only uses a subset
            switch (id)
            {
                case 0: return "Vehicle";       // None - shouldn't happen
                case 1: return "Player";        // Player - shouldn't be a vehicle
                case 2: return "Ship";          // Ship (Canoe, Viking Ship)
                case 3: return "Airship";       // Plane (Enterprise)
                case 4: return "Vehicle";       // Symbol - internal marker
                case 5: return "Vehicle";       // Content - internal marker
                case 6: return "Submarine";     // Submarine (Nautilus)
                case 7: return "Airship";       // LowFlying
                case 8: return "Airship";       // SpecialPlane (Invincible)
                default: return $"Vehicle {id}";
            }
        }
    }
}
