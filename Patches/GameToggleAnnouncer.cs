using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

using CheatSettingsClient = Il2CppLast.Management.CheatSettingsClient;
using Config = Il2CppLast.Data.User.Config;
using MapUIManager = Il2CppLast.Map.MapUIManager;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Announces the two game-side field toggles — walk/run (auto-dash) and random encounters —
    /// whatever flipped them: F1/F3, a controller stick click passed through to the game, or the cheat
    /// menu. Event-driven: postfixes on the methods that change them compare against the last known
    /// value and speak only on a real change during field gameplay. Outside the field (menus, battle,
    /// loading) the value is re-seeded silently, so a context change is never reported as a toggle.
    /// CheatSettingsData.set_IsEnableEncount is deliberately NOT patched: its native body is shared by
    /// 23 trivial setters (RVA 0x38B7C0), so a detour there would run for all of them.
    /// </summary>
    internal static class GameToggleAnnouncer
    {
        private static bool? lastEncounter;
        private static bool? lastRun;

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(CheatSettingsClient), "SetIsEnableEncount",
                typeof(GameToggleAnnouncer), nameof(Encounter_Postfix), "[GameToggle]");
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(Config), "set_IsAutoDash",
                typeof(GameToggleAnnouncer), nameof(AutoDash_Postfix), "[GameToggle]");
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(MapUIManager), "AutoDashOperationSwitch",
                typeof(GameToggleAnnouncer), nameof(AutoDash_Postfix), "[GameToggle]");
        }

        /// <summary>
        /// Records the current values without speaking. Called after each scene load, so the first
        /// toggle on a new map compares against the real state.
        /// </summary>
        public static void Seed()
        {
            lastEncounter = ReadEncounter();
            lastRun = ReadRun();
        }

        public static void Encounter_Postfix()
        {
            bool? value = ReadEncounter();
            if (value.HasValue && ShouldAnnounce(ref lastEncounter, value.Value))
                FFIII_ScreenReaderMod.SpeakText(value.Value ? T("Encounters on") : T("Encounters off"), interrupt: true);
        }

        public static void AutoDash_Postfix()
        {
            bool? value = ReadRun();
            if (value.HasValue && ShouldAnnounce(ref lastRun, value.Value))
                FFIII_ScreenReaderMod.SpeakText(value.Value ? T("Run") : T("Walk"), interrupt: true);
        }

        /// <summary>
        /// Updates the last value; true only for a real change during field gameplay.
        /// </summary>
        private static bool ShouldAnnounce(ref bool? last, bool value)
        {
            bool changed = last.HasValue && last.Value != value;
            last = value;
            return changed && ControllerRouter.IsFieldActive;
        }

        private static bool? ReadEncounter()
        {
            try
            {
                var cheat = UserDataManager.Instance()?.CheatSettingsData;
                return cheat != null ? cheat.IsEnableEncount : (bool?)null;
            }
            catch { return null; }
        }

        private static bool? ReadRun()
        {
            try
            {
                return UserDataManager.Instance()?.Config != null ? MoveStateHelper.GetDashFlag() : (bool?)null;
            }
            catch { return null; }
        }
    }
}
