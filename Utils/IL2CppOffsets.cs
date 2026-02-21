namespace FFIII_ScreenReader.Utils
{
    /// <summary>
    /// Centralized IL2CPP memory offsets and state machine enum values.
    /// All offsets are derived from dump.cs analysis and verified at runtime.
    /// If the game updates, check these first for breakage.
    /// </summary>
    internal static class IL2CppOffsets
    {
        /// <summary>
        /// Common state machine reading offsets (used by StateReaderHelper).
        /// </summary>
        internal static class StateMachine
        {
            public const int OFFSET_CURRENT = 0x10;   // StateMachine.current pointer
            public const int OFFSET_TAG = 0x10;        // State.Tag (int)
        }

        /// <summary>
        /// BattleCommandSelectController (dump.cs line 435181)
        /// </summary>
        internal static class BattleCommand
        {
            public const int OFFSET_STATE_MACHINE = 0x48;

            public const int STATE_NONE = 0;
            public const int STATE_NORMAL = 1;       // Main command menu (Attack, Magic, etc.)
            public const int STATE_EXTRA = 2;        // Sub-commands (White Magic, Black Magic, etc.)
            public const int STATE_MANIPULATE = 3;   // Enemy manipulation commands
        }

        /// <summary>
        /// EquipmentWindowController (KeyInput version)
        /// </summary>
        internal static class Equipment
        {
            public const int OFFSET_STATE_MACHINE = 0x60;

            public const int STATE_NONE = 0;         // Menu closed
            public const int STATE_COMMAND = 1;      // Command bar
            public const int STATE_INFO = 2;         // Slot selection
            public const int STATE_SELECT = 3;       // Item selection
        }

        /// <summary>
        /// ItemWindowController (KeyInput version, dump.cs line 456894)
        /// NOTE: Touch version has different values.
        /// </summary>
        internal static class Item
        {
            public const int OFFSET_STATE_MACHINE = 0x70;

            public const int STATE_NONE = 0;             // Menu closed
            public const int STATE_COMMAND_SELECT = 1;   // Command bar (Use/Key Items/Sort)
            public const int STATE_USE_SELECT = 2;       // Regular item list
            public const int STATE_IMPORTANT_SELECT = 3; // Key items list
            public const int STATE_ORGANIZE_SELECT = 4;  // Organize/Sort mode
            public const int STATE_TARGET_SELECT = 5;    // Character target selection
        }

        /// <summary>
        /// AbilityWindowController (dump.cs line 281758)
        /// </summary>
        internal static class Magic
        {
            public const int OFFSET_STATE_MACHINE = 0x88;

            public const int STATE_USE_LIST = 1;         // Use mode spell list
            public const int STATE_MEMORIZE_LIST = 3;    // Learn mode spell list
            public const int STATE_REMOVE_LIST = 4;      // Remove mode spell list
            public const int STATE_COMMAND = 7;          // Command menu

            // AbilityContentListController / AbilityUseContentListController offsets
            public const int OFFSET_DATA_LIST = 0x38;            // List<OwnedAbility> (Use/Remove)
            public const int OFFSET_ABILITY_ITEM_LIST = 0x40;   // List<OwnedItemData> (Learn)
            public const int OFFSET_CONTENT_LIST = 0x60;        // List<BattleAbilityInfomationContentController>
            public const int OFFSET_TARGET_CHARACTER = 0x98;     // OwnedCharacterData targetCharacterData
            public const int OFFSET_IS_LEARNING_ITEM = 0xA8;    // bool isLearnigItem (game typo)
        }

        /// <summary>
        /// ShopController (dump.cs line 466830)
        /// </summary>
        internal static class Shop
        {
            public const int OFFSET_STATE_MACHINE = 0x98;

            public const int STATE_NONE = 0;             // Menu closed
            public const int STATE_SELECT_COMMAND = 1;   // Command bar (Buy/Sell/Equipment/Back)

            // ShopTradeWindowController offsets
            public const int OFFSET_TRADE_VIEW = 0x30;          // ShopTradeWindowView
            public const int OFFSET_SELECTED_COUNT = 0x3C;      // int selectedCount
            public const int OFFSET_SELECT_COUNT_TEXT = 0x68;    // Text selectCountText
            public const int OFFSET_TOTAL_PRICE_TEXT = 0x70;     // Text totarlPriceText (game typo)
        }

        /// <summary>
        /// BattlePauseController / BattleUIManager
        /// </summary>
        internal static class BattlePause
        {
            public const int OFFSET_PAUSE_CONTROLLER = 0x90;       // BattleUIManager.pauseController
            public const int OFFSET_IS_ACTIVE_PAUSE_MENU = 0x71;   // BattlePauseController.isActivePauseMenu

            // CommonPopup offsets (used for button reading)
            public const int OFFSET_SELECT_CURSOR = 0x68;           // Cursor selectCursor
            public const int OFFSET_COMMAND_LIST = 0x70;            // List<CommonCommand> commandList
            public const int OFFSET_COMMAND_TEXT = 0x18;            // Text text (in CommonCommand)
        }

        /// <summary>
        /// SubSceneManagerMainGame state values
        /// </summary>
        internal static class GameState
        {
            public const int STATE_CHANGE_MAP = 1;
            public const int STATE_FIELD_READY = 2;
            public const int STATE_PLAYER = 3;
            public const int STATE_BATTLE = 13;
        }

        /// <summary>
        /// PreeMptiveState (battle start conditions)
        /// </summary>
        internal static class BattleStart
        {
            public const int STATE_NON = -1;
            public const int STATE_NORMAL = 0;              // Normal encounter
            public const int STATE_PREEMPTIVE = 1;          // Party preemptive
            public const int STATE_BACK_ATTACK = 2;
            public const int STATE_ENEMY_PREEMPTIVE = 3;    // Enemy preemptive
            public const int STATE_ENEMY_SIDE_ATTACK = 4;
            public const int STATE_SIDE_ATTACK = 5;
        }

        /// <summary>
        /// SelectFieldContentController (NPC event item selection)
        /// </summary>
        internal static class EventItemSelect
        {
            public const int OFFSET_CONTENT_DATA_LIST = 0x28;       // List<SelectFieldContentData>
            public const int OFFSET_SELECT_CURSOR = 0x30;           // Cursor

            // SelectFieldContentData offsets
            public const int OFFSET_NAME_MESSAGE_ID = 0x18;         // string
            public const int OFFSET_DESCRIPTION_MESSAGE_ID = 0x20;  // string
        }

        /// <summary>
        /// MessageWindowManager offsets
        /// </summary>
        internal static class MessageWindow
        {
            public const int OFFSET_MESSAGE_LIST = 0x88;            // List<string> messageList
            public const int OFFSET_NEW_PAGE_LINE_LIST = 0xA0;      // List<int> newPageLineList
            public const int OFFSET_SPEAKER_VALUE = 0xA8;           // string spekerValue (game typo)
            public const int OFFSET_CURRENT_PAGE_NUMBER = 0xF8;     // int currentPageNumber
        }

        /// <summary>
        /// StatusDetailsControllerBase offsets (stat reading)
        /// </summary>
        internal static class StatusDetails
        {
            public const int OFFSET_CONTENT_LIST = 0x48;            // contentList
            public const int OFFSET_PARAM_TYPE = 0x18;              // ParameterContentController.type
            public const int OFFSET_PARAM_VIEW = 0x20;              // ParameterContentController.view
            public const int OFFSET_MULTIPLIED_VALUE_TEXT = 0x28;   // ParameterContentView.multipliedValueText
            public const int PARAMETER_TYPE_ATTACK = 10;            // ParameterType.Attack enum value
        }

        /// <summary>
        /// FieldPlayer movement state values (MoveState enum)
        /// </summary>
        internal static class MoveState
        {
            public const int WALK = 0;
            public const int DUSH = 1;          // Run (game typo: "dash")
            public const int AIRSHIP = 2;
            public const int SHIP = 3;
            public const int LOWFLYING = 4;
            public const int CHOCOBO = 5;
            public const int GIMMICK = 6;
            public const int UNIQUE = 7;
        }

        /// <summary>
        /// Transport type values (TransportType enum in game)
        /// </summary>
        internal static class Transport
        {
            public const int SHIP = 2;
            public const int PLANE = 3;              // Airship
            public const int SUBMARINE = 6;
            public const int LOWFLYING = 7;
            public const int SPECIALPLANE = 8;
            public const int YELLOWCHOCOBO = 9;
            public const int BLACKCHOCOBO = 10;
            public const int BOKO = 11;
        }
    }
}
