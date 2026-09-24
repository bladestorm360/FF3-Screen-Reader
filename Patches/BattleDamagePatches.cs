using System;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

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
        // Frame the multiplier was captured on. Used to reject a stale count that was never
        // consumed by a CreateDamageView (e.g. a fully-evaded multi-hit) so it can't leak into
        // an unrelated later attack's damage announcement.
        public static int PendingHitCountFrame = -1;

        // BattleBaseFunction.<battleActData>k__BackingField — a protected property, so read by offset.
        private const int OFFSET_BATTLE_ACT_DATA = 0x28;
        // Ability.TypeId of weapon attacks. BattleBasicFunction.CreateHitCount only draws the ×N for
        // this type, so the calculated-hit-count fallback follows the same rule.
        private const int WEAPON_ABILITY_TYPE = 4;

        /// <summary>
        /// The attack's own hit count against this target, from the function's calculation results
        /// (ICalcResultDic → ICalcResult.GetHitCount). Used when no on-screen ×N was paired with the
        /// damage view. Weapon attacks only; 1 for anything else or on any failure.
        /// </summary>
        private static int ReadWeaponHitCount(BattleBasicFunction function, BattleUnitData target)
        {
            try
            {
                if (function == null || target == null) return 1;
                IntPtr actPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(function.Pointer, OFFSET_BATTLE_ACT_DATA);
                if (actPtr == IntPtr.Zero) return 1;
                var abilities = new BattleActData(actPtr).abilityList;
                if (abilities == null || abilities.Count == 0 || abilities[0] == null
                    || abilities[0].TypeId != WEAPON_ABILITY_TYPE)
                    return 1;
                var results = function.ICalcResultDic;
                if (results == null || !results.ContainsKey(target)) return 1;
                var result = results[target];
                return result != null ? Math.Max(1, result.GetHitCount()) : 1;
            }
            catch
            {
                return 1;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(BattleBasicFunction __instance, BattleUnitData data, int value, HitType hitType, bool isRecovery)
        {
            try
            {
                if (data == null) return;

                string targetName = BattleUnitHelper.GetUnitName(data) ?? T("Unknown");

                // Consume the multi-hit count captured by CreateHitCount (it fires just before this
                // view, on the same or adjacent frame). Reject a stale count from an earlier action
                // that never produced a damage view, then reset to 1 so a later damage with no fresh
                // hit count defaults to single. When no ×N was paired with this view, fall back to the
                // attack's own calculated hit count.
                bool fresh = UnityEngine.Time.frameCount - PendingHitCountFrame <= 1;
                int hitCount = fresh ? PendingHitCount : 1;
                PendingHitCount = 1;
                if (hitCount <= 1)
                    hitCount = ReadWeaponHitCount(__instance, data);

                string message;
                if (hitType == HitType.Miss)
                {
                    message = string.Format(T("{0}: Miss"), targetName);
                }
                else if (value == 0)
                {
                    // Value-0 views, settled offline from GameAssembly.dll (debug.md, "Open-issues pass
                    // (2026-09-23, session 2)"):
                    //  - buffs/debuffs (AddCondition*, RandomAddCondition, Kill, UserAddCondition) carry
                    //    Hit on success, Miss on failure; the condition itself is announced by the
                    //    BattleConditionController.Add patch, so stay silent;
                    //  - Zero is set only for a genuine zero result (DamageAggregater.CheckUndead,
                    //    MagicAbsorptionFunction): "0 damage";
                    //  - RecoveryCondition is never set by FF3: a status cure on a target that has the
                    //    condition is GetFixedStatus(Hit), the same Hit/0 as other non-damage effects, so
                    //    it cannot be told apart here. The branch is kept for parity with the other mods.
                    MelonLogger.Msg($"[Battle] value-0 view: hitType={(int)hitType} isRecovery={isRecovery} target={targetName}");
                    if (hitType == HitType.Zero)
                        message = string.Format(T("{0}: {1} damage"), targetName, 0);
                    else if (hitType == HitType.RecoveryCondition)
                        message = string.Format(T("{0}: cured"), targetName);
                    else
                        return;
                }
                else if (hitType == HitType.MPRecovery)
                {
                    message = string.Format(T("{0}: Recovered {1} MP"), targetName, value);
                }
                else if (hitType == HitType.MPHit)
                {
                    message = string.Format(T("{0}: {1} MP damage"), targetName, value);
                }
                else if (hitType == HitType.Recovery || isRecovery)
                {
                    message = string.Format(T("{0}: Recovered {1} HP"), targetName, value);
                }
                else
                {
                    // HP DAMAGE — optionally prepend the multi-hit "{N}x" multiplier (e.g. "14x1552
                    // damage"). The " damage" suffix stays so damage/recovery remain distinguishable.
                    message = (PreferencesManager.DamageDisplay == 1 && hitCount > 1)
                        ? string.Format(T("{0}: {1}x{2} damage"), targetName, hitCount, value)
                        : string.Format(T("{0}: {1} damage"), targetName, value);
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
            BattleBasicFunction_CreateDamageView_Patch.PendingHitCountFrame = UnityEngine.Time.frameCount;
        }
    }

    /// <summary>
    /// Patch BattleConditionController.Add to announce status effects when applied.
    /// </summary>
    [HarmonyPatch(typeof(BattleConditionController), nameof(BattleConditionController.Add))]
    internal static class BattleConditionController_Add_Patch
    {
        private static string lastAnnouncement = "";

        /// <summary>
        /// Clears the last-announcement dedup (called at battle start) so a repeat of the previous
        /// battle's final status line isn't swallowed.
        /// </summary>
        public static void ResetState()
        {
            lastAnnouncement = "";
        }

        [HarmonyPostfix]
        public static void Postfix(BattleUnitData battleUnitData, int id)
        {
            try
            {
                if (battleUnitData == null) return;

                string targetName = BattleUnitHelper.GetUnitName(battleUnitData) ?? T("Unknown");

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
