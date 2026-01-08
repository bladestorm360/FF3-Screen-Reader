using System;
using MelonLoader;
using UnityEngine;
using ShopCommandMenuController = Il2CppLast.UI.KeyInput.ShopCommandMenuController;
using ShopCommandMenuContentController = Il2CppLast.UI.KeyInput.ShopCommandMenuContentController;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading shop command menu (Buy/Sell/Equipment/Back).
    /// Called by MenuTextDiscovery when navigating the shop command bar.
    /// Uses hierarchy traversal from cursor position for fast lookups.
    /// </summary>
    public static class ShopCommandReader
    {
        /// <summary>
        /// Try to read shop command menu text.
        /// Returns command name if cursor is on a shop command, null otherwise.
        /// </summary>
        public static string TryReadShopCommand(Transform cursorTransform, int cursorIndex)
        {
            if (cursorTransform == null)
                return null;

            try
            {
                // Walk up the hierarchy to find shop command structures
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string lowerName = current.name.ToLower();

                    // Look for shop command menu structures
                    if (lowerName.Contains("shop") && lowerName.Contains("command"))
                    {
                        // Try to find Content list (common pattern: Scroll View -> Viewport -> Content)
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.childCount)
                        {
                            Transform commandSlot = contentList.GetChild(cursorIndex);

                            // Try to read from the slot
                            string commandText = ReadCommandFromTransform(commandSlot);
                            if (commandText != null)
                            {
                                return commandText;
                            }
                        }

                        // Fallback: try to find controller on this object
                        var menuController = current.GetComponent<ShopCommandMenuController>();
                        if (menuController == null)
                        {
                            menuController = current.GetComponentInChildren<ShopCommandMenuController>();
                        }

                        if (menuController != null)
                        {
                            return ReadFromController(menuController, cursorIndex);
                        }
                    }

                    // Also check if we're directly on a command content element
                    if (lowerName.Contains("command") && lowerName.Contains("content"))
                    {
                        string commandText = ReadCommandFromTransform(current);
                        if (commandText != null)
                        {
                            return commandText;
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"ShopCommandReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Find the Content transform within a ScrollView structure.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            try
            {
                var allTransforms = root.GetComponentsInChildren<Transform>();
                foreach (var t in allTransforms)
                {
                    if (t.name == "Content" && t.parent != null &&
                        (t.parent.name == "Viewport" || t.parent.parent?.name == "Scroll View"))
                    {
                        return t;
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Read command from a transform by finding ShopCommandMenuContentController.
        /// </summary>
        private static string ReadCommandFromTransform(Transform slotTransform)
        {
            if (slotTransform == null)
                return null;

            try
            {
                var contentController = slotTransform.GetComponent<ShopCommandMenuContentController>();
                if (contentController == null)
                {
                    contentController = slotTransform.GetComponentInChildren<ShopCommandMenuContentController>();
                }

                if (contentController != null)
                {
                    return GetCommandName(contentController);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading command transform: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read command from ShopCommandMenuController using cursor index.
        /// </summary>
        private static string ReadFromController(ShopCommandMenuController menuController, int cursorIndex)
        {
            try
            {
                var contentList = menuController.contentList;
                if (contentList == null || contentList.Count == 0)
                    return null;

                // Use cursor index to get the command
                if (cursorIndex >= 0 && cursorIndex < contentList.Count)
                {
                    var contentController = contentList[cursorIndex];
                    if (contentController != null)
                    {
                        return GetCommandName(contentController);
                    }
                }

                // Fallback: find the focused command by checking FocusCursorParent
                foreach (var content in contentList)
                {
                    if (content == null) continue;

                    try
                    {
                        var focusParent = content.FocusCursorParent;
                        if (focusParent != null && focusParent.activeInHierarchy)
                        {
                            return GetCommandName(content);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading from controller: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Get command name from a ShopCommandMenuContentController.
        /// </summary>
        private static string GetCommandName(ShopCommandMenuContentController content)
        {
            try
            {
                var commandId = content.CommandId;
                return commandId switch
                {
                    Il2CppLast.Defaine.ShopCommandId.Buy => "Buy",
                    Il2CppLast.Defaine.ShopCommandId.Sell => "Sell",
                    Il2CppLast.Defaine.ShopCommandId.Equipment => "Equipment",
                    Il2CppLast.Defaine.ShopCommandId.Back => "Back",
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }
    }
}
