using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;
using Il2CppLast.Data.User;
using Il2CppLast.UI.KeyInput;
using Il2CppLast.Battle;
using Il2CppSerial.FF3.UI.KeyInput;
using Il2CppSerial.Template.UI.KeyInput;
using FFIII_ScreenReader.Core;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;

namespace FFIII_ScreenReader.Menus
{
    /// <summary>
    /// Handles reading character status details.
    /// Provides stat reading functions for physical and magical stats.
    /// Ported from FF5 screen reader.
    /// </summary>
    internal static class StatusDetailsReader
    {
        private static OwnedCharacterData currentCharacterData = null;

        public static void SetCurrentCharacterData(OwnedCharacterData data)
        {
            currentCharacterData = data;
        }

        public static void ClearCurrentCharacterData()
        {
            currentCharacterData = null;
        }

        public static OwnedCharacterData GetCurrentCharacterData()
        {
            return currentCharacterData;
        }

        /// <summary>
        /// Read all character status information from the status details view.
        /// Returns a formatted string with all relevant information.
        /// </summary>
        public static string ReadStatusDetails(StatusDetailsController controller)
        {
            if (controller == null)
            {
                return null;
            }

            var parts = new List<string>();

            // Try to read from current character data
            if (currentCharacterData != null)
            {
                try
                {
                    // Character name
                    string name = currentCharacterData.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        parts.Add(name);
                    }

                    // Level
                    var param = currentCharacterData.Parameter;
                    if (param != null)
                    {
                        parts.Add(string.Format(T("Level {0}"), param.ConfirmedLevel()));

                        // HP
                        int currentHp = param.currentHP;
                        int maxHp = param.ConfirmedMaxHp();
                        parts.Add(string.Format(T("HP: {0} / {1}"), currentHp, maxHp));
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Error reading status details: {ex.Message}");
                }
            }

            return parts.Count > 0 ? string.Join(". ", parts) : null;
        }

        /// <summary>
        /// Safely get text from a Text component, returning null if invalid.
        /// </summary>
        private static string GetTextSafe(Text textComponent)
        {
            if (textComponent == null)
            {
                return null;
            }

            try
            {
                string text = textComponent.text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                return text.Trim();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read physical combat stats (Strength, Vitality, Defense, Evade).
        /// </summary>
        public static string ReadPhysicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return T("No character data available");
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                int strength = param.ConfirmedPower();
                parts.Add(string.Format(T("Strength: {0}"), strength));

                int vitality = param.ConfirmedVitality();
                parts.Add(string.Format(T("Vitality: {0}"), vitality));

                int defense = param.ConfirmedDefense();
                parts.Add(string.Format(T("Defense: {0}"), defense));

                int evade = param.ConfirmedDefenseCount();
                parts.Add(string.Format(T("Evade: {0}"), evade));

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading physical stats: {ex.Message}");
                return $"Error reading physical stats: {ex.Message}";
            }
        }

        /// <summary>
        /// Read magical combat stats (Magic, Magic Defense, Magic Evade).
        /// </summary>
        public static string ReadMagicalStats()
        {
            if (currentCharacterData == null || currentCharacterData.Parameter == null)
            {
                return T("No character data available");
            }

            try
            {
                var param = currentCharacterData.Parameter;
                var parts = new List<string>();

                int magic = param.ConfirmedMagic();
                parts.Add(string.Format(T("Magic: {0}"), magic));

                int magicDefense = param.ConfirmedAbilityDefense();
                parts.Add(string.Format(T("Magic Defense: {0}"), magicDefense));

                int magicEvade = param.ConfirmedMagicDefenseCount();
                parts.Add(string.Format(T("Magic Evade: {0}"), magicEvade));

                return string.Join(". ", parts);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading magical stats: {ex.Message}");
                return $"Error reading magical stats: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// Stat groups for organizing status screen statistics
    /// </summary>
    public enum StatGroup
    {
        CharacterInfo,  // Name, Job, Level, Experience, Next Level
        Vitals,         // HP, Job Level
        Attributes,     // Strength, Agility, Stamina, Intellect, Spirit
        CombatStats,    // Attack, Accuracy, Defense, Evasion, Magic Defense, Magic Evasion
    }

    /// <summary>
    /// Definition of a single stat that can be navigated
    /// </summary>
    internal class StatusStatDefinition
    {
        public string Name { get; set; }
        public StatGroup Group { get; set; }
        public Func<OwnedCharacterData, string> Reader { get; set; }

        public StatusStatDefinition(string name, StatGroup group, Func<OwnedCharacterData, string> reader)
        {
            Name = name;
            Group = group;
            Reader = reader;
        }
    }

    /// <summary>
    /// Tracks navigation state within the status screen for arrow key navigation
    /// </summary>
    internal class StatusNavigationTracker
    {
        private static StatusNavigationTracker instance = null;
        public static StatusNavigationTracker Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = new StatusNavigationTracker();
                }
                return instance;
            }
        }

