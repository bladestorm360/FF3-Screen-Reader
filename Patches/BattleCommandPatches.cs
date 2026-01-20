using System;
using System.Collections;
using HarmonyLib;
using MelonLoader;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Battle;
using Il2CppLast.Data.User;
using Il2CppLast.Management;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using BattleMenuWindowController = Il2CppLastDebug.Battle.BattleMenuWindowController;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for battle command menu.
    /// Prevents generic cursor from double-reading commands.
    /// </summary>
    public static class BattleCommandState
    {
        /// <summary>
        /// True when battle command menu is actively handling announcements.
        /// Delegates to MenuStateRegistry for centralized state tracking.
        /// </summary>
        public static bool IsActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_COMMAND);
            set => MenuStateRegistry.SetActive(MenuStateRegistry.BATTLE_COMMAND, value);
        }

        // State machine offset for BattleCommandSelectController (from dump.cs)
        private const int OFFSET_STATE_MACHINE = 0x48;

        // BattleCommandSelectController.State values (from dump.cs line 435181)
        private const int STATE_NONE = 0;
        private const int STATE_NORMAL = 1;     // Main command menu (Attack, Magic, etc.)
        private const int STATE_EXTRA = 2;      // Sub-commands (White Magic, Black Magic, etc.)
        private const int STATE_MANIPULATE = 3; // Enemy manipulation commands

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Simply returns IsActive - flag is cleared explicitly at battle end via BattleResultPatches.
        /// This prevents false clears during state transitions within battle.
        /// </summary>
        public static bool ShouldSuppress()
        {
            return IsActive;
        }

        /// <summary>
        /// Reads the current state from BattleCommandSelectController's state machine.
        /// Returns -1 if unable to read.
        /// </summary>
        private static int GetCurrentState(BattleCommandSelectController controller)
        {
            if (controller == null)
                return -1;
            return StateReaderHelper.ReadStateTag(controller.Pointer, OFFSET_STATE_MACHINE);
        }

        /// <summary>
        /// Clears battle command state.
        /// </summary>
        public static void ClearState()
        {
            IsActive = false;
        }
    }

    /// <summary>
    /// Patch for SetCommandData - announces when a character's turn becomes active.
    /// </summary>
    [HarmonyPatch(typeof(BattleCommandSelectController), nameof(BattleCommandSelectController.SetCommandData))]
    public static class BattleCommandSelectController_SetCommandData_Patch
    {
        private static int lastCharacterId = -1;

        [HarmonyPostfix]
        public static void Postfix(BattleCommandSelectController __instance, OwnedCharacterData data)
        {
            try
            {
                if (data == null) return;

                int characterId = data.Id;
                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Reset tracking for new turn
                BattleTargetPatches.ResetState();
                BattleCommandSelectController_SetCursor_Patch.ResetState();

                // Clear flee-in-progress flag when a player's turn begins
                // If flee succeeded, battle would have ended. If we're here, flee failed.
                GlobalBattleMessageTracker.ClearFleeInProgress();

                string announcement = $"{characterName}'s turn";
                MelonLogger.Msg($"[Battle Turn] {announcement}");
                // Turn announcements can interrupt
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCommandData patch: {ex.Message}");
            }
        }

        public static void ResetState()
        {
            lastCharacterId = -1;
        }
    }

    /// <summary>
    /// Patches for battle command selection (Attack, Magic, Item, Defend, etc.)
    /// Uses string method name since SetCursor is private.
    /// </summary>
    [HarmonyPatch(typeof(BattleCommandSelectController), "SetCursor", new Type[] { typeof(int) })]
    public static class BattleCommandSelectController_SetCursor_Patch
    {
        private const string CONTEXT_CURSOR = "BattleCommand.Cursor";

        [HarmonyPostfix]
        public static void Postfix(BattleCommandSelectController __instance, int index)
        {
            try
            {
                if (__instance == null) return;

                // Mark battle command menu as active for suppression
                // Also clear other menu states to prevent conflicts
                FFIII_ScreenReader.Core.FFIII_ScreenReaderMod.ClearOtherMenuStates("BattleCommand");
                BattleCommandState.IsActive = true;

                // Actively check target selection state (more reliable than just reading the flag)
                bool targetActive = BattleTargetPatches.CheckAndUpdateTargetSelectionActive();

                // Log EVERY SetCursor call to understand what's happening
                MelonLogger.Msg($"[Battle Command] SetCursor called: index={index}, TargetActive={targetActive}");

                // SUPPRESSION: If targeting is active, do not announce commands
                if (targetActive)
                {
                    MelonLogger.Msg($"[Battle Command] SUPPRESSED - target selection active");
                    return;
                }

                // SUPPRESSION: If flee is in progress, do not announce commands
                // This prevents "Defend" being announced when flee action resets cursor to index 0
                if (GlobalBattleMessageTracker.IsFleeInProgress)
                {
                    MelonLogger.Msg($"[Battle Command] SUPPRESSED - flee in progress");
                    return;
                }

                // Skip duplicate announcements
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_CURSOR, index))
                {
                    MelonLogger.Msg($"[Battle Command] SUPPRESSED - duplicate index");
                    return;
                }

                var contentList = __instance.contentList;
                if (contentList == null || contentList.Count == 0) return;
                if (index < 0 || index >= contentList.Count) return;

                var contentController = contentList[index];
                if (contentController == null || contentController.TargetCommand == null) return;

                string mesIdName = contentController.TargetCommand.MesIdName;
                if (string.IsNullOrWhiteSpace(mesIdName)) return;

                var messageManager = MessageManager.Instance;
                if (messageManager == null) return;

                string commandName = messageManager.GetMessage(mesIdName);
                if (string.IsNullOrWhiteSpace(commandName)) return;

                // Immediate speech - no delay needed since we actively check target selection state
                MelonLogger.Msg($"[Battle Command] Speaking: {commandName}");
                FFIII_ScreenReaderMod.SpeakText(commandName, interrupt: false);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCursor patch: {ex.Message}");
            }
        }

        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(CONTEXT_CURSOR);
        }
    }

    /// <summary>
    /// Tracks battle target selection state.
    /// </summary>
    public static class BattleTargetPatches
    {
        private const string CONTEXT_PLAYER = "BattleTarget.Player";
        private const string CONTEXT_ENEMY = "BattleTarget.Enemy";

        /// <summary>
        /// True when target selection is active. Delegates to MenuStateRegistry.
        /// </summary>
        public static bool IsTargetSelectionActive
        {
            get => MenuStateRegistry.IsActive(MenuStateRegistry.BATTLE_TARGET);
            private set => MenuStateRegistry.SetActive(MenuStateRegistry.BATTLE_TARGET, value);
        }

        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(CONTEXT_PLAYER, CONTEXT_ENEMY);
        }

        // Cached reference to avoid FindObjectOfType on every call
        private static BattleTargetSelectController cachedTargetController = null;

        /// <summary>
        /// Checks if target selection is actually active by looking at the controller's gameObject.
        /// This is more reliable than relying on ShowWindow being called.
        /// Optimized to skip expensive checks when flag is already false.
        /// </summary>
        public static bool CheckAndUpdateTargetSelectionActive()
        {
            try
            {
                // Fast path: if flag is false, only do expensive check occasionally
                // The flag gets set to true by SelectContent patches, so we trust that
                if (!IsTargetSelectionActive)
                {
                    return false;
                }

                // Flag is true - verify it's still actually active
                // Try cached reference first
                if (cachedTargetController == null || cachedTargetController.gameObject == null)
                {
                    cachedTargetController = UnityEngine.Object.FindObjectOfType<BattleTargetSelectController>();
                }

                if (cachedTargetController == null)
                {
                    MelonLogger.Msg("[Battle Target] Controller not found, resetting flag to false");
                    IsTargetSelectionActive = false;
                    return false;
                }

                // Check if the controller has active children (view is shown)
                bool isActuallyActive = false;
                var children = cachedTargetController.GetComponentsInChildren<UnityEngine.Transform>(false);
                foreach (var child in children)
                {
                    if (child != null && child.gameObject != cachedTargetController.gameObject)
                    {
                        isActuallyActive = true;
                        break;
                    }
                }

                if (!isActuallyActive)
                {
                    MelonLogger.Msg($"[Battle Target] State mismatch detected: flag=True, actual=False");
                    IsTargetSelectionActive = false;
                }

                return IsTargetSelectionActive;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error checking target selection state: {ex.Message}");
                return IsTargetSelectionActive;
            }
        }

        public static void SetTargetSelectionActive(bool active)
        {
            MelonLogger.Msg($"[Battle Target] SetTargetSelectionActive: {active}");
            IsTargetSelectionActive = active;
            if (active)
            {
                // Only reset target tracking when entering target selection
                // Do NOT reset command cursor state - this prevents "Attack" from being re-announced
                // when returning from target selection to command menu
                ResetState();
            }
        }

        /// <summary>
        /// Check if GenericCursor should be suppressed.
        /// Validates that target selection controller is still active.
        /// Auto-clears stuck flag when battle ends.
        /// </summary>
        public static bool ShouldSuppress()
        {
            if (!IsTargetSelectionActive) return false;

            try
            {
                // Validate target selection controller is still active
                var controller = UnityEngine.Object.FindObjectOfType<BattleTargetSelectController>();
                if (controller == null || !controller.gameObject.activeInHierarchy)
                {
                    // Controller gone (battle ended) - clear stuck flag
                    MelonLogger.Msg("[Battle Target] ShouldSuppress: Controller not active, clearing flag");
                    IsTargetSelectionActive = false;
                    cachedTargetController = null;
                    return false;
                }

                return true;
            }
            catch
            {
                IsTargetSelectionActive = false;
                cachedTargetController = null;
                return false;
            }
        }

        public static void AnnouncePlayerTarget(Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> list, int index)
        {
            try
            {
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_PLAYER, index)) return;
                AnnouncementDeduplicator.Reset(CONTEXT_ENEMY);

                var playerList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>();
                var selectedPlayer = SelectContentHelper.TryGetItem(playerList, index);
                if (selectedPlayer == null) return;

                string name = "Unknown";
                int currentHp = 0, maxHp = 0;

                var ownedCharData = selectedPlayer.ownedCharacterData;
                if (ownedCharData != null)
                {
                    name = ownedCharData.Name;
                    var charParam = ownedCharData.Parameter;
                    if (charParam != null)
                    {
                        try
                        {
                            maxHp = charParam.ConfirmedMaxHp();
                        }
                        catch { }
                    }
                }

                var battleInfo = selectedPlayer.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    if (maxHp == 0)
                    {
                        try
                        {
                            maxHp = battleInfo.Parameter.ConfirmedMaxHp();
                        }
                        catch
                        {
                            maxHp = battleInfo.Parameter.BaseMaxHp;
                        }
                    }
                }

                // Note: FF3 uses spell charges per level, not MP
                string announcement = $"{name}: HP {currentHp}/{maxHp}";
                MelonLogger.Msg($"[Battle Target] {announcement}");
                // Target selection SHOULD interrupt - user confirmed a command and wants to hear the target
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing player target: {ex.Message}");
            }
        }

        public static void AnnounceEnemyTarget(Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list, int index)
        {
            try
            {
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_ENEMY, index)) return;
                AnnouncementDeduplicator.Reset(CONTEXT_PLAYER);

                var enemyList = list.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>();
                var selectedEnemy = SelectContentHelper.TryGetItem(enemyList, index);
                if (selectedEnemy == null) return;

                string name = "Unknown";
                int currentHp = 0, maxHp = 0;

                try
                {
                    string mesIdName = selectedEnemy.GetMesIdName();
                    var messageManager = MessageManager.Instance;
                    if (messageManager != null && !string.IsNullOrEmpty(mesIdName))
                    {
                        string localizedName = messageManager.GetMessage(mesIdName);
                        if (!string.IsNullOrEmpty(localizedName))
                        {
                            name = localizedName;
                        }
                    }
                }
                catch { }

                var battleInfo = selectedEnemy.BattleUnitDataInfo;
                if (battleInfo?.Parameter != null)
                {
                    currentHp = battleInfo.Parameter.CurrentHP;
                    try
                    {
                        maxHp = battleInfo.Parameter.ConfirmedMaxHp();
                    }
                    catch
                    {
                        maxHp = battleInfo.Parameter.BaseMaxHp;
                    }
                }

                // Check for multiple enemies with same name
                int sameNameCount = 0;
                int positionInGroup = 0;
                var messageManagerForCount = MessageManager.Instance;

                for (int i = 0; i < enemyList.Count; i++)
                {
                    var enemy = enemyList[i];
                    if (enemy != null)
                    {
                        try
                        {
                            string enemyMesId = enemy.GetMesIdName();
                            if (!string.IsNullOrEmpty(enemyMesId) && messageManagerForCount != null)
                            {
                                string enemyName = messageManagerForCount.GetMessage(enemyMesId);
                                if (enemyName == name)
                                {
                                    sameNameCount++;
                                    if (i < index) positionInGroup++;
                                }
                            }
                        }
                        catch { }
                    }
                }

                string announcement = name;
                if (sameNameCount > 1)
                {
                    char letter = (char)('A' + positionInGroup);
                    announcement += $" {letter}";
                }
                announcement += $": HP {currentHp}/{maxHp}";

                MelonLogger.Msg($"[Battle Target] {announcement}");
                // Target selection SHOULD interrupt - user confirmed a command and wants to hear the target
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing enemy target: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for when player target selection changes.
    /// Also sets IsTargetSelectionActive since SelectContent is called when target selection is open.
    /// </summary>
    [HarmonyPatch(typeof(BattleTargetSelectController), "SelectContent",
        new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData>), typeof(int) })]
    public static class BattleTargetSelectController_SelectContent_Player_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            MelonLogger.Msg("[Battle Target] SelectContent(Player) Prefix - setting flag true");
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        [HarmonyPostfix]
        public static void Postfix(BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> list, int index)
        {
            try
            {
                BattleTargetPatches.AnnouncePlayerTarget(list, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Player) patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for when enemy target selection changes.
    /// Also sets IsTargetSelectionActive since SelectContent is called when target selection is open.
    /// </summary>
    [HarmonyPatch(typeof(BattleTargetSelectController), "SelectContent",
        new Type[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData>), typeof(int) })]
    public static class BattleTargetSelectController_SelectContent_Enemy_Patch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            MelonLogger.Msg("[Battle Target] SelectContent(Enemy) Prefix - setting flag true");
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        [HarmonyPostfix]
        public static void Postfix(BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> list, int index)
        {
            try
            {
                BattleTargetPatches.AnnounceEnemyTarget(list, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Enemy) patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// DEPRECATED: Attribute-based ShowWindow patch doesn't work reliably in FF3.
    /// Use BattleTargetShowWindowManualPatch instead (applied via TryPatchBattleTargetShowWindow).
    /// </summary>
    // [HarmonyPatch(typeof(BattleTargetSelectController), nameof(BattleTargetSelectController.ShowWindow))]
    // public static class BattleTargetSelectController_ShowWindow_Patch { ... }

    /// <summary>
    /// Manual patch for ShowWindow to track when target selection window is shown/hidden.
    /// Applied via FFIII_ScreenReaderMod.TryPatchBattleTargetShowWindow().
    /// </summary>
    public static class BattleTargetShowWindowManualPatch
    {
        public static void Prefix(object __instance, bool isShow)
        {
            try
            {
                MelonLogger.Msg($"[Battle Target] ShowWindow Manual Prefix: isShow={isShow}");
                BattleTargetPatches.SetTargetSelectionActive(isShow);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowWindow manual patch: {ex.Message}");
            }
        }
    }
}
