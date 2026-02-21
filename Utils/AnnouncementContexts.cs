namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized announcement deduplication context strings.
    /// Each context represents a distinct UI element or state that tracks
    /// what was last announced to avoid repeating the same text.
    /// </summary>
    public static class AnnouncementContexts
    {
        // Battle
        public const string BATTLE_MESSAGE = "Battle.Message";
        public const string BATTLE_ACTION = "BattleAction";
        public const string BATTLE_COMMAND_CURSOR = "BattleCommand.Cursor";
        public const string BATTLE_TARGET_PLAYER = "BattleTarget.Player";
        public const string BATTLE_TARGET_ENEMY = "BattleTarget.Enemy";
        public const string BATTLE_RESULT_DATA = "BattleResult.Data";

        // Config menu
        public const string CONFIG_TEXT = "ConfigMenu.Text";
        public const string CONFIG_SETTING = "ConfigMenu.Setting";
        public const string CONFIG_ARROW = "ConfigMenu.Arrow";
        public const string CONFIG_SLIDER = "ConfigMenu.Slider";
        public const string CONFIG_SLIDER_CONTROLLER = "ConfigMenu.SliderController";
        public const string CONFIG_TOUCH_ARROW = "ConfigMenu.TouchArrow";
        public const string CONFIG_TOUCH_SLIDER = "ConfigMenu.TouchSlider";
        public const string CONFIG_TOUCH_SLIDER_CONTROLLER = "ConfigMenu.TouchSliderController";

        // New game
        public const string NEW_GAME_NAME = "NewGame.Name";
        public const string NEW_GAME_AUTO_NAME_INDEX = "NewGame.AutoNameIndex";
        public const string NEW_GAME_SELECTED_INDEX = "NewGame.SelectedIndex";
        public const string NEW_GAME_SLOT_NAME = "NewGame.SlotName";

        // Popups
        public const string POPUP_GAMEOVER_LOAD_BUTTON = "Popup.GameOverLoadButton";

        // Magic
        public const string MAGIC_TARGET = "MagicTarget";

        // Shop
        public const string SHOP_ITEM = "Shop.Item";
        public const string SHOP_QUANTITY = "Shop.Quantity";
    }
}
