using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Patches;
using ConfigActualDetailsControllerBase_KeyInput = Il2CppLast.UI.KeyInput.ConfigActualDetailsControllerBase;
using ConfigActualDetailsControllerBase_Touch = Il2CppLast.UI.Touch.ConfigActualDetailsControllerBase;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Manages all keyboard input handling for the screen reader mod.
    /// Uses KeyBindingRegistry for declarative, context-aware dispatch.
    /// </summary>
    internal class InputManager
    {
        private readonly FFIII_ScreenReaderMod mod;
        private readonly KeyBindingRegistry registry = new KeyBindingRegistry();

        public InputManager(FFIII_ScreenReaderMod mod)
        {
            this.mod = mod;
            InitializeBindings();
        }

        private void RegisterFieldWithBattleFeedback(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            registry.Register(key, modifier, KeyContext.Field, action, description);
            registry.Register(key, modifier, KeyContext.Battle, NotAvailableInBattle, description + " (battle blocked)");
        }

        private static void NotAvailableInBattle()
        {
            FFIII_ScreenReaderMod.SpeakText("Not available in battle", interrupt: true);
        }

        private void InitializeBindings()
        {
            // --- Status screen: navigation ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToTop, "Jump to first stat");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToPreviousGroup, "Jump to previous stat group");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigatePrevious, "Previous stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Status, StatusNavigationReader.JumpToBottom, "Jump to last stat");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.Status, StatusNavigationReader.JumpToNextGroup, "Jump to next stat group");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.Status, StatusNavigationReader.NavigateNext, "Next stat");
            registry.Register(KeyCode.R, KeyContext.Status, StatusNavigationReader.ReadCurrentStat, "Repeat current stat");

            // --- Field: entity navigation (brackets + backslash) -- with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldWithBattleFeedback(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldWithBattleFeedback(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldWithBattleFeedback(KeyCode.Backslash, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity");

            // --- Field: alternate keys (J/K/L/P) -- with battle feedback ---
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldWithBattleFeedback(KeyCode.P, KeyModifier.None, mod.AnnounceCurrentEntity, "Announce current entity (alt)");

            // --- Field: waypoint keys ---
            registry.Register(KeyCode.Comma, KeyModifier.Shift, KeyContext.Field, mod.CyclePreviousWaypointCategory, "Previous waypoint category");
            registry.Register(KeyCode.Comma, KeyModifier.None, KeyContext.Field, mod.CyclePreviousWaypoint, "Previous waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Ctrl, KeyContext.Field, mod.RenameCurrentWaypoint, "Rename waypoint");
            registry.Register(KeyCode.Period, KeyModifier.Shift, KeyContext.Field, mod.CycleNextWaypointCategory, "Next waypoint category");
            registry.Register(KeyCode.Period, KeyModifier.None, KeyContext.Field, mod.CycleNextWaypoint, "Next waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.CtrlShift, KeyContext.Field, mod.ClearAllWaypointsForMap, "Clear all waypoints for map");
            registry.Register(KeyCode.Slash, KeyModifier.Ctrl, KeyContext.Field, mod.RemoveCurrentWaypoint, "Remove current waypoint");
            registry.Register(KeyCode.Slash, KeyModifier.Shift, KeyContext.Field, mod.AddNewWaypointWithNaming, "Add waypoint with name");
            registry.Register(KeyCode.Slash, KeyModifier.None, KeyContext.Field, mod.PathfindToCurrentWaypoint, "Pathfind to waypoint");

            // --- Field: teleport (Ctrl+Arrow) ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, 16)), "Teleport north");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(0, -16)), "Teleport south");
            registry.Register(KeyCode.LeftArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(-16, 0)), "Teleport west");
            registry.Register(KeyCode.RightArrow, KeyModifier.Ctrl, KeyContext.Field, () => mod.TeleportInDirection(new Vector2(16, 0)), "Teleport east");

            // --- Global: info/announcements ---
            registry.Register(KeyCode.G, KeyContext.Global, GameInfoAnnouncer.AnnounceGilAmount, "Announce Gil");
            registry.Register(KeyCode.H, KeyContext.Global, GameInfoAnnouncer.AnnounceCharacterStatus, "Announce character status");
            registry.Register(KeyCode.M, KeyModifier.Shift, KeyContext.Global, mod.ToggleMapExitFilter, "Toggle map exit filter");
            registry.Register(KeyCode.M, KeyModifier.None, KeyContext.Global, GameInfoAnnouncer.AnnounceCurrentMap, "Announce current map");
            registry.Register(KeyCode.V, KeyContext.Global, AnnounceVehicleState, "Announce vehicle state");
            registry.Register(KeyCode.I, KeyContext.Global, HandleItemDetailsKey, "Item details");
            registry.Register(KeyCode.Alpha0, KeyContext.Global, DumpUntranslatedEntityNames, "Dump untranslated entity names");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldWithBattleFeedback(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldWithBattleFeedback(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldWithBattleFeedback(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");

            // --- Field-only category shortcuts (blocked in battle with feedback) ---
            RegisterFieldWithBattleFeedback(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldWithBattleFeedback(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldWithBattleFeedback(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
            // Handle modal dialogs first
            if (ConfirmationDialog.HandleInput()) return;
            if (TextInputWindow.HandleInput()) return;
            if (ModMenu.HandleInput()) return;

            if (!Input.anyKeyDown) return;

            // F8 to open mod menu
            if (Input.GetKeyDown(KeyCode.F8) && !ModMenu.IsOpen)
            {
                ModMenu.Open();
                return;
            }

            // Handle function keys (F1/F3/F5 -- special coroutine/battle logic)
            HandleFunctionKeyInput();

            // Skip hotkeys when player is typing in a text field
            if (IsInputFieldFocused()) return;

            // Determine active context and modifiers
            KeyContext activeContext = DetermineContext();
            KeyModifier currentModifiers = GetCurrentModifiers();

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private KeyContext DetermineContext()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive && tracker.ValidateState())
                return KeyContext.Status;

            if (IsInBattle())
                return KeyContext.Battle;

            return KeyContext.Field;
        }

        private static bool IsInBattle()
        {
            return MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_COMMAND) ||
                   MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_TARGET) ||
                   MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_ITEM) ||
                   MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_MAGIC);
        }

        private static KeyModifier GetCurrentModifiers()
        {
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                if (Input.GetKeyDown(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }
        }

        private void HandleFunctionKeyInput()
        {
            // F1 toggles walk/run - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F1))
            {
                CoroutineManager.StartManaged(AnnounceWalkRunState());
                return;
            }

            // F3 toggles encounters - announce after game processes it
            if (Input.GetKeyDown(KeyCode.F3))
            {
                CoroutineManager.StartManaged(AnnounceEncounterState());
                return;
            }

            // F5 to toggle enemy HP display (only when not in battle)
            if (Input.GetKeyDown(KeyCode.F5))
            {
                ToggleEnemyHPDisplay();
            }
        }

        private void AnnounceVehicleState()
        {
            if (!mod.EnsureFieldContext()) return;

            try
            {
                int moveState = MoveStateHelper.GetCurrentMoveState();
                string stateName = MoveStateHelper.GetMoveStateName(moveState);
                FFIII_ScreenReaderMod.SpeakText(stateName, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Vehicle State] Error: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText("Unable to detect vehicle state", interrupt: true);
            }
        }

        private void HandleItemDetailsKey()
        {
            // Check if in config menu
            if (IsConfigMenuActive())
            {
                AnnounceConfigTooltip();
            }
            // Check if in shop menu
            else if (ShopMenuTracker.ValidateState())
            {
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
            }
            // Check if in item menu - announces equipment job requirements
            else if (ItemMenuState.IsItemMenuActive)
            {
                ItemDetailsAnnouncer.AnnounceEquipRequirements();
            }
        }

        private static bool IsConfigMenuActive()
        {
            try
            {
                var keyInputController = GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                    return true;

                var touchController = GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                    return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking config menu state: {ex.Message}");
            }

            return false;
        }

        private static void AnnounceConfigTooltip()
        {
            try
            {
                var keyInputController = GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_KeyInput>();
                if (keyInputController != null && keyInputController.gameObject.activeInHierarchy)
                {
                    var descText = keyInputController.descriptionText;
                    if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                    {
                        FFIII_ScreenReaderMod.SpeakText(descText.text.Trim());
                        return;
                    }
                }

                var touchController = GameObjectCache.GetOrFind<ConfigActualDetailsControllerBase_Touch>();
                if (touchController != null && touchController.gameObject.activeInHierarchy)
                {
                    var descText = touchController.descriptionText;
                    if (descText != null && !string.IsNullOrWhiteSpace(descText.text))
                    {
                        FFIII_ScreenReaderMod.SpeakText(descText.text.Trim());
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading config tooltip: {ex.Message}");
            }
        }

        private void ToggleEnemyHPDisplay()
        {
            if (IsInBattle())
            {
                FFIII_ScreenReaderMod.SpeakText("Unavailable in battle", interrupt: true);
                return;
            }

            int current = PreferencesManager.EnemyHPDisplay;
            int next = (current + 1) % 3;
            PreferencesManager.SetEnemyHPDisplay(next);

            string[] options = { "Numbers", "Percentage", "Hidden" };
            FFIII_ScreenReaderMod.SpeakText($"Enemy HP: {options[next]}", interrupt: true);
        }

        private static void DumpUntranslatedEntityNames()
        {
            try
            {
                string result = EntityTranslator.DumpUntranslatedNames();
                FFIII_ScreenReaderMod.SpeakText(result, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error dumping entity names: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText("Failed to dump entity names", true);
            }
        }

        private static bool IsInputFieldFocused()
        {
            try
            {
                if (EventSystem.current == null)
                    return false;

                var currentObj = EventSystem.current.currentSelectedGameObject;
                if (currentObj == null)
                    return false;

                return currentObj.TryGetComponent(out UnityEngine.UI.InputField inputField);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error checking input field state: {ex.Message}");
                return false;
            }
        }

        private static IEnumerator AnnounceWalkRunState()
        {
            yield return null;
            yield return null;
            yield return null; // Wait 3 frames

            try
            {
                bool isDashing = MoveStateHelper.GetDashFlag();
                string state = isDashing ? "Run" : "Walk";
                FFIII_ScreenReaderMod.SpeakText(state, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F1] Error reading walk/run state: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceEncounterState()
        {
            yield return null; // Wait 1 frame

            try
            {
                var userData = Il2CppLast.Management.UserDataManager.Instance();
                if (userData?.CheatSettingsData != null)
                {
                    bool enabled = userData.CheatSettingsData.IsEnableEncount;
                    string state = enabled ? "Encounters on" : "Encounters off";
                    FFIII_ScreenReaderMod.SpeakText(state, interrupt: true);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[F3] Error reading encounter state: {ex.Message}");
            }
        }
    }
}
