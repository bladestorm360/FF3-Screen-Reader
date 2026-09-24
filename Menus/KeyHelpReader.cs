using System;
using System.Collections.Generic;
using Il2CppLast.UI.KeyInput;
using UnityEngine;
using UnityEngine.UI;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Reads the controls display. Two independent features:
    ///   • Shift+I — reads the on-screen control-hint bar (the persistent KeyHelpController) at once.
    ///     Uses GameObjectCache + transform navigation + GetComponentsInChildren&lt;Text&gt;()
    ///     to avoid IL2CPP Cast constraint errors from array-based access on game-specific types.
    ///   • Arrows/WASD/D-pad — step the config "Gamepad/Keyboard Controls" pop-up one entry at a time
    ///     via a NavigationBuffer, gated to KeyContext.KeyHelp. The buffer is armed ONLY from that pop-up
    ///     (ConfigKeysSettingController's Help state, fed by ConfigMenuPatches), never from the hint bar,
    ///     so no other menu's arrows are hijacked.
    /// </summary>
    public static class KeyHelpReader
    {
        // KeyHelpController.view (KeyHelpView) — private field, no public accessor
        private const int OFFSET_VIEW = 0x18;

        private static NavigationBuffer buffer = null;

        // The controls pop-up owner validates the screen is still shown (cheap, no scene scan), so a
        // missed close can't leave KeyContext.KeyHelp stuck.
        private static bool helpActive = false;
        private static ConfigKeysSettingController helpOwner = null;

        /// <summary>
        /// Called by ConfigMenuPatches when the Gamepad/Keyboard Controls pop-up opens, with the
        /// rendered entry strings (action + binding). Builds the buffer and reads the first entry.
        /// </summary>
        public static void OpenControlsHelp(ConfigKeysSettingController owner, List<string> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                CloseControlsHelp();
                return;
            }

            var funcs = new List<Func<string>>(entries.Count);
            foreach (var entry in entries) { var s = entry; funcs.Add(() => s); }
            buffer = new NavigationBuffer(funcs);
            helpOwner = owner;
            helpActive = true;
            SpeakEntry(buffer.Current());
        }

        /// <summary>Called when the pop-up returns to the controls list or the screen closes.</summary>
        public static void CloseControlsHelp()
        {
            helpActive = false;
            helpOwner = null;
            buffer = null;
        }

        /// <summary>True while the controls pop-up is on screen — drives KeyContext.KeyHelp.</summary>
        public static bool IsScreenActive
        {
            get
            {
                try
                {
                    if (!helpActive) return false;
                    if (helpOwner == null || helpOwner.gameObject == null || !helpOwner.gameObject.activeInHierarchy)
                    {
                        CloseControlsHelp();
                        return false;
                    }
                    return true;
                }
                catch { CloseControlsHelp(); return false; }
            }
        }

        public static void NavigateNext() => SpeakEntry(buffer?.Next());
        public static void NavigatePrevious() => SpeakEntry(buffer?.Previous());
        public static void JumpToTop() => SpeakEntry(buffer?.JumpTop());
        public static void JumpToBottom() => SpeakEntry(buffer?.JumpBottom());

        private static void SpeakEntry(string s)
        {
            if (string.IsNullOrEmpty(s) || buffer == null) return;
            var (index, count) = buffer.CurrentGroupPosition();
            FFIII_ScreenReaderMod.SpeakText(MenuPosition.Format(s, index, count), interrupt: true);
        }

        /// <summary>
        /// Public entry point — reads all visible key help controls and speaks them.
        /// </summary>
        public static void AnnounceKeyHelp()
        {
            try
            {
                string result = ReadVisibleKeyHelp();
                FFIII_ScreenReaderMod.SpeakText(result, interrupt: true);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"Error in AnnounceKeyHelp: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText(T("Error reading controls"), interrupt: true);
            }
        }

        /// <summary>
        /// Gets the active KeyHelpController via GameObjectCache (single instance, no Cast-based
        /// array indexer), navigates to its ContentsParent via the private view field, then reads
        /// all visible control entries using GetComponentsInChildren&lt;Text&gt;().
        /// </summary>
        private static unsafe string ReadVisibleKeyHelp()
        {
            // Get KeyHelpController via GameObjectCache (same pattern as AnnounceConfigTooltip)
            var controller = GameObjectCache.Get<KeyHelpController>();
            if (controller == null)
                controller = GameObjectCache.Refresh<KeyHelpController>();

            if (controller == null || controller.gameObject == null || !controller.gameObject.activeInHierarchy)
                return T("No controls displayed");

            // Read private 'view' field (KeyHelpView) at offset 0x18 via unsafe pointer
            IntPtr controllerPtr = controller.Pointer;
            IntPtr viewPtr = *(IntPtr*)((byte*)controllerPtr.ToPointer() + OFFSET_VIEW);
            if (viewPtr == IntPtr.Zero)
                return T("No controls displayed");

            var view = new KeyHelpView(viewPtr);

            // Get ContentsParent via public property
            var contentsParent = view.ContentsParent;
            if (contentsParent == null)
                return T("No controls displayed");

            var contentsTransform = contentsParent.transform;
            if (contentsTransform == null || contentsTransform.childCount == 0)
                return T("No controls displayed");

            var entries = new List<string>();

            // Iterate children of ContentsParent — each is a control entry (KeyIconController).
            // The game deactivates entries on other pages, so activeInHierarchy filters to
            // the visible page only.
            for (int i = 0; i < contentsTransform.childCount; i++)
            {
                var child = contentsTransform.GetChild(i);
                if (child == null || child.gameObject == null || !child.gameObject.activeInHierarchy)
                    continue;

                // Get all Text components within this entry (same pattern as KeyboardGamepadReader)
                var texts = child.GetComponentsInChildren<Text>(false);
                if (texts == null)
                    continue;

                var parts = new List<string>();
                foreach (var txt in texts)
                {
                    if (txt != null && !string.IsNullOrWhiteSpace(txt.text))
                        parts.Add(txt.text.Trim());
                }

                if (parts.Count > 0)
                    entries.Add(string.Join(": ", parts));
            }

            if (entries.Count == 0)
                return T("No controls displayed");

            return string.Join(", ", entries);
        }
    }
}
