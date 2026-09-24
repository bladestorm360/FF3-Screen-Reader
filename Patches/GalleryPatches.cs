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
    /// Tracks gallery scene state.
    /// Mirrors MusicPlayerStateTracker pattern.
    /// </summary>
    public static class GalleryStateTracker
    {
        public static bool IsInGallery { get; set; } = false;
        public static bool SuppressContentChange { get; set; } = false;
        public static IntPtr CachedFocusedPtr { get; set; } = IntPtr.Zero;
        public static int PreviousState { get; set; } = 0;

        public static void ClearState()
        {
            IsInGallery = false;
            SuppressContentChange = false;
            CachedFocusedPtr = IntPtr.Zero;
            PreviousState = 0;
            MenuStateRegistry.Reset(MenuStateRegistry.GALLERY);
            AnnouncementDeduplicator.Reset(AnnouncementContexts.GALLERY_LIST_ENTRY);
        }
    }

    /// <summary>
    /// Patches for the Gallery (Extra Gallery) extras menu.
    /// Uses manual Harmony patching (required for FF3 IL2CPP).
    /// </summary>
    internal static class GalleryPatches
    {
        public static void ApplyPatches(HarmonyLib.Harmony harmony)
        {
            try
            {
                // Patch 1: SubSceneManagerExtraGallery.ChangeState
                var changeStateMethod = AccessTools.Method(
                    typeof(SubSceneManagerExtraGallery), "ChangeState");
                if (changeStateMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GalleryPatches), nameof(ChangeState_Postfix));
                    harmony.Patch(changeStateMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Gallery] SubSceneManagerExtraGallery.ChangeState not found");
                }

                // Patch 2: GalleryTopListController.SetFocusContent
                var setFocusMethod = AccessTools.Method(
                    typeof(GalleryTopListController), "SetFocusContent");
                if (setFocusMethod != null)
                {
                    var postfix = AccessTools.Method(typeof(GalleryPatches), nameof(SetFocusContent_Postfix));
                    harmony.Patch(setFocusMethod, postfix: new HarmonyMethod(postfix));
                }
                else
                {
                    MelonLogger.Warning("[Gallery] GalleryTopListController.SetFocusContent not found");
                }

                MelonLogger.Msg("[Gallery] Patches applied");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Gallery] Error applying patches: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 1: State transitions — SubSceneManagerExtraGallery.ChangeState
        // ─────────────────────────────────────────────────────────────────────────

        public static void ChangeState_Postfix(int state)
        {
            try
            {
                switch (state)
                {
                    case 1: // View
                        if (GalleryStateTracker.PreviousState == 0) // First entry from Init
                        {
                            GalleryStateTracker.IsInGallery = true;
                            GalleryStateTracker.SuppressContentChange = true;
                            GalleryStateTracker.CachedFocusedPtr = IntPtr.Zero;
                            entryHeaderSpoken = false;
                            MenuStateRegistry.SetActiveExclusive(MenuStateRegistry.GALLERY);
                            CoroutineManager.StartManaged(AnnounceGalleryEntry());
                        }
                        else if (GalleryStateTracker.PreviousState == 2) // Returning from Details
                        {
                            AnnouncementDeduplicator.Reset(AnnouncementContexts.GALLERY_LIST_ENTRY);
                        }
                        GalleryStateTracker.PreviousState = 1;
                        break;

                    case 2: // Details — image opened
                        FFIII_ScreenReaderMod.SpeakText(T("Image open"), true);
                        GalleryStateTracker.PreviousState = 2;
                        break;

                    case 3: // GotoTitle — leaving gallery
                        GalleryStateTracker.ClearState();
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in ChangeState patch: {ex.Message}");
            }
        }

        // "Gallery" has been spoken and the entry item is still to be read (SuppressContentChange on).
        private static bool entryHeaderSpoken;

        /// <summary>
        /// Gallery entry: "Gallery" one frame after the View state, then the focused item as soon as
        /// SetFocusContent has cached it (already, or when it next fires). Replaces a per-frame poll of
        /// the cached pointer for up to 2 s: the SetFocusContent postfix is the event that provides it.
        /// </summary>
        private static IEnumerator AnnounceGalleryEntry()
        {
            yield return null;
            if (!GalleryStateTracker.IsInGallery) yield break;
            FFIII_ScreenReaderMod.SpeakText(T("Gallery"), true);
            entryHeaderSpoken = true;
            TryAnnounceEntryItem();
        }

        /// <summary>
        /// Speaks the item cached during the entry and ends the entry suppression; does nothing until
        /// "Gallery" has been spoken and an item is cached.
        /// </summary>
        private static void TryAnnounceEntryItem()
        {
            if (!entryHeaderSpoken || !GalleryStateTracker.SuppressContentChange) return;
            try
            {
                IntPtr focusedPtr = GalleryStateTracker.CachedFocusedPtr;
                if (focusedPtr == IntPtr.Zero) return;

                entryHeaderSpoken = false;
                GalleryStateTracker.SuppressContentChange = false;
                if (GalleryReader.ReadContentFromPointer(focusedPtr, out int number, out string name))
                {
                    string entry = GalleryReader.ReadListEntry(number, name);
                    if (!string.IsNullOrEmpty(entry))
                        FFIII_ScreenReaderMod.SpeakText(entry, false);
                }
            }
            catch (Exception ex)
            {
                entryHeaderSpoken = false;
                GalleryStateTracker.SuppressContentChange = false;
                MelonLogger.Warning($"[Gallery] Error announcing entry item: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Patch 2: List navigation — GalleryTopListController.SetFocusContent
        // ─────────────────────────────────────────────────────────────────────────

        public static void SetFocusContent_Postfix(GalleryTopListController __instance, bool isFocus)
        {
            try
            {
                if (!isFocus) return;
                if (!GalleryStateTracker.IsInGallery) return;

                IntPtr ptr;
                try
                {
                    if (__instance == null) return;
                    ptr = __instance.Pointer;
                }
                catch { return; }
                if (ptr == IntPtr.Zero) return;

                if (GalleryStateTracker.SuppressContentChange)
                {
                    // Entry: the item is read after "Gallery"
                    GalleryStateTracker.CachedFocusedPtr = ptr;
                    TryAnnounceEntryItem();
                    return;
                }

                if (!GalleryReader.ReadContentFromPointer(ptr, out int number, out string name))
                    return;

                string entry = GalleryReader.ReadListEntry(number, name);
                if (!string.IsNullOrEmpty(entry))
                {
                    AnnouncementDeduplicator.AnnounceIfNew(
                        AnnouncementContexts.GALLERY_LIST_ENTRY, entry);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[Gallery] Error in SetFocusContent patch: {ex.Message}");
            }
        }
    }
}
