using System;
using System.Collections.Generic;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

// FF3 Shop UI types
using ShopController = Il2CppLast.UI.KeyInput.ShopController;
using ShopInfoController = Il2CppLast.UI.KeyInput.ShopInfoController;
using ShopListMainContentController = Il2CppLast.UI.KeyInput.ShopListMainContentController;
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopCommandMenuController = Il2CppLast.UI.KeyInput.ShopCommandMenuController;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using ShopCommandId = Il2CppLast.Defaine.ShopCommandId;
using GameCursor = Il2CppLast.UI.Cursor;

// Master data types for item stats
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state for 'I' / 'U' key access and generic-cursor suppression.
    /// Active for the whole shop session — command bar, item lists, trade window, equipment — because
    /// every shop panel has a dedicated announcer in ShopPatches.
    /// </summary>
    internal static class ShopMenuTracker
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.SHOP_MENU);

        static ShopMenuTracker()
        {
            _helper.RegisterResetHandler(() =>
            {
                LastItemName = null;
                LastItemDescription = null;
                ShopPatches.ResetShopTracking();
            });
        }

        public static bool IsShopMenuActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }

        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }

        /// <summary>
        /// Returns true if generic cursor reading should be suppressed.
        /// Validates the shop controller at runtime to detect the shop closing.
        /// </summary>
        public static bool ValidateState()
        {
            if (!IsShopMenuActive)
                return false;

            int state = GetShopControllerState();
            if (state == IL2CppOffsets.Shop.STATE_NONE || state < 0)
            {
                IsShopMenuActive = false;
                return false;
            }

            return true;
        }

        /// <summary>
        /// ShopController state tag, or -1 when the shop controller isn't shown.
        /// </summary>
        internal static int GetShopControllerState()
        {
            var shopController = GameObjectCache.GetOrFind<ShopController>();
            if (shopController == null || !shopController.gameObject.activeInHierarchy)
                return -1;
            return StateReaderHelper.ReadStateTag(shopController.Pointer, IL2CppOffsets.Shop.OFFSET_STATE_MACHINE);
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed (and after the item name with Auto Detail).
    /// Announces stats first, then description.
    /// Format: "Defense 3, Magic Defense 1. Armor made of leather."
    /// </summary>
    internal static class ShopDetailsAnnouncer
    {
        /// <param name="interrupt">
        /// When true (the on-demand 'I' key), interrupts current speech. Auto Detail passes
        /// false so this queues after the item-name announce instead of cutting it off.
        /// </param>
        /// <param name="announceIfEmpty">
        /// When true (the 'I' key), speaks "No item details available" when nothing is cached.
        /// Auto Detail passes false so it stays silent rather than saying that on every plain item.
        /// </param>
        public static void AnnounceCurrentItemDetails(bool interrupt = true, bool announceIfEmpty = true)
        {
            try
            {
                if (!ShopMenuTracker.ValidateState())
                    return;

                string stats = ShopPatches.GetItemStats(ShopMenuTracker.LastItemName);
                string description = ShopMenuTracker.LastItemDescription;

                string announcement = stats ?? "";
                if (!string.IsNullOrEmpty(description))
                    announcement = string.IsNullOrEmpty(announcement) ? description : $"{announcement}. {description}";

                if (string.IsNullOrEmpty(announcement))
                {
                    if (!announceIfEmpty)
                        return;
                    announcement = T("No item details available");
                }

                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: interrupt);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shop menu patches using manual Harmony patching.
    ///   • ShopInfoController.SetDescription — fires on every cursor move in the buy/sell list,
    ///     affordable or not; the focused item is read from ShopListMainContentController.
    ///   • ShopCommandMenuController.SetCursor — the command bar (Buy / Sell / Equipment / Back).
    ///   • ShopTradeWindowController.Show / AddCount / TakeCount — the quantity window.
    ///   • ShopController.InitSelectCommand / Close — session start and end.
    /// Postfixes never declare the string parameters of the patched methods (IL2CPP crash).
    /// </summary>
    internal static class ShopPatches
    {
        private const string LOG = "[Shop]";

        // ShopListMainContentController (KeyInput)
        private const int OFFSET_LIST_SELECT_CURSOR = 0x48;
        private const int OFFSET_PRODUCT_CONTENT_LIST = 0x68;   // List<ShopListItemContentController>

        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopInfoController), "SetDescription",
                typeof(ShopPatches), nameof(SetDescription_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopCommandMenuController), "SetCursor",
                typeof(ShopPatches), nameof(CommandSetCursor_Postfix), LOG, new Type[] { typeof(int) });
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopTradeWindowController), "Show",
                typeof(ShopPatches), nameof(TradeWindowShow_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopTradeWindowController), "AddCount",
                typeof(ShopPatches), nameof(TradeWindowCount_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopTradeWindowController), "TakeCount",
                typeof(ShopPatches), nameof(TradeWindowCount_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopController), "InitSelectCommand",
                typeof(ShopPatches), nameof(InitSelectCommand_Postfix), LOG);
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(ShopController), "Close",
                typeof(ShopPatches), nameof(ShopClose_Postfix), LOG);
        }

        // ============ Item list ============

        // Scoped index dedup: SetDescription also fires when the stats/description panel is toggled for
        // the same focused item, and no other signal covers unaffordable items.
        private static int lastAnnouncedListIndex = -1;

        private static ShopListMainContentController cachedMainList;

        /// <summary>
        /// Fires whenever the shop description panel updates — on every cursor move in the item list,
        /// affordable or not. Used as the cursor-moved signal; the focused item's data is read from the
        /// list itself (the description parameter is not declared here).
        /// </summary>
        public static void SetDescription_Postfix()
        {
            try
            {
                // Only while a buy/sell list has focus; other states (command bar, trade window,
                // transitions) also refresh the panel.
                int state = ShopMenuTracker.GetShopControllerState();
                if (state != IL2CppOffsets.Shop.STATE_SELECT_PRODUCT && state != IL2CppOffsets.Shop.STATE_SELECT_SELL_ITEM)
                {
                    lastAnnouncedListIndex = -1;
                    return;
                }

                AnnounceFocusedFromList();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"{LOG} Error in SetDescription_Postfix: {ex.Message}");
            }
        }

        private static void AnnounceFocusedFromList()
        {
            var mainList = FindActiveMainContentController();
            if (mainList == null)
                return;

            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.SHOP_MENU);

            IntPtr ptr = mainList.Pointer;
            IntPtr cursorPtr = StateReaderHelper.ReadPointerField(ptr, OFFSET_LIST_SELECT_CURSOR);
            if (cursorPtr == IntPtr.Zero)
                return;

            int index = new GameCursor(cursorPtr).Index;
            if (index < 0 || index == lastAnnouncedListIndex)
                return;

            IntPtr listPtr = StateReaderHelper.ReadPointerField(ptr, OFFSET_PRODUCT_CONTENT_LIST);
            if (listPtr == IntPtr.Zero)
                return;

            var list = new Il2CppSystem.Collections.Generic.List<ShopListItemContentController>(listPtr);
            if (index >= list.Count)
                return;

            lastAnnouncedListIndex = index;

            // The product list is a fixed pool whose unused entries still carry other names, so the
            // position counts the ACTIVE entries only.
            int activeCount = 0;
            for (int i = 0; i < list.Count; i++)
            {
                try
                {
                    var c = list[i];
                    if (c != null && c.gameObject.activeInHierarchy) activeCount++;
                }
                catch { }
            }

            AnnounceFocusedItem(list[index], index, activeCount);
        }

        private static ShopListMainContentController FindActiveMainContentController()
        {
            try
            {
                if (cachedMainList != null && cachedMainList.gameObject != null && cachedMainList.gameObject.activeInHierarchy)
                    return cachedMainList;

                // Buy and sell each have their own list controller; use the one that is shown.
                cachedMainList = null;
                foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ShopListMainContentController>())
                {
                    if (candidate != null && candidate.gameObject.activeInHierarchy)
                    {
                        cachedMainList = candidate;
                        break;
                    }
                }
            }
            catch { cachedMainList = null; }
            return cachedMainList;
        }

        /// <summary>
        /// Announces the focused shop item as "Name, Price" plus its position, and caches it for the
        /// I/U keys; with Auto Detail the stats/description follow (queued). An empty slot announces
        /// "Empty" and keeps the previous item cached for I/U.
        /// </summary>
        private static void AnnounceFocusedItem(ShopListItemContentController content, int index, int count)
        {
            string itemName = null;
            string price = null;
            string description = null;
            try
            {
                itemName = TextUtils.StripIconMarkup(content?.iconTextView?.nameText?.text);
                price = content?.shopListItemContentView?.priceText?.text;
                description = content?.Message;
            }
            catch { }

            if (string.IsNullOrEmpty(itemName))
            {
                FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(T("Empty"), index, count), interrupt: true);
                return;
            }

            ShopMenuTracker.LastItemName = itemName;
            ShopMenuTracker.LastItemDescription = TextUtils.StripIconMarkup(description);

            string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(announcement, index, count), interrupt: true);

            // Auto Detail: queue the same stats/description the 'I' key reads (interrupt:false)
            if (PreferencesManager.AutoDetailEnabled)
                ShopDetailsAnnouncer.AnnounceCurrentItemDetails(interrupt: false, announceIfEmpty: false);
        }

        // ============ Command bar ============

        /// <summary>
        /// Cursor moved on the shop command bar. Announces only while the bar is the active panel
        /// (SelectCommand): during shop preparation the cursor also moves while the state is already
        /// SelectProduct, which would otherwise read a stray "Buy".
        /// </summary>
        public static void CommandSetCursor_Postfix(ShopCommandMenuController __instance, int index)
        {
            try
            {
                if (__instance == null)
                    return;
                if (ShopMenuTracker.GetShopControllerState() != IL2CppOffsets.Shop.STATE_SELECT_COMMAND)
                    return;

                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.SHOP_MENU);

                var contentList = __instance.contentList;
                var commandContent = SelectContentHelper.TryGetItem(contentList, index);
                if (commandContent == null)
                    return;

                string commandName = GetCommandName(commandContent.CommandId);
                if (string.IsNullOrEmpty(commandName))
                    return;

                FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(commandName, index, contentList.Count), interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"{LOG} Error in CommandSetCursor_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// The command bar gained focus (shop opened on it, or backed out of a list). Marks the shop
        /// session active so the generic cursor reader doesn't also read the bar.
        /// </summary>
        public static void InitSelectCommand_Postfix()
        {
            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.SHOP_MENU);
            lastAnnouncedListIndex = -1;
        }

        internal static string GetCommandName(ShopCommandId commandId)
        {
            return commandId switch
            {
                ShopCommandId.Buy => T("Buy"),
                ShopCommandId.Sell => T("Sell"),
                ShopCommandId.Equipment => T("Equipment"),
                ShopCommandId.Back => T("Back"),
                _ => null
            };
        }

        // ============ Trade window ============

        /// <summary>
        /// Fires once when the trade window opens (buy or sell confirm): announces the starting
        /// quantity and total.
        /// </summary>
        public static void TradeWindowShow_Postfix(ShopTradeWindowController __instance)
        {
            lastAnnouncedListIndex = -1;
            AnnounceQuantity(__instance);
        }

        /// <summary>
        /// AddCount / TakeCount: each call is a discrete key press, so announce unconditionally (a press
        /// against the max/min replays the same value, which confirms the limit).
        /// </summary>
        public static void TradeWindowCount_Postfix(ShopTradeWindowController __instance)
        {
            AnnounceQuantity(__instance);
        }

        private static void AnnounceQuantity(ShopTradeWindowController controller)
        {
            try
            {
                if (controller == null)
                    return;

                int selectedCount = GetSelectedCount(controller);
                string totalPrice = GetTotalPriceText(controller);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? string.Format(T("Quantity: {0}"), selectedCount)
                    : string.Format(T("Quantity: {0}, Total: {1}"), selectedCount, totalPrice);

                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"{LOG} Error announcing quantity: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads selectedCount from ShopTradeWindowController at offset 0x3C.
        /// </summary>
        private static int GetSelectedCount(ShopTradeWindowController controller)
        {
            try
            {
                unsafe
                {
                    IntPtr ptr = controller.Pointer;
                    if (ptr != IntPtr.Zero)
                        return *(int*)((byte*)ptr.ToPointer() + IL2CppOffsets.Shop.OFFSET_SELECTED_COUNT);
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Reads the total price text: controller -> view (0x30) -> totarlPriceText (0x70) -> text.
        /// </summary>
        private static string GetTotalPriceText(ShopTradeWindowController controller)
        {
            try
            {
                IntPtr viewPtr = StateReaderHelper.ReadPointerField(controller.Pointer, IL2CppOffsets.Shop.OFFSET_TRADE_VIEW);
                if (viewPtr == IntPtr.Zero) return null;

                IntPtr textPtr = StateReaderHelper.ReadPointerField(viewPtr, IL2CppOffsets.Shop.OFFSET_TOTAL_PRICE_TEXT);
                if (textPtr == IntPtr.Zero) return null;

                return new UnityEngine.UI.Text(textPtr).text;
            }
            catch { return null; }
        }

        // ============ Shop close ============

        public static void ShopClose_Postfix()
        {
            cachedMainList = null;
            ShopMenuTracker.IsShopMenuActive = false;
        }

        /// <summary>
        /// Resets the shop tracking when leaving the shop menu.
        /// Called from ShopMenuTracker's reset handler.
        /// </summary>
        public static void ResetShopTracking()
        {
            lastAnnouncedListIndex = -1;
        }

        // ============ Item stats (I key / Auto Detail) ============

        /// <summary>
        /// Stats of a weapon or armor, resolved from master data by its displayed name (the list's
        /// ContentId is not a reliable Content key). Null for other items.
        /// </summary>
        internal static string GetItemStats(string itemName)
        {
            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null || string.IsNullOrEmpty(itemName))
                    return null;

                if (!UsableByAnnouncer.TryResolveContent(masterManager, itemName, out int typeId, out int itemId))
                    return null;

                if (typeId == FF3Constants.ItemContentTypes.WEAPON)
                    return GetWeaponStats(masterManager.GetData<Weapon>(itemId));
                if (typeId == FF3Constants.ItemContentTypes.ARMOR)
                    return GetArmorStats(masterManager.GetData<Armor>(itemId));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"{LOG} Error getting item stats: {ex.Message}");
            }
            return null;
        }

        private static string GetWeaponStats(Weapon weapon)
        {
            if (weapon == null)
                return null;

            var stats = new List<string>();
            AddStat(stats, T("Attack {0}"), weapon.Attack);
            AddStat(stats, T("Accuracy {0}"), weapon.AccuracyRate);
            AddStat(stats, T("Evasion {0}"), weapon.EvasionRate);
            AddStatBonuses(stats, weapon.Strength, weapon.Vitality, weapon.Agility, weapon.Intelligence, weapon.Spirit, weapon.Magic);
            return stats.Count > 0 ? string.Join(", ", stats) : null;
        }

        private static string GetArmorStats(Armor armor)
        {
            if (armor == null)
                return null;

            var stats = new List<string>();
            AddStat(stats, T("Defense {0}"), armor.Defense);
            AddStat(stats, T("Magic Defense {0}"), armor.AbilityDefense);
            AddStat(stats, T("Evasion {0}"), armor.EvasionRate);
            AddStat(stats, T("Magic Evasion {0}"), armor.AbilityEvasionRate);
            AddStatBonuses(stats, armor.Strength, armor.Vitality, armor.Agility, armor.Intelligence, armor.Spirit, armor.Magic);
            return stats.Count > 0 ? string.Join(", ", stats) : null;
        }

        private static void AddStatBonuses(List<string> stats, int strength, int vitality, int agility, int intelligence, int spirit, int magic)
        {
            AddStat(stats, T("Strength +{0}"), strength);
            AddStat(stats, T("Vitality +{0}"), vitality);
            AddStat(stats, T("Agility +{0}"), agility);
            AddStat(stats, T("Intelligence +{0}"), intelligence);
            AddStat(stats, T("Spirit +{0}"), spirit);
            AddStat(stats, T("Magic +{0}"), magic);
        }

        private static void AddStat(List<string> stats, string format, int value)
        {
            if (value > 0)
                stats.Add(string.Format(format, value));
        }
    }
}
