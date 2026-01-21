using FFIII_ScreenReader.Patches;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Centralized cursor suppression check.
    /// Replaces scattered ShouldSuppress() calls in CursorNavigation_Postfix.
    ///
    /// Returns the name of the active state that should suppress cursor reading,
    /// or null if no suppression needed.
    /// </summary>
    public static class CursorSuppressionCheck
    {
        /// <summary>
        /// Result of a suppression check.
        /// </summary>
        public class SuppressionResult
        {
            public bool ShouldSuppress { get; set; }
            public string StateName { get; set; }
            public bool IsPopup { get; set; }

            public static SuppressionResult None => new SuppressionResult
            {
                ShouldSuppress = false,
                StateName = null,
                IsPopup = false
            };

            public static SuppressionResult Suppressed(string stateName, bool isPopup = false) => new SuppressionResult
            {
                ShouldSuppress = true,
                StateName = stateName,
                IsPopup = isPopup
            };
        }

        /// <summary>
        /// Checks all menu states to determine if cursor reading should be suppressed.
        /// Returns suppression result with state name.
        ///
        /// Order matters:
        /// 1. Battle submenus checked FIRST (they have dedicated announcement patches)
        /// 2. Popup check (handled specially by caller with ReadCurrentButton)
        /// 3. Other menu states
        ///
        /// NOTE: Battle pause menu is handled by special case in CursorNavigation_Postfix
        /// before this check runs (detects "curosr_parent" in cursor path).
        /// </summary>
        public static SuppressionResult Check()
        {
            // === BATTLE SUBMENUS ===
            // These have dedicated patches that handle announcements.

            // Battle command menu - SetCursor patch handles command announcements
            if (BattleCommandState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleCommand");

            // Battle target selection - needs target HP/status
            if (BattleTargetPatches.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleTarget");

            // Battle item menu - needs item data in battle
            if (BattleItemMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleItem");

            // Battle magic menu - needs spell data with charges in battle
            if (BattleMagicMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("BattleMagic");

            // === OTHER MENUS ===

            // Popup - special case, handled with ReadCurrentButton
            if (PopupState.ShouldSuppress())
                return SuppressionResult.Suppressed("Popup", isPopup: true);

            // Shop menus (buy/sell item lists) - needs price data
            if (ShopMenuTracker.ValidateState())
                return SuppressionResult.Suppressed("Shop");

            // Item menu (item list) - needs item description
            if (ItemMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("ItemMenu");

            // Job menu (job list) - needs job level data
            if (JobMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("JobMenu");

            // Status menu character selection - SelectContent_Postfix handles announcements
            if (StatusMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("StatusMenu");

            // Status details screen - stat navigation handles announcements
            if (StatusDetailsState.ShouldSuppress())
                return SuppressionResult.Suppressed("StatusDetails");

            // Equipment menus (slot and item list) - needs stat comparison
            if (EquipMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("EquipMenu");

            // Config menu - needs current setting values
            if (ConfigMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("ConfigMenu");

            // Magic menu spell list - needs spell data with charges
            if (MagicMenuState.ShouldSuppress())
                return SuppressionResult.Suppressed("MagicMenu");

            // No suppression needed
            return SuppressionResult.None;
        }

        /// <summary>
        /// Simple check - returns true if any state should suppress cursor.
        /// Use Check() for more detailed information.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return Check().ShouldSuppress;
        }
    }
}
