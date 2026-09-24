using System;
using System.Collections.Generic;
using MelonLoader;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Utils;

using MasterManager = Il2CppLast.Data.Master.MasterManager;
using MessageManager = Il2CppLast.Management.MessageManager;
using JobGroup = Il2CppLast.Data.Master.JobGroup;
using Content = Il2CppLast.Data.Master.Content;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Announces which unlocked jobs can equip the currently-focused weapon/armor.
    /// Triggered by the U key (or right stick left on a controller).
    /// Works in shops (resolves the item from master data by its displayed name) and in the item menu
    /// (delegates to ItemDetailsAnnouncer).
    /// </summary>
    internal static class UsableByAnnouncer
    {
        private const int CONTENT_TYPE_WEAPON = FF3Constants.ItemContentTypes.WEAPON;
        private const int CONTENT_TYPE_ARMOR = FF3Constants.ItemContentTypes.ARMOR;

        public static void AnnounceForCurrentContext()
        {
            try
            {
                if (ShopMenuTracker.ValidateState())
                {
                    string announcement = BuildShopItemAnnouncement();
                    if (!string.IsNullOrEmpty(announcement))
                        FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
                    return;
                }

                if (ItemMenuState.IsItemMenuActive)
                {
                    ItemDetailsAnnouncer.AnnounceEquipRequirements();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the "Can equip: ..." string for the focused shop item.
        /// Returns null if the item isn't equipment or can't be resolved by name.
        /// </summary>
        private static string BuildShopItemAnnouncement()
        {
            string itemName = ShopMenuTracker.LastItemName;
            if (string.IsNullOrEmpty(itemName))
                return null;

            var masterManager = MasterManager.Instance;
            if (masterManager == null)
                return null;

            if (!TryResolveContent(masterManager, itemName, out int itemType, out int itemId))
                return null;
            if (itemType != CONTENT_TYPE_WEAPON && itemType != CONTENT_TYPE_ARMOR)
                return null;

            int equipJobGroupId = ItemDetailsAnnouncer.GetEquipJobGroupId(masterManager, itemType, itemId);
            if (equipJobGroupId <= 0)
                return null;

            var jobGroup = masterManager.GetData<JobGroup>(equipJobGroupId);
            if (jobGroup == null)
                return null;

            var unlockedJobIds = ItemDetailsAnnouncer.GetUnlockedJobIds();
            var canEquipJobs = ItemDetailsAnnouncer.GetEquippableJobs(masterManager, jobGroup, unlockedJobIds);
            return ItemDetailsAnnouncer.BuildAnnouncement(canEquipJobs);
        }

        // Display name → (TypeId, TypeValue); null when the name matched no Content. Names are localized,
        // so a language change simply produces new keys.
        private static readonly Dictionary<string, (int type, int id)?> resolvedContent = new Dictionary<string, (int, int)?>();

        /// <summary>
        /// Iterates the Content master list to resolve a display name into (TypeId, TypeValue).
        /// Content is the unified item table: TypeId is the ContentType (2=weapon, 3=armor, ...),
        /// TypeValue is the ID within the corresponding Weapon/Armor/Item master list.
        /// Matching by display name bypasses ShopListItemContentController.ContentId, which is not a
        /// reliable Content key. Results are cached per name (shops re-query on every focus).
        /// </summary>
        internal static bool TryResolveContent(MasterManager masterManager, string itemName, out int itemType, out int itemId)
        {
            itemType = -1;
            itemId = 0;

            string target = TextUtils.StripIconMarkup(itemName);
            if (string.IsNullOrEmpty(target))
                return false;

            if (!resolvedContent.TryGetValue(target, out var resolved))
            {
                var messageManager = MessageManager.Instance;
                if (messageManager == null)
                    return false;

                resolved = ScanContent(masterManager, messageManager, target);
                resolvedContent[target] = resolved;
            }

            if (!resolved.HasValue)
                return false;

            itemType = resolved.Value.type;
            itemId = resolved.Value.id;
            return true;
        }

        private static (int type, int id)? ScanContent(MasterManager masterManager, MessageManager messageManager, string target)
        {
            try
            {
                var contents = masterManager.GetList<Content>();
                if (contents == null)
                    return null;

                foreach (var kvp in contents)
                {
                    var content = kvp.Value;
                    if (content == null) continue;
                    string mes = content.MesIdName;
                    if (string.IsNullOrEmpty(mes)) continue;
                    string localized = messageManager.GetMessage(mes, false);
                    if (string.IsNullOrEmpty(localized)) continue;
                    if (string.Equals(TextUtils.StripIconMarkup(localized), target, StringComparison.Ordinal))
                        return (content.TypeId, content.TypeValue);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[UsableBy] Content scan failed: {ex.Message}");
            }

            return null;
        }
    }
}
