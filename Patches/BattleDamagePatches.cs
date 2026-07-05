using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using BattlePlayerData = Il2Cpp.BattlePlayerData;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Patch BattleBasicFunction.CreateDamageView for damage announcements.
    /// </summary>
    [HarmonyPatch(typeof(BattleBasicFunction), "CreateDamageView",
        new Type[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool) })]
    internal static class BattleBasicFunction_CreateDamageView_Patch
    {
        // Multi-hit multiplier captured from DamageViewUIManager.CreateHitCount, which fires just
        // before the matching CreateDamageView. Consumed (and reset to 1) by the postfix below.
        internal static int PendingHitCount = 1;

        [HarmonyPostfix]
        public static void Postfix(BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                string targetName = "Unknown";

                var playerData = data.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }

                var enemyData = data.TryCast<BattleEnemyData>();
                if (enemyData != null)
                {
                    string mesIdName = enemyData.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            targetName = localizedName;
                        }
                    }
                }

                // Consume the multi-hit count captured by CreateHitCount (it fires just before this
                // view). Reset to 1 so a later damage with no fresh hit count defaults to single.
                int hitCount = PendingHitCount;
                PendingHitCount = 1;

                string message;
                if (hitType == HitType.Miss || value == 0)
                {
                    message = $"{targetName}: Miss";
                }
                else if (isRecovery)
                {
                    message = $"{targetName}: Recovered {value} HP";
                }
                else
                {
                    // HP DAMAGE — optionally prepend the multi-hit "{N}x" multiplier (e.g. "14x1552
                    // damage"). The " damage" suffix stays so damage/recovery remain distinguishable.
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? $"{targetName}: {hitCount}x{value} damage"
                        : $"{targetName}: {value} damage";
                }

                // Damage doesn't interrupt - queues after action announcement
                FFIII_ScreenReaderMod.SpeakText(message, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in CreateDamageView patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Capture the on-screen "×N" multi-hit multiplier from DamageViewUIManager.CreateHitCount,
    /// which fires just before the matching CreateDamageView, so the damage announce can prepend it
    /// (e.g. "14x1552 damage"). The value is consumed and reset by the CreateDamageView postfix.
    /// Signature: CreateHitCount(int hitCountValue, BattleSpriteEntity attack, BattleSpriteEntity target).
    /// </summary>
    [HarmonyPatch(typeof(Il2CppLast.UI.DamageViewUIManager), "CreateHitCount")]
    internal static class DamageViewUIManager_CreateHitCount_Patch
    {
        [HarmonyPostfix]
        public static void Postfix(int hitCountValue)
        {
            BattleBasicFunction_CreateDamageView_Patch.PendingHitCount = hitCountValue;
        }
    }

    /// <summary>
    /// Patch BattleConditionController.Add to announce status effects when applied.
    /// </summary>
    [HarmonyPatch(typeof(BattleConditionController), nameof(BattleConditionController.Add))]
    internal static class BattleConditionController_Add_Patch
    {
        private static string lastAnnouncement = "";

        [HarmonyPostfix]
        public static void Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                // Get target name
                string targetName = "Unknown";
                var playerData = battleUnitData.TryCast<BattlePlayerData>();
                if (playerData?.ownedCharacterData != null)
                {
                    targetName = playerData.ownedCharacterData.Name;
                }
                else
                {
                    var enemyData = battleUnitData.TryCast<BattleEnemyData>();
                    if (enemyData != null)
                    {
                        string mesIdName = enemyData.GetMesIdName();
                        var messageManager = MessageManager.Instance;
                        if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                        {
                            string localizedName = messageManager.GetMessage(mesIdName);
                            if (!string.IsNullOrEmpty(localizedName))
                            {
                                targetName = localizedName;
                            }
                        }
                    }
                }

                // Get condition name from ID
                string conditionName = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    if (unitDataInfo?.Parameter != null)
                    {
                        var confirmedList = unitDataInfo.Parameter.ConfirmedConditionList();
                        if (confirmedList != null && confirmedList.Count > 0)
                        {
                            foreach (var condition in confirmedList)
                            {
                                if (condition != null && condition.Id == id)
                                {
                                    string conditionMesId = condition.MesIdName;

                                    // Skip conditions with no message ID (internal/hidden statuses)
                                    if (string.IsNullOrEmpty(conditionMesId) || conditionMesId == "None")
                                    {
                                        return;
                                    }

                                    var messageManager = MessageManager.Instance;
                                    if (messageManager != null)
                                    {
                                        string localizedConditionName = messageManager.GetMessage(conditionMesId);
                                        if (!string.IsNullOrEmpty(localizedConditionName))
                                        {
                                            conditionName = localizedConditionName;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    if (conditionName == null)
                    {
                        return;
                    }
                }
                catch
                {
                    return;
                }

                string announcement = $"{targetName}: {conditionName}";

                // Skip duplicates
                if (announcement == lastAnnouncement) return;
                lastAnnouncement = announcement;

                // Status doesn't interrupt
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.Add patch: {ex.Message}");
            }
        }
    }
}
