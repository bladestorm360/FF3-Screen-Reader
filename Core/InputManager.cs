using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using MelonLoader;
using FFIII_ScreenReader.Utils;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Patches;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
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

        private void RegisterFieldOnly(KeyCode key, KeyModifier modifier, Action action, string description)
        {
            // Field-only action. Off-field (menu/battle/title) the active context is never
            // Field, so this binding has no match and dispatch silently does nothing.
            registry.Register(key, modifier, KeyContext.Field, action, description);
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

            // --- Bestiary detail: navigation ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToTop, "Jump to first stat (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToPreviousGroup, "Jump to previous group (bestiary)");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigatePrevious, "Previous stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToBottom, "Jump to last stat (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.Shift, KeyContext.BestiaryDetail, BestiaryNavigationReader.JumpToNextGroup, "Jump to next group (bestiary)");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.BestiaryDetail, BestiaryNavigationReader.NavigateNext, "Next stat (bestiary)");

            // --- Controls pop-up (config Gamepad/Keyboard Controls): flat list navigation ---
            registry.Register(KeyCode.UpArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToTop, "Jump to first control");
            registry.Register(KeyCode.UpArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigatePrevious, "Previous control");
            registry.Register(KeyCode.DownArrow, KeyModifier.Ctrl, KeyContext.KeyHelp, KeyHelpReader.JumpToBottom, "Jump to last control");
            registry.Register(KeyCode.DownArrow, KeyModifier.None, KeyContext.KeyHelp, KeyHelpReader.NavigateNext, "Next control");

            // --- Field: entity navigation (brackets + backslash) -- with battle feedback ---
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category");
            RegisterFieldOnly(KeyCode.LeftBracket, KeyModifier.None, mod.CyclePrevious, "Previous entity");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category");
            RegisterFieldOnly(KeyCode.RightBracket, KeyModifier.None, mod.CycleNext, "Next entity");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter");
            RegisterFieldOnly(KeyCode.Backslash, KeyModifier.None, AnnounceOrRestartEntityBeacon, "Announce current entity / restart beacon");

            // --- Field: manual entity rescan (backtick) ---
            RegisterFieldOnly(KeyCode.BackQuote, KeyModifier.None, mod.ManualEntityRescan, "Force entity rescan");

            // --- Field: alternate keys (J/K/L/P) -- with battle feedback ---
            RegisterFieldOnly(KeyCode.J, KeyModifier.Shift, mod.CyclePreviousCategory, "Previous entity category (alt)");
            RegisterFieldOnly(KeyCode.J, KeyModifier.None, mod.CyclePrevious, "Previous entity (alt)");
            RegisterFieldOnly(KeyCode.K, KeyModifier.None, mod.AnnounceEntityOnly, "Announce entity name (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.Shift, mod.CycleNextCategory, "Next entity category (alt)");
            RegisterFieldOnly(KeyCode.L, KeyModifier.None, mod.CycleNext, "Next entity (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Ctrl, mod.ToggleToLayerFilter, "Toggle layer filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.Shift, mod.TogglePathfindingFilter, "Toggle pathfinding filter (alt)");
            RegisterFieldOnly(KeyCode.P, KeyModifier.None, AnnounceOrRestartEntityBeacon, "Announce current entity / restart beacon (alt)");

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
            registry.Register(KeyCode.Tab, KeyContext.Global, HandleTabKey, "Clear battle state fallback");
            registry.Register(KeyCode.V, KeyContext.Global, AnnounceVehicleState, "Announce vehicle state");

            // --- Global: R repeats the current dialogue page (the Status binding above takes precedence) ---
            registry.Register(KeyCode.R, KeyContext.Global, () =>
            {
                if (DialogueTracker.IsInDialogue)
                    DialogueTracker.RepeatLastPage();
            }, "Repeat dialogue");
            registry.Register(KeyCode.I, KeyModifier.Shift, KeyContext.Global, KeyHelpReader.AnnounceKeyHelp, "Announce key help controls");
            registry.Register(KeyCode.I, KeyModifier.None, KeyContext.Global, HandleItemDetailsKey, "Item details");
            registry.Register(KeyCode.U, KeyContext.Global, UsableByAnnouncer.AnnounceForCurrentContext, "Usable by jobs");
            registry.Register(KeyCode.Alpha0, KeyContext.Global, DumpUntranslatedEntityNames, "Dump untranslated entity names");

            // --- Field-only toggles (blocked in battle with feedback) ---
            RegisterFieldOnly(KeyCode.Quote, KeyModifier.None, mod.ToggleFootsteps, "Toggle footsteps");
            RegisterFieldOnly(KeyCode.Semicolon, KeyModifier.None, mod.ToggleWallTones, "Toggle wall tones");
            RegisterFieldOnly(KeyCode.F6, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons");
            RegisterFieldOnly(KeyCode.Alpha9, KeyModifier.None, mod.ToggleAudioBeacons, "Toggle audio beacons (alt)");

            // --- Field-only category shortcuts (blocked in battle with feedback) ---
            RegisterFieldOnly(KeyCode.K, KeyModifier.Shift, mod.ResetToAllCategory, "Reset to All category");
            RegisterFieldOnly(KeyCode.Equals, KeyModifier.None, mod.CycleNextCategory, "Next entity category (global)");
            RegisterFieldOnly(KeyCode.Minus, KeyModifier.None, mod.CyclePreviousCategory, "Previous entity category (global)");

            // Sort for correct modifier precedence
            registry.FinalizeRegistration();
        }

        public void Update()
        {
            // Poll SDL3 gamepad + GetAsyncKeyState keyboard once per frame.
            // Must come before any mod input handling so edge-detection state is fresh.
            GamepadManager.Update();

            // Suppress Unity legacy Input when the mod is consuming. Safe because the mod reads
            // keyboard via GetAsyncKeyState (unaffected by ResetInputAxes). This + the
            // InputPassthroughPatches = complete game keyboard suppression, and it works even
            // when no gamepad is connected (the passthrough patches early-return without one).
            if (ControllerRouter.SuppressGameInput)
                Input.ResetInputAxes();

            // Determine context AFTER polling so the router (and dispatch below) sees fresh
            // input for this frame.
            KeyContext activeContext = DetermineContext();

            // Route controller inputs to the appropriate state-machine bucket. Runs every frame
            // so the router can interrupt speech / drive nav even without a gamepad.
            ControllerRouter.Update(activeContext);

            // Per-tile footstep check (field-active gated, silent in vehicles). Runs after
            // ControllerRouter.Update so IsFieldActive is fresh for this frame.
            MovementSoundPatches.PollFootsteps();

            if (GamepadManager.AnyKeyboardKeyDown())
                ControllerRouter.NotifyKeyboardInput();

            // Handle modal dialogs first (each consumes all input when open)
            if (ConfirmationDialog.HandleInput()) return;
            if (TextInputWindow.HandleInput()) return;
            if (ModMenu.HandleInput()) return;

            // Game-context hotkeys below only fire when the game window is the foreground
            // window, so mod functions don't trigger while the player is in another app.
            // Placed AFTER the modals so the now-virtual dialogs/menu keep working even when
            // the game window isn't foreground.
            if (!WindowsFocusHelper.IsGameWindowFocused())
                return;

            if (!GamepadManager.AnyKeyboardKeyDown())
                return;

            // Skip ALL mod hotkeys (including F8 and the function keys) while the player is
            // typing in the game's own text field, so naming/input screens aren't disrupted.
            if (IsInputFieldFocused()) return;

            // Bare F-keys only fire with no modifier held, so OS shortcuts like Alt+F4
            // (close window), Ctrl+F-keys and Shift+F-keys don't trigger the screen
            // reader. Explicit Shift/Ctrl bindings still match via GetCurrentModifiers.
            bool anyModifierHeld = IsAnyModifierHeld();

            // F8 to open mod menu — gated to field-only via ControllerRouter.IsFieldActive
            // (blocks battle, in-game menus, title screen). Rejection wording lives in
            // ControllerRouter.SpeakModMenuUnavailable so Start-button and F8 stay in sync.
            if (!anyModifierHeld && GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                if (ControllerRouter.IsFieldActive)
                    ModMenu.Open();
                else
                    ControllerRouter.SpeakModMenuUnavailable();
                return;
            }

            // Handle function keys (F5 enemy HP display, F7 Auto Detail) — bare keypress only
            if (!anyModifierHeld)
                HandleFunctionKeyInput();

            KeyModifier currentModifiers = GetCurrentModifiers();

            // Alt held with no registered Alt-binding → skip dispatch so Alt+<key> doesn't
            // accidentally trigger the unmodified binding. (Shift/Ctrl are routed through
            // currentModifiers and matched exactly by the registry, so they still work.)
            if (IsAltHeld())
                return;

            // Dispatch all registered bindings
            DispatchRegisteredBindings(activeContext, currentModifiers);
        }

        private static bool IsAltHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private static bool IsAnyModifierHeld()
        {
            return GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl)
                || GamepadManager.IsKeyCodeHeld(KeyCode.LeftAlt)
                || GamepadManager.IsKeyCodeHeld(KeyCode.RightAlt);
        }

        private KeyContext DetermineContext()
        {
            // The config controls pop-up takes priority while shown.
            if (KeyHelpReader.IsScreenActive)
                return KeyContext.KeyHelp;

            var tracker = StatusNavigationTracker.Instance;
            if (tracker.IsNavigationActive && tracker.ValidateState())
                return KeyContext.Status;

            var bestiaryTracker = BestiaryNavigationTracker.Instance;
            if (bestiaryTracker.IsNavigationActive && bestiaryTracker.ValidateState())
                return KeyContext.BestiaryDetail;

            if (BattleStateHelper.IsInBattle)
                return KeyContext.Battle;

            // Field keys only fire while actively on a field map with no menu open.
            // Otherwise fall through to Global so field/entity/waypoint/toggle hotkeys
            // are silent no-ops off-field, while Global info keys still work everywhere.
            if (IsOnValidMap() && !MenuStateRegistry.AnyActive())
                return KeyContext.Field;

            return KeyContext.Global;
        }

        // Frame of the last cache-miss rescan in IsOnValidMap (see there).
        private static int lastRefreshFrame = -1000;
        private const int REFRESH_ON_MISS_INTERVAL_FRAMES = 30;

        private static bool IsOnValidMap()
        {
            // Self-heal the cache so a cleared or stale entry can't wedge the field context into
            // Global and silently disable field hotkeys. DetermineContext runs every frame, and
            // off-field (title, intro, credits, Extras) there is no FieldPlayerController to find,
            // so the rescan on a miss is throttled: at most one scene scan every 30 frames.
            try
            {
                var pc = GameObjectCache.Get<Il2CppLast.Map.FieldPlayerController>();
                if (pc == null && Time.frameCount - lastRefreshFrame >= REFRESH_ON_MISS_INTERVAL_FRAMES)
                {
                    lastRefreshFrame = Time.frameCount;
                    pc = GameObjectCache.Refresh<Il2CppLast.Map.FieldPlayerController>();
                }
                return pc?.fieldPlayer != null;
            }
            catch { }
            return false;
        }

        private static KeyModifier GetCurrentModifiers()
        {
            bool shift = GamepadManager.IsKeyCodeHeld(KeyCode.LeftShift) || GamepadManager.IsKeyCodeHeld(KeyCode.RightShift);
            bool ctrl = GamepadManager.IsKeyCodeHeld(KeyCode.LeftControl) || GamepadManager.IsKeyCodeHeld(KeyCode.RightControl);

            if (ctrl && shift) return KeyModifier.CtrlShift;
            if (ctrl) return KeyModifier.Ctrl;
            if (shift) return KeyModifier.Shift;
            return KeyModifier.None;
        }

        private void DispatchRegisteredBindings(KeyContext activeContext, KeyModifier currentModifiers)
        {
            foreach (var key in registry.RegisteredKeys)
            {
                // Read via SDL/GetAsyncKeyState (hardware state) so hotkeys are unaffected by
                // Input.ResetInputAxes suppression and work regardless of gamepad presence.
                if (GamepadManager.IsKeyCodePressed(key))
                    registry.TryExecute(key, currentModifiers, activeContext);
            }

            // WASD as alternative arrow keys — ONLY in navigation-buffer contexts, so game WASD
            // movement and letter hotkeys elsewhere are untouched. Reuses the arrow bindings, so
            // modifiers carry (Shift+W = Shift+Up = previous group).
            if (IsBufferContext(activeContext))
            {
                if (GamepadManager.IsKeyCodePressed(KeyCode.W)) registry.TryExecute(KeyCode.UpArrow, currentModifiers, activeContext);
                if (GamepadManager.IsKeyCodePressed(KeyCode.S)) registry.TryExecute(KeyCode.DownArrow, currentModifiers, activeContext);
            }
        }

        private static bool IsBufferContext(KeyContext ctx)
            => ctx == KeyContext.Status || ctx == KeyContext.BestiaryDetail || ctx == KeyContext.KeyHelp;

        /// <summary>
        /// \ and P: with beacon navigation on, re-target and re-ping the beacon; otherwise speak the
        /// path to the selected entity.
        /// </summary>
        private void AnnounceOrRestartEntityBeacon()
        {
            NavigationTargetTracker.MarkEntity();
            if (PreferencesManager.AudioBeaconsEnabled)
                mod.RestartEntityBeacon();
            else
                mod.AnnounceCurrentEntity();
        }

        /// <summary>
        /// Tab opens the main menu, which only happens on the field: a battle state still set at that
        /// point is stale, so clear it. Tab is also pressed mid-battle, so only clear when no live
        /// BattleController exists (one scene scan per keypress, only while the flag is set).
        /// </summary>
        private static void HandleTabKey()
        {
            if (!BattleStateHelper.IsInBattle) return;

            try
            {
                if (UnityEngine.Object.FindObjectOfType<Il2CppLast.Battle.BattleController>() != null)
                    return;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle State] Tab: BattleController check failed: {ex.Message}");
                return;
            }

            BattleStateHelper.TryClearOnBattleEnd();
        }

        private void HandleFunctionKeyInput()
        {
            // F1 (walk/run) and F3 (encounters) are the game's own toggles; GameToggleAnnouncer speaks
            // the new state from the game's setters, whichever input flipped it.

            // F5 to toggle enemy HP display (battle only)
            if (GamepadManager.IsKeyCodePressed(KeyCode.F5))
            {
                ToggleEnemyHPDisplay();
            }

            // F7 toggles Auto Detail (descriptions spoken on focus)
            if (GamepadManager.IsKeyCodePressed(KeyCode.F7))
            {
                mod.ToggleAutoDetail();
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
                FFIII_ScreenReaderMod.SpeakText(T("Unable to detect vehicle state"), interrupt: true);
            }
        }

        /// <summary>
        /// I key / right stick up: reads the focused entry's description or details on demand, so
        /// descriptions stay reachable with Auto Detail off. Most specific screens are checked first.
        /// Equipment job requirements are on U (UsableByAnnouncer).
        /// </summary>
        internal static void HandleItemDetailsKey()
        {
            try
            {
                if (IsConfigMenuActive())
                    AnnounceConfigTooltip();
                else if (JobMenuState.IsActive)
                    SpeakDescription(JobMenuState.GetFocusedJobDescription());
                else if (ShopMenuTracker.ValidateState())
                    ShopDetailsAnnouncer.AnnounceCurrentItemDetails();
                else if (EquipMenuState.IsActive)
                    SpeakDescription(EquipMenuState.LastFocusedDescription);
                else if (MagicMenuState.IsSpellListActive)
                    SpeakDescription(MagicMenuState.LastFocusedDescription);
                else if (ItemMenuState.IsItemMenuActive)
                    SpeakDescription(ItemMenuState.LastSelectedItem?.Description);
                else if (BattleMagicMenuState.IsActive)
                    SpeakDescription(BattleMagicMenuState.LastFocusedDescription);
                else if (BattleItemMenuState.IsActive)
                    SpeakDescription(BattleItemMenuState.LastFocusedDescription);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error handling I key: {ex.Message}");
            }
        }

        private static void SpeakDescription(string description)
        {
            description = TextUtils.StripIconMarkup(description);
            FFIII_ScreenReaderMod.SpeakText(string.IsNullOrWhiteSpace(description) ? T("No description") : description, interrupt: true);
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
            // Enemy HP Display is a battle feature, so gate on in-battle (not IsFieldActive,
            // which is false during battle). Restores the pre-refactor behavior.
            if (!BattleStateHelper.IsInBattle)
            {
                ControllerRouter.SpeakModMenuUnavailable();
                return;
            }

            int current = PreferencesManager.EnemyHPDisplay;
            int next = (current + 1) % 3;
            PreferencesManager.SetEnemyHPDisplay(next);

            string[] options = { T("Numbers"), T("Percentage"), T("Hidden") };
            FFIII_ScreenReaderMod.SpeakText(string.Format(T("Enemy HP: {0}"), options[next]), interrupt: true);
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
                FFIII_ScreenReaderMod.SpeakText(T("Failed to dump entity names"), true);
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
    }
}
