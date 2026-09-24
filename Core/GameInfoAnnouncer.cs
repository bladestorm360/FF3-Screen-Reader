using System;
using MelonLoader;
using FFIII_ScreenReader.Patches;
using FFIII_ScreenReader.Utils;
using static FFIII_ScreenReader.Utils.ModTextTranslator;
using UserDataManager = Il2CppLast.Management.UserDataManager;

namespace FFIII_ScreenReader.Core
{
    /// <summary>
    /// Announces game information: Gil amount, current map, character status.
    /// Extracted from FFIII_ScreenReaderMod to reduce file size.
    /// </summary>
    internal static class GameInfoAnnouncer
    {
        public static void AnnounceGilAmount()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager != null)
                {
                    int gil = userDataManager.OwendGil;
                    FFIII_ScreenReaderMod.SpeakText(string.Format(T("{0} Gil"), gil));
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting gil: {ex.Message}");
            }
            FFIII_ScreenReaderMod.SpeakText(T("Gil not available"));
        }

        public static void AnnounceCurrentMap()
        {
            try
            {
                string mapName = Field.MapNameResolver.GetCurrentMapName();
                if (!string.IsNullOrEmpty(mapName) && mapName != "Unknown")
                {
                    FFIII_ScreenReaderMod.SpeakText(mapName);
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting map name: {ex.Message}");
            }
            FFIII_ScreenReaderMod.SpeakText(T("Map name not available"));
        }

        /// <summary>
        /// H key / battle mod-mode X: HP and status effects of the character whose command turn is
        /// active. Battle only. (FF3 magic uses per-level spell charges, which the magic menus read.)
        /// </summary>
        public static void AnnounceCharacterStatus()
        {
            try
            {
                if (!BattleStateHelper.IsInBattle)
                {
                    FFIII_ScreenReaderMod.SpeakText(T("Party status only available in battle"));
                    return;
                }

                var charData = BattleCommandState.CurrentActor;
                if (charData == null)
                {
                    FFIII_ScreenReaderMod.SpeakText(T("No active character"));
                    return;
                }

                var param = charData.Parameter;
                if (param == null)
                {
                    FFIII_ScreenReaderMod.SpeakText(T("Character status not available"));
                    return;
                }

                string line = string.Format(T("{0}: HP {1}/{2}"), charData.Name, param.CurrentHP, param.ConfirmedMaxHp());

                string conditions = CharacterStatusHelper.GetStatusConditions(param);
                if (!string.IsNullOrEmpty(conditions))
                    line += ", " + conditions;

                FFIII_ScreenReaderMod.SpeakText(line);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character status: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText(T("Character status not available"));
            }
        }
    }
}
