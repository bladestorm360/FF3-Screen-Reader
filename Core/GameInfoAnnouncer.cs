using System;
using MelonLoader;
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
                    FFIII_ScreenReaderMod.SpeakText($"{gil} Gil");
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting gil: {ex.Message}");
            }
            FFIII_ScreenReaderMod.SpeakText("Gil not available");
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
            FFIII_ScreenReaderMod.SpeakText("Map name not available");
        }

        public static void AnnounceCharacterStatus()
        {
            try
            {
                var userDataManager = UserDataManager.Instance();
                if (userDataManager == null)
                {
                    FFIII_ScreenReaderMod.SpeakText("Character data not available");
                    return;
                }

                var partyList = userDataManager.GetOwnedCharactersClone(false);
                if (partyList == null || partyList.Count == 0)
                {
                    FFIII_ScreenReaderMod.SpeakText("No party members");
                    return;
                }

                var sb = new System.Text.StringBuilder();
                foreach (var charData in partyList)
                {
                    try
                    {
                        if (charData != null)
                        {
                            string name = charData.Name;
                            var param = charData.Parameter;
                            if (param != null)
                            {
                                int currentHp = param.CurrentHP;
                                int maxHp = param.ConfirmedMaxHp();
                                int currentMp = param.CurrentMP;
                                int maxMp = param.ConfirmedMaxMp();

                                sb.AppendLine($"{name}: HP {currentHp}/{maxHp}, MP {currentMp}/{maxMp}");
                            }
                        }
                    }
                    catch { }
                }

                string status = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(status))
                    FFIII_ScreenReaderMod.SpeakText(status);
                else
                    FFIII_ScreenReaderMod.SpeakText("No character status available");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Error getting character status: {ex.Message}");
                FFIII_ScreenReaderMod.SpeakText("Character status not available");
            }
        }
    }
}
