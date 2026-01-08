using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;

// FF3 Shop UI types
using ShopListItemContentController = Il2CppLast.UI.KeyInput.ShopListItemContentController;
using ShopTradeWindowController = Il2CppLast.UI.KeyInput.ShopTradeWindowController;
using ShopTradeWindowView = Il2CppLast.UI.KeyInput.ShopTradeWindowView;
using ShopInfoController = Il2CppLast.UI.KeyInput.ShopInfoController;
using KeyInputShopController = Il2CppLast.UI.KeyInput.ShopController;

// Master data types for item stats
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Weapon = Il2CppLast.Data.Master.Weapon;
using Armor = Il2CppLast.Data.Master.Armor;
using Item = Il2CppLast.Data.Master.Item;
using Content = Il2CppLast.Data.Master.Content;
using ContentType = Il2CppLast.Defaine.Content.ContentType;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks shop menu state for 'I' key description access and suppression.
    /// Uses state machine validation to only suppress during item list navigation,
    /// not during command menu (Buy/Sell/Exit) navigation.
    /// </summary>
    public static class ShopMenuTracker
    {
        public static bool IsShopMenuActive { get; set; }
        public static string LastItemName { get; set; }
        public static string LastItemDescription { get; set; }
        public static string LastItemPrice { get; set; }
        public static string LastItemStats { get; set; }

        // State machine offsets (from dump.cs)
        // KeyInput.ShopController has stateMachine at offset 0x98
        private const int OFFSET_STATE_MACHINE = 0x98;
        private const int OFFSET_STATE_MACHINE_CURRENT = 0x10;
        private const int OFFSET_STATE_TAG = 0x10;

        // ShopController.State values (from dump.cs line 466830)
        private const int STATE_NONE = 0;
        private const int STATE_SELECT_COMMAND = 1;      // Command bar (Buy/Sell/Exit)
        private const int STATE_SELECT_PRODUCT = 2;      // Buy item list
        private const int STATE_SELECT_SELL_ITEM = 3;    // Sell item list
        private const int STATE_SELECT_ABILITY_TARGET = 4;
        private const int STATE_SELECT_EQUIPMENT = 5;
        private const int STATE_CONFIRMATION_BUY_ITEM = 6;
        private const int STATE_CONFIRMATION_SELL_ITEM = 7;
        private const int STATE_CONFIRMATION_FORGET_MAGIC = 8;
        private const int STATE_CONFIRMATION_BUY_MAGIC = 9;

        /// <summary>
        /// Validates that shop menu is active and should suppress generic cursor.
        /// - Command menu (STATE_SELECT_COMMAND): NOT suppressed - ShopCommandReader in MenuTextDiscovery handles it
        /// - Item lists: Suppressed - SetFocus_Postfix handles with price info
        /// - Quantity: Suppressed - UpdateCotroller_Postfix handles
        /// </summary>
        public static bool ValidateState()
        {
            if (!IsShopMenuActive)
                return false;

            try
            {
                // Check the ShopController's state machine
                var shopController = UnityEngine.Object.FindObjectOfType<KeyInputShopController>();
                if (shopController != null)
                {
                    int currentState = GetCurrentState(shopController);

                    // STATE_NONE means shop is closing/closed
                    if (currentState == STATE_NONE)
                    {
                        ClearState();
                        return false;
                    }

                    // Command menu: Don't suppress - let MenuTextDiscovery + ShopCommandReader handle
                    if (currentState == STATE_SELECT_COMMAND)
                    {
                        return false;
                    }

                    // Item list states: Suppress - specific patches handle with extra data
                    if (currentState == STATE_SELECT_PRODUCT ||
                        currentState == STATE_SELECT_SELL_ITEM ||
                        currentState == STATE_SELECT_ABILITY_TARGET ||
                        currentState == STATE_SELECT_EQUIPMENT ||
                        currentState == STATE_CONFIRMATION_BUY_ITEM ||
                        currentState == STATE_CONFIRMATION_SELL_ITEM ||
                        currentState == STATE_CONFIRMATION_FORGET_MAGIC ||
                        currentState == STATE_CONFIRMATION_BUY_MAGIC)
                    {
                        return true;
                    }
                }

                // Fallback: Check if shop item controller is active (legacy behavior)
                var shopItemController = UnityEngine.Object.FindObjectOfType<ShopListItemContentController>();
                if (shopItemController != null && shopItemController.gameObject.activeInHierarchy)
                {
                    return true;
                }

                // Shop is no longer active
                ClearState();
                return false;
            }
            catch
            {
                ClearState();
                return false;
            }
        }

        /// <summary>
        /// Reads the current state from ShopController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(KeyInputShopController controller)
        {
            try
            {
                IntPtr controllerPtr = controller.Pointer;
                if (controllerPtr == IntPtr.Zero)
                    return -1;

                unsafe
                {
                    // Read stateMachine pointer at offset 0x98
                    IntPtr stateMachinePtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_STATE_MACHINE);
                    if (stateMachinePtr == IntPtr.Zero)
                        return -1;

                    // Read current State<T> pointer at offset 0x10
                    IntPtr currentStatePtr = *(IntPtr*)((byte*)stateMachinePtr.ToPointer() + OFFSET_STATE_MACHINE_CURRENT);
                    if (currentStatePtr == IntPtr.Zero)
                        return -1;

                    // Read Tag (int) at offset 0x10
                    int stateValue = *(int*)((byte*)currentStatePtr.ToPointer() + OFFSET_STATE_TAG);
                    return stateValue;
                }
            }
            catch
            {
                return -1;
            }
        }

        public static void ClearState()
        {
            IsShopMenuActive = false;
            LastItemName = null;
            LastItemDescription = null;
            LastItemPrice = null;
            LastItemStats = null;
            ShopPatches.ResetQuantityTracking();
        }
    }

    /// <summary>
    /// Announces shop item details when 'I' key is pressed.
    /// Announces stats first, then description.
    /// Format: "Defense 3, Magic Defense 1. Armor made of leather."
    /// </summary>
    public static class ShopDetailsAnnouncer
    {
        public static void AnnounceCurrentItemDetails()
        {
            try
            {
                if (!ShopMenuTracker.ValidateState())
                {
                    return;
                }

                // Build announcement: Stats first, then description
                string stats = ShopMenuTracker.LastItemStats;
                string description = ShopMenuTracker.LastItemDescription;

                string announcement = "";

                // Add stats if available
                if (!string.IsNullOrEmpty(stats))
                {
                    announcement = stats;
                }

                // Add description
                if (!string.IsNullOrEmpty(description))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ". " + description;
                    }
                    else
                    {
                        announcement = description;
                    }
                }

                if (string.IsNullOrEmpty(announcement))
                {
                    announcement = "No item details available";
                }

                MelonLogger.Msg($"[Shop Details] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing shop details: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Shop menu patches using manual Harmony patching.
    ///
    /// CRITICAL: FF3 crashes with methods that have string parameters.
    /// All patches here use methods with non-string params only.
    /// </summary>
    public static class ShopPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch ShopListItemContentController.SetFocus(bool) - item selection (buy/sell lists)
                PatchSetFocus(harmony);

                // NOTE: Command menu (Buy/Sell/Equipment/Back) is handled by ShopCommandReader in MenuTextDiscovery.
                // No patch needed here - ValidateState() returns false for STATE_SELECT_COMMAND.

                // Patch ShopTradeWindowController.UpdateCotroller(bool) - quantity changes
                // Note: AddCount/TakeCount are private and IL2CPP doesn't expose them
                PatchTradeWindow(harmony);

                MelonLogger.Msg("[Shop] All shop patches applied successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to apply shop patches: {ex.Message}");
            }
        }

        private static void PatchSetFocus(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type controllerType = typeof(ShopListItemContentController);
                var setFocusMethod = controllerType.GetMethod("SetFocus", new Type[] { typeof(bool) });

                if (setFocusMethod == null)
                {
                    MelonLogger.Warning("[Shop] Could not find ShopListItemContentController.SetFocus(bool)");
                    return;
                }

                harmony.Patch(setFocusMethod,
                    postfix: new HarmonyMethod(typeof(ShopPatches), nameof(SetFocus_Postfix)));

                MelonLogger.Msg("[Shop] Patched ShopListItemContentController.SetFocus");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch SetFocus: {ex.Message}");
            }
        }

        private static void PatchTradeWindow(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type tradeType = typeof(ShopTradeWindowController);

                // UpdateCotroller is public (note: game typo - "Cotroller" not "Controller")
                // This is called after count changes, more reliable than private AddCount/TakeCount
                var updateMethod = tradeType.GetMethod("UpdateCotroller", new Type[] { typeof(bool) });
                if (updateMethod != null)
                {
                    harmony.Patch(updateMethod,
                        postfix: new HarmonyMethod(typeof(ShopPatches), nameof(UpdateCotroller_Postfix)));
                    MelonLogger.Msg("[Shop] Patched ShopTradeWindowController.UpdateCotroller");
                }
                else
                {
                    MelonLogger.Warning("[Shop] Could not find ShopTradeWindowController.UpdateCotroller");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Failed to patch trade window: {ex.Message}");
            }
        }

        // ============ Postfix Methods ============

        // Track last announcement to prevent duplicates from list recycling
        private static string lastAnnouncedText = "";
        private static float lastAnnouncedTime = 0f;
        private const float DIFFERENT_ITEM_DEBOUNCE = 0.1f; // 100ms debounce for different items (list recycling)
        private const float SAME_ITEM_DEBOUNCE = 0.15f; // 150ms debounce for same item (menu entry/exit)

        /// <summary>
        /// Called when an item in the shop list gains/loses focus.
        /// Announces item name, price, and caches description and stats for 'I' key.
        /// </summary>
        public static void SetFocus_Postfix(ShopListItemContentController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus || __instance == null)
                    return;

                // Mark shop as active
                ShopMenuTracker.IsShopMenuActive = true;

                // Get item name from iconTextView
                string itemName = null;
                try
                {
                    itemName = __instance.iconTextView?.nameText?.text;
                }
                catch { }

                if (string.IsNullOrEmpty(itemName))
                    return;

                // Strip any icon markup
                itemName = TextUtils.StripIconMarkup(itemName);

                // Get price from shopListItemContentView
                string price = null;
                try
                {
                    price = __instance.shopListItemContentView?.priceText?.text;
                }
                catch { }

                // Get description from Message property (cached for 'I' key)
                string description = null;
                try
                {
                    description = __instance.Message;
                }
                catch { }

                // Get item stats from master data (cached for 'I' key)
                string stats = null;
                try
                {
                    int contentId = __instance.ContentId;
                    if (contentId > 0)
                    {
                        stats = GetItemStats(contentId);
                    }
                }
                catch { }

                // Cache for 'I' key
                ShopMenuTracker.LastItemName = itemName;
                ShopMenuTracker.LastItemPrice = price;
                ShopMenuTracker.LastItemDescription = description;
                ShopMenuTracker.LastItemStats = stats;

                // Build announcement: "Item Name, Price"
                string announcement = string.IsNullOrEmpty(price) ? itemName : $"{itemName}, {price}";

                // Handle list recycling and menu transitions
                float currentTime = UnityEngine.Time.time;
                float timeSinceLastAnnouncement = currentTime - lastAnnouncedTime;

                // If this is a DIFFERENT item announced within debounce window, skip it
                // (this handles virtual list recycling where old items briefly get focus)
                if (announcement != lastAnnouncedText && timeSinceLastAnnouncement < DIFFERENT_ITEM_DEBOUNCE)
                {
                    MelonLogger.Msg($"[Shop] Skipping '{announcement}' - different item within {DIFFERENT_ITEM_DEBOUNCE}s of '{lastAnnouncedText}'");
                    return;
                }

                // If this is the SAME item announced within a short window, skip it
                // (this handles menu entry/exit where SetFocus fires twice)
                if (announcement == lastAnnouncedText && timeSinceLastAnnouncement < SAME_ITEM_DEBOUNCE)
                {
                    MelonLogger.Msg($"[Shop] Skipping '{announcement}' - same item within {SAME_ITEM_DEBOUNCE}s");
                    return;
                }

                lastAnnouncedText = announcement;
                lastAnnouncedTime = currentTime;

                MelonLogger.Msg($"[Shop Item] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in SetFocus_Postfix: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets item stats by looking up master data.
        /// ContentId is the Content system ID - we need to look up the Content
        /// to get TypeValue (the actual weapon/armor ID for master data).
        /// </summary>
        private static string GetItemStats(int contentId)
        {
            try
            {
                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                // Look up the Content to get the actual item ID (TypeValue) and type (TypeId)
                var content = masterManager.GetData<Content>(contentId);
                if (content == null)
                    return null;

                int typeId = content.TypeId;           // ContentType: 1=Item, 2=Weapon, 3=Armor
                int actualItemId = content.TypeValue;  // The actual weapon/armor/item ID

                // Look up stats based on content type
                switch ((ContentType)typeId)
                {
                    case ContentType.Weapon:
                        return GetWeaponStats(masterManager, actualItemId);

                    case ContentType.Armor:
                        return GetArmorStats(masterManager, actualItemId);

                    default:
                        // Regular items and other types don't have equipment stats
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets stats string for a weapon.
        /// Format: "Attack X" (with optional additional stats)
        /// </summary>
        private static string GetWeaponStats(MasterManager masterManager, int weaponId)
        {
            try
            {
                var weapon = masterManager.GetData<Weapon>(weaponId);
                if (weapon == null)
                    return null;

                var stats = new System.Collections.Generic.List<string>();

                int attack = weapon.Attack;
                if (attack > 0)
                    stats.Add($"Attack {attack}");

                int accuracy = weapon.AccuracyRate;
                if (accuracy > 0)
                    stats.Add($"Accuracy {accuracy}");

                int evasion = weapon.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                // Stat bonuses
                if (weapon.Strength > 0) stats.Add($"Strength +{weapon.Strength}");
                if (weapon.Vitality > 0) stats.Add($"Vitality +{weapon.Vitality}");
                if (weapon.Agility > 0) stats.Add($"Agility +{weapon.Agility}");
                if (weapon.Intelligence > 0) stats.Add($"Intelligence +{weapon.Intelligence}");
                if (weapon.Spirit > 0) stats.Add($"Spirit +{weapon.Spirit}");
                if (weapon.Magic > 0) stats.Add($"Magic +{weapon.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] Error getting weapon stats: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets stats string for armor.
        /// Format: "Defense X, Magic Defense Y" (with optional additional stats)
        /// </summary>
        private static string GetArmorStats(MasterManager masterManager, int armorId)
        {
            try
            {
                var armor = masterManager.GetData<Armor>(armorId);
                if (armor == null)
                    return null;

                var stats = new System.Collections.Generic.List<string>();

                int defense = armor.Defense;
                if (defense > 0)
                    stats.Add($"Defense {defense}");

                int magicDefense = armor.AbilityDefense;
                if (magicDefense > 0)
                    stats.Add($"Magic Defense {magicDefense}");

                int evasion = armor.EvasionRate;
                if (evasion > 0)
                    stats.Add($"Evasion {evasion}");

                int magicEvasion = armor.AbilityEvasionRate;
                if (magicEvasion > 0)
                    stats.Add($"Magic Evasion {magicEvasion}");

                // Stat bonuses
                if (armor.Strength > 0) stats.Add($"Strength +{armor.Strength}");
                if (armor.Vitality > 0) stats.Add($"Vitality +{armor.Vitality}");
                if (armor.Agility > 0) stats.Add($"Agility +{armor.Agility}");
                if (armor.Intelligence > 0) stats.Add($"Intelligence +{armor.Intelligence}");
                if (armor.Spirit > 0) stats.Add($"Spirit +{armor.Spirit}");
                if (armor.Magic > 0) stats.Add($"Magic +{armor.Magic}");

                return stats.Count > 0 ? string.Join(", ", stats) : null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Shop] Error getting armor stats: {ex.Message}");
                return null;
            }
        }

        // Memory offsets for ShopTradeWindowController (KeyInput version)
        private const int OFFSET_TRADE_VIEW = 0x30;           // ShopTradeWindowView
        private const int OFFSET_SELECTED_COUNT = 0x3C;       // int selectedCount

        // Memory offsets for ShopTradeWindowView (KeyInput version)
        private const int OFFSET_SELECT_COUNT_TEXT = 0x68;    // Text selectCountText
        private const int OFFSET_TOTAL_PRICE_TEXT = 0x70;     // Text totarlPriceText

        // Track last quantity to avoid duplicate announcements
        private static int lastAnnouncedQuantity = -1;

        /// <summary>
        /// Called when the trade window updates (after quantity changes).
        /// Announces the current quantity and total price.
        /// </summary>
        public static void UpdateCotroller_Postfix(ShopTradeWindowController __instance, bool isCount)
        {
            try
            {
                if (__instance == null)
                    return;

                // Read selectedCount via pointer (offset 0x3C)
                int selectedCount = GetSelectedCount(__instance);

                // Skip if quantity hasn't changed (UpdateCotroller is called frequently)
                if (selectedCount == lastAnnouncedQuantity)
                    return;

                lastAnnouncedQuantity = selectedCount;

                // Read total price text via pointer chain
                string totalPrice = GetTotalPriceText(__instance);

                string announcement = string.IsNullOrEmpty(totalPrice)
                    ? selectedCount.ToString()
                    : $"{selectedCount}, {totalPrice}";

                MelonLogger.Msg($"[Shop Quantity] {announcement}");
                FFIII_ScreenReaderMod.SpeakText(announcement);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Shop] Error in UpdateCotroller_Postfix: {ex.Message}");
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
                    {
                        return *(int*)((byte*)ptr.ToPointer() + OFFSET_SELECTED_COUNT);
                    }
                }
            }
            catch { }
            return 0;
        }

        /// <summary>
        /// Reads the total price text from the trade window view.
        /// Uses pointer chain: controller -> view -> totarlPriceText -> text
        /// </summary>
        private static string GetTotalPriceText(ShopTradeWindowController controller)
        {
            try
            {
                // Try direct access first (IL2CPP wrapper might expose it)
                var view = controller.view;
                if (view != null)
                {
                    var priceText = view.totarlPriceText;
                    if (priceText != null)
                    {
                        return priceText.text;
                    }
                }
            }
            catch
            {
                // Direct access failed, try pointer-based access
                try
                {
                    unsafe
                    {
                        IntPtr controllerPtr = controller.Pointer;
                        if (controllerPtr == IntPtr.Zero) return null;

                        // Read view pointer at offset 0x30
                        IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_TRADE_VIEW);
                        if (viewPtr == IntPtr.Zero) return null;

                        // Read totarlPriceText pointer at offset 0x70
                        IntPtr textPtr = *(IntPtr*)((byte*)viewPtr.ToPointer() + OFFSET_TOTAL_PRICE_TEXT);
                        if (textPtr == IntPtr.Zero) return null;

                        // Wrap as Text component and read text property
                        var textComponent = new UnityEngine.UI.Text(textPtr);
                        return textComponent.text;
                    }
                }
                catch { }
            }
            return null;
        }

        /// <summary>
        /// Resets the quantity tracking when leaving the trade window.
        /// Called from ShopMenuTracker.ClearState().
        /// </summary>
        public static void ResetQuantityTracking()
        {
            lastAnnouncedQuantity = -1;
        }

    }
}