        public bool IsNavigationActive { get; set; }
        public int CurrentStatIndex { get; set; }
        public OwnedCharacterData CurrentCharacterData { get; set; }
        public StatusDetailsController ActiveController { get; set; }

        private StatusNavigationTracker()
        {
            Reset();
        }

        public void Reset()
        {
            IsNavigationActive = false;
            CurrentStatIndex = 0;
            CurrentCharacterData = null;
            ActiveController = null;
        }

        public bool ValidateState()
        {
            return IsNavigationActive &&
                   CurrentCharacterData != null &&
                   ActiveController != null &&
                   ActiveController.gameObject != null &&
                   ActiveController.gameObject.activeInHierarchy;
        }
    }

    /// <summary>
    /// Handles navigation through status screen stats using arrow keys
    /// </summary>
    internal static class StatusNavigationReader
    {
        private static List<StatusStatDefinition> statList = null;
        // Group start indices: CharacterInfo=0, Vitals=6, Attributes=16, CombatStats=21
        private static readonly int[] GroupStartIndices = new int[] { 0, 6, 16, 21 };

        /// <summary>
        /// Initialize the stat list with all visible stats in UI order.
        /// FF3-specific stats matching the actual status screen display.
        /// </summary>
        public static void InitializeStatList()
        {
            if (statList != null) return;

            statList = new List<StatusStatDefinition>();

            // Character Info Group (indices 0-5): includes Job Level with progression stats
            statList.Add(new StatusStatDefinition("Name", StatGroup.CharacterInfo, ReadName));
            statList.Add(new StatusStatDefinition("Job", StatGroup.CharacterInfo, ReadJobName));
            statList.Add(new StatusStatDefinition("Level", StatGroup.CharacterInfo, ReadCharacterLevel));
            statList.Add(new StatusStatDefinition("Experience", StatGroup.CharacterInfo, ReadExperience));
            statList.Add(new StatusStatDefinition("Next Level", StatGroup.CharacterInfo, ReadNextLevel));
            statList.Add(new StatusStatDefinition("Job Level", StatGroup.CharacterInfo, ReadJobLevel));

            // Vitals Group (indices 6-15): HP and 8 MP levels
            statList.Add(new StatusStatDefinition("HP", StatGroup.Vitals, ReadHP));
            statList.Add(new StatusStatDefinition("LV1", StatGroup.Vitals, ReadMPLevel1));
            statList.Add(new StatusStatDefinition("LV2", StatGroup.Vitals, ReadMPLevel2));
            statList.Add(new StatusStatDefinition("LV3", StatGroup.Vitals, ReadMPLevel3));
            statList.Add(new StatusStatDefinition("LV4", StatGroup.Vitals, ReadMPLevel4));
            statList.Add(new StatusStatDefinition("LV5", StatGroup.Vitals, ReadMPLevel5));
            statList.Add(new StatusStatDefinition("LV6", StatGroup.Vitals, ReadMPLevel6));
            statList.Add(new StatusStatDefinition("LV7", StatGroup.Vitals, ReadMPLevel7));
            statList.Add(new StatusStatDefinition("LV8", StatGroup.Vitals, ReadMPLevel8));

            // Attributes Group (indices 16-20)
            statList.Add(new StatusStatDefinition("Strength", StatGroup.Attributes, ReadStrength));
            statList.Add(new StatusStatDefinition("Agility", StatGroup.Attributes, ReadAgility));
            statList.Add(new StatusStatDefinition("Stamina", StatGroup.Attributes, ReadStamina));
            statList.Add(new StatusStatDefinition("Intellect", StatGroup.Attributes, ReadIntellect));
            statList.Add(new StatusStatDefinition("Spirit", StatGroup.Attributes, ReadSpirit));

            // Combat Stats Group (indices 21-26)
            statList.Add(new StatusStatDefinition("Attack", StatGroup.CombatStats, ReadAttack));
            statList.Add(new StatusStatDefinition("Accuracy", StatGroup.CombatStats, ReadAccuracy));
            statList.Add(new StatusStatDefinition("Defense", StatGroup.CombatStats, ReadDefense));
            statList.Add(new StatusStatDefinition("Evasion", StatGroup.CombatStats, ReadEvasion));
            statList.Add(new StatusStatDefinition("Magic Defense", StatGroup.CombatStats, ReadMagicDefense));
            statList.Add(new StatusStatDefinition("Magic Evasion", StatGroup.CombatStats, ReadMagicEvasion));
        }

