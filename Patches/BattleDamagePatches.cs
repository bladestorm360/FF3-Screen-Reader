using System;
using System.Collections.Generic;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.Management;
using Il2CppLast.Battle;
using Il2CppLast.Battle.Function;
using Il2CppLast.Systems;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
using Condition = Il2CppLast.Data.Master.Condition;
using CharacterParameterBase = Il2CppLast.Data.CharacterParameterBase;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Manual registration of the damage-view and condition hooks (attribute patches crash on IL2CPP).
    /// Every target's RVA is unique in dump.cs: CreateDamageView 0x94B6D0, DamageViewUIManager.CreateHitCount
    /// 0x435640, BattleConditionController (Last.Battle, the logic class) Add 0x3A8880, RemoveFunction
    /// 0x3ABE20, BattleEndRecoveryCondition 0x3A8A70.
    /// </summary>
    internal static class BattleDamagePatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, typeof(BattleBasicFunction), "CreateDamageView",
                new[] { typeof(BattleUnitData), typeof(int), typeof(HitType), typeof(bool) },
                postfix: AccessTools.Method(typeof(BattleBasicFunction_CreateDamageView_Patch), nameof(BattleBasicFunction_CreateDamageView_Patch.Postfix)));
            Patch(harmony, typeof(Il2CppLast.UI.DamageViewUIManager), "CreateHitCount", null,
                postfix: AccessTools.Method(typeof(DamageViewUIManager_CreateHitCount_Patch), nameof(DamageViewUIManager_CreateHitCount_Patch.Postfix)));
            Patch(harmony, typeof(BattleConditionController), "Add", null,
                postfix: AccessTools.Method(typeof(BattleConditionController_Add_Patch), nameof(BattleConditionController_Add_Patch.Postfix)));
            Patch(harmony, typeof(BattleConditionController), "RemoveFunction", null,
                prefix: AccessTools.Method(typeof(BattleConditionController_Remove_Patch), nameof(BattleConditionController_Remove_Patch.RemoveFunction_Prefix)));
            Patch(harmony, typeof(BattleConditionController), "BattleEndRecoveryCondition", null,
                prefix: AccessTools.Method(typeof(BattleConditionController_Remove_Patch), nameof(BattleConditionController_Remove_Patch.BattleEndRecoveryCondition_Prefix)));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string name, Type[] args,
            System.Reflection.MethodInfo prefix = null, System.Reflection.MethodInfo postfix = null)
        {
            try
            {
                var target = args != null ? AccessTools.Method(type, name, args) : AccessTools.Method(type, name);
                if (target == null)
                {
                    MelonLogger.Warning($"[Battle] {type.Name}.{name} not found");
                    return;
                }
                harmony.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle] Error patching {type.Name}.{name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Localized name of a condition, or null for hidden/internal conditions (no message id, "None",
        /// or an empty message). Shared by the add and the removal announcements so the wording matches.
        /// </summary>
        internal static string GetConditionName(Condition condition)
        {
            if (condition == null) return null;
            string mesId = condition.MesIdName;
            if (string.IsNullOrEmpty(mesId) || mesId == "None") return null;
            var messageManager = MessageManager.Instance;
            if (messageManager == null) return null;
            string name = messageManager.GetMessage(mesId);
            return string.IsNullOrEmpty(name) ? null : name;
        }
    }

    /// <summary>
    /// Postfix on BattleBasicFunction.CreateDamageView for damage announcements (registered manually in
    /// BattleDamagePatches; the HitType argument is taken as an int).
    /// </summary>
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

        // HitType values (Last.Systems.HitType in dump.cs); the patch takes the enum argument as an int.
        private const int HIT_MISS = 2;
        private const int HIT_ZERO = 3;
        private const int HIT_RECOVERY = 4;
        private const int HIT_MP_HIT = 5;
        private const int HIT_MP_RECOVERY = 6;

        /// <summary>
        /// CreateDamageView(BattleUnitData data, int value, HitType hitType, bool isRecovery), positional.
        /// </summary>
        public static void Postfix(BattleBasicFunction __instance, BattleUnitData __0, int __1, int __2, bool __3)
        {
            try
            {
                BattleUnitData data = __0;
                int value = __1;
                int hitType = __2;
                bool isRecovery = __3;
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
                if (hitType == HIT_MISS)
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
                    //  - status cures are Hit/0 too (RecoveryCondition is never produced by FF3); the
                    //    removal is announced by the BattleConditionController.RemoveFunction patch;
                    //  - Zero is set only for a genuine zero result (DamageAggregater.CheckUndead,
                    //    MagicAbsorptionFunction): "0 damage".
                    if (hitType != HIT_ZERO) return;
                    message = string.Format(T("{0}: {1} damage"), targetName, 0);
                }
                else if (hitType == HIT_MP_RECOVERY)
                {
                    message = string.Format(T("{0}: Recovered {1} MP"), targetName, value);
                }
                else if (hitType == HIT_MP_HIT)
                {
                    message = string.Format(T("{0}: {1} MP damage"), targetName, value);
                }
                else if (hitType == HIT_RECOVERY || isRecovery)
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
    /// Registered manually in BattleDamagePatches.
    /// </summary>
    internal static class DamageViewUIManager_CreateHitCount_Patch
    {
        public static void Postfix(int __0)
        {
            BattleBasicFunction_CreateDamageView_Patch.PendingHitCount = __0;
            BattleBasicFunction_CreateDamageView_Patch.PendingHitCountFrame = UnityEngine.Time.frameCount;
        }
    }

    /// <summary>
    /// Postfix on BattleConditionController.Add(BattleUnitData, int id) (the Last.Battle logic class):
    /// announces a status when its condition function is created. Registered manually in
    /// BattleDamagePatches.
    /// </summary>
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

        /// <summary>
        /// A removal was spoken for this line: the same status coming back must be spoken again.
        /// </summary>
        internal static void ForgetAnnouncement(string announcement)
        {
            if (announcement == lastAnnouncement)
                lastAnnouncement = "";
        }

        public static void Postfix(BattleUnitData __0, int __1)
        {
            try
            {
                BattleUnitData battleUnitData = __0;
                int id = __1;
                if (battleUnitData == null) return;

                string targetName = BattleUnitHelper.GetUnitName(battleUnitData) ?? T("Unknown");

                // Condition name from the unit's confirmed condition list, by id
                string conditionName = null;
                try
                {
                    var unitDataInfo = battleUnitData.BattleUnitDataInfo;
                    var confirmedList = unitDataInfo?.Parameter?.ConfirmedConditionList();
                    if (confirmedList != null)
                    {
                        foreach (var condition in confirmedList)
                        {
                            if (condition != null && condition.Id == id)
                            {
                                // Null for hidden/internal statuses (no message id)
                                conditionName = BattleDamagePatches.GetConditionName(condition);
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    return;
                }
                if (conditionName == null) return;

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

    /// <summary>
    /// Status removal: "{unit}: {condition} removed". Hooked on BattleConditionController.RemoveFunction
    /// (0x3ABE20), the one place a unit's condition function is destroyed. FF3 has two removal routes and
    /// both end there:
    ///  - cures, natural wear-off (BattleConditionFunction.Recovery / NaturalRemove), revive, conflicts
    ///    (ConflictCondition → Cancellation) and the KO clear-out take the condition out of the unit's
    ///    CurrentConditionList; the next CheckConditionFunction diff calls RemoveConditionFunction →
    ///    RemoveFunction for every function left without its condition;
    ///  - Remove(unit, id, isNegate) (InterruptRemoveCondition, BattleEndRecoveryCondition) tail-calls
    ///    RemoveFunction. FF3 passes isNegate = false at all three call sites.
    /// Remove itself is not the FF1-style hook here: cures and wear-off never reach it in FF3.
    /// A prefix, so the function (and its Condition) still exists. RemoveFunction does nothing when the
    /// unit has no function for the id, i.e. when Add never ran, so a status that never applied is
    /// never announced as removed.
    /// </summary>
    internal static class BattleConditionController_Remove_Patch
    {
        // Condition.ConditionType values (Last.Defaine.ConditionType)
        private const int CONDITION_TYPE_UNABLE_FIGHT = 5;   // KO
        private const int CONDITION_TYPE_MINERALIZATION = 11; // Stone

        // Set by BattleEndRecoveryCondition (victory: StartWinResult; escape: EndEscapeFadeOut) and kept
        // until the next battle starts: battle-end clean-up is silent.
        private static bool battleEndCleanup;

        // (unit, condition id) removals already spoken this frame
        private static readonly HashSet<(IntPtr, int)> spokenThisFrame = new HashSet<(IntPtr, int)>();
        private static int spokenFrame = -1;

        /// <summary>Battle start: removals are spoken again.</summary>
        public static void ResetState()
        {
            battleEndCleanup = false;
            spokenThisFrame.Clear();
            spokenFrame = -1;
        }

        public static void BattleEndRecoveryCondition_Prefix()
        {
            battleEndCleanup = true;
        }

        /// <summary>RemoveFunction(BattleUnitData battleUnitData, int id), positional.</summary>
        public static void RemoveFunction_Prefix(BattleUnitData __0, int __1)
        {
            try
            {
                if (battleEndCleanup || __0 == null) return;

                var info = __0.BattleUnitDataInfo;
                var functions = info?.BattleConditionFunction;
                if (functions == null) return;

                // The function being removed: the condition it was created for
                Condition removed = null;
                for (int i = 0; i < functions.Count; i++)
                {
                    var c = functions[i]?.condition;
                    if (c != null && c.Id == __1) { removed = c; break; }
                }
                if (removed == null) return; // no function: nothing is removed (never applied)

                string conditionName = BattleDamagePatches.GetConditionName(removed);
                if (conditionName == null) return; // hidden/internal condition

                var parameter = info.Parameter;
                int removedType = removed.ConditionType;

                // KO / Stone clearing the unit's other statuses: the unit still has the incapacitating
                // condition when its other functions are removed.
                if (HasIncapacitatingCondition(parameter, removed.Id)) return;

                // KO dropped from a unit that is still at 0 HP is clean-up, not a revive
                if (removedType == CONDITION_TYPE_UNABLE_FIGHT && parameter != null && parameter.CurrentHP <= 0) return;

                int frame = UnityEngine.Time.frameCount;
                if (frame != spokenFrame)
                {
                    spokenFrame = frame;
                    spokenThisFrame.Clear();
                }
                if (!spokenThisFrame.Add((__0.Pointer, __1))) return;

                string targetName = BattleUnitHelper.GetUnitName(__0) ?? T("Unknown");
                BattleConditionController_Add_Patch.ForgetAnnouncement($"{targetName}: {conditionName}");

                // Game event: never interrupts
                FFIII_ScreenReaderMod.SpeakText(string.Format(T("{0}: {1} removed"), targetName, conditionName), interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in BattleConditionController.RemoveFunction patch: {ex.Message}");
            }
        }

        /// <summary>
        /// True if the unit's current conditions include KO or Stone other than the one being removed.
        /// </summary>
        private static bool HasIncapacitatingCondition(CharacterParameterBase parameter, int removedId)
        {
            var current = parameter?.CurrentConditionList;
            if (current == null) return false;
            for (int i = 0; i < current.Count; i++)
            {
                var c = current[i];
                if (c == null || c.Id == removedId) continue;
                int type = c.ConditionType;
                if (type == CONDITION_TYPE_UNABLE_FIGHT || type == CONDITION_TYPE_MINERALIZATION)
                    return true;
            }
            return false;
        }
    }
}
