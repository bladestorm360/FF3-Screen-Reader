using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MelonLoader;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Audio-only virtual menu for adjusting screen reader settings.
    /// Accessible via F8 key. No Unity UI overlay - purely navigational state + announcements.
    /// </summary>
    internal static class ModMenu
    {
        /// <summary>
        /// Whether the mod menu is currently open.
        /// </summary>
        public static bool IsOpen { get; private set; }

        private static int currentIndex = 0;
        private static List<MenuItem> items;

        #region Menu Item Types

        private abstract class MenuItem
        {
            public string Name { get; protected set; }
            // Read by the I key. A lambda so toggle descriptions follow the current state.
            public Func<string> DescriptionGetter { get; protected set; } = () => "";
            public abstract string GetValueString();
            public abstract void Adjust(int delta);
            public abstract void Toggle();
        }

        private class ToggleItem : MenuItem
        {
            private readonly Func<bool> getter;
            private readonly Action toggle;

            public ToggleItem(string name, Func<bool> getter, Action toggle, Func<string> description)
            {
                Name = name;
                this.getter = getter;
                this.toggle = toggle;
                DescriptionGetter = description;
            }

            public override string GetValueString() => getter() ? T("On") : T("Off");
            public override void Adjust(int delta) => toggle();
            public override void Toggle() => toggle();
        }

        private class VolumeItem : MenuItem
        {
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public VolumeItem(string name, Func<int> getter, Action<int> setter, Func<string> description)
            {
                Name = name;
                this.getter = getter;
                this.setter = setter;
                DescriptionGetter = description;
            }

            public override string GetValueString() => $"{getter()}%";

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = Math.Clamp(current + (delta * 5), 0, 100);
                setter(newValue);
            }

            public override void Toggle()
            {
                // Toggle between 0 and 50 for quick mute/unmute
                int current = getter();
                setter(current == 0 ? 50 : 0);
            }
        }

        private class EnumItem : MenuItem
        {
            private readonly string[] options;
            private readonly Func<int> getter;
            private readonly Action<int> setter;

            public EnumItem(string name, string[] options, Func<int> getter, Action<int> setter, Func<string> description)
            {
                Name = name;
                this.options = options;
                this.getter = getter;
                this.setter = setter;
                DescriptionGetter = description;
            }

            public override string GetValueString()
            {
                int index = getter();
                if (index >= 0 && index < options.Length)
                    return options[index];
                return T("Unknown");
            }

            public override void Adjust(int delta)
            {
                int current = getter();
                int newValue = current + delta;
                if (newValue < 0) newValue = options.Length - 1;
                if (newValue >= options.Length) newValue = 0;
                setter(newValue);
            }

            public override void Toggle() => Adjust(1);
        }

        private class SectionHeader : MenuItem
        {
            public SectionHeader(string name)
            {
                Name = name;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) { }
            public override void Toggle() { }
        }

        private class ActionItem : MenuItem
        {
            private readonly Action action;

            public ActionItem(string name, Action action, Func<string> description)
            {
                Name = name;
                this.action = action;
                DescriptionGetter = description;
            }

            public override string GetValueString() => "";
            public override void Adjust(int delta) => action();
            public override void Toggle() => action();
        }

        #endregion

        /// <summary>
        /// Initializes the mod menu with all menu items.
        /// Call this once during mod initialization.
        /// </summary>
        public static void Initialize()
        {
            items = new List<MenuItem>
            {
                // Audio Feedback section
                new SectionHeader(T("Audio Feedback")),
                new ToggleItem(T("Wall Tones"),
                    () => PreferencesManager.WallTonesEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleWallTones(),
                    () => PreferencesManager.WallTonesEnabled
                        ? T("On. Directional tones play as you approach walls.")
                        : T("Off. No directional wall feedback.")),
                new ToggleItem(T("Footsteps"),
                    () => PreferencesManager.FootstepsEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleFootsteps(),
                    () => PreferencesManager.FootstepsEnabled
                        ? T("On. A click plays for each tile you walk.")
                        : T("Off. No per-tile movement sound.")),
                new ToggleItem(T("Beacon Navigation"),
                    () => PreferencesManager.AudioBeaconsEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleAudioBeacons(),
                    () => PreferencesManager.AudioBeaconsEnabled
                        ? T("On. A ping sounds toward the selected destination, and the pathfind keys restart it instead of speaking directions.")
                        : T("Off. The pathfind keys speak turn-by-turn directions.")),
                new ToggleItem(T("Beacon Destination Announcement"),
                    () => FFIII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleAnnounceOnBeaconRestart(),
                    () => FFIII_ScreenReaderMod.AnnounceOnBeaconRestartEnabled
                        ? T("On. Restarting the beacon also speaks the destination.")
                        : T("Off. Restarting the beacon only pings.")),

                // Volume Controls section
                new SectionHeader(T("Volume Controls")),
                new VolumeItem(T("Wall Bump Volume"),
                    () => PreferencesManager.WallBumpVolume,
                    PreferencesManager.SetWallBumpVolume,
                    () => T("Volume of the wall bump sound, 0 to 100 percent.")),
                new VolumeItem(T("Footstep Volume"),
                    () => PreferencesManager.FootstepVolume,
                    PreferencesManager.SetFootstepVolume,
                    () => T("Volume of the footstep click, 0 to 100 percent.")),
                new VolumeItem(T("Wall Tone Volume"),
                    () => PreferencesManager.WallToneVolume,
                    PreferencesManager.SetWallToneVolume,
                    () => T("Volume of the directional wall tones, 0 to 100 percent.")),
                new VolumeItem(T("Beacon Volume"),
                    () => PreferencesManager.BeaconVolume,
                    PreferencesManager.SetBeaconVolume,
                    () => T("Volume of the audio beacon ping, 0 to 100 percent.")),

                // Navigation Filters section
                new SectionHeader(T("Navigation Filters")),
                new ToggleItem(T("Pathfinding Filter"),
                    () => PreferencesManager.PathfindingFilterEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.TogglePathfindingFilter(),
                    () => PreferencesManager.PathfindingFilterEnabled
                        ? T("On. Entity cycling lists only entities with a walkable path.")
                        : T("Off. Every entity is listed, including unreachable ones.")),
                new ToggleItem(T("Map Exit Filter"),
                    () => PreferencesManager.MapExitFilterEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleMapExitFilter(),
                    () => PreferencesManager.MapExitFilterEnabled
                        ? T("On. Exits leading to the same map are merged into the closest one.")
                        : T("Off. Every map exit is listed.")),
                new ToggleItem(T("Layer Transition Filter"),
                    () => PreferencesManager.ToLayerFilterEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleToLayerFilter(),
                    () => PreferencesManager.ToLayerFilterEnabled
                        ? T("On. Stairs and ladders between floors are left out of the entity list.")
                        : T("Off. Stairs and ladders between floors are listed.")),

                // Battle Settings section
                new SectionHeader(T("Battle Settings")),
                new EnumItem(T("Enemy HP Display"),
                    new[] { T("Numbers"), T("Percentage"), T("Hidden") },
                    () => PreferencesManager.EnemyHPDisplay,
                    PreferencesManager.SetEnemyHPDisplay,
                    () => T("How enemy HP is read when targeting: numbers, percentage of maximum, or not at all.")),
                new EnumItem(T("Multi-hit Damage"),
                    new[] { T("Total only"), T("With hit count") },
                    () => PreferencesManager.DamageDisplay,
                    PreferencesManager.SetDamageDisplay,
                    () => T("For attacks that hit several times, optionally say the number of hits before the damage, for example 4x120 damage.")),

                // Battle Results section
                new SectionHeader(T("Battle Results")),
                new ToggleItem(T("EXP Counter Sound"),
                    () => PreferencesManager.ExpCounterEnabled,
                    FFIII_ScreenReaderMod.ToggleExpCounter,
                    () => PreferencesManager.ExpCounterEnabled
                        ? T("On. A rapid tick plays while the EXP bar fills after battle.")
                        : T("Off. The EXP bar fills silently after battle.")),
                new VolumeItem(T("EXP Counter Volume"),
                    () => PreferencesManager.ExpCounterVolume,
                    PreferencesManager.SetExpCounterVolume,
                    () => T("Volume of the EXP counter tick, 0 to 100 percent.")),

                // Announcements section
                new SectionHeader(T("Announcements")),
                new ToggleItem(T("Auto Detail"),
                    () => PreferencesManager.AutoDetailEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleAutoDetail(),
                    () => PreferencesManager.AutoDetailEnabled
                        ? T("On. Descriptions and stats are read automatically for items, spells, equipment and shop goods.")
                        : T("Off. Only names are read; press I for the description.")),
                new ToggleItem(T("Menu Position Announcements"),
                    () => PreferencesManager.MenuPositionAnnouncementsEnabled,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleMenuPositionAnnouncements(),
                    () => PreferencesManager.MenuPositionAnnouncementsEnabled
                        ? T("On. List entries include their position, for example 3 of 12.")
                        : T("Off. List entries are read without their position.")),

                // Controller Settings section
                new SectionHeader(T("Controller Settings")),
                new ToggleItem(T("Stick Click Normalization"),
                    () => PreferencesManager.StickClickNormalization,
                    () => FFIII_ScreenReaderMod.Instance?.ToggleStickClickNormalization(),
                    () => PreferencesManager.StickClickNormalization
                        ? T("On. L3 and R3 go to the game (auto-dash and encounters); their mod functions move to mod mode.")
                        : T("Off. L3 toggles audio beacons and R3 the pathfinding filter; the game does not receive them.")),

                // Close Menu action
                new ActionItem(T("Close Menu"), Close,
                    () => T("Closes the mod menu and returns to the game."))
            };

            MelonLogger.Msg("[ModMenu] Initialized with " + items.Count + " items");
        }

        /// <summary>
        /// Opens the mod menu.
        /// </summary>
        public static void Open()
        {
            if (IsOpen) return;

            IsOpen = true;
            currentIndex = 0;

            // Skip section header at index 0
            if (items != null && items.Count > 1 && items[0] is SectionHeader)
                currentIndex = 1;

            // Announce that the menu opened (both F8 and the controller Start button reach here),
            // then the first item after a short delay. The menu is virtual — game input is
            // suppressed via ControllerRouter.SuppressGameInput + InputPassthroughPatches (no
            // window stealing), so we speak the title ourselves instead of relying on NVDA.
            FFIII_ScreenReaderMod.SpeakText(T("Mod menu"), interrupt: true);
            CoroutineManager.StartUntracked(AnnounceFirstItemDelayed());
        }

        private static IEnumerator AnnounceFirstItemDelayed()
        {
            // Wait 2 frames for TTS to queue "Mod menu" before adding first item
            yield return null;
            yield return null;

            if (IsOpen) // Still open after delay
            {
                AnnounceCurrentItem(interrupt: false);
            }
        }

        /// <summary>
        /// Closes the mod menu.
        /// </summary>
        public static void Close()
        {
            if (!IsOpen) return;

            IsOpen = false;
            // Announce on every close path (keyboard Escape/F8, "Close Menu" item, controller B/Start).
            // Game input is restored automatically — ControllerRouter.SuppressGameInput becomes false.
            FFIII_ScreenReaderMod.SpeakText(T("Mod menu closed"), interrupt: true);
        }

        /// <summary>
        /// Handles input when the mod menu is open. Reads keys via GamepadManager
        /// (SDL3 + GetAsyncKeyState — hardware state); game input is suppressed by
        /// InputPassthroughPatches + Input.ResetInputAxes while open. No window focus stealing.
        /// Returns true if input was consumed (menu is open).
        /// </summary>
        public static bool HandleInput()
        {
            if (!IsOpen) return false;
            if (items == null || items.Count == 0) return false;

            // Escape or F8 to close
            if (GamepadManager.IsKeyCodePressed(KeyCode.Escape) || GamepadManager.IsKeyCodePressed(KeyCode.F8))
            {
                Close();
                return true;
            }

            // Up arrow - navigate to previous item
            if (GamepadManager.IsKeyCodePressed(KeyCode.UpArrow))
            {
                NavigatePrevious();
                return true;
            }

            // Down arrow - navigate to next item
            if (GamepadManager.IsKeyCodePressed(KeyCode.DownArrow))
            {
                NavigateNext();
                return true;
            }

            // Left arrow - decrease value
            if (GamepadManager.IsKeyCodePressed(KeyCode.LeftArrow))
            {
                AdjustCurrentItem(-1);
                return true;
            }

            // Right arrow - increase value
            if (GamepadManager.IsKeyCodePressed(KeyCode.RightArrow))
            {
                AdjustCurrentItem(1);
                return true;
            }

            // Enter or Space - toggle/activate
            if (GamepadManager.IsKeyCodePressed(KeyCode.Return) || GamepadManager.IsKeyCodePressed(KeyCode.Space))
            {
                ToggleCurrentItem();
                return true;
            }

            // I - read the current item's description
            if (GamepadManager.IsKeyCodePressed(KeyCode.I))
            {
                AnnounceCurrentItemDescription();
                return true;
            }

            return true; // Consume all input while menu is open
        }

        private static void AnnounceCurrentItemDescription()
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            string desc = items[currentIndex].DescriptionGetter?.Invoke();
            FFIII_ScreenReaderMod.SpeakText(string.IsNullOrWhiteSpace(desc) ? T("No description") : desc, interrupt: true);
        }

        public static void NavigateNext()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex++;
                if (currentIndex >= items.Count)
                    currentIndex = 0;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        public static void NavigatePrevious()
        {
            int startIndex = currentIndex;
            do
            {
                currentIndex--;
                if (currentIndex < 0)
                    currentIndex = items.Count - 1;

                // Skip section headers
                if (!(items[currentIndex] is SectionHeader))
                    break;

            } while (currentIndex != startIndex);

            AnnounceCurrentItem();
        }

        public static void AdjustCurrentItem(int delta)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Adjust(delta);
            AnnounceCurrentItem();
        }

        public static void ToggleCurrentItem()
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            if (item is SectionHeader) return;

            item.Toggle();

            // For action items (like Close Menu), don't re-announce
            if (item is ActionItem) return;

            AnnounceCurrentItem();
        }

        private static void AnnounceCurrentItem(bool interrupt = true)
        {
            if (currentIndex < 0 || currentIndex >= items.Count) return;

            var item = items[currentIndex];
            string value = item.GetValueString();

            string announcement;
            if (string.IsNullOrEmpty(value))
            {
                announcement = item.Name;
            }
            else
            {
                announcement = $"{item.Name}: {value}";
            }

            // Position among navigable (non-header) items, so headers don't count.
            var (index, count) = NavigablePosition();
            announcement = MenuPosition.Format(announcement, index, count);

            FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
        }

        /// <summary>
        /// Position of the current item among the navigable (non-header) items. Section headers are
        /// skipped during navigation, so the user hears "(N of total settings)" — not counting headers.
        /// </summary>
        private static (int index, int count) NavigablePosition()
        {
            int count = 0, index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] is SectionHeader) continue;
                if (i == currentIndex) index = count;
                count++;
            }
            return (index, count);
        }
    }
}
