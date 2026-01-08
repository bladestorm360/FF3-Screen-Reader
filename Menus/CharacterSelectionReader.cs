using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using static FFIII_ScreenReader.Utils.TextUtils;
using MenuManager = Il2CppLast.UI.MenuManager;
using StatusWindowContentControllerBase = Il2CppSerial.Template.UI.StatusWindowContentControllerBase;
using OwnedCharacterData = Il2CppLast.Data.User.OwnedCharacterData;
using UserDataManager = Il2CppLast.Management.UserDataManager;
using Corps = Il2CppLast.Data.User.Corps;
using CorpsId = Il2CppLast.Defaine.User.CorpsId;
using MessageManager = Il2CppLast.Management.MessageManager;
using MasterManager = Il2CppLast.Data.Master.MasterManager;
using Job = Il2CppLast.Data.Master.Job;
using Condition = Il2CppLast.Data.Master.Condition;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character information from character selection screens.
    /// Used in menus like Status, Magic, Equipment, etc.
    /// Extracts and announces: character name, job, level, HP, MP.
    /// </summary>
    public static class CharacterSelectionReader
    {
        /// <summary>
        /// Try to read character information from the current cursor position.
        /// Returns a formatted string with character information, or null if not a character selection.
        /// Format: "Name, Job, Level X, HP current/max, MP current/max"
        /// </summary>
        public static string TryReadCharacterSelection(Transform cursorTransform, int cursorIndex)
        {
            try
            {
                // Safety check: Only read character data if we're in a menu or battle
                // This prevents character data from being read during game load
                var sceneName = SceneManager.GetActiveScene().name;
                bool isBattleScene = sceneName != null && sceneName.Contains("Battle");
                bool isMenuOpen = false;

                try
                {
                    var menuManager = MenuManager.Instance;
                    if (menuManager != null)
                    {
                        isMenuOpen = menuManager.IsOpen;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Could not check MenuManager.IsOpen: {ex.Message}");
                }

                if (!isBattleScene && !isMenuOpen)
                {
                    return null;
                }

                // Walk up the hierarchy to find character selection structures
                Transform current = cursorTransform;
                int depth = 0;

                while (current != null && depth < 15)
                {
                    string lowerName = current.name.ToLower();

                    // Look for character selection menu structures
                    if (lowerName.Contains("character") || lowerName.Contains("chara") ||
                        lowerName.Contains("status") || lowerName.Contains("formation") ||
                        lowerName.Contains("party") || lowerName.Contains("member"))
                    {
                        // Try to find Content list
                        Transform contentList = FindContentList(current);

                        if (contentList != null && cursorIndex >= 0 && cursorIndex < contentList.childCount)
                        {
                            Transform characterSlot = contentList.GetChild(cursorIndex);

                            // Try to read the character information
                            string characterInfo = ReadCharacterInformation(characterSlot);
                            if (characterInfo != null)
                            {
                                return characterInfo;
                            }
                        }
                    }

                    // Also check if we're directly on a character info element
                    if (lowerName.Contains("info_content") || lowerName.Contains("status_info") ||
                        lowerName.Contains("chara_status"))
                    {
                        string characterInfo = ReadCharacterInformation(current);
                        if (characterInfo != null)
                        {
                            return characterInfo;
                        }
                    }

                    current = current.parent;
                    depth++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"CharacterSelectionReader error: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Check if a string contains only numeric characters.
        /// Used to filter out job IDs (numbers) vs job names (text).
        /// </summary>
        private static bool IsNumericOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            foreach (char c in value)
            {
                if (!char.IsDigit(c))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Check if a string contains numeric content (may have leading/trailing whitespace).
        /// Used to validate HP/MP values.
        /// </summary>
        private static bool IsNumericContent(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return false;

            foreach (char c in trimmed)
            {
                if (!char.IsDigit(c))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Find the Content transform within a ScrollView structure.
        /// </summary>
        private static Transform FindContentList(Transform root)
        {
            try
            {
                var content = FindTransformInChildren(root, "Content");
                if (content != null && content.parent != null &&
                    (content.parent.name == "Viewport" || content.parent.parent?.name == "Scroll View"))
                {
                    return content;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error finding content list: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Read character information from a character slot transform.
        /// Returns formatted announcement string or null if unable to read.
        /// Tries controller data first (for row info), falls back to UI text.
        /// </summary>
        private static string ReadCharacterInformation(Transform slotTransform)
        {
            try
            {
                // Try to read from StatusWindowContentControllerBase (like SaveSlotReader pattern)
                // This gives us access to OwnedCharacterData including row information
                var contentController = slotTransform.GetComponent<StatusWindowContentControllerBase>();
                if (contentController == null)
                {
                    contentController = slotTransform.GetComponentInChildren<StatusWindowContentControllerBase>();
                }

                if (contentController != null)
                {
                    try
                    {
                        var characterData = contentController.CharacterData;
                        if (characterData != null)
                        {
                            string result = ReadFromCharacterData(characterData);
                            if (!string.IsNullOrEmpty(result))
                            {
                                return result;
                            }
                        }
                    }
                    catch (Exception controllerEx)
                    {
                        MelonLogger.Warning($"CharacterSelectionReader: Error accessing CharacterData: {controllerEx.Message}");
                    }
                }

                // Fallback: Try to read from text components (original behavior)
                return ReadFromTextComponents(slotTransform);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character information: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Read character information directly from OwnedCharacterData.
        /// This gives us access to row information via Corps lookup.
        /// Format: "Name, Job, Front Row, Level X, HP current/max"
        /// </summary>
        private static string ReadFromCharacterData(OwnedCharacterData characterData)
        {
            try
            {
                var parts = new List<string>();

                // Character name
                string name = characterData.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    parts.Add(name);
                }

                // Job name from current job
                string jobName = GetJobName(characterData);
                if (!string.IsNullOrEmpty(jobName))
                {
                    parts.Add(jobName);
                }

                // Row (Front/Back) - this is why we need controller access!
                string row = GetCharacterRow(characterData);
                if (!string.IsNullOrEmpty(row))
                {
                    parts.Add(row);
                }

                // Level, HP from Parameter
                var parameter = characterData.Parameter;
                if (parameter != null)
                {
                    int level = parameter.BaseLevel;
                    if (level > 0)
                    {
                        parts.Add($"Level {level}");
                    }

                    int currentHp = parameter.currentHP;
                    int maxHp = parameter.ConfirmedMaxHp();
                    if (maxHp > 0)
                    {
                        parts.Add($"HP {currentHp}/{maxHp}");
                    }
                }

                if (parts.Count > 0)
                {
                    return string.Join(", ", parts);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"CharacterSelectionReader: Error in ReadFromCharacterData: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets the character's current job name.
        /// Uses JobId to look up Job master data.
        /// </summary>
        private static string GetJobName(OwnedCharacterData characterData)
        {
            try
            {
                int jobId = characterData.JobId;
                if (jobId <= 0)
                    return null;

                var masterManager = MasterManager.Instance;
                if (masterManager == null)
                    return null;

                var job = masterManager.GetData<Job>(jobId);
                if (job != null)
                {
                    string mesId = job.MesIdName;
                    if (!string.IsNullOrEmpty(mesId))
                    {
                        var msgManager = MessageManager.Instance;
                        if (msgManager != null)
                        {
                            string localizedName = msgManager.GetMessage(mesId, false);
                            if (!string.IsNullOrWhiteSpace(localizedName))
                            {
                                return localizedName;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Gets the character's row (Front Row / Back Row) from Corps data.
        /// </summary>
        private static string GetCharacterRow(OwnedCharacterData characterData)
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                    return null;

                var corpsList = userDataManager.GetCorpsListClone();
                if (corpsList == null)
                    return null;

                int characterId = characterData.Id;
                foreach (var corps in corpsList)
                {
                    if (corps != null && corps.CharacterId == characterId)
                    {
                        return corps.Id == CorpsId.Front ? "Front Row" : "Back Row";
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Read character information from text components.
        /// Looks for specific text field names to extract character data.
        /// </summary>
        private static string ReadFromTextComponents(Transform slotTransform)
        {
            try
            {
                string characterName = null;
                string jobName = null;
                string level = null;
                string currentHP = null;
                string maxHP = null;
                string currentMP = null;
                string maxMP = null;

                ForEachTextInChildren(slotTransform, text =>
                {
                    if (text == null || text.text == null) return;

                    string content = text.text.Trim();
                    if (string.IsNullOrEmpty(content)) return;

                    string textName = text.name.ToLower();

                    // Check for character name
                    // Name text fields - exclude job names, area names, etc.
                    if ((textName.Contains("name") && !textName.Contains("job") &&
                        !textName.Contains("area") && !textName.Contains("floor") &&
                        !textName.Contains("slot")) ||
                        textName == "nametext" || textName == "name_text")
                    {
                        // Skip labels and very short text
                        if (content.Length > 1 && !content.Contains(":") &&
                            content != "HP" && content != "MP" && content != "Lv")
                        {
                            characterName = content;
                        }
                    }
                    // Check for job/class name
                    // Be specific: look for jobNameText, job_name, classNameText, etc.
                    // Exclude fields that contain "id", "level", or "lv" to avoid reading job ID or job level
                    else if ((textName.Contains("job") || textName.Contains("class")) &&
                             !textName.Contains("label") && !textName.Contains("id") &&
                             !textName.Contains("level") && !textName.Contains("lv"))
                    {
                        // Filter out numeric-only content (likely a job ID, not a job name)
                        if (!IsNumericOnly(content))
                        {
                            jobName = content;
                        }
                    }
                    // Check for level
                    else if ((textName.Contains("level") || textName.Contains("lv")) &&
                             !textName.Contains("label") && !textName.Contains("fixed"))
                    {
                        // Skip "Lv" label, get the number
                        if (content != "Lv" && content != "Level" && content != "LV")
                        {
                            level = content;
                        }
                    }
                    // Check for HP values
                    else if (textName.Contains("hp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentHP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxHP = content;
                        }
                    }
                    // Check for MP values
                    else if (textName.Contains("mp") && !textName.Contains("label"))
                    {
                        if (textName.Contains("current") || textName.Contains("now"))
                        {
                            currentMP = content;
                        }
                        else if (textName.Contains("max"))
                        {
                            maxMP = content;
                        }
                    }
                });

                // Build announcement string
                // Format: "Name, Job, Level X, HP current/max, MP current/max"
                string announcement = "";

                // Start with character name
                if (!string.IsNullOrEmpty(characterName))
                {
                    announcement = characterName;
                }

                // Add job name
                if (!string.IsNullOrEmpty(jobName))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ", " + jobName;
                    }
                    else
                    {
                        announcement = jobName;
                    }
                }

                // Add level
                if (!string.IsNullOrEmpty(level))
                {
                    if (!string.IsNullOrEmpty(announcement))
                    {
                        announcement += ", Level " + level;
                    }
                    else
                    {
                        announcement = "Level " + level;
                    }
                }

                // Add HP
                if (!string.IsNullOrEmpty(currentHP))
                {
                    if (!string.IsNullOrEmpty(maxHP))
                    {
                        announcement += $", HP {currentHP}/{maxHP}";
                    }
                    else
                    {
                        announcement += $", HP {currentHP}";
                    }
                }

                // Note: FF3 uses spell charges per level, not MP. MP display removed.

                if (!string.IsNullOrEmpty(announcement))
                {
                    return announcement;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading character text components: {ex.Message}");
            }

            return null;
        }
    }
}