        /// <summary>
        /// Navigate to the next stat (wraps to top at end)
        /// </summary>
        public static void NavigateNext()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = (tracker.CurrentStatIndex + 1) % statList.Count;
            ReadCurrentStat();
        }

        /// <summary>
        /// Navigate to the previous stat (wraps to bottom at top)
        /// </summary>
        public static void NavigatePrevious()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex--;
            if (tracker.CurrentStatIndex < 0)
            {
                tracker.CurrentStatIndex = statList.Count - 1;
            }
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the next group
        /// </summary>
        public static void JumpToNextGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int nextGroupIndex = -1;

            // Find next group start index
            for (int i = 0; i < GroupStartIndices.Length; i++)
            {
                if (GroupStartIndices[i] > currentIndex)
                {
                    nextGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to first group if at end
            if (nextGroupIndex == -1)
            {
                nextGroupIndex = GroupStartIndices[0];
            }

            tracker.CurrentStatIndex = nextGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the first stat of the previous group
        /// </summary>
        public static void JumpToPreviousGroup()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            int currentIndex = tracker.CurrentStatIndex;
            int prevGroupIndex = -1;

            // Find previous group start index
            for (int i = GroupStartIndices.Length - 1; i >= 0; i--)
            {
                if (GroupStartIndices[i] < currentIndex)
                {
                    prevGroupIndex = GroupStartIndices[i];
                    break;
                }
            }

            // Wrap to last group if at beginning
            if (prevGroupIndex == -1)
            {
                prevGroupIndex = GroupStartIndices[GroupStartIndices.Length - 1];
            }

            tracker.CurrentStatIndex = prevGroupIndex;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the top (first stat)
        /// </summary>
        public static void JumpToTop()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = 0;
            ReadCurrentStat();
        }

        /// <summary>
        /// Jump to the bottom (last stat)
        /// </summary>
        public static void JumpToBottom()
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.IsNavigationActive) return;

