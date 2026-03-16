using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Menus;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
using Il2CppLast.Management;
using Il2CppLast.UI.KeyInput;

namespace FFIII_ScreenReader.Patches
{
    /// <summary>
    /// Tracks music player (Extra Sound) scene state.
    /// Mirrors BestiaryStateTracker pattern: coroutine clears suppression flag in finally.
    /// </summary>
    public static class MusicPlayerStateTracker
    {
        public static bool IsInMusicPlayer { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;

        // ExtraSoundListController field offsets
        public const int OFFSET_CURRENT_LIST_TYPE = 0xC0;  // currentListType (AudioManager.BgmType)

        public static void ClearState()
        {
            IsInMusicPlayer = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            MenuStateRegistry.Reset(MenuStateRegistry.MUSIC_PLAYER);
            AnnouncementDeduplicator.Reset(AnnouncementContexts.MUSIC_LIST_ENTRY);
        }
    }

    /// <summary>
    /// Patches for the Music Player (Extra Sound) extras menu.
    /// Uses manual Harmony patching (required for FF3 IL2CPP).
    /// </summary>
    internal static class MusicPlayerPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch 1: SubSceneManagerExtraSound.ChangeState
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerExtraSound), "ChangeState");
                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] SubSceneManagerExtraSound.ChangeState not found");
                }

                // Patch 2: ExtraSoundListContentController.SetFocus
                var setFocusMethod = AccessTools.Method(
                    typeof(ExtraSoundListContentController), "SetFocus");
                if (setFocusMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerPatches), nameof(SetFocus_Postfix));
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundListContentController.SetFocus not found");
                }

                // Patch 3: ExtraSoundController.ChangeKeyHelpPlaybackIcon
                var playbackIconMethod = AccessTools.Method(
                    typeof(ExtraSoundController), "ChangeKeyHelpPlaybackIcon");
                if (playbackIconMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(MusicPlayerPatches), nameof(ChangeKeyHelpPlaybackIcon_Postfix));
                    harmony.Patch(playbackIconMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundController.ChangeKeyHelpPlaybackIcon not found");
                }

                // Patch 4: ExtraSoundListController.SwitchOriginalArrangeList
                var switchListMethod = AccessTools.Method(
                    typeof(ExtraSoundListController), "SwitchOriginalArrangeList");
                if (switchListMethod != null)
                {
                    var prefix = AccessTools.Method(typeof(MusicPlayerPatches), nameof(SwitchOriginalArrangeList_Prefix));
                    var postfix = AccessTools.Method(typeof(MusicPlayerPatches), nameof(SwitchOriginalArrangeList_Postfix));
                    harmony.Patch(switchListMethod, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[MusicPlayer] ExtraSoundListController.SwitchOriginalArrangeList not found");
                }

                MelonLogger.Msg("[MusicPlayer] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[MusicPlayer] Error applying patches: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 1: State transitions — SubSceneManagerExtraSound.ChangeState
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeState_Postfix(int state)
        {
            try
            {
                switch (state)
                {
                    case 1: // View — entering music player
                        MusicPlayerStateTracker.IsInMusicPlayer = true;
                        MusicPlayerStateTracker.SuppressContentChange = true;
                        MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.MUSIC_PLAYER);
                        CoroutineManager.StartManaged(AnnounceMusicPlayerEntry());
                        break;

                    case 2: // GotoTitle — leaving music player
                        MusicPlayerStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in ChangeState patch: {ex.Message}");
            }
        }

        private static IEnumerator AnnounceMusicPlayerEntry()
        {
            yield return null;
            FFIII_ScreenReaderMod.SpeakText(T("Music Player"), true);

            float elapsed = 0f;

            while (elapsed < 2f)
            {
                yield return null;
                elapsed += Time.deltaTime;

                try
                {
                    IntPtr focusedPtr = MusicPlayerStateTracker.CachedFocusedPtr;
                    if (focusedPtr != IntPtr.Zero &&
                        MusicPlayerReader.ReadContentFromPointer(focusedPtr, out string name, out int bgmId, out int idx))
                    {
                        string entry = MusicPlayerReader.ReadSongEntry(name, bgmId, idx);
                        if (!string.IsNullOrEmpty(entry))
                            FFIII_ScreenReaderMod.SpeakText(entry, false);
                        MusicPlayerStateTracker.SuppressContentChange = false;
                        yield break;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"[MusicPlayer] Error announcing entry song: {ex.Message}");
                    break;
                }
            }

            MusicPlayerStateTracker.SuppressContentChange = false;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2: Song navigation — ExtraSoundListContentController.SetFocus
        // ─────────────────────────────────────────────────────────────────────────

        public static void SetFocus_Postfix(ExtraSoundListContentController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;
                if (MusicPlayerStateTracker.SuppressContentChange)
                {
                    try
                    {
                        if (__instance != null)
                            MusicPlayerStateTracker.CachedFocusedPtr = __instance.Pointer;
                    }
                    catch { }
                    return;
                }

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (!MusicPlayerReader.ReadContentFromPointer(ptr, out string musicName, out int bgmId, out int index))
                    return;

                string entry = MusicPlayerReader.ReadSongEntry(musicName, bgmId, index);
                if (!string.IsNullOrEmpty(entry))
                {
                    AnnouncementDeduplicator.AnnounceIfNew(
                        AnnouncementContexts.MUSIC_LIST_ENTRY, entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in SetFocus patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 3: Play All toggle — ExtraSoundController.ChangeKeyHelpPlaybackIcon
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeKeyHelpPlaybackIcon_Postfix(int key)
        {
            try
            {
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;

                // LoopKeys: PlaybackOn=0, PlaybackOff=1
                string announcement = key == 0 ? T("Play All On") : T("Play All Off");
                FFIII_ScreenReaderMod.SpeakText(announcement, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in PlaybackIcon patch: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 4: Arrangement toggle — ExtraSoundListController.SwitchOriginalArrangeList
        // ─────────────────────────────────────────────────────────────────────────

        public static void SwitchOriginalArrangeList_Prefix()
        {
            if (MusicPlayerStateTracker.IsInMusicPlayer)
                MusicPlayerStateTracker.SuppressContentChange = true;
        }

        public static unsafe void SwitchOriginalArrangeList_Postfix(ExtraSoundListController __instance)
        {
            try
            {
                if (!MusicPlayerStateTracker.IsInMusicPlayer) return;

                IntPtr instancePtr = __instance.Pointer;
                if (instancePtr == IntPtr.Zero) return;

                int listType = *(int*)((byte*)instancePtr.ToPointer() + MusicPlayerStateTracker.OFFSET_CURRENT_LIST_TYPE);
                string toggleLabel = listType == 1 ? T("Original") : T("Arrangement");
                FFIII_ScreenReaderMod.SpeakText(toggleLabel, true);

                // Read and announce current song from cached focused pointer
                AnnouncementDeduplicator.Reset(AnnouncementContexts.MUSIC_LIST_ENTRY);
                IntPtr focusedPtr = MusicPlayerStateTracker.CachedFocusedPtr;
                if (focusedPtr != IntPtr.Zero &&
                    MusicPlayerReader.ReadContentFromPointer(focusedPtr, out string name, out int bgmId, out int idx))
                {
                    string entry = MusicPlayerReader.ReadSongEntry(name, bgmId, idx);
                    if (!string.IsNullOrEmpty(entry))
                        FFIII_ScreenReaderMod.SpeakText(entry, false);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[MusicPlayer] Error in SwitchOriginalArrangeList patch: {ex.Message}");
            }
            finally
            {
                MusicPlayerStateTracker.SuppressContentChange = false;
            }
        }
    }
}
