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
using static FFIII_ScreenReader.Utils.ModTextTranslator;
using BattlePlayerData = Il2Cpp.BattlePlayerData;
using GameCursor = Il2CppLast.UI.Cursor;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// State tracker for battle command menu.
    /// Prevents generic cursor from double-reading commands.
    /// </summary>
    internal static class BattleCommandState
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.BATTLE_COMMAND);

        public static bool IsActive
        {
            get => _helper.IsActive;
            set => _helper.IsActive = value;
        }

        public static bool ShouldSuppress() => IsActive;

        /// <summary>
        /// The character whose command turn is active. Set on every SetCommandData (including a
        /// same-character re-entry), cleared at battle end. Scopes the H key / mod-mode X readout.
        /// </summary>
        public static OwnedCharacterData CurrentActor { get; set; } = null;
    }

    /// <summary>
    /// Manual registration (attribute patches crash on IL2CPP) of the battle command and target hooks.
    /// KeyInput BattleCommandSelectController: SetCommandData 0x3C2B20 (prefix closes the command-announce
    /// window before the body runs, so the cursor resets fired during the actor handoff are suppressed by
    /// the SetCursor postfix; postfix announces the turn), SetCursor(int) 0x3C3930. KeyInput
    /// BattleTargetSelectController.SelectContent(IEnumerable&lt;BattlePlayerData&gt;, int) 0x8A16B0 and
    /// (IEnumerable&lt;BattleEnemyData&gt;, int) 0x8A15A0. All RVAs unique in dump.cs.
    /// </summary>
    internal static class BattleCommandManualPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            Patch(harmony, typeof(BattleCommandSelectController), "SetCommandData", null,
                AccessTools.Method(typeof(BattleCommandManualPatches), nameof(SetCommandData_Prefix)),
                AccessTools.Method(typeof(BattleCommandSelectController_SetCommandData_Patch), nameof(BattleCommandSelectController_SetCommandData_Patch.Postfix)));
            Patch(harmony, typeof(BattleCommandSelectController), "SetCursor", new[] { typeof(int) },
                null,
                AccessTools.Method(typeof(BattleCommandSelectController_SetCursor_Patch), nameof(BattleCommandSelectController_SetCursor_Patch.Postfix)));
            Patch(harmony, typeof(BattleTargetSelectController), "SelectContent",
                new[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData>), typeof(int) },
                AccessTools.Method(typeof(BattleTargetSelectController_SelectContent_Player_Patch), nameof(BattleTargetSelectController_SelectContent_Player_Patch.Prefix)),
                AccessTools.Method(typeof(BattleTargetSelectController_SelectContent_Player_Patch), nameof(BattleTargetSelectController_SelectContent_Player_Patch.Postfix)));
            Patch(harmony, typeof(BattleTargetSelectController), "SelectContent",
                new[] { typeof(Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData>), typeof(int) },
                AccessTools.Method(typeof(BattleTargetSelectController_SelectContent_Enemy_Patch), nameof(BattleTargetSelectController_SelectContent_Enemy_Patch.Prefix)),
                AccessTools.Method(typeof(BattleTargetSelectController_SelectContent_Enemy_Patch), nameof(BattleTargetSelectController_SelectContent_Enemy_Patch.Postfix)));
        }

        private static void Patch(HarmonyLib.Harmony harmony, Type type, string name, Type[] args,
            System.Reflection.MethodInfo prefix, System.Reflection.MethodInfo postfix)
        {
            try
            {
                var method = args != null ? AccessTools.Method(type, name, args) : AccessTools.Method(type, name);
                if (method == null)
                {
                    MelonLogger.Warning($"[Battle Command] {type.Name}.{name} not found");
                    return;
                }
                harmony.Patch(method,
                    prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                    postfix: postfix != null ? new HarmonyMethod(postfix) : null);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Command] Error patching {type.Name}.{name}: {ex.Message}");
            }
        }

        public static void SetCommandData_Prefix()
        {
            try { BattleCommandSelectController_SetCursor_Patch.OnTurnHandoff(); }
            catch (Exception ex) { MelonLogger.Warning($"[Battle Command] Error in SetCommandData prefix: {ex.Message}"); }
        }
    }

    /// <summary>
    /// SetCommandData postfix - announces when a character's turn becomes active.
    /// (Registered, with its prefix, in BattleCommandManualPatches.)
    /// </summary>
    internal static class BattleCommandSelectController_SetCommandData_Patch
    {
        private static int lastCharacterId = -1;

        /// <summary>SetCommandData(OwnedCharacterData data, ...), positional.</summary>
        public static void Postfix(BattleCommandSelectController __instance, OwnedCharacterData __0)
        {
            try
            {
                OwnedCharacterData data = __0;
                if (data == null) return;

                // Open the window before any early-return below: a same-character re-entry (e.g. after
                // canceling a target back to the command menu) is still that actor's input turn.
                BattleCommandSelectController_SetCursor_Patch.OnTurnStart();
                BattleTargetPatches.ResetInitialTargetRead();
                BattleCommandState.CurrentActor = data;

                int characterId = data.Id;
                if (characterId == lastCharacterId) return;
                lastCharacterId = characterId;

                string characterName = data.Name;
                if (string.IsNullOrEmpty(characterName)) return;

                // Reset tracking for new turn
                BattleTargetPatches.ResetState();

                // Clear flee-in-progress flag when a player's turn begins
                // If flee succeeded, battle would have ended. If we're here, flee failed.
                GlobalBattleMessageTracker.ClearFleeInProgress();

                // Turn announcements can interrupt
                FFIII_ScreenReaderMod.SpeakText(string.Format(T("{0}'s turn"), characterName), interrupt: true);
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
    /// Battle command selection (Attack, Magic, Item, Defend, etc.): postfix on the private
    /// SetCursor(int), registered in BattleCommandManualPatches.
    /// </summary>
    internal static class BattleCommandSelectController_SetCursor_Patch
    {
        // Command-announce window. Opened by the SetCommandData postfix ("X's turn"); closed by the
        // SetCommandData prefix and by ShowWindow(false) (the actor's commit/teardown, where the spurious
        // "Attack" bursts fire). SetCursor only announces while this is true.
        private static bool commandTurnReady;

        // Message id of the last command announced. Keyed on command identity, not the cursor index:
        // left/right switches between the Normal and Extra pages while the index stays the same, which an
        // index-based dedup would wrongly swallow. Reset each turn so the first command always speaks.
        private static string lastAnnouncedCmdMesId;

        // Back-out re-announce one-shot. Armed when the player leaves the command menu for a sub-context
        // (targeting, or the magic/item list). A commit's teardown burst is identical to a cancel-return at
        // the SetCursor instant, so the next SetCursor defers one frame and speaks only if no commit signal
        // (ShowWindow(false) / SetCommandData handoff) appeared.
        private static bool commandReannouncePending;

        // Bumped on every SetCursor so a later cursor event supersedes a pending deferred re-announce.
        private static int reannounceGen;

        // Bumped on every SetCommandData prefix: a change while the re-announce is deferred means a new
        // turn started (the commit signal).
        private static int setCommandDataSeq;

        public static void OnTurnHandoff()
        {
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            commandReannouncePending = false; // never carry a back-out arm across the handoff
            setCommandDataSeq++;
        }

        public static void OnTurnStart()
        {
            commandTurnReady = true;
        }

        /// <summary>Target window closed: the actor committed or the target was torn down.</summary>
        public static void OnTargetWindowClosed()
        {
            commandTurnReady = false;
        }

        /// <summary>
        /// Called when the target or the magic/item list (a command sub-context) is announced. Reopens the
        /// command-announce window (a magic/item target cancel's ShowWindow(false) closed it) and arms the
        /// back-out re-announce. Commit-safe: these never announce during a commit.
        /// </summary>
        public static void NotifyCommandSubmenuActive()
        {
            commandTurnReady = true;
            commandReannouncePending = true;
        }

        /// <summary>Arms the back-out re-announce without reopening the window (target announce).</summary>
        public static void ArmReannounce()
        {
            commandReannouncePending = true;
        }

        /// <summary>SetCursor(int index), positional.</summary>
        public static void Postfix(BattleCommandSelectController __instance, int __0)
        {
            try
            {
                int index = __0;
                if (__instance == null) return;

                int myGen = ++reannounceGen;

                // The cursor resets to index 0 (Attack) one frame before the command menu goes inactive at
                // end-of-turn; don't speak "Attack" in that window.
                if (!__instance.gameObject.activeInHierarchy) return;

                // Turn-window gate: only announce between a turn's "X's turn" and the next handoff.
                if (!commandTurnReady) return;

                // Check targeting BEFORE claiming the command state: SetActiveExclusive clears the
                // BATTLE_TARGET flag this check reads, which made target suppression a no-op.
                if (BattleTargetPatches.CheckAndUpdateTargetSelectionActive())
                    return;

                // Mark battle command menu as active for suppression and clear other menu states
                MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.BATTLE_COMMAND);

                // SUPPRESSION: If flee is in progress, do not announce commands
                // This prevents "Defend" being announced when flee action resets cursor to index 0
                if (GlobalBattleMessageTracker.IsFleeInProgress)
                    return;

                if (commandReannouncePending)
                {
                    commandReannouncePending = false;
                    CoroutineManager.StartManaged(DeferredCommandReannounce(__instance, index, myGen, setCommandDataSeq));
                    return;
                }

                AnnounceCommandAt(__instance, index);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SetCursor patch: {ex.Message}");
            }
        }

        /// <summary>
        /// One-frame-deferred command back-out re-announce: speaks only if no commit signal appeared
        /// (a commit's ShowWindow(false) closes the window in the same frame, a turn handoff bumps the
        /// sequence) and no newer cursor event superseded it.
        /// </summary>
        private static IEnumerator DeferredCommandReannounce(BattleCommandSelectController controller, int index, int gen, int seq)
        {
            yield return null;

            if (gen != reannounceGen) yield break;
            if (!commandTurnReady) yield break;
            if (seq != setCommandDataSeq) yield break;
            if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy) yield break;

            // Confirmed cancel-return: clear the dedup once so the focused command speaks again.
            lastAnnouncedCmdMesId = null;
            AnnounceCommandAt(controller, index);
        }

        /// <summary>
        /// Announces the command at contentList[index] with its position among the active command slots,
        /// deduped by command identity.
        /// </summary>
        private static void AnnounceCommandAt(BattleCommandSelectController controller, int index)
        {
            var contentList = controller.contentList;
            if (contentList == null || contentList.Count == 0) return;
            if (index < 0 || index >= contentList.Count) return;

            var contentController = contentList[index];
            if (contentController == null || contentController.TargetCommand == null) return;

            string mesIdName = contentController.TargetCommand.MesIdName;
            if (string.IsNullOrWhiteSpace(mesIdName)) return;
            if (mesIdName == lastAnnouncedCmdMesId) return;

            var messageManager = MessageManager.Instance;
            if (messageManager == null) return;

            string commandName = TextUtils.StripIconMarkup(messageManager.GetMessage(mesIdName));
            if (string.IsNullOrWhiteSpace(commandName)) return;

            // contentList is a fixed slot list: count only populated, active slots so unused slots (or a
            // stale Extra-page leftover) don't inflate the "(X of Y)" total.
            int visibleCount = 0;
            for (int i = 0; i < contentList.Count; i++)
            {
                try
                {
                    var cc = contentList[i];
                    if (cc != null && cc.TargetCommand != null && cc.gameObject.activeInHierarchy)
                        visibleCount++;
                }
                catch { }
            }
            if (visibleCount <= 0) visibleCount = contentList.Count;

            lastAnnouncedCmdMesId = mesIdName;
            // Command selection doesn't interrupt - queues after turn announcement
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(commandName, index, visibleCount), interrupt: false);
        }

        /// <summary>
        /// Clears the turn window and re-announce state (battle end).
        /// </summary>
        public static void ResetState()
        {
            commandTurnReady = false;
            lastAnnouncedCmdMesId = null;
            commandReannouncePending = false;
        }
    }

    /// <summary>
    /// Tracks battle target selection state.
    /// </summary>
    internal static class BattleTargetPatches
    {
        private static readonly MenuStateHelper _helper = new(MenuStateRegistry.BATTLE_TARGET, AnnouncementContexts.BATTLE_TARGET_PLAYER, AnnouncementContexts.BATTLE_TARGET_ENEMY);
        private const string CONTEXT_PLAYER = AnnouncementContexts.BATTLE_TARGET_PLAYER;
        private const string CONTEXT_ENEMY = AnnouncementContexts.BATTLE_TARGET_ENEMY;

        // Frames to retry the initial-focus read after a target state's Init, until cursor + list are built.
        private const int INITIAL_READ_MAX_FRAMES = 30;

        static BattleTargetPatches()
        {
            _helper.RegisterResetHandler();
        }

        public static bool IsTargetSelectionActive
        {
            get => _helper.IsActive;
            private set => _helper.IsActive = value;
        }

        /// <summary>
        /// Patches the single-target state entries (EnemysInit / PlayerInit). These fire for every
        /// single-target open, including plain Attack (which never calls ShowWindow), and on re-entry
        /// within the same turn. SelectContent only fires on cursor movement, so without this the
        /// initially focused target is never spoken.
        /// </summary>
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(BattleTargetSelectController), "EnemysInit",
                typeof(BattleTargetPatches), nameof(EnemysInit_Postfix), "[Battle Target]");
            HarmonyPatchHelper.PatchPostfix(harmony, typeof(BattleTargetSelectController), "PlayerInit",
                typeof(BattleTargetPatches), nameof(PlayerInit_Postfix), "[Battle Target]");
        }

        /// <summary>
        /// Resets dedup contexts between turns without deactivating target selection.
        /// </summary>
        public static void ResetState()
        {
            AnnouncementDeduplicator.Reset(CONTEXT_PLAYER, CONTEXT_ENEMY);
        }

        // Cached reference to avoid FindObjectOfType on every call
        private static BattleTargetSelectController cachedTargetController = null;

        // Bumped on every target state entry / turn start so a stale initial-read coroutine stops.
        private static int initialReadGen;

        // Frame of the last spoken target. EnemysInit calls SelectContent(enemies) itself (0x89DB09),
        // so on enemy entry the SelectContent postfix may already have spoken the focused target
        // inside the Init body; the dedup reset below is skipped then to avoid reading it twice.
        private static int lastTargetSpokenFrame = -1;

        /// <summary>Cancels any pending initial-target read (new turn).</summary>
        public static void ResetInitialTargetRead()
        {
            initialReadGen++;
        }

        public static void EnemysInit_Postfix(object __instance) => StartInitialTargetRead(__instance, isEnemy: true);

        public static void PlayerInit_Postfix(object __instance) => StartInitialTargetRead(__instance, isEnemy: false);

        private static void StartInitialTargetRead(object instance, bool isEnemy)
        {
            try
            {
                var controller = instance as BattleTargetSelectController;
                if (controller == null) return;

                int gen = ++initialReadGen;

                // EnemysInit calls SelectContent(enemies) itself (0x89DB09): if its postfix already spoke
                // the focused target inside this Init, there is nothing left to read.
                if (lastTargetSpokenFrame == UnityEngine.Time.frameCount)
                    return;

                // A state entry is a fresh targeting pass: clear the index dedup so a focused index
                // equal to the last one (Attack, cancel, Attack; a lone survivor) is still spoken.
                // Plain Attack never calls ShowWindow, and SetCommandData skips its reset on a
                // same-character re-entry, so nothing else clears it here.
                AnnouncementDeduplicator.Reset(CONTEXT_PLAYER, CONTEXT_ENEMY);

                // The Init itself is the event: PlayerInit sets the cursor (BattleCursorUtility.
                // SetTargetPlayer) and EnemysInit selects the content before returning, and
                // StateMachine.Change runs the Init synchronously, so the state, cursor and list are
                // normally readable here. The bounded frame retry is kept only as a fallback.
                if (TryReadInitialTarget(controller, isEnemy)) return;
                CoroutineManager.StartManaged(ReadInitialTargetWhenReady(controller, isEnemy, gen));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error starting initial target read: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads the initially focused target once the controller is still in the single-target state and
        /// its cursor + list are populated (retries a bounded number of frames).
        /// </summary>
        private static IEnumerator ReadInitialTargetWhenReady(BattleTargetSelectController controller, bool isEnemy, int gen)
        {
            for (int frame = 0; frame < INITIAL_READ_MAX_FRAMES; frame++)
            {
                yield return null;
                if (gen != initialReadGen) yield break;
                if (TryReadInitialTarget(controller, isEnemy)) yield break;
            }
        }

        private static bool TryReadInitialTarget(BattleTargetSelectController controller, bool isEnemy)
        {
            try
            {
                if (controller == null || controller.gameObject == null) return false;

                // Attack's targeting path presents an inactive controller (magic/item use an active one), so
                // activeInHierarchy is not required; prefer an active instance if this one is a leftover.
                if (!controller.gameObject.activeInHierarchy)
                {
                    var active = GameObjectCache.GetOrFind<BattleTargetSelectController>();
                    if (active != null && active.gameObject.activeInHierarchy)
                        controller = active;
                }

                IntPtr ptr = controller.Pointer;
                if (ptr == IntPtr.Zero) return false;

                // Only read while the controller is still in the single-target state that armed us.
                int expectedState = isEnemy ? IL2CppOffsets.BattleTarget.STATE_ENEMYS : IL2CppOffsets.BattleTarget.STATE_PLAYERS;
                if (StateReaderHelper.ReadStateTag(ptr, IL2CppOffsets.BattleTarget.OFFSET_STATE_MACHINE) != expectedState)
                    return false;

                IntPtr cursorPtr = StateReaderHelper.ReadPointerField(ptr, IL2CppOffsets.BattleTarget.OFFSET_SELECT_CURSOR);
                if (cursorPtr == IntPtr.Zero) return false;
                int index = new GameCursor(cursorPtr).Index;
                if (index < 0) return false;

                if (isEnemy)
                {
                    var list = ReadList<BattleEnemyData>(ptr, IL2CppOffsets.BattleTarget.OFFSET_ENEMY_DATA_LIST);
                    if (list == null || list.Count == 0)
                        list = ReadList<BattleEnemyData>(ptr, IL2CppOffsets.BattleTarget.OFFSET_TARGET_ENEMY_LIST);
                    if (list == null || index >= list.Count) return false;
                    AnnounceEnemyTarget(list, index);
                }
                else
                {
                    var list = ReadList<BattlePlayerData>(ptr, IL2CppOffsets.BattleTarget.OFFSET_PLAYER_DATA_LIST);
                    if (list == null || list.Count == 0)
                        list = ReadList<BattlePlayerData>(ptr, IL2CppOffsets.BattleTarget.OFFSET_TARGET_PLAYER_LIST);
                    if (list == null || index >= list.Count) return false;
                    AnnouncePlayerTarget(list, index);
                }
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Battle Target] Error reading initial target: {ex.Message}");
                return true; // don't keep retrying a read that throws
            }
        }

        /// <summary>Reads an IEnumerable&lt;T&gt; field as a List&lt;T&gt; (null if absent / not a List).</summary>
        private static Il2CppSystem.Collections.Generic.List<T> ReadList<T>(IntPtr controllerPtr, int offset)
            where T : Il2CppSystem.Object
        {
            IntPtr p = StateReaderHelper.ReadPointerField(controllerPtr, offset);
            return p != IntPtr.Zero
                ? new Il2CppSystem.Object(p).TryCast<Il2CppSystem.Collections.Generic.List<T>>()
                : null;
        }

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
                    cachedTargetController = GameObjectCache.GetOrFind<BattleTargetSelectController>();
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
        /// Target window shown/hidden (ShowWindow prefix). Hide = the actor committed or the target was
        /// torn down, so close the command-announce window (suppresses the handoff "Attack" bursts).
        /// </summary>
        public static void OnShowWindow(bool isShow)
        {
            SetTargetSelectionActive(isShow);
            if (!isShow)
            {
                BattleCommandSelectController_SetCursor_Patch.OnTargetWindowClosed();
                cachedTargetController = null;
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
                var controller = GameObjectCache.GetOrFind<BattleTargetSelectController>();
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

        public static void AnnouncePlayerTarget(Il2CppSystem.Collections.Generic.List<BattlePlayerData> playerList, int index)
        {
            try
            {
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_PLAYER, index)) return;
                AnnouncementDeduplicator.Reset(CONTEXT_ENEMY);

                var selectedPlayer = SelectContentHelper.TryGetItem(playerList, index);
                if (selectedPlayer == null) return;

                string name = T("Unknown");
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
                string announcement = string.Format(T("{0}: HP {1}/{2}"), name, currentHp, maxHp);
                announcement = MenuPosition.Format(announcement, index, playerList.Count);

                // Entering targeting = left the command menu; arm the command back-out re-announce. Safe on
                // commit: the SetCursor postfix returns on the targetActive check while the target is up.
                BattleCommandSelectController_SetCursor_Patch.ArmReannounce();
                lastTargetSpokenFrame = UnityEngine.Time.frameCount;
                // Target selection SHOULD interrupt - user confirmed a command and wants to hear the target
                FFIII_ScreenReaderMod.SpeakText(announcement, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error announcing player target: {ex.Message}");
            }
        }

        public static void AnnounceEnemyTarget(Il2CppSystem.Collections.Generic.List<BattleEnemyData> enemyList, int index)
        {
            try
            {
                if (!AnnouncementDeduplicator.ShouldAnnounce(CONTEXT_ENEMY, index)) return;
                AnnouncementDeduplicator.Reset(CONTEXT_PLAYER);

                var selectedEnemy = SelectContentHelper.TryGetItem(enemyList, index);
                if (selectedEnemy == null) return;

                string name = T("Unknown");
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

                // Apply HP display format based on user preference
                int hpMode = PreferencesManager.EnemyHPDisplay;
                switch (hpMode)
                {
                    case 0: // Numbers
                        announcement += string.Format(T(": HP {0}/{1}"), currentHp, maxHp);
                        break;
                    case 1: // Percentage
                        int pct = maxHp > 0 ? (currentHp * 100 / maxHp) : 0;
                        announcement += $": {pct}%";
                        break;
                    case 2: // Hidden
                        break; // No HP appended
                }

                announcement = MenuPosition.Format(announcement, index, enemyList.Count);

                // Entering targeting = left the command menu; arm the command back-out re-announce.
                BattleCommandSelectController_SetCursor_Patch.ArmReannounce();
                lastTargetSpokenFrame = UnityEngine.Time.frameCount;
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
    /// Player target selection changes (registered in BattleCommandManualPatches).
    /// Also sets IsTargetSelectionActive since SelectContent is called when target selection is open.
    /// </summary>
    internal static class BattleTargetSelectController_SelectContent_Player_Patch
    {
        public static void Prefix()
        {
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        /// <summary>SelectContent(IEnumerable&lt;BattlePlayerData&gt; list, int index), positional.</summary>
        public static void Postfix(BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattlePlayerData> __0, int __1)
        {
            try
            {
                BattleTargetPatches.AnnouncePlayerTarget(
                    __0.TryCast<Il2CppSystem.Collections.Generic.List<BattlePlayerData>>(), __1);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Player) patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Enemy target selection changes (registered in BattleCommandManualPatches).
    /// Also sets IsTargetSelectionActive since SelectContent is called when target selection is open.
    /// </summary>
    internal static class BattleTargetSelectController_SelectContent_Enemy_Patch
    {
        public static void Prefix()
        {
            BattleTargetPatches.SetTargetSelectionActive(true);
        }

        /// <summary>SelectContent(IEnumerable&lt;BattleEnemyData&gt; list, int index), positional.</summary>
        public static void Postfix(BattleTargetSelectController __instance,
            Il2CppSystem.Collections.Generic.IEnumerable<BattleEnemyData> __0, int __1)
        {
            try
            {
                BattleTargetPatches.AnnounceEnemyTarget(
                    __0.TryCast<Il2CppSystem.Collections.Generic.List<BattleEnemyData>>(), __1);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in SelectContent(Enemy) patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Manual patch for ShowWindow to track when target selection window is shown/hidden.
    /// Applied via FFIII_ScreenReaderMod.TryPatchBattleTargetShowWindow().
    /// KeyInput BattleTargetSelectController.ShowWindow (0x2F2990) shares its body with 9 other bool
    /// setters (CharacterContentController.SetEnableCharacterContent, NameContentView.SetEnableNameText,
    /// ResultSkillView.SetEnableSkillView, ShopCharaStatusContentController.SetActive, ...), so the
    /// prefix checks the native class before acting.
    /// </summary>
    internal static class BattleTargetShowWindowManualPatch
    {
        public static void Prefix(BattleTargetSelectController __instance, bool __0)
        {
            try
            {
                if (!HarmonyPatchHelper.IsNativeInstanceOf<BattleTargetSelectController>(__instance)) return;
                BattleTargetPatches.OnShowWindow(__0);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error in ShowWindow manual patch: {ex.Message}");
            }
        }
    }
}