            tracker.CurrentStatIndex = statList.Count - 1;
            ReadCurrentStat();
        }

        /// <summary>
        /// Read the stat at the current index
        /// </summary>
        public static void ReadCurrentStat()
        {
            var tracker = StatusNavigationTracker.Instance;
            if (!tracker.ValidateState())
            {
                FFIII_ScreenReaderMod.SpeakText(T("Navigation not available"));
                return;
            }

            ReadStatAtIndex(tracker.CurrentStatIndex);
        }

        /// <summary>
        /// Read the stat at the specified index
        /// </summary>
        private static void ReadStatAtIndex(int index)
        {
            if (statList == null) InitializeStatList();

            var tracker = StatusNavigationTracker.Instance;

            if (index < 0 || index >= statList.Count)
            {
                MelonLogger.Warning($"Invalid stat index: {index}");
                return;
            }

            if (tracker.CurrentCharacterData == null)
            {
                FFIII_ScreenReaderMod.SpeakText(T("No character data"));
                return;
            }

            try
            {
                var stat = statList[index];
                string value = stat.Reader(tracker.CurrentCharacterData);
                FFIII_ScreenReaderMod.SpeakText(value, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error reading stat at index {index}: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText(T("Error reading stat"));
            }
        }

        // Character Info readers
        private static string ReadName(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return "N/A";
                string name = data.Name;
                return !string.IsNullOrWhiteSpace(name) ? string.Format(T("Name: {0}"), name) : T("N/A");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading name: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadJobName(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return T("N/A");

                // Use the existing helper from StatusMenuState
                string jobName = Patches.StatusMenuState.GetCurrentJobName(data);
                if (!string.IsNullOrWhiteSpace(jobName))
                {
                    return string.Format(T("Job: {0}"), jobName);
                }

                return string.Format(T("Job: {0}"), $"ID {data.JobId}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading job name: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadCharacterLevel(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Level: {0}"), data.Parameter.ConfirmedLevel());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading character level: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadExperience(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return T("N/A");
                int currentExp = data.CurrentExp;
                return string.Format(T("Experience: {0}"), currentExp);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Experience: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadNextLevel(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return T("N/A");
                int nextExp = data.GetNextExp();
                return string.Format(T("Next Level: {0}"), nextExp);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Next Level: {ex.Message}");
                return T("N/A");
            }
        }

        // Vitals readers
        private static string ReadHP(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                int current = data.Parameter.currentHP;
                int max = data.Parameter.ConfirmedMaxHp();
                return string.Format(T("HP: {0} / {1}"), current, max);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading HP: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadMPLevelN(OwnedCharacterData data, int level)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");

                var currentCharges = data.Parameter.CurrentMpCountList;
                int current = 0;
                if (currentCharges != null && currentCharges.ContainsKey(level))
                {
                    current = currentCharges[level];
                }
                return string.Format(T("MP LV{0}: {1}"), level, current);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading MP level {level}: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadMPLevel1(OwnedCharacterData data) => ReadMPLevelN(data, 1);
        private static string ReadMPLevel2(OwnedCharacterData data) => ReadMPLevelN(data, 2);
        private static string ReadMPLevel3(OwnedCharacterData data) => ReadMPLevelN(data, 3);
        private static string ReadMPLevel4(OwnedCharacterData data) => ReadMPLevelN(data, 4);
        private static string ReadMPLevel5(OwnedCharacterData data) => ReadMPLevelN(data, 5);
        private static string ReadMPLevel6(OwnedCharacterData data) => ReadMPLevelN(data, 6);
        private static string ReadMPLevel7(OwnedCharacterData data) => ReadMPLevelN(data, 7);
        private static string ReadMPLevel8(OwnedCharacterData data) => ReadMPLevelN(data, 8);

        private static string ReadJobLevel(OwnedCharacterData data)
        {
            try
            {
                if (data == null) return T("N/A");

                // Use BattleUtility.GetJobLevel - the game's own calculation method
                int jobLevel = BattleUtility.GetJobLevel(data);

                if (jobLevel > 0)
                {
                    return string.Format(T("Job Level: {0}"), jobLevel);
                }

                // Fallback to data accessor
                var ownedJob = data.OwnedJob;
                if (ownedJob != null)
                {
                    return string.Format(T("Job Level: {0}"), ownedJob.Level > 0 ? ownedJob.Level : 1);
                }

                return string.Format(T("Job Level: {0}"), T("N/A"));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Job Level: {ex.Message}");
                return T("N/A");
            }
        }

        // Attribute readers
        private static string ReadStrength(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Strength: {0}"), data.Parameter.ConfirmedPower());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Strength: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadAgility(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Agility: {0}"), data.Parameter.ConfirmedAgility());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Agility: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadStamina(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Stamina: {0}"), data.Parameter.ConfirmedVitality());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Stamina: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadIntellect(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Intellect: {0}"), data.Parameter.ConfirmedIntelligence());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Intellect: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadSpirit(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Spirit: {0}"), data.Parameter.ConfirmedSpirit());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Spirit: {ex.Message}");
                return T("N/A");
            }
        }

        // Combat stat readers

        /// <summary>
        /// Try to read attack count from the UI view.
        /// Returns -1 if unable to read from UI.
        /// </summary>
        private static int TryReadAttackCountFromUI()
        {
            try
            {
                // Find all ParameterContentController objects in scene
                var controllers = UnityEngine.Object.FindObjectsOfType<Il2CppLast.UI.KeyInput.ParameterContentController>();
                if (controllers == null || controllers.Length == 0)
                    return -1;

                foreach (var controller in controllers)
                {
                    if (controller == null) continue;

                    // Check if this is the Attack parameter (type == 10)
                    IntPtr controllerPtr = controller.Pointer;
                    if (controllerPtr == IntPtr.Zero) continue;

                    int paramType;
                    unsafe
                    {
                        paramType = *(int*)((byte*)controllerPtr + IL2CppOffsets.StatusDetails.OFFSET_PARAM_TYPE);
                    }

                    if (paramType != IL2CppOffsets.StatusDetails.PARAMETER_TYPE_ATTACK)
                        continue;

                    // Found Attack parameter - read the view at offset 0x20
                    IntPtr viewPtr;
                    unsafe
                    {
                        viewPtr = *(IntPtr*)((byte*)controllerPtr + IL2CppOffsets.StatusDetails.OFFSET_PARAM_VIEW);
                    }
                    if (viewPtr == IntPtr.Zero)
                        return -1;

                    // Read multipliedValueText at offset 0x28
                    IntPtr textPtr;
                    unsafe
                    {
                        textPtr = *(IntPtr*)((byte*)viewPtr + IL2CppOffsets.StatusDetails.OFFSET_MULTIPLIED_VALUE_TEXT);
                    }
                    if (textPtr == IntPtr.Zero)
                        return -1;

                    // Cast to Text component and read value
                    var textComponent = new UnityEngine.UI.Text(textPtr);
                    if (textComponent == null)
                        return -1;

                    string textValue = textComponent.text;
                    if (string.IsNullOrEmpty(textValue))
                        return -1;

                    // Parse the attack count from the text
                    if (int.TryParse(textValue.Trim(), out int attackCount))
                    {
                        return attackCount;
                    }

                    return -1;
                }

                return -1;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading attack count from UI: {ex.Message}");
                return -1;
            }
        }

        private static string ReadAttack(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");

                int attackPower = data.Parameter.ConfirmedAttack();

                // Try to read attack count from UI first
                int attackCount = TryReadAttackCountFromUI();

                // Fallback to simple calculation if UI read fails
                if (attackCount <= 0)
                {
                    // Simple fallback: 1 attack (most common case)
                    // The accurate count requires weapon skill levels which we can't easily access
                    attackCount = 1;
                }

                return string.Format(T("Attack: {0} x {1}"), attackCount, attackPower);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Attack: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadAccuracy(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                int accuracy = data.Parameter.ConfirmedAccuracyRate(false);
                return string.Format(T("Accuracy: {0}%"), accuracy);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Accuracy: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Defense: {0}"), data.Parameter.ConfirmedDefense());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Defense: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                int evasion = data.Parameter.ConfirmedEvasionRate(false);
                return string.Format(T("Evasion: {0}%"), evasion);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Evasion: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadMagicDefense(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                return string.Format(T("Magic Defense: {0}"), data.Parameter.ConfirmedAbilityDefense());
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Defense: {ex.Message}");
                return T("N/A");
            }
        }

        private static string ReadMagicEvasion(OwnedCharacterData data)
        {
            try
            {
                if (data?.Parameter == null) return T("N/A");
                int magicEvasion = data.Parameter.ConfirmedAbilityEvasionRate(false);
                return string.Format(T("Magic Evasion: {0}%"), magicEvasion);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error reading Magic Evasion: {ex.Message}");
                return T("N/A");
            }
        }
    }
}
